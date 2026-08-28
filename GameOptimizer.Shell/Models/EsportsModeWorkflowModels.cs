// File: GameOptimizer.Shell/Models/EsportsModeWorkflowModels.cs
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Shell.Models;

public enum EsportsWorkflowAction
{
    Apply,
    Restore
}

public enum EsportsProbeOutcome
{
    Detected,
    MissingOperation,
    Failed,
    TimedOut,
    Cancelled
}

public enum EsportsExecutionOutcome
{
    Succeeded,
    Failed,
    Blocked,
    TimedOut,
    Cancelled
}

public enum EsportsFailureKind
{
    None,
    InvalidPlan,
    PermissionDenied,
    BackupUnavailable,
    BackupFailed,
    BackupTimedOut,
    SafetyConfirmationUnavailable,
    SafetyConfirmationDeclined,
    SafetyConfirmationTimedOut,
    Cancelled,
    ExecutionFailed,
    ExecutionTimedOut,
    ReadbackFailed,
    ReadbackTimedOut,
    BlockedByPriorFailure
}

public sealed record EsportsWorkflowOptions(
    TimeSpan BackupTimeout,
    TimeSpan DetectionTimeout,
    TimeSpan OperationTimeout,
    TimeSpan ReadbackTimeout)
{
    public static EsportsWorkflowOptions Default { get; } = new(
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromSeconds(15));

    public void Validate()
    {
        if (BackupTimeout <= TimeSpan.Zero ||
            DetectionTimeout <= TimeSpan.Zero ||
            OperationTimeout <= TimeSpan.Zero ||
            ReadbackTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BackupTimeout), "电竞模式超时必须大于零。");
        }
    }
}

public sealed record EsportsProbeResult(
    string OperationId,
    EsportsProbeOutcome Outcome,
    bool? IsApplied,
    string Message);

public sealed class EsportsDetectionReport
{
    public EsportsDetectionReport(IReadOnlyList<EsportsProbeResult> items)
    {
        Items = items;
        States = items
            .Where(static item => item.Outcome == EsportsProbeOutcome.Detected && item.IsApplied.HasValue)
            .ToDictionary(static item => item.OperationId, static item => item.IsApplied!.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<EsportsProbeResult> Items { get; }

    public IReadOnlyDictionary<string, bool> States { get; }

    public bool IsComplete => Items.All(static item => item.Outcome == EsportsProbeOutcome.Detected);

    /// <summary>
    /// Every registered operation was read, while absent operations were conclusively marked
    /// unavailable. Timeout, failure and cancellation remain blocking states.
    /// </summary>
    public bool IsReadyForPlanning => Items.All(static item =>
        item.Outcome is EsportsProbeOutcome.Detected or EsportsProbeOutcome.MissingOperation);

    public bool WasCancelled => Items.Any(static item => item.Outcome == EsportsProbeOutcome.Cancelled);
}

public sealed record EsportsOperationPlanItem(
    string OperationId,
    string ModuleKey,
    string Title,
    bool RequiresAdministrator);

public sealed record EsportsOperationResult(
    string OperationId,
    string ModuleKey,
    string Title,
    EsportsExecutionOutcome Outcome,
    EsportsFailureKind FailureKind,
    string Message,
    GameOptimizer.Core.Optimization.OperationStatus? ReadbackStatus = null)
{
    public bool Success => Outcome == EsportsExecutionOutcome.Succeeded;
}

public sealed class EsportsOperationPlan
{
    public EsportsOperationPlan(
        EsportsWorkflowAction action,
        IReadOnlyList<string> requestedOperationIds,
        IReadOnlyList<EsportsOperationPlanItem> items,
        IReadOnlyList<EsportsOperationResult> rejectedItems)
    {
        Action = action;
        RequestedOperationIds = requestedOperationIds;
        Items = items;
        RejectedItems = rejectedItems;
    }

    public EsportsWorkflowAction Action { get; }

    public IReadOnlyList<string> RequestedOperationIds { get; }

    public IReadOnlyList<EsportsOperationPlanItem> Items { get; }

    public IReadOnlyList<EsportsOperationResult> RejectedItems { get; }

    public bool CanExecute => Items.Count > 0 && RejectedItems.Count == 0;
}

public sealed record EsportsBackupGateResult(
    bool Allowed,
    EsportsFailureKind FailureKind,
    string Message)
{
    public static EsportsBackupGateResult Passed(string message) => new(true, EsportsFailureKind.None, message);

    public static EsportsBackupGateResult Blocked(EsportsFailureKind failureKind, string message) => new(false, failureKind, message);
}

public sealed record EsportsWorkflowProgress(
    int Current,
    int Total,
    string OperationId,
    string Title);

public sealed class EsportsWorkflowReport
{
    public EsportsWorkflowReport(
        EsportsWorkflowAction action,
        IReadOnlyList<string> plannedOperationIds,
        IReadOnlyList<EsportsOperationResult> results,
        EsportsBackupGateResult backupGate)
    {
        Action = action;
        PlannedOperationIds = plannedOperationIds;
        Results = results;
        BackupGate = backupGate;
    }

    public EsportsWorkflowAction Action { get; }

    public IReadOnlyList<string> PlannedOperationIds { get; }

    public IReadOnlyList<EsportsOperationResult> Results { get; }

    public EsportsBackupGateResult BackupGate { get; }

    public bool Success => Results.Count > 0 && Results.All(static result => result.Success);

    public bool WasCancelled => Results.Any(static result => result.Outcome == EsportsExecutionOutcome.Cancelled);
}
