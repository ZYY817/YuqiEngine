// File: GameOptimizer.Core/Services/Repair/RepairOperations/CpuRepairs.cs
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class CpuRepairs
{
    private static LocalizationService Loc => LocalizationService.Instance;

    public static IEnumerable<RepairItem> GetAll()
    {
        yield return new RepairItem
        {
            Id = "repair.timer_resolution",
            Name = "重置计时器分辨率",
            NameKey = "Repair_TimerResolution_Name",
            Description = "重置系统计时器到默认值",
            DescriptionKey = "Repair_TimerResolution_Desc",
            Icon = "⏱️",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => BootMemoryOwnerRequired("repair.timer_resolution")
        };

        yield return new RepairItem
        {
            Id = "repair.core_parking",
            Name = "重置核心停靠",
            NameKey = "Repair_CoreParking_Name",
            Description = "重置 CPU 核心停靠设置到默认值",
            DescriptionKey = "Repair_CoreParking_Desc",
            Icon = "🔧",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetCoreParkingAsync
        };

        yield return new RepairItem
        {
            Id = "repair.cpu_scheduling",
            Name = "重置 CPU 调度",
            NameKey = "Repair_CpuScheduling_Name",
            Description = "重置处理器调度策略到默认值",
            DescriptionKey = "Repair_CpuScheduling_Desc",
            Icon = "⚙️",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckCpuSchedulingHealthAsync,
            RepairAsync = _ => CpuSchedulingOwnerRequired("repair.cpu_scheduling")
        };

        yield return new RepairItem
        {
            Id = "repair.cpu_affinity",
            Name = "重置 CPU 亲和性",
            NameKey = "Repair_CpuAffinity_Name",
            Description = "清除所有进程的 CPU 亲和性设置",
            DescriptionKey = "Repair_CpuAffinity_Desc",
            Icon = "🎯",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => CpuSchedulingOwnerRequired("repair.cpu_affinity")
        };

        yield return new RepairItem
        {
            Id = "repair.cpu_frequency",
            Name = "重置 CPU 频率设置",
            NameKey = "Repair_CpuFrequency_Name",
            Description = "重置处理器最大/最小频率到默认值",
            DescriptionKey = "Repair_CpuFrequency_Desc",
            Icon = "📊",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetCpuFrequencyAsync
        };

        yield return new RepairItem
        {
            Id = "repair.hpet",
            Name = "重置 HPET 设置",
            NameKey = "Repair_Hpet_Name",
            Description = "重置高精度事件计时器设置",
            DescriptionKey = "Repair_Hpet_Desc",
            Icon = "🕐",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => BootMemoryOwnerRequired("repair.hpet")
        };

        yield return new RepairItem
        {
            Id = "repair.tsc_sync",
            Name = "修复 TSC 同步",
            NameKey = "Repair_TscSync_Name",
            Description = "修复时间戳计数器同步问题",
            DescriptionKey = "Repair_TscSync_Desc",
            Icon = "🔄",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => BootMemoryOwnerRequired("repair.tsc_sync")
        };
    }

    private static async Task<bool> CheckCpuSchedulingHealthAsync()
    {
        try
        {
            var result = await RepairHelper.RunCommandAsync("powershell.exe",
                "-ExecutionPolicy Bypass -Command \"(Get-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\PriorityControl' -Name 'Win32PrioritySeparation' -ErrorAction SilentlyContinue).Win32PrioritySeparation\"", 5000);
            return int.TryParse(result.Trim(), out _);
        }
        catch { return true; }
    }

    private static Task<RepairResult> ResetCoreParkingAsync(IProgress<RepairProgress>? progress) =>
        PowerCoordinatorRequired("repair.core_parking");

    private static Task<RepairResult> ResetCpuFrequencyAsync(IProgress<RepairProgress>? progress) =>
        PowerCoordinatorRequired("repair.cpu_frequency");

    private static Task<RepairResult> PowerCoordinatorRequired(string repairId) =>
        Task.FromResult(RepairResult.Failed(
            $"{repairId} 必须通过统一修复协调器执行；旧 powercfg 入口已安全禁用。"));

    private static Task<RepairResult> BootMemoryOwnerRequired(string repairId) =>
        Task.FromResult(RepairResult.Failed(
            $"{repairId} 必须通过统一 Boot/Memory Owner 执行；当前缺少兼容性探测和完整离线恢复证明，已安全阻断。"));

    private static Task<RepairResult> CpuSchedulingOwnerRequired(string repairId) =>
        Task.FromResult(RepairResult.Failed(
            $"{repairId} 必须通过统一 CPU scheduling/affinity Owner 执行；当前缺少稳定身份、漂移检测与精确恢复证明，已安全阻断。"));
}
