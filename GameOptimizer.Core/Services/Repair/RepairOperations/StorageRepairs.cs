using System.Diagnostics;
using System.Threading;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.Services.MaintenanceDiagnostics;
using GameOptimizer.Core.Services.StorageSafety;
using GameOptimizer.Core.Services;
using Microsoft.Win32;
namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class StorageRepairs
{
    public static IEnumerable<RepairItem> GetAll(
        IMaintenanceDiagnosticOwner? maintenanceOwner = null,
        IStorageFeatureRepairOwner? storageFeatureOwner = null)
    {
        maintenanceOwner ??= MaintenanceDiagnosticOwnerFallback.Instance;
        storageFeatureOwner ??= StorageFeatureRepairOwnerFallback.Instance;
        yield return new RepairItem
        {
            Id = "repair.chkdsk",
            Name = "检查磁盘错误",
            NameKey = "Repair.Chkdsk.Name",
            Description = "检查磁盘文件系统错误",
            DescriptionKey = "Repair.Chkdsk.Desc",
            Icon = "💽",
            Category = RepairCategory.Storage,
            IsRisky = true,
            CheckHealthAsync = () => IsHealthyAsync(maintenanceOwner, "repair.chkdsk"),
            RepairAsync = progress => maintenanceOwner.RepairAsync("repair.chkdsk", progress, CancellationToken.None)
        };

        yield return new RepairItem
        {
            Id = "repair.search_index",
            Name = "重建搜索索引",
            NameKey = "Repair.SearchIndex.Name",
            Description = "修复 Windows 搜索不工作问题",
            DescriptionKey = "Repair.SearchIndex.Desc",
            Icon = "🔎",
            Category = RepairCategory.Storage,
            CheckHealthAsync = () => IsHealthyAsync(maintenanceOwner, "repair.search_index"),
            RepairAsync = progress => maintenanceOwner.RepairAsync("repair.search_index", progress, CancellationToken.None)
        };

        yield return new RepairItem
        {
            Id = "repair.storage_service",
            Name = "修复存储服务",
            NameKey = "Repair.StorageService.Name",
            Description = "修复磁盘识别和存储空间问题",
            DescriptionKey = "Repair.StorageService.Desc",
            Icon = "📥",
            Category = RepairCategory.Storage,
            CheckHealthAsync = () => IsHealthyAsync(maintenanceOwner, "repair.storage_service"),
            RepairAsync = progress => maintenanceOwner.RepairAsync("repair.storage_service", progress, CancellationToken.None)
        };

        yield return new RepairItem
        {
            Id = "repair.ntfs_encryption",
            Name = "恢复 NTFS 加密 (EFS)",
            Description = "恢复 NTFS 加密配置与 EFS 服务",
            Icon = "🔐",
            Category = RepairCategory.Storage,
            CheckHealthAsync = async () =>
                (await storageFeatureOwner.DiagnoseAsync("repair.ntfs_encryption", CancellationToken.None)
                    .ConfigureAwait(false)).IsHealthy == true,
            RepairAsync = progress => storageFeatureOwner.RepairAsync(
                "repair.ntfs_encryption",
                progress,
                CancellationToken.None)
        };

        yield return new RepairItem
        {
            Id = "repair.iso_mount",
            Name = "修复 ISO 挂载",
            NameKey = "Repair.IsoMount.Name",
            Description = "修复无法挂载 ISO 镜像文件问题",
            DescriptionKey = "Repair.IsoMount.Desc",
            Icon = "💿",
            Category = RepairCategory.Storage,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => Task.FromResult(RepairResult.Failed(
                "repair.iso_mount 尚未接入唯一 typed Owner，已安全阻断；不会修改 ISO 文件关联。"))
        };

        yield return new RepairItem
        {
            Id = "repair.recycle_bin",
            Name = "修复回收站",
            NameKey = "Repair.RecycleBin.Name",
            Description = "修复回收站损坏或无法清空问题",
            DescriptionKey = "Repair.RecycleBin.Desc",
            Icon = "🗑️",
            Category = RepairCategory.Storage,
            CheckHealthAsync = CheckRecycleBinHealthAsync,
            RepairAsync = RepairRecycleBinAsync
        };

        yield return new RepairItem
        {
            Id = "repair.temp_cleanup",
            Name = "清理临时文件",
            NameKey = "Repair.TempCleanup.Name",
            Description = "通过可恢复隔离流程处理系统临时文件",
            DescriptionKey = "Repair.TempCleanup.Desc",
            Icon = "🧹",
            Category = RepairCategory.Storage,
            CheckHealthAsync = CheckTempFilesHealthAsync,
            RepairAsync = CleanTempFilesAsync
        };

        yield return new RepairItem
        {
            Id = "repair.file_history",
            Name = "修复文件历史记录",
            NameKey = "Repair.FileHistory.Name",
            Description = "修复文件历史记录配置与备份问题",
            DescriptionKey = "Repair.FileHistory.Desc",
            Icon = "📄",
            Category = RepairCategory.Storage,
            CheckHealthAsync = CheckFileHistoryServiceHealthAsync,
            RepairAsync = RepairFileHistoryAsync
        };
    }

    private static async Task<bool> IsHealthyAsync(IMaintenanceDiagnosticOwner owner, string repairItemId)
    {
        var snapshot = await owner.DiagnoseAsync(repairItemId, CancellationToken.None).ConfigureAwait(false);
        return snapshot.IsHealthy == true;
    }

    private static Task<bool> CheckRecycleBinHealthAsync()
    {
        try
        {
            // 只读检查固定驱动器上的回收站目录。
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            foreach (var drive in drives)
            {
                var recycleBinPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(recycleBinPath))
                    return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch { return Task.FromResult(false); }
    }

    private static Task<bool> CheckTempFilesHealthAsync()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            if (!Directory.Exists(tempPath)) return Task.FromResult(false);
            var files = Directory.GetFiles(tempPath, "*", SearchOption.TopDirectoryOnly);
            return Task.FromResult(files.Length < 10000);
        }
        catch { return Task.FromResult(true); }
    }

    private static async Task<bool> CheckFileHistoryServiceHealthAsync()
    {
        try
        {
            var result = await RepairHelper.RunCommandAsync("sc", "query fhsvc", 5000);
            return result.Contains("RUNNING") || result.Contains("STOPPED");
        }
        catch { return false; }
    }

    private static Task<RepairResult> RepairRecycleBinAsync(IProgress<RepairProgress>? progress) =>
        FileShellRepairBoundary.DenyAsync("repair.recycle_bin", FileShellMutationKind.PermanentDelete, progress);

    private static Task<RepairResult> CleanTempFilesAsync(IProgress<RepairProgress>? progress) =>
        FileShellRepairBoundary.DenyAsync("repair.temp_cleanup", FileShellMutationKind.QuarantineFiles, progress);

    private static Task<RepairResult> RepairFileHistoryAsync(IProgress<RepairProgress>? progress) =>
        FileShellRepairBoundary.DenyAsync("repair.file_history", FileShellMutationKind.PermanentDelete, progress);
}



