namespace GameOptimizer.Shell.Models;

/// <summary>
/// 将执行器的原始结果转换为面向用户的原因说明。
/// 不把策略、设备或回读失败误报成管理员权限问题。
/// </summary>
public static class OptimizationResultMessageFormatter
{
    public static string Format(string? rawMessage, bool isSuccess, bool isSkipped, bool isAdministrator)
    {
        var raw = rawMessage?.Trim() ?? string.Empty;
        if (isSuccess)
        {
            return string.IsNullOrWhiteSpace(raw) ? "已完成并通过状态回读验证。" : raw;
        }

        if (isSkipped)
        {
            return string.IsNullOrWhiteSpace(raw) ? "当前环境不适用，未写入任何系统设置。" : raw;
        }

        if (Contains(raw, "administrator", "管理员权限", "permission denied", "access denied", "拒绝访问"))
        {
            // Why: 统一走 ElevationGate，已提权时绝不返回「请以管理员身份重启」。
            return WithDetail(
                GameOptimizer.Core.Services.ElevationGate.DescribeFailure("优化写入", raw),
                raw);
        }

        if (Contains(raw, "policy", "managed", "策略", "受管理"))
        {
            return WithDetail("该设置受 Windows、组织策略或安全产品管理，软件没有绕过该限制；本次未写入系统。", raw);
        }

        if (Contains(raw, "unsupported", "not supported", "不支持", "not applicable", "不适用"))
        {
            return WithDetail("当前 Windows 版本、硬件或驱动不支持此项设置，软件未执行写入。", raw);
        }

        if (Contains(raw, "timed out", "timeout", "超时"))
        {
            return WithDetail("操作等待系统响应超时，软件没有自动重试。请刷新状态后确认是否已生效，再决定是否重试。", raw);
        }

        if (Contains(raw, "verification", "verify", "回读", "确认目标"))
        {
            return WithDetail("系统写入后未通过独立状态回读验证，因此不会被标记为已应用。请查看详细信息并刷新状态。", raw);
        }

        return WithDetail("应用失败，系统未返回可确认的成功状态。请查看技术信息后重试。", raw);
    }

    private static bool Contains(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string WithDetail(string message, string detail) =>
        string.IsNullOrWhiteSpace(detail) ? message : $"{message}{Environment.NewLine}技术信息：{detail}";
}
