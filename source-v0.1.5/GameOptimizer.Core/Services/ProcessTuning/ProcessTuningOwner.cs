using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.ProcessTuning;

/// <summary>
/// Single application-facing runtime process Owner. It never opens a Process handle;
/// the capability/platform pair performs that work under the centralized safety chain.
/// </summary>
public sealed class ProcessTuningOwner : IProcessTuningOwner
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _processLocks = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Guid> _activeReceiptsByPid = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _activePidsByReceipt = new();
    private readonly IChangeOrchestrator _orchestrator;
    private readonly IChangeConfirmationAuthority _confirmationAuthority;

    public ProcessTuningOwner(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority confirmationAuthority)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _confirmationAuthority = confirmationAuthority ?? throw new ArgumentNullException(nameof(confirmationAuthority));
    }

    public async Task<ProcessTuningResult> PrepareAsync(
        ProcessTuningRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsRequestValid(request, out var validationCode, out var validationMessage))
        {
            return new(ProcessTuningResultKind.Blocked, validationCode, validationMessage);
        }

        PreparationResult preparation;
        try
        {
            preparation = await _orchestrator.PrepareAsync(
                new ChangeRequest(
                    ProcessTuningCapability.CapabilityIdFor(request.Target.Operation),
                    new TargetScope(TargetScopeKind.Process, request.Target.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ProcessTuningCapability.SerializeTarget(request.Target)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(ProcessTuningResultKind.Cancelled, "process.prepare-cancelled", "准备期间已取消，未执行进程写入。");
        }

        return preparation.Status switch
        {
            ChangePreparationStatus.Ready when preparation.Plan is not null => new(
                ProcessTuningResultKind.Ready,
                preparation.ReasonCode,
                preparation.Message,
                preparation.Plan),
            ChangePreparationStatus.NoChange => new(
                ProcessTuningResultKind.NoChange,
                preparation.ReasonCode,
                preparation.Message),
            _ => new(Map(preparation.ReasonCode, ChangeOutcome.Blocked), preparation.ReasonCode, preparation.Message)
        };
    }

    public async Task<ProcessTuningResult> RunInteractiveAsync(
        ProcessTuningRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request?.Target is null)
            return new(ProcessTuningResultKind.Blocked, "process.target-invalid", "未提供进程目标。");

        var gate = _processLocks.GetOrAdd(request.Target.ProcessId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        if (_activeReceiptsByPid.TryGetValue(request.Target.ProcessId, out var activeReceipt))
            return new(ProcessTuningResultKind.Conflict, "process.active-receipt", $"该 PID 已有未恢复的执行记录 {activeReceipt}，请先恢复。");
        var prepared = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        if (prepared.Kind != ProcessTuningResultKind.Ready || prepared.Plan is null)
        {
            return prepared;
        }

        ConfirmationToken? confirmation;
        try
        {
            confirmation = await _confirmationAuthority.RequestAsync(
                prepared.Plan,
                "确认调整进程运行时状态",
                BuildConfirmationSummary(request),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(ProcessTuningResultKind.Cancelled, "process.confirmation-cancelled", "确认期间已取消，未执行进程写入。", prepared.Plan);
        }

        if (confirmation is null)
        {
            return new(ProcessTuningResultKind.Denied, "process.confirmation-denied", "未取得当前计划的显式确认，未执行进程写入。", prepared.Plan);
        }

        var result = await ExecuteAsync(prepared.Plan, confirmation, cancellationToken).ConfigureAwait(false);
        if (result.IsTerminalSuccess && result.Receipt is not null)
        {
            _activeReceiptsByPid[request.Target.ProcessId] = result.Receipt.ReceiptId;
            _activePidsByReceipt[result.Receipt.ReceiptId] = request.Target.ProcessId;
        }
        return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ProcessTuningResult> ExecuteAsync(
        PreparedChangePlan plan,
        ConfirmationToken confirmation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var receipt = await _orchestrator.ExecuteAsync(plan, confirmation, cancellationToken).ConfigureAwait(false);
            return FromReceipt(receipt.Operation, plan);
        }
        catch (OperationCanceledException)
        {
            return new(ProcessTuningResultKind.Cancelled, "process.execute-cancelled", "执行已取消；不得假定目标状态，需依据 durable receipt 核对。", plan);
        }
    }

    public async Task<ProcessTuningResult> RestoreAsync(
        RestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var receipt = await _orchestrator.RestoreAsync(request, cancellationToken).ConfigureAwait(false);
            var result = FromReceipt(receipt.Operation, null);
            if (result.Kind is ProcessTuningResultKind.RestoredVerified or ProcessTuningResultKind.NotApplicable
                && _activePidsByReceipt.TryRemove(request.ReceiptId, out var pid))
                _activeReceiptsByPid.TryRemove(pid, out _);
            return result;
        }
        catch (OperationCanceledException)
        {
            return new(ProcessTuningResultKind.Cancelled, "process.restore-cancelled", "恢复已取消；原 receipt 保留，未报告恢复成功。");
        }
    }

    private static ProcessTuningResult FromReceipt(OperationReceipt receipt, PreparedChangePlan? plan) =>
        new(Map(receipt.ReasonCode, receipt.Outcome), receipt.ReasonCode, receipt.Message, plan, receipt);

    private static ProcessTuningResultKind Map(string reasonCode, ChangeOutcome outcome)
    {
        if (reasonCode.StartsWith("process.exited", StringComparison.Ordinal)
            || reasonCode.StartsWith("process.not-applicable", StringComparison.Ordinal)
            || reasonCode.StartsWith("process.ppl", StringComparison.Ordinal))
        {
            return ProcessTuningResultKind.NotApplicable;
        }

        // The orchestrator is the canonical source for execution conflicts. Do not
        // classify static unsupported topology/architecture reasons as runtime drift.
        if (outcome == ChangeOutcome.Conflict
            || reasonCode.EndsWith(".drift", StringComparison.Ordinal)
            || reasonCode.EndsWith(".replayed", StringComparison.Ordinal))
        {
            return ProcessTuningResultKind.Conflict;
        }

        return outcome switch
        {
            ChangeOutcome.SucceededVerified => ProcessTuningResultKind.SucceededVerified,
            ChangeOutcome.RestoredVerified => ProcessTuningResultKind.RestoredVerified,
            ChangeOutcome.NoChange => ProcessTuningResultKind.NoChange,
            ChangeOutcome.Denied => ProcessTuningResultKind.Denied,
            ChangeOutcome.CancelledBeforeWrite or ChangeOutcome.CancelledAfterPartialWrite => ProcessTuningResultKind.Cancelled,
            ChangeOutcome.Conflict => ProcessTuningResultKind.Conflict,
            ChangeOutcome.RecoveryRequired => ProcessTuningResultKind.RecoveryRequired,
            ChangeOutcome.Blocked => ProcessTuningResultKind.Blocked,
            _ => ProcessTuningResultKind.Failed
        };
    }

    private static bool IsRequestValid(ProcessTuningRequest? request, out string code, out string message)
    {
        if (request?.Target is null || request.Target.ProcessId <= 4)
        {
            code = "process.target-invalid";
            message = "仅允许对明确的非系统 PID 建立进程调优计划。";
            return false;
        }

        if (request.Target.Operation == ProcessTuningOperation.Affinity && request.Target.AffinityMask is not > 0)
        {
            code = "process.affinity-mask-invalid";
            message = "亲和性掩码必须为非零正值。";
            return false;
        }

        if (request.Target.Operation == ProcessTuningOperation.Priority
            && request.Target.Priority is not (System.Diagnostics.ProcessPriorityClass.Normal
                or System.Diagnostics.ProcessPriorityClass.AboveNormal
                or System.Diagnostics.ProcessPriorityClass.High))
        {
            code = "process.priority-unsupported";
            message = "仅允许可回读的 Normal、AboveNormal 或 High 运行时优先级。";
            return false;
        }

        code = string.Empty;
        message = string.Empty;
        return true;
    }

    private static string BuildConfirmationSummary(ProcessTuningRequest request) => request.Target.Operation switch
    {
        ProcessTuningOperation.Affinity => $"目标 PID：{request.Target.ProcessId}\n运行时亲和性掩码：0x{request.Target.AffinityMask:X}\n执行前将重新核验进程 identity、拓扑和当前状态。",
        ProcessTuningOperation.Priority => $"目标 PID：{request.Target.ProcessId}\n运行时优先级：{request.Target.Priority}\n执行前将重新核验进程 identity 和当前状态。",
        _ => $"目标 PID：{request.Target.ProcessId}\nIdealProcessor 未具备可靠回读与恢复链，必须保持阻断。"
    };
}
