namespace GameOptimizer.Tests.Visual;

public sealed class EsportsModeBackgroundReadContractTests
{
    private const string XamlPath = "GameOptimizer.Shell/Views/EsportsModePage.xaml";
    private const string CodePath = "GameOptimizer.Shell/Views/EsportsModePage.xaml.cs";

    [Fact]
    public void StateReadUsesInlineProgressWithoutOpeningAReadWindow()
    {
        var xaml = Read(XamlPath);
        var code = Read(CodePath);
        var detectionMethod = Slice(
            code,
            "private async Task DetectSystemStatesAsync",
            "private void SetDetectionStatus");

        Assert.Contains("x:Name=\"DetectionStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetectionProgressBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("new Progress<EsportsWorkflowProgress>", detectionMethod, StringComparison.Ordinal);
        Assert.Contains("SetDetectionStatus(", detectionMethod, StringComparison.Ordinal);
        Assert.Contains("cancellationToken", detectionMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgressDialog", detectionMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", detectionMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("InfoDialog", detectionMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void MutatingActionsStayDisabledUntilAllReadinessGatesPass()
    {
        var xaml = Read(XamlPath);
        var code = Read(CodePath);
        var availabilityMethod = Slice(
            code,
            "private void UpdateActionAvailability",
            "private static void SetActionButtonState");

        foreach (var button in new[] { "ApplyBtn", "RestoreUserBtn", "RestoreWinBtn" })
        {
            Assert.Matches($"x:Name=\\\"{button}\\\"[^>]*IsEnabled=\\\"False\\\"", xaml);
            Assert.Contains($"SetActionButtonState({button}, canExecute);", availabilityMethod, StringComparison.Ordinal);
        }

        Assert.Contains("_isInitialized", availabilityMethod, StringComparison.Ordinal);
        Assert.Contains("!_isOperating", availabilityMethod, StringComparison.Ordinal);
        Assert.Contains("!_isStateReadInProgress", availabilityMethod, StringComparison.Ordinal);
        Assert.Contains("_hardwareDetectionSucceeded", availabilityMethod, StringComparison.Ordinal);
        Assert.Contains("_lastDetectionReport?.IsReadyForPlanning == true", availabilityMethod, StringComparison.Ordinal);
        Assert.Contains("EsportsModePage(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", code, StringComparison.Ordinal);
        Assert.Contains("if (!_isInitialized || _isOperating || _isStateReadInProgress", code, StringComparison.Ordinal);
        Assert.Contains("EsportsProbeOutcome.MissingOperation", code, StringComparison.Ordinal);
        Assert.Contains("row.IsCompatible = false", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicPresetsAreGatedAndCustomDoesNotAutoSelectOperations()
    {
        var xaml = Read(XamlPath);
        var code = Read(CodePath);
        var availabilityMethod = Slice(
            code,
            "private void UpdateActionAvailability",
            "private static void SetActionButtonState");
        var presetMethod = Slice(
            code,
            "private void ApplyStrategyPreset",
            "private void UpdateStrategyButtons");

        foreach (var button in new[] { "SafeBtn", "BalancedBtn", "ExtremeBtn" })
        {
            Assert.Matches($"x:Name=\\\"{button}\\\"[^>]*IsEnabled=\\\"False\\\"", xaml);
            Assert.Contains($"SetActionButtonState({button}, canExecute);", availabilityMethod, StringComparison.Ordinal);
        }

        Assert.Contains("稳妥推荐", xaml, StringComparison.Ordinal);
        Assert.Contains("性能推荐", xaml, StringComparison.Ordinal);
        Assert.Contains("自定义", xaml, StringComparison.Ordinal);
        Assert.Contains("_presetPlanner.GetPreflightOperationIds", presetMethod, StringComparison.Ordinal);
        Assert.Contains("_stateService.CreatePlan", presetMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("item.RiskLevel ==", presetMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("StrategyType.Extreme => true", presetMethod, StringComparison.Ordinal);
        Assert.Contains("if (strategy == StrategyType.Extreme)", code, StringComparison.Ordinal);
        Assert.Contains("config.PresetPolicyVersion < CurrentPresetPolicyVersion", code, StringComparison.Ordinal);
        Assert.Contains("_legacyExtremeSelectionCleared", code, StringComparison.Ordinal);
        _ = System.Xml.Linq.XDocument.Parse(xaml, System.Xml.Linq.LoadOptions.PreserveWhitespace);
    }

    [Fact]
    public void GameModeHubReusesInjectedHardwareServiceAndCleansUpItsSubscriptions()
    {
        var code = Read("GameOptimizer.Shell/ViewModels/GameModeHubViewModel.cs");

        Assert.Contains("IHardwareDetectionService hardwareDetectionService", code, StringComparison.Ordinal);
        Assert.Contains("_hardwareDetectionService.GetOrDetectAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", code, StringComparison.Ordinal);
        Assert.Contains("item.PropertyChanged += OnOptimizationItemPropertyChanged", code, StringComparison.Ordinal);
        Assert.Contains("item.PropertyChanged -= OnOptimizationItemPropertyChanged", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_panel_is_given_the_page_guard_service_and_unsubscribes_when_unloaded()
    {
        var pageCode = Read(CodePath);
        var panelCode = Read("GameOptimizer.Shell/Controls/GuardServicePanel.xaml.cs");

        Assert.Contains("GuardPanel.AttachGuardService(_guardService)", pageCode, StringComparison.Ordinal);
        Assert.Contains("public void AttachGuardService(OptimizationGuardService guardService)", panelCode, StringComparison.Ordinal);
        Assert.Contains("Unloaded += OnUnloaded", panelCode, StringComparison.Ordinal);
        Assert.Contains("UnsubscribeGuardEvents();", panelCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", panelCode, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate source slice: {startMarker}");
        return source[start..end];
    }

    private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

    private static string RepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
                return Path.Combine(directory.FullName, relativePath);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
