using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using GameOptimizer.Core.Services.SystemServices;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.Services;

public sealed class WindowsServiceAccessor : IWindowsServiceAccessor
{
    // Why: 白名单必须从能力定义单一事实源派生；手工维护的第二份清单曾与
    // ServiceCapabilityDefinitions 漂移（25 定义 vs 10 白名单），
    // 导致已注册 typed 能力在真机上被误阻断。
    private static readonly HashSet<string> AllowedServices = new(
        ServiceCapabilityDefinitions.AllServiceNames,
        StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(30);

    public Task<ServicePlatformAssessment> ProbeAsync(string serviceName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateServiceName(serviceName);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return Task.FromResult(new ServicePlatformAssessment(
                false, false, false, false, "os.unsupported", "需要 Windows 10 2004 或更高版本。"));
        }

        var exists = ServiceExists(serviceName);
        var administrator = IsAdministrator();
        return Task.FromResult(new ServicePlatformAssessment(
            exists,
            administrator,
            ManagedByPolicy: false,
            ProtectedService: false,
            exists
                ? administrator ? "applicable" : "permission.administrator-required"
                : "service.not-found",
            exists
                ? administrator ? "服务存在且当前进程已提升。" : "修改服务需要管理员权限。"
                : "当前系统不存在该服务。"));
    }

    public Task<ServiceStateSnapshot> ReadAsync(string serviceName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateServiceName(serviceName);
        if (!ServiceExists(serviceName))
        {
            return Task.FromResult(new ServiceStateSnapshot(
                serviceName, false, WindowsServiceRunState.Unknown, WindowsServiceStartType.Unknown));
        }

        using var controller = new ServiceController(serviceName);
        controller.Refresh();
        return Task.FromResult(new ServiceStateSnapshot(
            serviceName,
            true,
            MapStatus(controller.Status),
            ReadStartType(serviceName)));
    }

