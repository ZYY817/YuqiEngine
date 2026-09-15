// File: GameOptimizer.Core/Services/SafeServiceHelper.cs
using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 安全的 Windows 服务操作助手
/// 解决服务不存在、权限不足、超时等问题
/// </summary>
public static class SafeServiceHelper
{
    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    public static bool ServiceExists(string serviceName)
    {
        try
        {
            var services = ServiceController.GetServices();
            var found = false;
            // Why: GetServices() 返回的每个 ServiceController 都持有 native 句柄，
            // 必须全部 Dispose，否则每次调用泄漏 N 个句柄
            foreach (var service in services)
            {
                if (!found && string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                }
                service.Dispose();
            }
            return found;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeService] Failed to check if service exists: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 安全获取服务状态
    /// </summary>
    public static (ServiceControllerStatus? Status, string? Error) TryGetServiceStatus(string serviceName)
    {
        if (!ServiceExists(serviceName))
        {
            return (null, $"服务不存在: {serviceName}");
        }
        
        try
        {
            using var service = new ServiceController(serviceName);
            return (service.Status, null);
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[SafeService] Failed to get service status for {serviceName}: {ex.Message}");
            return (null, $"无法获取服务状态: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeService] Unexpected error getting service status for {serviceName}: {ex.Message}");
            return (null, $"获取服务状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 安全启动服务
    /// </summary>
    public static (bool Success, string? Error) TryStartService(string serviceName, int timeoutMs = 30000)
    {
        if (!ServiceExists(serviceName))
        {
            return (false, $"服务不存在: {serviceName}");
        }
        
        try
        {
            using var service = new ServiceController(serviceName);
            
            // 检查服务是否已经在运行
            if (service.Status == ServiceControllerStatus.Running)
            {
                return (true, null);
            }
            
            // 检查服务是否处于停止状态
            if (service.Status == ServiceControllerStatus.Stopped || 
                service.Status == ServiceControllerStatus.StopPending)
            {
                // 可以启动
            }
            else if (service.Status != ServiceControllerStatus.StartPending)
            {
                // 服务处于其他状态，可能无法启动
            }
            
            // 启动服务
            service.Start();
            
            // 等待服务启动
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);
            service.WaitForStatus(ServiceControllerStatus.Running, timeout);
            
            return (true, null);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Debug.WriteLine($"[SafeService] No permission to start service {serviceName}: {ex.Message}");
            return (false, ElevationGate.DescribeWriteDenial($"无权限启动服务: {serviceName}"));
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[SafeService] Cannot start service {serviceName}: {ex.Message}");
            return (false, $"无法启动服务: {ex.Message}");
        }
        catch (System.TimeoutException)
        {
            Debug.WriteLine($"[SafeService] Timeout starting service {serviceName}");
            return (false, $"启动服务超时（{timeoutMs}ms）: {serviceName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeService] Failed to start service {serviceName}: {ex.Message}");
            return (false, $"启动服务失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全停止服务
    /// </summary>
    public static (bool Success, string? Error) TryStopService(string serviceName, int timeoutMs = 30000)
    {
        if (!ServiceExists(serviceName))
        {
            return (false, $"服务不存在: {serviceName}");
        }
        
        try
        {
            using var service = new ServiceController(serviceName);
            
            // 检查服务是否已经停止
            if (service.Status == ServiceControllerStatus.Stopped)
            {
                return (true, null);
            }
            
            // 检查服务是否可以停止
            if (!service.CanStop)
            {
                return (false, $"服务不允许停止: {serviceName}");
            }
            
            // 停止服务
            service.Stop();
            
            // 等待服务停止
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);
            service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            
            return (true, null);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Debug.WriteLine($"[SafeService] No permission to stop service {serviceName}: {ex.Message}");
            return (false, ElevationGate.DescribeWriteDenial($"无权限停止服务: {serviceName}"));
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[SafeService] Cannot stop service {serviceName}: {ex.Message}");
            return (false, $"无法停止服务: {ex.Message}");
        }
        catch (System.TimeoutException)
        {
            Debug.WriteLine($"[SafeService] Timeout stopping service {serviceName}");
            return (false, $"停止服务超时（{timeoutMs}ms）: {serviceName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeService] Failed to stop service {serviceName}: {ex.Message}");
            return (false, $"停止服务失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 等待服务达到指定状态
    /// </summary>
    public static (bool Success, string? Error) WaitForServiceStatus(
        ServiceController service, 
        ServiceControllerStatus status, 
        int timeoutMs)
    {
        try
        {
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);
            service.WaitForStatus(status, timeout);
            return (true, null);
        }
        catch (System.TimeoutException)
        {
            Debug.WriteLine($"[SafeService] Timeout waiting for service {service.ServiceName} to reach status {status}");
            return (false, $"等待服务状态超时（{timeoutMs}ms）");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeService] Error waiting for service status: {ex.Message}");
            return (false, $"等待服务状态失败: {ex.Message}");
        }
    }

    // Why: 同步 WaitForStatus 会阻塞 ThreadPool 线程，当 OptimizationEngine 并行执行多个
    // ServiceToggleOperation 时可能导致 ThreadPool 饥饿。异步版本用轮询+Delay替代阻塞等待。
    private static async System.Threading.Tasks.Task<(bool Success, string? Error)> WaitForServiceStatusAsync(
        string serviceName,
        ServiceControllerStatus targetStatus,
        int timeoutMs,
        System.Threading.CancellationToken cancellationToken = default)
    {
        const int pollIntervalMs = 250;
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (status, error) = TryGetServiceStatus(serviceName);
            if (status == targetStatus) return (true, null);
            if (status == null) return (false, error);
            await System.Threading.Tasks.Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        Debug.WriteLine($"[SafeService] Async timeout waiting for service {serviceName} to reach {targetStatus}");
        return (false, $"等待服务状态超时（{timeoutMs}ms）");
    }

    /// <summary>
    /// 异步启动服务（不阻塞 ThreadPool 线程）
    /// </summary>
    public static async System.Threading.Tasks.Task<(bool Success, string? Error)> TryStartServiceAsync(
        string serviceName, int timeoutMs = 30000, System.Threading.CancellationToken cancellationToken = default)
    {
        if (!ServiceExists(serviceName))
            return (false, $"服务不存在: {serviceName}");

        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status == ServiceControllerStatus.Running)
                return (true, null);

            service.Start();
            return await WaitForServiceStatusAsync(serviceName, ServiceControllerStatus.Running, timeoutMs, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return (false, ElevationGate.DescribeWriteDenial($"无权限启动服务: {serviceName}"));
        }
        catch (InvalidOperationException ex)
        {
            return (false, $"无法启动服务: {ex.Message}");
        }
        catch (System.OperationCanceledException)
        {
            return (false, $"启动服务已取消: {serviceName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeService] Async start failed for {serviceName}: {ex.Message}");
            return (false, $"启动服务失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步停止服务（不阻塞 ThreadPool 线程）
    /// </summary>
    public static async System.Threading.Tasks.Task<(bool Success, string? Error)> TryStopServiceAsync(
        string serviceName, int timeoutMs = 30000, System.Threading.CancellationToken cancellationToken = default)
    {
        if (!ServiceExists(serviceName))
            return (false, $"服务不存在: {serviceName}");

        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status == ServiceControllerStatus.Stopped)
                return (true, null);
            if (!service.CanStop)
                return (false, $"服务不允许停止: {serviceName}");

            service.Stop();
            return await WaitForServiceStatusAsync(serviceName, ServiceControllerStatus.Stopped, timeoutMs, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return (false, ElevationGate.DescribeWriteDenial($"无权限停止服务: {serviceName}"));
        }
        catch (InvalidOperationException ex)
        {
            return (false, $"无法停止服务: {ex.Message}");
        }
        catch (System.OperationCanceledException)
        {
            return (false, $"停止服务已取消: {serviceName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeService] Async stop failed for {serviceName}: {ex.Message}");
            return (false, $"停止服务失败: {ex.Message}");
        }
    }
}
