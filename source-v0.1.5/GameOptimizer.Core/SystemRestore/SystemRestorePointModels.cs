using System.Collections.ObjectModel;

namespace GameOptimizer.Core.SystemRestore;

public static class SystemRestorePointIdentityConstants
{
    public const int SchemaVersion = 3;
    public const string OwnerId = "gameoptimizer.system-restore-point";
    public const string Scope = "machine";
    public const string ResourceKey = "system-restore:machine:system-volume";
    public const int MinimumSupportedBuild = 19041;
}

public enum SystemRestorePointIntent
{
    ReadStatus,
    CreateRestorePoint,
    ExecuteSystemRestore
}

public enum SystemProtectionState
{
    Unknown,
    Enabled,
    Disabled,
    PolicyDisabled,
    Unsupported
}

public enum SystemRestorePointWorkflowStatus
{
    Ready,
    Succeeded,
    Unsupported,
    PermissionDenied,
    ProtectionDisabled,
    PolicyDenied,
    CooldownActive,
    InvalidRequest,
    ConfirmationRequired,
    Expired,
    ReplayBlocked,
    IdentityDrifted,
    PlatformFailure,
    TimedOut,
    Cancelled,
    ReadBackMismatch,
    StoreFailure,
    ManualInterventionRequired,
    RebootRequired,
    Unknown
}

public enum SystemRestorePointPhase
{
    Inspect,
    Prepare,
    Confirmation,
    Baseline,
    DriftCheck,
    PersistExecutingReceipt,
    Execute,
    ReadBack,
    PersistTerminalReceipt,
    Reconcile,
    Completed
}

public enum SystemRestorePointMutationOutcome
{
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
    Unknown
}

public enum SystemRestorePointMutationState
{
    NotChanged,
    Changed,
    Unknown
}

public enum SystemRestorePointCreationBoundary
{
    InProcessNonCancellable,
    IsolatedAndTerminable
}

public sealed record SystemRestorePointCreateCapability(
    bool IsAvailable,
    SystemRestorePointWorkflowStatus Status,
    string Reason);

public static class SystemRestorePointCapabilities
{
    public static SystemRestorePointCreateCapability EvaluateCreate(
        SystemRestorePointProbe? probe,
        DateTimeOffset nowUtc)
    {
        if (probe is null)
        {
            return Blocked(SystemRestorePointWorkflowStatus.Unknown,
                "System Restore state has not been inspected.");
        }
        if (!probe.IsWindows || probe.WindowsBuild < SystemRestorePointIdentityConstants.MinimumSupportedBuild)
        {
            return Blocked(SystemRestorePointWorkflowStatus.Unsupported,
                probe.Message ?? "Windows build 19041 or newer is required.");
        }
        if (!probe.IsAdministrator)
        {
            return Blocked(SystemRestorePointWorkflowStatus.PermissionDenied,
                "Administrator permission is required.");
        }
        if (probe.ProtectionState == SystemProtectionState.PolicyDisabled)
        {
            return Blocked(SystemRestorePointWorkflowStatus.PolicyDenied,
                probe.Message ?? "System Protection is disabled by policy.");
        }
        if (probe.ProtectionState == SystemProtectionState.Disabled)
        {
            return Blocked(SystemRestorePointWorkflowStatus.ProtectionDisabled,
                probe.Message ?? "System Protection is disabled for the system volume.");
        }
        if (probe.ProtectionState != SystemProtectionState.Enabled)
        {
            return Blocked(SystemRestorePointWorkflowStatus.Unknown,
                probe.Message ?? "System Protection is not authoritatively enabled.");
        }
        if (!probe.IsCreationApiAvailable)
        {
            return Blocked(SystemRestorePointWorkflowStatus.Unsupported,
                "The official typed System Restore creation API is unavailable.");
        }
        if (probe.CreationBoundary != SystemRestorePointCreationBoundary.IsolatedAndTerminable)
        {
            return Blocked(SystemRestorePointWorkflowStatus.PolicyDenied,
                "Restore-point creation is unavailable because no isolated, terminable writer is installed.");
        }
        if (probe.NextCreationAllowedAtUtc is { } next && next > nowUtc)
        {
            return Blocked(SystemRestorePointWorkflowStatus.CooldownActive,
                $"Windows restore-point creation cooldown remains active until {next:O}.");
        }

        return new(true, SystemRestorePointWorkflowStatus.Ready, string.Empty);
    }

