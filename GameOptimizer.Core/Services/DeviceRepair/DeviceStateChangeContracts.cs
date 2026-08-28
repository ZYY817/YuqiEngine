namespace GameOptimizer.Core.Services.DeviceRepair;

public sealed record DeviceStateSnapshot(
    bool Found,
    string DeviceId,
    string Name,
    bool IsEnabled);

public sealed record DevicePlatformAssessment(
    bool HasRequiredPermission,
    string ReasonCode,
    string Details);

public sealed record DeviceMutationResult(
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message);

public interface IDeviceStateAccessor
{
    Task<DevicePlatformAssessment> ProbeAsync(CancellationToken cancellationToken);

    Task<DeviceStateSnapshot> ReadAsync(string deviceNamePattern, CancellationToken cancellationToken);

    Task<DeviceMutationResult> ApplyAsync(string deviceId, bool enabled, CancellationToken cancellationToken);

    Task<DeviceMutationResult> RestoreAsync(DeviceStateSnapshot original, CancellationToken cancellationToken);
}
