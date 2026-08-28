using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Memory;

/// <summary>
/// The only application-facing Memory mutation owner. All mutation requests are
/// delegated to the central orchestrator; it never opens a process or calls a
/// native memory API itself.
/// </summary>
public sealed class MemoryCleaningOwner : IMemoryCleaningOwner
{
    private readonly IChangeOrchestrator _orchestrator;
    private readonly IChangeConfirmationAuthority _confirmationAuthority;

    public MemoryCleaningOwner(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority confirmationAuthority)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _confirmationAuthority = confirmationAuthority ?? throw new ArgumentNullException(nameof(confirmationAuthority));
    }

    public Task<MemoryProcessTargetResult> CaptureWorkingSetTargetAsync(
        int processId,
        CancellationToken cancellationToken = default) =>
        CaptureProcessTargetAsync(MemoryCleaningCapabilityKind.WorkingSetTrim, processId, cancellationToken);

    public async Task<MemoryProcessTargetResult> CaptureProcessTargetAsync(
        MemoryCleaningCapabilityKind capability,
        int processId,
        CancellationToken cancellationToken = default)
    {
        if (capability is not (MemoryCleaningCapabilityKind.WorkingSetTrim or MemoryCleaningCapabilityKind.ProcessTermination)
            || processId <= 4)
        {
            return new(false, "memory.process-not-eligible", "该进程不是可操作的用户进程。");
        }

        try
        {
            var scope = new TargetScope(TargetScopeKind.Process, processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var observation = await _orchestrator.ObserveAsync(
                MemoryCleaningCapabilityIds.For(capability),
                scope,
                cancellationToken).ConfigureAwait(false);
            if (observation is null)
            {
                return new(false, "memory.capability-unavailable", "进程结束能力未接入，未执行任何操作。");
            }

            var observed = MemoryCleaningPayloadCodec.DeserializeObserved(observation.Payload);
            if (observed.ProcessIdentity?.IsUsable != true)
            {
                return new(false, "memory.process-identity-unavailable", "无法完整核验目标进程身份，未执行任何操作。");
            }

            return new(
                true,
                "memory.process-identity-captured",
                "已读取目标进程身份，执行前会再次核验。",
                capability == MemoryCleaningCapabilityKind.ProcessTermination
                    ? MemoryCleaningRequest.ForProcessTermination(observed.ProcessIdentity)
                    : MemoryCleaningRequest.ForWorkingSet(observed.ProcessIdentity));
        }
        catch (ArgumentException)
        {
            return new(false, "memory.process-exited", "目标进程已在执行前退出，未对陈旧 PID 执行任何操作。");
        }
        catch (OperationCanceledException)
        {
            return new(false, "memory.target-capture-cancelled", "目标进程读取已取消，未执行任何操作。");
        }
        catch (Exception)
        {
            return new(false, "memory.target-capture-failed", "无法读取目标进程或其身份，未执行任何操作。");
        }
    }

    public async Task<MemoryCleaningResult> ReadStatusAsync(
        MemoryCleaningRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.IsValid)
        {
            return Blocked("memory.target-invalid", "Memory 目标身份不完整，未读取可执行状态。", MemoryCleaningResultKind.FailClosed);
        }

        try
        {
            var observation = await _orchestrator.ObserveAsync(
                MemoryCleaningCapabilityIds.For(request.Capability),
                request.Scope,
                cancellationToken).ConfigureAwait(false);

            return observation is null
                ? Blocked("memory.capability-unavailable", "Memory typed capability 未接入，当前仅允许只读监控。", MemoryCleaningResultKind.ReadOnly)
                : new(
                    MemoryCleaningResultKind.ReadOnly,
                    "memory.read-only",
                    "Memory 当前状态已读取；没有执行任何写入。",
                    Observation: observation);
        }
        catch (OperationCanceledException)
        {
            return Blocked("memory.read-cancelled", "Memory 状态读取已取消。", MemoryCleaningResultKind.Cancelled);
        }
        catch (Exception ex)
        {
            return Blocked("memory.read-failed", Sanitize(ex.Message), MemoryCleaningResultKind.Failed);
        }
    }

    public async Task<MemoryCleaningResult> PrepareAsync(
        MemoryCleaningRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.IsValid)
        {
            return Blocked("memory.target-invalid", "Memory 目标身份不完整，拒绝生成写入计划。", MemoryCleaningResultKind.FailClosed);
        }

        try
        {
            var preparation = await _orchestrator.PrepareAsync(
                new ChangeRequest(
                    MemoryCleaningCapabilityIds.For(request.Capability),
                    request.Scope,
                    MemoryCleaningPayloadCodec.SerializeTarget(request)),
                cancellationToken).ConfigureAwait(false);

            return preparation.Status switch
            {
                ChangePreparationStatus.Ready when preparation.Plan is not null => new(
                    MemoryCleaningResultKind.Ready,
                    preparation.ReasonCode,
                    preparation.Message,
                    preparation.Plan,
                    Observation: preparation.Current),
                ChangePreparationStatus.NoChange => new(
                    MemoryCleaningResultKind.NoChange,
                    preparation.ReasonCode,
                    preparation.Message,
                    Observation: preparation.Current),
                _ => Blocked(
                    preparation.ReasonCode,
                    preparation.Message,
                    MapBlockedKind(preparation.ReasonCode))
            };
        }
        catch (OperationCanceledException)
        {
            return Blocked("memory.prepare-cancelled", "Memory 计划准备已取消，未执行任何写入。", MemoryCleaningResultKind.Cancelled);
        }
        catch (Exception ex)
        {
            return Blocked("memory.prepare-failed", Sanitize(ex.Message), MemoryCleaningResultKind.Failed);
        }
    }

    public async Task<MemoryCleaningResult> RunInteractiveAsync(
        MemoryCleaningRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        if (prepared.Kind != MemoryCleaningResultKind.Ready || prepared.Plan is null)
        {
            return prepared;
        }

        ConfirmationToken? confirmation;
        try
        {
            confirmation = await _confirmationAuthority.RequestAsync(
                prepared.Plan,
                "确认内存清理",
                BuildConfirmationSummary(request),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Blocked("memory.confirmation-cancelled", "确认已取消，未执行任何写入。", MemoryCleaningResultKind.Cancelled, prepared.Plan);
        }

        if (confirmation is null)
        {
            return Blocked("memory.confirmation-denied", "未取得当前计划的显式确认，未执行任何写入。", MemoryCleaningResultKind.Denied, prepared.Plan);
        }

        return await ExecuteAsync(prepared.Plan, confirmation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryCleaningResult> ExecuteAsync(
        PreparedChangePlan plan,
        ConfirmationToken confirmation,
        CancellationToken cancellationToken = default)
    {
        if (!IsMemoryCapability(plan.Change.CapabilityId))
        {
            return Blocked("memory.plan-capability-mismatch", "计划不是 Memory typed capability，拒绝执行。", MemoryCleaningResultKind.FailClosed, plan);
        }

        try
        {
            var receipt = await _orchestrator.ExecuteAsync(plan, confirmation, cancellationToken).ConfigureAwait(false);
            return FromReceipt(receipt.Operation, plan);
        }
        catch (OperationCanceledException)
        {
            return Blocked("memory.execute-cancelled", "Memory 执行已取消；没有报告成功。", MemoryCleaningResultKind.Cancelled, plan);
        }
        catch (Exception ex)
        {
            return Blocked("memory.execute-failed", Sanitize(ex.Message), MemoryCleaningResultKind.Failed, plan);
        }
    }

    public async Task<MemoryCleaningResult> RestoreAsync(
        RestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var receipt = await _orchestrator.RestoreAsync(request, cancellationToken).ConfigureAwait(false);
            return FromReceipt(receipt.Operation, null);
        }
        catch (OperationCanceledException)
        {
            return Blocked("memory.restore-cancelled", "Memory 恢复已取消；原始 receipt 保留，未报告成功。", MemoryCleaningResultKind.Cancelled);
        }
        catch (Exception ex)
        {
            return Blocked("memory.restore-failed", Sanitize(ex.Message), MemoryCleaningResultKind.Failed);
        }
    }

    private static MemoryCleaningResult FromReceipt(
        OperationReceipt receipt,
        PreparedChangePlan? plan) =>
        new(
            MapOutcome(receipt.Outcome),
            receipt.ReasonCode,
            receipt.Message,
            plan,
            receipt);

    private static MemoryCleaningResultKind MapOutcome(ChangeOutcome outcome) => outcome switch
    {
        ChangeOutcome.SucceededVerified => MemoryCleaningResultKind.SucceededVerified,
        ChangeOutcome.RestoredVerified => MemoryCleaningResultKind.RestoredVerified,
        ChangeOutcome.NoChange => MemoryCleaningResultKind.NoChange,
        ChangeOutcome.Denied => MemoryCleaningResultKind.Denied,
        ChangeOutcome.CancelledBeforeWrite or ChangeOutcome.CancelledAfterPartialWrite => MemoryCleaningResultKind.Cancelled,
        ChangeOutcome.Conflict => MemoryCleaningResultKind.Conflict,
        ChangeOutcome.RecoveryRequired => MemoryCleaningResultKind.RecoveryRequired,
        ChangeOutcome.Blocked => MemoryCleaningResultKind.FailClosed,
        _ => MemoryCleaningResultKind.Failed
    };

    private static MemoryCleaningResultKind MapBlockedKind(string reasonCode) =>
        reasonCode.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || reasonCode.Contains("denied", StringComparison.OrdinalIgnoreCase)
            ? MemoryCleaningResultKind.Denied
            : MemoryCleaningResultKind.FailClosed;

    private static bool IsMemoryCapability(string capabilityId) =>
        string.Equals(capabilityId, MemoryCleaningCapabilityIds.WorkingSetTrim, StringComparison.OrdinalIgnoreCase)
        || string.Equals(capabilityId, MemoryCleaningCapabilityIds.ProcessTermination, StringComparison.OrdinalIgnoreCase)
        || string.Equals(capabilityId, MemoryCleaningCapabilityIds.StandbyListPurge, StringComparison.OrdinalIgnoreCase);

    private static MemoryCleaningResult Blocked(
        string reasonCode,
        string message,
        MemoryCleaningResultKind kind,
        PreparedChangePlan? plan = null) =>
        new(kind, reasonCode, message, plan);

    private static string BuildConfirmationSummary(MemoryCleaningRequest request) =>
        request.Capability switch
        {
            MemoryCleaningCapabilityKind.WorkingSetTrim => $"目标 PID：{request.ProcessIdentity!.ProcessId}\n这是高级不可回滚操作：仅释放该进程当前工作集。执行前将重新核验进程身份；执行后会读取工作集变化。",
            MemoryCleaningCapabilityKind.ProcessTermination => $"目标 PID：{request.ProcessIdentity!.ProcessId}\n这会强制结束所选进程，未保存的数据可能丢失且无法恢复。执行前将重新核验进程身份，执行后必须确认该实例已经退出。",
            _ => $"目标机器：{request.MachineIdentity}\nStandby List 无法提供可持久化的原始材料时将阻断，不会伪造 RecoveryId。"
        };

    private static string Sanitize(string message) =>
        string.IsNullOrWhiteSpace(message) ? "Memory 操作失败，未执行或未确认任何写入。" : message;
}
