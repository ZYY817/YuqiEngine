using System.Xml.Linq;
using System.Text;
using System.Runtime.CompilerServices;

namespace GameOptimizer.Tests.Visual;

public sealed class SafetyOverviewPageContractTests
{
    private static readonly string[] PageXamlFiles =
    [
        "GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml",
        "GameOptimizer.Shell/Views/BackupManagerPage.xaml",
        "GameOptimizer.Shell/Views/RestorePointPage.xaml",
        "GameOptimizer.Shell/Views/OverviewPage.xaml"
    ];

    private static readonly string[] PageCodeFiles =
    [
        "GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs",
        "GameOptimizer.Shell/Views/BackupManagerPage.xaml.cs",
        "GameOptimizer.Shell/Views/RestorePointPage.xaml.cs",
        "GameOptimizer.Shell/Views/OverviewPage.xaml.cs",
        "GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs",
        "GameOptimizer.Shell/ViewModels/RestorePointViewModel.cs",
        "GameOptimizer.Shell/Converters/OverviewModuleStatusConverter.cs"
    ];

    [Fact]
    public void SafetyOverviewPagesUseSemanticResourcesAndNoLegacyVisualDebt()
    {
        foreach (var file in PageXamlFiles.Where(file => !file.EndsWith("OverviewPage.xaml", StringComparison.Ordinal)))
        {
            var text = Read(file);
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", text);
            Assert.DoesNotMatch(string.Concat("(?:Linear|Radial)", "Grad", "ient", "Brush"), text);
            Assert.DoesNotContain(string.Concat("G", "lass"), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(string.Concat("Grad", "ient"), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch("Background\\s*=\\s*\"Transparent\"", text);
            Assert.DoesNotMatch("RepeatBehavior\\s*=\\s*\"Forever\"", text);
            Assert.Contains("DynamicResource Theme.", text, StringComparison.Ordinal);
            Assert.Contains("Token.", text, StringComparison.Ordinal);
            Assert.Contains("ScrollViewer", text, StringComparison.Ordinal);
            Assert.Contains("TextWrapping", text, StringComparison.Ordinal);
            Assert.Contains("FocusVisualStyle", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SafetyOverviewPagesExposeRealStateAndHighRiskBoundaries()
    {
        var all = string.Join("\n", PageXamlFiles.Concat(PageCodeFiles).Select(Read));
        foreach (var state in new[]
        {
            "Loading", "Empty", "Ready", "Partial", "Error", "Unauthorized",
            "Recovering", "InterruptedManual", "CompensatedVerified", "RebootRequired"
        })
        {
            Assert.Contains(state, all, StringComparison.Ordinal);
        }

        var backup = Read("GameOptimizer.Shell/Views/BackupManagerPage.xaml") + Read("GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs");
        Assert.Contains("PrepareRestoreSnapshotCommand", backup, StringComparison.Ordinal);
        Assert.Contains("ExecutePreparedRestoreCommand", backup, StringComparison.Ordinal);
        Assert.Contains("RestoreReceipts", backup, StringComparison.Ordinal);
        Assert.Contains("V1-V3", backup, StringComparison.Ordinal);
        Assert.Contains("CanExecutePendingRestore", backup, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding ViewModel.CanExecutePendingRestore", backup, StringComparison.Ordinal);

        var restore = Read("GameOptimizer.Shell/Views/RestorePointPage.xaml") + Read("GameOptimizer.Shell/ViewModels/RestorePointViewModel.cs");
        Assert.Contains("恢复时会打开 Windows 的恢复界面，由 Windows 完成确认。", restore, StringComparison.Ordinal);
        Assert.DoesNotContain("执行还原（阻断）", restore, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", restore, StringComparison.Ordinal);
        Assert.Contains("需要重启", restore, StringComparison.Ordinal);
        Assert.Contains("人工", restore, StringComparison.Ordinal);

        var baseline = Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml") + Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs");
        Assert.Contains("ManifestHashText", baseline, StringComparison.Ordinal);
        Assert.Contains("ManifestRecoverabilityText", baseline, StringComparison.Ordinal);
        Assert.Contains("GetCurrentManifestAsync", baseline, StringComparison.Ordinal);
        Assert.Contains("GetStateAsync", baseline, StringComparison.Ordinal);
        Assert.Contains("IsIndeterminate", baseline, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstProtectionWizardUsesFiveStepsAndGatesBackupBehindTimedAcknowledgement()
    {
        var xaml = Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml");
        var code = Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs");

        Assert.Contains("IntroStepIndicatorText", xaml, StringComparison.Ordinal);
        Assert.Contains("RiskAcknowledgementCheckBox", xaml, StringComparison.Ordinal);
        Assert.Contains("IntroBackBtn", xaml, StringComparison.Ordinal);
        Assert.Contains("IntroNextBtn", xaml, StringComparison.Ordinal);
        Assert.Contains("WelcomeStepPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("BoundaryStepPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("AuthorStepPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("RiskStepPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("BackupStepPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("第 {_introStep} 步 / 共 4 步", code, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(TimeSpan.FromSeconds(1))", code, StringComparison.Ordinal);
        Assert.Contains("请先完整阅读，{remaining}s 后可继续", code, StringComparison.Ordinal);
        Assert.Contains("if (_introStep != 4 || !_riskAcknowledged || !StartBackupBtn.IsEnabled)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstProtectionExplainsModifiedSystemsAndDoesNotPromiseUnsafeSystemReplay()
    {
        var dialog = Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs");
        var collector = Read("GameOptimizer.Shell/Services/Baseline/BaselineDomainCollector.cs");

        Assert.Contains("系统被自行精简、封装、深度改装", dialog, StringComparison.Ordinal);
        Assert.Contains("精简版或第三方优化痕迹较多", dialog, StringComparison.Ordinal);
        Assert.Contains("软件不会自动执行该操作", collector, StringComparison.Ordinal);
        Assert.Contains("软件不会回放整份策略", collector, StringComparison.Ordinal);
        Assert.Contains("软件不会自动导入或重置", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments = \"-import {{source}}\"", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments = \"/configure /db secedit.sdb", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments = \"advfirewall import {{source}}\"", collector, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstProtectionDeferralRequiresExplicitAcknowledgementAndExplainsRecoveryFallbacks()
    {
        var dialog = Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml")
            + Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs");

        Assert.Contains("ConfirmDeferredBaseline", dialog, StringComparison.Ordinal);
        Assert.Contains("确认暂时跳过首次安全备份", dialog, StringComparison.Ordinal);
        Assert.Contains("无法补回首次使用前的系统基线", dialog, StringComparison.Ordinal);
        Assert.Contains("手动创建系统还原点", dialog, StringComparison.Ordinal);
        Assert.Contains("重新安装驱动", dialog, StringComparison.Ordinal);
        Assert.Contains("原电源计划", dialog, StringComparison.Ordinal);
        Assert.Contains("重装系统", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstProtectionMarksTheTestBuildAndShowsAuthorInfoBeforeEnteringSoftware()
    {
        var xaml = Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml");
        var dialog = Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs");
        var about = Read("GameOptimizer.Shell/Views/AboutDialog.xaml.cs");

        Assert.Contains("当前为测试版本", xaml, StringComparison.Ordinal);
        Assert.Contains("使用任何优化前完成安全备份", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowAuthorInfoBeforeEnteringSoftware", dialog, StringComparison.Ordinal);
        Assert.Contains("CloseAfterAuthorInfo", dialog, StringComparison.Ordinal);
        Assert.Contains("pack://application:,,,", about, StringComparison.Ordinal);
    }

    [Fact]
    public void PageCompositionDoesNotUseServiceLocatorOrDataContextOverride()
    {
        foreach (var file in PageCodeFiles.Where(file => file.Contains("/Views/", StringComparison.Ordinal)))
        {
            var text = Read(file);
            Assert.DoesNotContain("GetService<", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GetService(", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new SnapshotService", text, StringComparison.Ordinal);
            Assert.DoesNotMatch("DataContext\\s*=", text);
        }

        var overview = Read("GameOptimizer.Shell/Views/OverviewPage.xaml.cs");
        Assert.Contains("NavigateToPageRequested", overview, StringComparison.Ordinal);
        Assert.Contains("NavigateToPage(target)", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void PageTextIsUtf8WithoutKnownMojibakeOrPlaceholderMarkers()
    {
        foreach (var file in PageXamlFiles.Concat(PageCodeFiles))
        {
            var bytes = File.ReadAllBytes(RepoPath(file));
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            Assert.DoesNotContain('\uFFFD', text);
            foreach (var marker in new[] { "鈥", "鍔", "璁", "绯", "锟" })
            {
                Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void PageXamlHasUniqueLocalResourceKeysAndSharedControls()
    {
        foreach (var file in PageXamlFiles)
        {
            var document = XDocument.Load(RepoPath(file), LoadOptions.PreserveWhitespace);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var keys = document.Descendants().Attributes(xNamespace + "Key").Select(attribute => attribute.Value).ToArray();
            Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            var serialized = document.ToString(SaveOptions.DisableFormatting);
            if (file.EndsWith("OverviewPage.xaml", StringComparison.Ordinal))
                Assert.Contains("Overview.FeatureMatrix", serialized, StringComparison.Ordinal);
            else if (file.EndsWith("BackupManagerPage.xaml", StringComparison.Ordinal)
                     || file.EndsWith("RestorePointPage.xaml", StringComparison.Ordinal))
                Assert.Contains("WebSurface", serialized, StringComparison.Ordinal);
            else
                Assert.Contains("PageHeader", serialized, StringComparison.Ordinal);
        }

        var combined = string.Join("\n", PageXamlFiles.Select(Read));
        Assert.Contains("SectionCard", combined, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

    private static string RepoPath(string relativePath, [CallerFilePath] string sourcePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourcePath) ?? AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GameOptimizer.sln")))
        {
            current = current.Parent;
        }

        return Path.Combine((current ?? throw new DirectoryNotFoundException("Repository root not found.")).FullName, relativePath);
    }
}
