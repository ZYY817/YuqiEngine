using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Contracts.Changes;

/// <summary>
/// Mandatory authorization boundary invoked before any change plan is executed.
/// Implementations must fail closed for Apply. Restore uses a distinct intent so
/// that recovery is not accidentally blocked by an apply-only baseline policy.
/// </summary>
public interface IChangeExecutionGate
{
    Task<ChangeExecutionGateDecision> EnsureCanExecuteAsync(
        string capabilityId,
        ChangeExecutionIntent intent,
        CancellationToken cancellationToken = default);
}

public enum ChangeExecutionIntent
{
    Apply,
    Restore
}

public enum ChangeExecutionGateDecisionKind
{
    Allowed,
    ReadonlyOnly,
    BlockedNeedBaseline
}

public sealed record ChangeExecutionGateDecision(
    ChangeExecutionGateDecisionKind Kind,
    string Reason)
{
    public bool IsAllowed => Kind == ChangeExecutionGateDecisionKind.Allowed;

    public static ChangeExecutionGateDecision Allowed(string reason = "Authorized") =>
        new(ChangeExecutionGateDecisionKind.Allowed, reason);
}
