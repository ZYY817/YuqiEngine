using System.Xml.Linq;

namespace GameOptimizer.Tests.Visual;

public sealed class MaintenancePageVisualContractTests
{
    [Theory]
    [InlineData("GameOptimizer.Shell/Views/HardwareInfoPage.xaml")]
    [InlineData("GameOptimizer.Shell/Views/NetworkTestPage.xaml")]
    [InlineData("GameOptimizer.Shell/Views/RestorePointPage.xaml")]
    [InlineData("GameOptimizer.Shell/Views/RepairCenterPage.xaml")]
    [InlineData("GameOptimizer.Shell/Views/BackupManagerPage.xaml")]
    [InlineData("GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml")]
    [InlineData("GameOptimizer.Shell/Views/PowerPlanEditorPage.xaml")]
    public void MaintenancePagesKeepScrollableSquareSurfaceContract(string relativePath)
    {
        var xaml = Read(relativePath);

        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"4\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"6\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"8\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"10\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"12\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"14\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"16\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"CornerRadius\" Value=\"4\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"CornerRadius\" Value=\"6\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"CornerRadius\" Value=\"8\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"CornerRadius\" Value=\"10\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"CornerRadius\" Value=\"12\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"CornerRadius\" Value=\"14\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"CornerRadius\" Value=\"16\"", xaml, StringComparison.Ordinal);

