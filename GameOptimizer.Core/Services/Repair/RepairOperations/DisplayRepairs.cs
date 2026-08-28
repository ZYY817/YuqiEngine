using System.Diagnostics;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.GraphicsSafety;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class DisplayRepairs
{
    public static IEnumerable<RepairItem> GetAll()
    {
        yield return Item("repair.dwm_restart", "重启桌面窗口管理器（安全阻断）",
            "DWM 生命周期操作可能中断桌面合成，本版本只读检查进程状态。", "🖥️",
            CheckDwmHealthAsync,
            progress => GraphicsRepairBoundary.DenyAsync("repair.dwm_restart", GraphicsMutationKind.DwmLifecycle, progress));
        yield return Item("repair.display_driver", "重启显卡驱动（安全阻断）",
            "显示设备禁用/启用需要厂商、设备与会话级恢复证明，本版本不执行。", "🎮",
            CheckDisplayAdapterHealthAsync,
            progress => GraphicsRepairBoundary.DenyAsync("repair.display_driver", GraphicsMutationKind.DriverLifecycle, progress));
        yield return Item("repair.display_reset", "重置显示设置（安全阻断）",
            "切换显示拓扑会影响多显示器与远程会话，缺少拓扑快照时禁止执行。", "📺",
            CheckDisplayAdapterHealthAsync,
            progress => GraphicsRepairBoundary.DenyAsync("repair.display_reset", GraphicsMutationKind.DisplayTopology, progress));
        yield return Item("repair.dpi_reset", "重置 DPI 缩放",
            "复用现有 repair.display.dpi-reset typed capability；直接注册表删除已禁用。", "🔍",
            () => Task.FromResult(true),
            progress => GraphicsRepairBoundary.RequireExistingOwnerAsync("repair.dpi_reset", progress));
        yield return Item("repair.explorer_restart", "重启资源管理器（安全阻断）",
            "Explorer 生命周期继续由文件与 Shell 安全边界阻断。", "📁",
            CheckExplorerHealthAsync,
            progress => FileShellRepairBoundary.DenyAsync("repair.explorer_restart", FileShellMutationKind.ExplorerRestart, progress));
    }

    private static RepairItem Item(
        string id,
        string name,
        string description,
        string icon,
        Func<Task<bool>> check,
        Func<IProgress<RepairProgress>?, Task<RepairResult>> repair) => new()
        {
            Id = id,
            Name = name,
            Description = description,
            Icon = icon,
            Category = RepairCategory.Display,
            IsRisky = true,
            CheckHealthAsync = check,
            RepairAsync = repair
        };

    private static Task<bool> CheckDwmHealthAsync() => CheckProcessAsync("dwm");
    private static Task<bool> CheckExplorerHealthAsync() => CheckProcessAsync("explorer");

    private static Task<bool> CheckProcessAsync(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            var found = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            return Task.FromResult(found);
        }
        catch { return Task.FromResult(false); }
    }

    private static Task<bool> CheckDisplayAdapterHealthAsync()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", writable: false);
            return Task.FromResult(key?.GetSubKeyNames().Length > 0);
        }
        catch { return Task.FromResult(false); }
    }
}
