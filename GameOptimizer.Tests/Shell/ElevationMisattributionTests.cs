using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;
using EngineOperationStatus = GameOptimizer.Core.Optimization.OperationStatus;

namespace GameOptimizer.Tests.Shell;

/// <summary>
/// 已提权进程上仍出现「需要管理员」的误归因回归：UI 不得再把 PermissionRequired
/// 当成真实阻断，也不得把拒绝访问在已提权时归因成缺管理员。
/// </summary>
public sealed class ElevationMisattributionTests
{
    [Fact]
    public void ElevatedProcess_IgnoresStalePermissionRequiredAndAllowsApply()
    {
        var model = new OptimizationToggleModel(
            "示例优化",
            operationId: "test.elevated",
            operationRoute: OptimizationOperationRoute.TypedExecutable,
            requiresAdmin: true,
            isAdministrator: true);

        model.SyncOperationContract(
            new EngineOperationStatus(
                false,
                "该开关受支持，但当前进程没有管理员权限，已禁止执行。",
                IsKnown: true,
                SupportState: OperationSupportState.PermissionRequired,
                SupportReasonCode: "permission.administrator-required"),
            isAdministrator: true);

        Assert.True(model.CanToggle);
        Assert.True(model.CanApply);
        Assert.Equal(ModuleCapabilityState.Ready, model.CapabilityState);
        Assert.NotEqual(ModuleCapabilityState.AdminRequired, model.CapabilityState);
    }

    [Fact]
    public void UnelevatedProcess_StillReportsAdminRequired()
    {
        var model = new OptimizationToggleModel(
            "示例优化",
            operationId: "test.unelevated",
            operationRoute: OptimizationOperationRoute.TypedExecutable,
            requiresAdmin: true,
            isAdministrator: false);

        model.SyncOperationContract(
            new EngineOperationStatus(
                false,
                "需要管理员权限。",
                IsKnown: true,
                SupportState: OperationSupportState.PermissionRequired,
                SupportReasonCode: "permission.administrator-required"),
            isAdministrator: false);

        Assert.False(model.CanToggle);
        Assert.Equal(ModuleCapabilityState.AdminRequired, model.CapabilityState);
    }

    [Fact]
    public void ElevatedAdminRequiredMessage_DoesNotTellUserToRestartAsAdmin()
    {
        var message = PermissionChecker.GetAdminRequiredMessage(
            "测试操作",
            isElevated: true,
            isAdministratorAccount: true);

        Assert.DoesNotContain("请以管理员身份重新启动", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Restart the app as administrator", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("系统拒绝", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedUnauthorizedAccessException_IsNotClassifiedAsMissingAdmin()
    {
        var presentation = GameOptimizer.Shell.Services.CommandResultPresentationMapper.FromException(
            new UnauthorizedAccessException("Access to the registry path is denied."));

        // 真实提权状态依赖宿主令牌；这里只断言分类器在「已提权」分支不会返回 PermissionDenied。
        if (WindowsElevation.IsElevated)
        {
            Assert.NotEqual(GameOptimizer.Shell.Services.CommandResultStatus.PermissionDenied, presentation.Status);
            Assert.Equal(GameOptimizer.Shell.Services.CommandResultStatus.Blocked, presentation.Status);
            Assert.DoesNotContain("需要管理员权限", presentation.DisplayText, StringComparison.Ordinal);
        }
    }
}
