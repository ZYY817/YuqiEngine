// File: GameOptimizer.Tests/Visual/SettingsCleanupVisualContractTests.cs
using System.Xml.Linq;

namespace GameOptimizer.Tests.Visual;

public sealed class SettingsCleanupVisualContractTests
{
    [Fact]
    public void SettingsKeepsConceptInformationHierarchyAndDefersStartupOwnerResolution()
    {
        var xaml = Read("GameOptimizer.Shell/Views/SettingsPage.xaml");
        var code = Read("GameOptimizer.Shell/Views/SettingsPage.xaml.cs");
        var app = Read("GameOptimizer.Shell/App.xaml.cs");

        Assert.Contains("AppearanceSectionTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LowUsageVisual", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualMode", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LightThemeCard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemThemeCard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LightThemeCard_Click", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemThemeCard_Click", code, StringComparison.Ordinal);
        Assert.Contains("SafetySectionTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("GeneralSectionTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("LogSectionTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("LogRetentionSlider", xaml, StringComparison.Ordinal);
        Assert.Contains("CustomLogRetentionTextBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyCustomLogRetentionButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("日志保存目录", xaml, StringComparison.Ordinal);
        Assert.Contains("下次启动后对实时日志生效", xaml, StringComparison.Ordinal);
        Assert.Contains("GenerateProblemPackageBtn", xaml, StringComparison.Ordinal);
        Assert.Contains("不包含设置、备份、密钥或用户文档", xaml, StringComparison.Ordinal);
        Assert.Contains("GenerateProblemPackageButton_Click", code, StringComparison.Ordinal);
        Assert.Contains("CreateDiagnosticPackageAsync", code, StringComparison.Ordinal);
        Assert.Contains("不会自动上传", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<DiagnosticPackageService>()", app, StringComparison.Ordinal);
        Assert.Contains("AppPaths.TryConfigureLogsDirectory(startupSettings.LogBackupDirectory", app, StringComparison.Ordinal);
        Assert.Contains("Directory.CreateDirectory(AppPaths.LogsDir)", app, StringComparison.Ordinal);
        Assert.Contains("导出优化方案", xaml, StringComparison.Ordinal);
        Assert.Contains("导入优化方案", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewGuideButton_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("重新查看引导", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewGuideButton_Click", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GuideSelectionDialog", code, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SettingsPage_Loaded", code, StringComparison.Ordinal);

        Assert.Contains("private IStartupEntryOwner? _startupOwner", code, StringComparison.Ordinal);
        Assert.Contains("Func<IStartupEntryOwner> startupOwnerFactory", code, StringComparison.Ordinal);
        Assert.Contains("_startupOwner ??= _startupOwnerFactory();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider _services", code, StringComparison.Ordinal);
        Assert.Contains("_startupOwner.PrepareAsync", code, StringComparison.Ordinal);
        Assert.Contains("_startupOwner.ExecuteAsync", code, StringComparison.Ordinal);
        Assert.Contains("settings.StartWithWindows = enabled", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IStartupEntryOwner>", app, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemCleanupKeepsInjectedCatalogWithQuarantineRecoveryAndExplicitEmptyState()
    {
        var xaml = Read("GameOptimizer.Shell/Views/SystemCleanupPage.xaml");
        var page = Read("GameOptimizer.Shell/Views/SystemCleanupPage.xaml.cs");
        var viewModel = Read("GameOptimizer.Shell/ViewModels/SystemCleanupViewModel.cs");
        var app = Read("GameOptimizer.Shell/App.xaml.cs");

        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        Assert.Equal("SystemCleanupPage", document.Root?.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value.Split('.').Last());
        Assert.Contains("ScrollViewer VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PanningMode=\"VerticalOnly\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FilteredCleanupItems}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SelectCategoryCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding CleanupItems.Count, Converter={StaticResource ZeroToVisibleConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"暂无可清理项目\"", xaml, StringComparison.Ordinal);
        Assert.Contains("已阻止执行", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ScanCore_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RunScannerActionAsync", page, StringComparison.Ordinal);
        Assert.Contains("Data=\"{Binding ScannerProgressArc}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScanSweepStoryboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding RestoreQuarantineCommand}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("SystemCleanupPage(SystemCleanupViewModel viewModel)", page, StringComparison.Ordinal);
        Assert.Contains("DataContext = _viewModel", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext = new SystemCleanupViewModel()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext = null", page, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<SystemCleanupViewModel>()", app, StringComparison.Ordinal);
        Assert.Contains("CleanupItems.Count > 0", viewModel, StringComparison.Ordinal);
        Assert.Contains("public IEnumerable<CleanupItem> FilteredCleanupItems", viewModel, StringComparison.Ordinal);
        Assert.Contains("public Geometry ScannerProgressArc", viewModel, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(120, token)", viewModel, StringComparison.Ordinal);
        Assert.Contains("当前没有可恢复的隔离文件。", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsAndCleanupExposeScrollableLocalizedFeedbackLandmarks()
    {
        var settings = Read("GameOptimizer.Shell/Views/SettingsPage.xaml");
        var settingsCode = Read("GameOptimizer.Shell/Views/SettingsPage.xaml.cs");
        var cleanup = Read("GameOptimizer.Shell/Views/SystemCleanupPage.xaml");
        var cleanupCode = Read("GameOptimizer.Shell/Views/SystemCleanupPage.xaml.cs");

        Assert.Contains("ScrollViewer", settings, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", settings, StringComparison.Ordinal);
        Assert.Contains("LanguageChanged", settingsCode, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer VerticalScrollBarVisibility=\"Auto\"", cleanup, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", cleanup, StringComparison.Ordinal);
        Assert.Contains("LocalizationService.Instance.LanguageChanged", cleanupCode, StringComparison.Ordinal);
        Assert.Contains("LogBackupDirectoryHealthText", settings, StringComparison.Ordinal);
        Assert.Contains("ApplyCustomLogRetentionButton_Click", settings, StringComparison.Ordinal);
        Assert.Contains("StartupToggle.IsEnabled = false", settingsCode, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.CardBackgroundAlt", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"{DynamicResource Theme.Brush.HeroHeaderBackground}\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualMode", settingsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Theme.Brush.ControlAccentSelectedBg", settingsCode, StringComparison.Ordinal);
        Assert.Contains("GenerateProblemPackageButton_Click", settings, StringComparison.Ordinal);
        Assert.Contains("UpdateProblemPackagePreview()", settingsCode, StringComparison.Ordinal);
        Assert.Contains("CreateDiagnosticPackageAsync", settingsCode, StringComparison.Ordinal);
        Assert.Contains("未自动上传", settingsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOnlyExposeBackedPreferencesAndTruthfulOptimizationProfileScope()
    {
        var settings = Read("GameOptimizer.Shell/Views/SettingsPage.xaml");
        var masterModule = Read("GameOptimizer.Shell/Views/MasterModuleTemplate.xaml.cs");
        var dashboard = Read("GameOptimizer.Shell/ViewModels/DashboardViewModel.cs");
        var exportService = Read("GameOptimizer.Shell/Services/ConfigurationExportService.cs");

        Assert.Contains("settings.ShowRiskWarnings", masterModule, StringComparison.Ordinal);
        Assert.Contains("_appSettings.EnableSoundNotification", dashboard, StringComparison.Ordinal);
        Assert.Contains("这里导入导出的是已应用优化方案，不是日志备份或完整应用设置", settings, StringComparison.Ordinal);
        Assert.Contains("Scope { get; init; } = \"optimization-profile\"", exportService, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"StartWithWindows\"]", exportService, StringComparison.Ordinal);
        Assert.DoesNotContain("_powerPlanService", exportService, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

    private static string RepoPath(string relativePath)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("YUQI_REPO_ROOT");
        var startPaths = string.IsNullOrWhiteSpace(configuredRoot)
            ? new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            : new[] { configuredRoot, AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

        foreach (var startPath in startPaths)
        {
            var current = new DirectoryInfo(startPath);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "GameOptimizer.sln")))
                    return Path.Combine(current.FullName, relativePath);
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
