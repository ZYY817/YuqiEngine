using System.Diagnostics;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.ApplicationRepair;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class GameOptimizationRepairs
{
    private static LocalizationService Loc => LocalizationService.Instance;
    public static IEnumerable<RepairItem> GetAll(IApplicationPackageRepairOwner? applicationRepairOwner = null)
    {
        applicationRepairOwner ??= UnavailableApplicationPackageRepairOwner.Instance;
        // Game Mode 修复
        yield return new RepairItem
        {
            Id = "repair.game_mode",
            Name = "修复游戏模式",
            NameKey = "Repair_GameMode_Name",
            Description = "重置 Windows 游戏模式设置，修复游戏模式不生效问题",
            DescriptionKey = "Repair_GameMode_Desc",
            Icon = "🎮",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = RepairGameModeAsync
        };

        // Game DVR 修复
        yield return new RepairItem
        {
            Id = "repair.game_dvr",
            Name = "修复游戏录制",
            NameKey = "Repair_GameDvr_Name",
            Description = "重置 Game DVR 设置，修复游戏录制不工作问题",
            DescriptionKey = "Repair_GameDvr_Desc",
            Icon = "📹",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = RepairGameDvrAsync
        };

        // 全屏优化修复
        yield return new RepairItem
        {
            Id = "repair.fullscreen_optimization",
            Name = "重置全屏优化",
            NameKey = "Repair_FullscreenOptimization_Name",
            Description = "重置全屏优化设置到默认值",
            DescriptionKey = "Repair_FullscreenOptimization_Desc",
            Icon = "🖥️",
            Category = RepairCategory.Display,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetFullscreenOptimizationAsync
        };

        // 游戏栏修复
        yield return new RepairItem
        {
            Id = "repair.game_bar",
            Name = "修复 Xbox Game Bar",
            NameKey = "Repair_GameBar_Name",
            Description = "重置 Xbox Game Bar，修复游戏栏无法打开问题",
            DescriptionKey = "Repair_GameBar_Desc",
            Icon = "🎯",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = progress => RepairGameBarAsync(applicationRepairOwner, progress)
        };

        // 游戏配置重置
        yield return new RepairItem
        {
            Id = "repair.game_config",
            Name = "重置游戏配置缓存",
            NameKey = "Repair_GameConfig_Name",
            Description = "清理游戏配置缓存，修复游戏设置异常问题",
            DescriptionKey = "Repair_GameConfig_Desc",
            Icon = "⚙️",
            Category = RepairCategory.Storage,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetGameConfigCacheAsync
        };
    }

    private static Task<RepairResult> RepairGameModeAsync(IProgress<RepairProgress>? progress) =>
        GraphicsRepairBoundary.RequireExistingOwnerAsync("repair.game_mode", progress);

    private static Task<RepairResult> RepairGameDvrAsync(IProgress<RepairProgress>? progress) =>
        GraphicsRepairBoundary.RequireExistingOwnerAsync("repair.game_dvr", progress);

    private static Task<RepairResult> ResetFullscreenOptimizationAsync(IProgress<RepairProgress>? progress) =>
        GraphicsRepairBoundary.DenyAsync(
            "repair.fullscreen_optimization",
            GraphicsMutationKind.ExistingTypedRegistryCapability,
            progress);

    private static async Task<RepairResult> RepairGameBarAsync(
        IApplicationPackageRepairOwner owner,
        IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.RepairingGameBar"), PercentComplete = 10 });
        var result = await owner.PrepareAsync(
            ApplicationPackageRepairTarget.XboxGameBar,
            ApplicationPackageRepairAction.Reregister,
            CancellationToken.None).ConfigureAwait(false);
        return RepairResult.Failed(result.Message, detail: result.Status);
    }

    private static Task<RepairResult> ResetGameConfigCacheAsync(IProgress<RepairProgress>? progress) =>
        FileShellRepairBoundary.DenyAsync("repair.game_config", FileShellMutationKind.QuarantineFiles, progress);
}
