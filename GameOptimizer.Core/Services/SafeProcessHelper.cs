// File: GameOptimizer.Core/Services/SafeProcessHelper.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 安全的进程操作助手
/// 解决 P0-003: 进程操作未捕获异常
/// </summary>
public static class SafeProcessHelper
{
    public sealed class WindowProcessInfo
    {
        public int Id { get; init; }
        /// <summary>
        /// UI selection identity. A PID can be reused after a process exits, so callers
        /// must combine it with this creation time when preserving a selection.
        /// </summary>
        public long CreationTimeUtcTicks { get; init; }
        public string Name { get; init; } = string.Empty;
        public string WindowTitle { get; init; } = string.Empty;
    }

    public sealed class WindowProcessEnumerationResult
    {
        public bool Success { get; init; }
        public IReadOnlyList<WindowProcessInfo> Processes { get; init; } = Array.Empty<WindowProcessInfo>();
        public string? DiagnosticMessage { get; init; }
        public int ScannedProcessCount { get; init; }
        public int AccessDeniedCount { get; init; }
        public int RuntimeFailureCount { get; init; }
    }

    /// <summary>
    /// 安全获取进程（带异常处理）
    /// </summary>
    public static (Process? Process, string? Error) GetProcessById(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return (null, $"进程 (PID: {processId}) 已退出");
            }
            return (process, null);
        }
        catch (ArgumentException)
        {
            return (null, $"进程 (PID: {processId}) 不存在");
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[SafeProcess] Process {processId} operation failed: {ex.Message}");
            return (null, $"进程操作失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeProcess] Failed to get process {processId}: {ex.Message}");
            return (null, $"获取进程失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 安全枚举具有窗口标题的进程，并返回可用于 UI 展示的诊断信息。
    /// </summary>
    public static WindowProcessEnumerationResult EnumerateWindowProcesses(
        Func<string, bool>? excludeByName = null,
        int maxWindowTitleLength = 0)
    {
        Process[] allProcesses;
        try
        {
            allProcesses = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            return new WindowProcessEnumerationResult
            {
                Success = false,
                Processes = Array.Empty<WindowProcessInfo>(),
                DiagnosticMessage = BuildEnumerationFailureMessage(ex),
                ScannedProcessCount = 0,
                AccessDeniedCount = 0,
                RuntimeFailureCount = 1
            };
        }

        var result = new List<WindowProcessInfo>(capacity: allProcesses.Length);
        var accessDeniedCount = 0;
        var runtimeFailureCount = 0;

        foreach (var process in allProcesses)
        {
            try
            {
                var processName = process.ProcessName;
                if (excludeByName != null && excludeByName(processName))
                {
                    continue;
                }

                var windowHandle = process.MainWindowHandle;
                var windowTitle = process.MainWindowTitle;
                if (windowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(windowTitle))
                {
                    continue;
                }

                if (maxWindowTitleLength > 3 && windowTitle.Length > maxWindowTitleLength)
                {
                    windowTitle = windowTitle[..(maxWindowTitleLength - 3)] + "...";
                }

                result.Add(new WindowProcessInfo
                {
                    Id = process.Id,
                    CreationTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                    Name = processName,
                    WindowTitle = windowTitle
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                accessDeniedCount++;
            }
            catch (InvalidOperationException)
            {
                runtimeFailureCount++;
            }
            catch (Exception)
            {
                runtimeFailureCount++;
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }

        var ordered = result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var diagnosticMessage = BuildEnumerationDiagnosticMessage(
            scannedCount: allProcesses.Length,
            availableCount: ordered.Count,
            accessDeniedCount: accessDeniedCount,
            runtimeFailureCount: runtimeFailureCount);

        var success = ordered.Count > 0 || accessDeniedCount == 0;
        return new WindowProcessEnumerationResult
        {
            Success = success,
            Processes = ordered,
            DiagnosticMessage = diagnosticMessage,
            ScannedProcessCount = allProcesses.Length,
            AccessDeniedCount = accessDeniedCount,
            RuntimeFailureCount = runtimeFailureCount
        };
    }
    
    /// <summary>
    /// 安全设置进程亲和性
    /// </summary>
    public static (bool Success, string? Error) SetProcessorAffinity(int processId, IntPtr affinityMask)
    {
        var (process, error) = GetProcessById(processId);
        if (process == null)
        {
            return (false, error);
        }
        
        try
        {
            using (process)
            {
                if (process.HasExited)
                {
                    return (false, $"进程 (PID: {processId}) 已退出");
                }
                
                process.ProcessorAffinity = affinityMask;
                return (true, null);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Debug.WriteLine($"[SafeProcess] No permission to set affinity for {processId}: {ex.Message}");
            return (false, "无权限修改进程亲和性。请以管理员身份运行程序。");
        }
        catch (InvalidOperationException)
        {
            return (false, $"进程 (PID: {processId}) 已退出");
        }
        catch (NotSupportedException ex)
        {
            Debug.WriteLine($"[SafeProcess] Affinity not supported for {processId}: {ex.Message}");
            return (false, "当前系统不支持设置进程亲和性");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeProcess] Failed to set affinity for {processId}: {ex.Message}");
            return (false, $"设置进程亲和性失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全设置进程优先级
    /// </summary>
    public static (bool Success, string? Error) SetPriorityClass(int processId, ProcessPriorityClass priorityClass)
    {
        var (process, error) = GetProcessById(processId);
        if (process == null)
        {
            return (false, error);
        }
        
        try
        {
            using (process)
            {
                if (process.HasExited)
                {
                    return (false, $"进程 (PID: {processId}) 已退出");
                }
                
                process.PriorityClass = priorityClass;
                return (true, null);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Debug.WriteLine($"[SafeProcess] No permission to set priority for {processId}: {ex.Message}");
            return (false, "无权限修改进程优先级。请以管理员身份运行程序。");
        }
        catch (InvalidOperationException)
        {
            return (false, $"进程 (PID: {processId}) 已退出");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeProcess] Failed to set priority for {processId}: {ex.Message}");
            return (false, $"设置进程优先级失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全获取进程亲和性
    /// </summary>
    public static (IntPtr Affinity, string? Error) GetProcessorAffinity(int processId)
    {
        var (process, error) = GetProcessById(processId);
        if (process == null)
        {
            return (IntPtr.Zero, error);
        }
        
        try
        {
            using (process)
            {
                if (process.HasExited)
                {
                    return (IntPtr.Zero, $"进程 (PID: {processId}) 已退出");
                }
                
                return (process.ProcessorAffinity, null);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Debug.WriteLine($"[SafeProcess] No permission to read affinity for {processId}: {ex.Message}");
            return (IntPtr.Zero, "无权限读取进程亲和性");
        }
        catch (InvalidOperationException)
        {
            return (IntPtr.Zero, $"进程 (PID: {processId}) 已退出");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeProcess] Failed to get affinity for {processId}: {ex.Message}");
            return (IntPtr.Zero, $"获取进程亲和性失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全启动进程
    /// </summary>
    public static (Process? Process, string? Error) StartProcess(ProcessStartInfo startInfo)
    {
        try
        {
            var process = Process.Start(startInfo);
            if (process == null)
            {
                return (null, "进程启动失败（返回 null）");
            }
            return (process, null);
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine($"[SafeProcess] Failed to start process: {ex.Message}");
            return (null, $"启动进程失败: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[SafeProcess] Invalid operation when starting process: {ex.Message}");
            return (null, $"进程启动配置无效: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeProcess] Unexpected error starting process: {ex.Message}");
            return (null, $"启动进程时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全启动进程并等待退出
    /// </summary>
    public static async Task<(int ExitCode, string Output, string Error, string? ErrorMessage)> RunProcessAsync(
        string fileName, 
        string arguments, 
        CancellationToken ct = default,
        int timeoutMs = 30000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Why: .NET 8 的 Encoding.Default 是 UTF-8，但 powercfg/netsh 等系统命令输出 OEM 编码
            StandardOutputEncoding = GetOemEncoding(),
            StandardErrorEncoding = GetOemEncoding()
        };
        
        var (process, error) = StartProcess(startInfo);
        if (process == null)
        {
            return (-1, string.Empty, string.Empty, error);
        }
        
        try
        {
            using (process)
            {
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                var timeoutTask = Task.Delay(timeoutMs, ct);
                var processTask = process.WaitForExitAsync(ct);
                
                var completedTask = await Task.WhenAny(processTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    return (-1, string.Empty, string.Empty, $"进程执行超时（{timeoutMs}ms）");
                }
                
                var output = await outputTask;
                var errorOutput = await errorTask;
                
                return (process.ExitCode, output, errorOutput, null);
            }
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            return (-1, string.Empty, string.Empty, "操作已取消");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeProcess] Error running process: {ex.Message}");
            return (-1, string.Empty, string.Empty, $"执行进程时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 检查进程是否存在且未退出
    /// </summary>
    public static bool IsProcessAlive(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 安全终止进程
    /// </summary>
    public static (bool Success, string? Error) KillProcess(int processId)
    {
        var (process, error) = GetProcessById(processId);
        if (process == null)
        {
            return (false, error);
        }
        
        try
        {
            using (process)
            {
                if (process.HasExited)
                {
                    return (true, null);
                }
                
                process.Kill();
                process.WaitForExit(5000);
                return (true, null);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Debug.WriteLine($"[SafeProcess] No permission to kill {processId}: {ex.Message}");
            return (false, "无权限终止进程。请以管理员身份运行程序。");
        }
        catch (InvalidOperationException)
        {
            return (true, null); // 进程已退出
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeProcess] Failed to kill {processId}: {ex.Message}");
            return (false, $"终止进程失败: {ex.Message}");
        }
    }

    // Why: .NET 8 中 Encoding.Default 是 UTF-8，但 powercfg/netsh 等系统命令输出 OEM 编码
    // 必须动态获取系统 OEM 代码页（中文=936, 英文=437, 日文=932）
    private static Encoding GetOemEncoding()
    {
        try
        {
            var oemCp = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oemCp);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static string BuildEnumerationFailureMessage(Exception ex)
    {
        return ex switch
        {
            Win32Exception win32Ex when win32Ex.NativeErrorCode == 5
                => "无法读取进程列表：权限不足（请以管理员运行，并检查安全软件拦截）。",
            _ => $"无法读取进程列表：{ex.Message}"
        };
    }

    private static string? BuildEnumerationDiagnosticMessage(
        int scannedCount,
        int availableCount,
        int accessDeniedCount,
        int runtimeFailureCount)
    {
        if (accessDeniedCount == 0 && runtimeFailureCount == 0)
        {
            return null;
        }

        return $"进程扫描: 总计{scannedCount}，可用{availableCount}，权限拦截{accessDeniedCount}，读取失败{runtimeFailureCount}。建议管理员运行并检查安全软件拦截。";
    }
}
