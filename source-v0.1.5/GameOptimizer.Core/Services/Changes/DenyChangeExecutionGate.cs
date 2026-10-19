using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Changes;

/// <summary>
/// Fail-closed default execution gate used when no baseline authority has been composed
/// into the container. Apply is always blocked; Restore uses a distinct intent so that
/// recovery is never locked out by an apply-only default. The Shell overrides this with
/// <c>BaselineChangeExecutionGate</c>, which delegates to the live baseline authority.
/// </summary>
public sealed class DenyChangeExecutionGate : IChangeExecutionGate
{
    public Task<ChangeExecutionGateDecision> EnsureCanExecuteAsync(
        string capabilityId,
        ChangeExecutionIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        cancellationToken.ThrowIfCancellationRequested();

        if (intent == ChangeExecutionIntent.Restore)
        {
            return Task.FromResult(
                ChangeExecutionGateDecision.Allowed("Recovery intent is not blocked by the fail-closed default gate."));
        }

        return Task.FromResult(new ChangeExecutionGateDecision(
            ChangeExecutionGateDecisionKind.BlockedNeedBaseline,
            "No baseline execution gate is composed; centralized writes fail closed."));
    }
}
