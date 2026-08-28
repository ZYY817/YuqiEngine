namespace GameOptimizer.Tests.PowerManagement;

public sealed class SmartOptimizationPowerBoundaryTests
{
    [Fact]
    public void RetiredBatchService_IsDeleted()
    {
        Assert.False(File.Exists(Path.Combine(
            FindRepositoryRoot(), "GameOptimizer.Shell", "Services", "SmartOptimizationService.cs")));
        Assert.DoesNotContain("SmartOptimizationService", Read("GameOptimizer.Shell", "App.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredBatchDialogs_AreNotProductionEntries()
    {
        Assert.False(File.Exists(Path.Combine(
            FindRepositoryRoot(), "GameOptimizer.Shell", "Views", "SmartOptimizeDialog.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(
            FindRepositoryRoot(), "GameOptimizer.Shell", "Views", "SmartOptimizeWizardDialog.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(
            FindRepositoryRoot(), "GameOptimizer.Shell", "Services", "SmartOptimizeWizardService.cs")));
    }

    [Fact]
    public void LegacyPowerPlanHelpers_HaveNoSecondWriter()
    {
        foreach (var file in new[] { "PowerPlanCreator.cs", "PowerPlanActivator.cs", "PowerSettingApplicator.cs" })
        {
            var source = Read("GameOptimizer.Shell", "Services", "PowerPlan", file);
            Assert.DoesNotContain("PowerPlanService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ExecuteCommandAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("powercfg", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PowerOwnerFaultInjection_CoversRequiredCentralWriteBoundaries()
    {
        var source = Read("GameOptimizer.Tests", "PowerManagement", "PowerManagementOwnerTests.cs");

        foreach (var requiredEvidence in new[]
        {
            "Baseline", "Drift", "Cancel", "Replay", "Restore", "Permission", "CommandFailure"
        })
            Assert.Contains(requiredEvidence, source, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(segments).ToArray()));

    private static string FindRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var sourceDirectory = new FileInfo(sourceFile).Directory?.Parent?.Parent;
        if (sourceDirectory is not null && Directory.Exists(Path.Combine(sourceDirectory.FullName, "GameOptimizer.Shell")))
            return sourceDirectory.FullName;
        var workingDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        if (Directory.Exists(Path.Combine(workingDirectory.FullName, "GameOptimizer.Shell")))
            return workingDirectory.FullName;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "GameOptimizer.Shell")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
