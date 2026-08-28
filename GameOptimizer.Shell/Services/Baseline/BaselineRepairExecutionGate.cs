using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// Adapts the single Shell baseline authority to the Core repair execution boundary.
/// Restore deliberately bypasses the apply-only baseline decision; its identity and permission
/// checks remain the responsibility of the recovery owner that produced the restore item.
/// </summary>
public sealed class BaselineRepairExecutionGate : IRepairExecutionGate
{
    private readonly IBaselineGateService _baselineGateService;

    public BaselineRepairExecutionGate(IBaselineGateService baselineGateService)
    {
        _baselineGateService = baselineGateService ?? throw new ArgumentNullException(nameof(baselineGateService));
    }

    public async Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
        string repairItemId,
        RepairExecutionIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repairItemId);
        cancellationToken.ThrowIfCancellationRequested();

        if (intent == RepairExecutionIntent.Restore)
        {
            return RepairExecutionGateDecision.Allowed("Recovery intent is not blocked by the apply baseline gate.");
        }

        BaselineGateDecision decision = await _baselineGateService
            .EnsureCanOptimizeAsync($"repair:{repairItemId}", cancellationToken)
            .ConfigureAwait(false);

        if (decision.CanProceed)
        {
            return RepairExecutionGateDecision.Allowed(decision.Reason);
        }

        return decision.Kind == BaselineGateDecisionKind.ReadonlyOnly
            ? new(RepairExecutionGateDecisionKind.ReadonlyOnly, decision.Reason)
            : new(RepairExecutionGateDecisionKind.BlockedNeedBaseline, decision.Reason);
    }
}
