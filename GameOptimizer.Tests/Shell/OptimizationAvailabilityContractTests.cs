using GameOptimizer.Shell.Models;

namespace GameOptimizer.Tests.Shell;

public sealed class OptimizationAvailabilityContractTests
{
    [Fact]
    public void ManualOptimizationPath_DoesNotContainTemporaryBlockLists()
    {
        var root = FindRepositoryRoot();
        var dashboard = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "ViewModels", "DashboardViewModel.cs"));
        var engine = File.ReadAllText(Path.Combine(root, "GameOptimizer.Core", "Optimization", "OptimizationEngine.cs"));
        var guardStart = dashboard.IndexOf("private ApplyGuardResult EvaluateApplyGuard", StringComparison.Ordinal);
        var guardEnd = dashboard.IndexOf("private void AppendApplyGuardLogs", StringComparison.Ordinal);

        Assert.True(guardStart >= 0 && guardEnd > guardStart);
        var manualGuard = dashboard[guardStart..guardEnd];

        Assert.DoesNotContain("GloballyBlockedOperationIds", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("GloballyFrozenApplyOperationIds", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("OptimizeNowCommand", manualGuard, StringComparison.Ordinal);
        Assert.DoesNotContain("OptimizeNowAsync", manualGuard, StringComparison.Ordinal);
        Assert.DoesNotContain("OneClickPlanBuildResult", manualGuard, StringComparison.Ordinal);
        Assert.DoesNotContain("默认禁用", manualGuard, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyGlobalModeCommand", dashboard, StringComparison.Ordinal);
        Assert.Contains("\"ModulePage\").ConfigureAwait", dashboard, StringComparison.Ordinal);
        Assert.Contains("\"CrossModule\").ConfigureAwait", dashboard, StringComparison.Ordinal);

        var backupGateStart = dashboard.IndexOf("private async Task<(bool Allowed, string? BlockReason)> EnsureApplyBackupGateAsync", StringComparison.Ordinal);
        var backupGateEnd = dashboard.IndexOf("private OperationExecutionSummary CreateBlockedOperationSummary", StringComparison.Ordinal);
        Assert.True(backupGateStart >= 0 && backupGateEnd > backupGateStart);
        var backupGate = dashboard[backupGateStart..backupGateEnd];
        Assert.Contains("if (requiresRestorePointCount <= 0)", backupGate, StringComparison.Ordinal);
        Assert.Contains("typed 快照和回读保护", backupGate, StringComparison.Ordinal);
        Assert.DoesNotContain("nonSafeOperationCount <= 0 && requiresRestorePointCount <= 0", backupGate, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureMessage_DistinguishesAdminModeFromAccessDenied()
    {
        var normal = OptimizationResultMessageFormatter.Format(
            "Administrator permission is required.", false, false, isAdministrator: false);
        var elevated = OptimizationResultMessageFormatter.Format(
            "Access denied while writing the target value.", false, false, isAdministrator: true);

        Assert.Contains("以管理员身份重新启动", normal, StringComparison.Ordinal);
        Assert.Contains("已检测到管理员模式", elevated, StringComparison.Ordinal);
        Assert.DoesNotContain("以管理员身份重新启动", elevated, StringComparison.Ordinal);
    }

    [Fact]
    public void EnableSystemRestoreEntry_OpensWindowsProtectionSettingsInsteadOfDeadFailClosedButton()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "ViewModels", "RestorePointViewModel.cs"));
        var methodStart = source.IndexOf("private Task EnableSystemRestoreAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void SetManualRestoreState", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("_manualLauncher.OpenProtectionSettings()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("当前系统接口不提供该写入", method, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureMessage_ExplainsPolicyAndVerificationWithoutMislabelingPermission()
    {
        var policy = OptimizationResultMessageFormatter.Format(
            "PolicyDenied: managed by organization policy.", false, false, isAdministrator: true);
        var verification = OptimizationResultMessageFormatter.Format(
            "Independent state verification did not confirm the target.", false, false, isAdministrator: true);

        Assert.Contains("组织策略", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("管理员模式", policy, StringComparison.Ordinal);
        Assert.Contains("状态回读验证", verification, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationManifest_RequestsHighestAvailablePrivilegeForSystemOperations()
    {
        var manifest = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "GameOptimizer.Shell",
            "app.manifest"));

        Assert.Contains("requestedExecutionLevel level=\"highestAvailable\"", manifest, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
