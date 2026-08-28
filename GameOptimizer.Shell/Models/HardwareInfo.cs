using System;
using System.Management;
using System.Runtime.Versioning;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// GPU 厂商枚举
/// </summary>
public enum GpuVendor
{
    Unknown,
    NVIDIA,
    AMD,
    Intel
}

/// <summary>
/// 设备类型枚举
/// </summary>
public enum DeviceType
{
    Unknown,
    Desktop,    // 台式机
    Laptop,     // 笔记本
    Tablet,     // 平板
    Server      // 服务器
}

/// <summary>
/// GPU 信息记录
/// </summary>
public record GpuInfo(
    string Name,
    GpuVendor Vendor,
    long MemoryMb,
    string DriverVersion,
    GpuAdapterKind Kind,
    string ClassificationSource)
{
    public bool IsDiscrete => Kind == GpuAdapterKind.Discrete;
}

/// <summary>
/// 硬件信息模型
/// </summary>
[SupportedOSPlatform("windows")]
public class HardwareInfo
{
    /// <summary>
    /// CPU 名称
    /// </summary>
    public string CpuName { get; set; } = "Unknown";

    /// <summary>
    /// CPU 核心数
    /// </summary>
    public int CpuCores { get; set; }

    /// <summary>
    /// CPU 线程数
    /// </summary>
    public int CpuThreads { get; set; }

    /// <summary>
    /// CPU 基础频率 (MHz)
    /// </summary>
    public int CpuBaseFrequency { get; set; }

    /// <summary>
    /// GPU 名称
    /// </summary>
    public string GpuName { get; set; } = "Unknown";

    /// <summary>
    /// GPU 显存 (MB)
    /// </summary>
    public long GpuMemoryMb { get; set; }

    /// <summary>
    /// GPU 驱动版本
    /// </summary>
    public string GpuDriverVersion { get; set; } = "Unknown";

    /// <summary>
    /// 内存总量 (GB)
    /// </summary>
    public double TotalMemoryGb { get; set; }

    /// <summary>
    /// 内存类型
    /// </summary>
    public string MemoryType { get; set; } = "Unknown";

    /// <summary>
    /// 内存频率 (MHz)
    /// </summary>
    public int MemoryFrequency { get; set; }

    /// <summary>
    /// 主板名称
    /// </summary>
    public string MotherboardName { get; set; } = "Unknown";

    /// <summary>
    /// 操作系统版本
    /// </summary>
    public string OsVersion { get; set; } = "Unknown";

    /// <summary>
    /// Windows 版本号
    /// </summary>
    public string WindowsBuild { get; set; } = "Unknown";

    /// <summary>
    /// 系统盘剩余空间 (GB)
    /// </summary>
    public double SystemDriveFreeGb { get; set; }

    /// <summary>
    /// CPU 厂商
    /// </summary>
    public CpuVendor CpuVendor { get; set; } = CpuVendor.Unknown;

    /// <summary>
    /// 是否为 AMD X3D 系列 CPU
    /// </summary>
    public bool IsAmdX3D { get; set; }

    /// <summary>
    /// GPU 厂商
    /// </summary>
    public GpuVendor GpuVendor { get; set; } = GpuVendor.Unknown;

    /// <summary>
    /// 设备类型（台式机/笔记本）
    /// </summary>
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;

    /// <summary>
    /// 是否有电池（笔记本判断依据之一）
    /// </summary>
    public bool HasBattery { get; set; }

    /// <summary>
    /// 是否为 Intel 大小核 CPU（12代及以上）
    /// </summary>
    public bool IsIntelHybrid { get; set; }

    /// <summary>
    /// P核数量（Intel大小核）
    /// </summary>
    public int PCoreCount { get; set; }

    /// <summary>
    /// E核数量（Intel大小核）
    /// </summary>
    public int ECoreCount { get; set; }

    /// <summary>
    /// 是否有独立显卡
    /// </summary>
    public bool HasDiscreteGpu { get; set; }

    /// <summary>
    /// 当前展示主适配器的类型。Unknown 不会被降格为集显。
    /// </summary>
    public GpuAdapterKind PrimaryGpuKind { get; set; } = GpuAdapterKind.Unknown;

