using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 电源状态枚举
/// </summary>
public enum PowerLineStatus
{
    Unknown,
    Online,     // 插电
    Offline     // 电池
}

/// <summary>
/// 系统监控数据
/// </summary>
public record SystemMonitorData
{
    public int CpuTemperature { get; init; }        // CPU温度 (°C)
    public int CpuFrequency { get; init; }          // CPU当前频率 (MHz)
    public int CpuUsage { get; init; }              // CPU使用率 (%)
    public PowerLineStatus PowerStatus { get; init; } // 电源状态
    public int BatteryPercent { get; init; }        // 电池电量 (%)
    public bool IsThermalThrottling { get; init; }  // 是否温度降频
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 系统监控服务 - 实时监控CPU温度、频率、电源状态
/// </summary>
[SupportedOSPlatform("windows")]
public class SystemMonitorService : IDisposable
{
    private readonly PerformanceCounter? _cpuCounter;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    
    // 缓存字段
    private int _cachedCpuFrequency;
    private DateTime _cpuFrequencyCacheTime = DateTime.MinValue;
    private static readonly TimeSpan CpuFrequencyCacheExpiry = TimeSpan.FromSeconds(1);
    
    private int _cachedCpuTemperature;
    private DateTime _cpuTemperatureCacheTime = DateTime.MinValue;
    private static readonly TimeSpan CpuTemperatureCacheExpiry = TimeSpan.FromSeconds(2);
    
    public event Action<SystemMonitorData>? OnDataUpdated;
    public event Action<int>? OnTemperatureWarning;
    
    public int WarningTemperature { get; set; } = 85;
    public int CriticalTemperature { get; set; } = 95;
    
    public SystemMonitorData? LatestData { get; private set; }

