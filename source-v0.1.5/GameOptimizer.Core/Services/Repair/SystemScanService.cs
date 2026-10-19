using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.Repair;

public interface ISystemScanService
{
    Task<SystemScanResult> ScanAllAsync(IProgress<ScanProgressInfo>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<ScanIssue>> ScanNetworkAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScanIssue>> ScanAudioAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScanIssue>> ScanBluetoothAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScanIssue>> ScanDisplayAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScanIssue>> ScanPeripheralAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScanIssue>> ScanSystemAsync(CancellationToken ct = default);
}

[SupportedOSPlatform("windows")]
public class SystemScanService : ISystemScanService
{
    public async Task<SystemScanResult> ScanAllAsync(IProgress<ScanProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var loc = LocalizationService.Instance;
        var sw = Stopwatch.StartNew();
        var allIssues = new List<ScanIssue>();
        var tasks = new (string Name, Func<CancellationToken, Task<IReadOnlyList<ScanIssue>>> Scan)[]
        {
            (loc.Get("Scan.Network"), ScanNetworkAsync),
            (loc.Get("Scan.Audio"), ScanAudioAsync),
            (loc.Get("Scan.Bluetooth"), ScanBluetoothAsync),
            (loc.Get("Scan.Display"), ScanDisplayAsync),
            (loc.Get("Scan.Peripheral"), ScanPeripheralAsync),
            (loc.Get("Scan.System"), ScanSystemAsync)
        };

        for (int i = 0; i < tasks.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgressInfo
            {
                CurrentTask = tasks[i].Name,
                CompletedTasks = i,
                TotalTasks = tasks.Length,
                IssuesFound = allIssues.Count
            });

            try
            {
                var issues = await tasks[i].Scan(ct);
                allIssues.AddRange(issues);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemScanService] {tasks[i].Name} scan error: {ex.Message}");
            }
        }

        sw.Stop();
        progress?.Report(new ScanProgressInfo
        {
            CurrentTask = loc.Get("Scan.Complete"),
            CompletedTasks = tasks.Length,
            TotalTasks = tasks.Length,
            IssuesFound = allIssues.Count
        });

