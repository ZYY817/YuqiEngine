namespace GameOptimizer.Core.Services.ScheduledTasks;

public sealed record ScheduledTaskStateSnapshot(
    string TaskPath,
    bool Exists,
    bool Enabled,
    string State,
    string Xml);

public sealed record ScheduledTaskPlatformAssessment(
    bool Exists,
    bool HasRequiredPermission,
    bool ManagedByPolicy,
    string ReasonCode,
    string Details);

public sealed record ScheduledTaskMutationResult(
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message);

public interface IScheduledTaskAccessor
{
    Task<ScheduledTaskPlatformAssessment> ProbeAsync(string taskPath, CancellationToken cancellationToken);

    Task<ScheduledTaskStateSnapshot> ReadAsync(string taskPath, CancellationToken cancellationToken);

    Task<ScheduledTaskMutationResult> ApplyAsync(string taskPath, bool enabled, CancellationToken cancellationToken);

    Task<ScheduledTaskMutationResult> RestoreAsync(ScheduledTaskStateSnapshot original, CancellationToken cancellationToken);
}
