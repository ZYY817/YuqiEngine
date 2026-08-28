using GameOptimizer.Core.Services.Startup;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Compatibility catalog operation. It delegates only when the typed ScheduledTask owner is supplied;
/// legacy command execution is never used. Existing catalog construction remains fail-closed.
/// </summary>
public sealed class ScheduledTaskOperation : IOptimizationOperation
{
    private readonly IScheduledTaskOwner? _owner;

    public ScheduledTaskOperation(string id, string title, string description, string category, string taskPath, bool targetEnabled = false)
        : this(id, title, description, category, taskPath, targetEnabled, null) { }

    public ScheduledTaskOperation(string id, string title, string description, string category, string taskPath, bool targetEnabled, IScheduledTaskOwner? owner)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        TaskPath = taskPath;
        TargetEnabled = targetEnabled;
        _owner = owner;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public string TaskPath { get; }
    public bool TargetEnabled { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.ScheduledTask;
    public string? ManualPath => "taskschd.msc";
    public string? TargetName => TaskPath;
    public string? TargetValue => TargetEnabled ? "Enabled" : "Disabled";
    public string? ManualSteps => "Use the controlled ScheduledTask workflow; direct task commands are not exposed.";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(
        new OperationStatus(false, _owner is null
            ? "Typed ScheduledTask owner is not connected."
            : "Status requires a prepared typed task snapshot; catalog probing is fail-closed."));

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        if (_owner is null) return Blocked("Typed ScheduledTask owner is not connected.");
        var identity = Identity();
        var prepared = await _owner.PrepareAsync(identity, TargetEnabled ? ManagedAutostartAction.Enable : ManagedAutostartAction.Disable, cancellationToken).ConfigureAwait(false);
        if (!prepared.Success || prepared.Plan is null) return Blocked(prepared.Message);
        return Blocked("Explicit user confirmation is required; catalog execution cannot auto-confirm a scheduled-task plan.");
    }

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Blocked("Restore requires a durable typed receipt ID; legacy operation snapshots are not accepted."));

    private ManagedAutostartIdentity Identity() => new(
        ManagedAutostartKind.ScheduledTask,
        "machine",
        TaskPath);

    private static OperationInvokeResult Blocked(string message) => new(false, false, message);
}
