using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services;

public interface IUpdateCheckService
{
    string CurrentVersion { get; }
    Task<OperationResult<UpdateInfo?>> CheckForUpdateAsync(bool includePreRelease = false, CancellationToken cancellationToken = default);
    Task<OperationResult<UpdateInfo?>> CheckForUpdateAsync(string? repository, bool includePreRelease = false, CancellationToken cancellationToken = default);
    Task<OperationResult<string>> DownloadUpdatePackageAsync(string downloadUrl, string version, CancellationToken cancellationToken = default);
}
