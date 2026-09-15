using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Utilities;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

internal static class AudioRegistryRestoreHelper
{
    public static RegistryKey? OpenWritableCurrentUserKey(string path) => Registry.CurrentUser.CreateSubKey(path);

    public static RegistryKey? OpenWritableLocalMachineKey(string path) => Registry.LocalMachine.CreateSubKey(path);

    public static int? ReadDwordValue(RegistryKey key, string valueName) => key.GetValue(valueName) as int?;

    public static string? ReadStringValue(RegistryKey key, string valueName) => key.GetValue(valueName) as string;

    public static bool MatchesDwordValue(RegistryKey key, string valueName, int? value) =>
        ReadDwordValue(key, valueName) == value;

    public static bool MatchesStringValue(RegistryKey key, string valueName, string? value) =>
        string.Equals(ReadStringValue(key, valueName), value, StringComparison.OrdinalIgnoreCase);

    public static void RestoreDwordValue(RegistryKey key, string valueName, int? value)
    {
        if (value.HasValue)
        {
            key.SetValue(valueName, value.Value, RegistryValueKind.DWord);
            return;
        }

        key.DeleteValue(valueName, false);
    }

    public static void RestoreStringValue(RegistryKey key, string valueName, string? value)
    {
        if (value is not null)
        {
            key.SetValue(valueName, value, RegistryValueKind.String);
            return;
        }

        key.DeleteValue(valueName, false);
    }
}

