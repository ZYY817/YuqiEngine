namespace GameOptimizer.Core.Services.Repair;

public sealed record SystemMaintenanceRepairDefinition(
    string RepairItemId,
    string CapabilityId,
    string ResourceKey,
    string Title,
    string BlockReason);

public static class SystemMaintenanceRepairCatalog
{
    public static IReadOnlyList<SystemMaintenanceRepairDefinition> All { get; } =
    [
        Block("repair.sfc_dism", "system-files", "修复系统文件", "SFC/DISM 缺少可本地化且权威的健康回读与不可逆人工恢复回执，当前保持阻断。"),
        Block("repair.windows_update", "windows-update", "重置 Windows Update", "更新数据库、服务与下载缓存的批量重置不可完整回滚。"),
        Block("repair.windows_search", "windows-search", "重建 Windows 搜索", "索引重建与服务状态属于独立 Search Owner。"),
        Block("repair.windows_firewall", "windows-firewall", "重置 Windows 防火墙", "防火墙属于 Security Owner，禁止组件修复旁路。"),
        Block("repair.windows_installer", "windows-installer", "修复 Windows Installer", "安装器服务与注册状态缺少完整快照和恢复语义。"),
        Block("repair.component_store", "component-store", "清理组件存储", "StartComponentCleanup 不可逆且没有可验证恢复材料。"),
        Block("repair.directx", "directx", "修复 DirectX", "旧 regsvr32 批量注册缺少组件清单、回读和补偿。"),
        Block("repair.vcredist", "vcredist", "修复 Visual C++ 运行库", "旧批量注册未绑定安装产品身份和恢复材料。"),
        Block("repair.dotnet", "dotnet", "修复 .NET Framework", "Optional Feature 变更不可由通用命令入口执行。"),
        Block("repair.complus", "complus", "修复 COM+", "服务控制属于专属 Service Owner。"),
        Block("repair.wia", "wia", "修复图像采集", "设备与服务组合属于 Peripheral Owner。"),
        Block("repair.time_service", "time-service", "修复时间同步", "服务重注册与时间写入需要专属 Owner。"),
        Block("repair.eventlog", "eventlog", "修复事件日志", "关键系统服务不可由通用组件修复启停。"),
        Block("repair.task_scheduler", "task-scheduler", "修复任务计划程序", "Task Scheduler 属于 Startup/Task Owner。"),
        Block("repair.security_center", "security-center", "修复安全中心", "Security Center 属于 Security Owner。"),
        Block("repair.group_policy", "group-policy", "重置组策略", "删除本地策略不可逆且会破坏管理员策略。"),
        Block("repair.icon_cache", "icon-cache", "重建图标缓存", "停止 Explorer 与删除缓存需要 Desktop Owner 和隔离恢复。"),
        Block("repair.font_cache", "font-cache", "重建字体缓存", "服务启停与缓存删除缺少文件级恢复材料。"),
        Block("repair.thumbnail_cache", "thumbnail-cache", "清理缩略图缓存", "文件删除不可恢复，必须由 Cleanup Owner 接管。"),
        Block("repair.edge", "edge", "修复 Microsoft Edge", "AppX 重注册需要 package identity 与结果回读。"),
        Block("repair.xbox", "xbox", "修复 Xbox 服务", "Xbox 服务组属于 Gaming/Service 专属 Owner。"),
        Block("repair.location", "location", "修复位置服务", "位置服务与隐私策略属于 Privacy Owner。"),
        Block("repair.clipboard", "clipboard", "修复剪贴板", "清空用户剪贴板不可恢复。"),
        Block("repair.notification", "notification", "修复通知服务", "通知服务属于 Desktop Owner。"),
        Block("repair.voice_input", "voice-input", "修复语音输入", "输入服务与进程启动属于 Input Owner。")
    ];

    public static bool Contains(string repairItemId) =>
        All.Any(item => string.Equals(item.RepairItemId, repairItemId, StringComparison.OrdinalIgnoreCase));

    private static SystemMaintenanceRepairDefinition Block(
        string repairItemId, string resourceSuffix, string title, string reason) =>
        new(
            repairItemId,
            $"repair.system-maintenance.{repairItemId["repair.".Length..].Replace('_', '-')}",
            $"system-maintenance:{resourceSuffix}",
            title,
            reason);
}
