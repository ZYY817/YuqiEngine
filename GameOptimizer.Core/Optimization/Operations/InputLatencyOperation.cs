using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class DisablePointerTrailsOperation : IOptimizationOperation
{
    private record Snapshot(string? MouseTrails);
    private const string MousePath = @"Control Panel\Mouse";
    private const string ValueName = "MouseTrails";
    private const string TargetValue = "0";

    public string Id => "input.disable_pointer_trails";
    public string Title => "Disable pointer trails";
    public string Description => "Turn off mouse pointer trails.";
    public string Category => "Input latency";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "Reduces visual trail effects.";
    public string? Drawback => "Pointer trails are no longer visible.";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;
    public bool AffectsUserFeeling => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        using var key = Registry.CurrentUser.OpenSubKey(MousePath);
        var val = key?.GetValue(ValueName) as string ?? TargetValue;
        var disabled = val == TargetValue || val == "-1";
        return Task.FromResult(new OperationStatus(disabled, disabled ? "Pointer trails are disabled." : $"Current value: {val}."));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        using var key = Registry.CurrentUser.CreateSubKey(MousePath);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open mouse registry path.");

        var old = key.GetValue(ValueName) as string;
        if (old == TargetValue || old == "-1")
            return new OperationInvokeResult(true, false, "Pointer trails are already disabled.");

        var snapshot = new Snapshot(old);
        await ctx.SaveSnapshotAsync(Id, snapshot, ct).ConfigureAwait(false);

        bool IsOriginalStateIntact() => InputRegistryValueHelper.MatchesStringValue(key, ValueName, snapshot.MouseTrails);
        (bool Success, string Message) RestoreOriginalState() =>
            InputRegistryValueHelper.RestoreStringValue(key, ValueName, snapshot.MouseTrails)
                ? (true, string.Empty)
                : (false, "Failed to verify restored pointer trails state.");

        try
        {
            if (!InputRegistryValueHelper.WriteStringValue(key, ValueName, TargetValue))
            {
                return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                    ctx,
                    Id,
                    "Pointer trails write failed.",
                    IsOriginalStateIntact,
                    RestoreOriginalState,
                    "Original state remained intact.",
                    "Original state restored.",
                    ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                ctx,
                Id,
                string.Concat("Pointer trails apply threw an exception: ", ex.Message),
                IsOriginalStateIntact,
                RestoreOriginalState,
                "Original state remained intact.",
                "Original state restored.",
                ct).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, "Pointer trails were disabled.");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var snap = await ctx.LoadSnapshotAsync<Snapshot>(Id, ct).ConfigureAwait(false);
        if (snap is null)
            return new OperationInvokeResult(false, false, "Pointer trails snapshot is missing or corrupted.");

        using var key = Registry.CurrentUser.OpenSubKey(MousePath, writable: true);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open mouse registry path.");

        return InputRegistryValueHelper.RestoreStringValue(key, ValueName, snap.MouseTrails)
            ? new OperationInvokeResult(true, true, "Pointer trails were restored.")
            : new OperationInvokeResult(false, false, "Pointer trails restore verification failed.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableSnapToOperation : IOptimizationOperation
{
    private record Snapshot(string? SnapToDefaultButton);
    private const string MousePath = @"Control Panel\Mouse";
    private const string ValueName = "SnapToDefaultButton";
    private const string TargetValue = "0";

    public string Id => "input.disable_snap_to";
    public string Title => "Disable snap-to";
    public string Description => "Disable automatic snap-to-default-button behavior.";
    public string Category => "Input latency";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "Avoids cursor jumps caused by snap-to behavior.";
    public string? Drawback => "Dialog default buttons no longer attract the cursor.";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;
    public bool AffectsUserFeeling => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        using var key = Registry.CurrentUser.OpenSubKey(MousePath);
        var val = key?.GetValue(ValueName) as string ?? TargetValue;
        var disabled = val == TargetValue;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "Snap-to is disabled." : $"Current value: {val}."));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        using var key = Registry.CurrentUser.CreateSubKey(MousePath);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open mouse registry path.");

        var old = key.GetValue(ValueName) as string;
        if (old == TargetValue)
            return new OperationInvokeResult(true, false, "Snap-to is already disabled.");

        var snapshot = new Snapshot(old);
        await ctx.SaveSnapshotAsync(Id, snapshot, ct).ConfigureAwait(false);

        bool IsOriginalStateIntact() => InputRegistryValueHelper.MatchesStringValue(key, ValueName, snapshot.SnapToDefaultButton);
        (bool Success, string Message) RestoreOriginalState() =>
            InputRegistryValueHelper.RestoreStringValue(key, ValueName, snapshot.SnapToDefaultButton)
                ? (true, string.Empty)
                : (false, "Failed to verify restored snap-to state.");

        try
        {
            if (!InputRegistryValueHelper.WriteStringValue(key, ValueName, TargetValue))
            {
                return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                    ctx,
                    Id,
                    "Snap-to write failed.",
                    IsOriginalStateIntact,
                    RestoreOriginalState,
                    "Original state remained intact.",
                    "Original state restored.",
                    ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                ctx,
                Id,
                string.Concat("Snap-to apply threw an exception: ", ex.Message),
                IsOriginalStateIntact,
                RestoreOriginalState,
                "Original state remained intact.",
                "Original state restored.",
                ct).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, "Snap-to was disabled.");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var snap = await ctx.LoadSnapshotAsync<Snapshot>(Id, ct).ConfigureAwait(false);
        if (snap is null)
            return new OperationInvokeResult(false, false, "Snap-to snapshot is missing or corrupted.");

        using var key = Registry.CurrentUser.OpenSubKey(MousePath, writable: true);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open mouse registry path.");

        return InputRegistryValueHelper.RestoreStringValue(key, ValueName, snap.SnapToDefaultButton)
            ? new OperationInvokeResult(true, true, "Snap-to was restored.")
            : new OperationInvokeResult(false, false, "Snap-to restore verification failed.");
    }
}
[SupportedOSPlatform("windows")]
public sealed class DisableTouchFeedbackOperation : IOptimizationOperation
{
    private record Snapshot(int? ContactVisualization, int? GestureVisualization);
    private const string TouchPath = @"Control Panel\Cursors";
    private const string ContactValueName = "ContactVisualization";
    private const string GestureValueName = "GestureVisualization";
    private const int TargetValue = 0;

