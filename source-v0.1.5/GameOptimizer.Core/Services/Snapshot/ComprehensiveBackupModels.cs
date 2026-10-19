using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Services.Snapshot;

public enum ComprehensiveBackupArtifactKind
{
    UserRegistry,
    TypedSnapshot,
    ApplicationConfiguration,
    OperationRecoveryMaterial
}

public enum ComprehensiveBackupArtifactStatus
{
    Captured,
    Partial,
    Failed,
    ManualOnly
}

public sealed record ComprehensiveBackupArtifact(
    ComprehensiveBackupArtifactKind Kind,
    string RelativePath,
    ComprehensiveBackupArtifactStatus Status,
    long SizeBytes,
    string Sha256,
    bool AutomaticRestoreSupported,
    string Description,
    string? Reason = null)
{
    /// <summary>HKCU 导出可能包含凭据、应用令牌或个人偏好，UI/日志不得展开其内容。</summary>
    public bool ContainsSensitiveData => Kind is ComprehensiveBackupArtifactKind.UserRegistry
        or ComprehensiveBackupArtifactKind.OperationRecoveryMaterial;
}

public sealed record ComprehensiveBackupManifest(
    int FormatVersion,
    string PackageId,
    DateTimeOffset CreatedUtc,
    string UserSidSha256,
    string MachineIdentitySha256,
    string WindowsVersion,
    int WindowsBuild,
    string Architecture,
    int ProcessorCount,
    IReadOnlyList<ComprehensiveBackupArtifact> Artifacts,
    SnapshotCoverageReport TypedCoverage,
    IReadOnlyList<ComprehensiveBackupOperationState> OperationStates,
    bool IsComplete,
    string? IncompleteReason = null);

public sealed record ComprehensiveBackupPackageInfo(
    string PackageId,
    string DirectoryPath,
    ComprehensiveBackupManifest Manifest,
    bool IntegrityVerified = false,
    string? IntegrityError = null);

/// <summary>
/// A package-level typed restore preparation. The returned plan must still go through
/// the normal explicit-confirmation, execution, read-back and recovery-receipt path.
/// </summary>
public sealed record ComprehensiveBackupRestorePreparation(
    string PackageId,
    string ImportedSnapshotId,
    SnapshotRestorePlan Plan,
    IReadOnlyList<ComprehensiveBackupArtifact> ManualOnlyArtifacts);

public sealed record ComprehensiveBackupOperationState(
    string ModuleKey,
    string OperationId,
    bool? IsCompliant,
    string SupportState,
    string? SupportReasonCode,
    string Details,
    bool AutomaticRestoreSupported,
    string? Error = null,
    string? RegistryHive = null,
    string? RegistryPath = null,
    string? RegistryValueName = null,
    string? CapturedValueJson = null,
    string CaptureStatus = "NotAttempted");

public sealed record UserRegistryExportResult(
    bool Succeeded,
    long SizeBytes,
    string? ErrorMessage = null);

public interface IUserRegistryExportBackend
{
    Task<UserRegistryExportResult> ExportCurrentUserAsync(string destinationPath, CancellationToken ct = default);
}
