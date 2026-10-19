namespace GameOptimizer.Core.Services.Repair;

public sealed record CpuSchedulingRepairDefinition(
    string RepairItemId,
    string CapabilityId,
    string ResourceKey,
    string Title,
    string BlockReason);

/// <summary>
/// Reserves unique identities for CPU scheduling repairs that cannot safely reuse
/// process-affinity profiles or legacy optimization operations.
/// </summary>
public static class CpuSchedulingRepairCatalog
{
    public static IReadOnlyList<CpuSchedulingRepairDefinition> All { get; } =
    [
        new(
            "repair.cpu_scheduling",
            "repair.cpu-scheduling.system-default",
            "cpu-scheduling:win32-priority-separation",
            "重置系统处理器调度策略",
            "Win32PrioritySeparation 的默认值受产品/系统策略影响；现有 Optimization operation 不是 durable Change Owner，禁止建立第二写入者或猜测默认值。"),
        new(
            "repair.cpu_affinity",
            "repair.cpu-affinity.clear-all",
            "cpu-affinity:all-processes",
            "清除所有进程 CPU 亲和性",
            "批量进程亲和性无法冻结稳定 PID/启动身份与拓扑，进程退出、重启或权限不足时也无法精确恢复；不得与持久化 profile 配置混为同一动作。")
    ];

    public static bool Contains(string repairItemId) =>
        All.Any(item => string.Equals(item.RepairItemId, repairItemId, StringComparison.OrdinalIgnoreCase));
}
