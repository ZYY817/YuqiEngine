using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.ScheduledTasks;

public sealed record ScheduledTaskChangeCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string TaskPath,
    bool TargetEnabled = false,
    ChangeRisk Risk = ChangeRisk.MediumRebootOrExperience,
    bool RequiresRestart = false);

public sealed class ScheduledTaskChangeCapability : IChangeCapability
{
    private readonly ScheduledTaskChangeCapabilityDefinition _definition;
    private readonly IScheduledTaskAccessor _accessor;
    private readonly TimeProvider _timeProvider;

    public ScheduledTaskChangeCapability(
        ScheduledTaskChangeCapabilityDefinition definition,
        IScheduledTaskAccessor accessor,
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
            $"task:{definition.TaskPath}");
    }

    public CapabilityDescriptor Descriptor { get; }

    public async Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable, false, "scope.invalid", "计划任务能力只能应用到当前计算机作用域。");
        }

        var assessment = await _accessor.ProbeAsync(_definition.TaskPath, cancellationToken).ConfigureAwait(false);
        var applicability = assessment switch
        {
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
        var snapshot = await _accessor.ReadAsync(_definition.TaskPath, cancellationToken).ConfigureAwait(false);
        ValidateStableSnapshot(snapshot);
        var payload = JsonSerializer.Serialize(snapshot);
        // Why: 指纹只覆盖语义字段 {TaskPath, Exists, Enabled}。平台 /CHANGE
        // 会重写任务 XML 并把 State 从 Ready 翻成 Disabled，全负载指纹在
        // 合法的写入后永远无法匹配冻结目标，导致应用与恢复被错误判定为漂移。
        return new StateObservation(
            scope, payload, SemanticFingerprint(snapshot), _timeProvider.GetUtcNow(),
            "Task Scheduler", "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = DeserializeSnapshot(current.Payload);
        ValidateStableSnapshot(before);
        if (!string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("计划任务能力只接受固定目标 \"apply\"。");

        var target = before with { Enabled = _definition.TargetEnabled };
        var canonicalTarget = JsonSerializer.Serialize(target);
        return Task.FromResult(new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            canonicalTarget,
            SemanticFingerprint(target),
            Descriptor.ResourceKey));
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        ValidateFrozenChange(change);
        var before = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
            return new PlatformExecutionResult(false, false, "state.drift", "计划任务状态在写入前发生变化，已拒绝旧计划。");

        var mutation = await _accessor.ApplyAsync(_definition.TaskPath, _definition.TargetEnabled, cancellationToken).ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        var afterSnapshot = DeserializeSnapshot(after.Payload);
        // Why: 指纹涵盖任务 XML 与 State，而 /CHANGE 会同时改写二者，
        // 全负载哈希比对恒为 mismatch；验证只比对语义字段。
        return afterSnapshot.Exists && afterSnapshot.Enabled == _definition.TargetEnabled
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "execution.verified", "计划任务状态已变更并回读确认。")
            : new PlatformExecutionResult(false, true, "verification.mismatch", "计划任务写入后的状态与冻结目标不一致。");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("恢复材料与计划任务能力身份不一致。");
        ValidateScope(material.Scope);
        var original = DeserializeSnapshot(material.BeforePayload);
        ValidateStableSnapshot(original);
        var mutation = await _accessor.RestoreAsync(original, cancellationToken).ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        var afterSnapshot = DeserializeSnapshot(after.Payload);
        return afterSnapshot.Exists == original.Exists && afterSnapshot.Enabled == original.Enabled
            ? new PlatformExecutionResult(true, mutation.MutationOccurred, "restore.verified", "原计划任务状态已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的计划任务状态与原快照不一致。");
    }

    private void ValidateFrozenChange(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("冻结计划与计划任务能力定义不一致。");
        if (!string.Equals(SemanticFingerprint(DeserializeSnapshot(change.TargetPayload)), change.TargetFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("冻结计划任务目标与其指纹不一致。");
    }

    private static ScheduledTaskStateSnapshot DeserializeSnapshot(string payload) =>
        JsonSerializer.Deserialize<ScheduledTaskStateSnapshot>(payload)
        ?? throw new InvalidDataException("计划任务状态快照无法解析。");

    private void ValidateStableSnapshot(ScheduledTaskStateSnapshot snapshot)
    {
        if (!string.Equals(snapshot.TaskPath, _definition.TaskPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("计划任务快照与能力固定任务路径不一致。");
        if (!snapshot.Exists)
            throw new InvalidDataException("计划任务不存在，不能创建可恢复计划。");
        if (snapshot.State is "Unknown" or "Pending")
            throw new InvalidDataException("计划任务状态不稳定或无法识别，已阻断变更。");
    }

    private static void ValidateScope(TargetScope scope)
    {
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("计划任务能力只能使用当前计算机作用域。");
    }

    private static void ValidateDefinition(ScheduledTaskChangeCapabilityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.TaskPath);
        if (!definition.TaskPath.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("仅允许迁移 \\Microsoft\\ 系统计划任务的显式路径。", nameof(definition));
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static string SemanticFingerprint(ScheduledTaskStateSnapshot snapshot) =>
        Fingerprint(JsonSerializer.Serialize(new { snapshot.TaskPath, snapshot.Exists, snapshot.Enabled }));
}
