using System.Collections.ObjectModel;

namespace GameOptimizer.Core.PowerManagement;

public static class PowerManagementGuids
{
    public static readonly Guid DisplaySubgroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    public static readonly Guid DisplayIdleTimeout = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
}

public static class PowerManagementIdentity
{
    public const int SchemaVersion = 1;
    public const string OwnerId = "gameoptimizer.power-management";
    public const string Scope = "machine";
    public const uint MaximumDisplayTimeoutSeconds = 86_400;
    // Display timeout mutation always finishes with SETACTIVE, so the resource is machine-global.
    public const string DisplayTimeoutAndActiveSchemeResource = "power:machine:display-timeout-and-active-scheme";
}

public sealed record PowerManagementTimingOptions(
    TimeSpan ResourceLeaseTimeout,
    TimeSpan ExecutionDeadline,
    TimeSpan IndependentIoTimeout)
{
    public static PowerManagementTimingOptions Default { get; } =
        new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10));
}

public enum PowerWorkflowStatus
{
    Ready,
    Unsupported,
    PermissionDenied,
    PolicyManaged,
    InvalidRequest,
    ConfirmationRequired,
    Expired,
    ReplayBlocked,
    BaselineBlocked,
    DriftBlocked,
    CommandFailed,
    TimedOut,
    Cancelled,
    ReadBackMismatch,
    RecoveryUnavailable,
    StoreFailure
}

public enum PowerExecutionPhase
{
    Inspect,
    Prepare,
    Confirmation,
    BaselineGate,
    DriftCheck,
    Apply,
    ReadBack,
    Rollback,
    RecoveryDriftCheck,
    Restore,
    Completed
}

public enum PowerCommandOutcome
{
    Success,
    Failed,
    TimedOut,
    Cancelled
}

public enum PowerReceiptState
{
    Executing,
    Applied,
    ApplyFailed,
    Restoring,
    Restored,
    RestoreFailed
}

public sealed record PowerEnvironment(
    bool IsWindows,
    int WindowsBuild,
    bool IsAdministrator,
    bool HasBattery,
    bool IsPolicyManaged,
    bool IsModernStandby,
    string? BlockReason = null);

public sealed record PowerScheme(Guid Id, string Name, bool IsActive);

public sealed record PowerSettingCapability(
    bool IsAvailable,
    bool IsPolicyManaged,
    uint MinimumSeconds,
    uint MaximumSeconds,
    string? BlockReason = null);

public sealed record PowerSettingValues(uint AcSeconds, uint? DcSeconds);

public sealed record PowerState(
    PowerEnvironment Environment,
    IReadOnlyList<PowerScheme> Schemes,
    Guid ActiveSchemeId,
    Guid TargetSchemeId,
    PowerSettingCapability DisplayTimeoutCapability,
    PowerSettingValues DisplayTimeout);

public sealed record PowerPrepareRequest(Guid TargetSchemeId, uint AcSeconds, uint? DcSeconds);

public sealed record PowerPreparedPlan(
    Guid PlanId,
    string PlanHash,
    DateTimeOffset PreparedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    Guid OriginalActiveSchemeId,
    Guid TargetSchemeId,
    PowerSettingValues OriginalDisplayTimeout,
    PowerSettingValues TargetDisplayTimeout,
    string Risk,
    string Drawback,
    bool RequiresAdministrator,
    bool RequiresExplicitConfirmation,
    bool RequiresRestart,
    bool RequiresSignOut,
    string OwnerId = PowerManagementIdentity.OwnerId,
    string Scope = PowerManagementIdentity.Scope,
    string ResourceKey = "");

public sealed record PowerConfirmationToken(
    Guid PlanId,
    string PlanHash,
    Guid Nonce,
    DateTimeOffset ExpiresAtUtc,
    string Signature);

public sealed record PowerCommandResult(
    PowerCommandOutcome Outcome,
    string Operation,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string? Detail = null);

public sealed record PowerMutationResult(
    PowerCommandOutcome Outcome,
    IReadOnlyList<PowerCommandResult> Commands,
    string? Detail = null)
{
    public static PowerMutationResult Succeeded(params PowerCommandResult[] commands) =>
        new(PowerCommandOutcome.Success, new ReadOnlyCollection<PowerCommandResult>(commands));
}

public sealed record PowerBaselineDecision(bool Allowed, string? BaselineId, string? Reason);

public sealed record PowerReceipt(
    Guid ReceiptId,
    Guid PlanId,
    string PlanHash,
    DateTimeOffset CreatedAtUtc,
    Guid OriginalActiveSchemeId,
    Guid TargetSchemeId,
    PowerSettingValues OriginalDisplayTimeout,
    PowerSettingValues AppliedDisplayTimeout,
    string BaselineId,
    bool ApplyVerified,
    bool RestoreVerified,
    string? FailureDetail = null,
    PowerReceiptState State = PowerReceiptState.Executing,
    bool CallerCancellationRequested = false,
    string OwnerId = PowerManagementIdentity.OwnerId,
    string Scope = PowerManagementIdentity.Scope,
    string ResourceKey = "");

public sealed record PowerWorkflowResult(
    bool Success,
    bool Changed,
    PowerWorkflowStatus Status,
    PowerExecutionPhase Phase,
    string Message,
    PowerPreparedPlan? Plan = null,
    PowerReceipt? Receipt = null,
    IReadOnlyList<PowerCommandResult>? Commands = null);
