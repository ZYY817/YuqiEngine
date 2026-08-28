using System.Diagnostics;
using GameOptimizer.Core.FileShellSafety;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class ExplorerRepairs
{
    public static IEnumerable<RepairItem> GetAll()
    {
        yield return Item("repair.explorer", "重启资源管理器（安全阻断）",
            "本版本不会终止或重启 Explorer；请先保存工作，并等待受控生命周期能力接管。", "🖥️",
            RepairCategory.Display, CheckExplorerHealthAsync, FileShellMutationKind.ExplorerRestart);
        yield return Item("repair.taskbar", "修复任务栏（安全阻断）",
            "任务栏状态重置缺少可恢复快照，本版本不会更改 Shell 状态。", "📊",
            RepairCategory.Display, CheckExplorerHealthAsync, FileShellMutationKind.ShellStateReset);
        yield return Item("repair.start_menu", "修复开始菜单（安全阻断）",
            "开始菜单状态重置缺少可恢复快照，本版本不会更改 Shell 状态。", "🪟",
            RepairCategory.Display, CheckStartMenuHealthAsync, FileShellMutationKind.ShellStateReset);
        yield return Item("repair.context_menu", "修复右键菜单（安全阻断）",
            "右键菜单缓存修改尚无可恢复 Owner，本版本不会清理或重置。", "📋",
            RepairCategory.Display, () => Task.FromResult(true), FileShellMutationKind.ShellStateReset);
        yield return Item("repair.file_association", "重置文件关联（安全阻断）",
            "文件关联是高影响 Shell 状态，本版本不会在缺少快照与恢复证明时重置。", "📄",
            RepairCategory.SystemCore, () => Task.FromResult(true), FileShellMutationKind.ShellStateReset);
        yield return Item("repair.shortcuts", "修复快捷方式（安全阻断）",
            "快捷方式与图标缓存处理尚无隔离清单，本版本不会删除文件。", "🔗",
            RepairCategory.Display, CheckIconCacheHealthAsync, FileShellMutationKind.QuarantineFiles);
        yield return Item("repair.action_center", "修复通知中心（安全阻断）",
            "通知中心状态重置缺少可恢复快照，本版本只执行健康检查。", "🔔",
            RepairCategory.Display, CheckNotificationServiceHealthAsync, FileShellMutationKind.ShellStateReset);
        yield return Item("repair.system_tray", "修复系统托盘（安全阻断）",
            "系统托盘状态重置缺少可恢复快照，本版本只执行健康检查。", "📌",
            RepairCategory.Display, CheckExplorerHealthAsync, FileShellMutationKind.ShellStateReset);
        yield return Item("repair.virtual_desktop", "修复虚拟桌面（安全阻断）",
            "虚拟桌面状态重置缺少可恢复快照，本版本不会更改 Shell 状态。", "🖼️",
            RepairCategory.Display, () => Task.FromResult(true), FileShellMutationKind.ShellStateReset);
    }

    private static RepairItem Item(
        string id,
        string name,
        string description,
        string icon,
        RepairCategory category,
        Func<Task<bool>> check,
        FileShellMutationKind kind) =>
        new()
        {
            Id = id,
            Name = name,
            Description = description,
            Icon = icon,
            Category = category,
            IsRisky = true,
            CheckHealthAsync = check,
            RepairAsync = progress => FileShellRepairBoundary.DenyAsync(id, kind, progress)
        };

    private static Task<bool> CheckExplorerHealthAsync()
    {
        try
        {
            using var process = Process.GetProcessesByName("explorer").FirstOrDefault();
            return Task.FromResult(process is not null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static Task<bool> CheckStartMenuHealthAsync()
    {
        try
        {
            using var process = Process.GetProcessesByName("ShellExperienceHost").FirstOrDefault();
            return Task.FromResult(process is not null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static Task<bool> CheckIconCacheHealthAsync()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Task.FromResult(File.Exists(Path.Combine(localAppData, "IconCache.db")));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static async Task<bool> CheckNotificationServiceHealthAsync()
    {
        try
        {
            var result = await RepairHelper.RunCommandAsync("sc", "query WpnService", 5000).ConfigureAwait(false);
            return result.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
