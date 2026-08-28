namespace GameOptimizer.Core.Services.Repair;

public sealed record BootMemoryRepairDefinition(
    string RepairItemId,
    string CapabilityId,
    string ResourceKey,
    string Title,
    string BlockReason);

/// <summary>
/// Fixed catalog for boot configuration, platform timer, and page-file repairs.
/// These entries deliberately fail closed until a version-aware owner can create
/// complete recovery material and prove offline recovery.
/// </summary>
public static class BootMemoryRepairCatalog
{
    public static IReadOnlyList<BootMemoryRepairDefinition> All { get; } =
    [
        Block("repair.timer_resolution", "boot-timer", "重置系统计时器策略",
            "系统级计时器策略缺少按 Windows 版本冻结的快照、兼容性探测和离线恢复证明。"),
        Block("repair.hpet", "bcd-hpet", "重置 HPET 启动配置",
            "BCD 计时器配置会影响启动与虚拟化依赖；缺少完整 BCD 导出、导入及离线恢复链时禁止写入。"),
        Block("repair.tsc_sync", "bcd-tsc", "重置 TSC 同步策略",
            "TSC/BCD 目标依赖硬件、虚拟化与系统版本；无法证明精确恢复前禁止写入。"),
        Block("repair.virtual_memory", "pagefile", "重置虚拟内存",
            "页面文件配置跨卷且需要保留原自动管理、大小和位置；缺少 typed 快照与重启后回读时禁止写入。")
    ];

    public static bool Contains(string repairItemId) =>
        All.Any(item => string.Equals(item.RepairItemId, repairItemId, StringComparison.OrdinalIgnoreCase));

    private static BootMemoryRepairDefinition Block(
        string repairItemId, string resourceSuffix, string title, string reason) =>
        new(
            repairItemId,
            $"repair.boot-memory.{repairItemId["repair.".Length..].Replace('_', '-')}",
            $"boot-memory:{resourceSuffix}",
            title,
            reason);
}
