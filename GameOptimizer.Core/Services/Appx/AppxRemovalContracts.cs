// File: GameOptimizer.Core/Services/Appx/AppxRemovalContracts.cs
using System;
using System.Collections.Generic;

namespace GameOptimizer.Core.Services.Appx;

public enum AppxRecoveryClass
{
    StagedRegister,
    StoreOnly
}

/// <summary>AppX 当前用户移除的允许名单条目；只有经目录复核的条目才允许构造能力实例。</summary>
public sealed record AppxRemovalEntry(
    string FamilyName,
    string DisplayName,
    string ProductId,
    AppxRecoveryClass RecoveryClass,
    bool Allowed);

public sealed record AppxPlatformAssessment(
    bool HasRequiredPermission,
    string ReasonCode,
    string Details);

public sealed record AppxPackageState(
    string Name,
    string FullName,
    string FamilyName,
    string Version,
    string InstallLocation,
    bool Installed,
    bool IsStaged,
    bool NonRemovable,
    bool IsFramework,
    int DependencyCount,
    string SignatureKind,
    string? ErrorCode);

public sealed record AppxMutationResult(
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message);

/// <summary>
/// A4 AppX 当前用户包移除的唯一平台通道。语义边界：只做当前用户注册解除与 Staged 载荷重注册，
/// 禁止 AllUsers 移除、禁止预置（Provisioned）操作、禁止通配符批量移除。
/// </summary>
public interface IAppxRemovalOwner
{
    Task<AppxPlatformAssessment> ProbeAsync(CancellationToken cancellationToken);

    /// <summary>枚举当前用户已注册的主包（Main），供自选卸载页展示；含不可移除项（Removable=false，UI 禁勾）。</summary>
    Task<IReadOnlyList<AppxCatalogEntry>> EnumerateInstalledAsync(CancellationToken cancellationToken);

    Task<AppxPackageState> ReadStateAsync(string packageFamilyName, CancellationToken cancellationToken);

    Task<AppxMutationResult> RemoveForCurrentUserAsync(string packageFullName, CancellationToken cancellationToken);

    Task<AppxMutationResult> RegisterFromInstallLocationAsync(string installLocation, CancellationToken cancellationToken);
}

/// <summary>自选卸载页列表条目：RecoveryClass 决定恢复徽章（StagedRegister=绿/StoreOnly=红），Removable=false 时 UI 禁勾。</summary>
public sealed record AppxCatalogEntry(
    string DisplayName,
    string FamilyName,
    string FullName,
    string Version,
    string PublisherDisplayName,
    AppxRecoveryClass RecoveryClass,
    bool Removable,
    string NotRemovableReason);

/// <summary>
/// 目录硬拒绝名单：NonRemovable/SystemApps 收件箱核心、Store 自身及其依赖链入口。
/// Framework 与依赖方判定在运行时按包状态执行，不在本名单内。
/// </summary>
public static class AppxRemovalDenyList
{
    public const string WindowsInboxPublisherId = "cw5n1h2txyewy";

    public const string SystemAppsPathPrefix = @"C:\Windows\SystemApps";

    private static readonly string[] ProtectedNamePrefixes =
    {
        "Microsoft.WindowsStore",
        "Microsoft.Windows.Client.Core",
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.Windows.Search",
        "Microsoft.Windows.Lkg"
    };

    /// <summary>前缀匹配同时覆盖包名与带发布者后缀的族名。</summary>
    public static bool IsProtected(string packageNameOrFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageNameOrFamilyName))
            return false;

        foreach (var prefix in ProtectedNamePrefixes)
        {
            if (packageNameOrFamilyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsSystemAppsLocation(string installLocation) =>
        !string.IsNullOrWhiteSpace(installLocation)
        && installLocation.StartsWith(SystemAppsPathPrefix, StringComparison.OrdinalIgnoreCase);
}

public static class AppxRemovalEnvironment
{
    /// <summary>当前进程用户的 SID，用于快照固化与跨用户恢复材料拒绝；获取失败返回空串并阻断恢复。</summary>
    public static string GetCurrentUserSid()
    {
        try
        {
            return System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
