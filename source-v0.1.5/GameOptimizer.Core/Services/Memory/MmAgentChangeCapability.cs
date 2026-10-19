using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Memory;

public sealed record MmAgentChangeCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    bool TargetMemoryCompressionEnabled = false,
    ChangeRisk Risk = ChangeRisk.MediumRebootOrExperience,
    bool RequiresRestart = true);

public sealed class MmAgentChangeCapability : IChangeCapability
{
    private readonly MmAgentChangeCapabilityDefinition _definition;
    private readonly IMmAgentAccessor _accessor;
    private readonly TimeProvider _timeProvider;

    public MmAgentChangeCapability(
        MmAgentChangeCapabilityDefinition definition,
        IMmAgentAccessor accessor,
        TimeProvider? timeProvider = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _timeProvider = timeProvider ?? TimeProvider.System;
        ValidateDefinition(definition);
        Descriptor = new CapabilityDescriptor(
            definition.CapabilityId,
            definition.DefinitionVersion,
            definition.Risk,
            [TargetScopeKind.Machine],
            RequiresAdministrator: true,
            RequiresExplicitConfirmation: true,
            RequiresRecoveryMaterial: true,
            definition.RequiresRestart,
            "mmagent:memory-compression");
    }

    public CapabilityDescriptor Descriptor { get; }

    public async Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable, false, "scope.invalid", "内存压缩能力只能应用到当前计算机作用域。");
        }

        var assessment = await _accessor.ProbeAsync(cancellationToken).ConfigureAwait(false);
        return new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            assessment.HasRequiredPermission,
            assessment.ReasonCode,
            assessment.Details);
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        var snapshot = await _accessor.ReadAsync(cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(snapshot);
        return new StateObservation(
            scope, payload, Fingerprint(payload), _timeProvider.GetUtcNow(),
            "MMAgent PowerShell cmdlet", "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = DeserializeSnapshot(current.Payload);
        if (!string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("内存压缩能力只接受固定目标 \"apply\"。");

        var target = before with { MemoryCompressionEnabled = _definition.TargetMemoryCompressionEnabled };
        var canonicalTarget = JsonSerializer.Serialize(target);
        return Task.FromResult(new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            canonicalTarget,
            Fingerprint(canonicalTarget),
            Descriptor.ResourceKey));
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        ValidateFrozenChange(change);
        var before = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
            return new PlatformExecutionResult(false, false, "state.drift", "内存压缩状态在写入前发生变化，已拒绝旧计划。");

        var targetSnapshot = DeserializeSnapshot(change.TargetPayload);
        var mutation = await _accessor
            .ApplyAsync(targetSnapshot.MemoryCompressionEnabled, cancellationToken)
            .ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "execution.verified", "内存压缩状态已变更并回读确认。")
            : new PlatformExecutionResult(false, true, "verification.mismatch", "内存压缩写入后的状态与冻结目标不一致。");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("恢复材料与内存压缩能力身份不一致。");
        ValidateScope(material.Scope);
        var original = DeserializeSnapshot(material.BeforePayload);
        var mutation = await _accessor.RestoreAsync(original, cancellationToken).ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "restore.verified", "原内存压缩状态已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的内存压缩状态与原快照不一致。");
    }

    private void ValidateFrozenChange(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("冻结计划与内存压缩能力定义不一致。");
        if (!string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("冻结内存压缩目标与其指纹不一致。");
    }

    private static MmAgentStateSnapshot DeserializeSnapshot(string payload) =>
        JsonSerializer.Deserialize<MmAgentStateSnapshot>(payload)
        ?? throw new InvalidDataException("内存压缩状态快照无法解析。");

    private static void ValidateScope(TargetScope scope)
    {
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("内存压缩能力只能使用当前计算机作用域。");
    }

    private static void ValidateDefinition(MmAgentChangeCapabilityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