        return new SystemScanResult
        {
            Issues = allIssues,
            TotalScanned = tasks.Length,
            Duration = sw.Elapsed
        };
    }

    public async Task<IReadOnlyList<ScanIssue>> ScanNetworkAsync(CancellationToken ct = default)
    {
        var loc = LocalizationService.Instance;
        var issues = new List<ScanIssue>();

        // P0-5: 多目标Ping检测，避免单一目标导致误报
        var pingTargets = new[] { "8.8.8.8", "114.114.114.114", "1.1.1.1" };
        var pingSuccess = false;
        string lastError = string.Empty;

        foreach (var target in pingTargets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    pingSuccess = true;
                    break;
                }
                lastError = reply.Status.ToString();
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        // ICMP 可被路由器或防火墙屏蔽，不能单独作为“无法联网”的证据。
        // 只有 ICMP 与 DNS 两种独立探测都失败时，才报告网络连接异常。
        var dnsResolved = false;
        try
        {
            // Why: nslookup 失败文本随系统语言本地化（中文报"超时"/"找不到"），
            // 英文 token 匹配在本地化系统上恒为假阴性——直接做真实 DNS 解析。
            var addresses = await System.Net.Dns
                .GetHostAddressesAsync("www.baidu.com")
                .WaitAsync(TimeSpan.FromSeconds(5));
            dnsResolved = addresses.Length > 0;
            if (!dnsResolved)
            {
                issues.Add(new ScanIssue
                {
                    Id = "scan.network.dns_failed",
                    Title = loc.Get("Scan.DnsFailed.Title"),
                    Description = loc.Get("Scan.DnsFailed.Desc"),
                    Category = ScanCategory.Network,
                    Severity = IssueSeverity.Warning,
                    SuggestedRepairId = "repair.dns"
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemScanService] DNS lookup error: {ex.Message}");
        }

        if (!pingSuccess && !dnsResolved)
        {
            issues.Add(new ScanIssue
            {
                Id = "scan.network.no_internet",
                Title = loc.Get("Scan.NoInternet.Title"),
                Description = loc.Get("Scan.NoInternet.Desc", lastError),
                Category = ScanCategory.Network,
                Severity = IssueSeverity.Error,
                SuggestedRepairId = "repair.tcp_ip"
            });
        }

        return issues;
    }

    public async Task<IReadOnlyList<ScanIssue>> ScanAudioAsync(CancellationToken ct = default)
    {
        var loc = LocalizationService.Instance;
        var issues = new List<ScanIssue>();

        // 检测音频服务
        if (!await RepairHelper.IsServiceRunningAsync("Audiosrv"))
        {
            issues.Add(new ScanIssue
            {
                Id = "scan.audio.service_stopped",
                Title = loc.Get("Scan.AudioService.Title"),
                Description = loc.Get("Scan.AudioService.Desc"),
                Category = ScanCategory.Audio,
                Severity = IssueSeverity.Error,
                SuggestedRepairId = "repair.audio_service"
            });
        }

        // 检测音频端点服务
        if (!await RepairHelper.IsServiceRunningAsync("AudioEndpointBuilder"))
        {
            issues.Add(new ScanIssue
            {
                Id = "scan.audio.endpoint_stopped",
                Title = loc.Get("Scan.AudioEndpoint.Title"),
                Description = loc.Get("Scan.AudioEndpoint.Desc"),
                Category = ScanCategory.Audio,
                Severity = IssueSeverity.Error,
                SuggestedRepairId = "repair.audio_endpoint"
            });
        }

        return issues;
    }

    public async Task<IReadOnlyList<ScanIssue>> ScanBluetoothAsync(CancellationToken ct = default)
    {
        var loc = LocalizationService.Instance;
        var issues = new List<ScanIssue>();

        // 检测是否有蓝牙适配器
        try
        {
            // P0-6: 增加PowerShell超时到10秒
            var result = await RepairHelper.RunCommandAsync("powershell.exe",
                "-ExecutionPolicy Bypass -Command \"Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Select-Object -First 1\"", 10000);
            
            if (string.IsNullOrWhiteSpace(result) || !result.Contains("Bluetooth"))
            {
                // 没有蓝牙适配器，不报告问题（可能是台式机）
                return issues;
            }

            // bthserv 可以按需启动；仅凭当前未运行不能断言蓝牙异常。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemScanService] Bluetooth detection error: {ex.Message}");
        }

        return issues;
    }

    public Task<IReadOnlyList<ScanIssue>> ScanDisplayAsync(CancellationToken ct = default)
    {
        var loc = LocalizationService.Instance;
        var issues = new List<ScanIssue>();

        // 检测DWM进程
        var dwmProcesses = Process.GetProcessesByName("dwm");
        if (dwmProcesses.Length == 0)
        {
            issues.Add(new ScanIssue
            {
                Id = "scan.display.dwm_not_running",
                Title = loc.Get("Scan.DwmNotRunning.Title"),
                Description = loc.Get("Scan.DwmNotRunning.Desc"),
                Category = ScanCategory.Display,
                Severity = IssueSeverity.Critical,
                SuggestedRepairId = "repair.dwm_restart"
            });
        }
        foreach (var p in dwmProcesses) p.Dispose();

        // 检测Explorer进程
        var explorerProcesses = Process.GetProcessesByName("explorer");
        if (explorerProcesses.Length == 0)
        {
            issues.Add(new ScanIssue
            {
                Id = "scan.display.explorer_not_running",
                Title = loc.Get("Scan.ExplorerNotRunning.Title"),
                Description = loc.Get("Scan.ExplorerNotRunning.Desc"),
                Category = ScanCategory.Display,
                Severity = IssueSeverity.Error,
                SuggestedRepairId = "repair.explorer_restart"
            });
        }
        foreach (var p in explorerProcesses) p.Dispose();

        return Task.FromResult<IReadOnlyList<ScanIssue>>(issues);
    }

    public async Task<IReadOnlyList<ScanIssue>> ScanPeripheralAsync(CancellationToken ct = default)
    {
        var loc = LocalizationService.Instance;
        var issues = new List<ScanIssue>();

        // hidserv 同样可以按需启动；不把“未运行”误报为鼠标、键盘故障。

        // 检测打印后台处理程序（仅当有打印机时）
        try
        {
            // P0-6: 增加PowerShell超时到10秒
            var result = await RepairHelper.RunCommandAsync("powershell.exe",
                "-ExecutionPolicy Bypass -Command \"Get-Printer -ErrorAction SilentlyContinue | Select-Object -First 1\"", 10000);
            
            if (!string.IsNullOrWhiteSpace(result) && result.Contains("Name"))
            {
                if (!await RepairHelper.IsServiceRunningAsync("Spooler"))
                {
                    issues.Add(new ScanIssue
                    {
                        Id = "scan.peripheral.spooler_stopped",
                        Title = loc.Get("Scan.SpoolerStopped.Title"),
                        Description = loc.Get("Scan.SpoolerStopped.Desc"),
                        Category = ScanCategory.Peripheral,
                        Severity = IssueSeverity.Warning,
                        SuggestedRepairId = "repair.printer"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemScanService] Printer detection error: {ex.Message}");
        }

        return issues;
    }

    public async Task<IReadOnlyList<ScanIssue>> ScanSystemAsync(CancellationToken ct = default)
    {
        var loc = LocalizationService.Instance;
        var issues = new List<ScanIssue>();

        // 检测关键系统服务
        var criticalServices = new[]
        {
            ("RpcSs", loc.Get("Scan.Service.Rpc"), IssueSeverity.Critical),
            ("DcomLaunch", loc.Get("Scan.Service.Dcom"), IssueSeverity.Critical),
            ("EventLog", loc.Get("Scan.Service.EventLog"), IssueSeverity.Warning),
            ("Schedule", loc.Get("Scan.Service.Schedule"), IssueSeverity.Warning)
        };

        foreach (var (serviceName, displayName, severity) in criticalServices)
        {
            if (!await RepairHelper.IsServiceRunningAsync(serviceName))
            {
                issues.Add(new ScanIssue
                {
                    Id = $"scan.system.{serviceName.ToLower()}_stopped",
                    Title = loc.Get("Scan.ServiceStopped.Title", displayName),
                    Description = loc.Get("Scan.ServiceStopped.Desc", displayName),
                    Category = ScanCategory.System,
                    Severity = severity
                });
            }
        }

        // 检测磁盘空间
        try
        {
            var systemDrive = new DriveInfo(Environment.SystemDirectory[..1]);
            var freeGb = systemDrive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            if (freeGb < 5)
            {
                issues.Add(new ScanIssue
                {
                    Id = "scan.system.low_disk_space",
                    Title = loc.Get("Scan.LowDiskSpace.Title"),
                    Description = loc.Get("Scan.LowDiskSpace.Desc", freeGb),
                    Category = ScanCategory.Storage,
                    Severity = freeGb < 1 ? IssueSeverity.Critical : IssueSeverity.Warning,
                    SuggestedRepairId = "repair.temp_cleanup"
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemScanService] Disk space check error: {ex.Message}");
        }

        return issues;
    }
}
