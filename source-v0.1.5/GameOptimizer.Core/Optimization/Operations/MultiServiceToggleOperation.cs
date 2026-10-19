using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class MultiServiceToggleOperation : IOptimizationOperation
{
    private readonly IReadOnlyList<string> _serviceNames;
    private readonly ServiceStartMode _targetMode;
    private readonly bool _stopWhenDisabling;
    private readonly bool _startWhenEnabling;

    private record ServiceSnapshot(string ServiceName, int StartValue, bool WasRunning);
    private record OperationSnapshot(IReadOnlyList<ServiceSnapshot> Entries);

    public MultiServiceToggleOperation(
        string id,
        string title,
        string description,
        string category,
        IEnumerable<string> serviceNames,
        ServiceStartMode targetMode,
        bool stopWhenDisabling = true,
        bool startWhenEnabling = true)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _serviceNames = serviceNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _targetMode = targetMode;
        _stopWhenDisabling = stopWhenDisabling;
        _startWhenEnabling = startWhenEnabling;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.Service;
    public string? ManualPath => "services.msc";
    public string? TargetName => _serviceNames.Count <= 3
        ? string.Join(", ", _serviceNames)
        : string.Join(", ", _serviceNames.Take(3)) + ", ...";
    public string? TargetValue => _targetMode switch
    {
        ServiceStartMode.Disabled => "Disabled",
        ServiceStartMode.Manual => "Manual",
        ServiceStartMode.Automatic => "Automatic",
        _ => "Manual"
    };

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var desired = MapStartValue(_targetMode);
        var mismatches = new List<string>();
        var missing = new List<string>();

        foreach (var serviceName in _serviceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentValue = TryReadStartValue(serviceName);
            if (!currentValue.HasValue)
            {
                missing.Add(serviceName);
                continue;
            }

            if (currentValue.Value != desired)
            {
                mismatches.Add($"{serviceName}: Start={currentValue.Value}");
            }
        }

        if (missing.Count == _serviceNames.Count)
        {
            return Task.FromResult(new OperationStatus(true, "All target services are missing on this system. The operation was skipped."));
        }

        if (mismatches.Count == 0)
        {
            var detail = missing.Count > 0
                ? $"All existing target services already match {TargetValue}. Missing services: {string.Join(", ", missing.Take(5))}."
                : $"All target services already match {TargetValue}.";
            return Task.FromResult(new OperationStatus(true, detail));
        }

        var message = string.Join("; ", mismatches);
        if (missing.Count > 0)
        {
            message += $" Missing services: {string.Join(", ", missing.Take(5))}.";
        }

        return Task.FromResult(new OperationStatus(false, message));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var desired = MapStartValue(_targetMode);
        var plannedSnapshots = new List<ServiceSnapshot>();
        var successfulSnapshots = new List<ServiceSnapshot>();
        var missing = new List<string>();
        var blocked = new List<string>();
        var warnings = new List<string>();

        foreach (var serviceName in _serviceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentValue = TryReadStartValue(serviceName);
            if (!currentValue.HasValue)
            {
                missing.Add(serviceName);
                context.Logger.LogWarning("Service {ServiceName} not found, skipping operation {OperationId}", serviceName, Id);
                continue;
            }

            if (_targetMode == ServiceStartMode.Disabled)
            {
                var checkResult = ServiceDependencyChecker.CheckBeforeDisable(serviceName);
                if (!checkResult.CanProceed)
                {
                    blocked.Add($"{serviceName}: {checkResult.Message}");
                    context.Logger.LogWarning("Disable blocked for {ServiceName}: {Reason}", serviceName, checkResult.Message);
                    continue;
                }

                if (checkResult.IsWarning && !string.IsNullOrWhiteSpace(checkResult.Message))
                {
                    warnings.Add($"{serviceName}: {checkResult.Message}");
                    context.Logger.LogWarning("Dependency warning for {ServiceName}: {Warning}", serviceName, checkResult.Message);
                }
            }

            if (currentValue.Value == desired)
            {
                continue;
            }

            plannedSnapshots.Add(new ServiceSnapshot(serviceName, currentValue.Value, IsRunning(serviceName)));
        }

        if (plannedSnapshots.Count == 0)
        {
            var noChangeMessage = BuildApplyMessage(0, missing, blocked, warnings);
            return new OperationInvokeResult(true, false, noChangeMessage);
        }

        await context.SaveSnapshotAsync(Id, new OperationSnapshot(plannedSnapshots), cancellationToken).ConfigureAwait(false);

        foreach (var snapshot in plannedSnapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serviceName = snapshot.ServiceName;
            if (!TryWriteStartValue(serviceName, desired))
            {
                warnings.Add($"{serviceName}: failed to update Start value.");
                context.Logger.LogWarning("Failed to update Start value for service {ServiceName}", serviceName);
                continue;
            }

            if (_targetMode == ServiceStartMode.Disabled && _stopWhenDisabling)
            {
                await TryStopServiceAsync(serviceName).ConfigureAwait(false);
            }
            else if (_targetMode == ServiceStartMode.Automatic && _startWhenEnabling)
            {
                await TryStartServiceAsync(serviceName).ConfigureAwait(false);
            }

            var expectRunning = _targetMode == ServiceStartMode.Automatic && _startWhenEnabling;
            var expectStopped = _targetMode == ServiceStartMode.Disabled && _stopWhenDisabling;
            var verified = await VerifyServiceStateAsync(serviceName, desired, expectRunning, expectStopped, cancellationToken).ConfigureAwait(false);
            if (!verified)
            {
                warnings.Add($"{serviceName}: verification failed after apply, change rolled back.");
                context.Logger.LogWarning("Service {ServiceName} failed verification after apply. Rolling back.", serviceName);
                await RollbackToSnapshotAsync(snapshot).ConfigureAwait(false);
                continue;
            }

            successfulSnapshots.Add(snapshot);
        }

        if (successfulSnapshots.Count == 0)
        {
            await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
        }
        else if (successfulSnapshots.Count != plannedSnapshots.Count)
        {
            try
            {
                await context.SaveSnapshotAsync(Id, new OperationSnapshot(successfulSnapshots), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(ex, "Operation {OperationId} could not shrink persisted multi-service snapshot after partial apply success.", Id);
            }
        }

        var message = BuildApplyMessage(successfulSnapshots.Count, missing, blocked, warnings);
        return new OperationInvokeResult(true, successfulSnapshots.Count > 0, message);
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<OperationSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.Entries.Count == 0)
        {
            return new OperationInvokeResult(false, false, "Restore snapshot missing for multi-service operation.");
        }

        var restored = 0;
        var failed = new List<string>();

        foreach (var entry in snapshot.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryWriteStartValue(entry.ServiceName, entry.StartValue))
            {
                failed.Add(entry.ServiceName);
                context.Logger.LogWarning("Failed to restore Start value for service {ServiceName}", entry.ServiceName);
                continue;
            }

            if (entry.WasRunning && entry.StartValue != (int)ServiceStartMode.Disabled)
            {
                await TryStartServiceAsync(entry.ServiceName).ConfigureAwait(false);
            }

            var expectRunning = entry.WasRunning && entry.StartValue != (int)ServiceStartMode.Disabled;
            var expectStopped = entry.StartValue == (int)ServiceStartMode.Disabled;
            var verified = await VerifyServiceStateAsync(entry.ServiceName, entry.StartValue, expectRunning, expectStopped, cancellationToken).ConfigureAwait(false);
            if (!verified)
            {
                failed.Add(entry.ServiceName);
                context.Logger.LogWarning("Service {ServiceName} restore could not be verified", entry.ServiceName);
                continue;
            }

            restored++;
        }

        var message = restored > 0
            ? $"Restored {restored} service settings."
            : "No service settings were restored.";

        if (failed.Count > 0)
        {
            message += $" Failed services: {string.Join(", ", failed.Take(5))}.";
            return new OperationInvokeResult(false, restored > 0, message);
        }

        return new OperationInvokeResult(true, restored > 0, message);
    }

    private Task TryDeleteSnapshotAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        return context.TryDeleteSnapshotAsync(Id, cancellationToken);
    }

    private static string BuildApplyMessage(int changedCount, IReadOnlyCollection<string> missing, IReadOnlyCollection<string> blocked, IReadOnlyCollection<string> warnings)
    {
        var parts = new List<string>
        {
            changedCount > 0
                ? $"Adjusted {changedCount} service settings."
                : "No service settings were changed."
        };

        if (blocked.Count > 0)
        {
            parts.Add($"Blocked: {string.Join("; ", blocked.Take(3))}");
        }

        if (missing.Count > 0)
        {
            parts.Add($"Missing: {string.Join(", ", missing.Take(5))}");
        }

        if (warnings.Count > 0)
        {
            parts.Add($"Warnings: {string.Join("; ", warnings.Take(3))}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static int MapStartValue(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => 2,
        ServiceStartMode.Manual => 3,
        ServiceStartMode.Disabled => 4,
        _ => 3
    };

    private static int? TryReadStartValue(string serviceName) => WindowsFeatureHealthHelper.TryReadServiceStartValue(serviceName);

    private static bool TryWriteStartValue(string serviceName, int value)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = hive.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue("Start", value, RegistryValueKind.DWord);
            var verified = WindowsFeatureHealthHelper.TryReadServiceStartValue(serviceName);
            return verified.HasValue && verified.Value == value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiServiceToggle][TryWriteStartValue] {serviceName}: {ex.Message}");
            return false;
        }
    }

    private static bool IsRunning(string serviceName)
    {
        if (!SafeServiceHelper.ServiceExists(serviceName))
        {
            return false;
        }

        var (status, _) = SafeServiceHelper.TryGetServiceStatus(serviceName);
        return status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
    }

    private static async Task VerifyDelayAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> VerifyServiceStateAsync(string serviceName, int expectedStartValue, bool expectRunning, bool expectStopped, CancellationToken cancellationToken)
    {
        await VerifyDelayAsync(cancellationToken).ConfigureAwait(false);

        var currentValue = TryReadStartValue(serviceName);
        if (!currentValue.HasValue || currentValue.Value != expectedStartValue)
        {
            return false;
        }

        if (expectRunning)
        {
            return IsRunning(serviceName);
        }

        if (expectStopped)
        {
            return !IsRunning(serviceName);
        }

        return true;
    }

    private static async Task RollbackToSnapshotAsync(ServiceSnapshot snapshot)
    {
        if (!TryWriteStartValue(snapshot.ServiceName, snapshot.StartValue))
        {
            return;
        }

        if (snapshot.WasRunning && snapshot.StartValue != (int)ServiceStartMode.Disabled)
        {
            await TryStartServiceAsync(snapshot.ServiceName).ConfigureAwait(false);
            return;
        }

        if (snapshot.StartValue == (int)ServiceStartMode.Disabled)
        {
            await TryStopServiceAsync(snapshot.ServiceName).ConfigureAwait(false);
        }
    }

    private static async Task TryStopServiceAsync(string serviceName)
    {
        var (success, error) = await SafeServiceHelper.TryStopServiceAsync(serviceName, timeoutMs: 15000).ConfigureAwait(false);
        if (!success)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiServiceToggle] Failed to stop {serviceName}: {error}");
        }
    }

    private static async Task TryStartServiceAsync(string serviceName)
    {
        var (success, error) = await SafeServiceHelper.TryStartServiceAsync(serviceName, timeoutMs: 15000).ConfigureAwait(false);
        if (!success)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiServiceToggle] Failed to start {serviceName}: {error}");
        }
    }
}