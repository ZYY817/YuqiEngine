using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.Services;

/// <summary>
/// Registry targets backed by Microsoft documentation where available.
/// Undocumented settings deliberately retain the canonical path declared by the
/// typed Owner; this catalog never guesses a version-specific key.
/// </summary>
public sealed record WindowsRegistryTarget(
    string Hive,
    string Path,
    string? ValueName,
    bool IsMicrosoftDocumented,
    string EvidenceUrl,
    string Applicability)
{
    public string DisplayPath => $"{Hive}\\{Path}";

    /// <summary>
    /// Optional machine-policy location documented by Microsoft when the Owner writes a
    /// per-user/canonical value instead. This is informational only; execution remains owned
    /// by the operation and never switches to this path implicitly.
    /// </summary>
    public string? OfficialPolicyPath { get; init; }
    public string? OfficialPolicyValueName { get; init; }
    public string? OfficialPolicyEvidenceUrl { get; init; }
}

public static class WindowsRegistryTargetCatalog
{
    private const string CortanaPolicyEvidence =
        "https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services";
    private const string PrivacyPolicyCspEvidence =
        "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy";
    private const string GamesPolicyCspEvidence =
        "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-applicationmanagement";

    public static WindowsRegistryTarget? Resolve(IOptimizationOperation operation, int buildNumber)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (string.IsNullOrWhiteSpace(operation.RegistryPath))
        {
            return TryResolveKnownOperationTarget(operation.Id, out var knownTarget)
                ? knownTarget
                : null;
        }

        if (string.Equals(operation.Id, "privacy.disable_cortana", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsRegistryTarget(
                "HKLM",
                @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                "AllowCortana",
                IsMicrosoftDocumented: true,
                CortanaPolicyEvidence,
                buildNumber >= (int)WindowsBuild.Win10_2004
                    ? "Windows 10 2004+ and Windows 11 client policy path"
                    : "Windows client policy path");
        }

        if (string.Equals(operation.Id, "windows.disable_timeline", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsRegistryTarget(
                "HKLM",
                operation.RegistryPath,
                operation.RegistryValueName,
                IsMicrosoftDocumented: true,
                PrivacyPolicyCspEvidence,
                "EnableActivityFeed: Windows 10 1709+; PublishUserActivities: Windows 10 1709+; UploadUserActivities: Windows 10 1803+ (Windows 11 supported)");
        }

        if (string.Equals(operation.Id, "gaming.dvr.disable", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsRegistryTarget(
                "HKLM",
                operation.RegistryPath,
                operation.RegistryValueName,
                IsMicrosoftDocumented: true,
                GamesPolicyCspEvidence,
                "Windows 10 1507+ desktop policy; Windows Game Recording and Broadcasting");
        }

        if (string.Equals(operation.Id, "privacy.disable_cloud_speech", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsRegistryTarget(
                "HKLM",
                operation.RegistryPath,
                operation.RegistryValueName,
                IsMicrosoftDocumented: true,
                PrivacyPolicyCspEvidence,
                "Windows 10 1507+ (AllowInputPersonalization policy; updated in Windows 10 1809)");
        }

        if (string.Equals(operation.Id, "privacy.disable_advertising_id", StringComparison.OrdinalIgnoreCase))
        {
            var ownerTarget = new WindowsRegistryTarget(
                "HKCU",
                operation.RegistryPath,
                operation.RegistryValueName,
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Owner-defined per-user values; Microsoft documents the machine policy alternative below")
            {
                OfficialPolicyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo",
                OfficialPolicyValueName = "DisabledByGroupPolicy",
                OfficialPolicyEvidenceUrl = PrivacyPolicyCspEvidence
            };
            return ownerTarget;
        }

        var hive = string.Equals(operation.RegistryHiveText, "HKLM", StringComparison.OrdinalIgnoreCase)
            ? "HKLM"
            : "HKCU";
        return new WindowsRegistryTarget(
            hive,
            operation.RegistryPath,
            operation.RegistryValueName,
            IsMicrosoftDocumented: false,
            EvidenceUrl: string.Empty,
            Applicability: "Owner-defined canonical path; no official Microsoft version split found");
    }

    private static bool TryResolveKnownOperationTarget(string operationId, out WindowsRegistryTarget? target)
    {
        target = operationId.ToLowerInvariant() switch
        {
            "windows.disable_tips" => new WindowsRegistryTarget(
                "HKCU",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "SoftLandingEnabled;SubscribedContent-338389Enabled",
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Owner-defined per-user ContentDeliveryManager values; no official Microsoft version split found"),
            "windows.disable_suggested_apps" => new WindowsRegistryTarget(
                "HKCU",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "OemPreInstalledAppsEnabled;PreInstalledAppsEnabled;PreInstalledAppsEverEnabled;SilentInstalledAppsEnabled",
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Owner-defined per-user ContentDeliveryManager values; no official Microsoft version split found"),
            "windows.disable_clipboard_history" => new WindowsRegistryTarget(
                "HKCU",
                @"SOFTWARE\Microsoft\Clipboard",
                "EnableClipboardHistory",
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Owner-defined per-user Clipboard value; no official Microsoft version split found"),
            "windows.disable_focus_assist" => new WindowsRegistryTarget(
                "HKCU",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings",
                "NOC_GLOBAL_SETTING_TOASTS_ENABLED",
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Owner-defined per-user notification value; no official Microsoft version split found"),
            "privacy.disable_ceip" => new WindowsRegistryTarget(
                "HKLM",
                @"SOFTWARE\Microsoft\SQMClient\Windows",
                "CEIPEnable",
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Composite Owner; also writes HKLM\\SOFTWARE\\Policies\\Microsoft\\SQMClient\\Windows\\CEIPEnable"),
            "privacy.disable_feedback" => new WindowsRegistryTarget(
                "HKCU",
                @"SOFTWARE\Microsoft\Siuf\Rules",
                "PeriodInNanoSeconds",
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Composite Owner; also writes HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\\DoNotShowFeedbackNotifications"),
            "gpu.disable_game_dvr_complete" => new WindowsRegistryTarget(
                "HKCU",
                @"System\GameConfigStore",
                "GameDVR_Enabled",
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Composite Owner; also writes HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\GameDVR\\AppCaptureEnabled"),
            "input.optimize_usb_polling" => new WindowsRegistryTarget(
                "HKLM",
                @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
                "MouseDataQueueSize",
                IsMicrosoftDocumented: false,
                EvidenceUrl: string.Empty,
                Applicability: "Composite Owner; also writes HKLM\\SYSTEM\\CurrentControlSet\\Services\\kbdclass\\Parameters\\KeyboardDataQueueSize"),
            _ => null
        };
        return target is not null;
    }

    public static bool MatchesOwnerPath(IOptimizationOperation operation, WindowsRegistryTarget target)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(target);

        var ownerHive = string.Equals(operation.RegistryHiveText, "HKLM", StringComparison.OrdinalIgnoreCase)
            ? "HKLM"
            : "HKCU";
        return string.Equals(ownerHive, target.Hive, StringComparison.OrdinalIgnoreCase)
            && string.Equals(operation.RegistryPath, target.Path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(operation.RegistryValueName, target.ValueName, StringComparison.OrdinalIgnoreCase);
    }
}
