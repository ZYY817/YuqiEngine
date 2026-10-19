// File: GameOptimizer.Tests/Shell/AdminGatingTests.cs
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.ViewModels;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Tests.Shell;

/// <summary>
/// 管理员门禁误判回归测试。
/// 目标不变式：进程令牌**已经提权**时，不得再让用户"以管理员身份运行/重试"，必须给出真实不可用原因。
/// Why: 本机无法真实复现 UAC 提权会话，所以这里断言的是"已提权"输入下的文案与判定结果，
///      属于模拟条件下的自动化验证，不代表真机提权行为已被验证。
/// </summary>
public sealed class AdminGatingTests
{
    [Fact]
    public void ElevationGate_DoesNotAskForAdminWhenProcessIsAlreadyElevated()
    {
        var gate = PermissionChecker.EvaluateElevationGate(
            "修改进程亲和性",
            isElevated: true,
            isAdministratorAccount: true,
            technicalDetail: "Win32 错误 5：拒绝访问");

        Assert.False(gate.NeedsAdmin);
        Assert.Contains("管理员权限不是本次失败的原因", gate.Message, StringComparison.Ordinal);
        Assert.Contains("Win32 错误 5", gate.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("请以管理员身份运行", gate.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("以管理员身份重新启动", gate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminRequiredMessage_IsNotReturnedForAnElevatedProcess()
    {
        var elevated = PermissionChecker.GetAdminRequiredMessage(
            "修改进程亲和性",
            isElevated: true,
            isAdministratorAccount: true,
            elevationReasonCode: "Elevated",
            elevationDetail: "令牌已提权");

        Assert.DoesNotContain("【需要管理员权限】", elevated, StringComparison.Ordinal);
        Assert.DoesNotContain("请以管理员身份运行", elevated, StringComparison.Ordinal);
        Assert.Contains("管理员权限不是本次失败的原因", elevated, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevationGate_AsksForAdminOnlyWhenProcessIsNotElevated()
    {
        var adminAccount = PermissionChecker.EvaluateElevationGate(
            "删除系统文件",
            isElevated: false,
            isAdministratorAccount: true,
            elevationReasonCode: "AdministratorNotElevated",
            elevationDetail: "UAC 筛选令牌");

        Assert.True(adminAccount.NeedsAdmin);
        Assert.Contains("UAC 筛选令牌", adminAccount.Message, StringComparison.Ordinal);
        Assert.Contains("以管理员身份运行", adminAccount.Message, StringComparison.Ordinal);

        var standardAccount = PermissionChecker.EvaluateElevationGate(
            "删除系统文件",
            isElevated: false,
            isAdministratorAccount: false,
            elevationReasonCode: "StandardUser",
            elevationDetail: "不在管理员组");

        Assert.True(standardAccount.NeedsAdmin);
        Assert.Contains("不在管理员组内", standardAccount.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanRestartAsAdmin_ReadsRealElevationInsteadOfMainModule()
    {
        var expected = !PermissionChecker.IsRunningAsAdministrator() && PermissionChecker.IsAdministratorAccount();
        Assert.Equal(expected, PermissionChecker.CanRestartAsAdmin());

        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "GameOptimizer.Core",
            "Services",
            "PermissionChecker.cs"));

        var start = source.IndexOf("public static bool CanRestartAsAdmin", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = source[start..];

        Assert.DoesNotContain("MainModule", body, StringComparison.Ordinal);
        Assert.Contains("ElevationGate.CanRestartAsAdmin", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CpuCoreManagementPermissionHint_DoesNotTellAnElevatedUserToElevateAgain()
    {
        var elevated = CpuCoreManagementViewModel.BuildPermissionHint(
            isElevated: true,
            isAdministratorAccount: true,
            reasonCode: "Elevated",
            detail: "TOKEN_ELEVATION=1");

        Assert.Contains("已提权", elevated, StringComparison.Ordinal);
        Assert.DoesNotContain("请以管理员身份", elevated, StringComparison.Ordinal);
        Assert.DoesNotContain("需要以管理员身份重新启动", elevated, StringComparison.Ordinal);

        var normal = CpuCoreManagementViewModel.BuildPermissionHint(
            isElevated: false,
            isAdministratorAccount: true,
            reasonCode: "AdministratorNotElevated",
            detail: "UAC 筛选令牌");

        Assert.Contains("需要以管理员身份重新启动", normal, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessPriorityBlockingMessage_StatesRealReasonAndNeverAsksForAdminRetry()
    {
        var elevated = ProcessPriorityPage.BuildBlockingMessage(isElevated: true);
        var normal = ProcessPriorityPage.BuildBlockingMessage(isElevated: false);

        foreach (var message in new[] { elevated, normal })
        {
            Assert.Contains("IFEO", message, StringComparison.Ordinal);
            Assert.DoesNotContain("请以管理员身份运行", message, StringComparison.Ordinal);
            Assert.DoesNotContain("重试", message, StringComparison.Ordinal);
        }

        Assert.Contains("已提权", elevated, StringComparison.Ordinal);
        Assert.Contains("不是权限不足", elevated, StringComparison.Ordinal);
        Assert.Contains("不是权限问题", normal, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessPriorityPage_AdminPrivilegeCheckReadsRealElevation()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "GameOptimizer.Shell",
            "Views",
            "ProcessPriorityPage.xaml.cs"));

        var start = source.IndexOf("private void CheckAdminPrivileges()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = source[start..(start + 400)];

        Assert.Contains("GameOptimizer.Core.Services.WindowsElevation.IsElevated", body, StringComparison.Ordinal);
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
