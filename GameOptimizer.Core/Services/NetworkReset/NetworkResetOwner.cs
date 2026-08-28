using GameOptimizer.Core.Contracts.Changes;
using static GameOptimizer.Core.Services.NetworkReset.NetworkResetPolicy;

namespace GameOptimizer.Core.Services.NetworkReset;

public sealed class NetworkResetOwner : INetworkResetOwner
{
    private readonly INetworkResetPlatform _platform;
    private readonly INetworkResetBaselineGate _baselineGate;
    private readonly IChangeConfirmationAuthority _confirmationAuthority;
    private readonly INetworkResetPlanStore _planStore;
    private readonly INetworkResetReceiptStore _receiptStore;
    private readonly IChangeResourceLockProvider _resourceLockProvider;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _resourceLockTimeout;

    public NetworkResetOwner(
        INetworkResetPlatform platform,
        INetworkResetBaselineGate baselineGate,
        IChangeConfirmationAuthority confirmationAuthority,
        INetworkResetPlanStore planStore,
        INetworkResetReceiptStore receiptStore,
        IChangeResourceLockProvider resourceLockProvider,
        TimeProvider? timeProvider = null,
        TimeSpan? resourceLockTimeout = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _baselineGate = baselineGate ?? throw new ArgumentNullException(nameof(baselineGate));
        _confirmationAuthority = confirmationAuthority ?? throw new ArgumentNullException(nameof(confirmationAuthority));
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
        _receiptStore = receiptStore ?? throw new ArgumentNullException(nameof(receiptStore));
        _resourceLockProvider = resourceLockProvider ?? throw new ArgumentNullException(nameof(resourceLockProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _resourceLockTimeout = resourceLockTimeout ?? DefaultResourceLockTimeout;
        if (_resourceLockTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(resourceLockTimeout));
        }
    }

    public async Task<NetworkResetPlan> PrepareAsync(
        NetworkResetProfile profile,
        CancellationToken cancellationToken = default)
    {
        var status = await ReadStatusAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!status.IsSupported)
        {
            throw new InvalidOperationException($"{status.ReasonCode}: {status.Message}");
        }
        if (!status.IsAdministrator)
        {
            throw new UnauthorizedAccessException("network.reset.administrator-required");
        }
        if (!status.SnapshotAvailable || status.Snapshot is null)
        {
            throw new InvalidOperationException($"{status.ReasonCode}: {status.Message}");
        }

        var recoveryDecision = profile == NetworkResetProfile.DnsCache
            ? NetworkRecoveryDecision.NotRequired
            : NetworkRecoveryDecision.ManualRecoveryOnly;
        var snapshot = status.Snapshot;
        var steps = MutationCommands(profile);
        var risks = Risks(profile);
        var now = _timeProvider.GetUtcNow();
        var beforePayload = Serialize(snapshot);
        var targetPayload = Serialize(steps);
        var change = new PreparedCapabilityChange(
            $"network.reset.{profile.ToString().ToLowerInvariant()}",
            "2.0.0",
            TargetScope.Machine(Environment.MachineName),
            beforePayload,
            snapshot.IdentityHash,
            targetPayload,
            Hash(targetPayload),
            ResourceKey);
        var changePlan = new PreparedChangePlan(
            Guid.NewGuid(),
            change,
            string.Empty,
            now,
            now.Add(PlanLifetime),
            RequiresAdministrator: true,
            RequiresExplicitConfirmation: true,
            RequiresRecoveryMaterial: profile != NetworkResetProfile.DnsCache,
            Risk: ChangeRisk.HighSystem,
            ExecutionPolicyVersion: PolicyVersion,
            ExecutionPolicyFingerprint: PolicyFingerprint());
        var plan = new NetworkResetPlan(
            changePlan,
            profile,
            snapshot,
            steps,
            risks,
            RebootRequired: profile != NetworkResetProfile.DnsCache,
            recoveryDecision);
        plan = plan with
        {
            ChangePlan = plan.ChangePlan with { PlanHash = ComputePlanHash(plan) }
        };
        await _planStore.RegisterAsync(plan, cancellationToken).ConfigureAwait(false);
        return plan;
    }

