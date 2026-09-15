// File: GameOptimizer.Core/Services/ElevationGate.cs
// Why: 「管理员」必须是完整体系，而不是散落的 if (IsAdmin) + 硬编码文案。
// 本类型是全仓唯一的「提权判定 + 用户归因 + 失败文案」权威入口：
// - 判定真相只来自 WindowsElevation（令牌）；
// - 已提权时任何路径都不得再要求用户「以管理员身份运行」；
// - 未提权时必须区分 UAC 筛选令牌 vs 标准账户；
// - 访问被拒绝在已提权时必须归因到策略/ACL/安全软件，而不是缺权限。
using System;

namespace GameOptimizer.Core.Services;

/// <summary>失败/阻断与提权的真实关系。UI 与诊断只能消费本枚举，不得自行猜测。</summary>
public enum ElevationAttribution
{
    /// <summary>与提权无关（策略、设备、兼容性、取消等）。</summary>
    Unrelated = 0,

    /// <summary>进程未提权；用户需要重新以管理员身份启动。</summary>
    MissingElevation = 1,

    /// <summary>账户在管理员组，但进程是 UAC 筛选令牌。</summary>
    UacFilteredToken = 2,

    /// <summary>进程已提权，但 Windows 仍拒绝；原因是策略/ACL/安全软件/占用。</summary>
    SystemDenied = 3,

    /// <summary>无法读取令牌，按最小权限处理；不得假装「再提权就能好」。</summary>
    ProbeUnavailable = 4
}

/// <summary>一次提权相关的统一裁决。</summary>
public sealed record ElevationDecision(
    bool NeedsElevation,
    ElevationAttribution Attribution,
    string UserMessage,
    ElevationSnapshot Snapshot)
{
    public bool IsElevated => Snapshot.IsElevated;
    public bool IsAdministratorAccount => Snapshot.IsAdministratorAccount;
}

/// <summary>
/// 全仓唯一 Elevation 门禁。所有「是否需要管理员 / 提示说什么 / 失败算谁的」都应经过这里。
/// </summary>
public static class ElevationGate
{
    public static ElevationSnapshot Snapshot => WindowsElevation.Current;

    public static bool IsElevated => WindowsElevation.IsElevated;

    public static bool IsAdministratorAccount => WindowsElevation.IsAdministratorAccount;

    public static ElevationLevel Level => WindowsElevation.Level;

    public static ElevationAttribution AttributeFromSnapshot(ElevationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Level switch
        {
            ElevationLevel.ElevatedAdministrator => ElevationAttribution.SystemDenied,
            ElevationLevel.AdministratorNotElevated => ElevationAttribution.UacFilteredToken,
            ElevationLevel.StandardUser => ElevationAttribution.MissingElevation,
            _ => ElevationAttribution.ProbeUnavailable
        };
    }

    /// <summary>
    /// 操作本身是否被提权状态阻断。
    /// 已提权 ⇒ NeedsElevation=false（即使调用方声称 requiresAdministrator）。
    /// </summary>
    public static ElevationDecision Evaluate(
        string operationName,
        bool operationRequiresAdministrator,
        string? technicalDetail = null)
    {
        var snapshot = WindowsElevation.Current;
        return Evaluate(operationName, operationRequiresAdministrator, snapshot, technicalDetail);
    }

    public static ElevationDecision Evaluate(
        string operationName,
        bool operationRequiresAdministrator,
        ElevationSnapshot snapshot,
        string? technicalDetail = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var operation = string.IsNullOrWhiteSpace(operationName) ? "系统写入" : operationName;

        if (!operationRequiresAdministrator)
        {
            return new ElevationDecision(
                NeedsElevation: false,
                ElevationAttribution.Unrelated,
                BuildUnrelatedOrSystemDenied(operation, technicalDetail, snapshot),
                snapshot);
        }

        if (snapshot.IsElevated)
        {
            // Why: 声明位说需要管理员，但令牌已提权 ⇒ 不能再让用户去提权。
            return new ElevationDecision(
                NeedsElevation: false,
                ElevationAttribution.SystemDenied,
                GetSystemDeniedMessage(operation, technicalDetail),
                snapshot);
        }

        return new ElevationDecision(
            NeedsElevation: true,
            snapshot.IsAdministratorAccount
                ? ElevationAttribution.UacFilteredToken
                : ElevationAttribution.MissingElevation,
            GetAdminRequiredMessage(operation, snapshot),
            snapshot);
    }

