using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services.Repair;
using Microsoft.Win32;

namespace GameOptimizer.Core.Services;

internal readonly record struct WindowsFeatureHealthResult(bool IsHealthy, bool RecoveryAttempted, string Message);

internal static class WindowsFeatureHealthHelper
{
    public static async Task<WindowsFeatureHealthResult> EnsureSecurityCenterHealthyAsync(bool attemptRecovery, CancellationToken cancellationToken)
    {
        if (await IsSecurityCenterHealthyAsync().ConfigureAwait(false))
        {
            return new WindowsFeatureHealthResult(true, false, "Security Center services are healthy.");
        }

        if (!attemptRecovery)
        {
            return new WindowsFeatureHealthResult(false, false, "Security Center services are unhealthy.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new WindowsFeatureHealthResult(false, false,
            "Security Center recovery is blocked: no typed recovery adapter with policy, third-party provider, snapshot, and readback proof is registered.");
    }

    public static async Task<WindowsFeatureHealthResult> EnsureFirewallHealthyAsync(bool attemptRecovery, CancellationToken cancellationToken)
    {
        if (await IsFirewallHealthyAsync().ConfigureAwait(false))
        {
            return new WindowsFeatureHealthResult(true, false, "Windows Firewall is healthy.");
        }

        if (!attemptRecovery)
        {
            return new WindowsFeatureHealthResult(false, false, "Windows Firewall is unhealthy.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new WindowsFeatureHealthResult(false, false,
            "Windows Firewall recovery is blocked: service state alone cannot prove profile policy, third-party ownership, snapshot integrity, or readback.");
    }

    public static async Task<WindowsFeatureHealthResult> EnsureStoreInstallHealthyAsync(bool attemptRecovery, CancellationToken cancellationToken)
    {
        if (await AreStoreInstallPrerequisitesHealthyAsync().ConfigureAwait(false))
        {
            return new WindowsFeatureHealthResult(true, false, "Store installation prerequisites are healthy.");
        }

        if (!attemptRecovery)
        {
            return new WindowsFeatureHealthResult(false, false, "Store installation prerequisites are unhealthy.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var firewall = await EnsureFirewallHealthyAsync(true, cancellationToken).ConfigureAwait(false);
        if (!firewall.IsHealthy)
        {
            return new WindowsFeatureHealthResult(false, false,
                "Store prerequisite recovery was blocked before mutation because authoritative Firewall recovery is unavailable.");
        }

        var efsOk = await EnsureServiceNotDisabledAsync("EFS", 3, startService: true, cancellationToken).ConfigureAwait(false);
        var appxOk = await EnsureServiceNotDisabledAsync("AppXSvc", 3, startService: false, cancellationToken).ConfigureAwait(false);
        var clipOk = await EnsureServiceNotDisabledAsync("ClipSVC", 3, startService: false, cancellationToken).ConfigureAwait(false);
        var installOk = await EnsureServiceNotDisabledAsync("InstallService", 3, startService: false, cancellationToken).ConfigureAwait(false);

        var healthy = firewall.IsHealthy && efsOk && appxOk && clipOk && installOk && await AreStoreInstallPrerequisitesHealthyAsync().ConfigureAwait(false);
        return healthy
            ? new WindowsFeatureHealthResult(true, true, "Store installation prerequisites recovered.")
            : new WindowsFeatureHealthResult(false, true, "Store installation prerequisites are still unhealthy after recovery.");
    }

    public static async Task<bool> IsSecurityCenterHealthyAsync()
    {
        var hasSecurityHealthService = await RepairHelper.ServiceExistsAsync("SecurityHealthService").ConfigureAwait(false);
        var hasWscsvc = await RepairHelper.ServiceExistsAsync("wscsvc").ConfigureAwait(false);

        if (!hasSecurityHealthService && !hasWscsvc)
        {
            return true;
        }

        var securityHealthOk = !hasSecurityHealthService || await RepairHelper.IsServiceRunningAsync("SecurityHealthService").ConfigureAwait(false);
        var wscsvcOk = !hasWscsvc || await RepairHelper.IsServiceRunningAsync("wscsvc").ConfigureAwait(false);
        return securityHealthOk && wscsvcOk;
    }

    public static async Task<bool> IsFirewallHealthyAsync()
    {
        if (!await RepairHelper.ServiceExistsAsync("MpsSvc").ConfigureAwait(false))
        {
            return false;
        }

        if (!await RepairHelper.ServiceExistsAsync("BFE").ConfigureAwait(false))
        {
            return false;
        }

        var mpsSvcOk = await RepairHelper.IsServiceRunningAsync("MpsSvc").ConfigureAwait(false);
        var bfeOk = await RepairHelper.IsServiceRunningAsync("BFE").ConfigureAwait(false);
        if (!mpsSvcOk || !bfeOk)
        {
            return false;
        }

        // Service state does not prove Domain/Private/Public profile policy. Without a
        // structured Firewall API adapter, health must remain unknown and fail closed.
        return false;
    }

    public static async Task<bool> AreStoreInstallPrerequisitesHealthyAsync()
    {
        var firewallOk = await IsFirewallHealthyAsync().ConfigureAwait(false);
        var efsOk = await IsServiceAvailableAndNotDisabledAsync("EFS").ConfigureAwait(false);
        var appxOk = await IsServiceAvailableAndNotDisabledAsync("AppXSvc").ConfigureAwait(false);
        var clipOk = await IsServiceAvailableAndNotDisabledAsync("ClipSVC").ConfigureAwait(false);
        var installOk = await IsServiceAvailableAndNotDisabledAsync("InstallService").ConfigureAwait(false);
        return firewallOk && efsOk && appxOk && clipOk && installOk;
    }
    public static async Task<bool> IsStoreInstallHealthyAsync()
    {
        var storeInstalled = await RepairHelper.CheckUwpAppInstalledAsync("WindowsStore").ConfigureAwait(false);
        return storeInstalled && await AreStoreInstallPrerequisitesHealthyAsync().ConfigureAwait(false);
    }

    public static int? TryReadServiceStartValue(string serviceName)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = hive.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
            if (key is null)
            {
                return null;
            }

            return Convert.ToInt32(key.GetValue("Start", 3) ?? 3);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsFeatureHealthHelper] Failed to read start value for {serviceName}: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> EnsureServiceNotDisabledAsync(string serviceName, int fallbackStartValue, bool startService, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await RepairHelper.ServiceExistsAsync(serviceName).ConfigureAwait(false))
        {
            return true;
        }

        var currentStartValue = TryReadServiceStartValue(serviceName);
        if (currentStartValue == 4)
        {
            await TryRunCommandAsync("sc", $"config {serviceName} start= demand", 10000).ConfigureAwait(false);
            currentStartValue = TryReadServiceStartValue(serviceName);
            if (currentStartValue == 4)
            {
                using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
                using var key = hive.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
                key?.SetValue("Start", fallbackStartValue, RegistryValueKind.DWord);
                currentStartValue = TryReadServiceStartValue(serviceName);
            }
        }

        if (startService && !await RepairHelper.IsServiceRunningAsync(serviceName).ConfigureAwait(false))
        {
            await TryRunCommandAsync("net", $"start {serviceName}", 15000).ConfigureAwait(false);
        }

        if (startService)
        {
            return currentStartValue.HasValue && currentStartValue.Value != 4 && await RepairHelper.IsServiceRunningAsync(serviceName).ConfigureAwait(false);
        }

        return currentStartValue.HasValue && currentStartValue.Value != 4;
    }

    private static async Task<bool> IsServiceAvailableAndNotDisabledAsync(string serviceName)
    {
        if (!await RepairHelper.ServiceExistsAsync(serviceName).ConfigureAwait(false))
        {
            return true;
        }

        var startValue = TryReadServiceStartValue(serviceName);
        return startValue.HasValue && startValue.Value != 4;
    }

    private static async Task<bool> TryRunCommandAsync(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            await RepairHelper.RunCommandAsync(fileName, arguments, timeoutMs).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsFeatureHealthHelper] Command failed: {fileName} {arguments} | {ex.Message}");
            return false;
        }
    }
}

