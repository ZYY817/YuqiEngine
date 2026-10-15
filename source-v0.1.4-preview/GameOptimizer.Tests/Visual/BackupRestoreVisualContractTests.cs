using System.Text;
using System.Xml.Linq;
using System.Runtime.CompilerServices;

namespace GameOptimizer.Tests.Visual;

public sealed class BackupRestoreVisualContractTests
{
    private const string BackupPage = "GameOptimizer.Shell/Views/BackupManagerPage.xaml";
    private const string BackupCode = "GameOptimizer.Shell/Views/BackupManagerPage.xaml.cs";
    private const string RestorePage = "GameOptimizer.Shell/Views/RestorePointPage.xaml";
    private const string RestoreCode = "GameOptimizer.Shell/Views/RestorePointPage.xaml.cs";

    [Fact]
    public void BackupPageKeepsTheVisualHierarchyAndTypedRestoreBoundary()
    {
        var xaml = Read(BackupPage);
        var code = Read(BackupCode);
        var app = Read("GameOptimizer.Shell/App.xaml.cs");

        Assert.Contains("WebSurface", xaml, StringComparison.Ordinal);
        Assert.Contains("备份管理", xaml, StringComparison.Ordinal);
        Assert.Contains("快照数量", xaml, StringComparison.Ordinal);
        Assert.Contains("创建设置快照", xaml, StringComparison.Ordinal);
        Assert.Contains("快照时间线", xaml, StringComparison.Ordinal);
        Assert.Contains("EmptyState", xaml, StringComparison.Ordinal);
        Assert.Contains("StatusText", xaml, StringComparison.Ordinal);
        Assert.Contains(string.Concat("Prepare", "RestoreSnapshotCommand"), xaml, StringComparison.Ordinal);
        Assert.Contains(string.Concat("ExecutePrepared", "RestoreCommand"), xaml, StringComparison.Ordinal);
        Assert.Contains("RestoreReceipts", xaml, StringComparison.Ordinal);
        Assert.Contains("CapabilityDisplayName", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding CapabilityId}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanExecutePendingRestore", xaml, StringComparison.Ordinal);
        Assert.Contains("PageStateToBadgeStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PageState", xaml, StringComparison.Ordinal);
        Assert.Contains("BackupCreateCard", xaml, StringComparison.Ordinal);
        Assert.Contains("BackupBaselineCard", xaml, StringComparison.Ordinal);
        Assert.Contains("BackupContextCard", xaml, StringComparison.Ordinal);
        Assert.Contains("BackupRail", xaml, StringComparison.Ordinal);
        Assert.Contains("恢复步骤", xaml, StringComparison.Ordinal);
        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectTimelineFilterCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSnapshotTimelineVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("IsBaselineTimelineVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("HasNoTimelineEntries", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding ViewModel.CanExecutePendingRestore", xaml, StringComparison.Ordinal);
        Assert.Contains("这份备份能恢复什么", xaml, StringComparison.Ordinal);
        Assert.Contains("RestoreCoverage", xaml, StringComparison.Ordinal);
        Assert.Contains("BackupStoragePathText", xaml, StringComparison.Ordinal);
        Assert.Contains("StorageVolumeText", xaml, StringComparison.Ordinal);
        Assert.Contains("BaselinePackageSizeText", xaml, StringComparison.Ordinal);
        Assert.Contains("ComprehensiveBackups", xaml, StringComparison.Ordinal);
        Assert.Contains("ComprehensiveBackupCoverageText", xaml, StringComparison.Ordinal);
        Assert.Contains("PrepareComprehensiveRestoreCommand", xaml, StringComparison.Ordinal);

        Assert.DoesNotContain(string.Concat("RestoreSpecific", "SnapshotCommand"), xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("ViewModel.", "RestoreSnapshotCommand"), xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("ViewModel.", "RestoreRegistryBackupCommand"), xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("首次强制备份", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Get", "Service"), code, StringComparison.Ordinal);
        Assert.DoesNotMatch(string.Concat("DataContext", "\\s*="), code);
        Assert.Contains("services.AddSingleton<BackupManagerViewModel>", app, StringComparison.Ordinal);
        Assert.Contains("new BackupManagerPage(provider.GetRequiredService<BackupManagerViewModel>())", app, StringComparison.Ordinal);

        var viewModel = Read("GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs");
        Assert.Contains("确认执行恢复", viewModel, StringComparison.Ordinal);
        Assert.Contains("PrepareTypedRestoreAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("ComprehensiveBackupCoverageText", viewModel, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc > DateTimeOffset.UtcNow", viewModel, StringComparison.Ordinal);
        Assert.Contains("CaptureStatus", viewModel, StringComparison.Ordinal);
        Assert.Contains("RecoveryStatus", viewModel, StringComparison.Ordinal);
        Assert.Contains("BaselineRecoveryCoverageClassifier", viewModel, StringComparison.Ordinal);
        Assert.Contains("可自动恢复", viewModel, StringComparison.Ordinal);
        Assert.Contains("仅手动恢复", viewModel, StringComparison.Ordinal);
        Assert.Contains("不可用", viewModel, StringComparison.Ordinal);
        Assert.Contains("不能恢复整个 Windows 系统", xaml, StringComparison.Ordinal);

        var injectedConstructor = code[code.IndexOf("public BackupManagerPage(BackupManagerViewModel", StringComparison.Ordinal)..];
        Assert.True(
            injectedConstructor.IndexOf("ViewModel = viewModel", StringComparison.Ordinal)
            < injectedConstructor.IndexOf("InitializeComponent", StringComparison.Ordinal),
            "注入的 ViewModel 必须在 InitializeComponent 前就绪，以便 Root 绑定首屏读取到状态。");
    }

    [Fact]
    public void RestorePointPageShowsRealStatesAndExplainsUnavailableActionsWithoutFakeButtons()
    {
        var xaml = Read(RestorePage);
        var code = Read(RestoreCode);

        Assert.Contains("WebSurface", xaml, StringComparison.Ordinal);
        Assert.Contains("系统保护", xaml, StringComparison.Ordinal);
        Assert.Contains("软件级恢复", xaml, StringComparison.Ordinal);
        Assert.Contains("还原点时间线", xaml, StringComparison.Ordinal);
        Assert.Contains("EmptyState", xaml, StringComparison.Ordinal);
        Assert.Contains("HasRestorePointStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("RestorePointStatusDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("恢复时会打开 Windows 的恢复界面，由 Windows 完成确认。", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("执行还原（阻断）", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("删除（不支持）", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenSystemRestoreSettingsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenSystemRestoreWizardCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("PageStateToBadgeStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PageState", xaml, StringComparison.Ordinal);
        Assert.Contains("RestoreCreateCard", xaml, StringComparison.Ordinal);
        Assert.Contains("RestoreProtectionCard", xaml, StringComparison.Ordinal);
        Assert.Contains("RestoreRail", xaml, StringComparison.Ordinal);
        Assert.Contains("恢复时会打开 Windows 的恢复界面，由 Windows 完成确认。", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectTimelineFilterCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("HasSystemTimelineEntries", xaml, StringComparison.Ordinal);
        Assert.Contains("HasSoftwareTimelineEntry", xaml, StringComparison.Ordinal);
        Assert.Contains("HasNoTimelineEntries", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("DataContext", " ="), code, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Get", "Service"), code, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("new ", "SystemRestoreService"), code, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("new ", "SnapshotService"), code, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupAndRestorePagesAreValidUtf8XamlWithoutLocalResourceDuplication()
    {
        foreach (var file in new[] { BackupPage, RestorePage })
        {
            var text = ReadUtf8(file);
            Assert.DoesNotContain('\u0000', text);
            Assert.DoesNotContain('\uFFFD', text);
            Assert.DoesNotContain(string.Concat("TO", "DO"), text, StringComparison.OrdinalIgnoreCase);

            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var keys = document.Descendants()
                .Attributes(xNamespace + "Key")
                .Select(attribute => attribute.Value)
                .ToArray();
            Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains("DynamicResource Theme.", text, StringComparison.Ordinal);
            Assert.Contains("Token.", text, StringComparison.Ordinal);
            Assert.Contains("ScrollViewer", text, StringComparison.Ordinal);
            Assert.Contains("TextWrapping", text, StringComparison.Ordinal);
            Assert.Contains("FocusVisualStyle", text, StringComparison.Ordinal);
        }
    }

    private static string Read(string relativePath) => ReadUtf8(relativePath);

    private static string ReadUtf8(string relativePath)
    {
        var bytes = File.ReadAllBytes(RepoPath(relativePath));
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
    }

    private static string RepoPath(string relativePath, [CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(
            Path.GetDirectoryName(sourceFilePath)
            ?? throw new DirectoryNotFoundException("Test source directory not found."));
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GameOptimizer.sln")))
        {
            current = current.Parent;
        }

        return Path.Combine((current ?? throw new DirectoryNotFoundException("Repository root not found.")).FullName, relativePath);
    }
}