    /// <summary>
    /// 写入失败/拒绝访问时的统一用户文案。已提权永不返回「请以管理员身份运行」。
    /// </summary>
    public static string DescribeWriteDenial(string operationName, string? technicalDetail = null)
    {
        var snapshot = WindowsElevation.Current;
        return DescribeWriteDenial(operationName, snapshot, technicalDetail);
    }

    public static string DescribeWriteDenial(string operationName, ElevationSnapshot snapshot, string? technicalDetail = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var operation = string.IsNullOrWhiteSpace(operationName) ? "系统写入" : operationName;
        if (snapshot.IsElevated)
        {
            return GetSystemDeniedMessage(operation, technicalDetail);
        }

        return GetAdminRequiredMessage(operation, snapshot, technicalDetail);
    }

    /// <summary>短标签：适合 Toast / StatusText 一行摘要。</summary>
    public static string ShortLabel(ElevationAttribution attribution) => attribution switch
    {
        ElevationAttribution.MissingElevation => "需要管理员权限",
        ElevationAttribution.UacFilteredToken => "需要提升权限（UAC）",
        ElevationAttribution.SystemDenied => "系统拒绝了写入（非权限不足）",
        ElevationAttribution.ProbeUnavailable => "无法确认提权状态",
        _ => "操作未完成"
    };

    /// <summary>
    /// 把失败文本/异常归因到提权体系。已提权时「拒绝访问」不得归为 MissingElevation。
    /// </summary>
    public static ElevationAttribution AttributeFailure(string? message, Exception? exception = null)
    {
        var text = Combine(message, exception?.Message);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ElevationAttribution.Unrelated;
        }

        var looksLikeAccessDenial =
            exception is UnauthorizedAccessException or System.Security.SecurityException
            || ContainsAny(text,
                "permission", "access denied", "administrator", "admin required",
                "权限", "拒绝访问", "无权限", "permission.administrator-required");

        if (!looksLikeAccessDenial)
        {
            return ElevationAttribution.Unrelated;
        }

        var snapshot = WindowsElevation.Current;
        if (snapshot.IsElevated)
        {
            return ElevationAttribution.SystemDenied;
        }

        return snapshot.IsAdministratorAccount
            ? ElevationAttribution.UacFilteredToken
            : ElevationAttribution.MissingElevation;
    }

    /// <summary>按失败文本生成用户文案（内部会读真实提权状态）。</summary>
    public static string DescribeFailure(string operationName, string? technicalDetail = null)
        => DescribeWriteDenial(operationName, technicalDetail);

    public static bool CanRestartAsAdmin() => !IsElevated && IsAdministratorAccount;

    private static string BuildUnrelatedOrSystemDenied(
        string operation,
        string? technicalDetail,
        ElevationSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(technicalDetail))
        {
            return $"操作「{operation}」未完成；与管理员权限无关。";
        }

        return snapshot.IsElevated
            ? GetSystemDeniedMessage(operation, technicalDetail)
            : $"操作「{operation}」未完成：{technicalDetail}";
    }

    private static string GetAdminRequiredMessage(
        string operation,
        ElevationSnapshot snapshot,
        string? technicalDetail = null)
    {
        var identity = snapshot.IsAdministratorAccount
            ? "当前账户在管理员组内，但进程令牌未提权（UAC 筛选令牌）。"
            : "当前账户不在管理员组内。";

        var message =
            $"【需要管理员权限】\n\n" +
            $"操作 \"{operation}\" 需要管理员权限才能执行。\n" +
            $"{identity}\n\n" +
            $"💡 解决方案：\n" +
            $"1. 关闭程序\n" +
            $"2. 右键程序图标\n" +
            $"3. 选择\"以管理员身份运行\"\n\n" +
            $"提权判定 / Elevation: {snapshot.ReasonCode} - {snapshot.Detail}";

        return string.IsNullOrWhiteSpace(technicalDetail)
            ? message
            : $"{message}\n\n技术信息：{technicalDetail}";
    }

    private static string GetSystemDeniedMessage(string operation, string? technicalDetail)
    {
        var message =
            $"【系统拒绝了写入】\n\n" +
            $"操作 \"{operation}\" 已在管理员模式下执行（进程令牌已提权），但 Windows 返回了拒绝访问。\n" +
            $"真实原因通常是：组织或系统策略托管、目标注册表/文件 ACL 限制、安全软件拦截、驱动或服务占用。\n\n" +
            $"管理员权限不是本次失败的原因，重复提权不会解决问题。";

        return string.IsNullOrWhiteSpace(technicalDetail)
            ? message
            : $"{message}\n\n技术信息：{technicalDetail}";
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Combine(params string?[] values) =>
        string.Join(" ", Array.FindAll(values, static v => !string.IsNullOrWhiteSpace(v)));
}
