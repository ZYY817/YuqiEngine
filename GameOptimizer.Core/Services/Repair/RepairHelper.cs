using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.Repair;

public static class RepairHelper
{
    private static LocalizationService Loc => LocalizationService.Instance;
    public static async Task<string> RunCommandAsync(string fileName, string arguments, int timeoutMs = 60000)
    {
        // Why: sc/net/ipconfig 等系统命令输出使用 OEM 编码，.NET 8 默认 UTF-8 会导致中文乱码
        var oemEncoding = GetOemEncoding();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = oemEncoding,
                StandardErrorEncoding = oemEncoding
            }
        };

        try
        {
            process.Start();
            
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);
                return output;
            }
            catch (OperationCanceledException)
            {
                // 超时，强制终止进程
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { /* 忽略终止失败 */ }
                
                throw new TimeoutException($"命令执行超时: {fileName} {arguments}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 命令不存在或无权限执行
            throw new InvalidOperationException($"无法执行命令 {fileName}: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// 运行PowerShell命令，带降级方案
    /// </summary>
    public static async Task<(bool Success, string Output, string Error)> RunPowerShellWithFallbackAsync(
        string command, int timeoutMs = 10000)
    {
        // 方案1: 尝试使用 -ExecutionPolicy Bypass
        try
        {
            var output = await RunCommandAsync("powershell.exe", 
                $"-ExecutionPolicy Bypass -NoProfile -Command \"{command}\"", timeoutMs);
            return (true, output, string.Empty);
        }
        catch (Exception ex1)
        {
            Debug.WriteLine($"[RepairHelper] PowerShell with Bypass failed: {ex1.Message}");
            
            // 方案2: 尝试不带 ExecutionPolicy 参数
            try
            {
                var output = await RunCommandAsync("powershell.exe", 
                    $"-NoProfile -Command \"{command}\"", timeoutMs);
                return (true, output, string.Empty);
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"[RepairHelper] PowerShell without Bypass failed: {ex2.Message}");
                return (false, string.Empty, 
                    Loc.Get("Repair.PowerShellRestricted", ex2.Message));
            }
        }
    }
    
    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    public static async Task<bool> ServiceExistsAsync(string serviceName)
    {
        try
        {
            var result = await RunCommandAsync("sc", $"query {serviceName}", 5000);
            // 如果服务不存在，sc会返回错误信息包含"指定的服务"或"does not exist"
            return !result.Contains("指定的服务") && 
                   !result.Contains("does not exist") &&
                   !result.Contains("FAILED");
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> IsServiceRunningAsync(string serviceName)
    {
        try
        {
            if (!await ServiceExistsAsync(serviceName))
                return false;
                
            var result = await RunCommandAsync("sc", $"query {serviceName}", 5000);
            return result.Contains("RUNNING");
        }
        catch { return false; }
    }

    public static async Task<RepairResult> RestartServiceAsync(string serviceName, string displayName, IProgress<RepairProgress>? progress)
    {
        // 先检查服务是否存在
        if (!await ServiceExistsAsync(serviceName))
        {
            return RepairResult.Succeeded(Loc.Get("Repair.ServiceNotFound", displayName));
        }
        
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.RestartingService", displayName), PercentComplete = 10 });
        try
        {
            await RunCommandAsync("net", $"stop {serviceName}", 10000);
            await Task.Delay(500);
            await RunCommandAsync("net", $"start {serviceName}", 10000);
            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return RepairResult.Succeeded(Loc.Get("Repair.ServiceRestarted", displayName));
        }
        catch (TimeoutException)
        {
            return RepairResult.Failed(Loc.Get("Repair.ServiceTimeout", displayName));
        }
        catch (Exception ex)
        {
            return RepairResult.Failed(Loc.Get("Repair.RestartServiceFailed", displayName), ex);
        }
    }

    public static async Task<RepairResult> EnableServiceAsync(string serviceName, string displayName, IProgress<RepairProgress>? progress)
    {
        // 先检查服务是否存在
        if (!await ServiceExistsAsync(serviceName))
        {
            return RepairResult.Succeeded(Loc.Get("Repair.ServiceNotFound", displayName));
        }
        
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.EnablingService", displayName), PercentComplete = 10 });
        try
        {
            await RunCommandAsync("sc", $"config {serviceName} start= auto", 10000);
            await RunCommandAsync("net", $"start {serviceName}", 10000);
            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return RepairResult.Succeeded(Loc.Get("Repair.ServiceEnabled", displayName));
        }
        catch (TimeoutException)
        {
            return RepairResult.Failed(Loc.Get("Repair.ServiceTimeout", displayName));
        }
        catch (Exception ex)
        {
            return RepairResult.Failed(Loc.Get("Repair.EnableServiceFailed", displayName), ex);
        }
    }

    public static Task<bool> CheckNetworkConnectivityAsync()
    {
        try
        {
            return Task.FromResult(NetworkInterface.GetIsNetworkAvailable());
        }
        catch { return Task.FromResult(false); }
    }

    public static async Task<bool> CheckDnsResolutionAsync()
    {
        try
        {
            var result = await RunCommandAsync("nslookup", "www.microsoft.com", 5000);
            return result.Contains("Address") && !result.Contains("can't find");
        }
        catch { return false; }
    }

    public static Task<bool> CheckRegistryKeyExistsAsync(string keyPath, string? valueName = null)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key == null) return Task.FromResult(false);
            if (valueName == null) return Task.FromResult(true);
            return Task.FromResult(key.GetValue(valueName) != null);
        }
        catch { return Task.FromResult(false); }
    }

    public static Task<bool> CheckFileExistsAsync(string filePath)
    {
        return Task.FromResult(File.Exists(filePath));
    }

    public static Task<bool> CheckUwpAppInstalledAsync(string appName) =>
        Task.FromResult(false);

    public static Task<bool> CheckFileAssociationAsync(string extension, string expectedProgId)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(extension);
            var progId = key?.GetValue(null) as string;
            return Task.FromResult(progId?.Contains(expectedProgId, StringComparison.OrdinalIgnoreCase) == true);
        }
        catch { return Task.FromResult(false); }
    }

    public static Task<bool> CheckVcRedistInstalledAsync()
    {
        try
        {
            var paths = new[]
            {
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
                @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86"
            };
            foreach (var path in paths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("Installed") is int installed && installed == 1)
                    return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch { return Task.FromResult(false); }
    }

    public static Task<bool> CheckDirectXHealthAsync()
    {
        try
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var requiredDlls = new[] { "d3d11.dll", "d3d12.dll", "dxgi.dll" };
            foreach (var dll in requiredDlls)
            {
                if (!File.Exists(Path.Combine(systemDir, dll)))
                    return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
        catch { return Task.FromResult(false); }
    }

    private static Encoding GetOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch { return Encoding.UTF8; }
    }

}