    private static SystemRestorePointCreateCapability Blocked(
        SystemRestorePointWorkflowStatus status,
        string reason) => new(false, status, reason);
}

public enum SystemRestorePointReceiptState
{
    Executing,
    Verified,
    Failed,
    Unknown
}

public enum SystemRestorePointCreateClaimState
{
    Executing,
    Verified,
    Failed,
    Interrupted,
    Unknown
}

public enum SystemRestorePointManualResolutionDisposition
{
    AcknowledgeUnknown,
    ConfirmVerifiedChanged
}

public enum SystemRestorePointReceiptRecoveryDisposition
{
    CreatedMissing,
    ReplacedCorrupt,
    ExistingValid
}

public enum SystemRestorePointStartupReconciliationStatus
{
    Completed,
    AlreadyRun,
    Cancelled,
    TimedOut,
    Faulted
}

public sealed record SystemRestorePointIdentity(
    string VolumeIdentity,
    long SequenceNumber,
    DateTimeOffset CreatedAtUtc,
    string Description,
    int RestorePointType);

public sealed record SystemRestorePointProbe(
    bool IsWindows,
    int WindowsBuild,
    bool IsAdministrator,
    SystemProtectionState ProtectionState,
    string SystemVolumeIdentity,
    IReadOnlyList<SystemRestorePointIdentity> RestorePoints,
    TimeSpan CreationCooldown,
    DateTimeOffset? NextCreationAllowedAtUtc,
    bool IsCreationApiAvailable,
    bool HasPendingRestart,
    bool IsRestoreExecutionPolicyAllowed,
    string? ErrorCode = null,
    string? Message = null,
    SystemRestorePointCreationBoundary CreationBoundary = SystemRestorePointCreationBoundary.InProcessNonCancellable)
{
    public static SystemRestorePointProbe Unsupported(string message) => new(
        false,
        0,
        false,
        SystemProtectionState.Unsupported,
        string.Empty,
        Array.Empty<SystemRestorePointIdentity>(),
        TimeSpan.Zero,
        null,
        false,
        false,
        false,
        "SYSTEM_RESTORE.UNSUPPORTED",
        message);
}

public sealed record SystemRestorePointCreateRequest(
    string Description,
    string Source,
    string? BaselineId = null);

public sealed record SystemRestorePointCreatePlan(
    Guid PlanId,
    string PlanHash,
    DateTimeOffset PreparedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Description,
    string Source,
    string? BaselineId,
    string VolumeIdentity,
    IReadOnlyList<SystemRestorePointIdentity> BaselineRestorePoints,
    TimeSpan CreationCooldown,
    bool RequiresAdministrator,
    bool RequiresExplicitConfirmation,
    string OwnerId = SystemRestorePointIdentityConstants.OwnerId,
    string Scope = SystemRestorePointIdentityConstants.Scope,
    string ResourceKey = SystemRestorePointIdentityConstants.ResourceKey);

public sealed record SystemRestoreExecutionPlan(
    Guid PlanId,
    string PlanHash,
    DateTimeOffset PreparedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    SystemRestorePointIdentity Target,
    string Source,
    bool RequiresAdministrator,
    bool RequiresExplicitConfirmation,
    bool RequiresReboot,
    bool ProductionExecutionAllowed,
    string OwnerId = SystemRestorePointIdentityConstants.OwnerId,
    string Scope = SystemRestorePointIdentityConstants.Scope,
    string ResourceKey = SystemRestorePointIdentityConstants.ResourceKey);

public sealed record SystemRestorePointConfirmationEvidence(
    Guid PlanId,
    string PlanHash,
    DateTimeOffset ConfirmedAtUtc,
    bool ExplicitlyConfirmed);

public sealed record SystemRestorePointConfirmationToken(
    Guid PlanId,
    string PlanHash,
    SystemRestorePointIntent Intent,
    Guid Nonce,
    DateTimeOffset ExpiresAtUtc,
    string Signature);

