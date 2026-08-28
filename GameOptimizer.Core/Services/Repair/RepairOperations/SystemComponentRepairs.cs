using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class SystemComponentRepairs
{
    public static IEnumerable<RepairItem> GetAll()
    {
        yield return FileCheck("repair.directx", "Repair.DirectX.Name", "Repair.DirectX.Desc",
            "修复 DirectX", "检查 DirectX 核心组件。", "d3d11.dll");
        yield return FileCheck("repair.vcredist", "Repair.VCRedist.Name", "Repair.VCRedist.Desc",
            "修复 Visual C++ 运行库", "检查 Visual C++ 运行库。", "vcruntime140.dll");
        yield return FileCheck("repair.dotnet", "Repair.DotNet.Name", "Repair.DotNet.Desc",
            "修复 .NET Framework", "检查 .NET Framework 运行时。", Path.Combine("Microsoft.NET", "Framework64"));
        yield return ServiceCheck("repair.complus", "Repair.ComPlus.Name", "Repair.ComPlus.Desc",
            "修复 COM+", "检查 COM+ 服务。", "COMSysApp");
        yield return ServiceCheck("repair.wia", "Repair.Wia.Name", "Repair.Wia.Desc",
            "修复图像采集", "检查图像采集服务。", "stisvc", RepairCategory.Peripheral);
        yield return ServiceCheck("repair.time_service", "Repair.TimeService.Name", "Repair.TimeService.Desc",
            "修复时间同步", "检查 Windows 时间服务。", "w32time");
        yield return ServiceCheck("repair.eventlog", "Repair.EventLog.Name", "Repair.EventLog.Desc",
            "修复事件日志", "检查 Windows 事件日志服务。", "eventlog");
        yield return ServiceCheck("repair.task_scheduler", "Repair.TaskScheduler.Name", "Repair.TaskScheduler.Desc",
            "修复任务计划程序", "检查任务计划程序服务。", "Schedule");
        yield return ServiceCheck("repair.security_center", "Repair.SecurityCenter.Name", "Repair.SecurityCenter.Desc",
            "修复安全中心", "检查 Windows 安全中心服务。", "wscsvc");
        yield return Generic("repair.group_policy", "Repair.GroupPolicy.Name", "Repair.GroupPolicy.Desc",
            "重置组策略", "重置本地组策略。", RepairCategory.SystemCore, risky: true);
        yield return Generic("repair.icon_cache", "Repair.IconCache.Name", "Repair.IconCache.Desc",
            "重建图标缓存", "修复图标显示异常。", RepairCategory.Display);
        yield return ServiceCheck("repair.font_cache", "Repair.FontCache.Name", "Repair.FontCache.Desc",
            "重建字体缓存", "检查字体缓存服务。", "FontCache", RepairCategory.Display);
        yield return Generic("repair.thumbnail_cache", "Repair.ThumbnailCache.Name", "Repair.ThumbnailCache.Desc",
            "清理缩略图缓存", "修复缩略图显示问题。", RepairCategory.Storage);
        yield return FileCheck("repair.edge", "Repair.Edge.Name", "Repair.Edge.Desc",
            "修复 Microsoft Edge", "检查 Microsoft Edge 应用。", Path.Combine("Microsoft", "Edge", "Application", "msedge.exe"), userPath: true);
        yield return Generic("repair.store", "Repair.Store.Name", "Repair.Store.Desc",
            "修复 Microsoft Store", "修复 Microsoft Store 组件。", RepairCategory.SystemCore);
        yield return ServiceCheck("repair.xbox", "Repair.Xbox.Name", "Repair.Xbox.Desc",
            "修复 Xbox 服务", "检查 Xbox 服务。", "XblAuthManager");
        yield return ServiceCheck("repair.location", "Repair.Location.Name", "Repair.Location.Desc",
            "修复位置服务", "检查 Windows 位置服务。", "lfsvc");
        yield return ServiceCheck("repair.clipboard", "Repair.Clipboard.Name", "Repair.Clipboard.Desc",
            "修复剪贴板", "检查剪贴板用户服务。", "cbdhsvc");
        yield return ServiceCheck("repair.notification", "Repair.Notification.Name", "Repair.Notification.Desc",
            "修复通知服务", "检查 Windows 推送通知服务。", "WpnService");
        yield return ServiceCheck("repair.voice_input", null, null,
            "修复语音输入", "检查 Windows 文本输入服务。", "TextInputManagementService");

        yield return new RepairItem
        {
            Id = "repair.power",
            Name = "重置电源计划",
            NameKey = "Repair.Power.Name",
            Description = "重置电源设置到默认值。",
            DescriptionKey = "Repair.Power.Desc",
            Icon = "⚡",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => Task.FromResult(RepairResult.Failed(
                "repair.power 必须通过统一 Power Owner 执行；旧 powercfg 入口已安全禁用。"))
        };
    }

    private static RepairItem Generic(
        string id, string? nameKey, string? descriptionKey, string name, string description,
        RepairCategory category, bool risky = false, Func<Task<bool>>? health = null) =>
        new()
        {
            Id = id,
            Name = name,
            NameKey = nameKey,
            Description = description,
            DescriptionKey = descriptionKey,
            Icon = "🛠",
            Category = category,
            IsRisky = risky,
            CheckHealthAsync = health ?? (() => Task.FromResult(true)),
            RepairAsync = BlockedCentralOwnerAsync
        };

    private static RepairItem ServiceCheck(
        string id, string? nameKey, string? descriptionKey, string name, string description,
        string serviceName, RepairCategory category = RepairCategory.SystemCore) =>
        Generic(id, nameKey, descriptionKey, name, description, category,
            health: () => Task.FromResult(
                SafeServiceHelper.TryGetServiceStatus(serviceName).Status.HasValue));

    private static RepairItem FileCheck(
        string id, string? nameKey, string? descriptionKey, string name, string description,
        string relativePath, bool userPath = false)
    {
        return Generic(id, nameKey, descriptionKey, name, description, RepairCategory.SystemCore,
            health: () => Task.FromResult(File.Exists(Path.Combine(
                userPath
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                    : Environment.GetFolderPath(Environment.SpecialFolder.System),
                relativePath)) || Directory.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), relativePath))));
    }

    private static Task<RepairResult> BlockedCentralOwnerAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = "正在转交中央系统组件 Owner…", PercentComplete = 0 });
        return Task.FromResult(RepairResult.Failed(
            "该修复必须通过中央系统组件 Owner 执行；旧服务、PowerShell、文件删除和组件命令入口已禁用。"));
    }
}
