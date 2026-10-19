using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Services.Snapshot
{
    public interface ISnapshotService
    {
        Task<OptimizationSnapshot> CreateSnapshotAsync(string name, string? description = null, CancellationToken ct = default);
        Task<SnapshotCoverageReport> GetCoverageReportAsync(CancellationToken ct = default);
        Task<SnapshotRestorePlan> PrepareRestoreAsync(string snapshotId, SnapshotRestoreKind kind, CancellationToken ct = default);
        Task<SnapshotRestoreResult> ExecuteRestoreAsync(SnapshotRestorePlan plan, CancellationToken ct = default);
        Task<RegistryBackupRestoreResult> ExecuteRegistryRestoreAsync(SnapshotRestorePlan plan, CancellationToken ct = default);
        Task<SnapshotRestoreRecoveryReport> ReconcileRestoreAsync(
            Guid planId,
            string planSha256,
            CancellationToken ct = default);
        Task<SnapshotRestoreRecoveryReport> ResolveInterruptedRestoreAsync(
            Guid planId,
            string planSha256,
            SnapshotRestoreManualDecision decision,
            CancellationToken ct = default);
        Task<SnapshotRestoreResult> RestoreSnapshotAsync(string snapshotId, CancellationToken ct = default);
        Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync();
        Task<SnapshotCatalogResult> GetSnapshotCatalogAsync(CancellationToken ct = default);
        Task<IReadOnlyList<SnapshotRestoreBatchIdentity>> GetPendingRestoreBatchesAsync(CancellationToken ct = default);
        Task<OptimizationSnapshot?> GetSnapshotAsync(string snapshotId);
        Task DeleteSnapshotAsync(string snapshotId);
        Task ExportSnapshotAsync(string snapshotId, string filePath);
        Task<OptimizationSnapshot?> ImportSnapshotAsync(string filePath);
        string GetSnapshotDirectory();
        
        /// <summary>
        /// 旧 .reg 深度恢复兼容入口；永久 fail-closed，不执行系统写入。
        /// </summary>
        Task<RegistryBackupRestoreResult> RestoreRegistryBackupAsync(string snapshotId, CancellationToken ct = default);
        
        /// <summary>
        /// 只读检查旧快照是否包含 .reg 证据文件。
        /// </summary>
        bool HasRegistryBackup(string snapshotId);
    }
}