    /// <summary>
    /// 是否插电（笔记本）
    /// </summary>
    public bool IsOnACPower { get; set; } = true;

    /// <summary>
    /// 当前CPU温度
    /// </summary>
    public int CurrentCpuTemp { get; set; }

    /// <summary>
    /// 当前CPU频率
    /// </summary>
    public int CurrentCpuFrequency { get; set; }

    /// <summary>
    /// 是否启用了 XMP/EXPO
    /// </summary>
    public bool? IsXmpEnabled { get; set; }

    #region 多GPU检测支持
    /// <summary>
    /// 是否有 NVIDIA 显卡（独显或集显）
    /// </summary>
    public bool HasNvidiaGpu { get; set; }

    /// <summary>
    /// 是否有 AMD 显卡（独显或集显）
    /// </summary>
    public bool HasAmdGpu { get; set; }

    /// <summary>
    /// 是否有 Intel 显卡（核显或 Arc 独显）
    /// </summary>
    public bool HasIntelGpu { get; set; }

    /// <summary>
    /// 所有检测到的 GPU 列表
    /// </summary>
    public System.Collections.Generic.List<GpuInfo> AllGpus { get; set; } = new();
    #endregion

    /// <summary>
    /// 检测硬件信息
    /// </summary>
    public static HardwareInfo Detect(IGpuInventoryService? gpuInventory = null)
    {
        var info = new HardwareInfo();

        try
        {
            // CPU 信息 - P2-034: 添加 WMI 超时
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                foreach (var obj in searcher.Get())
                {
                    info.CpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    info.CpuCores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    info.CpuThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
                    info.CpuBaseFrequency = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0);
                    
                    // 检测 CPU 厂商
                    var cpuManufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                    var cpuName = info.CpuName.ToUpperInvariant();
                    info.CpuVendor = DetectCpuVendor(cpuManufacturer, info.CpuName);
                    if (info.CpuVendor == CpuVendor.Intel)
                    {
                        // 精确检测 Intel 大小核 CPU
                        info.IsIntelHybrid = DetectIntelHybrid(cpuName, info.CpuCores, info.CpuThreads);
                        if (info.IsIntelHybrid)
                        {
                            (info.PCoreCount, info.ECoreCount) = EstimateIntelCoreLayout(cpuName, info.CpuCores, info.CpuThreads);
                        }
                    }
                    else if (info.CpuVendor == CpuVendor.AMD)
                    {
                        // 检测 X3D 系列（带 3D V-Cache）
                        info.IsAmdX3D = cpuName.Contains("X3D") || cpuName.Contains("3D V-CACHE");
                    }
                    break;
                }
            }

            // GPU 信息 - 支持多GPU检测
            DetectAllGpus(info, gpuInventory ?? new GpuInventoryService());