internal static class AudioOperationTransactionHelper
{
    public static async Task<OperationInvokeResult> ApplyWithRollbackAsync<TSnapshot>(
        OperationExecutionContext context,
        string operationId,
        TSnapshot snapshot,
        Action applyChange,
        Func<bool> verifyApplied,
        Func<TSnapshot, bool> verifyOriginal,
        Action<TSnapshot> restoreOriginal,
        string successMessage,
        string applyFailurePrefix,
        string verifyFailureMessage,
        CancellationToken cancellationToken)
    {
        await context.SaveSnapshotAsync(operationId, snapshot, cancellationToken).ConfigureAwait(false);

        try
        {
            applyChange();
        }
        catch (Exception ex)
        {
            return await FailApplyAndTryRollbackAsync(
                context,
                operationId,
                snapshot,
                verifyOriginal,
                restoreOriginal,
                $"{applyFailurePrefix} {ex.Message}",
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (verifyApplied())
            {
                return new OperationInvokeResult(true, true, successMessage);
            }
        }
        catch (Exception ex)
        {
            return await FailApplyAndTryRollbackAsync(
                context,
                operationId,
                snapshot,
                verifyOriginal,
                restoreOriginal,
                $"{verifyFailureMessage} {ex.Message}",
                cancellationToken).ConfigureAwait(false);
        }

        return await FailApplyAndTryRollbackAsync(
            context,
            operationId,
            snapshot,
            verifyOriginal,
            restoreOriginal,
            verifyFailureMessage,
            cancellationToken).ConfigureAwait(false);
    }

    public static OperationInvokeResult RestoreWithVerification<TSnapshot>(
        TSnapshot snapshot,
        Action<TSnapshot> restoreOriginal,
        Func<TSnapshot, bool> verifyRestored,
        string successMessage,
        string restoreFailurePrefix,
        string verifyFailureMessage)
    {
        try
        {
            restoreOriginal(snapshot);
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, $"{restoreFailurePrefix} {ex.Message}");
        }

        try
        {
            return verifyRestored(snapshot)
                ? new OperationInvokeResult(true, true, successMessage)
                : new OperationInvokeResult(false, false, verifyFailureMessage);
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, $"{verifyFailureMessage} {ex.Message}");
        }
    }

    private static async Task<OperationInvokeResult> FailApplyAndTryRollbackAsync<TSnapshot>(
        OperationExecutionContext context,
        string operationId,
        TSnapshot snapshot,
        Func<TSnapshot, bool> verifyOriginal,
        Action<TSnapshot> restoreOriginal,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalStateAsync(
            context,
            operationId,
            snapshot,
            verifyOriginal,
            restoreOriginal,
            cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(false, false, $"{failureMessage} {rollback.Message}");
    }

    private static async Task<(bool Success, string Message)> TryRestoreOriginalStateAsync<TSnapshot>(
        OperationExecutionContext context,
        string operationId,
        TSnapshot snapshot,
        Func<TSnapshot, bool> verifyOriginal,
        Action<TSnapshot> restoreOriginal,
        CancellationToken cancellationToken)
    {
        try
        {
            if (verifyOriginal(snapshot))
            {
                await context.TryDeleteSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
                return (true, "Original audio setting remained intact; stale snapshot removed.");
            }
        }
        catch
        {
        }

        try
        {
            restoreOriginal(snapshot);
        }
        catch (Exception ex)
        {
            return (false, $"Automatic rollback failed. {ex.Message}");
        }

        try
        {
            if (!verifyOriginal(snapshot))
            {
                return (false, "Automatic rollback failed. Restored values could not be verified.");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Automatic rollback failed. Restored values could not be verified. {ex.Message}");
        }

        await context.TryDeleteSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
        return (true, "Original audio setting restored.");
    }
}

/// <summary>
/// 禁用音频增强效果 - 减少音频处理延迟
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisableAudioEnhancementsOperation : IOptimizationOperation
{
    private record AudioEnhancementSnapshot(int? OriginalValue);

    public string Id => "audio.disable_enhancements";
    public string Title => "禁用音频增强";
    public string Description => "关闭 Windows 音频增强效果，减少音频处理延迟，适合游戏和专业音频应用。";
    public string Category => "音频优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "可能减少音频处理延迟，声音更真实";
    public string? Drawback => "失去虚拟环绕声等音效增强";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    private const string AudioKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AudioKeyPath);
            var value = key?.GetValue("DisableSystemEffects") as int?;
            var isDisabled = value == 1;
            return Task.FromResult(new OperationStatus(isDisabled, isDisabled ? "音频增强已禁用" : "音频增强已启用"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, $"检测失败：{ex.Message}"));
        }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AudioKeyPath);
            if (key == null)
            {
                return new OperationInvokeResult(false, false, "Unable to open the audio enhancements registry key.");
            }

            var originalValue = AudioRegistryRestoreHelper.ReadDwordValue(key, "DisableSystemEffects");

            if (originalValue == 1)
            {
                return new OperationInvokeResult(true, false, "Audio enhancements are already disabled.");
            }

            return await AudioOperationTransactionHelper.ApplyWithRollbackAsync(
                context,
                Id,
                new AudioEnhancementSnapshot(originalValue),
                () => key.SetValue("DisableSystemEffects", 1, RegistryValueKind.DWord),
                () => AudioRegistryRestoreHelper.MatchesDwordValue(key, "DisableSystemEffects", 1),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "DisableSystemEffects", state.OriginalValue),
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "DisableSystemEffects", state.OriginalValue),
                "Audio enhancements disabled.",
                "Failed to disable audio enhancements.",
                "Audio enhancement change could not be verified.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, $"Failed to disable audio enhancements. {ex.Message}");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<AudioEnhancementSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new OperationInvokeResult(false, false, "Audio enhancement snapshot is missing.");

        try
        {
            using var key = AudioRegistryRestoreHelper.OpenWritableCurrentUserKey(AudioKeyPath);
            if (key == null)
                return new OperationInvokeResult(false, false, "Unable to open the audio enhancements registry key.");

            return AudioOperationTransactionHelper.RestoreWithVerification(
                snapshot,
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "DisableSystemEffects", state.OriginalValue),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "DisableSystemEffects", state.OriginalValue),
                "Audio enhancements restored.",
                "Failed to restore audio enhancements.",
                "Audio enhancement restore could not be verified.");
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, $"Failed to restore audio enhancements. {ex.Message}");
        }
    }
}

