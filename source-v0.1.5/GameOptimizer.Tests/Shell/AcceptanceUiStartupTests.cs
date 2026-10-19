using GameOptimizer.Shell.Startup;

namespace GameOptimizer.Tests.Shell;

public sealed class AcceptanceUiStartupTests
{
    [Fact]
    public void AcceptanceUiUsesAnIsolatedMutexWithoutWeakeningProductionIdentity()
    {
        using var production = new StartupOrchestrator([]);
        using var recovery = new StartupOrchestrator(["--recovery-dev"]);
        using var acceptance = new StartupOrchestrator(["--acceptance-ui"]);

        Assert.False(production.IsAcceptanceUiInstance);
        Assert.True(acceptance.IsAcceptanceUiInstance);
        Assert.NotEqual(production.MutexIdentity, acceptance.MutexIdentity);
        Assert.NotEqual(recovery.MutexIdentity, acceptance.MutexIdentity);
        Assert.Equal("YuqiEngine_SingleInstance", production.MutexIdentity);
    }

    [Fact]
    public void AcceptanceSessionsUseSeparateSanitizedMutexIdentities()
    {
        using var first = new StartupOrchestrator(
            ["--acceptance-ui", "--acceptance-session=network-a"]);
        using var second = new StartupOrchestrator(
            ["--acceptance-ui", "--acceptance-session=network-b"]);
        using var unsafeName = new StartupOrchestrator(
            ["--acceptance-ui", "--acceptance-session=../visual test"]);

        Assert.NotEqual(first.MutexIdentity, second.MutexIdentity);
        Assert.Equal("network-a", first.AcceptanceSessionId);
        Assert.Equal("visualtest", unsafeName.AcceptanceSessionId);
        Assert.DoesNotContain("..", unsafeName.MutexIdentity, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptanceUiDoesNotImplicitlyEnableAutoTestExit()
    {
        using var acceptance = new StartupOrchestrator(
            ["--acceptance-ui", "--acceptance-page=network-test"]);

        Assert.True(acceptance.IsAcceptanceUiInstance);
        Assert.False(acceptance.IsAutoTestMode);
    }

    [Fact]
    public void ScreenshotAcceptanceProbeExitsAfterSavingTheWindow()
    {
        var app = Read("GameOptimizer.Shell/App.xaml.cs");

        Assert.Contains("CaptureAcceptanceWindow(mainWindow, acceptanceScreenshotPath)", app, StringComparison.Ordinal);
        Assert.Contains("Environment.Exit(0);", app, StringComparison.Ordinal);
        Assert.Contains("A screenshot request is a bounded, isolated probe", app, StringComparison.Ordinal);
    }

    [Fact]
    public void AppKeepsAcceptanceDataAndMigrationIsolated()
    {
        var app = Read("GameOptimizer.Shell/App.xaml.cs");

        Assert.Contains("Path.Combine(AppPaths.PreferredAppDataRoot, \"Acceptance\", acceptanceSessionId)", app, StringComparison.Ordinal);
        Assert.Contains("YUQIENGINE_ACCEPTANCE_MODE", app, StringComparison.Ordinal);
        Assert.Contains("else if (isRecoveryDevelopmentInstance)", app, StringComparison.Ordinal);
        Assert.Contains("mainWindow.NavigateToPage(acceptancePageKey)", app, StringComparison.Ordinal);
        Assert.Contains("mainWindow.InitializationCompleted", app, StringComparison.Ordinal);
        Assert.Contains("CaptureAcceptanceWindow(mainWindow, acceptanceScreenshotPath)", app, StringComparison.Ordinal);
        Assert.Contains("RenderTargetBitmap", app, StringComparison.Ordinal);
        Assert.Contains("ApplyDarkTheme();", app, StringComparison.Ordinal);
        Assert.DoesNotContain("--acceptance-theme=", app, StringComparison.Ordinal);
        Assert.Contains("--acceptance-width=", app, StringComparison.Ordinal);
        Assert.Contains("--acceptance-height=", app, StringComparison.Ordinal);
        Assert.Contains("mainWindow.Width = acceptanceWidth.Value", app, StringComparison.Ordinal);
        Assert.Contains("mainWindow.Height = acceptanceHeight.Value", app, StringComparison.Ordinal);
        Assert.Contains("--acceptance-scroll-y=", app, StringComparison.Ordinal);
        Assert.Contains("ScrollAcceptanceContent(mainWindow, acceptanceScrollOffset.Value)", app, StringComparison.Ordinal);
        Assert.Contains("ToolPageContentScrollViewer", app, StringComparison.Ordinal);
        Assert.Contains("FindAcceptanceScrollViewer(window, \"ToolPageContentScrollViewer\")", app, StringComparison.Ordinal);
        Assert.Contains("--acceptance-dialog=", app, StringComparison.Ordinal);
        Assert.Contains("--acceptance-action=", app, StringComparison.Ordinal);
        Assert.Contains("BeginAcceptanceNetworkTest()", app, StringComparison.Ordinal);
        Assert.Contains("system-cleanup-scan", app, StringComparison.Ordinal);
        Assert.Contains("isSystemCleanupScanProbe", app, StringComparison.Ordinal);
        Assert.Contains("isReadOnlyAcceptancePageProbe", app, StringComparison.Ordinal);
        Assert.Contains("&& !string.IsNullOrWhiteSpace(acceptanceScreenshotPath)", app, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(acceptancePageKey, \"repair-center\"", app, StringComparison.Ordinal);
        Assert.Contains("RunScannerActionAsync()", app, StringComparison.Ordinal);
        Assert.Contains("CaptureAcceptanceDialog(mainWindow, acceptanceDialogKey, acceptanceScreenshotPath, _host.Services, acceptanceScreengrab)", app, StringComparison.Ordinal);
        Assert.Contains("acceptanceLanguage ?? settings.Language", app, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IAppSettingsStore>()", app, StringComparison.Ordinal);
        Assert.Contains("视觉验收实例 · 隔离配置", app, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptanceUiDoesNotWaitForUnrelatedDashboardHealthScanBeforeNavigation()
    {
        var mainWindow = Read("GameOptimizer.Shell/MainWindow.xaml.cs");

        Assert.Contains("private static bool IsAcceptanceUiInstance", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Acceptance UI: skip unrelated dashboard telemetry/status/health scan", mainWindow, StringComparison.Ordinal);
        Assert.Contains("if (IsAcceptanceUiInstance)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("await _viewModel.InitializeAsync();", mainWindow, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        var directory = FindRepositoryRootFromEnvironment()
                        ?? FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory))
                        ?? FindRepositoryRoot(new DirectoryInfo(Directory.GetCurrentDirectory()));

        return File.ReadAllText(Path.Combine(
            (directory ?? throw new DirectoryNotFoundException("Repository root was not found.")).FullName,
            relativePath));
    }

    private static DirectoryInfo? FindRepositoryRootFromEnvironment()
    {
        var configuredPath = Environment.GetEnvironmentVariable("YUQIENGINE_TEST_REPOSITORY_ROOT");
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var directory = new DirectoryInfo(configuredPath);
        return File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")) ? directory : null;
    }

    private static DirectoryInfo? FindRepositoryRoot(DirectoryInfo? directory)
    {
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
            directory = directory.Parent;

        return directory;
    }
}
