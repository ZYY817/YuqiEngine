using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.SystemServices;

public sealed record ServiceChangeCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string ServiceName,
    ServiceChangeAction TargetAction,
    WindowsServiceStartType? TargetStartType = null,
    ChangeRisk Risk = ChangeRisk.HighSystem,
    bool RequiresRestart = false);

public sealed class ServiceChangeCapability : IChangeCapability
{
    private readonly ServiceChangeCapabilityDefinition _definition;
    private readonly IWindowsServiceAccessor _accessor;
    private readonly TimeProvider _timeProvider;

    public ServiceChangeCapability(
        ServiceChangeCapabilityDefinition definition,
        IWindowsServiceAccessor accessor,
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
            $"service:{definition.ServiceName.ToLowerInvariant()}");
    }

    public CapabilityDescriptor Descriptor { get; }

    public async Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "scope.invalid",
                "服务能力只能应用到当前计算机作用域。");
        }

        var assessment = await _accessor.ProbeAsync(_definition.ServiceName, cancellationToken).ConfigureAwait(false);
        var applicability = assessment switch
        {
            { ProtectedService: true } => CapabilityApplicability.Unsupported,
            { ManagedByPolicy: true } => CapabilityApplicability.ManagedByPolicy,
            { Exists: false } => CapabilityApplicability.NotApplicable,
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
        var snapshot = await _accessor.ReadAsync(_definition.ServiceName, cancellationToken).ConfigureAwait(false);
        ValidateStableSnapshot(snapshot);
        var payload = JsonSerializer.Serialize(snapshot);
        return new StateObservation(
            scope,
            payload,
            Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            "Windows Service Control Manager and registry",
            "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = DeserializeSnapshot(current.Payload);
        ValidateStableSnapshot(before);
        var expectedStartType = _definition.TargetStartType ?? before.StartType;
        var requested = string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase)
            ? new ServiceChangeTarget(_definition.TargetAction, expectedStartType)
            : JsonSerializer.Deserialize<ServiceChangeTarget>(targetPayload)
              ?? throw new InvalidDataException("服务目标无法解析。");
        if (requested.Action != _definition.TargetAction || requested.StartType != expectedStartType)
            throw new InvalidDataException("调用方目标与固定服务能力定义不一致。");

        var target = before with
        {
            RunState = requested.Action == ServiceChangeAction.EnsureRunning
                ? WindowsServiceRunState.Running
                : WindowsServiceRunState.Stopped,
            StartType = expectedStartType
        };
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
            return new PlatformExecutionResult(false, false, "state.drift", "服务状态在写入前发生变化，已拒绝旧计划。");

        var targetSnapshot = DeserializeSnapshot(change.TargetPayload);
        var target = new ServiceChangeTarget(
            targetSnapshot.RunState == WindowsServiceRunState.Running
                ? ServiceChangeAction.EnsureRunning
                : ServiceChangeAction.EnsureStopped,
            targetSnapshot.StartType);
        var mutation = await _accessor.ApplyAsync(_definition.ServiceName, target, cancellationToken).ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "execution.verified", "服务状态已变更并回读确认。")
            : new PlatformExecutionResult(false, true, "verification.mismatch", "服务写入后的状态与冻结目标不一致。");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("恢复材料与服务能力身份不一致。");
        ValidateScope(material.Scope);
        var original = DeserializeSnapshot(material.BeforePayload);
        ValidateStableSnapshot(original);
        var mutation = await _accessor.RestoreAsync(original, cancellationToken).ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "restore.verified", "原服务状态已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的服务状态与原快照不一致。");
    }

    private void ValidateFrozenChange(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("冻结计划与服务能力定义不一致。");
        if (!string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("冻结服务目标与其指纹不一致。");
    }

    private static ServiceStateSnapshot DeserializeSnapshot(string payload) =>
        JsonSerializer.Deserialize<ServiceStateSnapshot>(payload)
        ?? throw new InvalidDataException("服务状态快照无法解析。");

    private void ValidateStableSnapshot(ServiceStateSnapshot snapshot)
    {
        if (!string.Equals(snapshot.ServiceName, _definition.ServiceName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("服务快照与能力固定服务不一致。");
        if (!snapshot.Exists)
            throw new InvalidDataException("服务不存在，不能创建可恢复计划。");
        if (snapshot.StartType == WindowsServiceStartType.Unknown
            || snapshot.RunState is WindowsServiceRunState.Unknown
                or WindowsServiceRunState.StartPending
                or WindowsServiceRunState.StopPending
                or WindowsServiceRunState.Paused)
            throw new InvalidDataException("服务状态不稳定或无法识别，已阻断变更。");
    }

    private static void ValidateScope(TargetScope scope)
    {
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("服务能力只能使用当前计算机作用域。");
    }

    private static void ValidateDefinition(ServiceChangeCapabilityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ServiceName);
        if (definition.TargetAction == ServiceChangeAction.EnsureRunning
            && definition.TargetStartType == WindowsServiceStartType.Disabled)
            throw new ArgumentException(
                "EnsureRunning 不能把固定目标启动类型设为 Disabled。",
                nameof(definition));
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
