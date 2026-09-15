// File: GameOptimizer.Core/Services/PowerServiceDiagnostics.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services;

public record PowerServiceStatus(
    bool IsAdministrator,
    bool IsPowerServiceRunning,
    bool HasActivePowerPlan,
    List<string> AvailablePlans,
    string? ErrorMessage,
    string? DetailedDiagnostic
);

public class PowerServiceDiagnostics
{
    private const string PowerServiceName = "Power";
    
    public static async Task<PowerServiceStatus> DiagnoseAsync(CancellationToken ct = default)
    {
        var isAdmin = IsAdministrator();
        var isPowerRunning = await IsPowerServiceRunningAsync(ct);
        var (hasActivePlan, plans, error, diagnostic) = await CheckPowerPlansAsync(ct);
        
        return new PowerServiceStatus(
            isAdmin,
            isPowerRunning,
            hasActivePlan,
            plans,
            error,
            diagnostic
        );
    }
    
    public static async Task<(bool Success, string Message)> TryAutoRepairAsync(CancellationToken ct = default)
    {
        var status = await DiagnoseAsync(ct);
        
        if (!status.IsAdministrator)
        {
            return (false, "需要管理员权限才能修复电源服务问题");
        }
        
        if (!status.IsPowerServiceRunning)
        {
            var serviceStarted = await TryStartPowerServiceAsync(ct);
            if (!serviceStarted)
            {
                return (false, "无法启动 Power 服务，请手动检查服务状态");
            }
            
            await Task.Delay(2000, ct);
        }
        
        if (!status.HasActivePowerPlan)
        {
            var planActivated = await TryActivateDefaultPlanAsync(ct);
            if (!planActivated)
            {
                return (false, "无法激活默认电源计划，系统电源配置可能已损坏");
            }
        }
        
        var finalStatus = await DiagnoseAsync(ct);
        if (finalStatus.HasActivePowerPlan && finalStatus.IsPowerServiceRunning)
        {
            return (true, "电源服务已成功修复");
        }
        
        return (false, $"修复未完成: {finalStatus.ErrorMessage}");
    }
    
    public static bool IsAdministrator() => WindowsElevation.IsElevated;
    