public sealed record SystemRestorePointCreateCommand(
    Guid PlanId,
    string PlanHash,
    string VolumeIdentity,
    string Description);

public sealed record SystemRestorePointWriterRequest(
    int ProtocolVersion,
    SystemRestorePointCreateCommand Command)
{
    public const int CurrentProtocolVersion = 1;
}

public sealed record SystemRestorePointWriterResponse(
    int ProtocolVersion,
    SystemRestorePointMutationOutcome Outcome,
    long? SequenceNumber,
    int? NativeStatus,
    SystemRestorePointMutationState MutationState,
    string? Message = null);

public sealed record SystemRestorePointMutationResult(
    SystemRestorePointMutationOutcome Outcome,
    long? SequenceNumber,
    int? NativeStatus,
    string? Message = null,
    SystemRestorePointMutationState MutationState = SystemRestorePointMutationState.Unknown);

public sealed record SystemRestorePointCreateClaim(
    Guid ClaimId,
    Guid PlanId,
    string PlanHash,
    Guid ReceiptId,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    SystemRestorePointCreateClaimState State,
    SystemRestorePointMutationState MutationState,
    string? FailureCode = null,
    string? Message = null,
    string OwnerId = SystemRestorePointIdentityConstants.OwnerId,
    string Scope = SystemRestorePointIdentityConstants.Scope,
    string ResourceKey = SystemRestorePointIdentityConstants.ResourceKey);

public sealed record SystemRestorePointReceipt(
    Guid ReceiptId,
    Guid PlanId,
    string PlanHash,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string VolumeIdentity,
    string Description,
    string Source,
    string? BaselineId,
    SystemRestorePointReceiptState State,
    SystemRestorePointMutationOutcome MutationOutcome,
    SystemRestorePointIdentity? CreatedIdentity,
    bool ReadBackVerified,
    bool CallerCancellationRequested,
    string? FailureCode = null,
    string? Message = null,
    string OwnerId = SystemRestorePointIdentityConstants.OwnerId,
    string Scope = SystemRestorePointIdentityConstants.Scope,
    string ResourceKey = SystemRestorePointIdentityConstants.ResourceKey,
    SystemRestorePointMutationState MutationState = SystemRestorePointMutationState.Unknown,
    Guid? ClaimId = null);

public sealed record SystemRestorePointManualResolution(
    Guid PlanId,
    Guid ClaimId,
    SystemRestorePointManualResolutionDisposition Disposition,
    string Evidence,
    SystemRestorePointIdentity? VerifiedIdentity = null);

public sealed record SystemRestorePointReceiptRecoveryResult(
    SystemRestorePointReceiptRecoveryDisposition Disposition,
    SystemRestorePointReceipt Receipt,
    string? QuarantinedPath = null);

public sealed record SystemRestorePointStartupReconciliationResult(
    SystemRestorePointStartupReconciliationStatus Status,
    IReadOnlyList<SystemRestorePointWorkflowResult> Results,
    string Message,
    bool ManualInterventionRequired = false);

public sealed record SystemRestorePointWorkflowResult(
    bool Success,
    bool? Changed,
    SystemRestorePointWorkflowStatus Status,
    SystemRestorePointPhase Phase,
    string Message,
    bool ManualInterventionRequired = false,
    bool RequiresReboot = false,
    SystemRestorePointProbe? Probe = null,
    SystemRestorePointCreatePlan? CreatePlan = null,
    SystemRestoreExecutionPlan? RestorePlan = null,
    SystemRestorePointReceipt? Receipt = null);

public sealed record SystemRestorePointTimingOptions(
    TimeSpan PlanLifetime,
    TimeSpan ConfirmationLifetime,
    TimeSpan ResourceLeaseTimeout,
    TimeSpan ExecutionDeadline,
    TimeSpan IndependentReadBackTimeout)
{
    public static SystemRestorePointTimingOptions Default { get; } = new(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(15));
}

internal static class SystemRestorePointCollections
{
    public static IReadOnlyList<SystemRestorePointIdentity> Freeze(
        IEnumerable<SystemRestorePointIdentity> points) =>
        new ReadOnlyCollection<SystemRestorePointIdentity>(
            points.OrderBy(point => point.VolumeIdentity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(point => point.SequenceNumber)
                .ToArray());
}
