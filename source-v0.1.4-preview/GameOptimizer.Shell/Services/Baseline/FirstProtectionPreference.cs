using System;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// Records the user's first-protection onboarding choice. This is deliberately
/// separate from <see cref="BaselineSafetyState"/>, which remains the only
/// authority for permitting protected system changes.
/// </summary>
public sealed class FirstProtectionPreference
{
    /// <summary>
    /// Bumps when the first-use safety explanation meaningfully changes.
    /// A previous "skip" only suppresses the revision the user actually saw.
    /// </summary>
    public const int CurrentOnboardingRevision = 2;

    public int SchemaVersion { get; set; } = 1;

    // Missing in older preference files, which intentionally reads as revision 0
    // so a newly introduced safety flow is shown once after an upgrade.
    public int OnboardingRevision { get; set; }

    public FirstProtectionPreferenceStatus Status { get; set; } = FirstProtectionPreferenceStatus.Pending;

    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? IntroShownUtc { get; set; }

    public DateTime? BackupCompletedUtc { get; set; }

    public string? LastFailureReason { get; set; }

    public bool ShouldOfferOnStartup => Status is FirstProtectionPreferenceStatus.Pending
        or FirstProtectionPreferenceStatus.BackupFailed
        or FirstProtectionPreferenceStatus.Corrupted
        || (Status == FirstProtectionPreferenceStatus.UserDeferred
            && OnboardingRevision < CurrentOnboardingRevision);
}

public enum FirstProtectionPreferenceStatus
{
    Pending,
    UserDeferred,
    BackupFailed,
    BackupCompleted,
    Corrupted
}
