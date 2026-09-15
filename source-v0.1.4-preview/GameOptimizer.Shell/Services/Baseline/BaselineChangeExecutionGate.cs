using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// Adapts the single Shell baseline authority to the Core centralized change execution boundary.
/// Apply requires either a verified baseline or a per-operation risk acknowledgement. Restore uses
/// a distinct intent so that recovery is not accidentally blocked by an apply-only baseline policy.
/// </summary>
public sealed class BaselineChangeExecutionGate : IChangeExecutionGate
{
    private readonly IBaselineGateService _baselineGateService;

    public BaselineChangeExecutionGate(IBaselineGateService baselineGateService)
    {
        _baselineGateService = baselineGateService ?? throw new ArgumentNullException(nameof(baselineGateService));
    }

    public async Task<ChangeExecutionGateDecision> EnsureCanExecuteAsync(
        string capabilityId,
        ChangeExecutionIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        cancellationToken.ThrowIfCancellationRequested();

        if (intent == ChangeExecutionIntent.Restore)
        {
            return ChangeExecutionGateDecision.Allowed("Recovery intent is not blocked by the apply baseline gate.");
        }

        BaselineGateDecision decision = await _baselineGateService
            .EnsureCanOptimizeAsync($"change:{capabilityId}", cancellationToken)
            .ConfigureAwait(false);

        if (decision.CanProceed)
        {
            return ChangeExecutionGateDecision.Allowed(decision.Reason);
        }

        return decision.Kind == BaselineGateDecisionKind.ReadonlyOnly
            ? new(ChangeExecutionGateDecisionKind.ReadonlyOnly, decision.Reason)
            : new(ChangeExecutionGateDecisionKind.BlockedNeedBaseline, decision.Reason);
    }
}