/// <summary>
/// 设置音频独占模式优先 - 降低音频延迟
/// </summary>
// FIX-12/22: SystemResponsiveness 由 network_advanced 模块统一管理，此操作改为优化 Audio 任务优先级
[SupportedOSPlatform("windows")]
public sealed class AudioExclusiveModeOperation : IOptimizationOperation
{
    private record AudioExclusiveSnapshot(int? OriginalPriority);

    public string Id => "audio.exclusive_mode_priority";
    public string Title => "音频独占模式优先";
    public string Description => "提升音频任务优先级，获得最低延迟的音频输出。";
    public string Category => "音频优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "可能获得更低延迟的音频输出";
    public string? Drawback => "独占模式下其他应用无法播放声音";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string AudioTaskPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AudioTaskPath);
            var priority = key?.GetValue("Priority") as int? ?? 2;
            var isOptimized = priority >= 6;
            return Task.FromResult(new OperationStatus(isOptimized, isOptimized ? "音频优先级已优化" : "音频优先级为默认"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, $"检测失败：{ex.Message}"));
        }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(AudioTaskPath);
            if (key == null) return new OperationInvokeResult(false, false, "Unable to open the audio task priority registry key.");

            var originalPriority = AudioRegistryRestoreHelper.ReadDwordValue(key, "Priority");

            if ((originalPriority ?? 2) >= 6)
            {
                return new OperationInvokeResult(true, false, "Audio task priority is already optimized.");
            }

            return await AudioOperationTransactionHelper.ApplyWithRollbackAsync(
                context,
                Id,
                new AudioExclusiveSnapshot(originalPriority),
                () => key.SetValue("Priority", 6, RegistryValueKind.DWord),
                () => AudioRegistryRestoreHelper.MatchesDwordValue(key, "Priority", 6),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "Priority", state.OriginalPriority),
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "Priority", state.OriginalPriority),
                "Audio task priority optimized.",
                "Failed to optimize audio task priority.",
                "Audio task priority change could not be verified.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "Audio optimizations");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<AudioExclusiveSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new OperationInvokeResult(false, false, "Audio exclusive mode snapshot is missing.");

        try
        {
            using var key = AudioRegistryRestoreHelper.OpenWritableLocalMachineKey(AudioTaskPath);
            if (key == null)
                return new OperationInvokeResult(false, false, "Unable to open the audio task priority registry key.");

            return AudioOperationTransactionHelper.RestoreWithVerification(
                snapshot,
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "Priority", state.OriginalPriority),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "Priority", state.OriginalPriority),
                "Audio task priority restored.",
                "Failed to restore audio task priority.",
                "Audio task priority restore could not be verified.");
        }
        catch (Exception ex) { return new OperationInvokeResult(false, false, $"Failed to restore audio task priority. {ex.Message}"); }
    }
}


[SupportedOSPlatform("windows")]
public sealed class DisableSpatialSoundOperation : IOptimizationOperation
{
    private record SpatialSoundSnapshot(int? OriginalValue);

