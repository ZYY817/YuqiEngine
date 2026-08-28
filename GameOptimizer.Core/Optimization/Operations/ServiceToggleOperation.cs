using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class ServiceToggleOperation : IOptimizationOperation
{
    private record ServiceSnapshot(int StartValue, bool WasRunning);

    private readonly ServiceStartMode _targetMode;
    private readonly bool _stopWhenDisabling;
    private readonly bool _startWhenEnabling;

    private static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs",
        "PlugPlay",
        "DcomLaunch",
        "EventLog",
        "Schedule",
        "Winmgmt",
        "LanmanServer",
        "LanmanWorkstation",
        "SecurityHealthService",
        "wscsvc",
        "BFE",
        "MpsSvc",
        "EFS",
        "AppXSvc",
        "ClipSVC",
        "InstallService"
    };

    public ServiceToggleOperation(
        string id,
        string title,
        string description,
        string category,
        string serviceName,
        ServiceStartMode targetMode,
        bool stopWhenDisabling = true,
        bool startWhenEnabling = true)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        ServiceName = serviceName;
        _targetMode = targetMode;
        _stopWhenDisabling = stopWhenDisabling;
        _startWhenEnabling = startWhenEnabling;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public string ServiceName { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresSnapshotForRestore => true;

    public OperationType OperationType => OperationType.Service;
    public string? ManualPath => "services.msc";
    public string? TargetName => ServiceName;
    public string? TargetValue => _targetMode switch
    {
        ServiceStartMode.Disabled => "Disabled",
        ServiceStartMode.Manual => "Manual",
        ServiceStartMode.Automatic => "Automatic",
        _ => "Manual"
    };
    public string? ManualSteps =>
        $"1. Win+R -> services.msc\n2. Find \"{ServiceName}\"\n3. Open Properties\n4. Set Startup type to {TargetValue}\n5. Save";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var currentValue = WindowsFeatureHealthHelper.TryReadServiceStartValue(ServiceName);
        if (!currentValue.HasValue)
        {
            return Task.FromResult(new OperationStatus(false, $"Service {ServiceName} not found or not detectable."));
        }

        var desiredValue = MapStartValue(_targetMode);
        var compliant = currentValue.Value == desiredValue;
        var details = compliant
            ? $"Start={currentValue} matches {_targetMode}"
            : $"Start={currentValue}, expected={desiredValue}";
        return Task.FromResult(new OperationStatus(compliant, details));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var currentValue = WindowsFeatureHealthHelper.TryReadServiceStartValue(ServiceName);
        if (!currentValue.HasValue)
        {
            context.Logger.LogWarning("Service {ServiceName} not found, skipping operation {OperationId}", ServiceName, Id);
            return new OperationInvokeResult(true, false, $"Service {ServiceName} not found, skipped.");
        }

        if (_targetMode == ServiceStartMode.Disabled && ProtectedServices.Contains(ServiceName))
        {
            context.Logger.LogWarning("Protected system service {ServiceName} cannot be disabled", ServiceName);
            return new OperationInvokeResult(false, false, $"Service {ServiceName} is protected and cannot be disabled.");
        }

        string? dependencyWarning = null;
        if (_targetMode == ServiceStartMode.Disabled)
        {
            var checkResult = ServiceDependencyChecker.CheckBeforeDisable(ServiceName);
            if (!checkResult.CanProceed)
            {
                context.Logger.LogWarning("Disable blocked for {ServiceName}: {Reason}", ServiceName, checkResult.Message);
                return new OperationInvokeResult(false, false, checkResult.Message);
            }

            if (checkResult.IsWarning && !string.IsNullOrEmpty(checkResult.Message))
            {
                dependencyWarning = checkResult.Message;
                context.Logger.LogWarning("Dependency warning for {ServiceName}: {Warning}", ServiceName, dependencyWarning);
            }
        }

        var desiredValue = MapStartValue(_targetMode);
        if (currentValue.Value == desiredValue)
        {
            return new OperationInvokeResult(true, false, $"Service {ServiceName} is already configured as {TargetValue}.");
        }

        var snapshot = new ServiceSnapshot(currentValue.Value, IsRunning());
        await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

        if (!TryWriteStartValue(desiredValue))
        {
            context.Logger.LogError("Failed to write Start value for service {ServiceName}", ServiceName);
            await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
            return new OperationInvokeResult(false, false, $"Failed to update service {ServiceName}");
        }

        if (_targetMode == ServiceStartMode.Disabled && _stopWhenDisabling)
        {
            await TryStopServiceAsync().ConfigureAwait(false);
        }
        else if (_targetMode == ServiceStartMode.Automatic && _startWhenEnabling)
        {
            await TryStartServiceAsync().ConfigureAwait(false);
        }

        var expectRunning = _targetMode == ServiceStartMode.Automatic && _startWhenEnabling;
        var expectStopped = _targetMode == ServiceStartMode.Disabled && _stopWhenDisabling;
        var verified = await VerifyServiceStateAsync(desiredValue, expectRunning, expectStopped, cancellationToken).ConfigureAwait(false);
        if (!verified)
        {
            context.Logger.LogWarning("Service {ServiceName} failed verification after apply. Rolling back.", ServiceName);
            await RollbackToSnapshotAsync(snapshot).ConfigureAwait(false);
            await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
            return new OperationInvokeResult(false, false, $"Service {ServiceName} verification failed after apply and the change was rolled back.");
        }

        var message = $"Service startup set to {desiredValue}";
        if (dependencyWarning is not null)
        {
            message += $"\n{dependencyWarning}";
        }

        return new OperationInvokeResult(true, true, message);
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<ServiceSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new OperationInvokeResult(false, false, $"Restore snapshot missing for service {ServiceName}.");
        }

        if (!TryWriteStartValue(snapshot.StartValue))
        {
            return new OperationInvokeResult(false, false, $"Service {ServiceName} cannot be restored.");
        }

        if (snapshot.WasRunning && snapshot.StartValue != (int)ServiceStartMode.Disabled)
        {
            await TryStartServiceAsync().ConfigureAwait(false);
        }

        var expectRunning = snapshot.WasRunning && snapshot.StartValue != (int)ServiceStartMode.Disabled;
        var expectStopped = snapshot.StartValue == (int)ServiceStartMode.Disabled;
        var verified = await VerifyServiceStateAsync(snapshot.StartValue, expectRunning, expectStopped, cancellationToken).ConfigureAwait(false);
        if (!verified)
        {
            return new OperationInvokeResult(false, false, $"Service {ServiceName} restore could not be verified.");
        }

        return new OperationInvokeResult(true, true, $"Service restored to Start={snapshot.StartValue}");
    }

    private Task TryDeleteSnapshotAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        return context.TryDeleteSnapshotAsync(Id, cancellationToken);
    }

    private static int MapStartValue(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => 2,
        ServiceStartMode.Manual => 3,
        ServiceStartMode.Disabled => 4,
        _ => 3
    };

    private bool TryWriteStartValue(int value)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = hive.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}", writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue("Start", value, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ServiceToggle][TryWriteStartValue] {ServiceName}: {ex.Message}");
            return false;
        }
    }

    private bool IsRunning()
    {
        if (!SafeServiceHelper.ServiceExists(ServiceName))
        {
            return false;
        }

        var (status, _) = SafeServiceHelper.TryGetServiceStatus(ServiceName);
        return status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
    }

    private async Task<bool> VerifyServiceStateAsync(int expectedStartValue, bool expectRunning, bool expectStopped, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actualStartValue = WindowsFeatureHealthHelper.TryReadServiceStartValue(ServiceName);
            if (actualStartValue == expectedStartValue)
            {
                if (expectRunning && !IsRunning())
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (expectStopped && IsRunning())
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return true;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task RollbackToSnapshotAsync(ServiceSnapshot snapshot)
    {
        if (!TryWriteStartValue(snapshot.StartValue))
        {
            return;
        }

        if (snapshot.WasRunning && snapshot.StartValue != (int)ServiceStartMode.Disabled)
        {
            await TryStartServiceAsync().ConfigureAwait(false);
            return;
        }

        if (snapshot.StartValue == (int)ServiceStartMode.Disabled)
        {
            await TryStopServiceAsync().ConfigureAwait(false);
        }
    }

    private async Task TryStopServiceAsync()
    {
        var (success, error) = await SafeServiceHelper.TryStopServiceAsync(ServiceName, timeoutMs: 15000).ConfigureAwait(false);
        if (!success)
        {
            System.Diagnostics.Debug.WriteLine($"[ServiceToggle] Failed to stop {ServiceName}: {error}");
        }
    }

    private async Task TryStartServiceAsync()
    {
        var (success, error) = await SafeServiceHelper.TryStartServiceAsync(ServiceName, timeoutMs: 15000).ConfigureAwait(false);
        if (!success)
        {
            System.Diagnostics.Debug.WriteLine($"[ServiceToggle] Failed to start {ServiceName}: {error}");
        }
    }
}