            // 内存信息 - P2-034: 添加 WMI 超时
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                long totalBytes = 0;
                int configuredSpeed = 0;
                int baseSpeed = 0;
                foreach (var obj in searcher.Get())
                {
                    totalBytes += Convert.ToInt64(obj["Capacity"] ?? 0);
                    configuredSpeed = Convert.ToInt32(obj["ConfiguredClockSpeed"] ?? obj["Speed"] ?? 0);
                    baseSpeed = Convert.ToInt32(obj["Speed"] ?? 0);
                    info.MemoryFrequency = configuredSpeed > 0 ? configuredSpeed : baseSpeed;
                    var memType = Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0);
                    info.MemoryType = memType switch
                    {
                        20 => "DDR",
                        21 => "DDR2",
                        24 => "DDR3",
                        26 => "DDR4",
                        34 => "DDR5",
                        _ => LocalizationService.Instance.Get("Hardware.MemoryType.Unknown")
                    };
                }
                info.TotalMemoryGb = totalBytes / 1024.0 / 1024.0 / 1024.0;
                
                // 简单判断 XMP/EXPO 是否启用（配置频率高于标称频率）
                // DDR4 标准频率 2133-3200，DDR5 标准频率 4800-5600
                if (info.MemoryType == "DDR4" && info.MemoryFrequency > 3200)
                {
                    info.IsXmpEnabled = true;
                }
                else if (info.MemoryType == "DDR5" && info.MemoryFrequency > 5600)
                {
                    info.IsXmpEnabled = true;
                }
                else if (configuredSpeed > baseSpeed && baseSpeed > 0)
                {
                    info.IsXmpEnabled = true;
                }
                else
                {
                    info.IsXmpEnabled = false;
                }
            }

            // 主板信息 - P2-034: 添加 WMI 超时
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                foreach (var obj in searcher.Get())
                {
                    var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                    var product = obj["Product"]?.ToString() ?? "";
                    info.MotherboardName = $"{manufacturer} {product}".Trim();
                    break;
                }
            }

            // 操作系统信息 - P2-034: 添加 WMI 超时
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                foreach (var obj in searcher.Get())
                {
                    info.OsVersion = obj["Caption"]?.ToString() ?? "Unknown";
                    info.WindowsBuild = obj["BuildNumber"]?.ToString() ?? "Unknown";
                    break;
                }
            }

            // 系统盘空间
            var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var systemDrive = !string.IsNullOrEmpty(systemPath) ? systemPath.Substring(0, 1) : "C";
            var driveInfo = new System.IO.DriveInfo(systemDrive);
            info.SystemDriveFreeGb = driveInfo.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;

            // 设备类型检测（台式机/笔记本）
            info.DeviceType = DetectDeviceType();
            info.HasBattery = DetectBattery();
            
            // 电源状态检测
            info.IsOnACPower = DetectACPower();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HardwareInfo] Detect error: {ex.Message}");
        }

        return info;
    }

    /// <summary>
    /// 精确检测 Intel 大小核 CPU
    /// </summary>
    private static bool DetectIntelHybrid(string cpuName, int cores, int threads)
    {
        // 12/13/14代、Ultra系列和Core Ultra 200系列是大小核架构
        // Why: "Core Ultra 7 258V" 不含 "-12/-13/-14" 也不含单独的 "ULTRA"
        var isHybridGen = cpuName.Contains("-12") || cpuName.Contains("-13") || 
                          cpuName.Contains("-14") || cpuName.Contains("ULTRA") ||
                          cpuName.Contains("CORE ULTRA");
        
        if (!isHybridGen) return false;
        
        // 进一步验证：大小核CPU的线程数不是核心数的2倍
        // 例如：i5-12400 是 6核12线程（纯大核），i5-12600K 是 10核16线程（6P+4E）
        // P核支持超线程，E核不支持
        // 如果 threads == cores * 2，说明是纯大核
        if (threads == cores * 2) return false;
        
        // 如果线程数介于核心数和核心数*2之间，说明有E核
        // 例如：10核16线程 = 6P*2 + 4E = 16
        return threads > cores && threads < cores * 2;
    }

    private static CpuVendor DetectCpuVendor(string manufacturer, string cpuName)
    {
        // Why: keep vendor detection consistent across Shell/Core modules and avoid
        // false Intel matches from generic strings like "12-Core Processor".
        return HardwareVendorDetector.DetectCpuVendor(manufacturer, cpuName);
    }

    /// <summary>
    /// 估算 Intel P核和E核数量
    /// </summary>
    private static (int pCores, int eCores) EstimateIntelCoreLayout(string cpuName, int cores, int threads)
    {
        // P核支持超线程(2线程)，E核不支持(1线程)
        // 设 P核数量为 p，E核数量为 e
        // p + e = cores
        // 2p + e = threads
        // 解方程：p = threads - cores, e = 2*cores - threads
        var pCores = threads - cores;
        var eCores = 2 * cores - threads;
        
        if (pCores < 0) pCores = 0;
        if (eCores < 0) eCores = 0;
        
        return (pCores, eCores);
    }

    /// <summary>
    /// 检测是否插电
    /// </summary>
    private static bool DetectACPower()
    {
        try
        {
            // 通过 WMI 查询电池状态
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (var obj in searcher.Get())
            {
                var batteryStatus = Convert.ToInt32(obj["BatteryStatus"] ?? 0);
                // BatteryStatus: 1=Discharging(电池), 2=AC Power(插电)
                return batteryStatus == 2;
            }
            // 没有电池 = 台式机 = 始终插电
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HardwareInfo] DetectACPower error: {ex.Message}");
            return true; // 默认认为插电
        }
    }
    
    /// <summary>
    /// 获取 CPU 厂商显示名称
    /// </summary>
    public string GetCpuVendorDisplayName() => CpuVendor switch
    {
        CpuVendor.Intel => "Intel",
        CpuVendor.AMD => IsAmdX3D ? LocalizationService.Instance.Get("Hardware.CpuVendor.AmdX3D") : "AMD",
        _ => LocalizationService.Instance.Get("Hardware.CpuVendor.Unknown")
    };
    
    /// <summary>
    /// 获取 GPU 厂商显示名称
    /// </summary>
    public string GetGpuVendorDisplayName() => GpuVendor switch
    {
        GpuVendor.NVIDIA => "NVIDIA",
        GpuVendor.AMD => "AMD",
        GpuVendor.Intel => "Intel",
        _ => LocalizationService.Instance.Get("Hardware.GpuVendor.Unknown")
    };
    
    /// <summary>
    /// 获取 XMP/EXPO 状态描述
    /// </summary>
    public string GetXmpStatusDescription()
    {
        var loc = LocalizationService.Instance;
        if (!IsXmpEnabled.HasValue) return loc.Get("Hardware.Xmp.Undetectable");
        if (IsXmpEnabled.Value) return loc.Get("Hardware.Xmp.Enabled", MemoryFrequency);
        
        var recommendedKey = MemoryType switch
        {
            "DDR4" => "Hardware.Xmp.Disabled.DDR4",
            "DDR5" => "Hardware.Xmp.Disabled.DDR5",
            _ => "Hardware.Xmp.Disabled.Other"
        };
        return loc.Get(recommendedKey);
    }

    /// <summary>
    /// 获取硬件评分（0-100）
    /// </summary>
    public int GetHardwareScore()
    {
        var score = 50; // 基础分

        // CPU 评分
        if (CpuThreads >= 16) score += 15;
        else if (CpuThreads >= 8) score += 10;
        else if (CpuThreads >= 4) score += 5;

        // GPU 评分
        if (GpuMemoryMb >= 8192) score += 15;
        else if (GpuMemoryMb >= 4096) score += 10;
        else if (GpuMemoryMb >= 2048) score += 5;

        // 内存评分
        if (TotalMemoryGb >= 32) score += 15;
        else if (TotalMemoryGb >= 16) score += 10;
        else if (TotalMemoryGb >= 8) score += 5;

        // 内存类型评分
        if (MemoryType == "DDR5") score += 5;
        else if (MemoryType == "DDR4") score += 3;

        return Math.Min(100, score);
    }

    /// <summary>
    /// 获取硬件等级描述
    /// </summary>
    public string GetHardwareGrade()
    {
        var loc = LocalizationService.Instance;
        var score = GetHardwareScore();
        return score switch
        {
            >= 90 => loc.Get("Hardware.Grade.TopTier"),
            >= 75 => loc.Get("Hardware.Grade.HighEnd"),
            >= 60 => loc.Get("Hardware.Grade.UpperMid"),
            >= 45 => loc.Get("Hardware.Grade.Mid"),
            >= 30 => loc.Get("Hardware.Grade.Entry"),
            _ => loc.Get("Hardware.Grade.Basic")
        };
    }

    /// <summary>
    /// 获取设备类型显示名称
    /// </summary>
    public string GetDeviceTypeDisplayName()
    {
        var loc = LocalizationService.Instance;
        return DeviceType switch
        {
            DeviceType.Desktop => loc.Get("Hardware.Device.Desktop"),
            DeviceType.Laptop => loc.Get("Hardware.Device.Laptop"),
            DeviceType.Tablet => loc.Get("Hardware.Device.Tablet"),
            DeviceType.Server => loc.Get("Hardware.Device.Server"),
            _ => loc.Get("Hardware.Device.Unknown")
        };
    }

    /// <summary>
    /// 检测设备类型（台式机/笔记本）
    /// </summary>
    private static DeviceType DetectDeviceType()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (var obj in searcher.Get())
            {
                var chassisTypes = obj["ChassisTypes"] as ushort[];
                if (chassisTypes == null || chassisTypes.Length == 0) continue;
                
                foreach (var chassisType in chassisTypes)
                {
                    // ChassisTypes 参考：https://docs.microsoft.com/en-us/windows/win32/cimwin32prov/win32-systemenclosure
                    // 笔记本类型：8=Portable, 9=Laptop, 10=Notebook, 14=Sub Notebook, 31=Convertible, 32=Detachable
                    if (chassisType is 8 or 9 or 10 or 14 or 31 or 32)
                        return DeviceType.Laptop;
                    
                    // 台式机类型：3=Desktop, 4=Low Profile Desktop, 5=Pizza Box, 6=Mini Tower, 7=Tower, 13=All in One, 35=Mini PC
                    if (chassisType is 3 or 4 or 5 or 6 or 7 or 13 or 35)
                        return DeviceType.Desktop;
                    
                    // 平板：30=Tablet
                    if (chassisType == 30)
                        return DeviceType.Tablet;
                    
                    // 服务器：17=Main Server Chassis, 23=Rack Mount Chassis
                    if (chassisType is 17 or 23)
                        return DeviceType.Server;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HardwareInfo] DetectDeviceType error: {ex.Message}");
        }
        
        return DeviceType.Unknown;
    }

    /// <summary>
    /// 检测是否有电池（笔记本判断依据之一）
    /// </summary>
    private static bool DetectBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (var obj in searcher.Get())
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HardwareInfo] DetectBattery error: {ex.Message}");
        }
        
        return false;
    }

    /// <summary>
    /// 检测所有 GPU（支持多显卡场景：核显+独显、双独显等）
    /// </summary>
    private static void DetectAllGpus(HardwareInfo info, IGpuInventoryService gpuInventory)
    {
        try
        {
            var inventory = gpuInventory.GetSnapshot();
            // 保留软件/远程/虚拟适配器并明确其类型，不能静默丢弃后再把其它卡当作唯一可见 GPU。
            foreach (var adapter in inventory.Adapters)
            {
                var gpu = new GpuInfo(
                    adapter.Name,
                    ToShellVendor(adapter.Vendor),
                    adapter.DedicatedMemoryMb,
                    adapter.DriverVersion,
                    adapter.Kind,
                    adapter.ClassificationSource);
                info.AllGpus.Add(gpu);
                info.HasNvidiaGpu |= adapter.Vendor == GpuAdapterVendor.Nvidia;
                info.HasAmdGpu |= adapter.Vendor == GpuAdapterVendor.Amd;
                info.HasIntelGpu |= adapter.Vendor == GpuAdapterVendor.Intel;
            }

            var primaryGpu = info.AllGpus
                .OrderBy(gpu => gpu.Kind == GpuAdapterKind.Discrete ? 0 : gpu.Kind == GpuAdapterKind.Integrated ? 1 : 2)
                .ThenByDescending(gpu => gpu.MemoryMb)
                .FirstOrDefault();

            // 设置主显卡信息
            if (primaryGpu != null)
            {
                info.GpuName = primaryGpu.Name;
                info.GpuMemoryMb = primaryGpu.MemoryMb;
                info.GpuDriverVersion = primaryGpu.DriverVersion;
                info.GpuVendor = primaryGpu.Vendor;
                info.HasDiscreteGpu = primaryGpu.IsDiscrete;
                info.PrimaryGpuKind = primaryGpu.Kind;
            }

            System.Diagnostics.Debug.WriteLine($"[HardwareInfo] GPU Detection Summary: Total={info.AllGpus.Count}, " +
                $"NVIDIA={info.HasNvidiaGpu}, AMD={info.HasAmdGpu}, Intel={info.HasIntelGpu}, Primary={primaryGpu?.Name ?? "None"}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HardwareInfo] DetectAllGpus error: {ex.Message}");
        }
    }

    private static GpuVendor ToShellVendor(GpuAdapterVendor vendor) => vendor switch
    {
        GpuAdapterVendor.Nvidia => GpuVendor.NVIDIA,
        GpuAdapterVendor.Amd => GpuVendor.AMD,
        GpuAdapterVendor.Intel => GpuVendor.Intel,
        _ => GpuVendor.Unknown
    };
}
