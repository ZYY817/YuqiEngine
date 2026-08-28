using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Repair.RepairOperations;
using Microsoft.Extensions.Logging.Abstractions;
using RepairResult = GameOptimizer.Core.Services.Repair.RepairResult;

namespace GameOptimizer.Tests.FileShellSafety;

public sealed class FileShellEntryBoundaryTests
{
    private static readonly string[] CleanupIds =
    [
        "cleanup.gpu_cache",
        "cleanup.hosts_backup",
        "cleanup.prefetch_files",
        "cleanup.recycle_bin",
        "cleanup.system_temp",
        "cleanup.thumbnail_cache",
        "cleanup.user_temp",
        "cleanup.windows_logs",
        "cleanup.windows_update_cache"
    ];

    private static readonly string[] RepairIds =
    [
        "repair.action_center",
        "repair.amd_settings",
        "repair.context_menu",
        "repair.dwm_restart",
        "repair.explorer",
        "repair.explorer_restart",
        "repair.file_association",
        "repair.file_history",
        "repair.font_cache",
        "repair.game_config",
        "repair.gpu_cache",
        "repair.group_policy",
        "repair.icon_cache",
        "repair.nvidia_settings",
        "repair.recycle_bin",
        "repair.search_index",
        "repair.shortcuts",
        "repair.start_menu",
        "repair.system_tray",
        "repair.taskbar",
        "repair.temp_cleanup",
        "repair.thumbnail_cache",
        "repair.virtual_desktop",
        "repair.windows_search"
    ];

    [Fact]
    public void CleanupCatalogUsesOneFailClosedTypeForAllLegacyIds()
    {
        var operations = CleanupOperations();

        Assert.Equal(CleanupIds, operations.Select(operation => operation.Id).Order().ToArray());
        Assert.Equal(operations.Length, operations.Select(operation => operation.Id).Distinct().Count());

        // 可恢复清理六项已迁入 typed OrchestratedFileCleanupOperation：
        // 预览 → 授权门 → 确认 → SHA-256 校验隔离，恢复走中央隔离区。
        // hosts_backup 为 Owner 委托型编排操作（原子备份 + 哈希校验恢复）。
        foreach (var id in new[]
                 {
                     "cleanup.gpu_cache", "cleanup.prefetch_files", "cleanup.system_temp",
                     "cleanup.user_temp", "cleanup.thumbnail_cache", "cleanup.windows_update_cache",
                     "cleanup.hosts_backup"
                 })
        {
            var operation = Assert.Single(operations, candidate => candidate.Id == id);
            Assert.IsAssignableFrom<OrchestratedChangeOperation>(operation);
            Assert.True(operation.RequiresExplicitConfirmation);
            Assert.False(operation.RequiresSnapshotForRestore);
            Assert.False(string.IsNullOrWhiteSpace(operation.Benefit));
        }

        // 永久 fail-closed 两项：诊断日志（不可逆）与回收站（永久销毁用户数据）。
        foreach (var id in new[] { "cleanup.windows_logs", "cleanup.recycle_bin" })
        {
            var operation = Assert.Single(operations, candidate => candidate.Id == id);
            Assert.IsType<FailClosedFileShellOptimizationOperation>(operation);
            Assert.True(operation.RequiresExplicitConfirmation);
            Assert.Null(operation.Benefit);
        }
    }

