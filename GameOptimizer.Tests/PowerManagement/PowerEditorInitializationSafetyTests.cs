namespace GameOptimizer.Tests.PowerManagement;

public sealed class PowerEditorInitializationSafetyTests
{
    [Fact]
    public void PageDoesNotStartAHiddenMonitorOrLegacyPollingLoop()
    {
        var source = Read("GameOptimizer.Shell", "Views", "PowerPlanEditorPage.xaml.cs");

        Assert.Contains("RefreshPageAsync", source, StringComparison.Ordinal);
        Assert.Contains("InspectAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SystemMonitorService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PageOnlyReadsAdvancedSettingsAfterManagedPlanSelection()
    {
        var source = Read("GameOptimizer.Shell", "Views", "PowerPlanEditorPage.xaml.cs");

        Assert.Contains("ManagedPlanSelector_SelectionChanged", source, StringComparison.Ordinal);
        Assert.Contains("ReadSettingsAsync(_selectedManagedPlanId.Value", source, StringComparison.Ordinal);
        Assert.Contains("目标不是宇奇创建并登记", Read("GameOptimizer.Infrastructure.Windows", "PowerManagement", "ManagedPowerPlanService.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("var tasks = new[]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAll(tasks)", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var root = new FileInfo(typeof(PowerEditorInitializationSafetyTests).Assembly.Location).Directory;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GameOptimizer.sln")) ) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Repository root was not found.");
        return File.ReadAllText(Path.Combine(new[] { root.FullName }.Concat(parts).ToArray()));
    }
}