    public string Id => "audio.disable_spatial_sound";
    public string Title => "禁用空间音效";
    public string Description => "关闭 Windows Sonic 等空间音效处理，减少音频延迟。";
    public string Category => "音频优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "减少音频处理延迟";
    public string? Drawback => "失去 3D 环绕声效果";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            var value = key?.GetValue("EnableSpatialAudio") as int? ?? 1;
            var isDisabled = value == 0;
            return Task.FromResult(new OperationStatus(isDisabled, isDisabled ? "空间音效已禁用" : "空间音效已启用"));
        }
        catch (Exception ex) { return Task.FromResult(new OperationStatus(false, $"检测失败：{ex.Message}")); }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (key == null) return new OperationInvokeResult(false, false, "Unable to open the spatial sound registry key.");

            var original = AudioRegistryRestoreHelper.ReadDwordValue(key, "EnableSpatialAudio");
            if (original == 0)
            {
                return new OperationInvokeResult(true, false, "Spatial sound is already disabled.");
            }

            return await AudioOperationTransactionHelper.ApplyWithRollbackAsync(
                context,
                Id,
                new SpatialSoundSnapshot(original),
                () => key.SetValue("EnableSpatialAudio", 0, RegistryValueKind.DWord),
                () => AudioRegistryRestoreHelper.MatchesDwordValue(key, "EnableSpatialAudio", 0),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "EnableSpatialAudio", state.OriginalValue),
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "EnableSpatialAudio", state.OriginalValue),
                "Spatial sound disabled.",
                "Failed to disable spatial sound.",
                "Spatial sound change could not be verified.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "Audio optimizations");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<SpatialSoundSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new OperationInvokeResult(false, false, "Spatial sound snapshot is missing.");

        try
        {
            using var key = AudioRegistryRestoreHelper.OpenWritableCurrentUserKey(KeyPath);
            if (key == null)
                return new OperationInvokeResult(false, false, "Unable to open the spatial sound registry key.");

            return AudioOperationTransactionHelper.RestoreWithVerification(
                snapshot,
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "EnableSpatialAudio", state.OriginalValue),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "EnableSpatialAudio", state.OriginalValue),
                "Spatial sound restored.",
                "Failed to restore spatial sound.",
                "Spatial sound restore could not be verified.");
        }
        catch (Exception ex) { return new OperationInvokeResult(false, false, $"Failed to restore spatial sound. {ex.Message}"); }
    }
}

[SupportedOSPlatform("windows")]
public sealed class OptimizeAudioBufferOperation : IOptimizationOperation
{
    private record AudioBufferSnapshot(int? OriginalValue, bool OriginalValueExists);