    public async Task<NetworkResetReadOnlyStatus> ReadStatusAsync(
        NetworkResetProfile profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = _timeProvider.GetUtcNow();
        NetworkResetPlatformAssessment assessment;
        try
        {
            assessment = await _platform.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new NetworkResetReadOnlyStatus(
                profile,
                false,
                false,
                false,
                "platform.probe-failed",
                $"平台只读探测失败：{ex.GetType().Name}",
                null,
                observedAt);
        }

        if (!assessment.IsSupported)
        {
            return new NetworkResetReadOnlyStatus(
                profile,
                assessment.IsSupported,
                assessment.IsAdministrator,
                false,
                assessment.ReasonCode,
                assessment.Message,
                null,
                observedAt);
        }

        NetworkResetSafetyAssessment? safety = null;
        if (profile != NetworkResetProfile.DnsCache)
        {
            try
            {
                safety = await _platform.AssessResetSafetyAsync(profile, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new NetworkResetReadOnlyStatus(
                    profile,
                    false,
                    assessment.IsAdministrator,
                    false,
                    "safety.probe-failed",
                    $"网络重置安全预检失败：{ex.GetType().Name}",
                    null,
                    observedAt);
            }

            if (!safety.IsExecutionAllowed)
            {
                return new NetworkResetReadOnlyStatus(
                    profile,
                    false,
                    assessment.IsAdministrator,
                    false,
                    safety.ReasonCode,
                    safety.Message,
                    null,
                    observedAt,
                    safety);
            }
        }

        if (!assessment.IsAdministrator)
        {
            return new NetworkResetReadOnlyStatus(
                profile,
                true,
                false,
                false,
                assessment.ReasonCode,
                assessment.Message,
                null,
                observedAt,
                safety);
        }

        try
        {
            var entries = await CaptureEntriesAsync(profile, cancellationToken).ConfigureAwait(false);
            var snapshot = new NetworkResetSnapshot(
                Hash(Serialize(entries)),
                entries,
                IsMachineRestorable: false,
                RecoveryBoundary(profile));
            return new NetworkResetReadOnlyStatus(
                profile,
                true,
                true,
                true,
                "status.snapshot-ready",
                "管理员、平台与 typed 只读快照均可用。",
                snapshot,
                observedAt,
                safety);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new NetworkResetReadOnlyStatus(
                profile,
                true,
                true,
                false,
                "status.snapshot-failed",
                $"typed 只读快照失败：{ex.Message}",
                null,
                observedAt,
                safety);
        }
    }

    public async Task<NetworkResetReceipt> RunInteractiveAsync(
        NetworkResetProfile profile,
        CancellationToken cancellationToken = default)
    {
        NetworkResetPlan plan;
        try
        {
            plan = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Synthetic(
                profile,
                NetworkResetOutcome.CancelledBeforeWrite,
                "cancelled.prepare",
                "准备期间已取消，未发生网络写入。");
        }
        catch (Exception ex)
        {
            return Synthetic(profile, NetworkResetOutcome.Blocked, "prepare.blocked", ex.Message);
        }

        var summary = string.Join(Environment.NewLine, plan.Steps.Select(step => $"- {step}"))
            + Environment.NewLine
            + string.Join(Environment.NewLine, plan.Risks.Select(risk => $"风险：{risk}"))
            + Environment.NewLine
            + $"恢复边界：{plan.Snapshot.RecoveryBoundary}";
        ConfirmationToken? confirmation;
        try
        {
            confirmation = await _confirmationAuthority.RequestAsync(
                plan.ChangePlan,
                "确认执行网络重置",
                summary,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.CancelledBeforeWrite,
                "cancelled.confirmation",
                "确认期间已取消，未发生网络写入。",
                false).ConfigureAwait(false);
        }
        if (confirmation is null)
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.Denied,
                "confirmation.denied",
                "用户未确认或确认已过期，未发生网络写入。",
                false).ConfigureAwait(false);
        }
        return await ExecuteAsync(plan, confirmation, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<NetworkResetReceipt>> GetPendingRecoveryReceiptsAsync(
        CancellationToken cancellationToken = default) =>
        _receiptStore.LoadPendingAsync(cancellationToken);

    public async Task<NetworkResetReceipt> ExecuteAsync(
        NetworkResetPlan plan,
        ConfirmationToken confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmation);
        var started = _timeProvider.GetUtcNow();
        if (cancellationToken.IsCancellationRequested)
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.CancelledBeforeWrite,
                "cancelled.before-execution",
                "执行前已取消，未发生网络写入。",
                false,
                started).ConfigureAwait(false);
        }
        if (!IsPlanInternallyConsistent(plan))
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.Conflict,
                "plan.integrity-mismatch",
                "冻结计划、快照、步骤或计划 hash 不一致。",
                false,
                started).ConfigureAwait(false);
        }

        NetworkResetPlan? persisted;
        try
        {
            persisted = await _planStore
                .LoadAsync(plan.ChangePlan.PlanId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.CancelledBeforeWrite,
                "cancelled.plan-load",
                "读取 durable 计划期间已取消，未发生网络写入。",
                false,
                started).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.Conflict,
                "plan.load-failed",
                $"durable 计划无法验证：{ex.GetType().Name}",
                false,
                started).ConfigureAwait(false);
        }
        if (persisted is null
            || !IsPlanInternallyConsistent(persisted)
            || !FixedHashEquals(plan.ChangePlan.PlanHash, persisted.ChangePlan.PlanHash))
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.Conflict,
                "plan.persisted-mismatch",
                "调用方计划与 durable 冻结计划不一致。",
                false,
                started).ConfigureAwait(false);
        }
        if (started < plan.ChangePlan.PreparedAtUtc.AddSeconds(-30)
            || started > plan.ChangePlan.ExpiresAtUtc)
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.Denied,
                "plan.expired",
                "计划时间窗口无效或已过期。",
                false,
                started).ConfigureAwait(false);
        }
        if (!_confirmationAuthority.TryConsume(plan.ChangePlan, confirmation))
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.Denied,
                "confirmation.invalid",
                "确认与当前计划不匹配、已消费、跨实例或已过期。",
                false,
                started).ConfigureAwait(false);
        }

        NetworkResetBaselineDecision baseline;
        try
        {
            baseline = await _baselineGate.AuthorizeAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.CancelledBeforeWrite,
                "cancelled.baseline",
                "首次基线授权期间已取消，未发生网络写入。",
                false,
                started).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.Blocked,
                "baseline.exception",
                $"首次基线授权失败：{ex.GetType().Name}",
                false,
                started).ConfigureAwait(false);
        }
        if (!baseline.Authorized || string.IsNullOrWhiteSpace(baseline.EvidenceHash))
        {
            return await PersistNewTerminalAsync(
                plan,
                NetworkResetOutcome.Blocked,
                baseline.ReasonCode,
                baseline.Message,
                false,
                started).ConfigureAwait(false);
        }

        var receipt = CreateExecutingReceipt(plan, started) with
        {
            Phase = NetworkResetReceiptPhase.BaselineAuthorized,
            ReasonCode = baseline.ReasonCode,
            Message = "首次基线已授权；尚未执行网络写入。"
        };
        var persistenceError = await TrySaveReceiptAsync(receipt).ConfigureAwait(false);
        if (persistenceError is not null)
        {
            return PersistenceFailure(receipt, persistenceError);
        }

        PlanClaimStatus claim;
        try
        {
            claim = await _planStore
                .TryClaimAsync(plan.ChangePlan.PlanId, plan.ChangePlan.PlanHash, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await CompleteAsync(
                receipt,
                NetworkResetOutcome.CancelledBeforeWrite,
                "cancelled.plan-claim-unknown",
                "计划 claim 期间已取消；网络未写入，但该计划禁止重试。",
                false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await CompleteAsync(
                receipt,
                NetworkResetOutcome.Conflict,
                "plan.claim-failed",
                $"计划 claim 状态不可确认，禁止执行：{ex.GetType().Name}",
                false).ConfigureAwait(false);
        }
        if (claim != PlanClaimStatus.Claimed)
        {
            return await CompleteAsync(
                receipt,
                NetworkResetOutcome.Conflict,
                $"plan.{claim.ToString().ToLowerInvariant()}",
                "计划不能重复执行。",
                false).ConfigureAwait(false);
        }
        receipt = receipt with
        {
            Phase = NetworkResetReceiptPhase.Claimed,
            ReasonCode = "plan.claimed",
            Message = "计划已永久 claim；尚未执行网络写入。"
        };
        persistenceError = await TrySaveReceiptAsync(receipt).ConfigureAwait(false);
        if (persistenceError is not null)
        {
            return PersistenceFailure(receipt, persistenceError);
        }

        IChangeResourceLease? resourceLease;
        try
        {
            resourceLease = await _resourceLockProvider.TryAcquireAsync(
                ResourceKey,
                _resourceLockTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await CompleteAsync(
                receipt,
                NetworkResetOutcome.CancelledBeforeWrite,
                "cancelled.resource-lock",
                "等待全局网络栈资源锁期间已取消，未发生网络写入。",
                false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await CompleteAsync(
                receipt,
                NetworkResetOutcome.Blocked,
                "resource-lock.failed",
                $"全局网络栈资源锁不可用：{ex.GetType().Name}",
                false).ConfigureAwait(false);
        }
        if (resourceLease is null)
        {
            return await CompleteAsync(
                receipt,
                NetworkResetOutcome.Blocked,
                "resource-lock.timeout",
                "全局网络栈资源正被另一操作占用，已零写入阻断。",
                false).ConfigureAwait(false);
        }

        await using (resourceLease.ConfigureAwait(false))
        {
            receipt = receipt with
            {
                Phase = NetworkResetReceiptPhase.LeaseAcquired,
                ReasonCode = "resource-lock.acquired",
                Message = "已取得跨实例全局网络栈资源锁；尚未执行网络写入。"
            };
            persistenceError = await TrySaveReceiptAsync(receipt).ConfigureAwait(false);
            if (persistenceError is not null)
            {
                return PersistenceFailure(receipt, persistenceError);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return await CompleteAsync(
                    receipt,
                    NetworkResetOutcome.CancelledBeforeWrite,
                    "cancelled.before-write",
                    "取得资源锁后、写入前已取消，未发生网络写入。",
                    false).ConfigureAwait(false);
            }

            NetworkResetPlatformAssessment assessment;
            try
            {
                assessment = await _platform.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return await CompleteAsync(
                    receipt,
                    NetworkResetOutcome.CancelledBeforeWrite,
                    "cancelled.platform-probe",
                    "写入前平台探测期间已取消，未发生网络写入。",
                    false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await CompleteAsync(
                    receipt,
                    NetworkResetOutcome.Blocked,
                    "platform.probe-failed",
                    $"写入前平台探测失败：{ex.GetType().Name}",
                    false).ConfigureAwait(false);
            }
            if (!assessment.IsSupported || !assessment.IsAdministrator)
            {
                return await CompleteAsync(
                    receipt,
                    NetworkResetOutcome.Blocked,
                    assessment.ReasonCode,
                    assessment.Message,
                    false).ConfigureAwait(false);
            }

            if (plan.Profile != NetworkResetProfile.DnsCache)
            {
                NetworkResetSafetyAssessment safety;
                try
                {
                    safety = await _platform
                        .AssessResetSafetyAsync(plan.Profile, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return await CompleteAsync(
                        receipt,
                        NetworkResetOutcome.CancelledBeforeWrite,
                        "cancelled.safety-preflight",
                        "写入前安全预检期间已取消，未发生网络写入。",
                        false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return await CompleteAsync(
                        receipt,
                        NetworkResetOutcome.Blocked,
                        "safety.probe-failed",
                        $"写入前安全预检失败：{ex.GetType().Name}",
                        false).ConfigureAwait(false);
                }

                if (!safety.IsExecutionAllowed)
                {
                    return await CompleteAsync(
                        receipt,
                        NetworkResetOutcome.Blocked,
                        safety.ReasonCode,
                        safety.Message,
                        false).ConfigureAwait(false);
                }
            }

            string currentIdentity;
            try
            {
                currentIdentity = await CaptureIdentityAsync(plan.Profile, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return await CompleteAsync(
                    receipt,
                    NetworkResetOutcome.CancelledBeforeWrite,
                    "cancelled.snapshot-reread",
                    "写入前快照重读期间已取消，未发生网络写入。",
                    false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await CompleteAsync(
                    receipt,
                    NetworkResetOutcome.Blocked,
                    "snapshot.reread-failed",
                    $"写入前快照重读失败：{ex.GetType().Name}",
                    false).ConfigureAwait(false);
            }
            if (!FixedHashEquals(plan.Snapshot.IdentityHash, currentIdentity))
            {
                return await CompleteAsync(
                    receipt,
                    NetworkResetOutcome.Conflict,
                    "snapshot.drift",
                    "网络配置在确认后发生变化，已零写入阻断。",
                    false).ConfigureAwait(false);
            }

            receipt = receipt with
            {
                Phase = NetworkResetReceiptPhase.PreWriteVerified,
                ReasonCode = "prewrite.verified",
                Message = "权限、平台与冻结快照已复核；准备执行第一步。"
            };
            persistenceError = await TrySaveReceiptAsync(receipt).ConfigureAwait(false);
            if (persistenceError is not null)
            {
                return PersistenceFailure(receipt, persistenceError);
            }

            var confirmedMutationOccurred = false;
            for (var index = 0; index < plan.Steps.Count; index++)
            {
                var command = plan.Steps[index];
                if (cancellationToken.IsCancellationRequested)
                {
                    return await CompleteAsync(
                        receipt,
                        confirmedMutationOccurred
                            ? NetworkResetOutcome.RecoveryRequired
                            : NetworkResetOutcome.CancelledBeforeWrite,
                        "cancelled.safe-point",
                        confirmedMutationOccurred
                            ? "取消请求已记录；当前命令已退出，后续步骤已停止，需要人工检查。"
                            : "已在第一步写入前的安全点取消。",
                        confirmedMutationOccurred).ConfigureAwait(false);
                }

                var stepStarted = _timeProvider.GetUtcNow();
                var stepExecutingReceipt = receipt with
                {
                    Phase = NetworkResetReceiptPhase.StepExecuting,
                    CurrentStep = command,
                    MutationOccurred = true,
                    ManualInterventionRequired = true,
                    RebootRequired = plan.Profile != NetworkResetProfile.DnsCache,
                    RecoveryDecision = NetworkRecoveryDecision.RecoveryRequired,
                    RecoveryInstructions = RecoveryInstructions(
                        plan.Profile,
                        true,
                        NetworkResetOutcome.RecoveryRequired),
                    ReasonCode = "step.executing",
                    Message = $"步骤 {index + 1}/{plan.Steps.Count} 即将执行；若进程在下一个检查点前终止，必须按写入状态未知处理。"
                };
                persistenceError = await TrySaveReceiptAsync(stepExecutingReceipt).ConfigureAwait(false);
                if (persistenceError is not null)
                {
                    return PersistenceFailure(receipt, persistenceError);
                }
                receipt = stepExecutingReceipt;

                NetworkResetCommandResult execution;
                try
                {
                    // The platform owns the internal deadline and must wait for any started process to exit.
                    execution = await _platform
                        .RunMutationAsync(command, CommandTimeout, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var failedStep = CreateUnknownStep(index, command, stepStarted, ex.GetType().Name);
                    receipt = receipt with
                    {
                        MutationOccurred = true,
                        Steps = receipt.Steps.Append(failedStep).ToArray(),
                        CurrentStep = null
                    };
                    return await CompleteAsync(
                        receipt,
                        NetworkResetOutcome.RecoveryRequired,
                        "process.exception-write-unknown",
                        "平台执行异常，写入状态未知；不要自动重试。",
                        true).ConfigureAwait(false);
                }

                var mayHaveMutated = execution.ProcessStarted;
                confirmedMutationOccurred |= mayHaveMutated;
                receipt = receipt with
                {
                    MutationOccurred = confirmedMutationOccurred,
                    ManualInterventionRequired = confirmedMutationOccurred,
                    RebootRequired = confirmedMutationOccurred
                        && plan.Profile != NetworkResetProfile.DnsCache,
                    RecoveryDecision = confirmedMutationOccurred
                        ? NetworkRecoveryDecision.RecoveryRequired
                        : NetworkRecoveryDecision.NotRequired,
                    RecoveryInstructions = RecoveryInstructions(
                        plan.Profile,
                        confirmedMutationOccurred,
                        confirmedMutationOccurred
                            ? NetworkResetOutcome.RecoveryRequired
                            : NetworkResetOutcome.FailedNoChange)
                };
                if (!execution.ProcessStarted
                    || execution.TimedOut
                    || execution.CancellationObservedAfterStart
                    || execution.ExitCode != 0)
                {
                    var reason = Classify(execution);
                    var failedStep = new NetworkResetStepReceipt(
                        index + 1,
                        command,
                        execution.ExitCode,
                        mayHaveMutated,
                        false,
                        false,
                        reason,
                        "步骤未完成，后续命令已停止。",
                        Hash(execution.StandardOutput),
                        Hash(execution.StandardError),
                        stepStarted,
                        _timeProvider.GetUtcNow());
                    receipt = receipt with
                    {
                        Steps = receipt.Steps.Append(failedStep).ToArray(),
                        CurrentStep = null
                    };
                    return await CompleteAsync(
                        receipt,
                        confirmedMutationOccurred
                            ? NetworkResetOutcome.RecoveryRequired
                            : NetworkResetOutcome.FailedNoChange,
                        reason,
                        "网络重置未完整完成。",
                        confirmedMutationOccurred).ConfigureAwait(false);
                }

                bool readBackCompleted;
                try
                {
                    using var verificationCts = new CancellationTokenSource(VerificationTimeout);
                    readBackCompleted = await _platform
                        .VerifyAsync(command, VerificationTimeout, verificationCts.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                    readBackCompleted = false;
                }
                // `ipconfig /displaydns` can only establish that a postflight query is
                // reachable.  It cannot prove that every cache entry was cleared, so never
                // turn that observation into an "effect verified" claim.
                var effectVerified = false;
                var stepReason = !readBackCompleted
                    ? "readback.mismatch"
                    : IsDnsCommand(command)
                        ? "dns.postflight-accessible"
                        : "step.readback-observed-manual-recovery";
                var stepMessage = !readBackCompleted
                    ? "命令返回成功，但独立只读回读失败或超时。"
                    : IsDnsCommand(command)
                        ? "DNS 刷新命令成功；后续只读查询可访问，但不能证明缓存内容已清空。"
                        : "只读回读可用，但 Winsock/TCP 重置效果无法证明可自动逆转。";
                var step = new NetworkResetStepReceipt(
                    index + 1,
                    command,
                    execution.ExitCode,
                    true,
                    readBackCompleted,
                    effectVerified,
                    stepReason,
                    stepMessage,
                    Hash(execution.StandardOutput),
                    Hash(execution.StandardError),
                    stepStarted,
                    _timeProvider.GetUtcNow());
                receipt = receipt with
                {
                    Phase = NetworkResetReceiptPhase.StepCompleted,
                    Steps = receipt.Steps.Append(step).ToArray(),
                    CurrentStep = null,
                    ManualInterventionRequired = true,
                    RebootRequired = plan.Profile != NetworkResetProfile.DnsCache,
                    RecoveryDecision = NetworkRecoveryDecision.RecoveryRequired,
                    RecoveryInstructions = RecoveryInstructions(
                        plan.Profile,
                        true,
                        NetworkResetOutcome.RecoveryRequired),
                    ReasonCode = stepReason,
                    Message = $"步骤 {index + 1}/{plan.Steps.Count} 已写入 durable receipt。"
                };
                persistenceError = await TrySaveReceiptAsync(receipt).ConfigureAwait(false);
                if (persistenceError is not null)
                {
                    return PersistenceFailure(receipt, persistenceError);
                }
                if (!readBackCompleted)
                {
                    return await CompleteAsync(
                        receipt,
                        NetworkResetOutcome.RecoveryRequired,
                        "readback.mismatch",
                        "网络状态无法确认，需要人工恢复检查。",
                        true).ConfigureAwait(false);
                }
            }

            var cancellationObservedAtCompletion = cancellationToken.IsCancellationRequested;
            var dnsOnly = plan.Profile == NetworkResetProfile.DnsCache;
            return await CompleteAsync(
                receipt,
                cancellationObservedAtCompletion
                    ? NetworkResetOutcome.RecoveryRequired
                    : dnsOnly
                        ? NetworkResetOutcome.SucceededVerified
                    : NetworkResetOutcome.AppliedRequiresRestartManualRecovery,
                cancellationObservedAtCompletion
                    ? "network.reset.completed-cancel-observed"
                    : "network.reset.completed",
                dnsOnly
                    ? cancellationObservedAtCompletion
                        ? "执行中的取消请求已记录；DNS 刷新最终状态不报告成功，请先人工检查名称解析后再决定是否生成新计划。"
                        : "DNS 刷新命令已成功退出，后续只读查询可访问；不能据此证明缓存内容已清空。"
                    : cancellationObservedAtCompletion
                        ? "重置步骤已完成；取消请求已记录，仍需手动重启与人工恢复准备。"
                        : "重置步骤已完成；需要手动重启，且只能依赖基线与人工网络重配置恢复。",
                true).ConfigureAwait(false);
        }
    }

    private async Task<string> CaptureIdentityAsync(
        NetworkResetProfile profile,
        CancellationToken cancellationToken)
    {
        var entries = await CaptureEntriesAsync(profile, cancellationToken).ConfigureAwait(false);
        return Hash(Serialize(entries));
    }

    private async Task<IReadOnlyList<NetworkResetSnapshotEntry>> CaptureEntriesAsync(
        NetworkResetProfile profile,
        CancellationToken cancellationToken)
    {
        var entries = new List<NetworkResetSnapshotEntry>();
        foreach (var command in CaptureCommands(profile))
        {
            var capture = await _platform
                .RunReadOnlyAsync(command, CommandTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!capture.ProcessStarted || capture.TimedOut || capture.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"network.reset.snapshot-reread-failed:{command}:{Classify(capture)}");
            }
            entries.Add(new NetworkResetSnapshotEntry(command, capture.ExitCode, Hash(capture.StandardOutput)));
        }
        return entries.AsReadOnly();
    }

    private async Task<NetworkResetReceipt> PersistNewTerminalAsync(
        NetworkResetPlan plan,
        NetworkResetOutcome outcome,
        string reasonCode,
        string message,
        bool mutationOccurred,
        DateTimeOffset? startedAtUtc = null)
    {
        var receipt = CreateExecutingReceipt(plan, startedAtUtc ?? _timeProvider.GetUtcNow());
        return await CompleteAsync(receipt, outcome, reasonCode, message, mutationOccurred).ConfigureAwait(false);
    }

    private async Task<NetworkResetReceipt> CompleteAsync(
        NetworkResetReceipt receipt,
        NetworkResetOutcome outcome,
        string reasonCode,
        string message,
        bool mutationOccurred)
    {
        var manualIntervention = mutationOccurred
            && outcome is NetworkResetOutcome.RecoveryRequired
                or NetworkResetOutcome.FailedPartial
                or NetworkResetOutcome.AppliedRequiresRestartManualRecovery;
        var rebootRequired = mutationOccurred
            && receipt.Profile != NetworkResetProfile.DnsCache
            && outcome is not NetworkResetOutcome.FailedNoChange
            && outcome is not NetworkResetOutcome.CancelledBeforeWrite;
        var state = manualIntervention
            ? NetworkResetReceiptState.ManualInterventionRequired
            : NetworkResetReceiptState.Completed;
        var recoveryDecision = outcome == NetworkResetOutcome.RecoveryRequired
            ? NetworkRecoveryDecision.RecoveryRequired
            : manualIntervention
                ? NetworkRecoveryDecision.ManualRecoveryOnly
                : NetworkRecoveryDecision.NotRequired;
        var terminal = receipt with
        {
            State = state,
            Phase = NetworkResetReceiptPhase.Completed,
            Outcome = outcome,
            ReasonCode = reasonCode,
            Message = message,
            MutationOccurred = mutationOccurred,
            ManualInterventionRequired = manualIntervention,
            RebootRequired = rebootRequired,
            RecoveryDecision = recoveryDecision,
            RecoveryInstructions = RecoveryInstructions(receipt.Profile, mutationOccurred, outcome),
            CurrentStep = null,
            CompletedAtUtc = _timeProvider.GetUtcNow()
        };
        var error = await TrySaveReceiptAsync(terminal).ConfigureAwait(false);
        return error is null ? terminal : PersistenceFailure(terminal, error);
    }

    private async Task<Exception?> TrySaveReceiptAsync(NetworkResetReceipt receipt)
    {
        try
        {
            using var cts = new CancellationTokenSource(PersistenceTimeout);
            await _receiptStore.SaveAsync(receipt, cts.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private NetworkResetReceipt PersistenceFailure(
        NetworkResetReceipt receipt,
        Exception exception)
    {
        var evidence = $"receipt={receipt.ReceiptId:N};plan={receipt.PlanId:N};error={exception.GetType().Name}";
        return receipt with
        {
            State = NetworkResetReceiptState.Unknown,
            Phase = NetworkResetReceiptPhase.Completed,
            Outcome = receipt.MutationOccurred
                ? NetworkResetOutcome.RecoveryRequired
                : NetworkResetOutcome.FailedNoChange,
            ReasonCode = receipt.MutationOccurred
                ? "receipt.persist-failed-manual-intervention"
                : "receipt.persist-failed-no-change",
            Message = $"durable receipt 持久化失败；停止后续操作：{evidence}",
            ManualInterventionRequired = receipt.MutationOccurred,
            RebootRequired = receipt.MutationOccurred && receipt.Profile != NetworkResetProfile.DnsCache,
            RecoveryDecision = receipt.MutationOccurred
                ? NetworkRecoveryDecision.RecoveryRequired
                : NetworkRecoveryDecision.NotRequired,
            RecoveryInstructions = RecoveryInstructions(
                receipt.Profile,
                receipt.MutationOccurred,
                receipt.MutationOccurred
                    ? NetworkResetOutcome.RecoveryRequired
                    : NetworkResetOutcome.FailedNoChange),
            CurrentStep = null,
            CompletedAtUtc = _timeProvider.GetUtcNow()
        };
    }

    private NetworkResetReceipt CreateExecutingReceipt(
        NetworkResetPlan plan,
        DateTimeOffset startedAtUtc) => new(
        Guid.NewGuid(),
        plan.ChangePlan.PlanId,
        plan.ChangePlan.PlanHash,
        plan.Profile,
        NetworkResetReceiptState.Executing,
        NetworkResetReceiptPhase.Prepared,
        NetworkResetOutcome.InProgress,
        "workflow.prepared",
        "计划已冻结；尚未执行网络写入。",
        false,
        false,
        false,
        plan.RecoveryDecision,
        RecoveryInstructions(plan.Profile, false, NetworkResetOutcome.InProgress),
        Array.Empty<NetworkResetStepReceipt>(),
        null,
        startedAtUtc,
        null);

    private NetworkResetReceipt Synthetic(
        NetworkResetProfile profile,
        NetworkResetOutcome outcome,
        string reasonCode,
        string message)
    {
        var now = _timeProvider.GetUtcNow();
        return new NetworkResetReceipt(
            Guid.NewGuid(),
            Guid.Empty,
            string.Empty,
            profile,
            NetworkResetReceiptState.Completed,
            NetworkResetReceiptPhase.Completed,
            outcome,
            reasonCode,
            message,
            false,
            false,
            false,
            NetworkRecoveryDecision.NotRequired,
            "未执行网络写入，无需恢复。",
            Array.Empty<NetworkResetStepReceipt>(),
            null,
            now,
            now);
    }

    private NetworkResetStepReceipt CreateUnknownStep(
        int index,
        NetworkResetCommand command,
        DateTimeOffset startedAtUtc,
        string error) => new(
        index + 1,
        command,
        -1,
        true,
        false,
        false,
        "process.exception-write-unknown",
        "平台执行异常，写入状态未知。",
        Hash(string.Empty),
        Hash(error),
        startedAtUtc,
        _timeProvider.GetUtcNow());

}

public sealed class DenyNetworkResetBaselineGate : INetworkResetBaselineGate
{
    public Task<NetworkResetBaselineDecision> AuthorizeAsync(
        NetworkResetPlan plan,
        CancellationToken cancellationToken) =>
        Task.FromResult(new NetworkResetBaselineDecision(
            false,
            "baseline.gate-not-connected",
            "首次安全基线门禁未接入，网络重置保持阻断。",
            null));
}

public sealed class DenyNetworkResetPlanStore : INetworkResetPlanStore
{
    public Task RegisterAsync(NetworkResetPlan plan, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("network.reset.plan-store-not-connected");

    public Task<NetworkResetPlan?> LoadAsync(Guid planId, CancellationToken cancellationToken) =>
        Task.FromResult<NetworkResetPlan?>(null);

    public Task<PlanClaimStatus> TryClaimAsync(
        Guid planId,
        string planHash,
        CancellationToken cancellationToken) =>
        Task.FromResult(PlanClaimStatus.Missing);
}

public sealed class DenyNetworkResetReceiptStore : INetworkResetReceiptStore
{
    public Task SaveAsync(NetworkResetReceipt receipt, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("network.reset.receipt-store-not-connected");

    public Task<IReadOnlyList<NetworkResetReceipt>> LoadPendingAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NetworkResetReceipt>>(Array.Empty<NetworkResetReceipt>());
}
