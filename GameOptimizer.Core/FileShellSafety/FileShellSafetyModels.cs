namespace GameOptimizer.Core.FileShellSafety;

public static class FileShellSafetyIdentity
{
    public const string OwnerId = "gameoptimizer.file-shell-safety";
    public const string ResourceKey = "file-shell:machine:mutation";
}

public enum FileShellMutationKind
{
    QuarantineFiles,
    PermanentDelete,
    ExplorerRestart,
    ShellStateReset,
    AccessControlMutation
}

public enum FileShellSafetyStatus
{
    PolicyDenied,
    QuarantineOwnerUnavailable,
    InvalidPath,
    WildcardDenied,
    RootPathDenied,
    OutsideAllowlist,
    ReparsePointDenied,
    PreviewHashRequired,
    ConfirmationRequired,
    BaselineRequired,
    DriftBlocked,
    ManifestRequired,
    RestoreUnproven,
    TimedOut,
    Cancelled,
    InspectionFailed
}

public sealed record FileShellSafetyRequest(
    string OperationId,
    FileShellMutationKind Kind,
    string? TargetPath = null,
    string? ConfirmationToken = null);

public sealed record FileShellSafetyInspection(
    string CanonicalPath,
    bool IsPathSyntaxValid,
    bool ContainsWildcard,
    bool IsRootPath,
    bool IsWithinAllowlistedRoot,
    bool IsReparsePoint,
    string? PreviewSha256,
    string? BaselineId,
    bool IsStateCurrent,
    bool HasDurableQuarantineManifest,
    bool HasRestoreProof);

public sealed record FileShellSafetyDecision(
    Guid AuditId,
    DateTimeOffset EvaluatedAtUtc,
    FileShellSafetyStatus Status,
    string OperationId,
    FileShellMutationKind Kind,
    bool Allowed,
    string Message,
    string OwnerId = FileShellSafetyIdentity.OwnerId,
    string ResourceKey = FileShellSafetyIdentity.ResourceKey);

public sealed record FileShellSafetyTimingOptions(TimeSpan InspectionTimeout)
{
    public static FileShellSafetyTimingOptions Default { get; } = new(TimeSpan.FromSeconds(5));
}
