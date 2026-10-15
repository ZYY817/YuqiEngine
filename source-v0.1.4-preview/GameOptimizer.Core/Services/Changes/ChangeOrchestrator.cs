using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Changes;

public sealed class ChangeOrchestrator : IChangeOrchestrator
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResourceLockTimeout = TimeSpan.FromSeconds(15);

    private readonly IReadOnlyDictionary<string, IChangeCapability> _capabilities;
    private readonly IRecoveryRepository _recoveryRepository;
    private readonly IChangeJournal _journal;
    private readonly IChangeAuditStore _auditStore;
    private readonly TimeProvider _timeProvider;
    private readonly IChangePlanStore _planStore;
    private readonly IChangeResourceLockProvider _resourceLockProvider;
    private readonly IRecoveryClaimStore _recoveryClaimStore;
    private readonly ChangeExecutionPolicy _executionPolicy;
    private readonly IChangeConfirmationAuthority? _confirmationAuthority;

    private readonly IChangeExecutionGate _executionGate;
    private readonly IChangeCompatibilityPolicy? _compatibilityPolicy;
    private readonly IRecoveryIndexQuery? _recoveryIndexQuery;

    public ChangeOrchestrator(
        IEnumerable<IChangeCapability> capabilities,
        IRecoveryRepository recoveryRepository,
        IChangeJournal journal,
        IChangeAuditStore auditStore,
        TimeProvider? timeProvider = null,
        IChangePlanStore? planStore = null,
        IChangeResourceLockProvider? resourceLockProvider = null,
        IChangeConfirmationAuthority? confirmationAuthority = null,
        IRecoveryClaimStore? recoveryClaimStore = null,
        ChangeExecutionPolicy? executionPolicy = null,
        IChangeExecutionGate? executionGate = null,
        IChangeCompatibilityPolicy? compatibilityPolicy = null,
        IRecoveryIndexQuery? recoveryIndexQuery = null)
    {
        _capabilities = capabilities.ToDictionary(
            capability => capability.Descriptor.CapabilityId,
            StringComparer.OrdinalIgnoreCase);
        _recoveryRepository = recoveryRepository;
        _journal = journal;
        _auditStore = auditStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _planStore = planStore ?? new InMemoryChangePlanStore();
        _resourceLockProvider = resourceLockProvider ?? new InProcessChangeResourceLockProvider();
        _confirmationAuthority = confirmationAuthority;
        _recoveryClaimStore = recoveryClaimStore ?? new FileRecoveryClaimStore();
        _executionPolicy = executionPolicy ?? ChangeExecutionPolicy.Default;
        _executionGate = executionGate ?? throw new ArgumentNullException(nameof(executionGate), "A change execution gate is mandatory for centralized writes.");
        _compatibilityPolicy = compatibilityPolicy;
        _recoveryIndexQuery = recoveryIndexQuery;
    }

    public async Task<StateObservation?> ObserveAsync(
        string capabilityId,
        TargetScope scope,
        CancellationToken cancellationToken)
    {
        if (!_capabilities.TryGetValue(capabilityId, out var capability))
        {
            return null;
        }

        return await capability.ReadAsync(scope, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="registerPlan">
    /// Why: 状态探测（GetStatusAsync）语义上应为纯读；传入 false 时跳过
    /// 计划注册与 journal 追加，避免每次 UI 刷新都产生计划/日志增长。
    /// </param>
    public async Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken, bool registerPlan = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_capabilities.TryGetValue(request.CapabilityId, out var capability))
        {
            return Blocked("capability.unknown", $"未知能力：{request.CapabilityId}");
        }

        var descriptor = capability.Descriptor;
        if (!descriptor.SupportedScopes.Contains(request.Scope.Kind))
        {
            return Blocked("scope.unsupported", "该功能不支持当前作用域。");
        }

        var assessment = await ProbeAsync(capability, request.Scope, ChangeIntent.Prepare, cancellationToken).ConfigureAwait(false);
        if (assessment.Applicability != CapabilityApplicability.Applicable)
        {
            return Blocked(assessment.ReasonCode, assessment.Details);
        }

        if (!assessment.HasRequiredPermission)
        {
            return Blocked("permission.denied", assessment.Details);
        }

        var current = await capability.ReadAsync(request.Scope, cancellationToken).ConfigureAwait(false);
        var change = await capability.PrepareAsync(current, request.TargetPayload, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(change.CapabilityId, descriptor.CapabilityId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(change.DefinitionVersion, descriptor.DefinitionVersion, StringComparison.Ordinal)
            || change.Scope != request.Scope
            || !string.Equals(change.ResourceKey, descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(change.BeforePayload, current.Payload, StringComparison.Ordinal)
            || !string.Equals(change.BeforeFingerprint, current.Fingerprint, StringComparison.Ordinal))
        {
            return Blocked("prepare.contract-mismatch", "准备阶段返回的能力、版本、作用域、资源或原状态与本次请求不一致。");
        }

        if (string.Equals(change.BeforeFingerprint, change.TargetFingerprint, StringComparison.Ordinal))
        {
            return new PreparationResult(
                ChangePreparationStatus.NoChange,
                null,
                "change.no-op",
                "当前状态已经符合目标。",
                current);
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(PlanLifetime);
        var planId = Guid.NewGuid();
        var planHash = ComputePlanHash(planId, change, now, expiresAt, descriptor, _executionPolicy.Fingerprint);
        var plan = new PreparedChangePlan(
            planId,
            change,
            planHash,
            now,
            expiresAt,
            descriptor.RequiresAdministrator,
            descriptor.RequiresExplicitConfirmation,
            descriptor.RequiresRecoveryMaterial,
            descriptor.Risk,
            _executionPolicy.Version,
            _executionPolicy.Fingerprint);
        if (registerPlan)
        {
            try
            {
                await _planStore.RegisterAsync(plan, cancellationToken).ConfigureAwait(false);
                await AppendJournalAsync(plan, "Prepared", "执行计划已签发并持久化。", cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Blocked("plan.register-cancelled", "计划持久化期间取消，未生成可执行计划。");
            }
            catch (Exception ex)
            {
                return Blocked("plan.register-failed", Sanitize(ex.Message));
            }
        }

        return new PreparationResult(ChangePreparationStatus.Ready, plan, "ready", "计划已准备，尚未执行写入。", current);
    }

    public async Task<BatchReceipt> ExecuteAsync(
        PreparedChangePlan plan,
        ConfirmationToken confirmation,
        CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        if (!_capabilities.TryGetValue(plan.Change.CapabilityId, out var capability))
        {
            return Wrap(CreateReceipt(plan, null, ChangeOutcome.Blocked, "capability.unknown", "能力未注册。", false, null, startedAt));
        }

        var validationError = ValidatePlan(plan, capability.Descriptor, confirmation);
        if (validationError is not null)
        {
            return Wrap(CreateReceipt(plan, null, validationError.Value.Outcome, validationError.Value.Code, validationError.Value.Message, false, null, startedAt));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Wrap(CreateReceipt(plan, null, ChangeOutcome.CancelledBeforeWrite, "cancelled.before-write", "操作在写入前取消。", false, null, startedAt));
        }

        IChangeResourceLease? resourceLease = null;
        try
        {
            try
            {
                resourceLease = await _resourceLockProvider.TryAcquireAsync(
                    plan.Change.LockKey ?? plan.Change.ResourceKey,
                    ResourceLockTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Wrap(CreateReceipt(plan, null, ChangeOutcome.CancelledBeforeWrite, "cancelled.waiting-lock", "等待资源锁时取消。", false, null, startedAt));
            }

            if (resourceLease is null)
            {
                return Wrap(CreateReceipt(plan, null, ChangeOutcome.Conflict, "resource.busy", "同一系统资源正在被其他操作修改。", false, null, startedAt));
            }

            PlanClaimStatus claimStatus;
            try
            {
                claimStatus = await _planStore.TryClaimAsync(plan.PlanId, plan.PlanHash, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Wrap(CreateReceipt(plan, null, ChangeOutcome.CancelledBeforeWrite, "cancelled.plan-claim", "领取执行计划期间取消。", false, null, startedAt));
            }
            catch (Exception ex)
            {
                return Wrap(CreateReceipt(plan, null, ChangeOutcome.Blocked, "plan.claim-failed", Sanitize(ex.Message), false, null, startedAt));
            }

            if (claimStatus != PlanClaimStatus.Claimed)
            {
                var code = claimStatus switch
                {
                    PlanClaimStatus.AlreadyClaimed => "plan.replayed",
                    PlanClaimStatus.HashMismatch => "plan.hash-mismatch",
                    _ => "plan.not-issued"
                };
                return Wrap(CreateReceipt(plan, null, ChangeOutcome.Blocked, code, "执行计划不存在、已执行或已失效。", false, null, startedAt));
            }

            try
            {
                await AppendJournalAsync(plan, "Claimed", "执行计划已被唯一领取。", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.Blocked, "journal.claim-failed", Sanitize(ex.Message), false, null, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            CapabilityAssessment assessment;
            try
            {
                assessment = await ProbeAsync(capability, plan.Change.Scope, ChangeIntent.Execute, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.CancelledBeforeWrite, "cancelled.permission-recheck", "执行前权限与适用性复查期间取消，原计划已失效。", false, null, startedAt), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.Blocked, "probe.recheck-failed", Sanitize(ex.Message), false, null, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            if (assessment.Applicability != CapabilityApplicability.Applicable)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.Blocked, assessment.ReasonCode, assessment.Details, false, null, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            if (!assessment.HasRequiredPermission)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.Denied, "permission.denied", assessment.Details, false, null, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            StateObservation current;
            try
            {
                current = await capability.ReadAsync(plan.Change.Scope, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.CancelledBeforeWrite, "cancelled.before-readback", "写入前回读期间取消，原计划已失效。", false, null, startedAt), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.FailedNoChange, "read.current-failed", Sanitize(ex.Message), false, null, startedAt), CancellationToken.None).ConfigureAwait(false);
            }
            if (string.Equals(current.Fingerprint, plan.Change.TargetFingerprint, StringComparison.Ordinal))
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.NoChange, "change.no-op", "目标状态已由其他操作达成。", false, current.Fingerprint, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            if (!string.Equals(current.Fingerprint, plan.Change.BeforeFingerprint, StringComparison.Ordinal))
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.Conflict, "state.drift", "系统状态在准备后发生变化，原计划已失效。", false, current.Fingerprint, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            if (capability.Descriptor.RequiresExplicitConfirmation
                && (_confirmationAuthority is null || !_confirmationAuthority.TryConsume(plan, confirmation)))
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.Denied, "confirmation.invalid-or-consumed", "显式确认凭据不存在、与计划不匹配或已经消费。", false, current.Fingerprint, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            ChangeExecutionGateDecision gateDecision;
            try
            {
                gateDecision = await _executionGate.EnsureCanExecuteAsync(
                    plan.Change.CapabilityId,
                    ChangeExecutionIntent.Apply,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.CancelledBeforeWrite, "cancelled.execution-gate", "执行门禁检查期间取消，未执行系统写入。", false, current.Fingerprint, startedAt), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.Blocked, "execution-gate.failed", Sanitize(ex.Message), false, current.Fingerprint, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            if (!gateDecision.IsAllowed)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, null, ChangeOutcome.Denied, "execution-gate.blocked", gateDecision.Reason, false, current.Fingerprint, startedAt), CancellationToken.None).ConfigureAwait(false);
            }

            var recovery = plan.RequiresRecoveryMaterial
                ? CreateRecoveryMaterial(plan, startedAt)
                : null;
            var recoveryReceiptId = recovery?.ReceiptId;
            if (plan.RequiresRecoveryMaterial)
            {
                try
                {
                    if (recovery is null)
                    {
                        throw new InvalidOperationException("Recovery material is required by the plan.");
                    }

                    await _recoveryRepository.SaveAsync(recovery, cancellationToken).ConfigureAwait(false);
                    var persisted = await _recoveryRepository.LoadAsync(recovery.RecoveryId, cancellationToken).ConfigureAwait(false);
                    if (persisted is null
                        || !VerifyRecoveryHash(persisted)
                        || persisted != recovery)
                    {
                        return await PersistReceiptAsync(CreateReceipt(plan, recovery?.RecoveryId, ChangeOutcome.Blocked, "recovery.verify-failed", "恢复材料保存后无法通过完整性回读。", false, current.Fingerprint, startedAt, recoveryReceiptId), CancellationToken.None).ConfigureAwait(false);
                    }

                    await _recoveryClaimStore.RegisterAvailableAsync(
                        recovery.RecoveryId,
                        recovery.IntegrityHash,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return await PersistReceiptAsync(CreateReceipt(plan, recovery?.RecoveryId, ChangeOutcome.CancelledBeforeWrite, "cancelled.recovery-persist", "恢复材料持久化期间取消，未执行系统写入。", false, current.Fingerprint, startedAt, recoveryReceiptId), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return await PersistReceiptAsync(CreateReceipt(plan, recovery?.RecoveryId, ChangeOutcome.Blocked, "recovery.persist-failed", Sanitize(ex.Message), false, current.Fingerprint, startedAt, recoveryReceiptId), CancellationToken.None).ConfigureAwait(false);
                }
            }

            try
            {
                await AppendJournalAsync(
                    plan,
                    plan.RequiresRecoveryMaterial ? "SnapshotVerified" : "PreconditionsVerified",
                    plan.RequiresRecoveryMaterial ? "恢复材料已持久化并回读。" : "执行前状态与安全条件已回读验证。",
                    cancellationToken).ConfigureAwait(false);
                await AppendJournalAsync(plan, "Executing", "平台执行开始。", cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, recovery?.RecoveryId, ChangeOutcome.CancelledBeforeWrite, "cancelled.journal-persist", "执行日志持久化期间取消，未执行系统写入。", false, current.Fingerprint, startedAt, recoveryReceiptId), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await PersistReceiptAsync(CreateReceipt(plan, recovery?.RecoveryId, ChangeOutcome.Blocked, "journal.persist-failed", Sanitize(ex.Message), false, current.Fingerprint, startedAt, recoveryReceiptId), CancellationToken.None).ConfigureAwait(false);
            }

            var platform = await RunPlatformAsync(
                token => capability.ExecuteAsync(plan.Change, token),
                cancellationToken).ConfigureAwait(false);
            var verification = await ReadForVerificationAsync(capability, plan.Change.Scope).ConfigureAwait(false);
            platform = platform with { CallerCancelled = platform.CallerCancelled || cancellationToken.IsCancellationRequested };
            var receipt = CreateExecutionTerminalReceipt(plan, recovery?.RecoveryId, platform, verification, startedAt, recoveryReceiptId);
            return await PersistReceiptAsync(receipt, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (resourceLease is not null)
            {
                await resourceLease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<BatchReceipt?> RestoreLatestByCapabilityAsync(
        string capabilityId,
        CancellationToken cancellationToken)
    {
        if (_recoveryIndexQuery is null || string.IsNullOrWhiteSpace(capabilityId))
        {
            return null;
        }

        var entries = await _recoveryIndexQuery.ListAsync(cancellationToken).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(candidate =>
            string.Equals(candidate.CapabilityId, capabilityId, StringComparison.OrdinalIgnoreCase)
            && candidate.Status == RecoveryIndexStatus.Recoverable
            && candidate.ReceiptId is not null);
        if (entry is null)
        {
            return null;
        }

        return await RestoreAsync(
            new RestoreRequest(entry.ReceiptId!.Value, entry.RecoveryId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        if (request.ReceiptId == Guid.Empty)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.receipt-id-invalid", "恢复请求必须提供非空 ReceiptId。", startedAt));
        }

        OperationReceipt? sourceReceipt;
        try
        {
            sourceReceipt = await _auditStore.LoadAsync(request.ReceiptId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.CancelledBeforeWrite, "restore.cancelled-audit-read", "读取恢复回执时取消，未执行恢复写入。", startedAt));
        }
        catch (Exception ex)
        {
            if (request.RecoveryId is null)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.audit-read-failed", Sanitize(ex.Message), startedAt));
            }

            // A failed terminal audit write/read must not destroy the independent recovery-id path.
            sourceReceipt = null;
        }

        if (sourceReceipt is not null && sourceReceipt.ReceiptId != request.ReceiptId)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.receipt-id-mismatch", "审计存储返回的 ReceiptId 与恢复请求不一致。", startedAt, sourceReceipt));
        }

        if (sourceReceipt?.RecoveryId is not null
            && request.RecoveryId is not null
            && sourceReceipt.RecoveryId != request.RecoveryId)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.request-recovery-mismatch", "请求中的恢复材料与审计回执不匹配。", startedAt, sourceReceipt));
        }

        var recoveryId = sourceReceipt?.RecoveryId ?? request.RecoveryId;
        if (recoveryId is null)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.receipt-missing", "恢复回执不存在或未关联恢复材料。", startedAt));
        }

        RecoveryMaterial? material;
        try
        {
            material = await _recoveryRepository.LoadAsync(recoveryId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.CancelledBeforeWrite, "restore.cancelled-material-read", "读取恢复材料时取消，未执行恢复写入。", startedAt, sourceReceipt));
        }
        catch (Exception ex)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.material-read-failed", Sanitize(ex.Message), startedAt, sourceReceipt));
        }

        if (material is null || !VerifyRecoveryHash(material))
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.material-invalid", "恢复材料缺失或完整性校验失败。", startedAt, sourceReceipt));
        }

        if (sourceReceipt is null
            && (material.ReceiptId == Guid.Empty || request.ReceiptId != material.ReceiptId))
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.receipt-binding-missing", "审计回执不可用，且恢复材料未持久化同源 ReceiptId 绑定。", startedAt));
        }

        if (sourceReceipt is not null && !IsReceiptBoundToRecovery(sourceReceipt, material))
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.receipt-material-mismatch", "审计回执与恢复材料的计划、能力、作用域或状态指纹不匹配。", startedAt, sourceReceipt));
        }

        if (!_capabilities.TryGetValue(material.CapabilityId, out var capability))
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "capability.unknown", "恢复能力未注册。", startedAt, sourceReceipt));
        }

        if (!string.Equals(material.DefinitionVersion, capability.Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, capability.Descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase)
            || !capability.Descriptor.SupportedScopes.Contains(material.Scope.Kind))
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.contract-mismatch", "恢复材料与当前能力定义不兼容。", startedAt, sourceReceipt));
        }

        ChangeExecutionGateDecision restoreGateDecision;
        try
        {
            restoreGateDecision = await _executionGate.EnsureCanExecuteAsync(
                material.CapabilityId,
                ChangeExecutionIntent.Restore,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.CancelledBeforeWrite, "restore.cancelled-execution-gate", "恢复门禁检查期间取消，未执行恢复写入。", startedAt, sourceReceipt));
        }
        catch (Exception ex)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.execution-gate-failed", Sanitize(ex.Message), startedAt, sourceReceipt));
        }

        if (!restoreGateDecision.IsAllowed)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.execution-gate-blocked", restoreGateDecision.Reason, startedAt, sourceReceipt));
        }

        sourceReceipt ??= new OperationReceipt(
            material.ReceiptId,
            material.PlanId,
            material.RecoveryId,
            material.CapabilityId,
            material.Scope,
            ChangeOutcome.RecoveryRequired,
            material.BeforeFingerprint,
            material.TargetFingerprint,
            null,
            "audit.receipt-recovered",
            "通过恢复材料定位未持久化的执行回执。",
            true,
            material.CreatedAtUtc,
            startedAt);

        IChangeResourceLease? resourceLease;
        try
        {
            resourceLease = await _resourceLockProvider.TryAcquireAsync(
                material.LockKey ?? material.ResourceKey,
                ResourceLockTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.CancelledBeforeWrite, "restore.cancelled-waiting-lock", "等待恢复资源锁时取消，未执行恢复写入。", startedAt, sourceReceipt));
        }
        catch (Exception ex)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.lock-failed", Sanitize(ex.Message), startedAt, sourceReceipt));
        }
        if (resourceLease is null)
        {
            return Wrap(CreateStandaloneReceipt(ChangeOutcome.Conflict, "resource.busy", "同一系统资源正在被其他操作修改。", startedAt, sourceReceipt));
        }

        try
        {
            CapabilityAssessment assessment;
            try
            {
                assessment = await ProbeAsync(capability, material.Scope, ChangeIntent.Restore, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.CancelledBeforeWrite, "restore.cancelled-permission-recheck", "恢复前权限与适用性复查期间取消。", startedAt, sourceReceipt));
            }
            catch (Exception ex)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.probe-failed", Sanitize(ex.Message), startedAt, sourceReceipt));
            }

            if (assessment.Applicability != CapabilityApplicability.Applicable)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, assessment.ReasonCode, assessment.Details, startedAt, sourceReceipt));
            }

            if (!assessment.HasRequiredPermission)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.Denied, "permission.denied", assessment.Details, startedAt, sourceReceipt));
            }

            StateObservation current;
            try
            {
                current = await capability.ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.CancelledBeforeWrite, "restore.cancelled-before-write", "恢复写入前取消。", startedAt, sourceReceipt));
            }
            catch (Exception ex)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.RestoreFailed, "restore.read-current-failed", Sanitize(ex.Message), startedAt, sourceReceipt));
            }

            var alreadyAtOriginalState = string.Equals(current.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal);
            if (!alreadyAtOriginalState
                && !string.Equals(current.Fingerprint, material.TargetFingerprint, StringComparison.Ordinal))
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.Conflict, "restore.state-drift", "应用后状态已被外部修改，为避免覆盖用户变更，本次恢复被阻断。", startedAt, sourceReceipt, current.Fingerprint));
            }

            RecoveryClaimResult recoveryClaim;
            try
            {
                recoveryClaim = await _recoveryClaimStore.TryBeginRestoreAsync(
                    material.RecoveryId,
                    material.IntegrityHash,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.CancelledBeforeWrite, "restore.cancelled-claim", "领取恢复材料期间取消，未执行恢复写入。", startedAt, sourceReceipt, current.Fingerprint));
            }
            catch (Exception ex)
            {
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, "restore.claim-failed", Sanitize(ex.Message), startedAt, sourceReceipt, current.Fingerprint));
            }

            if (recoveryClaim.Status != RecoveryClaimStatus.Claimed || recoveryClaim.ClaimId is null)
            {
                var code = recoveryClaim.Status switch
                {
                    RecoveryClaimStatus.AlreadyRestoring => "restore.already-restoring",
                    RecoveryClaimStatus.AlreadyRestored => "restore.already-restored",
                    RecoveryClaimStatus.IntegrityMismatch => "restore.claim-integrity-mismatch",
                    _ => "restore.claim-missing"
                };
                return Wrap(CreateStandaloneReceipt(ChangeOutcome.Blocked, code, "恢复材料不存在、正在恢复、已经恢复或状态完整性不匹配。", startedAt, sourceReceipt, current.Fingerprint));
            }

            if (alreadyAtOriginalState)
            {
                try
                {
                    await _recoveryClaimStore.MarkRestoredAsync(
                        material.RecoveryId,
                        material.IntegrityHash,
                        recoveryClaim.ClaimId.Value,
                        CancellationToken.None).ConfigureAwait(false);
                    return await PersistReceiptAsync(
                        CreateStandaloneReceipt(
                            ChangeOutcome.NoChange,
                            "restore.no-op",
                            "当前状态已经等于恢复前状态，恢复材料已永久结案。",
                            startedAt,
                            sourceReceipt,
                            current.Fingerprint),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return await PersistReceiptAsync(CreateStandaloneReceipt(
                        ChangeOutcome.RecoveryRequired,
                        "restore.complete-state-failed",
                        Sanitize(ex.Message),
                        startedAt,
                        sourceReceipt,
                        current.Fingerprint), CancellationToken.None).ConfigureAwait(false);
                }
            }

            try
            {
                using var persistenceSource = CreatePersistenceTokenSource(cancellationToken);
                await _journal.AppendAsync(
                    new JournalEntry(material.PlanId, material.CapabilityId, "Restoring", _timeProvider.GetUtcNow(), "恢复平台调用开始。"),
                    persistenceSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var released = await TryReleaseRecoveryClaimAsync(material, recoveryClaim.ClaimId.Value).ConfigureAwait(false);
                return Wrap(CreateStandaloneReceipt(
                    released ? ChangeOutcome.CancelledBeforeWrite : ChangeOutcome.RecoveryRequired,
                    released ? "restore.cancelled-journal" : "restore.release-failed",
                    released ? "恢复日志持久化期间取消，未执行平台写入。" : "恢复尚未写入，但恢复领取状态无法安全释放，需要人工核查。",
                    startedAt,
                    sourceReceipt,
                    current.Fingerprint));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var released = await TryReleaseRecoveryClaimAsync(material, recoveryClaim.ClaimId.Value).ConfigureAwait(false);
                return Wrap(CreateStandaloneReceipt(
                    released ? ChangeOutcome.Blocked : ChangeOutcome.RecoveryRequired,
                    released ? "restore.journal-failed" : "restore.release-failed",
                    released ? Sanitize(ex.Message) : "恢复尚未写入，但恢复领取状态无法安全释放，需要人工核查。",
                    startedAt,
                    sourceReceipt,
                    current.Fingerprint));
            }

            var platform = await RunPlatformAsync(
                token => capability.RestoreAsync(material, token),
                cancellationToken).ConfigureAwait(false);
            var verification = await ReadForVerificationAsync(capability, material.Scope).ConfigureAwait(false);
            platform = platform with { CallerCancelled = platform.CallerCancelled || cancellationToken.IsCancellationRequested };
            if (verification.Observation is null)
            {
                return await PersistReceiptAsync(CreateStandaloneReceipt(
                    ChangeOutcome.RecoveryRequired,
                    verification.TimedOut ? "restore.verification-timeout" : "restore.verification-failed",
                    Sanitize(verification.Error?.Message ?? "恢复后回读未返回结果。"),
                    startedAt,
                    sourceReceipt,
                    null,
                    true), CancellationToken.None).ConfigureAwait(false);
            }

            var after = verification.Observation;
            if (string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal))
            {
                try
                {
                    await _recoveryClaimStore.MarkRestoredAsync(
                        material.RecoveryId,
                        material.IntegrityHash,
                        recoveryClaim.ClaimId.Value,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return await PersistReceiptAsync(CreateStandaloneReceipt(
                        ChangeOutcome.RecoveryRequired,
                        "restore.complete-state-failed",
                        Sanitize(ex.Message),
                        startedAt,
                        sourceReceipt,
                        after.Fingerprint,
                        true), CancellationToken.None).ConfigureAwait(false);
                }

                return await PersistReceiptAsync(CreateStandaloneReceipt(
                    ChangeOutcome.RestoredVerified,
                    platform.CallerCancelled || platform.TimedOut || platform.Error is not null
                        ? "restore.verified-after-interruption"
                        : "restore.verified",
                    platform.CallerCancelled || platform.TimedOut || platform.Error is not null
                        ? "恢复阶段曾取消、超时或报错，但等待调用真正结束后独立回读确认已恢复。"
                        : "恢复完成并通过独立回读。",
                    startedAt,
                    sourceReceipt,
                    after.Fingerprint,
                    true), CancellationToken.None).ConfigureAwait(false);
            }

            if (string.Equals(after.Fingerprint, material.TargetFingerprint, StringComparison.Ordinal))
            {
                if (platform.Error is not null && !platform.CallerCancelled && !platform.TimedOut)
                {
                    return await PersistReceiptAsync(CreateStandaloneReceipt(
                        ChangeOutcome.RecoveryRequired,
                        "restore.execution-exception",
                        Sanitize(platform.Error.Message),
                        startedAt,
                        sourceReceipt,
                        after.Fingerprint,
                        true), CancellationToken.None).ConfigureAwait(false);
                }

                if (platform.Result?.MutationOccurred == true)
                {
                    return await PersistReceiptAsync(CreateStandaloneReceipt(
                        ChangeOutcome.RecoveryRequired,
                        "restore.execution-mutation-unknown",
                        "平台报告恢复阶段发生了写入，但独立回读仍为应用后状态；保留恢复领取，禁止自动重放。",
                        startedAt,
                        sourceReceipt,
                        after.Fingerprint,
                        true), CancellationToken.None).ConfigureAwait(false);
                }

                var released = await TryReleaseRecoveryClaimAsync(material, recoveryClaim.ClaimId.Value).ConfigureAwait(false);
                return await PersistReceiptAsync(CreateStandaloneReceipt(
                    released
                        ? platform.CallerCancelled ? ChangeOutcome.CancelledBeforeWrite : ChangeOutcome.RestoreFailed
                        : ChangeOutcome.RecoveryRequired,
                    released
                        ? platform.CallerCancelled ? "restore.cancelled-no-write" : platform.TimedOut ? "restore.timeout-no-write" : platform.Result?.ReasonCode ?? "restore.failed-no-write"
                        : "restore.release-failed",
                    released
                        ? "等待恢复平台调用真正结束后，独立回读证明状态仍为应用后目标值，可以安全重试。"
                        : "恢复未改变最终状态，但恢复领取状态无法安全释放，需要人工核查。",
                    startedAt,
                    sourceReceipt,
                    after.Fingerprint), CancellationToken.None).ConfigureAwait(false);
            }

            return await PersistReceiptAsync(CreateStandaloneReceipt(
                ChangeOutcome.RecoveryRequired,
                "restore.state-unknown",
                "恢复阶段结束后的状态既不是恢复前状态也不是应用后目标状态，保留恢复领取并要求人工核查。",
                startedAt,
                sourceReceipt,
                after.Fingerprint,
                true), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await resourceLease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private (ChangeOutcome Outcome, string Code, string Message)? ValidatePlan(
        PreparedChangePlan plan,
        CapabilityDescriptor descriptor,
        ConfirmationToken confirmation)
    {
        var expectedHash = ComputePlanHash(
            plan.PlanId,
            plan.Change,
            plan.PreparedAtUtc,
            plan.ExpiresAtUtc,
            descriptor,
            plan.ExecutionPolicyFingerprint);
        if (!string.Equals(plan.PlanHash, expectedHash, StringComparison.Ordinal))
        {
            return (ChangeOutcome.Blocked, "plan.hash-invalid", "执行计划完整性校验失败。");
        }

        if (plan.RequiresAdministrator != descriptor.RequiresAdministrator
            || plan.RequiresExplicitConfirmation != descriptor.RequiresExplicitConfirmation
            || plan.RequiresRecoveryMaterial != descriptor.RequiresRecoveryMaterial
            || plan.Risk != descriptor.Risk)
        {
            return (ChangeOutcome.Blocked, "plan.policy-mismatch", "执行计划中的安全策略与能力定义不一致。");
        }

        if (!string.Equals(plan.Change.CapabilityId, descriptor.CapabilityId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(plan.Change.DefinitionVersion, descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(plan.Change.ResourceKey, descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase)
            || !descriptor.SupportedScopes.Contains(plan.Change.Scope.Kind))
        {
            return (ChangeOutcome.Blocked, "plan.contract-mismatch", "执行计划与当前能力的标识、版本、资源或作用域契约不一致。");
        }

        if (!string.Equals(plan.ExecutionPolicyVersion, descriptor.ExecutionPolicyVersion, StringComparison.Ordinal)
            || !string.Equals(plan.ExecutionPolicyVersion, _executionPolicy.Version, StringComparison.Ordinal)
            || !string.Equals(plan.ExecutionPolicyFingerprint, _executionPolicy.Fingerprint, StringComparison.Ordinal))
        {
            return (ChangeOutcome.Blocked, "plan.execution-policy-mismatch", "执行计划中的有界执行策略版本与当前能力或协调器不一致。");
        }

        var now = _timeProvider.GetUtcNow();
        if (now > plan.ExpiresAtUtc)
        {
            return (ChangeOutcome.Blocked, "plan.expired", "执行计划已过期，请重新读取系统状态。");
        }

        if (!string.Equals(confirmation.PlanHash, plan.PlanHash, StringComparison.Ordinal)
            || confirmation.Scope != plan.Change.Scope
            || confirmation.ExpiresAtUtc < now)
        {
            return (ChangeOutcome.Denied, "confirmation.invalid", "确认凭据与当前计划、作用域或有效期不匹配。");
        }

        return null;
    }

    private async Task<BatchReceipt> PersistReceiptAsync(OperationReceipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            using var persistenceSource = CreatePersistenceTokenSource(cancellationToken);
            await _auditStore.SaveAsync(receipt, persistenceSource.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var outcome = receipt.MutationOccurred ? ChangeOutcome.RecoveryRequired : ChangeOutcome.FailedNoChange;
            return Wrap(receipt with
            {
                Outcome = outcome,
                ReasonCode = "audit.persist-failed",
                Message = Sanitize(ex.Message),
                CompletedAtUtc = _timeProvider.GetUtcNow()
            });
        }

        try
        {
            using var persistenceSource = CreatePersistenceTokenSource(cancellationToken);
            await _journal.AppendAsync(
                new JournalEntry(
                    receipt.PlanId,
                    receipt.CapabilityId,
                    $"Terminal:{receipt.Outcome}",
                    _timeProvider.GetUtcNow(),
                    receipt.ReasonCode),
                persistenceSource.Token).ConfigureAwait(false);
            return Wrap(receipt);
        }
        catch (Exception ex)
        {
            var outcome = receipt.MutationOccurred ? ChangeOutcome.RecoveryRequired : ChangeOutcome.FailedNoChange;
            var amended = receipt with
            {
                Outcome = outcome,
                ReasonCode = "journal.terminal-persist-failed",
                Message = Sanitize(ex.Message),
                CompletedAtUtc = _timeProvider.GetUtcNow()
            };
            try
            {
                using var retrySource = CreatePersistenceTokenSource(CancellationToken.None);
                await _auditStore.SaveAsync(amended, retrySource.Token).ConfigureAwait(false);
            }
            catch
            {
                // The original receipt is already durable; callers still receive the stricter amended state.
            }

            return Wrap(amended);
        }
    }

    private async Task<bool> TryReleaseRecoveryClaimAsync(RecoveryMaterial material, Guid claimId)
    {
        try
        {
            await _recoveryClaimStore.ReleaseAsync(
                material.RecoveryId,
                material.IntegrityHash,
                claimId,
                CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<PlatformRun> RunPlatformAsync(
        Func<CancellationToken, Task<PlatformExecutionResult>> action,
        CancellationToken callerToken)
    {
        using var timeoutSource = new CancellationTokenSource(_executionPolicy.PlatformTimeout);
        try
        {
            var result = await action(timeoutSource.Token).ConfigureAwait(false);
            return new PlatformRun(result, null, callerToken.IsCancellationRequested, timeoutSource.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            return new PlatformRun(null, ex, callerToken.IsCancellationRequested, timeoutSource.IsCancellationRequested);
        }
    }

    private async Task<VerificationRead> ReadForVerificationAsync(IChangeCapability capability, TargetScope scope)
    {
        using var timeoutSource = new CancellationTokenSource(_executionPolicy.VerificationTimeout);
        try
        {
            var observation = await capability.ReadAsync(scope, timeoutSource.Token).ConfigureAwait(false);
            return new VerificationRead(observation, null, timeoutSource.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            return new VerificationRead(null, ex, timeoutSource.IsCancellationRequested);
        }
    }

    private OperationReceipt CreateExecutionTerminalReceipt(
        PreparedChangePlan plan,
        Guid? recoveryId,
        PlatformRun platform,
        VerificationRead verification,
        DateTimeOffset startedAt,
        Guid? receiptId)
    {
        if (verification.Observation is null)
        {
            var code = verification.TimedOut ? "verification.timeout" : "verification.read-failed";
            return CreateReceipt(
                plan,
                recoveryId,
                ChangeOutcome.RecoveryRequired,
                code,
                Sanitize(verification.Error?.Message ?? "写入后回读未返回结果。"),
                true,
                null,
                startedAt,
                receiptId);
        }

        var after = verification.Observation;
        if (string.Equals(after.Fingerprint, plan.Change.BeforeFingerprint, StringComparison.Ordinal))
        {
            var cancelled = platform.CallerCancelled;
            var timedOut = platform.TimedOut;
            if (!cancelled
                && !timedOut
                && platform.Error is null
                && platform.Result?.MutationOccurred == true)
            {
                return CreateReceipt(
                    plan,
                    recoveryId,
                    ChangeOutcome.AppliedButUnverified,
                    "execution.reported-mutation-without-target",
                    "平台报告已写入，但独立回读仍为原状态，不能把结果认定为成功。",
                    true,
                    after.Fingerprint,
                    startedAt,
                    receiptId);
            }

            return CreateReceipt(
                plan,
                recoveryId,
                cancelled ? ChangeOutcome.CancelledBeforeWrite : ChangeOutcome.FailedNoChange,
                cancelled ? "cancelled.platform-no-write" : timedOut ? "execution.timeout-no-write" : platform.Result?.ReasonCode ?? "execution.failed-no-write",
                cancelled ? "平台阶段收到取消请求，独立回读证明未发生写入。" : timedOut ? "平台执行超过策略时限，等待其真正结束后回读证明未发生写入。" : platform.Result?.Message ?? Sanitize(platform.Error?.Message ?? "平台执行失败且未发生写入。"),
                false,
                after.Fingerprint,
                startedAt,
                receiptId);
        }

        if (string.Equals(after.Fingerprint, plan.Change.TargetFingerprint, StringComparison.Ordinal))
        {
            var interrupted = platform.CallerCancelled || platform.TimedOut || platform.Error is not null;
            return CreateReceipt(
                plan,
                recoveryId,
                ChangeOutcome.SucceededVerified,
                interrupted ? "execution.verified-after-interruption" : "execution.verified",
                interrupted ? "平台阶段曾取消、超时或报错，但等待调用真正结束后独立回读确认目标已完整应用。" : "写入完成并通过独立回读。",
                true,
                after.Fingerprint,
                startedAt,
                receiptId);
        }

        return CreateReceipt(
            plan,
            recoveryId,
            ChangeOutcome.RecoveryRequired,
            "execution.state-unknown",
            "平台阶段结束后的状态既不是原状态也不是目标状态，需要恢复核查。",
            true,
            after.Fingerprint,
            startedAt,
            receiptId);
    }

    private sealed record PlatformRun(
        PlatformExecutionResult? Result,
        Exception? Error,
        bool CallerCancelled,
        bool TimedOut);

    private sealed record VerificationRead(
        StateObservation? Observation,
        Exception? Error,
        bool TimedOut);

    private async Task AppendJournalAsync(PreparedChangePlan plan, string stage, string details, CancellationToken cancellationToken)
    {
        using var persistenceSource = CreatePersistenceTokenSource(cancellationToken);
        await _journal.AppendAsync(
            new JournalEntry(plan.PlanId, plan.Change.CapabilityId, stage, _timeProvider.GetUtcNow(), details),
            persistenceSource.Token).ConfigureAwait(false);
    }

    private CancellationTokenSource CreatePersistenceTokenSource(CancellationToken callerToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        source.CancelAfter(_executionPolicy.PersistenceTimeout);
        return source;
    }

    private RecoveryMaterial CreateRecoveryMaterial(PreparedChangePlan plan, DateTimeOffset createdAt)
    {
        var recoveryId = Guid.NewGuid();
        var hash = ComputeRecoveryHash(
            recoveryId,
            plan.PlanId,
            plan.Change.CapabilityId,
            plan.Change.DefinitionVersion,
            plan.Change.Scope,
            plan.Change.BeforePayload,
            plan.Change.BeforeFingerprint,
            plan.Change.TargetFingerprint,
            plan.Change.ResourceKey,
            createdAt);
        var receiptId = RecoveryMaterialBinding.ComputeReceiptId(
            recoveryId,
            plan.Change.ResourceKey,
            hash);
        return new RecoveryMaterial(
            recoveryId,
            plan.PlanId,
            plan.Change.CapabilityId,
            plan.Change.DefinitionVersion,
            plan.Change.Scope,
            plan.Change.BeforePayload,
            plan.Change.BeforeFingerprint,
            plan.Change.TargetFingerprint,
            plan.Change.ResourceKey,
            createdAt,
            hash,
            receiptId,
            plan.Change.LockKey);
    }

    private static bool VerifyRecoveryHash(RecoveryMaterial material)
    {
        var contentHashMatches = string.Equals(
            material.IntegrityHash,
            ComputeRecoveryHash(
                material.RecoveryId,
                material.PlanId,
                material.CapabilityId,
                material.DefinitionVersion,
                material.Scope,
                material.BeforePayload,
                material.BeforeFingerprint,
                material.TargetFingerprint,
                material.ResourceKey,
                material.CreatedAtUtc),
            StringComparison.Ordinal);
        if (!contentHashMatches)
        {
            return false;
        }

        if (material.ReceiptId == Guid.Empty)
        {
            return true;
        }

        return material.ReceiptId == RecoveryMaterialBinding.ComputeReceiptId(
            material.RecoveryId,
            material.ResourceKey,
            material.IntegrityHash);
    }

    private static bool IsReceiptBoundToRecovery(OperationReceipt receipt, RecoveryMaterial material) =>
        (material.ReceiptId == Guid.Empty || receipt.ReceiptId == material.ReceiptId)
        && receipt.RecoveryId == material.RecoveryId
        && receipt.PlanId == material.PlanId
        && string.Equals(receipt.CapabilityId, material.CapabilityId, StringComparison.OrdinalIgnoreCase)
        && receipt.Scope == material.Scope
        && string.Equals(receipt.BeforeFingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
        && string.Equals(receipt.TargetFingerprint, material.TargetFingerprint, StringComparison.Ordinal);

    private OperationReceipt CreateReceipt(
        PreparedChangePlan plan,
        Guid? recoveryId,
        ChangeOutcome outcome,
        string reasonCode,
        string message,
        bool mutationOccurred,
        string? afterFingerprint,
        DateTimeOffset startedAt,
        Guid? receiptId = null) =>
        new(
            receiptId ?? Guid.NewGuid(),
            plan.PlanId,
            recoveryId,
            plan.Change.CapabilityId,
            plan.Change.Scope,
            outcome,
            plan.Change.BeforeFingerprint,
            plan.Change.TargetFingerprint,
            afterFingerprint,
            reasonCode,
            message,
            mutationOccurred,
            startedAt,
            _timeProvider.GetUtcNow());

    private OperationReceipt CreateStandaloneReceipt(
        ChangeOutcome outcome,
        string reasonCode,
        string message,
        DateTimeOffset startedAt,
        OperationReceipt? source = null,
        string? afterFingerprint = null,
        bool mutationOccurred = false) =>
        new(
            Guid.NewGuid(),
            source?.PlanId ?? Guid.Empty,
            source?.RecoveryId,
            source?.CapabilityId ?? string.Empty,
            source?.Scope ?? new TargetScope(TargetScopeKind.CurrentUser, string.Empty),
            outcome,
            source?.BeforeFingerprint ?? string.Empty,
            source?.TargetFingerprint ?? string.Empty,
            afterFingerprint,
            reasonCode,
            message,
            mutationOccurred,
            startedAt,
            _timeProvider.GetUtcNow());

    private async Task<CapabilityAssessment> ProbeAsync(
        IChangeCapability capability,
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken)
    {
        if (_compatibilityPolicy is not null)
        {
            var centralized = await _compatibilityPolicy.ProbeAsync(
                capability.Descriptor,
                scope,
                intent,
                cancellationToken).ConfigureAwait(false);
            if (centralized is not null)
            {
                return centralized;
            }
        }

        return await capability.ProbeAsync(scope, intent, cancellationToken).ConfigureAwait(false);
    }

    private static PreparationResult Blocked(string code, string message) =>
        new(ChangePreparationStatus.Blocked, null, code, message, null);

    private static BatchReceipt Wrap(OperationReceipt receipt) => new(receipt);

    private static string ComputePlanHash(
        Guid planId,
        PreparedCapabilityChange change,
        DateTimeOffset preparedAt,
        DateTimeOffset expiresAt,
        CapabilityDescriptor descriptor,
        string executionPolicyFingerprint) =>
        Hash(
            planId.ToString("N"),
            change.CapabilityId,
            change.DefinitionVersion,
            change.Scope.Kind.ToString(),
            change.Scope.Identity,
            change.BeforePayload,
            change.BeforeFingerprint,
            change.TargetPayload,
            change.TargetFingerprint,
            change.ResourceKey,
            preparedAt.UtcTicks.ToString(),
            expiresAt.UtcTicks.ToString(),
            descriptor.RequiresAdministrator.ToString(),
            descriptor.RequiresExplicitConfirmation.ToString(),
            descriptor.RequiresRecoveryMaterial.ToString(),
            descriptor.Risk.ToString(),
            descriptor.ExecutionPolicyVersion,
            executionPolicyFingerprint);

    private static string ComputeRecoveryHash(
        Guid recoveryId,
        Guid planId,
        string capabilityId,
        string definitionVersion,
        TargetScope scope,
        string beforePayload,
        string beforeFingerprint,
        string targetFingerprint,
        string resourceKey,
        DateTimeOffset createdAt) =>
        Hash(
            recoveryId.ToString("N"),
            planId.ToString("N"),
            capabilityId,
            definitionVersion,
            scope.Kind.ToString(),
            scope.Identity,
            beforePayload,
            beforeFingerprint,
            targetFingerprint,
            resourceKey,
            createdAt.UtcTicks.ToString());

    private static string Hash(params string[] values)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\u001F', values));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string Sanitize(string message) =>
        string.IsNullOrWhiteSpace(message) ? "未提供错误详情。" : message.Trim();
}
