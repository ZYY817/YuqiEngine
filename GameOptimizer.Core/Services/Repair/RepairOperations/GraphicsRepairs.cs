using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.GraphicsSafety;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class GraphicsRepairs
{
    public static IEnumerable<RepairItem> GetAll()
    {
        yield return Item("repair.hags", "恢复硬件加速 GPU 调度",
            "复用现有 gpu.enable_hags typed capability；需要管理员权限、显式确认与重启。", "🎮",
            () => Task.FromResult(true), ExistingOwner);
        yield return Item("repair.gpu_cache", "清理 GPU 着色器缓存（安全阻断）",
            "必须经 SystemCleanup quarantine Owner 生成预览、隔离清单和恢复证明。", "🗑️",
            CheckGpuCacheHealthAsync,
            progress => FileShellRepairBoundary.DenyAsync("repair.gpu_cache", FileShellMutationKind.QuarantineFiles, progress));
        yield return Item("repair.nvidia_settings", "重置 NVIDIA 设置（安全阻断）",
            "没有经 NVAPI 证明的设置快照与恢复契约，不执行厂商私有配置写入。", "🟢",
            CheckNvidiaHealthAsync,
            progress => GraphicsRepairBoundary.DenyAsync("repair.nvidia_settings", GraphicsMutationKind.VendorPrivateSetting, progress));
        yield return Item("repair.amd_settings", "重置 AMD 设置（安全阻断）",
            "没有经 ADL 证明的设置快照与恢复契约，不执行厂商私有配置写入。", "🔴",
            CheckAmdHealthAsync,
            progress => GraphicsRepairBoundary.DenyAsync("repair.amd_settings", GraphicsMutationKind.VendorPrivateSetting, progress));
        yield return Item("repair.display_adapter", "刷新显示适配器（安全阻断）",
            "pnputil 扫描属于设备生命周期操作，缺少固定设备集合与回读时禁止执行。", "🔄",
            CheckDisplayAdapterHealthAsync,
            progress => GraphicsRepairBoundary.DenyAsync("repair.display_adapter", GraphicsMutationKind.DriverLifecycle, progress));
        yield return Item("repair.display_driver_reset", "重置显示驱动（安全阻断）",
            "禁用再启用显示设备可能造成黑屏或远程会话中断，本版本不执行。", "🖥️",
            CheckDisplayAdapterHealthAsync,
            progress => GraphicsRepairBoundary.DenyAsync("repair.display_driver_reset", GraphicsMutationKind.DriverLifecycle, progress));
        yield return Item("repair.hardware_acceleration", "恢复硬件加速",
            "复用现有 repair.hardware-acceleration.reset typed capability。", "⚡",
            () => Task.FromResult(true), ExistingOwner);
        yield return Item("repair.vsync", "重置垂直同步覆盖",
            "复用现有 repair.graphics.vsync-reset typed capability。", "📺",
            () => Task.FromResult(true), ExistingOwner);
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

    private static Task<RepairResult> ExistingOwner(IProgress<RepairProgress>? progress) =>
        GraphicsRepairBoundary.RequireExistingOwnerAsync("graphics.existing-typed-owner", progress);

    private static Task<bool> CheckGpuCacheHealthAsync()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Task.FromResult(
                Directory.Exists(Path.Combine(local, "NVIDIA", "DXCache"))
                || Directory.Exists(Path.Combine(local, "AMD", "DxCache"))
                || Directory.Exists(Path.Combine(local, "D3DSCache")));
        }
        catch { return Task.FromResult(false); }
    }

    private static Task<bool> CheckNvidiaHealthAsync()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe");
        return Task.FromResult(File.Exists(path));
    }

    private static Task<bool> CheckAmdHealthAsync() =>
        Task.FromResult(Directory.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD")));

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