    public SystemMonitorService()
    {
        try
        {
            // Why: 先检查类别是否存在，避免在无 Processor 计数器的环境上抛 InvalidOperationException
            if (!PerformanceCounterCategory.Exists("Processor"))
            {
                Debug.WriteLine("[SystemMonitor] 'Processor' performance counter category not found");
                return;
            }
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemMonitor] Failed to init CPU counter: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取当前电源状态（插电/电池）
    /// </summary>
    public PowerLineStatus GetPowerLineStatus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (var obj in searcher.Get())
            {
                var batteryStatus = Convert.ToInt32(obj["BatteryStatus"] ?? 0);
                // BatteryStatus: 1=Discharging(电池), 2=AC Power(插电), 3-9=其他充电状态
                return batteryStatus >= 2 ? PowerLineStatus.Online : PowerLineStatus.Offline;
            }
            // 没有电池 = 台式机 = 始终插电
            return PowerLineStatus.Online;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemMonitor] GetPowerLineStatus error: {ex.Message}");
            return PowerLineStatus.Unknown;
        }
    }

    /// <summary>
    /// 获取电池电量百分比
    /// </summary>
    public int GetBatteryPercent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (var obj in searcher.Get())
            {
                return Convert.ToInt32(obj["EstimatedChargeRemaining"] ?? 0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemMonitor] GetBatteryPercent error: {ex.Message}");
        }
        
        return -1; // 无电池或无法获取
    }

    /// <summary>
    /// 获取CPU温度（通过WMI，带缓存）- P1-011: 同步版本，使用缓存
    /// </summary>
    public int GetCpuTemperature()
    {
        if (DateTime.Now - _cpuTemperatureCacheTime < CpuTemperatureCacheExpiry && _cachedCpuTemperature > 0)
            return _cachedCpuTemperature;
        
        // 返回缓存值，后台异步更新
        _ = Task.Run(UpdateCpuTemperatureCacheAsync);
        return _cachedCpuTemperature > 0 ? _cachedCpuTemperature : -1;
    }

    /// <summary>
    /// P1-011: 异步获取CPU温度，避免阻塞UI线程
    /// </summary>
    public async Task<int> GetCpuTemperatureAsync()
    {
        if (DateTime.Now - _cpuTemperatureCacheTime < CpuTemperatureCacheExpiry && _cachedCpuTemperature > 0)
            return _cachedCpuTemperature;
        
        await UpdateCpuTemperatureCacheAsync().ConfigureAwait(false);
        return _cachedCpuTemperature > 0 ? _cachedCpuTemperature : -1;
    }

    private async Task UpdateCpuTemperatureCacheAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", 
                    "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                foreach (var obj in searcher.Get())
                {
                    var temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    var celsius = (temp - 2732) / 10.0;
                    if (celsius > 0 && celsius < 150)
                    {
                        _cachedCpuTemperature = (int)celsius;
                        _cpuTemperatureCacheTime = DateTime.Now;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] WMI temp query failed: {ex.Message}");
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT CurrentReading FROM Win32_TemperatureProbe");
                searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                foreach (var obj in searcher.Get())
                {
                    var temp = Convert.ToInt32(obj["CurrentReading"] ?? 0);
                    if (temp > 0 && temp < 150)
                    {
                        _cachedCpuTemperature = temp;
                        _cpuTemperatureCacheTime = DateTime.Now;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] WMI temperature query failed: {ex.Message}");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取CPU当前频率（带缓存）
    /// </summary>
    public int GetCpuCurrentFrequency()
    {
        // 使用缓存避免频繁 WMI 查询
        if (DateTime.Now - _cpuFrequencyCacheTime < CpuFrequencyCacheExpiry && _cachedCpuFrequency > 0)
            return _cachedCpuFrequency;
        
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (var obj in searcher.Get())
            {
                _cachedCpuFrequency = Convert.ToInt32(obj["CurrentClockSpeed"] ?? 0);
                _cpuFrequencyCacheTime = DateTime.Now;
                return _cachedCpuFrequency;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemMonitor] WMI frequency query failed: {ex.Message}");
        }
        return _cachedCpuFrequency > 0 ? _cachedCpuFrequency : 0;
    }

    /// <summary>
    /// 获取CPU使用率
    /// </summary>
    public int GetCpuUsage()
    {
        try
        {
            return (int)(_cpuCounter?.NextValue() ?? 0);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 获取完整监控数据
    /// </summary>
    public SystemMonitorData GetCurrentData()
    {
        var temp = GetCpuTemperature();
        var data = new SystemMonitorData
        {
            CpuTemperature = temp,
            CpuFrequency = GetCpuCurrentFrequency(),
            CpuUsage = GetCpuUsage(),
            PowerStatus = GetPowerLineStatus(),
            BatteryPercent = GetBatteryPercent(),
            IsThermalThrottling = temp >= WarningTemperature,
            Timestamp = DateTime.Now
        };
        LatestData = data;
        return data;
    }

    /// <summary>
    /// 启动后台监控
    /// </summary>
    public void StartMonitoring(int intervalMs = 2000)
    {
        StopMonitoring();
        _monitorCts = new CancellationTokenSource();
        _monitorTask = Task.Run(async () =>
        {
            while (!_monitorCts.Token.IsCancellationRequested)
            {
                try
                {
                    var data = GetCurrentData();
                    OnDataUpdated?.Invoke(data);
                    
                    if (data.CpuTemperature >= WarningTemperature)
                        OnTemperatureWarning?.Invoke(data.CpuTemperature);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemMonitor] Monitor error: {ex.Message}");
                }
                
                await Task.Delay(intervalMs, _monitorCts.Token).ConfigureAwait(false);
            }
        }, _monitorCts.Token);
    }

    /// <summary>
    /// 停止后台监控
    /// </summary>
    public void StopMonitoring()
    {
        if (_monitorCts == null) return;

        _monitorCts.Cancel();

        // Why: 不再用 .Wait() 阻塞调用线程（可能是 UI 线程）
        // Cancel 后监控循环的 Task.Delay 会抛 OperationCanceledException 自然退出
        var oldTask = _monitorTask;
        _monitorTask = null;

        try { _monitorCts.Dispose(); }
        catch (ObjectDisposedException) { /* 重复 Dispose 属正常路径 */ }
        _monitorCts = null;

        // fire-and-forget 等待旧任务结束，仅用于日志，不阻塞调用方
        if (oldTask != null)
        {
            _ = Task.Run(async () =>
            {
                try { await oldTask.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch { /* 超时/取消/已完成，均忽略 */ }
            });
        }
    }


    public void Dispose()
    {
        StopMonitoring();
        
        // 清理事件订阅，防止内存泄漏
        OnDataUpdated = null;
        OnTemperatureWarning = null;
        
        _cpuCounter?.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
