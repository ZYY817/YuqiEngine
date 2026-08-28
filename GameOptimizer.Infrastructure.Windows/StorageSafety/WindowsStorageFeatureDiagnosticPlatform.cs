using System.Management;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GameOptimizer.Core.Services.StorageSafety;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.StorageSafety;

public sealed class WindowsStorageFeatureDiagnosticPlatform : IStorageFeatureDiagnosticPlatform
{
    private const string FileSystemRegistryPath = @"SYSTEM\CurrentControlSet\Control\FileSystem";
    private const string EfsEnhancedKeyUsageOid = "1.3.6.1.4.1.311.10.3.4";
    private const string RecoveryAgentExtensionOid = "1.3.6.1.4.1.311.21.6";

    public Task<StorageFeatureSnapshot> InspectNtfsEncryptionAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Inspect(cancellationToken), cancellationToken);

    private static StorageFeatureSnapshot Inspect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', '/');
            if (string.IsNullOrWhiteSpace(drive))
            {
                return StorageFeatureSnapshot.Failed(
                    StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
                    StorageFeatureDiagnosticOutcome.Unknown,
                    "无法解析系统卷。");
            }

            using var volumeSearcher = new ManagementObjectSearcher(
                $"SELECT DeviceID, FileSystem FROM Win32_LogicalDisk WHERE DeviceID = '{EscapeWql(drive)}'");
            using var volumeResults = volumeSearcher.Get();
            var volume = volumeResults.Cast<ManagementObject>().FirstOrDefault();
            cancellationToken.ThrowIfCancellationRequested();
            if (volume is null)
            {
                return StorageFeatureSnapshot.Failed(
                    StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
                    StorageFeatureDiagnosticOutcome.NotApplicable,
                    $"未找到系统卷 {drive}。");
            }

            var fileSystem = volume["FileSystem"]?.ToString() ?? string.Empty;
            if (!string.Equals(fileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                return new StorageFeatureSnapshot(
                    StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
                    StorageFeatureDiagnosticOutcome.NotApplicable,
                    null,
                    $"volume:{drive}",
                    fileSystem,
                    false,
                    null,
                    string.Empty,
                    string.Empty,
                    $"系统卷文件系统为 {fileSystem}，NTFS/EFS 诊断不适用。");
            }

            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(FileSystemRegistryPath, writable: false);
            var rawValue = key?.GetValue("NtfsDisableEncryption", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var valueExists = rawValue is not null;
            int? policyValue = rawValue is null ? null : Convert.ToInt32(rawValue);

            using var serviceSearcher = new ManagementObjectSearcher(
                "SELECT Name, State, StartMode FROM Win32_Service WHERE Name = 'EFS'");
            using var serviceResults = serviceSearcher.Get();
            var service = serviceResults.Cast<ManagementObject>().FirstOrDefault();
            cancellationToken.ThrowIfCancellationRequested();
            if (service is null)
            {
                return new StorageFeatureSnapshot(
                    StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
                    StorageFeatureDiagnosticOutcome.NotApplicable,
                    null,
                    $"volume:{drive}",
                    fileSystem,
                    valueExists,
                    policyValue,
                    string.Empty,
                    string.Empty,
                    "当前系统未提供 EFS 服务，不能推断可修复状态。");
            }

            var state = service["State"]?.ToString() ?? "Unknown";
            var startMode = service["StartMode"]?.ToString() ?? "Unknown";
            var recoveryMaterial = InspectCurrentUserEfsMaterial(cancellationToken);
            var disabledByPolicy = policyValue == 1;
            var disabledService = string.Equals(startMode, "Disabled", StringComparison.OrdinalIgnoreCase);
            var healthy = !disabledByPolicy && !disabledService;
            return new StorageFeatureSnapshot(
                StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
                healthy ? StorageFeatureDiagnosticOutcome.Healthy : StorageFeatureDiagnosticOutcome.Degraded,
                healthy,
                $"volume:{drive}",
                fileSystem,
                valueExists,
                policyValue,
                state,
                startMode,
                healthy
                    ? "NTFS/EFS 可用性状态已只读验证；未测试或修改任何加密文件。本机证书聚合不等于离线恢复材料。"
                    : "NTFS/EFS 可用性存在禁用状态；仅报告证据，不自动修改策略或服务。本机证书聚合不等于离线恢复材料。",
                recoveryMaterial);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            return StorageFeatureSnapshot.Failed(
                StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
                StorageFeatureDiagnosticOutcome.PermissionDenied,
                $"WMI 只读查询被拒绝：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StorageFeatureSnapshot.Failed(
                StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
                StorageFeatureDiagnosticOutcome.PermissionDenied,
                $"只读查询权限不足：{ex.Message}");
        }
        catch (ManagementException ex)
        {
            return StorageFeatureSnapshot.Failed(
                StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
                StorageFeatureDiagnosticOutcome.Unknown,
                $"WMI 只读查询失败 ({ex.ErrorCode})：{ex.Message}");
        }
    }

    private static string EscapeWql(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static EfsRecoveryMaterialPreflight InspectCurrentUserEfsMaterial(CancellationToken cancellationToken)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            var now = DateTimeOffset.UtcNow;
            var efsUsage = 0;
            var valid = 0;
            var privateKeyAvailable = 0;
            var recoveryAgents = 0;

            foreach (var certificate in store.Certificates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hasEfsUsage = HasExtensionOrEnhancedUsage(certificate, EfsEnhancedKeyUsageOid);
                var isRecoveryAgent = HasExtension(certificate, RecoveryAgentExtensionOid);
                if (!hasEfsUsage && !isRecoveryAgent)
                {
                    continue;
                }

                if (hasEfsUsage)
                {
                    efsUsage++;
                }

                if (isRecoveryAgent)
                {
                    recoveryAgents++;
                }

                if (certificate.NotBefore.ToUniversalTime() <= now && now <= certificate.NotAfter.ToUniversalTime())
                {
                    valid++;
                }

                if (certificate.HasPrivateKey)
                {
                    privateKeyAvailable++;
                }
            }

            return new EfsRecoveryMaterialPreflight(
                true,
                efsUsage,
                valid,
                privateKeyAvailable,
                recoveryAgents,
                "仅完成 CurrentUser/My 证书库的只读聚合；未读取、导出或持久化任何证书、私钥、文件路径或口令。本机可见性不等于离线恢复材料。");
        }
        catch (CryptographicException)
        {
            return EfsRecoveryMaterialPreflight.Unavailable("CurrentUser/My 证书库不可读；未读取或导出任何证书内容。");
        }
        catch (UnauthorizedAccessException)
        {
            return EfsRecoveryMaterialPreflight.Unavailable("CurrentUser/My 证书库访问被拒绝；未读取或导出任何证书内容。");
        }
    }

    private static bool HasExtensionOrEnhancedUsage(X509Certificate2 certificate, string oid) =>
        HasExtension(certificate, oid) || certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(usage => string.Equals(usage.Value, oid, StringComparison.Ordinal));

    private static bool HasExtension(X509Certificate2 certificate, string oid) =>
        certificate.Extensions.Cast<X509Extension>()
            .Any(extension => string.Equals(extension.Oid?.Value, oid, StringComparison.Ordinal));
}

public static class StorageFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddStorageFeatureWindowsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(value => value.ServiceType == typeof(IStorageFeatureRepairOwner)))
        {
            throw new InvalidOperationException("IStorageFeatureRepairOwner 已注册，拒绝创建第二个存储安全边界。");
        }

        services.AddSingleton<IStorageFeatureDiagnosticPlatform, WindowsStorageFeatureDiagnosticPlatform>();
        services.AddSingleton<IStorageFeatureRepairOwner, StorageFeatureRepairOwner>();
        return services;
    }
}