    private static async Task<bool> IsPowerServiceRunningAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var (status, _) = SafeServiceHelper.TryGetServiceStatus(PowerServiceName);
            return status == ServiceControllerStatus.Running;
        }, ct);
    }
    
    private static async Task<bool> TryStartPowerServiceAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            // 检查服务是否存在
            if (!SafeServiceHelper.ServiceExists(PowerServiceName))
            {
                Debug.WriteLine($"[PowerServiceDiagnostics] {PowerServiceName} 服务不存在");
                return false;
            }
            
            // 获取服务状态
            var (status, error) = SafeServiceHelper.TryGetServiceStatus(PowerServiceName);
            if (status == null)
            {
                Debug.WriteLine($"[PowerServiceDiagnostics] 无法获取服务状态: {error}");
                return false;
            }
            
            if (status == ServiceControllerStatus.Running)
                return true;
            
            // P0-3: 带重试限制的服务启动（防止UI冻结）
            return StartServiceWithRetry(maxAttempts: 3, totalTimeoutMs: 30000);
        }, ct);
    }

    /// <summary>
    /// 带重试和超时保护的服务启动
    /// P0-3: 防止服务启动死循环导致UI冻结
    /// </summary>
    private static bool StartServiceWithRetry(int maxAttempts, int totalTimeoutMs)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // 检查总超时
            if (stopwatch.ElapsedMilliseconds >= totalTimeoutMs)
            {
                Debug.WriteLine($"[PowerServiceDiagnostics] Total timeout ({totalTimeoutMs}ms) exceeded");
                return false;
            }

            Debug.WriteLine($"[PowerServiceDiagnostics] Attempt {attempt}/{maxAttempts} to start Power service");
            
            // 尝试启动服务（单次超时10秒）
            var remainingTime = totalTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
            var attemptTimeout = Math.Min(10000, remainingTime);
            
            var (success, startError) = SafeServiceHelper.TryStartService(
                PowerServiceName, 
                timeoutMs: attemptTimeout);
            
            if (success)
            {
                Debug.WriteLine($"[PowerServiceDiagnostics] Power service started successfully on attempt {attempt}");
                return true;
            }
            
            Debug.WriteLine($"[PowerServiceDiagnostics] Attempt {attempt} failed: {startError}");
            
            // 最后一次尝试失败，不再等待
            if (attempt >= maxAttempts)
                break;
            
            // 指数退避：1s, 2s（第3次失败后不等待）
            var waitMs = attempt * 1000;
            if (stopwatch.ElapsedMilliseconds + waitMs >= totalTimeoutMs)
                break;
            
            Debug.WriteLine($"[PowerServiceDiagnostics] Waiting {waitMs}ms before retry...");
            System.Threading.Thread.Sleep(waitMs);
        }
        
        Debug.WriteLine($"[PowerServiceDiagnostics] Failed to start Power service after {maxAttempts} attempts");
        return false;
    }
    
    private static async Task<(bool HasActive, List<string> Plans, string? Error, string? Diagnostic)> CheckPowerPlansAsync(CancellationToken ct)
    {
        var plans = new List<string>();
        var diagnostic = new System.Text.StringBuilder();
        
        try
        {
            diagnostic.AppendLine("=== 电源计划诊断 ===");
            
            var activeResult = await RunPowerCfgAsync("/GETACTIVESCHEME", ct);
            diagnostic.AppendLine($"获取激活计划: ExitCode={activeResult.ExitCode}");
            
            if (activeResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(activeResult.Output))
            {
                diagnostic.AppendLine($"激活计划输出: {activeResult.Output.Substring(0, Math.Min(100, activeResult.Output.Length))}");
                var activeMatch = Regex.Match(activeResult.Output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                if (activeMatch.Success)
                {
                    diagnostic.AppendLine($"✓ 找到激活计划: {activeMatch.Value}");
                    return (true, new List<string> { activeMatch.Value }, null, diagnostic.ToString());
                }
            }
            else
            {
                diagnostic.AppendLine($"✗ 获取激活计划失败: {activeResult.Error}");
            }
            
            var listResult = await RunPowerCfgAsync("/LIST", ct);
            diagnostic.AppendLine($"列出所有计划: ExitCode={listResult.ExitCode}");
            
            if (listResult.ExitCode == 0)
            {
                var matches = Regex.Matches(listResult.Output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\(([^)]+)\)");
                diagnostic.AppendLine($"找到 {matches.Count} 个电源计划");
                
                foreach (Match match in matches)
                {
                    var guid = match.Groups[1].Value;
                    var name = match.Groups[2].Value;
                    plans.Add($"{name} ({guid})");
                    diagnostic.AppendLine($"  - {name}: {guid}");
                }
                
                if (plans.Count > 0)
                {
                    return (false, plans, "找到电源计划但无激活计划", diagnostic.ToString());
                }
            }
            else
            {
                diagnostic.AppendLine($"✗ 列出计划失败: {listResult.Error}");
            }
            
            return (false, plans, "系统中没有可用的电源计划", diagnostic.ToString());
        }
        catch (Exception ex)
        {
            diagnostic.AppendLine($"✗ 异常: {ex.Message}");
            return (false, plans, $"诊断过程出错: {ex.Message}", diagnostic.ToString());
        }
    }
    
    private static async Task<bool> TryActivateDefaultPlanAsync(CancellationToken ct)
    {
        try
        {
            var listResult = await RunPowerCfgAsync("/LIST", ct);
            if (listResult.ExitCode != 0)
                return false;
            
            // Why: powercfg /LIST 输出在中文系统为"电源方案 GUID:"，英文为"Power Scheme GUID:"，直接匹配 GUID 格式兼容所有语言
            var matches = Regex.Matches(listResult.Output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
            if (matches.Count == 0)
                return false;
            
            var firstPlanGuid = matches[0].Groups[1].Value;
            var setResult = await RunPowerCfgAsync($"/SETACTIVE {firstPlanGuid}", ct);
            
            if (setResult.ExitCode == 0)
            {
                Debug.WriteLine($"[PowerServiceDiagnostics] 成功激活电源计划: {firstPlanGuid}");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerServiceDiagnostics] 激活默认计划失败: {ex.Message}");
            return false;
        }
    }
    
    private static async Task<(int ExitCode, string Output, string Error)> RunPowerCfgAsync(string arguments, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = GetOemEncoding()
                }
            };
            
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            // Why: 即使调用方传入 CancellationToken.None，也需要超时保护防止永久挂起
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Constants.ProcessTimeouts.LongCommand);
            
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (-1, string.Empty, "powercfg 执行超时");
            }
            
            var output = await outputTask;
            var error = await errorTask;
            
            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static Encoding GetOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var oemCp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oemCp);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
