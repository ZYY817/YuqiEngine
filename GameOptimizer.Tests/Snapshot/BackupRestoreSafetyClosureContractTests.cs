using System.Runtime.CompilerServices;

namespace GameOptimizer.Tests.Snapshot;

/// <summary>
/// Guards the user-visible wiring of the snapshot safety closure without constructing
/// production services (which would otherwise probe the real application-data paths).
/// Runtime storage and manifest tests use an injected temporary snapshot root.
/// </summary>
public sealed class BackupRestoreSafetyClosureContractTests
{
    [Fact]
    public void BackupPageDescribesOnlyTheSupportedSettingsSnapshotCapability()
    {
        var xaml = Read("GameOptimizer.Shell/Views/BackupManagerPage.xaml");

        Assert.Contains("设置快照", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"完整备份\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"增量备份\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"快速备份\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreExecutionExposesCancellationAndInterruptedRecoveryActions()
    {
        var xaml = Read("GameOptimizer.Shell/Views/BackupManagerPage.xaml");
        var viewModel = Read("GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs");

        Assert.Contains("CancelRestoreCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Reconcile", viewModel, StringComparison.Ordinal);
        Assert.Contains("ResolveInterruptedRestoreAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource", viewModel, StringComparison.Ordinal);
        Assert.Contains("OperationCanceledException", viewModel, StringComparison.Ordinal);
        Assert.Contains("Cancel()", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewAndTimelineCountUseTheTypedManifestAsTheirSourceOfTruth()
    {
        var viewModel = Read("GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs");
        var models = Read("GameOptimizer.Core/Services/Snapshot/SnapshotModels.cs");

        Assert.Contains("TypedRegistryDwordStates", viewModel, StringComparison.Ordinal);
        Assert.Contains("TypedRegistryCapabilityStates", viewModel, StringComparison.Ordinal);
        Assert.Contains("TypedRegistryDwordStates", models, StringComparison.Ordinal);
        Assert.Contains("TypedRegistryCapabilityStates", models, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinarySettingsSnapshotCreationDoesNotRequireAdministratorRole()
    {
        var viewModel = Read("GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs");
        var createStart = viewModel.IndexOf("CreateSnapshotAsync", StringComparison.Ordinal);
        var quickStart = viewModel.IndexOf("QuickBackupAsync", StringComparison.Ordinal);
        var nextCommand = viewModel.IndexOf("[RelayCommand]", createStart + 1, StringComparison.Ordinal);

        Assert.True(createStart >= 0, "CreateSnapshotAsync command handler was not found.");
        Assert.True(nextCommand > createStart, "CreateSnapshotAsync command boundary was not found.");
        Assert.DoesNotContain(
            "IsRunningAsAdministrator",
            viewModel[createStart..nextCommand],
            StringComparison.Ordinal);

        if (quickStart >= 0)
        {
            var quickEnd = viewModel.IndexOf("[RelayCommand]", quickStart + 1, StringComparison.Ordinal);
            Assert.True(quickEnd > quickStart, "QuickBackupAsync command boundary was not found.");
            Assert.DoesNotContain(
                "IsRunningAsAdministrator",
                viewModel[quickStart..quickEnd],
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ApplicationStartupOrBackupPageInitiatesInterruptedRestoreReconciliation()
    {
        var app = Read("GameOptimizer.Shell/App.xaml.cs");
        var viewModel = Read("GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs");

        Assert.True(
            app.Contains("Reconcile", StringComparison.Ordinal)
            || viewModel.Contains("Reconcile", StringComparison.Ordinal),
            "Neither application startup nor the backup page initiates interrupted restore reconciliation.");
    }

    private static string Read(string relativePath, [CallerFilePath] string sourcePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GameOptimizer.sln")))
        {
            current = current.Parent;
        }

        return File.ReadAllText(Path.Combine(
            (current ?? throw new DirectoryNotFoundException("Repository root not found.")).FullName,
            relativePath));
    }
}
