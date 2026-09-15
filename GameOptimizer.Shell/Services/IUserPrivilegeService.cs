using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 进程提权状态的唯一读取入口（UI 侧）。
/// 判定结果来自 <see cref="WindowsElevation"/>，本服务只做缓存与变更通知。
/// </summary>
public interface IUserPrivilegeService
{
    /// <summary>进程令牌是否已提权（真实管理员权限）。</summary>
    bool IsAdministrator { get; }

    /// <summary>重新读取权威提权快照（不会改变已提权令牌，仅纠正探测失败/过期 UI 状态）。</summary>
    void EnsureAuthoritativeSnapshot();

    /// <summary>当前账户是否属于管理员组（即使令牌被 UAC 筛选）。</summary>
    bool IsAdministratorAccount { get; }

    /// <summary>真实提权级别。</summary>
    ElevationLevel ElevationLevel { get; }

    /// <summary>提权判定的人类可读原因（含失败时读取令牌的错误信息）。</summary>
    string ElevationDetail { get; }

    /// <summary>判定原因码，例如 elevation.administrator-token-filtered。</summary>
    string ElevationReasonCode { get; }

    void Refresh();
}