    public string Id => "audio.optimize_buffer";
    public string Title => "优化音频缓冲区";
    public string Description => "减小音频缓冲区大小，降低音频延迟（可能增加爆音风险）。";
    public string Category => "音频优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit => "降低音频延迟约 10-20ms";
    public string? Drawback => "低端声卡可能出现爆音";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            var value = key?.GetValue("Latency Sensitive") as string ?? "False";
            var isOptimized = value.Equals("True", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new OperationStatus(isOptimized, isOptimized ? "音频缓冲区已优化" : "音频缓冲区为默认"));
        }
        catch (Exception ex) { return Task.FromResult(new OperationStatus(false, $"检测失败：{ex.Message}")); }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(KeyPath);
            if (key == null) return new OperationInvokeResult(false, false, "Unable to open the audio buffer registry key.");

            var original = AudioRegistryRestoreHelper.ReadStringValue(key, "Latency Sensitive");
            if (AudioRegistryRestoreHelper.MatchesStringValue(key, "Latency Sensitive", "True"))
            {
                return new OperationInvokeResult(true, false, "Audio buffer is already optimized.");
            }

            return await AudioOperationTransactionHelper.ApplyWithRollbackAsync(
                context,
                Id,
                CreateAudioBufferSnapshot(original),
                () => key.SetValue("Latency Sensitive", "True", RegistryValueKind.String),
                () => AudioRegistryRestoreHelper.MatchesStringValue(key, "Latency Sensitive", "True"),
                state => AudioRegistryRestoreHelper.MatchesStringValue(key, "Latency Sensitive", ResolveAudioBufferValue(state)),
                state => AudioRegistryRestoreHelper.RestoreStringValue(key, "Latency Sensitive", ResolveAudioBufferValue(state)),
                "Audio buffer optimized.",
                "Failed to optimize audio buffer.",
                "Audio buffer change could not be verified.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "Audio optimizations");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<AudioBufferSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new OperationInvokeResult(false, false, "Audio buffer snapshot is missing.");

        try
        {
            using var key = AudioRegistryRestoreHelper.OpenWritableLocalMachineKey(KeyPath);
            if (key == null)
                return new OperationInvokeResult(false, false, "Unable to open the audio buffer registry key.");

            return AudioOperationTransactionHelper.RestoreWithVerification(
                snapshot,
                state => AudioRegistryRestoreHelper.RestoreStringValue(key, "Latency Sensitive", ResolveAudioBufferValue(state)),
                state => AudioRegistryRestoreHelper.MatchesStringValue(key, "Latency Sensitive", ResolveAudioBufferValue(state)),
                "Audio buffer settings restored.",
                "Failed to restore audio buffer settings.",
                "Audio buffer restore could not be verified.");
        }
        catch (Exception ex) { return new OperationInvokeResult(false, false, $"Failed to restore audio buffer settings. {ex.Message}"); }
    }

    private static AudioBufferSnapshot CreateAudioBufferSnapshot(string? value) =>
        new(
            value is null ? null : (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ? 1 : 0),
            value is not null);

    private static string? ResolveAudioBufferValue(AudioBufferSnapshot snapshot)
    {
        if (!snapshot.OriginalValueExists && !snapshot.OriginalValue.HasValue)
        {
            return null;
        }

        return snapshot.OriginalValue == 1 ? "True" : "False";
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableAudioServiceRestartOperation : IOptimizationOperation
{
    private record AudioServiceSnapshot(int? OriginalRecoveryAction);

    public string Id => "audio.disable_service_restart";
    public string Title => "禁用音频服务自动重启";
    public string Description => "防止 Windows Audio 服务意外重启导致游戏音频中断。";
    public string Category => "音频优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "避免游戏中音频突然中断";
    public string? Drawback => "音频服务崩溃后需手动重启";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Services\Audiosrv";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            // 检查 DelayedAutoStart 是否为 0（禁用延迟启动 = 更快响应）
            var delayedStart = key?.GetValue("DelayedAutoStart") as int? ?? 1;
            var isOptimized = delayedStart == 0;
            return Task.FromResult(new OperationStatus(isOptimized, isOptimized ? "音频服务已优化" : "音频服务为默认配置"));
        }
        catch (Exception ex) { return Task.FromResult(new OperationStatus(false, $"检测失败：{ex.Message}")); }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key == null) return new OperationInvokeResult(false, false, "Unable to open the audio service registry key.");

            var originalValue = AudioRegistryRestoreHelper.ReadDwordValue(key, "DelayedAutoStart");
            if ((originalValue ?? 1) == 0)
            {
                return new OperationInvokeResult(true, false, "Audio service delayed start is already disabled.");
            }

            return await AudioOperationTransactionHelper.ApplyWithRollbackAsync(
                context,
                Id,
                new AudioServiceSnapshot(originalValue),
                () => key.SetValue("DelayedAutoStart", 0, RegistryValueKind.DWord),
                () => AudioRegistryRestoreHelper.MatchesDwordValue(key, "DelayedAutoStart", 0),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "DelayedAutoStart", state.OriginalRecoveryAction),
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "DelayedAutoStart", state.OriginalRecoveryAction),
                "Audio service delayed start disabled.",
                "Failed to disable audio service delayed start.",
                "Audio service change could not be verified.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "Audio optimizations");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<AudioServiceSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new OperationInvokeResult(false, false, "Audio service snapshot is missing.");

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key == null)
                return new OperationInvokeResult(false, false, "Unable to open the audio service registry key.");

            return AudioOperationTransactionHelper.RestoreWithVerification(
                snapshot,
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "DelayedAutoStart", state.OriginalRecoveryAction),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "DelayedAutoStart", state.OriginalRecoveryAction),
                "Audio service settings restored.",
                "Failed to restore audio service settings.",
                "Audio service restore could not be verified.");
        }
        catch (Exception ex) { return new OperationInvokeResult(false, false, $"Failed to restore audio service settings. {ex.Message}"); }
    }
}

