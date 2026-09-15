using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.DeviceRepair;

public sealed record DeviceStateChangeCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string DeviceNamePattern,
    bool TargetEnabled = false,
    ChangeRisk Risk = ChangeRisk.HighSystem,
    bool RequiresRestart = false);

public static class DeviceStateCapabilityDefinitions
{
    public static IReadOnlyList<DeviceStateChangeCapabilityDefinition> All { get; } =
    [
        new(
            "device.disable_system_timer",
            "1.0.0",
            "System timer"),
        new(
            "device.disable_umbus",
            "1.0.0",
            "UMBus Root Bus Enumerator"),
        new(
            "device.disable_composite_bus",
            "1.0.0",
            "Composite Bus Enumerator"),
        new(
            "device.disable_hpet_device",
            "1.0.0",
            "High precision event timer")
    ];
}

public sealed class DeviceStateChangeCapability : IChangeCapability
{
    private readonly DeviceStateChangeCapabilityDefinition _definition;
    private readonly IDeviceStateAccessor _accessor;
    private readonly TimeProvider _timeProvider;

    public DeviceStateChangeCapability(
        DeviceStateChangeCapabilityDefinition definition,
        IDeviceStateAccessor accessor,
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
            $"device:{definition.DeviceNamePattern.ToLowerInvariant()}");
    }

    public CapabilityDescriptor Descriptor { get; }

    public async Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable, false, "scope.invalid", "设备状态能力只能应用到当前计算机作用域。");
        }

        var assessment = await _accessor.ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (!assessment.HasRequiredPermission && assessment.ReasonCode == "permission.administrator-required")
        {
            // 设备存在性在无权限时也无法可靠判定，保持 Applicable=false 由权限门禁阻断。
            return new CapabilityAssessment(
                CapabilityApplicability.Applicable,
                false,
                assessment.ReasonCode,
                assessment.Details);
        }

        var snapshot = await _accessor.ReadAsync(_definition.DeviceNamePattern, cancellationToken).ConfigureAwait(false);
        return snapshot.Found
            ? new CapabilityAssessment(
                CapabilityApplicability.Applicable,
                assessment.HasRequiredPermission,
                assessment.ReasonCode,
                assessment.Details)
            : new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                assessment.HasRequiredPermission,
                "device.not-found",
                "当前系统不存在该设备，无需变更。");
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        var snapshot = await _accessor.ReadAsync(_definition.DeviceNamePattern, cancellationToken).ConfigureAwait(false);
        ValidateFoundSnapshot(snapshot);
        var payload = JsonSerializer.Serialize(snapshot);
        return new StateObservation(
            scope, payload, Fingerprint(payload), _timeProvider.GetUtcNow(),
            "Win32_PnPEntity + pnputil", "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = DeserializeSnapshot(current.Payload);
        ValidateFoundSnapshot(before);
        if (!string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("设备状态能力只接受固定目标 \"apply\"。");

        var target = before with { IsEnabled = _definition.TargetEnabled };
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
            return new PlatformExecutionResult(false, false, "state.drift", "设备状态在写入前发生变化，已拒绝旧计划。");

        var targetSnapshot = DeserializeSnapshot(change.TargetPayload);
        var mutation = await _accessor
            .ApplyAsync(targetSnapshot.DeviceId, targetSnapshot.IsEnabled, cancellationToken)
            .ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "execution.verified", "设备状态已变更并回读确认。")
            : new PlatformExecutionResult(false, true, "verification.mismatch", "设备写入后的状态与冻结目标不一致。");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("恢复材料与设备状态能力身份不一致。");
        ValidateScope(material.Scope);
        var original = DeserializeSnapshot(material.BeforePayload);
        ValidateFoundSnapshot(original);
        var mutation = await _accessor.RestoreAsync(original, cancellationToken).ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "restore.verified", "原设备状态已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的设备状态与原快照不一致。");
    }

    private void ValidateFrozenChange(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("冻结计划与设备状态能力定义不一致。");
        if (!string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("冻结设备目标与其指纹不一致。");
    }

    private static DeviceStateSnapshot DeserializeSnapshot(string payload) =>
        JsonSerializer.Deserialize<DeviceStateSnapshot>(payload)
        ?? throw new InvalidDataException("设备状态快照无法解析。");

    private void ValidateFoundSnapshot(DeviceStateSnapshot snapshot)
    {
        if (!snapshot.Found || string.IsNullOrWhiteSpace(snapshot.DeviceId))
            throw new InvalidDataException("设备不存在或身份缺失，不能创建可恢复计划。");
        if (!string.Equals(snapshot.Name, _definition.DeviceNamePattern, StringComparison.OrdinalIgnoreCase)
            && !snapshot.Name.Contains(_definition.DeviceNamePattern, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("设备快照与能力固定设备模式不一致，已阻断变更。");
    }

    private static void ValidateScope(TargetScope scope)
    {
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("设备状态能力只能使用当前计算机作用域。");
    }

    private static void ValidateDefinition(DeviceStateChangeCapabilityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DeviceNamePattern);
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
