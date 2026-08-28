using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class WindowsCoreRepairs
{
    public static IEnumerable<RepairItem> GetAll()
    {
        yield return Create(
            "repair.sfc_dism", "Repair_SfcDism_Name", "Repair_SfcDism_Desc",
            "检查并修复系统文件", "使用 Windows 官方工具检查系统文件与组件存储。",
            CheckSystemRepairToolsAsync);
        yield return Create(
            "repair.windows_update", "Repair_WindowsUpdate_Name", "Repair_WindowsUpdate_Desc",
            "重置 Windows Update", "修复 Windows 更新失败或卡住问题。",
            () => ServiceRunningAsync("wuauserv"));
        yield return Create(
            "repair.windows_search", "Repair_WindowsSearch_Name", "Repair_WindowsSearch_Desc",
            "重建搜索索引", "修复 Windows 搜索服务与索引问题。",
            () => ServiceRunningAsync("WSearch"));
        yield return Create(
            "repair.windows_firewall", "Repair_WindowsFirewall_Name", "Repair_WindowsFirewall_Desc",
            "重置 Windows 防火墙", "恢复 Windows 防火墙健康状态。",
            () => ServiceRunningAsync("MpsSvc"), risky: true);
        yield return Create(
            "repair.windows_installer", "Repair_WindowsInstaller_Name", "Repair_WindowsInstaller_Desc",
            "修复 Windows Installer", "修复 MSI 安装服务问题。",
            () => ServiceAvailableAsync("msiserver"));
        yield return Create(
            "repair.component_store", "Repair_ComponentStore_Name", "Repair_ComponentStore_Desc",
            "维护组件存储", "检查 Windows 组件存储健康状态。",
            CheckComponentStoreAsync, risky: true);
    }

    private static RepairItem Create(
        string id,
        string nameKey,
        string descriptionKey,
        string name,
        string description,
        Func<Task<bool>> health,
        bool risky = false) =>
        new()
        {
            Id = id,
            Name = name,
            NameKey = nameKey,
            Description = description,
            DescriptionKey = descriptionKey,
            Icon = "🛠",
            Category = RepairCategory.SystemCore,
            IsRisky = risky,
            CheckHealthAsync = health,
            RepairAsync = BlockedCentralOwnerAsync
        };

    private static Task<RepairResult> BlockedCentralOwnerAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = "正在转交中央系统维护 Owner…", PercentComplete = 0 });
        return Task.FromResult(RepairResult.Failed(
            "该修复必须通过中央系统维护 Owner 执行；旧 DISM/SFC/Windows Update 直写入口已禁用。"));
    }

    private static Task<bool> CheckSystemRepairToolsAsync()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Task.FromResult(
            File.Exists(Path.Combine(system, "sfc.exe"))
            && File.Exists(Path.Combine(system, "dism.exe")));
    }

    private static Task<bool> CheckComponentStoreAsync()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Task.FromResult(Directory.Exists(Path.Combine(windows, "WinSxS")));
    }

    private static Task<bool> ServiceRunningAsync(string serviceName) =>
        Task.FromResult(SafeServiceHelper.TryGetServiceStatus(serviceName).Status
            == System.ServiceProcess.ServiceControllerStatus.Running);

    private static Task<bool> ServiceAvailableAsync(string serviceName) =>
        Task.FromResult(SafeServiceHelper.TryGetServiceStatus(serviceName).Status.HasValue);
}
