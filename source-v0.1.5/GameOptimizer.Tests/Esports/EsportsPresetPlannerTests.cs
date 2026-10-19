using GameOptimizer.Core.Optimization;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Tests.Esports;

public sealed class EsportsPresetPlannerTests
{
    private readonly EsportsPresetPlanner _planner = new();

    [Fact]
    public void ConservativeRecommendation_SelectsOnlyWindowsGameMode()
    {
        var items = CreateRepresentativeItems();
        var preflightIds = _planner.GetPreflightOperationIds(EsportsPresetKind.Conservative, items);
        var recommendation = _planner.CreateRecommendation(
            EsportsPresetKind.Conservative,
            items,
            preflightIds);

        Assert.Equal(new[] { "gaming.mode.enable" }, preflightIds);
        Assert.Equal(new[] { "gaming.mode.enable" }, recommendation.SelectedOperationIds);
    }

    [Fact]
    public void PerformanceRecommendation_NeverIncludesNetworkPowerDriverTimerSecurityOrStorageChanges()
    {
        var items = CreateRepresentativeItems();
        var preflightIds = _planner.GetPreflightOperationIds(EsportsPresetKind.Performance, items);
        var recommendation = _planner.CreateRecommendation(
            EsportsPresetKind.Performance,
            items,
            preflightIds);

        Assert.Equal(new[] { "gaming.mode.enable", "gaming.dvr.disable" }, preflightIds);
        Assert.Equal(preflightIds, recommendation.SelectedOperationIds);
        Assert.DoesNotContain(recommendation.SelectedOperationIds, id => id.StartsWith("network.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recommendation.SelectedOperationIds, id => id.StartsWith("power.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recommendation.SelectedOperationIds, id => id.Contains("hags", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recommendation.SelectedOperationIds, id => id.Contains("hpet", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recommendation.SelectedOperationIds, id => id.StartsWith("security.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recommendation.SelectedOperationIds, id => id.StartsWith("storage.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Recommendation_FailsClosedWhenCompatibilityOrPreflightDoesNotPass()
    {
        var gameMode = Item("gaming.mode.enable");
        gameMode.IsCompatible = false;
        var dvr = Item("gaming.dvr.disable");
        var unrelated = Item("network.disable_nagle");
        var items = new[] { gameMode, dvr, unrelated };

        var preflightIds = _planner.GetPreflightOperationIds(EsportsPresetKind.Performance, items);
        var recommendation = _planner.CreateRecommendation(
            EsportsPresetKind.Performance,
            items,
            executableOperationIds: Array.Empty<string>());

        Assert.Equal(new[] { "gaming.dvr.disable" }, preflightIds);
        Assert.Empty(recommendation.SelectedOperationIds);
        Assert.Equal(1, recommendation.IncompatibleCount);
        Assert.Equal(1, recommendation.UnavailableCount);
        Assert.Equal(1, recommendation.PolicyExcludedCount);
    }

    private static GameModeOptimizationItem[] CreateRepresentativeItems()
    {
        return
        [
            Item("gaming.mode.enable"),
            Item("gaming.dvr.disable"),
            Item("network.disable_nagle"),
            Item("power.ultimate_performance"),
            Item("gpu.enable_hags"),
            Item("cpu.configure_hpet"),
            Item("security.disable_vbs"),
            Item("storage.disable_write_cache_flush"),
            Item("gpu.force_high_performance_gpu"),
            Item("gpu.set_sfio_priority"),
            Item("fps.disable_fullscreen_optimizations")
        ];
    }

    private static GameModeOptimizationItem Item(string operationId)
    {
        return new GameModeOptimizationItem(operationId, "test", OperationRiskLevel.Safe);
    }
}
