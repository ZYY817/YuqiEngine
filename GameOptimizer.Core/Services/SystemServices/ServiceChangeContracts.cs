namespace GameOptimizer.Core.Services.SystemServices;

public enum WindowsServiceRunState
{
    Stopped,
    Running,
    StartPending,
    StopPending,
    Paused,
    Unknown
}

public enum WindowsServiceStartType
{
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
    Unknown
}

public enum ServiceChangeAction
{
    EnsureRunning,
    EnsureStopped
}

public sealed record ServiceStateSnapshot(
    string ServiceName,
    bool Exists,
    WindowsServiceRunState RunState,
    WindowsServiceStartType StartType);

public sealed record ServiceChangeTarget(
    ServiceChangeAction Action,
    WindowsServiceStartType StartType);

public sealed record ServicePlatformAssessment(
    bool Exists,
    bool HasRequiredPermission,
    bool ManagedByPolicy,
    bool ProtectedService,
    string ReasonCode,
    string Details);

public sealed record ServicePlatformMutationResult(
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message);

public interface IWindowsServiceAccessor
{
    Task<ServicePlatformAssessment> ProbeAsync(string serviceName, CancellationToken cancellationToken);

    Task<ServiceStateSnapshot> ReadAsync(string serviceName, CancellationToken cancellationToken);

    Task<ServicePlatformMutationResult> ApplyAsync(
        string serviceName,
        ServiceChangeTarget target,
        CancellationToken cancellationToken);

    Task<ServicePlatformMutationResult> RestoreAsync(
        ServiceStateSnapshot snapshot,
        CancellationToken cancellationToken);
}
