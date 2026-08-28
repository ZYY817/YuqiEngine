using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.Services.Snapshot;

public interface IComprehensiveBackupService
{
    Task<ComprehensiveBackupPackageInfo> CreatePackageAsync(
        string name,
        IProgress<ComprehensiveBackupProgress>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ComprehensiveBackupPackageInfo>> GetPackagesAsync(CancellationToken ct = default);

    Task<ComprehensiveBackupRestorePreparation> PrepareTypedRestoreAsync(
        string packageId,
        CancellationToken ct = default);
}

public sealed record ComprehensiveBackupProgress(
    string Stage,
    int Completed,
    int Total,
    string Message);
