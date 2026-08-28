using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Core.Services.StorageSafety;

public enum StorageFeatureDiagnosticOutcome
{
    Healthy,
    Degraded,
    NotApplicable,
    PermissionDenied,
    Unknown
}

public sealed record StorageFeatureSnapshot(
    string RepairItemId,
    StorageFeatureDiagnosticOutcome Outcome,
    bool? IsHealthy,
    string? VolumeIdentity,
    string FileSystem,
    bool PolicyValueExists,
    int? NtfsDisableEncryption,
    string EfsServiceState,
    string EfsServiceStartMode,
    string Message,
    EfsRecoveryMaterialPreflight? RecoveryMaterialPreflight = null)
{
    public static StorageFeatureSnapshot Failed(
        string repairItemId,
        StorageFeatureDiagnosticOutcome outcome,
        string message) =>
        new(repairItemId, outcome, null, null, string.Empty, false, null, string.Empty, string.Empty, message);
}

/// <summary>
/// 只记录 EFS 恢复材料的本机可见性聚合，不包含证书、私钥、文件或口令内容。
/// 这不能证明存在可用的离线恢复材料，因此绝不能作为放开写入修复的依据。
/// </summary>
public sealed record EfsRecoveryMaterialPreflight(
    bool CertificateStoreReadable,
    int EfsUsageCertificateCount,
    int ValidCertificateCount,
    int PrivateKeyAvailableCertificateCount,
    int RecoveryAgentCertificateCount,
    string Message)
{
    public static EfsRecoveryMaterialPreflight Unavailable(string message) =>
        new(false, 0, 0, 0, 0, message);
}

public interface IStorageFeatureDiagnosticPlatform
{
    Task<StorageFeatureSnapshot> InspectNtfsEncryptionAsync(CancellationToken cancellationToken);
}

public interface IStorageFeatureRepairOwner
{
    Task<StorageFeatureSnapshot> DiagnoseAsync(string repairItemId, CancellationToken cancellationToken);

    Task<GameOptimizer.Core.Services.Repair.RepairResult> RepairAsync(
        string repairItemId,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken);
}

public static class StorageFeatureRepairCatalog
{
    public const string NtfsEncryptionRepairId = "repair.ntfs_encryption";

    public static IReadOnlyCollection<string> All { get; } = [NtfsEncryptionRepairId];

    public static bool Contains(string repairItemId) =>
        string.Equals(repairItemId, NtfsEncryptionRepairId, StringComparison.OrdinalIgnoreCase);
}

public sealed class StorageFeatureRepairOwner : IStorageFeatureRepairOwner
{
    private readonly IStorageFeatureDiagnosticPlatform _platform;

    public StorageFeatureRepairOwner(IStorageFeatureDiagnosticPlatform platform) =>
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public async Task<StorageFeatureSnapshot> DiagnoseAsync(
        string repairItemId,
        CancellationToken cancellationToken)
    {
        if (!StorageFeatureRepairCatalog.Contains(repairItemId))
        {
            return StorageFeatureSnapshot.Failed(
                repairItemId,
                StorageFeatureDiagnosticOutcome.NotApplicable,
                "未知存储安全项，已拒绝诊断与写入。");
        }

        try
        {
            return await _platform.InspectNtfsEncryptionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            return StorageFeatureSnapshot.Failed(
                repairItemId,
                StorageFeatureDiagnosticOutcome.PermissionDenied,
                $"读取 NTFS/EFS 状态的权限不足：{ex.Message}");
        }
        catch (Exception ex)
        {
            return StorageFeatureSnapshot.Failed(
                repairItemId,
                StorageFeatureDiagnosticOutcome.Unknown,
                $"读取 NTFS/EFS 状态失败：{ex.Message}");
        }
    }

    public async Task<GameOptimizer.Core.Services.Repair.RepairResult> RepairAsync(
        string repairItemId,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!StorageFeatureRepairCatalog.Contains(repairItemId))
        {
            return GameOptimizer.Core.Services.Repair.RepairResult.Failed("未知存储安全项，已拒绝执行。");
        }

        progress?.Report(new RepairProgress { Message = "正在读取 NTFS 与 EFS 状态…", PercentComplete = 20 });
        var snapshot = await DiagnoseAsync(repairItemId, cancellationToken).ConfigureAwait(false);
        progress?.Report(new RepairProgress { Message = "加密与卷写入已由安全边界阻断。", PercentComplete = 100 });
        return GameOptimizer.Core.Services.Repair.RepairResult.Failed(
            "NTFS/EFS mutation 需要密钥、卷、文件身份、数据丢失保护和离线恢复证据；当前仅开放只读诊断，不会修改注册表、服务、卷或文件。本机证书可见性不等于离线恢复材料，不能解除门禁。",
            detail: $"Outcome={snapshot.Outcome}; Volume={snapshot.VolumeIdentity ?? "未解析"}; " +
                    $"FileSystem={snapshot.FileSystem}; PolicyExists={snapshot.PolicyValueExists}; " +
                    $"NtfsDisableEncryption={snapshot.NtfsDisableEncryption?.ToString() ?? "未设置"}; " +
                    $"EFS={snapshot.EfsServiceState}/{snapshot.EfsServiceStartMode}; " +
                    $"RecoveryMaterial={FormatRecoveryMaterial(snapshot.RecoveryMaterialPreflight)}; {snapshot.Message}");
    }

    private static string FormatRecoveryMaterial(EfsRecoveryMaterialPreflight? preflight) =>
        preflight is null
            ? "未读取"
            : $"StoreReadable={preflight.CertificateStoreReadable}; EfsUsage={preflight.EfsUsageCertificateCount}; " +
              $"Valid={preflight.ValidCertificateCount}; PrivateKeyAvailable={preflight.PrivateKeyAvailableCertificateCount}; " +
              $"RecoveryAgent={preflight.RecoveryAgentCertificateCount}";
}

internal sealed class UnavailableStorageFeatureDiagnosticPlatform : IStorageFeatureDiagnosticPlatform
{
    public Task<StorageFeatureSnapshot> InspectNtfsEncryptionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StorageFeatureSnapshot.Failed(
            StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
            StorageFeatureDiagnosticOutcome.Unknown,
            "存储安全只读平台未接入。"));
    }
}

public static class StorageFeatureRepairOwnerFallback
{
    public static IStorageFeatureRepairOwner Instance { get; } =
        new StorageFeatureRepairOwner(new UnavailableStorageFeatureDiagnosticPlatform());
}
