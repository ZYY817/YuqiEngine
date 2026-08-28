// File: GameOptimizer.Core/Services/Repair/RepairOperations/PowerRepairs.cs
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class PowerRepairs
{
    private static LocalizationService Loc => LocalizationService.Instance;

    public static IEnumerable<RepairItem> GetAll()
    {
        yield return new RepairItem
        {
            Id = "repair.power_plan",
            Name = "重置电源计划",
            NameKey = "Repair_PowerPlan_Name",
            Description = "重置所有电源计划到 Windows 默认值",
            DescriptionKey = "Repair_PowerPlan_Desc",
            Icon = "🔋",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetPowerPlanAsync
        };

        yield return new RepairItem
        {
            Id = "repair.high_performance",
            Name = "恢复高性能电源计划",
            NameKey = "Repair_HighPerformance_Name",
            Description = "恢复隐藏的高性能电源计划",
            DescriptionKey = "Repair_HighPerformance_Desc",
            Icon = "⚡",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckHighPerformancePlanHealthAsync,
            RepairAsync = RestoreHighPerformanceAsync
        };

        yield return new RepairItem
        {
            Id = "repair.ultimate_performance",
            Name = "恢复卓越性能电源计划",
            NameKey = "Repair_UltimatePerformance_Name",
            Description = "恢复隐藏的卓越性能电源计划",
            DescriptionKey = "Repair_UltimatePerformance_Desc",
            Icon = "🚀",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckUltimatePerformancePlanHealthAsync,
            RepairAsync = RestoreUltimatePerformanceAsync
        };

        yield return new RepairItem
        {
            Id = "repair.hibernation",
            Name = "修复休眠功能",
            NameKey = "Repair_Hibernation_Name",
            Description = "重新启用休眠功能",
            DescriptionKey = "Repair_Hibernation_Desc",
            Icon = "💤",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckHibernationHealthAsync,
            RepairAsync = RepairHibernationAsync
        };

        yield return new RepairItem
        {
            Id = "repair.sleep",
            Name = "修复睡眠功能",
            NameKey = "Repair_Sleep_Name",
            Description = "修复系统无法进入睡眠问题",
            DescriptionKey = "Repair_Sleep_Desc",
            Icon = "😴",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = RepairSleepAsync
        };

        yield return new RepairItem
        {
            Id = "repair.fast_startup",
            Name = "修复快速启动",
            NameKey = "Repair_FastStartup_Name",
            Description = "重置快速启动设置",
            DescriptionKey = "Repair_FastStartup_Desc",
            Icon = "⏩",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = RepairFastStartupAsync
        };

        yield return new RepairItem
        {
            Id = "repair.battery_report",
            Name = "生成电池报告",
            NameKey = "Repair_BatteryReport_Name",
            Description = "生成详细的电池健康报告",
            DescriptionKey = "Repair_BatteryReport_Desc",
            Icon = "🔌",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = GenerateBatteryReportAsync
        };

        yield return new RepairItem
        {
            Id = "repair.power_troubleshoot",
            Name = "电源故障排除",
            NameKey = "Repair_PowerTroubleshoot_Name",
            Description = "运行 Windows 电源故障排除程序",
            DescriptionKey = "Repair_PowerTroubleshoot_Desc",
            Icon = "🔧",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = RunPowerTroubleshootAsync
        };
    }

    private static async Task<bool> CheckHighPerformancePlanHealthAsync()
    {
        try
        {
            var result = await RepairHelper.RunCommandAsync("powercfg", "/list", 5000);
            return result.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        }
        catch { return false; }
    }

    private static async Task<bool> CheckUltimatePerformancePlanHealthAsync()
    {
        try
        {
            var result = await RepairHelper.RunCommandAsync("powercfg", "/list", 5000);
            return result.Contains("e9a42b02-d5df-448d-aa00-03f14749eb61");
        }
        catch { return false; }
    }

    private static Task<bool> CheckHibernationHealthAsync()
    {
        try
        {
            var hiberfil = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows).Substring(0, 3), "hiberfil.sys");
            return Task.FromResult(File.Exists(hiberfil));
        }
        catch { return Task.FromResult(false); }
    }

    private static Task<RepairResult> ResetPowerPlanAsync(IProgress<RepairProgress>? progress) =>
        CentralCoordinatorRequired("repair.power_plan");

    private static Task<RepairResult> RestoreHighPerformanceAsync(IProgress<RepairProgress>? progress) =>
        CentralCoordinatorRequired("repair.high_performance");

    private static Task<RepairResult> RestoreUltimatePerformanceAsync(IProgress<RepairProgress>? progress) =>
        CentralCoordinatorRequired("repair.ultimate_performance");

    private static Task<RepairResult> RepairHibernationAsync(IProgress<RepairProgress>? progress) =>
        CentralCoordinatorRequired("repair.hibernation");

    private static Task<RepairResult> RepairSleepAsync(IProgress<RepairProgress>? progress) =>
        CentralCoordinatorRequired("repair.sleep");

    private static Task<RepairResult> RepairFastStartupAsync(IProgress<RepairProgress>? progress) =>
        CentralCoordinatorRequired("repair.fast_startup");

    private static Task<RepairResult> GenerateBatteryReportAsync(IProgress<RepairProgress>? progress) =>
        CentralCoordinatorRequired("repair.battery_report");

    private static Task<RepairResult> RunPowerTroubleshootAsync(IProgress<RepairProgress>? progress) =>
        CentralCoordinatorRequired("repair.power_troubleshoot");

    private static Task<RepairResult> CentralCoordinatorRequired(string repairId) =>
        Task.FromResult(RepairResult.Failed(
            $"{repairId} 必须通过统一修复协调器执行；旧命令入口已安全禁用。"));
}
