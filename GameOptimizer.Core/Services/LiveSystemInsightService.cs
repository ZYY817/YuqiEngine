// File: GameOptimizer.Core/Services/LiveSystemInsightService.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class LiveSystemInsightService : ISystemInsightService, IHomeTelemetrySource, IDisposable
{
    private const string CpuSource = "PerformanceCounter: Processor/% Processor Time/_Total";
    private const string GpuSource = "PerformanceCounter: GPU Engine/Utilization Percentage (3D aggregate)";
    private const string MemorySource = "GlobalMemoryStatusEx";
    private const string DiskSource = "DriveInfo: system drive";

    private readonly object _snapshotGate = new();
    private readonly IGpuInventoryService _gpuInventory;
    private readonly PerformanceCounter? _cpuCounter;
    private TelemetryFieldStatus _cpuCounterInitializationStatus;
    private bool _cpuAwaitingSampleInterval;
    private readonly Ping _ping = new();
    private SystemResourceSnapshot? _lastSnapshot;

    private TelemetryFieldStatus _lastCpuStatus = TelemetryFieldStatus.Unavailable(CpuSource, "尚未采样");
    private TelemetryFieldStatus _lastGpuStatus = TelemetryFieldStatus.Unavailable(GpuSource, "尚未采样");
    private GpuLoadAttribution _lastGpuAttribution = GpuLoadAttribution.Unavailable(GpuSource, "尚未采样");
    private TelemetryFieldStatus _lastMemoryStatus = TelemetryFieldStatus.Unavailable(MemorySource, "尚未采样");
    private TelemetryFieldStatus _lastDiskStatus = TelemetryFieldStatus.Unavailable(DiskSource, "尚未采样");

    // Why: GPU Engine 实例按进程动态创建（pid_XXXX_luid_...），
    // 必须每次读取时重新枚举才能捕获新启动的游戏进程。
    // 字典缓存上一轮的计数器，保证 NextValue 有两次采样间隔。
    private Dictionary<string, PerformanceCounter> _gpuCounterCache = new(StringComparer.Ordinal);
    private bool _gpuEngineAvailable;
    private bool _gpuEngineChecked;

    // 缓存字段
    private double _cachedNetworkLatency;
    private DateTime _networkLatencyCacheTime = DateTime.MinValue;
    private static readonly TimeSpan NetworkLatencyCacheExpiry = TimeSpan.FromSeconds(5);

    private (double Cpu, double Gpu) _cachedTemperatures;
    private DateTime _temperatureCacheTime = DateTime.MinValue;
    private static readonly TimeSpan TemperatureCacheExpiry = TimeSpan.FromSeconds(2);

    private double _cachedDpcLatency;
    private DateTime _dpcLatencyCacheTime = DateTime.MinValue;
    private static readonly TimeSpan DpcLatencyCacheExpiry = TimeSpan.FromSeconds(3);

    public LiveSystemInsightService(IGpuInventoryService? gpuInventory = null)
    {
        _gpuInventory = gpuInventory ?? new GpuInventoryService();
        _cpuCounter = CreateCounter("Processor", "% Processor Time", "_Total", out _cpuCounterInitializationStatus);
        // Prime CPU counter
        if (_cpuCounter is not null)
        {
            if (TryReadNextValue(_cpuCounter, CpuSource, out _, out var primeStatus))
                _cpuAwaitingSampleInterval = true;
            else
                _cpuCounterInitializationStatus = primeStatus;
        }
    }

    public SystemResourceSnapshot GetCurrentSnapshot()
    {
        lock (_snapshotGate)
            return CaptureSnapshot();
    }

    public HomeTelemetrySnapshot GetCurrentSnapshotWithStatus()
    {
        lock (_snapshotGate)
        {
            var snapshot = CaptureSnapshot();
            return new HomeTelemetrySnapshot(
                snapshot,
                DateTimeOffset.UtcNow,
                _lastCpuStatus,
                _lastGpuStatus,
                _lastMemoryStatus,
                _lastDiskStatus)
            {
                GpuLoad = _lastGpuAttribution
            };
        }
    }

    private SystemResourceSnapshot CaptureSnapshot()
    {
        var cpu = ReadCpuUsage();
        var gpu = ReadGpuUsage();
        var (usedMemory, totalMemory) = ReadMemory();
        var latency = ReadNetworkLatency();
        var (cpuTemp, gpuTemp) = ReadTemperatures();
        var (diskUsed, diskTotal) = ReadDiskSpace();
        var dpcLatency = ReadDpcLatency();

        _lastSnapshot = new SystemResourceSnapshot(
            CpuUsage: cpu,
            GpuUsage: gpu,
            MemoryUsageGb: usedMemory,
            MemoryTotalGb: totalMemory,
            NetworkLatencyMs: latency,
            TemperatureGpu: gpuTemp,
            TemperatureCpu: cpuTemp,
            DiskUsedGb: diskUsed,
            DiskTotalGb: diskTotal,
            DpcLatencyUs: dpcLatency);

        return _lastSnapshot;
    }

    private (double Used, double Total) ReadDiskSpace()
    {
        try
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(systemDrive))
            {
                _lastDiskStatus = TelemetryFieldStatus.Unavailable(DiskSource, "无法解析系统盘路径");
                return (0, 0);
            }

            var driveInfo = new DriveInfo(systemDrive);
            if (driveInfo.IsReady)
            {
                var total = driveInfo.TotalSize / (1024d * 1024d * 1024d);
                var free = driveInfo.AvailableFreeSpace / (1024d * 1024d * 1024d);
                _lastDiskStatus = total > 0
                    ? TelemetryFieldStatus.Live(DiskSource)
                    : TelemetryFieldStatus.Error(DiskSource, "系统盘容量为 0");
                return (Math.Round(total - free, 1), Math.Round(total, 1));
            }

            _lastDiskStatus = TelemetryFieldStatus.Unavailable(DiskSource, "系统盘尚未就绪");
        }
        catch (UnauthorizedAccessException ex)
        {
            _lastDiskStatus = TelemetryFieldStatus.PermissionDenied(DiskSource, ex.Message);
        }
        catch (PlatformNotSupportedException ex)
        {
            _lastDiskStatus = TelemetryFieldStatus.Unsupported(DiskSource, ex.Message);
        }
        catch (Exception ex)
        {
            _lastDiskStatus = TelemetryFieldStatus.Error(DiskSource, ex.Message);
            Debug.WriteLine($"[LiveSystemInsightService][ReadDiskSpace] {ex.Message}");
        }
        return (0, 0);
    }

    public DetectedGame? GetActiveGame()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            _ = GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0) return null;

            using var process = Process.GetProcessById((int)processId);
            var name = string.IsNullOrWhiteSpace(process.MainWindowTitle)
                ? process.ProcessName
                : process.MainWindowTitle;
            if (string.IsNullOrWhiteSpace(name)) return null;

            var executablePath = TryGetExecutable(process) ?? process.ProcessName;
            var executable = Path.GetFileName(executablePath) ?? process.ProcessName;
            return new DetectedGame(
                name, executable,
                IconPath: executablePath,
                ProfileId: process.ProcessName,
                OptimizationStatus: "实时监控");
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<OptimizationRecommendation> GetRecommendations()
    {
        var snapshot = _lastSnapshot ?? GetCurrentSnapshot();
        var results = new List<OptimizationRecommendation>();

        if (snapshot.CpuUsage >= 85)
            results.Add(new OptimizationRecommendation("限制后台任务", "CPU 占用持续偏高，建议关闭高占用后台程序或开启性能加速模块。", "CPU 释放", RequiresRestart: false));

        if (snapshot.GpuUsage >= 90)
            results.Add(new OptimizationRecommendation("启用电竞模式", "GPU 正在满载运行，可切换电竞模式以提升调度优先级。", "帧率提升", RequiresRestart: false));

        if (snapshot.MemoryUsageGb >= snapshot.MemoryTotalGb * 0.85)
            results.Add(new OptimizationRecommendation("释放内存", "内存接近占满，建议清理缓存或减少常驻程序。", "稳定性", RequiresRestart: false));

        if (results.Count == 0)
            results.Add(new OptimizationRecommendation("保持当前策略", "硬件指标处于健康区间，可按需在各模块中逐项调整。", "状态正常", RequiresRestart: false));

        return results;
    }

    private double ReadCpuUsage()
    {
        if (_cpuCounter is null)
        {
            _lastCpuStatus = _cpuCounterInitializationStatus;
            return 0;
        }

        if (_cpuAwaitingSampleInterval)
        {
            _cpuAwaitingSampleInterval = false;
            if (TryReadNextValue(_cpuCounter, CpuSource, out _, out var baselineStatus))
                _lastCpuStatus = TelemetryFieldStatus.Unavailable(CpuSource, "等待下一次采样间隔");
            else
                _lastCpuStatus = baselineStatus;
            return 0;
        }

        if (!TryReadNextValue(_cpuCounter, CpuSource, out var value, out var status))
        {
            _lastCpuStatus = status;
            return 0;
        }

        _lastCpuStatus = status;
        return Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// 动态枚举 GPU Engine 实例，按 LUID 分组取使用率最高的 GPU。
    /// Why: GPU Engine 实例格式为 pid_XXXX_luid_0x..._phys_N_eng_M_engtype_3D，
    /// 每个进程的每个引擎都有独立实例，进程启动/退出时动态增减。
    /// 必须每次重新枚举才能捕获新启动的游戏进程的 GPU 使用率。
    /// </summary>
    private double ReadGpuUsage()
    {
        // 延迟检查 GPU Engine 类别是否存在（只检查一次）
        if (!_gpuEngineChecked)
        {
            _gpuEngineChecked = true;
            try
            {
                _gpuEngineAvailable = PerformanceCounterCategory.Exists("GPU Engine");
                if (!_gpuEngineAvailable)
                    _lastGpuStatus = TelemetryFieldStatus.Unsupported(GpuSource, "GPU Engine 性能计数器不可用");
            }
            catch (Exception ex)
            {
                _gpuEngineAvailable = false;
                _lastGpuStatus = ClassifyException(GpuSource, ex);
            }
            Debug.WriteLine($"[LiveSystemInsightService][ReadGpuUsage] GPU Engine available: {_gpuEngineAvailable}");
        }
        if (!_gpuEngineAvailable)
        {
            _lastGpuAttribution = GpuLoadAttribution.Unavailable(GpuSource, _lastGpuStatus.Detail);
            return 0;
        }

        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var currentInstances = category.GetInstanceNames()
                .Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (currentInstances.Length == 0)
            {
                _lastGpuStatus = TelemetryFieldStatus.Unavailable(GpuSource, "当前没有可用的 3D 引擎实例");
                _lastGpuAttribution = GpuLoadAttribution.Unavailable(GpuSource, _lastGpuStatus.Detail);
                return 0;
            }

            var newCache = new Dictionary<string, PerformanceCounter>(currentInstances.Length, StringComparer.Ordinal);
            var usageByLuid = new Dictionary<string, double>(StringComparer.Ordinal);
            TelemetryFieldStatus? lastFailure = null;

            foreach (var instance in currentInstances)
            {
                // Why: 复用上一轮的计数器，保证 NextValue 有两次采样间隔才返回有效值
                var isNewCounter = false;
                if (!_gpuCounterCache.Remove(instance, out var counter))
                {
                    isNewCounter = true;
                    try
                    {
                        counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                        if (!TryReadNextValue(counter, GpuSource, out _, out var primeStatus))
                        {
                            lastFailure = primeStatus;
                            counter.Dispose();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastFailure = ClassifyException(GpuSource, ex);
                        counter?.Dispose();
                        continue;
                    }
                }

                if (isNewCounter)
                {
                    newCache[instance] = counter;
                    lastFailure = TelemetryFieldStatus.Unavailable(GpuSource, "新 GPU 3D 引擎实例等待下一次采样间隔");
                    continue;
                }

                if (!TryReadNextValue(counter, GpuSource, out var value, out var valueStatus))
                {
                    lastFailure = valueStatus;
                    counter.Dispose();
                    continue;
                }

                newCache[instance] = counter;

                var luid = ExtractLuid(instance);
                if (luid is null) continue;

                if (!usageByLuid.TryGetValue(luid, out var existing))
                    usageByLuid[luid] = value;
                else
                    usageByLuid[luid] = existing + value;
            }

            // Dispose 已消失的旧计数器
            foreach (var stale in _gpuCounterCache.Values)
                stale.Dispose();

            _gpuCounterCache = newCache;

            if (usageByLuid.Count == 0)
            {
                _lastGpuStatus = lastFailure ?? TelemetryFieldStatus.Unavailable(GpuSource, "未能从 3D 引擎实例解析 GPU 标识");
                _lastGpuAttribution = GpuLoadAttribution.Unavailable(GpuSource, _lastGpuStatus.Detail);
                return 0;
            }

            _lastGpuStatus = TelemetryFieldStatus.Live(GpuSource);
            var primaryLoad = usageByLuid.MaxBy(static pair => pair.Value);
            var usage = Math.Clamp(primaryLoad.Value, 0, 100);
            _lastGpuAttribution = CreateGpuLoadAttribution(primaryLoad.Key, usage);
            return usage;
        }
        catch (Exception ex)
        {
            _lastGpuStatus = ClassifyException(GpuSource, ex);
            _lastGpuAttribution = GpuLoadAttribution.Unavailable(GpuSource, _lastGpuStatus.Detail);
            Debug.WriteLine($"[LiveSystemInsightService][ReadGpuUsage] {ex.Message}");
            return 0;
        }
    }

    private GpuLoadAttribution CreateGpuLoadAttribution(string luid, double usage)
    {
        var inventory = _gpuInventory.GetSnapshot();
        // GPU Engine 和 DXGI 共享 AdapterLuid；禁止按 WMI 枚举序号、厂商、显存或“系统仅有一张卡”猜测归属。
        var adapter = inventory.Adapters.FirstOrDefault(candidate =>
            string.Equals(candidate.DxgiLuid, luid, StringComparison.OrdinalIgnoreCase));

        return adapter is null
            ? GpuLoadAttribution.Unmapped(
                usage,
                GpuSource,
                $"GPU Engine LUID {luid} 已读取，但 DXGI 硬件清单没有可验证的同一 LUID 键；负载归属不可识别。")
            : new GpuLoadAttribution(
                usage,
                adapter.StableKey,
                adapter.Name,
                adapter.Kind,
                GpuSource,
                "GPU Engine LUID 与硬件清单稳定键一致。");
    }

    /// <summary>
    /// 从实例名中提取 LUID（如 "0x00000000_0x000126A1"）。
    /// 实例名格式: pid_XXXX_luid_0x00000000_0xYYYYYYYY_phys_N_eng_M_engtype_3D
    /// </summary>
    private static string? ExtractLuid(string instanceName)
    {
        // Why: 用手动查找代替正则，避免每次调用的正则开销（每秒调用 100+ 次）
        const string marker = "_luid_";
        var start = instanceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += marker.Length;

        const string endMarker = "_phys_";
        var end = instanceName.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;

        return instanceName.Substring(start, end - start);
    }

    private (double Used, double Total) ReadMemory()
    {
        try
        {
            var status = new MEMORYSTATUSEX();
            status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            if (GlobalMemoryStatusEx(ref status))
            {
                var total = status.ullTotalPhys / (1024d * 1024d * 1024d);
                var available = status.ullAvailPhys / (1024d * 1024d * 1024d);
                var used = Math.Clamp(total - available, 0, total);
                _lastMemoryStatus = total > 0
                    ? TelemetryFieldStatus.Live(MemorySource)
                    : TelemetryFieldStatus.Error(MemorySource, "系统报告的物理内存总量为 0");
                return (Math.Round(used, 1), Math.Round(total, 1));
            }

            _lastMemoryStatus = TelemetryFieldStatus.Error(MemorySource, "GlobalMemoryStatusEx 返回失败");
        }
        catch (EntryPointNotFoundException ex)
        {
            _lastMemoryStatus = TelemetryFieldStatus.Unsupported(MemorySource, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _lastMemoryStatus = TelemetryFieldStatus.PermissionDenied(MemorySource, ex.Message);
        }
        catch (Exception ex)
        {
            _lastMemoryStatus = ClassifyException(MemorySource, ex);
        }

        return (0, 0);
    }

    private double ReadNetworkLatency()
    {
        if (DateTime.Now - _networkLatencyCacheTime < NetworkLatencyCacheExpiry)
            return _cachedNetworkLatency;

        try
        {
            var reply = _ping.Send("1.1.1.1", 200);
            if (reply?.Status == IPStatus.Success)
            {
                _cachedNetworkLatency = reply.RoundtripTime;
                _networkLatencyCacheTime = DateTime.Now;
                return _cachedNetworkLatency;
            }
        }
        catch
        {
            Debug.WriteLine("[LiveSystemInsightService][ReadNetworkLatency] Ping failed");
        }
        return _cachedNetworkLatency > 0 ? _cachedNetworkLatency : 0;
    }

    private double ReadDpcLatency()
    {
        if (DateTime.Now - _dpcLatencyCacheTime < DpcLatencyCacheExpiry)
            return _cachedDpcLatency;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT InterruptsPersec FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);

            foreach (var obj in searcher.Get())
            {
                var rawVal = obj["InterruptsPersec"];
                if (rawVal is null) continue;
                try
                {
                    var interrupts = Convert.ToUInt64(rawVal);
                    var estimatedLatency = interrupts > 0 ? Math.Max(50, 500000.0 / interrupts) : 100;
                    _cachedDpcLatency = Math.Round(Math.Min(estimatedLatency, 1000), 1);
                    _dpcLatencyCacheTime = DateTime.Now;
                    return _cachedDpcLatency;
                }
                catch { /* 类型转换失败 */ }
            }
        }
        catch
        {
            Debug.WriteLine("[LiveSystemInsightService][ReadDpcLatency] WMI query failed");
        }
        return _cachedDpcLatency > 0 ? _cachedDpcLatency : 100;
    }

    private (double Cpu, double Gpu) ReadTemperatures()
    {
        if (DateTime.Now - _temperatureCacheTime < TemperatureCacheExpiry)
            return _cachedTemperatures;

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            var readings = new List<double>();
            foreach (var obj in searcher.Get())
            {
                var rawVal = obj["CurrentTemperature"];
                if (rawVal is null) continue;
                try
                {
                    var raw = Convert.ToUInt32(rawVal);
                    if (raw > 0)
                    {
                        var celsius = (raw - 2732) / 10.0;
                        if (!double.IsNaN(celsius) && celsius > 0)
                            readings.Add(celsius);
                    }
                }
                catch { /* 类型转换失败 */ }
            }

            if (readings.Count > 0)
            {
                var cpuTemp = Math.Round(readings.Average(), 1);
                _cachedTemperatures = (cpuTemp, cpuTemp);
                _temperatureCacheTime = DateTime.Now;
                return _cachedTemperatures;
            }
        }
        catch
        {
            Debug.WriteLine("[LiveSystemInsightService][ReadTemperatures] WMI thermal query failed");
        }
        return _cachedTemperatures.Cpu > 0 ? _cachedTemperatures : (0, 0);
    }

    private static PerformanceCounter? CreateCounter(
        string category,
        string counter,
        string instance,
        out TelemetryFieldStatus status)
    {
        var source = $"PerformanceCounter: {category}/{counter}/{instance}";
        try
        {
            status = TelemetryFieldStatus.Live(source);
            return new PerformanceCounter(category, counter, instance, readOnly: true);
        }
        catch (Exception ex)
        {
            status = ClassifyException(source, ex);
            return null;
        }
    }

    private static bool TryReadNextValue(
        PerformanceCounter counter,
        string source,
        out double value,
        out TelemetryFieldStatus status)
    {
        try
        {
            value = counter.NextValue();
            if (!double.IsFinite(value))
            {
                status = TelemetryFieldStatus.Error(source, "性能计数器返回了非有限值");
                value = 0;
                return false;
            }

            status = TelemetryFieldStatus.Live(source);
            return true;
        }
        catch (Exception ex)
        {
            value = 0;
            status = ClassifyException(source, ex);
            return false;
        }
    }

    private static TelemetryFieldStatus ClassifyException(string source, Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => TelemetryFieldStatus.PermissionDenied(source, exception.Message),
            PlatformNotSupportedException or EntryPointNotFoundException => TelemetryFieldStatus.Unsupported(source, exception.Message),
            InvalidOperationException => TelemetryFieldStatus.Unavailable(source, exception.Message),
            _ => TelemetryFieldStatus.Error(source, exception.Message)
        };
    }

    private static string? TryGetExecutable(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch (Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public void Dispose()
    {
        lock (_snapshotGate)
        {
            _cpuCounter?.Dispose();
            foreach (var counter in _gpuCounterCache.Values)
                counter.Dispose();
            _gpuCounterCache.Clear();
            _ping.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
