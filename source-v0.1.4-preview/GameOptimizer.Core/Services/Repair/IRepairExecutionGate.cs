namespace GameOptimizer.Core.Services.Repair;

/// <summary>
/// Mandatory authorization boundary invoked immediately before a repair delegate can mutate the system.
/// Implementations must fail closed for apply operations. Restore uses a distinct intent so that
/// recovery is not accidentally blocked by an apply-only baseline policy.
/// </summary>
public interface IRepairExecutionGate
{
    Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
        string repairItemId,
        RepairExecutionIntent intent,
        CancellationToken cancellationToken = default);
}

public enum RepairExecutionIntent
{
    Apply,
    Restore
}

public enum RepairExecutionGateDecisionKind
{
    Allowed,
    ReadonlyOnly,
    BlockedNeedBaseline
}

public sealed record RepairExecutionGateDecision(
    RepairExecutionGateDecisionKind Kind,
    string Reason,
    string? ReasonCode = null)
{
    public bool IsAllowed => Kind == RepairExecutionGateDecisionKind.Allowed;

    public static RepairExecutionGateDecision Allowed(string reason = "Authorized") =>
        new(RepairExecutionGateDecisionKind.Allowed, reason);
}
