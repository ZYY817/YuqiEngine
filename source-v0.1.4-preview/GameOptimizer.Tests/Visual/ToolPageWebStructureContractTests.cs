namespace GameOptimizer.Tests.Visual;

public sealed class ToolPageWebStructureContractTests
{
    public static IEnumerable<object[]> Pages =>
    [
        ["GameOptimizer.Shell/Views/HardwareInfoPage.xaml", new[] { "整机摘要", "HardwareContent", "CopyInfoButton_Click", "RefreshButton_Click" }],
        ["GameOptimizer.Shell/Views/NetworkTestPage.xaml", new[] { "Sonar Pulse", "SONAR", "NodeSelector_SelectionChanged", "RunTest_Click", "CancelTest_Click" }],
        ["GameOptimizer.Shell/Views/RestorePointPage.xaml", new[] { "还原点时间线", "CreateRestorePointCommand", "OpenSystemRestoreSettingsCommand" }],
        ["GameOptimizer.Shell/Views/RepairCenterPage.xaml", new[] { "让电脑回到健康状态", "FilterByCategoryCommand", "ExecuteQuickRepairActionCommand", "QuickRepairDetails_Click", "SelectedRepairCategory", "CategoryToggle" }],
        ["GameOptimizer.Shell/Views/BackupManagerPage.xaml", new[] { "首次安全基线", "CreateSnapshotCommand", "PrepareRestoreSnapshotCommand" }],
        ["GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml", new[] { "可配置进程", "CPU 核心分配", "ApplySingleProfileCommand" }],
        ["GameOptimizer.Shell/Views/PowerPlanEditorPage.xaml", new[] { "当前激活计划", "电源计划", "已验证设置", "BtnApplyAll_Click", "BtnRefreshPlans_Click", "BtnRestoreDefault_Click" }]
    ];

    [Theory]
    [MemberData(nameof(Pages))]
    public void ToolPageKeepsItsWebInformationStructureAndRealEntry(string relativePath, string[] requiredTokens)
    {
        var xaml = Read(relativePath);

        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        if (relativePath.EndsWith("CpuCoreManagementPage.xaml", StringComparison.Ordinal))
        {
            Assert.Contains("x:Name=\"LiveWorkspace\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"ProfileWorkspace\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("ToolPageContentScrollViewer", xaml, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("x:Name=\"ToolPageContentScrollViewer\"", xaml, StringComparison.Ordinal);
        }
        foreach (var token in requiredTokens)
            Assert.Contains(token, xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedToolSurfacesKeepWebHoverFeedbackAndNarrowHeaderTrimming()
    {
        var library = Read("GameOptimizer.Shell/Themes/YuqiComponentLibrary.xaml");
        var topbar = Read("GameOptimizer.Shell/Controls/ToolTopbar.xaml");

        Assert.Contains("x:Key=\"Yuqi.Tool.Surface.Interactive\"", library, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsMouseOver\" Value=\"True\"", library, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.CardBackgroundHover", library, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.CardBorderHover", library, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", topbar, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"150\"", topbar, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"130\"", topbar, StringComparison.Ordinal);
    }

    [Fact]
    public void PageSpecificClosureKeepsRepairFocusAndTruthfulCapabilityCopy()
    {
        var repair = Read("GameOptimizer.Shell/Views/RepairCenterPage.xaml");
        var network = Read("GameOptimizer.Shell/Views/NetworkTestPage.xaml");
        var restore = Read("GameOptimizer.Shell/Views/RestorePointPage.xaml");
        var restoreViewModel = Read("GameOptimizer.Shell/ViewModels/RestorePointViewModel.cs");
        var backup = Read("GameOptimizer.Shell/Views/BackupManagerPage.xaml");
        var backupViewModel = Read("GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs");

        Assert.Contains("x:Key=\"RepairItemCard\"", repair, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsKeyboardFocusWithin\" Value=\"True\"", repair, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource RepairItemCard}\"", repair, StringComparison.Ordinal);
        Assert.Contains("尚未开始测试", network, StringComparison.Ordinal);
        Assert.Contains("仅在完整测试成功后显示", network, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ProtectionStatusText", restore, StringComparison.Ordinal);
        Assert.Contains("ViewModel.RestorePointCountText", restore, StringComparison.Ordinal);
        Assert.DoesNotContain("保护空间配额不可用", restore, StringComparison.Ordinal);
        Assert.Contains("StorageVolumeDetail", backup, StringComparison.Ordinal);
        Assert.Contains("应用未设置容量上限", backupViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner 未返回空间数据", restore, StringComparison.Ordinal);
        Assert.DoesNotContain("当前 Owner", restoreViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerWorkspaceShowsOnlyCapabilityBackedCommands()
    {
        var power = Read("GameOptimizer.Shell/Views/PowerPlanEditorPage.xaml");

        Assert.Contains("PowerPlansList", power, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"200\"", power, StringComparison.Ordinal);
        Assert.Contains("Windows / OEM 计划保持只读", power, StringComparison.Ordinal);
        Assert.Contains("受管计划高级设置", power, StringComparison.Ordinal);
        Assert.Contains("SliderCpuMinimum", power, StringComparison.Ordinal);
        Assert.Contains("ManagedPlanSelector", power, StringComparison.Ordinal);
        Assert.Contains("BtnClonePlan_Click", power, StringComparison.Ordinal);
        Assert.Contains("BtnOpenIntelligentCreate_Click", power, StringComparison.Ordinal);
        Assert.Contains("智能创建电源计划", power, StringComparison.Ordinal);
        Assert.Contains("BtnActivateManagedPlan_Click", power, StringComparison.Ordinal);
        Assert.Contains("BtnApplyAll_Click", power, StringComparison.Ordinal);
        Assert.Contains("BtnRestoreDefault_Click", power, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerPlan_Click", power, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletePowerPlan_Click", power, StringComparison.Ordinal);
        Assert.DoesNotContain("新建空白计划", power, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleProcessor_Click", power, StringComparison.Ordinal);
        Assert.DoesNotContain("<Path Data=\"{x:Static constants:NavigationIconData.", power, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", power, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedPowerCreationPreviewKeepsCpuMatchingAndExplicitExtremeBoundary()
    {
        var dialog = Read("GameOptimizer.Shell/Views/ManagedPowerPlanCreateDialog.xaml");
        var codeBehind = Read("GameOptimizer.Shell/Views/ManagedPowerPlanCreateDialog.xaml.cs");
        var service = Read("GameOptimizer.Infrastructure.Windows/PowerManagement/ManagedPowerPlanService.cs");

        Assert.Contains("ExtremeModeCheckBox", dialog, StringComparison.Ordinal);
        Assert.Contains("本次将写入的已验证参数", dialog, StringComparison.Ordinal);
        Assert.Contains("PlanNameTextBox", dialog, StringComparison.Ordinal);
        Assert.Contains("MaximumPerformance", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_extremeProfile?.IsAvailable == true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("NormalizeCreatePlanName", service, StringComparison.Ordinal);
        Assert.Contains("自定义电源计划名称应为 1 到 80 个字符", service, StringComparison.Ordinal);
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
