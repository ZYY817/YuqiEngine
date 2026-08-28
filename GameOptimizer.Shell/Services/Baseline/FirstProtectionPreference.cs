using System;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// Records the user's first-protection onboarding choice. This is deliberately
/// separate from <see cref="BaselineSafetyState"/>, which remains the only
/// authority for permitting protected system changes.
/// </summary>
public sealed class FirstProtectionPreference
{
    public int SchemaVersion { get; set; } = 1;

    public FirstProtectionPreferenceStatus Status { get; set; } = FirstProtectionPreferenceStatus.Pending;

    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? IntroShownUtc { get; set; }

    public DateTime? BackupCompletedUtc { get; set; }

    public string? LastFailureReason { get; set; }

    public bool ShouldOfferOnStartup => Status is FirstProtectionPreferenceStatus.Pending
        or FirstProtectionPreferenceStatus.BackupFailed
        or FirstProtectionPreferenceStatus.Corrupted;
}

public enum FirstProtectionPreferenceStatus
{
    Pending,
    UserDeferred,
    BackupFailed,
    BackupCompleted,
    Corrupted
}
