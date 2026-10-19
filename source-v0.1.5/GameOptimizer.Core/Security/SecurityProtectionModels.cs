namespace GameOptimizer.Core.Security;

public static class SecurityProtectionIdentity
{
    public const string OwnerId = "gameoptimizer.security-protection";
    public const string ResourceKey = "security:machine:protection-policy";
    public const int MinimumWindowsBuild = 19041;
}

public enum SecurityProtectionArea
{
    SecurityCenter,
    Firewall,
    UserAccountControl,
    SecurityNotifications,
    DefenderRealtime,
    SmartScreen,
    ControlledFolderAccess,
    VirtualizationBasedSecurity,
    HypervisorCodeIntegrity,
    ControlFlowGuard,
    DataExecutionPrevention,
    StructuredExceptionProtection,
    SpectreMeltdownMitigations
}

public enum SecurityProtectionIntent
{
    WeakenProtection,
    RestoreProtection
}

public enum SecurityProtectionStatus
{
    PolicyDenied,
    Unsupported,
    PermissionDenied,
    ThirdPartySecurityProvider,
    ManagedByPolicy,
    TamperProtected,
    PartialState,
    DriftBlocked,
    TimedOut,
    Cancelled,
    PlatformFailure,
    RecoveryUnproven
}

public sealed record SecurityProtectionRequest(
    SecurityProtectionArea Area,
    SecurityProtectionIntent Intent,
    string? ExpectedStateFingerprint = null);

public sealed record SecurityProtectionEnvironment(
    bool IsWindows,
    int WindowsBuild,
    bool IsAdministrator,
    bool HasThirdPartySecurityProvider,
    bool IsPolicyManaged,
    bool IsTamperProtected,
    bool IsStateComplete,
    string StateFingerprint,
    string? Detail = null);

public sealed record SecurityProtectionDecision(
    Guid AuditId,
    DateTimeOffset EvaluatedAtUtc,
    SecurityProtectionStatus Status,
    SecurityProtectionArea Area,
    SecurityProtectionIntent Intent,
    bool Allowed,
    string Message,
    string OwnerId = SecurityProtectionIdentity.OwnerId,
    string ResourceKey = SecurityProtectionIdentity.ResourceKey);

public sealed record SecurityProtectionTimingOptions(TimeSpan InspectionTimeout)
{
    public static SecurityProtectionTimingOptions Default { get; } = new(TimeSpan.FromSeconds(8));
}