    [Fact]
    public async Task CleanupCatalogWithoutServicesFailsClosedAndWritesNoLegacySnapshot()
    {
        var store = new CountingRestoreStore();
        var context = new OperationExecutionContext(store, NullLogger.Instance);

        foreach (var operation in CleanupOperations())
        {
            // 无 DI 组合下：可恢复清理项因服务未接入而 fail-closed；保留两项永久阻断。
            Assert.False((await operation.ApplyAsync(context, default)).Success);
            if (operation is FailClosedFileShellOptimizationOperation)
            {
                Assert.False((await operation.GetStatusAsync(default)).IsCompliant);
                Assert.Equal("FailClosed", operation.TargetValue);
            }
        }

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task DormantShaderCacheCompatibilityEntryIsAlsoFailClosed()
    {
        IOptimizationOperation operation = new ClearShaderCacheOperation();
        var context = new OperationExecutionContext(new CountingRestoreStore(), NullLogger.Instance);

        Assert.Equal("gpu.clear_shader_cache", operation.Id);
        Assert.False((await operation.GetStatusAsync(default)).IsCompliant);
        Assert.False((await operation.ApplyAsync(context, default)).Success);
        Assert.False((await operation.RestoreAsync(context, default)).Success);
    }

    [Fact]
    public async Task FileAndExplorerRepairEntriesFailClosed()
    {
        var entries = ExplorerRepairs.GetAll()
            .Concat(SystemComponentRepairs.GetAll())
            .Concat(StorageRepairs.GetAll())
            .Concat(WindowsCoreRepairs.GetAll())
            .Concat(GraphicsRepairs.GetAll())
            .Concat(GameOptimizationRepairs.GetAll())
            .Concat(DisplayRepairs.GetAll())
            .Where(item => RepairIds.Contains(item.Id, StringComparer.Ordinal))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(RepairIds, entries.Select(item => item.Id).ToArray());
        foreach (var entry in entries)
        {
            RepairResult result = await entry.RepairAsync(null);
            Assert.False(result.Success);
            var detail = result.Detail ?? result.Message;
            Assert.True(
                detail.Contains("Owner", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("PolicyDenied", StringComparison.Ordinal)
                || detail.Contains("QuarantineOwnerUnavailable", StringComparison.Ordinal)
                || detail.Contains("未接入", StringComparison.Ordinal),
                $"Unexpected fail-closed detail for {entry.Id}: {detail}");
        }
    }

    [Fact]
    public async Task AutoRepairAclEntryFailsClosed()
    {
        var service = new AutoRepairService(NullLogger<AutoRepairService>.Instance);

        var result = await service.TryRepairAsync(new AutoRepairOption
        {
            Id = "repair_backup_request_permission",
            Description = "test"
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("PolicyDenied", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizedEntriesContainNoLegacyDirectWriter()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            @"GameOptimizer.Core\Optimization\Providers\BuiltInOptimizationOperationProvider.cs",
            @"GameOptimizer.Core\Optimization\Operations\DirectoryCleanupOperation.cs",
            @"GameOptimizer.Core\Optimization\Operations\GpuAdvancedOperation.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\ExplorerRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\SystemComponentRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\StorageRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\WindowsCoreRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\GraphicsRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\GameOptimizationRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\DisplayRepairs.cs",
            @"GameOptimizer.Core\Services\AutoRepairService.cs",
            @"GameOptimizer.Core\Diagnostics\BackupDiagnosticService.cs",
            @"GameOptimizer.Shell\Views\GameModeHubPage.xaml.cs"
        };
        var forbidden = new[]
        {
            "File.Delete(", "Directory.Delete(", ".Delete();", "taskkill\", \"/f /im explorer", "Stop-Process -Name explorer",
            "Process.GetProcessesByName(\"explorer\")) p.Kill", "Process.Start(\"explorer.exe\")",
            "ie4uinit", "takeown", "icacls.exe", " /c rd /s /q ",
            "Remove-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer"
        };

        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            foreach (var token in forbidden)
                Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }

        var settingsSource = File.ReadAllText(Path.Combine(root, @"GameOptimizer.Shell\Views\SettingsPage.xaml.cs"));
        Assert.DoesNotContain("File.Delete(file);", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.ClearLogsComplete", settingsSource, StringComparison.Ordinal);
    }

    private static IOptimizationOperation[] CleanupOperations() =>
        new BuiltInOptimizationOperationProvider()
            .GetModules()
            .SelectMany(module => module.Operations)
            .Where(operation => CleanupIds.Contains(operation.Id, StringComparer.Ordinal))
            .OrderBy(operation => operation.Id, StringComparer.Ordinal)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class CountingRestoreStore : IOperationRestoreStore
    {
        public int CallCount { get; private set; }
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) { CallCount++; return Task.CompletedTask; }
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) { CallCount++; return Task.FromResult<string?>(null); }
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) { CallCount++; return Task.CompletedTask; }
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) { CallCount++; return Task.FromResult(false); }
    }
}
