// File: GameOptimizer.Core/Services/WindowsElevation.cs
// Why: 进程提权判定过去散落在 20+ 个文件里，各自 new WindowsPrincipal(...).IsInRole(Administrator)。
// 该判定在 UAC 筛选令牌（管理员组 SID 被标记为 deny-only）下返回 false，于是"以管理员身份运行"
// 之后部分功能仍显示"需要管理员"，而真正不可用的原因（策略托管/ACL/驱动）被吞掉。
// 这里收敛为唯一权威实现：先读令牌 TOKEN_ELEVATION，再读 TOKEN_GROUPS 判断管理员组 SID 是否存在，
// 从而区分「标准用户」「管理员账户但令牌未提权」「已提权管理员」三种真实状态。
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 进程令牌的真实提权级别。
/// </summary>
public enum ElevationLevel
{
    /// <summary>无法判定（非 Windows，或令牌读取失败）。</summary>
    Unknown = 0,

    /// <summary>当前账户不在管理员组内。</summary>
    StandardUser = 1,

    /// <summary>当前账户在管理员组内，但进程运行在 UAC 筛选令牌下（未提权）。</summary>
    AdministratorNotElevated = 2,

    /// <summary>进程令牌已提权，具备管理员写入权限。</summary>
    ElevatedAdministrator = 3
}

/// <summary>
/// 一次提权判定的完整结果。所有 UI/业务判定都应消费该对象，而不是各自重新判定。
/// </summary>
public sealed record ElevationSnapshot(
    ElevationLevel Level,
    bool IsElevated,
    bool IsAdministratorAccount,
    bool IsWindows,
    string ReasonCode,
    string Detail)
{
    /// <summary>是否可以向 HKLM / 系统服务 / Program Files 等机器级位置写入。</summary>
    public bool CanWriteMachineScope => IsElevated;

    internal static ElevationSnapshot Unknown(string detail, bool isWindows) =>
        new(ElevationLevel.Unknown, false, false, isWindows, WindowsElevation.ReasonProbeFailed, detail);
}

/// <summary>
/// 提权状态的唯一权威判定（single authoritative implementation）。
/// </summary>
public static class WindowsElevation
{
    public const string ReasonElevated = "elevation.elevated-administrator";
    public const string ReasonAdministratorNotElevated = "elevation.administrator-token-filtered";
    public const string ReasonStandardUser = "elevation.standard-user";
    public const string ReasonNonWindows = "elevation.non-windows";
    public const string ReasonProbeFailed = "elevation.probe-failed";

    private const int TokenGroupsInformationClass = 2;
    private const int TokenElevationInformationClass = 20;
    private const uint SeGroupUseForDenyOnly = 0x00000010;
    private const int ErrorInsufficientBuffer = 122;
    private const int MaxTokenGroupCount = 4096;

    private static readonly object Gate = new();
    private static readonly SecurityIdentifier BuiltinAdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private static ElevationSnapshot? _cached;
    private static Func<ElevationSnapshot>? _probeOverride;

    /// <summary>
    /// 当前进程的提权状态。令牌在进程生命周期内不可变，因此结果会被缓存；
    /// 只有判定失败（<see cref="ElevationLevel.Unknown"/>）时不缓存，以便下次重试。
    /// </summary>
    public static ElevationSnapshot Current
    {
        get
        {
            lock (Gate)
            {
                if (_probeOverride is { } probe)
                {
                    return probe();
                }

                if (_cached is { } cached)
                {
                    return cached;
                }

                var snapshot = Probe();
                if (snapshot.Level != ElevationLevel.Unknown)
                {
                    _cached = snapshot;
                }

                return snapshot;
            }
        }
    }

    public static ElevationLevel Level => Current.Level;

    public static bool IsElevated => Current.IsElevated;

    /// <summary>当前账户是否属于管理员组（即使令牌被 UAC 筛选）。</summary>
    public static bool IsAdministratorAccount => Current.IsAdministratorAccount;

    /// <summary>管理员账户 + 未提权：需要重新以管理员身份启动，而不是"缺少管理员账户"。</summary>
    public static bool IsAdministratorAccountWithoutElevation =>
        Current.Level == ElevationLevel.AdministratorNotElevated;

    /// <summary>丢弃缓存并重新判定。</summary>
    public static void Refresh()
    {
        lock (Gate)
        {
            _cached = null;
        }
    }

    /// <summary>
    /// 直接探测（不读缓存）。用于诊断报告与自检。
    /// </summary>
    public static ElevationSnapshot Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ElevationSnapshot(
                ElevationLevel.Unknown,
                false,
                false,
                false,
                ReasonNonWindows,
                "非 Windows 平台无法判定进程提权状态。");
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            // WindowsIdentity.Token 返回 IntPtr(nint)，只有 AccessToken 才是 SafeAccessTokenHandle。
            var handle = identity.AccessToken.DangerousGetHandle();

            var tokenElevationKnown = TryReadTokenElevation(handle, out var tokenElevated);
            var groupsKnown = TryReadAdministratorsGroup(handle, out var groupPresent, out var groupDenyOnly);