        _ = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
    }

    [Fact]
    public void ReferenceTaskEntrancesStayBoundToRealPageCommands()
    {
        AssertPageContains(
            "GameOptimizer.Shell/Views/NetworkTestPage.xaml",
            "NodeSelector_SelectionChanged", "RunTest_Click", "CancelTest_Click");
        AssertPageContains(
            "GameOptimizer.Shell/Views/RepairCenterPage.xaml",
            "StartScanCommand", "CancelScanCommand", "ScanProgressText", "ExecuteQuickRepairActionCommand", "QuickRepairDetails_Click");
        AssertPageContains(
            "GameOptimizer.Shell/Views/BackupManagerPage.xaml",
            "CreateSnapshotCommand", "QuickBackupCommand", "PrepareRestoreSnapshotCommand");
        AssertPageContains(
            "GameOptimizer.Shell/Views/RestorePointPage.xaml",
            "CreateRestorePointCommand", "OpenSystemRestoreSettingsCommand", "OpenSystemRestoreWizardCommand");
        AssertPageContains(
            "GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml",
            "SelectAllCommand", "ApplyAllProfilesCommand", "RefreshProcessesCommand");
        AssertPageContains(
            "GameOptimizer.Shell/Views/PowerPlanEditorPage.xaml",
            "BtnApplyAll_Click", "BtnRefreshPlans_Click", "BtnRestoreDefault_Click");
    }

    [Fact]
    public void RepairCenterKeepsReadableThemeTextAndUsesReferenceTwoColumnWorkspace()
    {
        var xaml = Read("GameOptimizer.Shell/Views/RepairCenterPage.xaml");

        Assert.Contains("<Style TargetType=\"TextBlock\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.TextPrimary", xaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(xaml, "<UniformGrid Columns=\"2\"/>"));
        Assert.DoesNotContain("<UniformGrid Columns=\"3\"/>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button Width=\"248\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Border Width=\"248\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BlackActionButton", xaml, StringComparison.Ordinal);
        Assert.Contains("QuickRepairDetails_Click", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairCenterKeepsTheCategoryRailFixedAndScrollsOnlyItsRepairWorkspace()
    {
        var xaml = Read("GameOptimizer.Shell/Views/RepairCenterPage.xaml");
        var viewModel = Read("GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs");

        Assert.Contains("<Grid Margin=\"24,20,24,28\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Row=\"2\" VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Icon}\" FontFamily=\"Segoe MDL2 Assets\"", xaml, StringComparison.Ordinal);
        Assert.Contains("repair.audio_service\", IconGlyphs.AudioMute", viewModel, StringComparison.Ordinal);
        Assert.Contains("repair.audio_endpoint\", IconGlyphs.Headphone", viewModel, StringComparison.Ordinal);
        Assert.Contains("repair.network_adapter\", IconGlyphs.Wifi", viewModel, StringComparison.Ordinal);
        Assert.Contains("repair.winsock\", IconGlyphs.Network", viewModel, StringComparison.Ordinal);
        Assert.Contains("repair.windows_update\", IconGlyphs.Update", viewModel, StringComparison.Ordinal);
        Assert.Contains("repair.chkdsk\", IconGlyphs.Disk", viewModel, StringComparison.Ordinal);
        Assert.Contains("repair.bluetooth_adapter\", IconGlyphs.Bluetooth", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairCenterScanFeedbackKeepsOneCancellableScanFlow()
    {
        var viewModel = Read("GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs");

        Assert.Contains("private void CancelScan()", viewModel, StringComparison.Ordinal);
        Assert.Contains("_scanCts.Cancel();", viewModel, StringComparison.Ordinal);
        Assert.Contains("Repair.ScanCancelled", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (completed)", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairCenterShowsConcreteScanOutcomesInsteadOfAnUnsupportedHealthScore()
    {
        var xaml = Read("GameOptimizer.Shell/Views/RepairCenterPage.xaml");
        var viewModel = Read("GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs");

        Assert.DoesNotContain("健康分", xaml, StringComparison.Ordinal);
        Assert.Contains("本次扫描结果", xaml, StringComparison.Ordinal);
        Assert.Contains("发现的问题", xaml, StringComparison.Ordinal);
        Assert.Contains("本次未发现异常", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowScanResultDetailsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RepairDetectedIssueCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ScanResultIcon", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility\" Value=\"Collapsed", xaml, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.TextPrimary", xaml, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.Success", xaml, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.Warning", xaml, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.Error", xaml, StringComparison.Ordinal);
        Assert.Contains("public int NormalItemCount", viewModel, StringComparison.Ordinal);
        Assert.Contains("public int RepairableIssueCount", viewModel, StringComparison.Ordinal);
        Assert.Contains("private void ShowScanResultDetails", viewModel, StringComparison.Ordinal);
        Assert.Contains("_scanService.ScanAllAsync", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckHealthAsync(), itemCts.Token", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairCenterKeepsAllPresetRepairsVisibleAndOnlyEnablesExecutableActions()
    {
        var xaml = Read("GameOptimizer.Shell/Views/RepairCenterPage.xaml");
        var viewModel = Read("GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs");

        Assert.Contains("AvailabilityText", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsActionAvailable}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("QuickRepairActions.Add", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("itemVm is null || !itemVm.CanRepair", viewModel, StringComparison.Ordinal);
        Assert.Contains("RepairExecutionAvailability.WorkingBidirectional", viewModel, StringComparison.Ordinal);
        Assert.Contains("高级恢复暂不可用", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("raw.githubusercontent.com", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.SetText(command)", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("所有修复均可回滚", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairEntryPreflightsBeforeMutationAndExplainsItsChangePlan()
    {
        var viewModel = Read("GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs");
        var dialogXaml = Read("GameOptimizer.Shell/Views/RepairConfirmDialog.xaml");
        var dialogCode = Read("GameOptimizer.Shell/Views/RepairConfirmDialog.xaml.cs");
        var executor = Read("GameOptimizer.Core/Services/Repair/OrchestratedRepairChangeExecutor.cs");

        Assert.Contains("未发现“{itemVm.LocalizedName}”对应的异常，未执行修复", viewModel, StringComparison.Ordinal);
        Assert.Contains("无法确认“{itemVm.LocalizedName}”当前是否异常，未执行修复", viewModel, StringComparison.Ordinal);
        Assert.Contains("OrchestratedRepairChangeExecutor.GetPlannedChangeSummaries", viewModel, StringComparison.Ordinal);
        Assert.Contains("检测依据", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("将要修改", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("RepairConfirmationContext", dialogCode, StringComparison.Ordinal);
        Assert.Contains("GetPlannedChangeSummaries", executor, StringComparison.Ordinal);
    }

    private static void AssertPageContains(string path, params string[] values)
    {
        var xaml = Read(path);
        foreach (var value in values)
            Assert.Contains(value, xaml, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string token) =>
        value.Split(token, StringSplitOptions.None).Length - 1;

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
            directory = directory.Parent;

        return File.ReadAllText(Path.Combine(
            (directory ?? throw new DirectoryNotFoundException("Repository root was not found.")).FullName,
            relativePath));
    }
}