    public async Task<ServicePlatformMutationResult> ApplyAsync(
        string serviceName,
        ServiceChangeTarget target,
        CancellationToken cancellationToken)
    {
        ValidateServiceName(serviceName);
        var before = await ReadAsync(serviceName, cancellationToken).ConfigureAwait(false);
        if (!before.Exists)
            return new ServicePlatformMutationResult(false, false, "service.not-found", "服务不存在。");

        var mutationOccurred = false;
        try
        {
            if (before.StartType != target.StartType)
            {
                var configured = await ConfigureStartTypeAsync(serviceName, target.StartType, cancellationToken).ConfigureAwait(false);
                mutationOccurred |= configured.MutationOccurred;
                if (!configured.Success)
                    return configured with { MutationOccurred = mutationOccurred };
            }

            var stateResult = await SetRunStateAsync(serviceName, target.Action, cancellationToken).ConfigureAwait(false);
            return stateResult with { MutationOccurred = mutationOccurred || stateResult.MutationOccurred };
        }
        catch (OperationCanceledException) when (mutationOccurred)
        {
            await WaitUntilNotPendingAsync(serviceName, TransitionTimeout).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ServicePlatformMutationResult> RestoreAsync(
        ServiceStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ValidateServiceName(snapshot.ServiceName);
        if (!snapshot.Exists)
            return new ServicePlatformMutationResult(false, false, "snapshot.invalid", "恢复快照声明服务不存在。");

        var current = await ReadAsync(snapshot.ServiceName, cancellationToken).ConfigureAwait(false);
        var mutationOccurred = false;
        if (snapshot.RunState == WindowsServiceRunState.Running
            && snapshot.StartType == WindowsServiceStartType.Disabled)
        {
            // A service may legitimately be running while disabled. If it is currently
            // stopped+disabled, temporarily make it demand-startable, start it, then
            // reinstate Disabled. This is the only ordering that restores that exact pair.
            if (current.RunState != WindowsServiceRunState.Running
                && current.StartType == WindowsServiceStartType.Disabled)
            {
                var temporaryManual = await ConfigureStartTypeAsync(
                    snapshot.ServiceName, WindowsServiceStartType.Manual, cancellationToken).ConfigureAwait(false);
                mutationOccurred |= temporaryManual.MutationOccurred;
                if (!temporaryManual.Success)
                    return temporaryManual with { MutationOccurred = mutationOccurred };
            }

            var started = await SetRunStateAsync(
                snapshot.ServiceName, ServiceChangeAction.EnsureRunning, cancellationToken).ConfigureAwait(false);
            mutationOccurred |= started.MutationOccurred;
            if (!started.Success)
                return started with { MutationOccurred = mutationOccurred };

            var disabled = await ConfigureStartTypeAsync(
                snapshot.ServiceName, WindowsServiceStartType.Disabled, cancellationToken).ConfigureAwait(false);
            return disabled with { MutationOccurred = mutationOccurred || disabled.MutationOccurred };
        }

        if (snapshot.RunState == WindowsServiceRunState.Stopped
            && current.RunState != WindowsServiceRunState.Stopped)
        {
            var stopped = await SetRunStateAsync(
                snapshot.ServiceName, ServiceChangeAction.EnsureStopped, cancellationToken).ConfigureAwait(false);
            mutationOccurred |= stopped.MutationOccurred;
            if (!stopped.Success)
                return stopped with { MutationOccurred = mutationOccurred };
        }

        var latest = await ReadAsync(snapshot.ServiceName, cancellationToken).ConfigureAwait(false);
        if (latest.StartType != snapshot.StartType)
        {
            var configured = await ConfigureStartTypeAsync(snapshot.ServiceName, snapshot.StartType, cancellationToken)
                .ConfigureAwait(false);
            mutationOccurred |= configured.MutationOccurred;
            if (!configured.Success)
                return configured with { MutationOccurred = mutationOccurred };
        }

        var targetAction = snapshot.RunState == WindowsServiceRunState.Running
            ? ServiceChangeAction.EnsureRunning
            : ServiceChangeAction.EnsureStopped;
        var stateResult = await SetRunStateAsync(
            snapshot.ServiceName, targetAction, cancellationToken).ConfigureAwait(false);
        return stateResult with { MutationOccurred = mutationOccurred || stateResult.MutationOccurred };
    }

    private static async Task<ServicePlatformMutationResult> SetRunStateAsync(
        string serviceName,
        ServiceChangeAction action,
        CancellationToken cancellationToken)
    {
        using var controller = new ServiceController(serviceName);
        controller.Refresh();
        var desired = action == ServiceChangeAction.EnsureRunning
            ? ServiceControllerStatus.Running
            : ServiceControllerStatus.Stopped;
        if (controller.Status == desired)
            return new ServicePlatformMutationResult(true, false, "service.no-change", "服务已处于目标状态。");
        if (action == ServiceChangeAction.EnsureStopped && !controller.CanStop)
            return new ServicePlatformMutationResult(false, false, "service.cannot-stop", "服务不允许停止。");

        // Why: Start/Stop 的 Win32 失败（拒绝访问、服务被标记删除等）必须
        // 收敛为结果而非异常外泄——上层语义检查依赖结构化 ReasonCode。
        try
        {
            if (action == ServiceChangeAction.EnsureRunning)
                controller.Start();
            else
                controller.Stop();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ServicePlatformMutationResult(false, false, "service.state-change-failed", $"服务运行状态变更失败：{ex.Message}");
        }

        try
        {
            await WaitForStatusAsync(serviceName, desired, cancellationToken).ConfigureAwait(false);
            return new ServicePlatformMutationResult(true, true, "service.state-verified", "服务运行状态已回读确认。");
        }
        catch (OperationCanceledException)
        {
            await WaitUntilNotPendingAsync(serviceName, TransitionTimeout).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<ServicePlatformMutationResult> ConfigureStartTypeAsync(
        string serviceName,
        WindowsServiceStartType startType,
        CancellationToken cancellationToken)
    {
        var value = startType switch
        {
            WindowsServiceStartType.Automatic => "auto",
            WindowsServiceStartType.AutomaticDelayed => "delayed-auto",
            WindowsServiceStartType.Manual => "demand",
            WindowsServiceStartType.Disabled => "disabled",
            _ => throw new InvalidDataException("未知服务启动类型不能写入。")
        };
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("config");
        process.StartInfo.ArgumentList.Add(serviceName);
        process.StartInfo.ArgumentList.Add("start=");
        process.StartInfo.ArgumentList.Add(value);
        if (!process.Start())
            return new ServicePlatformMutationResult(false, false, "service.config-start-failed", "无法启动服务配置进程。");

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
            return new ServicePlatformMutationResult(
                false, true, "service.config-failed", $"服务启动类型配置失败，退出码 {process.ExitCode}：{error.Trim()}");
        }
        return new ServicePlatformMutationResult(true, true, "service.configured", "服务启动类型已配置。");
    }

    private static async Task WaitForStatusAsync(
        string serviceName,
        ServiceControllerStatus desired,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var controller = new ServiceController(serviceName);
            controller.Refresh();
            if (controller.Status == desired)
                return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitUntilNotPendingAsync(string serviceName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var controller = new ServiceController(serviceName);
            controller.Refresh();
            if (controller.Status is not (ServiceControllerStatus.StartPending or ServiceControllerStatus.StopPending))
                return;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    private static WindowsServiceStartType ReadStartType(string serviceName)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Services\{serviceName}",
            writable: false);
        var start = key?.GetValue("Start") as int?;
        var delayed = key?.GetValue("DelayedAutoStart") as int?;
        return start switch
        {
            2 when delayed == 1 => WindowsServiceStartType.AutomaticDelayed,
            2 => WindowsServiceStartType.Automatic,
            3 => WindowsServiceStartType.Manual,
            4 => WindowsServiceStartType.Disabled,
            _ => WindowsServiceStartType.Unknown
        };
    }

    private static WindowsServiceRunState MapStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Stopped => WindowsServiceRunState.Stopped,
        ServiceControllerStatus.Running => WindowsServiceRunState.Running,
        ServiceControllerStatus.StartPending => WindowsServiceRunState.StartPending,
        ServiceControllerStatus.StopPending => WindowsServiceRunState.StopPending,
        ServiceControllerStatus.Paused or ServiceControllerStatus.PausePending or ServiceControllerStatus.ContinuePending => WindowsServiceRunState.Paused,
        _ => WindowsServiceRunState.Unknown
    };

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;

    private static void ValidateServiceName(string serviceName)
    {
        if (!AllowedServices.Contains(serviceName))
            throw new InvalidOperationException("服务不在中央 Owner 固定白名单中，已阻断。");
    }
}
