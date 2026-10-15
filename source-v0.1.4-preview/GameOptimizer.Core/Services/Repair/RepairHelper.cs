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
    /// <summary>
    /// 运行命令并返回退出码与输出。Why: 只看 stdout 无法区分"命令拒绝/权限不足"与"成功"，
    /// 历史路径因此把失败误报为成功——变更类调用方必须用退出码判定。
    /// </summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunCommandResultAsync(
        string fileName, string arguments, int timeoutMs = 60000)
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
                var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);
                return (process.ExitCode, await outputTask, await errorTask);
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

    public static async Task<string> RunCommandAsync(string fileName, string arguments, int timeoutMs = 60000)
    {
        var (_, output, _) = await RunCommandResultAsync(fileName, arguments, timeoutMs).ConfigureAwait(false);
        return output;
    }

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    // Why: ServiceController 走 SCM API 而非解析 sc 文本——sc 输出随系统语言本地化，
    // "指定的服务"/"RUNNING" 在非中英系统上不匹配会误判存在性/状态。
    public static Task<bool> ServiceExistsAsync(string serviceName)
    {
        try
        {
            using var controller = new System.ServiceProcess.ServiceController(serviceName);
            _ = controller.Status;
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public static Task<bool> IsServiceRunningAsync(string serviceName)
    {
        try
        {
            using var controller = new System.ServiceProcess.ServiceController(serviceName);
            return Task.FromResult(controller.Status == System.ServiceProcess.ServiceControllerStatus.Running);
        }
        catch { return Task.FromResult(false); }
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
            // Why: ServiceController 直接报告 SCM 状态，避免解析 net stop/start 的本地化输出。
            using var controller = new System.ServiceProcess.ServiceController(serviceName);
            controller.Refresh();
            if (controller.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
            {
                controller.Stop();
                controller.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }

            controller.Refresh();
            controller.Start();
            controller.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            controller.Refresh();
            if (controller.Status != System.ServiceProcess.ServiceControllerStatus.Running)
            {
                return RepairResult.Failed($"{displayName} 重启后状态为 {controller.Status}，未运行。");
            }

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
            var (configCode, _, configErr) = await RunCommandResultAsync("sc", $"config {serviceName} start= auto", 10000);
            if (configCode != 0)
                return RepairResult.Failed($"启用 {displayName} 启动类型失败（sc config 退出码 {configCode}）：{configErr.Trim()}");

            using var controller = new System.ServiceProcess.ServiceController(serviceName);
            controller.Start();
            controller.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            controller.Refresh();
            if (controller.Status != System.ServiceProcess.ServiceControllerStatus.Running)
            {
                return RepairResult.Failed($"{displayName} 启动后状态为 {controller.Status}，未运行。");
            }

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

    // Why: nslookup 输出文本随系统语言本地化，英文 "Address"/"can't find"
    // 在非英文系统永不命中/误命中——直接做真实 DNS 查询，与语言无关。
    public static async Task<bool> CheckDnsResolutionAsync()
    {
        try
        {
            var addresses = await System.Net.Dns
                .GetHostAddressesAsync("www.microsoft.com")
                .WaitAsync(TimeSpan.FromSeconds(5));
            return addresses.Length > 0;
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

    // Why: 历史实现硬编码 false，Store 健康检查永远假阴性。
    // WinRT PackageManager.FindPackages 按包全名/前缀真实枚举，与语言无关。
    public static Task<bool> CheckUwpAppInstalledAsync(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName) || !OperatingSystem.IsWindows())
            return Task.FromResult(false);
        try
        {
            var packageManager = new Windows.Management.Deployment.PackageManager();
            var matches = packageManager.FindPackages()
                .Where(p => p.Id?.FullName?.Contains(appName, StringComparison.OrdinalIgnoreCase) == true
                    || p.Id?.Name?.Contains(appName, StringComparison.OrdinalIgnoreCase) == true);
            return Task.FromResult(matches.Any());
        }
        catch { return Task.FromResult(false); }
    }

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
