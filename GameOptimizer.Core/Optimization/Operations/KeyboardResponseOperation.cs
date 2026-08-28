using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Utilities;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class KeyboardResponseOperation : IOptimizationOperation
{
    private const string RegistryPath = @"Control Panel\Keyboard";
    private const string DelayValueName = "KeyboardDelay";
    private const string SpeedValueName = "KeyboardSpeed";
    private const string DefaultDelay = "0";
    private const string DefaultSpeed = "31";

    private record KeyboardSnapshot(string? Delay, string? Speed);

    public KeyboardResponseOperation(string id, string title, string description, string category)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;
    public bool AffectsUserFeeling => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (targetDelay, targetSpeed) = GetTargetValues();
            var (currentDelay, currentSpeed) = ReadValues();
            var compliant = string.Equals(NormalizeValue(currentDelay), targetDelay, StringComparison.Ordinal)
                && string.Equals(NormalizeValue(currentSpeed), targetSpeed, StringComparison.Ordinal);

            var message = compliant
                ? $"Keyboard response matches target values (Delay={targetDelay}, Speed={targetSpeed})."
                : $"Current Delay={currentDelay ?? "<missing>"}, Speed={currentSpeed ?? "<missing>"}; expected Delay={targetDelay}, Speed={targetSpeed}.";
            return Task.FromResult(new OperationStatus(compliant, message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, $"Keyboard response detection failed: {ex.Message}"));
        }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var key = hive.CreateSubKey(RegistryPath, writable: true);
            if (key is null)
            {
                return new OperationInvokeResult(false, false, $"Unable to open registry path: {RegistryPath}");
            }

            var (targetDelay, targetSpeed) = GetTargetValues();
            if (MatchesValues(key, targetDelay, targetSpeed))
            {
                return new OperationInvokeResult(true, false, "Keyboard response is already optimized.");
            }

            var snapshot = new KeyboardSnapshot(
                InputRegistryValueHelper.ReadStringValue(key, DelayValueName),
                InputRegistryValueHelper.ReadStringValue(key, SpeedValueName));
            await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

            bool IsOriginalStateIntact()
            {
                return InputRegistryValueHelper.MatchesStringValue(key, DelayValueName, snapshot.Delay)
                    && InputRegistryValueHelper.MatchesStringValue(key, SpeedValueName, snapshot.Speed);
            }

            (bool Success, string Message) RestoreOriginalState()
            {
                return RestoreSnapshotValues(key, snapshot)
                    ? (true, string.Empty)
                    : (false, "Failed to verify restored keyboard response state.");
            }

            try
            {
                if (!InputRegistryValueHelper.WriteStringValue(key, DelayValueName, targetDelay)
                    || !InputRegistryValueHelper.WriteStringValue(key, SpeedValueName, targetSpeed))
                {
                    return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                        context,
                        Id,
                        "Keyboard response write failed.",
                        IsOriginalStateIntact,
                        RestoreOriginalState,
                        "Original state remained intact.",
                        "Original state restored.",
                        cancellationToken).ConfigureAwait(false);
                }

                if (!MatchesValues(key, targetDelay, targetSpeed))
                {
                    return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                        context,
                        Id,
                        "Keyboard response verification failed after write.",
                        IsOriginalStateIntact,
                        RestoreOriginalState,
                        "Original state remained intact.",
                        "Original state restored.",
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                    context,
                    Id,
                    string.Concat("Keyboard response apply threw an exception: ", ex.Message),
                    IsOriginalStateIntact,
                    RestoreOriginalState,
                    "Original state remained intact.",
                    "Original state restored.",
                    cancellationToken).ConfigureAwait(false);
            }

            var message = PeripheralSettingsProvider.GetSettings() is null
                ? "Keyboard delay and repeat rate were set to the fastest safe values."
                : "Custom keyboard response values were applied.";
            return new OperationInvokeResult(true, true, message);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "Peripheral optimization");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await context.LoadSnapshotAsync<KeyboardSnapshot>(Id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return new OperationInvokeResult(false, false, "Keyboard response snapshot is missing or corrupted.");
            }

            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var key = hive.CreateSubKey(RegistryPath, writable: true);
            if (key is null)
            {
                return new OperationInvokeResult(false, false, $"Unable to open registry path: {RegistryPath}");
            }

            return RestoreSnapshotValues(key, snapshot)
                ? new OperationInvokeResult(true, true, "Keyboard response settings were restored.")
                : new OperationInvokeResult(false, false, "Keyboard response restore verification failed.");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "Peripheral optimization");
        }
    }

    private static (string Delay, string Speed) GetTargetValues()
    {
        var customSettings = PeripheralSettingsProvider.GetSettings();
        return customSettings is null
            ? (DefaultDelay, DefaultSpeed)
            : (customSettings.KeyboardDelay, customSettings.KeyboardSpeed);
    }

    private static (string? Delay, string? Speed) ReadValues()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var key = hive.OpenSubKey(RegistryPath, writable: false);
        return (
            InputRegistryValueHelper.ReadStringValue(key, DelayValueName),
            InputRegistryValueHelper.ReadStringValue(key, SpeedValueName));
    }

    private static string? NormalizeValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool MatchesValues(RegistryKey key, string delay, string speed)
    {
        return InputRegistryValueHelper.MatchesStringValue(key, DelayValueName, delay)
            && InputRegistryValueHelper.MatchesStringValue(key, SpeedValueName, speed);
    }

    private static bool RestoreSnapshotValues(RegistryKey key, KeyboardSnapshot snapshot)
    {
        return InputRegistryValueHelper.RestoreStringValue(key, DelayValueName, snapshot.Delay)
            && InputRegistryValueHelper.RestoreStringValue(key, SpeedValueName, snapshot.Speed);
    }
}