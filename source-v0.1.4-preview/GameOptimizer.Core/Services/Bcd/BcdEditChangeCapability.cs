using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Bcd;

public sealed record BcdEditChangeCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string ElementName,
    string? TargetValue,
    string? TargetDisplay = null,
    ChangeRisk Risk = ChangeRisk.HighSystem,
    bool RequiresRestart = true);

public static class BcdEditCapabilityDefinitions
{
    public static IReadOnlyList<BcdEditChangeCapabilityDefinition> All { get; } =
    [
        new(
            "cpu.disable_dynamic_tick",
            "1.0.0",
            "disabledynamictick",
            "Yes",
            "Yes"),
        new(
            "cpu.configure_hpet",
            "1.0.0",
            "useplatformclock",
            null,
            "未绑定 HPET"),
        new(
            "cpu.use_platform_tick",
            "1.0.0",
            "useplatformtick",
            "Yes",
            "Yes"),
        new(
            "cpu.disable_tsc_sync",
            "1.0.0",
            "tscsyncpolicy",
            "Legacy",
            "Legacy")
    ];
}

public sealed class BcdEditChangeCapability : IChangeCapability
{
    private readonly BcdEditChangeCapabilityDefinition _definition;
    private readonly IBcdEditAccessor _accessor;
    private readonly TimeProvider _timeProvider;

    public BcdEditChangeCapability(
        BcdEditChangeCapabilityDefinition definition,
        IBcdEditAccessor accessor,
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
            $"bcd:{definition.ElementName.ToLowerInvariant()}");
    }

    public CapabilityDescriptor Descriptor { get; }

    public async Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable, false, "scope.invalid", "BCD 能力只能应用到当前计算机作用域。");
        }

        var assessment = await _accessor.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var applicability = assessment switch
        {
            { OsSupported: false } => CapabilityApplicability.Unsupported,
            { SecureBootEnabled: true } => CapabilityApplicability.Unsupported,
            _ => CapabilityApplicability.Applicable
        };
        return new CapabilityAssessment(
            applicability,
            assessment.HasRequiredPermission,
            assessment.ReasonCode,
            assessment.Details);
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        var snapshot = await _accessor.ReadAsync(_definition.ElementName, cancellationToken).ConfigureAwait(false);
        ValidateReadableSnapshot(snapshot);
        var payload = JsonSerializer.Serialize(snapshot);
        return new StateObservation(
            scope, payload, Fingerprint(payload), _timeProvider.GetUtcNow(),
            "bcdedit boot configuration", "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = DeserializeSnapshot(current.Payload);
        ValidateReadableSnapshot(before);
        if (!string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("BCD 能力只接受固定目标 \"apply\"。");

        var target = before with { ElementValue = _definition.TargetValue };
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
            return new PlatformExecutionResult(false, false, "state.drift", "BCD 状态在写入前发生变化，已拒绝旧计划。");

        var targetSnapshot = DeserializeSnapshot(change.TargetPayload);
        var mutation = await _accessor
            .ApplyAsync(_definition.ElementName, targetSnapshot.ElementValue, cancellationToken)
            .ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "execution.verified", "BCD 元素已变更并回读确认。")
            : new PlatformExecutionResult(false, true, "verification.mismatch", "BCD 写入后的状态与冻结目标不一致。");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("恢复材料与 BCD 能力身份不一致。");
        ValidateScope(material.Scope);
        var original = DeserializeSnapshot(material.BeforePayload);
        ValidateReadableSnapshot(original);
        var mutation = await _accessor.RestoreAsync(_definition.ElementName, original, cancellationToken).ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "restore.verified", "原 BCD 值已精确恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的 BCD 状态与原快照不一致。");
    }

    private void ValidateFrozenChange(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("冻结计划与 BCD 能力定义不一致。");
        if (!string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("冻结 BCD 目标与其指纹不一致。");
    }

    private static BcdStateSnapshot DeserializeSnapshot(string payload) =>
        JsonSerializer.Deserialize<BcdStateSnapshot>(payload)
        ?? throw new InvalidDataException("BCD 状态快照无法解析。");

    private void ValidateReadableSnapshot(BcdStateSnapshot snapshot)
    {
        if (!snapshot.ReadSucceeded)
            throw new InvalidDataException("BCD 启动配置读取失败或元素无法识别，已阻断变更。");
    }

    private static void ValidateScope(TargetScope scope)
    {
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("BCD 能力只能使用当前计算机作用域。");
    }

    private static void ValidateDefinition(BcdEditChangeCapabilityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ElementName);
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
