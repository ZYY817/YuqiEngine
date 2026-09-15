// File: GameOptimizer.Core/Services/PermissionChecker.cs
// Why: 历史入口保留转发；真正的体系入口是 ElevationGate。
using System;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 权限检查助手。判定与文案已收敛到 <see cref="ElevationGate"/> / <see cref="WindowsElevation"/>。
/// </summary>
public static class PermissionChecker
{
    public static bool IsRunningAsAdministrator() => ElevationGate.IsElevated;

    public static bool IsAdministratorAccount() => ElevationGate.IsAdministratorAccount;

    public static ElevationLevel CurrentElevationLevel => ElevationGate.Level;

    public static (bool HasPermission, string? ErrorMessage) CheckOperationPermission(string operationName)
    {
        var decision = ElevationGate.Evaluate(operationName, operationRequiresAdministrator: true);
        if (decision.NeedsElevation)
        {
            return (false, decision.UserMessage);
        }

        // 已提权时权限条件已满足；后续失败属于系统拒绝，不再在此预判。
        return (true, null);
    }

    public static string GetAdminRequiredMessage(string operationName)
    {
        var snapshot = ElevationGate.Snapshot;
        return GetAdminRequiredMessage(
            operationName,
            snapshot.IsElevated,
            snapshot.IsAdministratorAccount,
            snapshot.ReasonCode,
            snapshot.Detail);
    }

    public static string GetAdminRequiredMessage(
        string operationName,
        bool isElevated,
        bool isAdministratorAccount,
        string? elevationReasonCode = null,
        string? elevationDetail = null)
    {
        var snapshot = new ElevationSnapshot(
            isElevated
                ? ElevationLevel.ElevatedAdministrator
                : isAdministratorAccount
                    ? ElevationLevel.AdministratorNotElevated
                    : ElevationLevel.StandardUser,
            isElevated,
            isAdministratorAccount,
            true,
            elevationReasonCode ?? (isElevated ? WindowsElevation.ReasonElevated : WindowsElevation.ReasonStandardUser),
            elevationDetail ?? string.Empty);

        return ElevationGate.DescribeWriteDenial(operationName, snapshot);
    }

    public static (bool NeedsAdmin, string Message) EvaluateElevationGate(
        string operationName,
        bool isElevated,
        bool isAdministratorAccount,
        string? technicalDetail = null,
        string? elevationReasonCode = null,
        string? elevationDetail = null)
    {
        var snapshot = new ElevationSnapshot(
            isElevated
                ? ElevationLevel.ElevatedAdministrator
                : isAdministratorAccount
                    ? ElevationLevel.AdministratorNotElevated
                    : ElevationLevel.StandardUser,
            isElevated,
            isAdministratorAccount,
            true,
            elevationReasonCode ?? (isElevated ? WindowsElevation.ReasonElevated : WindowsElevation.ReasonStandardUser),
            elevationDetail ?? string.Empty);

        var decision = ElevationGate.Evaluate(
            operationName,
            operationRequiresAdministrator: true,
            snapshot,
            technicalDetail);

        return (decision.NeedsElevation, decision.UserMessage);
    }

    public static string GetSystemDeniedMessage(string operationName, string? technicalDetail = null)
    {
        var message =
            $"【系统拒绝了写入】\n\n" +
            $"操作 \"{operationName}\" 已在管理员模式下执行（进程令牌已提权），但 Windows 返回了拒绝访问。\n" +
            $"真实原因通常是：组织或系统策略托管、目标注册表/文件 ACL 限制、安全软件拦截、驱动或服务占用。\n\n" +
            $"管理员权限不是本次失败的原因，重复提权不会解决问题。";

        return string.IsNullOrWhiteSpace(technicalDetail)
            ? message
            : $"{message}\n\n技术信息：{technicalDetail}";
    }

    public static bool CanRestartAsAdmin() => ElevationGate.CanRestartAsAdmin();
}
