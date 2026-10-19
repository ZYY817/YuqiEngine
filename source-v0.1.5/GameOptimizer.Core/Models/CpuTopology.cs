// File: GameOptimizer.Core/Models/CpuTopology.cs
using System;
using System.Collections.Generic;

namespace GameOptimizer.Core.Models;

public enum CpuVendor
{
    Unknown,
    Intel,
    AMD
}

public enum CpuArchitecture
{
    Standard,           // 普通多核CPU
    AmdMultiCcd,        // AMD多CCD（无V-Cache）
    AmdX3D,             // AMD X3D系列（带V-Cache）
    IntelHybrid,        // Intel大小核架构（12/13/14代）
    IntelStandard       // Intel传统架构
}

public enum CoreType
{
    Physical,           // 物理核心
    Smt,                // SMT/超线程核心
    PerformanceCore,    // Intel P-Core
    EfficiencyCore      // Intel E-Core
}

public sealed record CpuAffinityCapability(bool IsSupported, string ReasonCode, string Message);

public class CpuTopology
{
    public string CpuName { get; set; } = string.Empty;
    public CpuVendor Vendor { get; set; } = CpuVendor.Unknown;
    public CpuArchitecture Architecture { get; set; } = CpuArchitecture.Standard;
    public int PhysicalCores { get; set; }
    public int LogicalCores { get; set; }
    public int CcdCount { get; set; } = 1;
    public bool HasVCache { get; set; }
    public bool HasHybridCores { get; set; }
    public bool IsAffinityMaskSupported { get; set; }
    public bool IsDetailedTopologyReliable { get; set; }
    public bool IsVirtualized { get; set; }
    public int ProcessorGroupCount { get; set; } = 1;
    public string CapabilityMessage { get; set; } = string.Empty;
    public List<CcdInfo> Ccds { get; set; } = new();
    public List<CoreInfo> Cores { get; set; } = new();
    
    public string ArchitectureDescription => Architecture switch
    {
        CpuArchitecture.AmdX3D => $"AMD X3D (3D V-Cache, {CcdCount} CCD)",
        CpuArchitecture.AmdMultiCcd => $"AMD 多CCD架构 ({CcdCount} CCD)",
        CpuArchitecture.IntelHybrid => "Intel 混合架构 (P-Core + E-Core)",
        CpuArchitecture.IntelStandard => "Intel 标准架构",
        _ => "标准多核架构"
    };
    
    public string RecommendedConfig => Architecture switch
    {
        CpuArchitecture.AmdX3D => "推荐锁定到V-Cache CCD以获得最佳游戏性能",
        CpuArchitecture.AmdMultiCcd => "推荐锁定到单个CCD以减少跨CCD延迟",
        CpuArchitecture.IntelHybrid => "推荐锁定到P-Core以获得最佳单线程性能",
        _ => "推荐使用物理核心以获得稳定性能"
    };
}

public class CcdInfo
{
    public int CcdIndex { get; set; }
    public bool HasVCache { get; set; }
    public string DisplayName => HasVCache ? $"CCD {CcdIndex} (V-Cache)" : $"CCD {CcdIndex}";
    public List<int> PhysicalCoreIndices { get; set; } = new();
    public List<int> AllCoreIndices { get; set; } = new();
    public int CoreCount => PhysicalCoreIndices.Count;
}

public class CoreInfo
{
    public int Index { get; set; }
    public int PhysicalIndex { get; set; }
    public CoreType Type { get; set; }
    public int CcdIndex { get; set; }
    public bool HasVCache { get; set; }
    public bool IsEnabled { get; set; } = true;
    
    public string DisplayName => Type switch
    {
        CoreType.Smt => $"核心 {Index} (SMT)",
        CoreType.PerformanceCore => $"核心 {Index} (P-Core)",
        CoreType.EfficiencyCore => $"核心 {Index} (E-Core)",
        _ => $"核心 {Index}"
    };
    
    public string ShortName => $"核心 {Index}";
    
    public string TypeDescription => Type switch
    {
        CoreType.Physical => "物理核心",
        CoreType.Smt => "超线程",
        CoreType.PerformanceCore => "性能核心",
        CoreType.EfficiencyCore => "效率核心",
        _ => "未知"
    };
}