    public string Id => "input.disable_touch_feedback";
    public string Title => "Disable touch feedback";
    public string Description => "Disable touch contact and gesture visualization.";
    public string Category => "Input latency";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "Reduces touch feedback overlays.";
    public string? Drawback => "Touch feedback visuals are no longer shown.";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;
    public bool AffectsUserFeeling => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        using var key = Registry.CurrentUser.OpenSubKey(TouchPath);
        var contact = InputRegistryValueHelper.ReadDwordValue(key, ContactValueName) ?? TargetValue;
        var gesture = InputRegistryValueHelper.ReadDwordValue(key, GestureValueName) ?? TargetValue;
        var disabled = contact == TargetValue && gesture == TargetValue;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "Touch feedback is disabled." : $"Current values: contact={contact}, gesture={gesture}."));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        using var key = Registry.CurrentUser.CreateSubKey(TouchPath);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open touch registry path.");

        var oldContact = key.GetValue(ContactValueName) as int?;
        var oldGesture = key.GetValue(GestureValueName) as int?;
        if (oldContact == TargetValue && oldGesture == TargetValue)
            return new OperationInvokeResult(true, false, "Touch feedback is already disabled.");

        var snapshot = new Snapshot(oldContact, oldGesture);
        await ctx.SaveSnapshotAsync(Id, snapshot, ct).ConfigureAwait(false);

        bool IsOriginalStateIntact() =>
            InputRegistryValueHelper.MatchesDwordValue(key, ContactValueName, snapshot.ContactVisualization)
            && InputRegistryValueHelper.MatchesDwordValue(key, GestureValueName, snapshot.GestureVisualization);

        (bool Success, string Message) RestoreOriginalState()
        {
            var contactRestored = InputRegistryValueHelper.RestoreDwordValue(key, ContactValueName, snapshot.ContactVisualization);
            var gestureRestored = InputRegistryValueHelper.RestoreDwordValue(key, GestureValueName, snapshot.GestureVisualization);
            return contactRestored && gestureRestored
                ? (true, string.Empty)
                : (false, "Failed to verify restored touch feedback state.");
        }

        try
        {
            if (!InputRegistryValueHelper.WriteDwordValue(key, ContactValueName, TargetValue)
                || !InputRegistryValueHelper.WriteDwordValue(key, GestureValueName, TargetValue))
            {
                return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                    ctx,
                    Id,
                    "Touch feedback write failed.",
                    IsOriginalStateIntact,
                    RestoreOriginalState,
                    "Original state remained intact.",
                    "Original state restored.",
                    ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                ctx,
                Id,
                string.Concat("Touch feedback apply threw an exception: ", ex.Message),
                IsOriginalStateIntact,
                RestoreOriginalState,
                "Original state remained intact.",
                "Original state restored.",
                ct).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, "Touch feedback was disabled.");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var snap = await ctx.LoadSnapshotAsync<Snapshot>(Id, ct).ConfigureAwait(false);
        if (snap is null)
            return new OperationInvokeResult(false, false, "Touch feedback snapshot is missing or corrupted.");

        using var key = Registry.CurrentUser.OpenSubKey(TouchPath, writable: true);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open touch registry path.");

        var contactRestored = InputRegistryValueHelper.RestoreDwordValue(key, ContactValueName, snap.ContactVisualization);
        var gestureRestored = InputRegistryValueHelper.RestoreDwordValue(key, GestureValueName, snap.GestureVisualization);
        return contactRestored && gestureRestored
            ? new OperationInvokeResult(true, true, "Touch feedback was restored.")
            : new OperationInvokeResult(false, false, "Touch feedback restore verification failed.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisablePenFeedbackOperation : IOptimizationOperation
{
    private record Snapshot(int? PenVisualization);
    private const string PenPath = @"Software\Microsoft\TabletTip\1.7";
    private const string ValueName = "PenVisualization";
    private const int TargetValue = 0;

    public string Id => "input.disable_pen_feedback";
    public string Title => "Disable pen feedback";
    public string Description => "Disable pen visualization feedback.";
    public string Category => "Input latency";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "Removes pen feedback overlays.";
    public string? Drawback => "Pen feedback visuals are no longer shown.";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;
    public bool AffectsUserFeeling => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PenPath);
        var val = InputRegistryValueHelper.ReadDwordValue(key, ValueName) ?? 1;
        var disabled = val == TargetValue;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "Pen feedback is disabled." : $"Current value: {val}."));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PenPath);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open pen registry path.");

        var old = key.GetValue(ValueName) as int?;
        if (old == TargetValue)
            return new OperationInvokeResult(true, false, "Pen feedback is already disabled.");

        var snapshot = new Snapshot(old);
        await ctx.SaveSnapshotAsync(Id, snapshot, ct).ConfigureAwait(false);

        bool IsOriginalStateIntact() => InputRegistryValueHelper.MatchesDwordValue(key, ValueName, snapshot.PenVisualization);
        (bool Success, string Message) RestoreOriginalState() =>
            InputRegistryValueHelper.RestoreDwordValue(key, ValueName, snapshot.PenVisualization)
                ? (true, string.Empty)
                : (false, "Failed to verify restored pen feedback state.");

        try
        {
            if (!InputRegistryValueHelper.WriteDwordValue(key, ValueName, TargetValue))
            {
                return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                    ctx,
                    Id,
                    "Pen feedback write failed.",
                    IsOriginalStateIntact,
                    RestoreOriginalState,
                    "Original state remained intact.",
                    "Original state restored.",
                    ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                ctx,
                Id,
                string.Concat("Pen feedback apply threw an exception: ", ex.Message),
                IsOriginalStateIntact,
                RestoreOriginalState,
                "Original state remained intact.",
                "Original state restored.",
                ct).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, "Pen feedback was disabled.");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var snap = await ctx.LoadSnapshotAsync<Snapshot>(Id, ct).ConfigureAwait(false);
        if (snap is null)
            return new OperationInvokeResult(false, false, "Pen feedback snapshot is missing or corrupted.");

        using var key = Registry.CurrentUser.OpenSubKey(PenPath, writable: true);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open pen registry path.");

        return InputRegistryValueHelper.RestoreDwordValue(key, ValueName, snap.PenVisualization)
            ? new OperationInvokeResult(true, true, "Pen feedback was restored.")
            : new OperationInvokeResult(false, false, "Pen feedback restore verification failed.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class OptimizeUsbPollingOperation : IOptimizationOperation
{
    private record Snapshot(int? MouseDataQueueSize, int? KeyboardDataQueueSize);
    private const string MousePath = @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
    private const string KeyboardPath = @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";
    private const string MouseQueueValueName = "MouseDataQueueSize";
    private const string KeyboardQueueValueName = "KeyboardDataQueueSize";
    private const int DefaultTargetQueueSize = 200;
    private const int DefaultRestoreQueueSize = 100;

    public string Id => "input.optimize_usb_polling";
    public string Title => "Optimize USB queues";
    public string Description => "Tune mouse and keyboard queue sizes for input responsiveness.";
    public string Category => "Input latency";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit => "May improve USB input responsiveness.";
    public string? Drawback => "Queue sizes may be incompatible with some drivers.";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public bool AffectsUserFeeling => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        using var mouseKey = Registry.LocalMachine.OpenSubKey(MousePath);
        using var keyboardKey = Registry.LocalMachine.OpenSubKey(KeyboardPath);
        var settings = PeripheralSettingsProvider.GetSettings();
        var targetMouse = settings?.MouseDataQueueSize ?? DefaultTargetQueueSize;
        var targetKeyboard = settings?.KeyboardDataQueueSize ?? DefaultTargetQueueSize;
        var currentMouse = InputRegistryValueHelper.ReadDwordValue(mouseKey, MouseQueueValueName) ?? DefaultRestoreQueueSize;
        var currentKeyboard = InputRegistryValueHelper.ReadDwordValue(keyboardKey, KeyboardQueueValueName) ?? DefaultRestoreQueueSize;
        var optimized = currentMouse == targetMouse && currentKeyboard == targetKeyboard;
        return Task.FromResult(new OperationStatus(optimized, optimized ? "USB queue sizes match the target values." : $"Current mouse={currentMouse}, keyboard={currentKeyboard}; expected mouse={targetMouse}, keyboard={targetKeyboard}."));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        using var mouseKey = Registry.LocalMachine.CreateSubKey(MousePath);
        using var keyboardKey = Registry.LocalMachine.CreateSubKey(KeyboardPath);
        if (mouseKey == null || keyboardKey == null)
            return new OperationInvokeResult(false, false, "Unable to open USB queue registry paths.");

        var settings = PeripheralSettingsProvider.GetSettings();
        var targetMouse = settings?.MouseDataQueueSize ?? DefaultTargetQueueSize;
        var targetKeyboard = settings?.KeyboardDataQueueSize ?? DefaultTargetQueueSize;

        var oldMouse = mouseKey.GetValue(MouseQueueValueName) as int?;
        var oldKeyboard = keyboardKey.GetValue(KeyboardQueueValueName) as int?;
        if (oldMouse == targetMouse && oldKeyboard == targetKeyboard)
            return new OperationInvokeResult(true, false, "USB queue sizes are already optimized.");

        var snapshot = new Snapshot(oldMouse, oldKeyboard);
        await ctx.SaveSnapshotAsync(Id, snapshot, ct).ConfigureAwait(false);

        bool IsOriginalStateIntact() =>
            InputRegistryValueHelper.MatchesDwordValue(mouseKey, MouseQueueValueName, snapshot.MouseDataQueueSize)
            && InputRegistryValueHelper.MatchesDwordValue(keyboardKey, KeyboardQueueValueName, snapshot.KeyboardDataQueueSize);

        (bool Success, string Message) RestoreOriginalState()
        {
            var mouseRestored = InputRegistryValueHelper.RestoreDwordValue(mouseKey, MouseQueueValueName, snapshot.MouseDataQueueSize);
            var keyboardRestored = InputRegistryValueHelper.RestoreDwordValue(keyboardKey, KeyboardQueueValueName, snapshot.KeyboardDataQueueSize);
            return mouseRestored && keyboardRestored
                ? (true, string.Empty)
                : (false, "Failed to verify restored USB queue state.");
        }

        try
        {
            if (!InputRegistryValueHelper.WriteDwordValue(mouseKey, MouseQueueValueName, targetMouse)
                || !InputRegistryValueHelper.WriteDwordValue(keyboardKey, KeyboardQueueValueName, targetKeyboard))
            {
                return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                    ctx,
                    Id,
                    "USB queue write failed.",
                    IsOriginalStateIntact,
                    RestoreOriginalState,
                    "Original state remained intact.",
                    "Original state restored.",
                    ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                ctx,
                Id,
                string.Concat("USB queue apply threw an exception: ", ex.Message),
                IsOriginalStateIntact,
                RestoreOriginalState,
                "Original state remained intact.",
                "Original state restored.",
                ct).ConfigureAwait(false);
        }

        var message = settings is null
            ? $"USB queue sizes were set to the default optimized values (mouse={targetMouse}, keyboard={targetKeyboard})."
            : $"Custom USB queue sizes were applied (mouse={targetMouse}, keyboard={targetKeyboard}).";
        return new OperationInvokeResult(true, true, message);
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var snap = await ctx.LoadSnapshotAsync<Snapshot>(Id, ct).ConfigureAwait(false);
        if (snap is null)
            return new OperationInvokeResult(false, false, "USB queue snapshot is missing or corrupted.");

        using var mouseKey = Registry.LocalMachine.OpenSubKey(MousePath, writable: true);
        using var keyboardKey = Registry.LocalMachine.OpenSubKey(KeyboardPath, writable: true);
        if (mouseKey == null || keyboardKey == null)
            return new OperationInvokeResult(false, false, "Unable to open USB queue registry paths.");

        var mouseValue = snap.MouseDataQueueSize ?? DefaultRestoreQueueSize;
        var keyboardValue = snap.KeyboardDataQueueSize ?? DefaultRestoreQueueSize;
        mouseKey.SetValue(MouseQueueValueName, mouseValue, RegistryValueKind.DWord);
        keyboardKey.SetValue(KeyboardQueueValueName, keyboardValue, RegistryValueKind.DWord);
        return new OperationInvokeResult(true, true, "USB queue sizes were restored.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class OptimizeRawInputBufferOperation : IOptimizationOperation
{
    private record Snapshot(int? OriginalGpuPriority);
    private const string GamesTaskPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    private const string ValueName = "GPU Priority";
    private const int TargetGpuPriority = 8;

    public string Id => "input.optimize_raw_input_buffer";
    public string Title => "Optimize raw input buffer";
    public string Description => "Raise the Games task GPU priority to improve responsiveness.";
    public string Category => "Input latency";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit => "May improve responsiveness for raw input paths.";
    public string? Drawback => "Can conflict with custom multimedia task settings.";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public bool AffectsUserFeeling => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        using var key = Registry.LocalMachine.OpenSubKey(GamesTaskPath);
        var current = InputRegistryValueHelper.ReadDwordValue(key, ValueName);
        var optimized = current.HasValue && current.Value >= TargetGpuPriority;
        return Task.FromResult(new OperationStatus(optimized, optimized ? $"GPU Priority is {current}." : $"Current GPU Priority is {current?.ToString() ?? "<missing>"}."));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        using var key = Registry.LocalMachine.CreateSubKey(GamesTaskPath);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open Games task registry path.");

        var oldGpuPriority = key.GetValue(ValueName) as int?;
        if (oldGpuPriority >= TargetGpuPriority)
            return new OperationInvokeResult(true, false, "Games task GPU priority is already optimized.");

        var snapshot = new Snapshot(oldGpuPriority);
        await ctx.SaveSnapshotAsync(Id, snapshot, ct).ConfigureAwait(false);

        bool IsOriginalStateIntact() => InputRegistryValueHelper.MatchesDwordValue(key, ValueName, snapshot.OriginalGpuPriority);
        (bool Success, string Message) RestoreOriginalState() =>
            InputRegistryValueHelper.RestoreDwordValue(key, ValueName, snapshot.OriginalGpuPriority)
                ? (true, string.Empty)
                : (false, "Failed to verify restored GPU priority state.");

        try
        {
            if (!InputRegistryValueHelper.WriteDwordValue(key, ValueName, TargetGpuPriority))
            {
                return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                    ctx,
                    Id,
                    "GPU priority write failed.",
                    IsOriginalStateIntact,
                    RestoreOriginalState,
                    "Original state remained intact.",
                    "Original state restored.",
                    ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                ctx,
                Id,
                string.Concat("GPU priority apply threw an exception: ", ex.Message),
                IsOriginalStateIntact,
                RestoreOriginalState,
                "Original state remained intact.",
                "Original state restored.",
                ct).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, $"Games task GPU priority was set to {TargetGpuPriority}.");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var snap = await ctx.LoadSnapshotAsync<Snapshot>(Id, ct).ConfigureAwait(false);
        if (snap is null)
            return new OperationInvokeResult(false, false, "Raw input buffer snapshot is missing or corrupted.");

        using var key = Registry.LocalMachine.OpenSubKey(GamesTaskPath, writable: true);
        if (key == null)
            return new OperationInvokeResult(false, false, "Unable to open Games task registry path.");

        var gpuPriorityValue = snap.OriginalGpuPriority ?? TargetGpuPriority;
        key.SetValue(ValueName, gpuPriorityValue, RegistryValueKind.DWord);
        return new OperationInvokeResult(true, true, "Games task GPU priority was restored.");
    }
}
