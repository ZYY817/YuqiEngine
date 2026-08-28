using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Utilities;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class DisableMemoryCompressionOperation : IOptimizationOperation
{
    private sealed record MemoryCompressionSnapshot(bool WasEnabled);

    public string Id => "memory.disable_compression";
    public string Title => "禁用内存压缩";
    public string Description => "禁用 Windows 内存压缩以减少 CPU 开销，降低内存压缩带来的延迟";
    public string Category => "内存优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; } = "减少 CPU 压缩/解压开销";
    public string? Drawback { get; init; } = "可能增加内存占用，低内存机器上可能更频繁换页";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var isEnabled = await IsMemoryCompressionEnabledAsync(cancellationToken).ConfigureAwait(false);
        return new OperationStatus(!isEnabled, isEnabled ? "已启用" : "已禁用");
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var wasEnabled = await IsMemoryCompressionEnabledAsync(cancellationToken).ConfigureAwait(false);
            if (!wasEnabled)
            {
                return new OperationInvokeResult(true, false, "内存压缩已禁用");
            }

            var snapshot = new MemoryCompressionSnapshot(wasEnabled);
            await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

            var result = await RunPowerShellAsync("Disable-MMAgent -MemoryCompression", cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return await BuildApplyFailureResultAsync(context, snapshot, $"禁用失败：{result.StdErr}", cancellationToken).ConfigureAwait(false);
            }

            if (await IsMemoryCompressionEnabledAsync(cancellationToken).ConfigureAwait(false))
            {
                return await BuildApplyFailureResultAsync(context, snapshot, "禁用后仍检测到已启用", cancellationToken).ConfigureAwait(false);
            }

            return new OperationInvokeResult(true, true, "已禁用内存压缩");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "内存优化");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await context.LoadSnapshotAsync<MemoryCompressionSnapshot>(Id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return new OperationInvokeResult(false, false, "没有可恢复的快照");
            }

            var rollback = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return rollback.Success
                ? new OperationInvokeResult(true, true, "已恢复")
                : new OperationInvokeResult(false, false, rollback.Message);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "内存优化");
        }
    }

    private async Task<OperationInvokeResult> BuildApplyFailureResultAsync(
        OperationExecutionContext context,
        MemoryCompressionSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        return rollback.Success
            ? new OperationInvokeResult(false, false, failureMessage + " Original memory compression state remained intact or was restored.")
            : new OperationInvokeResult(false, false, failureMessage + $" Automatic rollback failed. {rollback.Message}");
    }

    private async Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        MemoryCompressionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var currentState = await IsMemoryCompressionEnabledAsync(cancellationToken).ConfigureAwait(false);
        if (currentState == snapshot.WasEnabled)
        {
            await context.TryDeleteSnapshotAsync(Id, cancellationToken).ConfigureAwait(false);
            return (true, "Original memory compression state remained intact.");
        }

        var rollback = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!rollback.Success)
        {
            return rollback;
        }

        await context.TryDeleteSnapshotAsync(Id, cancellationToken).ConfigureAwait(false);
        return (true, "Original memory compression state restored.");
    }

    private static async Task<(bool Success, string Message)> RestoreSnapshotInternalAsync(
        MemoryCompressionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var command = snapshot.WasEnabled ? "Enable-MMAgent -MemoryCompression" : "Disable-MMAgent -MemoryCompression";
        var result = await RunPowerShellAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return (false, string.IsNullOrWhiteSpace(result.StdErr) ? "PowerShell 无输出" : result.StdErr.Trim());
        }

        var enabled = await IsMemoryCompressionEnabledAsync(cancellationToken).ConfigureAwait(false);
        if (enabled != snapshot.WasEnabled)
        {
            return (false, "恢复后状态验证失败");
        }

        return (true, "已恢复");
    }

    private static async Task<bool> IsMemoryCompressionEnabledAsync(CancellationToken cancellationToken)
    {
        var result = await RunPowerShellAsync("(Get-MMAgent).MemoryCompression", cancellationToken).ConfigureAwait(false);
        return result.StdOut.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunPowerShellAsync(string command, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ProcessTimeouts.LongCommand);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return (process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, string.Empty, "PowerShell 命令执行超时");
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableSuperFetchOperation : IOptimizationOperation
{
    private sealed record SuperFetchSnapshot(int OriginalStartType);

    public string Id => "memory.disable_superfetch";
    public string Title => "禁用 SuperFetch";
    public string Description => "禁用 SysMain 服务的 SuperFetch 预取功能，减少后台磁盘活动，适合 SSD 用户";
    public string Category => "内存优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; } = "减少后台磁盘活动，降低 SSD 磨损";
    public string? Drawback { get; init; } = "可能降低 HDD 上的应用启动速度";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string ServiceKeyPath = RegistryPaths.SysMainService;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath);
            if (key is null)
            {
                return Task.FromResult(new OperationStatus(false, "无法读取 SysMain 服务键"));
            }

            var startType = MemoryConfigOperationShared.ReadDwordState(key, "Start").Value ?? 2;
            var isDisabled = startType == 4;
            return Task.FromResult(new OperationStatus(isDisabled, isDisabled ? "SysMain 已禁用" : "SysMain 运行中"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, $"读取失败：{ex.Message}"));
        }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, writable: true);
            if (key is null)
            {
                return new OperationInvokeResult(false, false, "无法打开 SysMain 服务键");
            }

            var originalStart = MemoryConfigOperationShared.ReadDwordState(key, "Start").Value ?? 2;
            if (originalStart == 4)
            {
                return new OperationInvokeResult(true, false, "SysMain 已禁用");
            }

            var snapshot = new SuperFetchSnapshot(originalStart);
            await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

            key.SetValue("Start", 4, RegistryValueKind.DWord);
            if (!MemoryConfigOperationShared.MatchesDesiredValue(key, "Start", 4))
            {
                return await MemoryConfigOperationShared.BuildRollbackFailureResultAsync(
                    context,
                    Id,
                    "禁用 SysMain 写入失败",
                    () => MemoryConfigOperationShared.MatchesDesiredValue(key, "Start", snapshot.OriginalStartType),
                    () => key.SetValue("Start", snapshot.OriginalStartType, RegistryValueKind.DWord),
                    cancellationToken).ConfigureAwait(false);
            }

            await StopServiceAsync("SysMain", cancellationToken).ConfigureAwait(false);
            return new OperationInvokeResult(true, true, "已禁用 SysMain 服务");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "内存优化");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await context.LoadSnapshotAsync<SuperFetchSnapshot>(Id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return new OperationInvokeResult(false, false, "没有 SysMain 的可恢复快照");
            }

            using var key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, writable: true);
            if (key is null)
            {
                return new OperationInvokeResult(false, false, "无法打开 SysMain 服务键进行恢复");
            }

            key.SetValue("Start", snapshot.OriginalStartType, RegistryValueKind.DWord);
            if (!MemoryConfigOperationShared.MatchesDesiredValue(key, "Start", snapshot.OriginalStartType))
            {
                return new OperationInvokeResult(false, false, "恢复 SysMain 写入失败");
            }

            return new OperationInvokeResult(true, true, "已恢复 SysMain 服务");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "内存优化");
        }
    }

    private static async Task StopServiceAsync(string serviceName, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = $"stop {serviceName}",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProcessTimeouts.StandardCommand);

        try
        {
            process.Start();
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableKernelPagingOperation : IOptimizationOperation
{
    public string Id => "memory.disable_kernel_paging";
    public string Title => "禁用内核分页";
    public string Description => "禁用内核分页，将内核代码常驻物理内存，减少磁盘分页";
    public string Category => "内存优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "减少内核分页延迟";
    public string? Drawback { get; init; } = "增加内存占用";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string ValueName = "DisablePagingExecutive";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        => MemoryConfigOperationShared.GetRegistryStatusAsync(KeyPath, ValueName, expectedValue: 1, enabledText: "已禁用", disabledText: "已启用");

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.ApplyRegistryOptimizationAsync(
            context,
            Id,
            KeyPath,
            ValueName,
            desiredValue: 1,
            alreadyMessage: "内核分页已禁用",
            successMessage: "已禁用内核分页",
            failureMessage: "禁用内核分页失败",
            cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.RestoreRegistryOptimizationAsync(
            context,
            Id,
            Title,
            KeyPath,
            ValueName,
            successMessage: "已恢复内核分页",
            failureMessage: "恢复内核分页失败",
            cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class EnableLargePagesOperation : IOptimizationOperation
{
    public string Id => "memory.enable_large_pages";
    public string Title => "启用大页面";
    public string Description => "启用 2MB 大页面，减少 TLB 未命中，提升内存访问性能";
    public string Category => "内存优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "减少 TLB 未命中，提升性能";
    public string? Drawback { get; init; } = "可能增加内存碎片，需要应用支持";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string ValueName = "LargePageMinimum";
    private const int DesiredValue = 2_097_152;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            if (key is null)
            {
                return Task.FromResult(new OperationStatus(false, "无法读取"));
            }

            var state = MemoryConfigOperationShared.ReadDwordState(key, ValueName);
            var isEnabled = state.Exists && state.Value.GetValueOrDefault() > 0;
            return Task.FromResult(new OperationStatus(isEnabled, isEnabled ? "已启用" : "未启用"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, $"读取失败：{ex.Message}"));
        }
    }

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.ApplyRegistryOptimizationAsync(
            context,
            Id,
            KeyPath,
            ValueName,
            DesiredValue,
            alreadyMessage: "大页面已启用",
            successMessage: "已启用大页面",
            failureMessage: "启用大页面失败",
            cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.RestoreRegistryOptimizationAsync(
            context,
            Id,
            Title,
            KeyPath,
            ValueName,
            successMessage: "已恢复大页面",
            failureMessage: "恢复大页面失败",
            cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class OptimizeSystemCacheOperation : IOptimizationOperation
{
    public string Id => "memory.optimize_system_cache";
    public string Title => "优化系统缓存";
    public string Description => "调整系统缓存策略，减少大文件缓存占用";
    public string Category => "内存优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "减少缓存占用";
    public string? Drawback { get; init; } = "可能降低文件缓存命中率";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string ValueName = "LargeSystemCache";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        => MemoryConfigOperationShared.GetRegistryStatusAsync(KeyPath, ValueName, expectedValue: 0, enabledText: "已优化", disabledText: "使用默认缓存策略");

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.ApplyRegistryOptimizationAsync(
            context,
            Id,
            KeyPath,
            ValueName,
            desiredValue: 0,
            alreadyMessage: "系统缓存已优化",
            successMessage: "已优化系统缓存",
            failureMessage: "优化系统缓存失败",
            cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.RestoreRegistryOptimizationAsync(
            context,
            Id,
            Title,
            KeyPath,
            ValueName,
            successMessage: "已恢复系统缓存",
            failureMessage: "恢复系统缓存失败",
            cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class OptimizePagefileOperation : IOptimizationOperation
{
    public string Id => "memory.optimize_pagefile";
    public string Title => "优化页面文件";
    public string Description => "关闭关机时清除页面文件，减少关机时间";
    public string Category => "内存优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; } = "减少关机时间";
    public string? Drawback { get; init; } = "关机时不再清除页面文件，可能残留敏感数据";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string ValueName = "ClearPageFileAtShutdown";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        => MemoryConfigOperationShared.GetRegistryStatusAsync(KeyPath, ValueName, expectedValue: 0, enabledText: "已优化", disabledText: "关机时清除页面文件");

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.ApplyRegistryOptimizationAsync(
            context,
            Id,
            KeyPath,
            ValueName,
            desiredValue: 0,
            alreadyMessage: "页面文件已优化",
            successMessage: "已优化页面文件",
            failureMessage: "优化页面文件失败",
            cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.RestoreRegistryOptimizationAsync(
            context,
            Id,
            Title,
            KeyPath,
            ValueName,
            successMessage: "已恢复页面文件",
            failureMessage: "恢复页面文件失败",
            cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class OptimizeMemoryPriorityOperation : IOptimizationOperation
{
    public string Id => "memory.optimize_priority";
    public string Title => "优化内存优先级";
    public string Description => "调整前台/后台进程优先级分配，让前台游戏获得更多 CPU 时间";
    public string Category => "内存优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "前台进程响应更快";
    public string? Drawback { get; init; } = "后台进程响应变慢";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string ValueName = "Win32PrioritySeparation";
    private const int DesiredValue = 38;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        => MemoryConfigOperationShared.GetRegistryStatusAsync(KeyPath, ValueName, expectedValue: DesiredValue, enabledText: "已优化", disabledText: "使用默认优先级", missingValueFallback: 2);

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.ApplyRegistryOptimizationAsync(
            context,
            Id,
            KeyPath,
            ValueName,
            DesiredValue,
            alreadyMessage: "内存优先级已优化",
            successMessage: "已优化内存优先级",
            failureMessage: "优化内存优先级失败",
            cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        => MemoryConfigOperationShared.RestoreRegistryOptimizationAsync(
            context,
            Id,
            Title,
            KeyPath,
            ValueName,
            successMessage: "已恢复内存优先级",
            failureMessage: "恢复内存优先级失败",
            cancellationToken);
}

internal readonly record struct MemoryRegistryDwordState(bool Exists, int? Value);

internal static class MemoryConfigOperationShared
{
    internal static Task<OperationStatus> GetRegistryStatusAsync(
        string keyPath,
        string valueName,
        int expectedValue,
        string enabledText,
        string disabledText,
        int? missingValueFallback = null)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key is null)
            {
                return Task.FromResult(new OperationStatus(false, "无法读取"));
            }

            var state = ReadDwordState(key, valueName);
            var current = state.Exists ? state.Value : missingValueFallback;
            var isExpected = current == expectedValue;
            return Task.FromResult(new OperationStatus(isExpected, isExpected ? enabledText : disabledText));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, $"读取失败：{ex.Message}"));
        }
    }

    internal static async Task<OperationInvokeResult> ApplyRegistryOptimizationAsync(
        OperationExecutionContext context,
        string operationId,
        string keyPath,
        string valueName,
        int desiredValue,
        string alreadyMessage,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key is null)
            {
                return new OperationInvokeResult(false, false, "无法读取");
            }

            var originalState = ReadDwordState(key, valueName);
            if (originalState.Exists && originalState.Value == desiredValue)
            {
                return new OperationInvokeResult(true, false, alreadyMessage);
            }

            await context.SaveSnapshotAsync(operationId, new SnapshotPayload(originalState), cancellationToken).ConfigureAwait(false);

            key.SetValue(valueName, desiredValue, RegistryValueKind.DWord);
            if (!MatchesDesiredValue(key, valueName, desiredValue))
            {
                return await BuildRollbackFailureResultAsync(
                    context,
                    operationId,
                    failureMessage,
                    () => MatchesState(key, valueName, originalState),
                    () => RestoreDwordState(key, valueName, originalState),
                    cancellationToken).ConfigureAwait(false);
            }

            return new OperationInvokeResult(true, true, successMessage);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "内存优化");
        }
    }

    internal static async Task<OperationInvokeResult> RestoreRegistryOptimizationAsync(
        OperationExecutionContext context,
        string operationId,
        string title,
        string keyPath,
        string valueName,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await context.LoadSnapshotAsync<SnapshotPayload>(operationId, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return MissingSnapshotResult(title);
            }

            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key is null)
            {
                return new OperationInvokeResult(false, false, "无法打开注册表键进行恢复");
            }

            RestoreDwordState(key, valueName, payload.OriginalState);
            if (!MatchesState(key, valueName, payload.OriginalState))
            {
                return new OperationInvokeResult(false, false, failureMessage);
            }

            return new OperationInvokeResult(true, true, successMessage);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "内存优化");
        }
    }

    internal static MemoryRegistryDwordState ReadDwordState(RegistryKey key, string valueName)
    {
        var raw = key.GetValue(valueName);
        if (raw is null)
        {
            return new MemoryRegistryDwordState(false, null);
        }

        return new MemoryRegistryDwordState(true, Convert.ToInt32(raw));
    }

    internal static bool MatchesDesiredValue(RegistryKey key, string valueName, int desiredValue)
    {
        var current = ReadDwordState(key, valueName);
        return current.Exists && current.Value == desiredValue;
    }

    internal static bool MatchesState(RegistryKey key, string valueName, MemoryRegistryDwordState expected)
    {
        var current = ReadDwordState(key, valueName);
        return current.Exists == expected.Exists && current.Value == expected.Value;
    }

    internal static void RestoreDwordState(RegistryKey key, string valueName, MemoryRegistryDwordState state)
    {
        if (!state.Exists)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(valueName, state.Value ?? 0, RegistryValueKind.DWord);
    }

    internal static async Task<OperationInvokeResult> BuildRollbackFailureResultAsync(
        OperationExecutionContext context,
        string operationId,
        string failureMessage,
        Func<bool> originalStateVerifier,
        Action rollbackAction,
        CancellationToken cancellationToken)
    {
        try
        {
            if (originalStateVerifier())
            {
                await context.TryDeleteSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
                return new OperationInvokeResult(false, false, failureMessage + " Original registry state remained intact; stale snapshot removed.");
            }

            rollbackAction();
            if (!originalStateVerifier())
            {
                return new OperationInvokeResult(false, false, failureMessage + " Automatic rollback verification failed.");
            }

            await context.TryDeleteSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
            return new OperationInvokeResult(false, false, failureMessage + " Original registry state restored.");
        }
        catch (Exception rollbackEx)
        {
            return new OperationInvokeResult(false, false, failureMessage + $" Automatic rollback failed. {rollbackEx.Message}");
        }
    }

    internal static OperationInvokeResult MissingSnapshotResult(string title)
        => new(false, false, $"{title} 没有可恢复的快照");

    private sealed record SnapshotPayload(MemoryRegistryDwordState OriginalState);
}
