// File: GameOptimizer.Infrastructure.Windows/Services/WindowsAppxRemovalOwner.cs
using System.Security.Principal;
using GameOptimizer.Core.Services.Appx;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace GameOptimizer.Infrastructure.Windows.Services;

/// <summary>
/// 官方 WinRT 部署 API 访问器（Windows.Management.Deployment.PackageManager）。
/// 只做当前用户包移除与 Staged 载荷重注册；绝不动 RemovalOptions.RemoveForAllUsers，
/// 绝不触碰 Provisioned 预置语义，不经过 PowerShell 白名单通道。
/// </summary>
public sealed class WindowsAppxRemovalOwner : IAppxRemovalOwner
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(90);

    public Task<AppxPlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return Task.FromResult(new AppxPlatformAssessment(false, "os.unsupported", "AppX 移除能力需要 Windows 平台。"));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return Task.FromResult(new AppxPlatformAssessment(false, "os.unsupported", "AppX 移除能力需要 Windows 10 2004 或更高版本。"));

        try
        {
            _ = new PackageManager();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(new AppxPlatformAssessment(false, "appx.deployment-unavailable", $"官方部署 API 不可用：{Sanitize(ex)}"));
        }

        if (!IsCurrentUserAdministrator())
            return Task.FromResult(new AppxPlatformAssessment(
                false,
                "permission.administrator-required",
                Core.Services.ElevationGate.DescribeWriteDenial("管理预装应用")));

        return Task.FromResult(new AppxPlatformAssessment(true, "applicable", "官方部署 API 可用，当前进程已提升且可枚举当前用户包。"));
    }

    public Task<IReadOnlyList<AppxCatalogEntry>> EnumerateInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var manager = new PackageManager();
            var sid = AppxRemovalEnvironment.GetCurrentUserSid();
            var entries = new List<AppxCatalogEntry>();
            foreach (var package in manager.FindPackagesForUser(sid))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Framework 包是其他包的运行库，无独立卸载语义，列表直接不展示。
                if (package.IsFramework)
                    continue;
                entries.Add(MapCatalogEntry(package));
            }

            return Task.FromResult<IReadOnlyList<AppxCatalogEntry>>(
                entries.OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"枚举当前用户 AppX 包失败：{Sanitize(ex)}");
        }
    }

    private static AppxCatalogEntry MapCatalogEntry(Package package)
    {
        var id = package.Id;
        var name = id?.Name ?? string.Empty;
        var familyName = id?.FamilyName ?? string.Empty;
        var location = package.InstalledLocation?.Path ?? string.Empty;

        // 透明原则：不可移除项也进列表（Removable=false 供 UI 禁勾），让用户知道为什么不能删。
        string? notRemovable = null;
        if (AppxRemovalDenyList.IsProtected(name) || AppxRemovalDenyList.IsProtected(familyName))
            notRemovable = "系统关键组件，禁止移除";
        else if (AppxRemovalDenyList.IsSystemAppsLocation(location))
            notRemovable = "收件箱系统应用，禁止移除";

        // 载荷清单仍在本机 → 移除后可 Register 自动恢复；否则仅能指引 Store 重装。
        var recoveryClass = !string.IsNullOrWhiteSpace(location)
            && File.Exists(Path.Combine(location, "AppxManifest.xml"))
                ? AppxRecoveryClass.StagedRegister
                : AppxRecoveryClass.StoreOnly;

        return new AppxCatalogEntry(
            string.IsNullOrWhiteSpace(package.DisplayName) ? name : package.DisplayName,
            familyName,
            id?.FullName ?? string.Empty,
            id is null ? string.Empty : $"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}",
            package.PublisherDisplayName ?? string.Empty,
            recoveryClass,
            Removable: notRemovable is null,
            NotRemovableReason: notRemovable ?? string.Empty);
    }

    public Task<AppxPackageState> ReadStateAsync(string packageFamilyName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFamilyName);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var manager = new PackageManager();
            var sid = AppxRemovalEnvironment.GetCurrentUserSid();
            var userPackages = manager.FindPackagesForUser(sid).ToList();
            cancellationToken.ThrowIfCancellationRequested();

            var registered = FindByFamily(userPackages, packageFamilyName);
            if (registered is not null)
                return Task.FromResult(MapPackage(registered, userPackages, installedForUser: true));

            IReadOnlyList<Package> machineWide;
            try
            {
                machineWide = manager.FindPackages().ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                machineWide = [];
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stagedOnly = FindByFamily(machineWide, packageFamilyName);
            return Task.FromResult(stagedOnly is null
                ? Missing(packageFamilyName)
                : MapPackage(stagedOnly, [], installedForUser: false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(Failed(packageFamilyName, "AccessDenied"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(Failed(packageFamilyName, Sanitize(ex)));
        }
    }

    public Task<AppxMutationResult> RemoveForCurrentUserAsync(string packageFullName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFullName);
        var manager = new PackageManager();
        return RunDeploymentAsync(
            () => manager.RemovePackageAsync(packageFullName),
            successMessage: "包已完成当前用户移除。",
            cancellationToken);
    }

    public Task<AppxMutationResult> RegisterFromInstallLocationAsync(string installLocation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installLocation);
        cancellationToken.ThrowIfCancellationRequested();

        var manifestPath = Path.Combine(installLocation, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return Task.FromResult(new AppxMutationResult(
                false, false, "appx.manifest-missing",
                "Staged 载荷清单不存在，无法自动恢复；需要用户从 Microsoft Store 手动重装。"));
        }

        var manager = new PackageManager();
        var options = new AddPackageOptions { DeferRegistrationWhenPackagesAreInUse = true };
        return RunDeploymentAsync(
            () => manager.AddPackageByUriAsync(new Uri(manifestPath), options),
            successMessage: "包已从 Staged 载荷为当前用户重新注册。",
            cancellationToken);
    }

    private static async Task<AppxMutationResult> RunDeploymentAsync(
        Func<IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress>> operationFactory,
        string successMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = operationFactory();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(OperationTimeout);

        DeploymentResult result;
        try
        {
            result = await operation.AsTask(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AppxMutationResult(false, false, "appx.operation-timeout", $"部署操作在 {(int)OperationTimeout.TotalSeconds} 秒内未完成，已取消等待。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AppxMutationResult(false, false, "appx.deployment-failed", $"部署操作异常：{Sanitize(ex)}");
        }

        // WinRT 异步部署失败时 AsTask 会直接抛出 ExtendedErrorCode 对应的异常（已在上方捕获映射）；
        // 能走到这里即为部署成功。
        return new AppxMutationResult(true, true, "ok", successMessage);
    }

    /// <summary>把部署失败 HRESULT 映射为稳定 ReasonCode，错误细节截断脱敏后进 Message。</summary>
    private static AppxMutationResult MapDeploymentFailure(DeploymentResult result)
    {
        var hresult = result.ExtendedErrorCode?.HResult ?? 0;
        var reason = hresult switch
        {
            unchecked((int)0x80073CFE) => "appx.dependency-error",
            unchecked((int)0x80070032) => "appx.nonremovable",
            unchecked((int)0x80073D02) => "appx.package-in-use",
            _ => "appx.deployment-failed"
        };

        var message = reason == "appx.package-in-use"
            ? $"目标应用正在运行，请关闭相关应用后重试（0x{hresult:X8}），不会静默强杀进程。"
            : $"部署操作失败（0x{hresult:X8}）：{Truncate(result.ErrorText ?? string.Empty)}";
        return new AppxMutationResult(false, false, reason, message);
    }

    private static Package? FindByFamily(IEnumerable<Package> packages, string packageFamilyName) =>
        packages.FirstOrDefault(candidate => string.Equals(
            candidate.Id.FamilyName,
            packageFamilyName,
            StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// IsStaged 语义 = 包载荷仍保留于 WindowsApps 且清单存在（Register 可回注册）。
    /// 用户已注册时按清单文件判定；未注册但整机可见时视为 Staged 残留。
    /// </summary>
    private static AppxPackageState MapPackage(
        Package package,
        IReadOnlyList<Package> currentUserPackages,
        bool installedForUser)
    {
        var identity = package.Id;
        var installPath = TryGetInstallPath(package);
        var version = identity.Version;
        return new AppxPackageState(
            Name: identity.Name,
            FullName: identity.FullName,
            FamilyName: identity.FamilyName,
            Version: $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
            InstallLocation: installPath,
            Installed: installedForUser,
            IsStaged: installedForUser ? File.Exists(Path.Combine(installPath, "AppxManifest.xml")) : true,
            NonRemovable: DeriveNonRemovable(package, installPath),
            IsFramework: SafeRead(() => package.IsFramework),
            DependencyCount: CountDependents(currentUserPackages, identity.FullName),
            SignatureKind: SafeRead(() => package.SignatureKind.ToString()) ?? string.Empty,
            ErrorCode: null);
    }

    private static AppxPackageState Missing(string packageFamilyName) =>
        new(string.Empty, string.Empty, packageFamilyName, string.Empty, string.Empty,
            Installed: false, IsStaged: false, NonRemovable: false, IsFramework: false,
            DependencyCount: 0, SignatureKind: string.Empty, ErrorCode: null);

    private static AppxPackageState Failed(string packageFamilyName, string errorCode) =>
        new(string.Empty, string.Empty, packageFamilyName, string.Empty, string.Empty,
            Installed: false, IsStaged: false, NonRemovable: false, IsFramework: false,
            DependencyCount: 0, SignatureKind: string.Empty, ErrorCode: errorCode);

    private static bool DeriveNonRemovable(Package package, string installPath) =>
        SafeRead(() => package.SignatureKind == PackageSignatureKind.System)
        || AppxRemovalDenyList.IsSystemAppsLocation(installPath);

    private static int CountDependents(IReadOnlyList<Package> currentUserPackages, string targetFullName)
    {
        var count = 0;
        foreach (var candidate in currentUserPackages)
        {
            if (string.Equals(candidate.Id.FullName, targetFullName, StringComparison.OrdinalIgnoreCase))
                continue;

            var dependencies = SafeRead(() => candidate.Dependencies);
            if (dependencies is null)
                continue;

            foreach (var dependency in dependencies)
            {
                if (string.Equals(dependency.Id.FullName, targetFullName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static string TryGetInstallPath(Package package)
    {
        try
        {
            return package.InstalledLocation?.Path ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static T? SafeRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return default;
        }
    }

    private static bool IsCurrentUserAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;

    private static string Sanitize(Exception ex) =>
        Truncate($"{ex.GetType().Name}: {ex.Message}");

    private static string Truncate(string text)
    {
        var normalized = text.ReplaceLineEndings(" ").Trim();
        const int maxLength = 200;
        return normalized.Length <= maxLength ? normalized : $"{normalized[..maxLength]}…";
    }
}