            if (!tokenElevationKnown && !groupsKnown)
            {
                // 令牌 API 不可用时退回托管判定，绝不因此把已提权进程判成非管理员。
                var isInRole = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                return new ElevationSnapshot(
                    isInRole ? ElevationLevel.ElevatedAdministrator : ElevationLevel.StandardUser,
                    isInRole,
                    isInRole,
                    true,
                    isInRole ? ReasonElevated : ReasonStandardUser,
                    isInRole
                        ? "令牌提权接口不可用；已通过 WindowsPrincipal.IsInRole 判定为管理员。"
                        : "令牌提权接口不可用；WindowsPrincipal.IsInRole 未报告管理员权限。");
            }

            var elevated = tokenElevationKnown
                ? tokenElevated
                : groupPresent && !groupDenyOnly;
            var administratorAccount = elevated || (groupsKnown && groupPresent);

            if (elevated)
            {
                return new ElevationSnapshot(
                    ElevationLevel.ElevatedAdministrator,
                    true,
                    true,
                    true,
                    ReasonElevated,
                    "进程令牌已提权（TokenElevation=1），具备管理员写入权限。");
            }

            if (administratorAccount)
            {
                return new ElevationSnapshot(
                    ElevationLevel.AdministratorNotElevated,
                    false,
                    true,
                    true,
                    ReasonAdministratorNotElevated,
                    "当前账户属于管理员组，但进程运行在 UAC 筛选令牌下（Administrators SID 为 deny-only），未获得提权。");
            }

            return new ElevationSnapshot(
                ElevationLevel.StandardUser,
                false,
                false,
                true,
                ReasonStandardUser,
                "当前账户不在管理员组内，无法获得提权令牌。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ElevationSnapshot.Unknown($"读取进程令牌失败：{ex.GetType().Name}: {ex.Message}", true);
        }
    }

    /// <summary>
    /// 面向用户的不可用原因：把"缺少管理员权限"与"系统/策略拒绝"分开。
    /// </summary>
    public static string DescribeMachineScopeUnavailable()
    {
        var snapshot = Current;
        return snapshot.ReasonCode switch
        {
            ReasonElevated => "进程已提权；若系统仍拒绝写入，原因是目标位置被策略、ACL 或安全软件保护，而不是缺少管理员权限。",
            ReasonAdministratorNotElevated => "当前账户属于管理员组，但进程令牌未提权（UAC 筛选令牌）。请退出后用「以管理员身份运行」重新启动宇奇引擎。",
            ReasonStandardUser => "当前账户不在管理员组内，无法获得管理员权限。请使用管理员账户登录后重新启动宇奇引擎。",
            ReasonNonWindows => "当前平台不是 Windows，机器级写入不可用。",
            _ => "无法读取进程令牌以确认提权状态；已按最小权限处理。"
        };
    }

    /// <summary>
    /// 供测试注入判定结果，避免测试依赖宿主进程的真实提权状态。
    /// </summary>
    internal static IDisposable OverrideProbeForTesting(Func<ElevationSnapshot> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var previous = _probeOverride;
        _probeOverride = probe;
        Refresh();
        return new ProbeOverrideScope(previous);
    }

    private sealed class ProbeOverrideScope(Func<ElevationSnapshot>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _probeOverride = previous;
            Refresh();
        }
    }

    private static bool TryReadTokenElevation(IntPtr handle, out bool isElevated)
    {
        isElevated = false;
        var size = sizeof(int);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(handle, TokenElevationInformationClass, buffer, size, out _))
            {
                return false;
            }

            isElevated = Marshal.ReadInt32(buffer) != 0;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryReadAdministratorsGroup(IntPtr handle, out bool present, out bool denyOnly)
    {
        present = false;
        denyOnly = false;

        if (!GetTokenInformation(handle, TokenGroupsInformationClass, IntPtr.Zero, 0, out var required)
            && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
        {
            return false;
        }

        if (required <= 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(required);
        try
        {
            if (!GetTokenInformation(handle, TokenGroupsInformationClass, buffer, required, out _))
            {
                return false;
            }

            var count = Marshal.ReadInt32(buffer);
            if (count <= 0)
            {
                return false;
            }

            if (count > MaxTokenGroupCount)
            {
                count = MaxTokenGroupCount;
            }

            // TOKEN_GROUPS：DWORD GroupCount + 对齐到指针宽度的 SID_AND_ATTRIBUTES 数组。
            var stride = IntPtr.Size == 8 ? 16 : 8;
            var firstEntry = IntPtr.Add(buffer, IntPtr.Size);
            for (var index = 0; index < count; index++)
            {
                var entry = IntPtr.Add(firstEntry, index * stride);
                var sidPointer = Marshal.ReadIntPtr(entry);
                if (sidPointer == IntPtr.Zero)
                {
                    continue;
                }

                var attributes = unchecked((uint)Marshal.ReadInt32(IntPtr.Add(entry, IntPtr.Size)));
                if (!string.Equals(
                        new SecurityIdentifier(sidPointer).Value,
                        BuiltinAdministratorsSid.Value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                present = true;
                if ((attributes & SeGroupUseForDenyOnly) != 0)
                {
                    denyOnly = true;
                }

                break;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}
