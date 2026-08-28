namespace GameOptimizer.Tests.PowerManagement;

public sealed class PowerEditorSafetyPresentationTests
{
    [Fact]
    public void AdvancedPowerFamiliesAreEditableOnlyThroughManagedPlanWorkflow()
    {
        var source = Read("GameOptimizer.Shell", "Views", "PowerPlanEditorPage.xaml.cs");
        var xaml = Read("GameOptimizer.Shell", "Views", "PowerPlanEditorPage.xaml");
        Assert.Contains("PrepareUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("_selectedManagedPlanId", source, StringComparison.Ordinal);
        Assert.Contains("受管计划高级设置", xaml, StringComparison.Ordinal);
        Assert.Contains("SliderCpuMinimum", xaml, StringComparison.Ordinal);
        Assert.Contains("Windows / OEM 原计划禁止写入", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleAspm", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadSavedConfiguration();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryUiUsesVerifiedReceiptHistoryAndAccurateWording()
    {
        var source = Read("GameOptimizer.Shell", "Views", "PowerPlanEditorPage.xaml.cs");
        var xaml = Read("GameOptimizer.Shell", "Views", "PowerPlanEditorPage.xaml");
        Assert.Contains("PrepareRestoreAsync", source, StringComparison.Ordinal);
        Assert.Contains("_managedOverview.Backups", source, StringComparison.Ordinal);
        Assert.Contains("恢复最近一次已验证更改", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"恢复 Windows 默认\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerPageDoesNotExposeRawExceptionsToUsers()
    {
        var source = Read("GameOptimizer.Shell", "Views", "PowerPlanEditorPage.xaml.cs");

        Assert.Contains("DescribePowerException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TxtPlansEmpty.Text = ex.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"电源更改失败\", ex.Message", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceModePresentationUsesCapabilityMatchedTriStateAndNoCpuPromises()
    {
        var capability = Read("GameOptimizer.Infrastructure.Windows", "PowerManagement", "PowerSchemeChangeCapability.cs");
        var translations = Read("GameOptimizer.Core", "Localization", "OptimizationTranslations.cs");
        var presets = Read("GameOptimizer.Shell", "Services", "GameOptimizationPresets.cs");
        var splash = Read("GameOptimizer.Shell", "Startup", "SplashWindow.xaml.cs");

        Assert.Contains("GuidanceOnly：", capability, StringComparison.Ordinal);
        Assert.Contains("Unavailable：", capability, StringComparison.Ordinal);
        Assert.Contains("Applied：", capability, StringComparison.Ordinal);
        Assert.Contains("services.enable_high_performance_mode", presets, StringComparison.Ordinal);
        Assert.DoesNotContain("performance.power_plan", presets, StringComparison.Ordinal);
        Assert.DoesNotContain("CPU 最小频率设为 100%", translations, StringComparison.Ordinal);
        Assert.DoesNotContain("disabling core parking and frequency downscaling", translations, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("创建游戏专用的高性能电源方案", splash, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceModeAndEditorShareOneActiveSchemeResource()
    {
        var definitions = Read("GameOptimizer.Infrastructure.Windows", "PowerManagement", "PowerSettingCapabilityDefinitions.cs");
        Assert.Contains("PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource", definitions, StringComparison.Ordinal);
        Assert.DoesNotContain("\"power:scheme:active\"", definitions, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var root = new FileInfo(typeof(PowerEditorSafetyPresentationTests).Assembly.Location).Directory;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GameOptimizer.sln"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Repository root was not found.");
        return File.ReadAllText(Path.Combine(new[] { root.FullName }.Concat(parts).ToArray()));
    }
}