[SupportedOSPlatform("windows")]
public sealed class OptimizeSampleRateOperation : IOptimizationOperation
{
    private record SampleRateSnapshot(int? OriginalPriority, int? OriginalAffinity, string? OriginalSchedulingCategory, string? OriginalSfioPriority);

    public string Id => "audio.optimize_sample_rate";
    public string Title => "优化音频采样率";
    public string Description => "设置音频采样率为 48kHz/24bit，平衡音质和性能。";
    public string Category => "音频优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "获得更好的音频质量";
    public string? Drawback => "略微增加 CPU 开销";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    // 使用 Audio 任务配置，与 AudioExclusiveModeOperation 使用的 SystemProfile 不同
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            var isOptimized = key is not null && IsSampleRateOptimized(key);
            return Task.FromResult(new OperationStatus(isOptimized, isOptimized ? "音频采样率已优化" : "音频采样率为默认"));
        }
        catch (Exception ex) { return Task.FromResult(new OperationStatus(false, $"检测失败：{ex.Message}")); }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(KeyPath);
            if (key == null) return new OperationInvokeResult(false, false, "Unable to open the sample rate registry key.");

            var originalPriority = AudioRegistryRestoreHelper.ReadDwordValue(key, "Priority");
            var originalAffinity = AudioRegistryRestoreHelper.ReadDwordValue(key, "Affinity");
            var originalSchedulingCategory = AudioRegistryRestoreHelper.ReadStringValue(key, "Scheduling Category");
            var originalSfioPriority = AudioRegistryRestoreHelper.ReadStringValue(key, "SFIO Priority");

            if (IsSampleRateOptimized(key))
                return new OperationInvokeResult(true, false, "Sample rate settings are already optimized.");

            return await AudioOperationTransactionHelper.ApplyWithRollbackAsync(
                context,
                Id,
                new SampleRateSnapshot(originalPriority, originalAffinity, originalSchedulingCategory, originalSfioPriority),
                () =>
                {
                    key.SetValue("Priority", 6, RegistryValueKind.DWord);
                    key.SetValue("Affinity", 0, RegistryValueKind.DWord);
                    key.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                    key.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                },
                () => IsSampleRateOptimized(key),
                state => MatchesSampleRateSnapshot(key, state),
                state => RestoreSampleRateSnapshot(key, state),
                "Sample rate settings optimized.",
                "Failed to optimize sample rate settings.",
                "Sample rate optimization could not be verified.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "Audio optimizations");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<SampleRateSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new OperationInvokeResult(false, false, "Sample rate snapshot is missing.");

        try
        {
            using var key = AudioRegistryRestoreHelper.OpenWritableLocalMachineKey(KeyPath);
            if (key == null)
                return new OperationInvokeResult(false, false, "Unable to open the sample rate registry key.");

            return AudioOperationTransactionHelper.RestoreWithVerification(
                snapshot,
                state => RestoreSampleRateSnapshot(key, state),
                state => MatchesSampleRateSnapshot(key, state),
                "Sample rate settings restored.",
                "Failed to restore sample rate settings.",
                "Sample rate restore could not be verified.");
        }
        catch (Exception ex) { return new OperationInvokeResult(false, false, $"Failed to restore sample rate settings. {ex.Message}"); }
    }

    private static bool IsSampleRateOptimized(RegistryKey key) =>
        AudioRegistryRestoreHelper.MatchesDwordValue(key, "Priority", 6)
        && AudioRegistryRestoreHelper.MatchesDwordValue(key, "Affinity", 0)
        && AudioRegistryRestoreHelper.MatchesStringValue(key, "Scheduling Category", "High")
        && AudioRegistryRestoreHelper.MatchesStringValue(key, "SFIO Priority", "High");

    private static bool MatchesSampleRateSnapshot(RegistryKey key, SampleRateSnapshot snapshot) =>
        AudioRegistryRestoreHelper.MatchesDwordValue(key, "Priority", snapshot.OriginalPriority)
        && AudioRegistryRestoreHelper.MatchesDwordValue(key, "Affinity", snapshot.OriginalAffinity)
        && AudioRegistryRestoreHelper.MatchesStringValue(key, "Scheduling Category", snapshot.OriginalSchedulingCategory)
        && AudioRegistryRestoreHelper.MatchesStringValue(key, "SFIO Priority", snapshot.OriginalSfioPriority);

    private static void RestoreSampleRateSnapshot(RegistryKey key, SampleRateSnapshot snapshot)
    {
        AudioRegistryRestoreHelper.RestoreDwordValue(key, "Priority", snapshot.OriginalPriority);
        AudioRegistryRestoreHelper.RestoreDwordValue(key, "Affinity", snapshot.OriginalAffinity);
        AudioRegistryRestoreHelper.RestoreStringValue(key, "Scheduling Category", snapshot.OriginalSchedulingCategory);
        AudioRegistryRestoreHelper.RestoreStringValue(key, "SFIO Priority", snapshot.OriginalSfioPriority);
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableCommunicationsDuckingOperation : IOptimizationOperation
{
    private record DuckingSnapshot(int? OriginalValue);

    public string Id => "audio.disable_comm_ducking";
    public string Title => "禁用通信音量降低";
    public string Description => "关闭 Windows 在检测到通信活动时自动降低其他音量的功能。";
    public string Category => "音频优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "游戏音量不会被语音通话影响";
    public string? Drawback => "通话时可能听不清对方";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SOFTWARE\Microsoft\Multimedia\Audio";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            var value = key?.GetValue("UserDuckingPreference") as int? ?? 0;
            var isDisabled = value == 3;
            return Task.FromResult(new OperationStatus(isDisabled, isDisabled ? "通信音量降低已禁用" : "通信音量降低已启用"));
        }
        catch (Exception ex) { return Task.FromResult(new OperationStatus(false, $"检测失败：{ex.Message}")); }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (key == null) return new OperationInvokeResult(false, false, "Unable to open the communications ducking registry key.");

            var original = AudioRegistryRestoreHelper.ReadDwordValue(key, "UserDuckingPreference");
            if (original == 3)
            {
                return new OperationInvokeResult(true, false, "Communications ducking is already disabled.");
            }

            return await AudioOperationTransactionHelper.ApplyWithRollbackAsync(
                context,
                Id,
                new DuckingSnapshot(original),
                () => key.SetValue("UserDuckingPreference", 3, RegistryValueKind.DWord),
                () => AudioRegistryRestoreHelper.MatchesDwordValue(key, "UserDuckingPreference", 3),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "UserDuckingPreference", state.OriginalValue),
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "UserDuckingPreference", state.OriginalValue),
                "Communications ducking disabled.",
                "Failed to disable communications ducking.",
                "Communications ducking change could not be verified.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "Audio optimizations");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<DuckingSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new OperationInvokeResult(false, false, "Communications ducking snapshot is missing.");

        try
        {
            using var key = AudioRegistryRestoreHelper.OpenWritableCurrentUserKey(KeyPath);
            if (key == null)
                return new OperationInvokeResult(false, false, "Unable to open the communications ducking registry key.");

            return AudioOperationTransactionHelper.RestoreWithVerification(
                snapshot,
                state => AudioRegistryRestoreHelper.RestoreDwordValue(key, "UserDuckingPreference", state.OriginalValue),
                state => AudioRegistryRestoreHelper.MatchesDwordValue(key, "UserDuckingPreference", state.OriginalValue),
                "Communications ducking restored.",
                "Failed to restore communications ducking.",
                "Communications ducking restore could not be verified.");
        }
        catch (Exception ex) { return new OperationInvokeResult(false, false, $"Failed to restore communications ducking. {ex.Message}"); }
    }
}



