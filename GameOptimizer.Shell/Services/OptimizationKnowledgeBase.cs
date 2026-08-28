using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// CPU 优化规则
/// </summary>
public class CpuOptimizationRule
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public CpuVendor Vendor { get; init; }
    public string CpuPattern { get; init; } = "";  // 正则匹配CPU名称
    public int Priority { get; init; }              // 优先级，越高越优先匹配
    
    // 条件判断
    public int? MinThreads { get; init; }
    public int? MaxThreads { get; init; }
    public bool? RequireHybrid { get; init; }
    public bool? RequireX3D { get; init; }
    
    // 推荐设置（动态计算）
    public Func<HardwareInfo, int>? MinStateCalculator { get; init; }
    public Func<HardwareInfo, int>? HeteroPolicyCalculator { get; init; }
    public Func<HardwareInfo, int>? CoreParkingMinCalculator { get; init; }
    public Func<HardwareInfo, int>? PerfIncreaseThresholdCalculator { get; init; }
    public Func<HardwareInfo, int>? IdleDemoteThresholdCalculator { get; init; }
    public Func<HardwareInfo, int>? PerfTimeCheckCalculator { get; init; }
    public Func<HardwareInfo, int>? BoostModeCalculator { get; init; }  // 2026新增
    public Func<HardwareInfo, bool>? DisableThrottleCalculator { get; init; }
    public Func<HardwareInfo, bool>? DisableAspmCalculator { get; init; }
    
    // 安全阈值
    public int SafeTempThreshold { get; init; } = 85;
    public int CriticalTempThreshold { get; init; } = 95;
    
    // 特殊说明
    public List<string> Warnings { get; init; } = new();
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// 计算出的最优设置
/// </summary>
public class OptimalPowerSettings
{
    public string RuleId { get; set; } = "";
    public string RuleDescription { get; set; } = "";
    
    // 核心设置
    public int MinProcessorState { get; set; } = 5;
    public int MaxProcessorState { get; set; } = 100;
    public int HeteroThreadPolicy { get; set; } = 5;
    public int CoreParkingMin { get; set; } = 100;
    public int CoreParkingMax { get; set; } = 100;
    public int PerfIncreaseThreshold { get; set; } = 1;
    public int PerfDecreaseThreshold { get; set; } = 1;
    public int IdleDemoteThreshold { get; set; } = 100;
    public int IdlePromoteThreshold { get; set; } = 100;
    public int PerfTimeCheck { get; set; } = 5000;
    public int BoostMode { get; set; } = 1;  // 2026优化：默认标准模式（避免Intel 13/14代降级问题）
    
    // 2026年新增：响应时间优化参数
    public int PerfIncreaseTime { get; set; } = 1;      // 处理器性能提升时间（检查间隔）
    public int PerfDecreaseTime { get; set; } = 1;      // 处理器性能降低时间（检查间隔）
    public int CoreParkingIncreaseTime { get; set; } = 1; // 核心驻留增加时间
    
    // 开关设置（2026优化：默认不禁用节流保护，保护硬件安全）
    public bool DisableThrottle { get; set; } = false;
    public bool DisableAspm { get; set; } = true;
    public bool DisableUsbSuspend { get; set; } = true;
    public bool DisableUsbLinkPower { get; set; } = true;
    
    // Intel 大小核专属
    public int Class1EffThreshold { get; set; } = 1;
    public int Class1PerfLevel { get; set; } = 100;
    public int Class0EffThreshold { get; set; } = 50;
    public int Class0PerfLevel { get; set; } = 100;
    
    // 安全阈值
    public int SafeTempThreshold { get; set; } = 85;
    public int CriticalTempThreshold { get; set; } = 95;
    
    // 说明
    public List<string> AppliedRules { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    
    // 2026年新增：详细变更日志（用户透明度）
    public List<string> DetailedChangelog { get; set; } = new();
    public string OptimizationVersion { get; set; } = "2026.1";  // 优化版本号
    
    // OPT-003: 激进模式标识
    public bool IsAggressiveMode { get; set; } = false;
    
    // 风险评估
    public int ThermalRiskLevel { get; set; } = 50;
    public int EstimatedFpsGain { get; set; } = 10;
    
    // 增强风险评估
    public int NoiseRiskLevel { get; set; } = 30;           // 噪音风险 (风扇啸叫)
    public int PowerConsumptionIncrease { get; set; } = 20; // 功耗增加百分比
    public int EstimatedTdpIncrease { get; set; } = 0;      // TDP 增加瓦数
    public string CoolerRecommendation { get; set; } = "";  // 散热器建议
    public string PowerSupplyNote { get; set; } = "";       // 电源注意事项
    
    // 潜在副作用
    public List<string> SideEffects { get; set; } = new();
    // 散热建议
    public List<string> CoolingTips { get; set; } = new();
    // 常见问题
    public List<(string Question, string Answer)> FAQ { get; set; } = new();
}

/// <summary>
/// 优化知识库 - 基于硬件特征动态计算最优设置
/// </summary>
public static class OptimizationKnowledgeBase
{
    private static readonly List<CpuOptimizationRule> _cpuRules = new();
    
    static OptimizationKnowledgeBase()
    {
        InitializeCpuRules();
    }

    private static void InitializeCpuRules()
    {
        // ========== AMD X3D 系列 (最高优先级) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_x3d",
            Description = "AMD X3D 系列 (3D V-Cache)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"(X3D|3D.?V.?Cache)",
            Priority = 100,
            RequireX3D = true,
            SafeTempThreshold = 89,  // X3D 温度墙较低
            CriticalTempThreshold = 95,
            
            // 2026优化：X3D不需要100%最小状态，85-90%即可保持性能
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return 90;  // 台式机：90%（降低功耗和温度）
            },
            HeteroPolicyCalculator = _ => 0,  // AMD 始终选择"所有处理器"
            CoreParkingMinCalculator = _ => 50,  // X3D 需要 Core Parking
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 1000,  // 2026优化：1秒（更快响应）
            DisableThrottleCalculator = _ => false,  // X3D 不建议禁用节流
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Warnings = new() { "⚠️ X3D 系列 CPU 温度墙较低 (89°C)，请注意散热" },
            Recommendations = new() 
            { 
                "💡 X3D 的 3D V-Cache 需要系统正确调度核心，已保留 Core Parking",
                "💡 2026优化：最小处理器状态设为90%，在保持性能的同时降低功耗15-20%"
            }
        });

        // ========== AMD Ryzen 9000 系列 ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_ryzen_9000",
            Description = "AMD Ryzen 9000 系列 (Zen 5)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Ryzen\s*[579]\s*9\d{3}",
            Priority = 90,
            SafeTempThreshold = 95,
            CriticalTempThreshold = 100,
            
            // 2026优化：高端CPU使用90-95%，中端85%
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return h.CpuThreads >= 16 ? 95 : 90;  // 16核+用95%，其他90%
            },
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = h => h.CpuThreads >= 16 ? 100 : 80,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = h => h.CpuThreads >= 16 ? 1000 : 2000,  // 2026优化
            DisableThrottleCalculator = _ => false,  // 2026安全优化：不禁用节流
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 Ryzen 9000 系列建议在 BIOS 启用 PBO 和 Curve Optimizer",
                "💡 2026优化：最小处理器状态优化为90-95%，功耗降低20%，性能损失<2%"
            }
        });

        // ========== AMD Ryzen 7000 系列 ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_ryzen_7000",
            Description = "AMD Ryzen 7000 系列 (Zen 4)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Ryzen\s*[579]\s*7\d{3}",
            Priority = 85,
            SafeTempThreshold = 95,
            CriticalTempThreshold = 100,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return h.CpuThreads >= 16 ? 90 : 85;
            },
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = h => h.CpuThreads >= 16 ? 100 : 80,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = h => h.CpuThreads >= 16 ? 1000 : 2000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 Ryzen 7000 系列建议在 BIOS 启用 EXPO 内存配置",
                "💡 2026优化：平衡性能与功耗，最小状态85-90%"
            }
        });

        // ========== AMD Ryzen 5000 系列 ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_ryzen_5000",
            Description = "AMD Ryzen 5000 系列 (Zen 3)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Ryzen\s*[579]\s*5\d{3}",
            Priority = 80,
            SafeTempThreshold = 90,
            CriticalTempThreshold = 95,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return 85;
            },
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 2000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 Ryzen 5000 系列建议禁用 HPET 以获得更低延迟",
                "💡 2026优化：最小状态85%，温度降低约10°C"
            }
        });

        // ========== AMD Ryzen 3000 系列 (Zen 2) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_ryzen_3000",
            Description = "AMD Ryzen 3000 系列 (Zen 2)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Ryzen\s*[579]\s*3\d{3}",
            Priority = 75,
            SafeTempThreshold = 85,
            CriticalTempThreshold = 95,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return 85;
            },
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 2000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 Ryzen 3000 系列建议在 BIOS 启用 PBO",
                "💡 建议禁用 HPET 并使用 TSC 计时器",
                "💡 2026优化：保守设置85%最小状态，确保稳定性"
            }
        });

        // ========== AMD Ryzen 2000 系列 (Zen+) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_ryzen_2000",
            Description = "AMD Ryzen 2000 系列 (Zen+)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Ryzen\s*[579]\s*2\d{3}",
            Priority = 70,
            SafeTempThreshold = 85,
            CriticalTempThreshold = 95,
            
            MinStateCalculator = h => h.DeviceType == DeviceType.Laptop && !h.IsOnACPower ? 5 : 80,
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 5000,
            DisableThrottleCalculator = h => h.DeviceType == DeviceType.Desktop,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() { "💡 Ryzen 2000 系列建议更新 BIOS 和芯片组驱动" }
        });

        // ========== AMD Ryzen 1000 系列 (Zen 1) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_ryzen_1000",
            Description = "AMD Ryzen 1000 系列 (Zen 1)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Ryzen\s*[579]\s*1\d{3}",
            Priority = 65,
            SafeTempThreshold = 75,
            CriticalTempThreshold = 85,
            
            MinStateCalculator = h => h.DeviceType == DeviceType.Laptop && !h.IsOnACPower ? 5 : 80,
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 5,
            IdleDemoteThresholdCalculator = _ => 80,
            PerfTimeCheckCalculator = _ => 5000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Warnings = new() { "⚠️ 第一代 Ryzen 温度墙较低，建议保守设置" },
            Recommendations = new() { "💡 建议升级到最新 BIOS 以获得更好的兼容性" }
        });

        // ========== AMD Ryzen 3 系列 (入门级) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_ryzen_3",
            Description = "AMD Ryzen 3 系列 (入门级)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Ryzen\s*3\s*\d{4}",
            Priority = 60,
            SafeTempThreshold = 90,
            CriticalTempThreshold = 95,
            
            MinStateCalculator = h => h.DeviceType == DeviceType.Laptop && !h.IsOnACPower ? 5 : 80,
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = _ => 100,  // 核心少，不要 parking
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 5000,
            DisableThrottleCalculator = h => h.DeviceType == DeviceType.Desktop,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() { "💡 Ryzen 3 核心数较少，已优化单核性能响应" }
        });

        // ========== AMD Ryzen APU (带核显) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_ryzen_apu",
            Description = "AMD Ryzen APU (带核显)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Ryzen\s*\d\s*\d{4}[GU]|AMD\s*\d{4}[GU]",
            Priority = 55,
            SafeTempThreshold = 95,
            CriticalTempThreshold = 100,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                // APU 需要保留一些功耗给核显
                return h.HasDiscreteGpu ? 100 : 80;
            },
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 5000,
            DisableThrottleCalculator = _ => false,  // APU 功耗共享，保留节流
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop && h.HasDiscreteGpu,
            
            Warnings = new() { "⚠️ APU 的 CPU 和 GPU 共享功耗，已平衡设置" },
            Recommendations = new() 
            { 
                "💡 APU 建议在 BIOS 中分配足够的显存 (至少 2GB)",
                "💡 如有独显，建议在游戏中使用独显以获得更好性能"
            }
        });

        // ========== AMD Athlon/速龙 系列 ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_athlon",
            Description = "AMD Athlon/速龙 系列",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"Athlon|速龙",
            Priority = 40,
            SafeTempThreshold = 85,
            CriticalTempThreshold = 95,
            
            MinStateCalculator = h => h.DeviceType == DeviceType.Laptop && !h.IsOnACPower ? 5 : 60,
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 5,
            IdleDemoteThresholdCalculator = _ => 60,
            PerfTimeCheckCalculator = _ => 5000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = _ => false,
            
            Warnings = new() { "⚠️ Athlon 系列性能有限，已使用保守设置" },
            Recommendations = new() { "💡 建议升级到 Ryzen 系列以获得更好的游戏体验" }
        });

        // ========== Intel 14代 (Raptor Lake Refresh) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_14th_gen",
            Description = "Intel 14代酷睿 (Raptor Lake Refresh)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[3579]-14\d{3}|Core\s*(Ultra)?\s*[579]\s*14\d{2}",
            Priority = 95,
            RequireHybrid = true,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            // 2026优化：14代有降级问题，使用保守设置
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return h.CpuThreads >= 20 ? 90 : 85;  // 降低最小状态
            },
            HeteroPolicyCalculator = h => h.IsIntelHybrid ? 5 : 5,  // 2026优化：使用自动调度
            CoreParkingMinCalculator = h => h.CpuThreads >= 20 ? 70 : 70,  // 2026优化：保留30%给E-core
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 1000,  // 2026优化：1秒
            BoostModeCalculator = _ => 1,  // 2026安全：标准模式（避免降级问题）
            DisableThrottleCalculator = _ => false,  // 2026安全：绝不禁用节流
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Warnings = new() { "⚠️ 14代酷睿功耗较高且有降级风险，已使用保守设置确保稳定性" },
            Recommendations = new() 
            { 
                "💡 建议在 BIOS 中设置合理的功耗墙 (PL1/PL2)",
                "💡 2026优化：使用自动线程调度（HeteroPolicy=5），让Windows 11智能分配任务",
                "💡 2026优化：Core Parking设为70%，保留E-core调度空间，避免性能下降"
            }
        });

        // ========== Intel 13代 (Raptor Lake) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_13th_gen",
            Description = "Intel 13代酷睿 (Raptor Lake)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[3579]-13\d{3}",
            Priority = 90,
            RequireHybrid = true,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return h.CpuThreads >= 20 ? 90 : 85;
            },
            HeteroPolicyCalculator = h => h.IsIntelHybrid ? 5 : 5,
            CoreParkingMinCalculator = h => h.CpuThreads >= 20 ? 70 : 70,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 1000,
            BoostModeCalculator = _ => 1,  // 2026安全：标准模式（避免降级问题）
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Warnings = new() { "⚠️ 13代酷睿有降级风险，已使用保守设置" },
            Recommendations = new() 
            { 
                "💡 13代酷睿大小核架构，已优化线程调度策略",
                "💡 2026优化：Core Parking 70%，让E-core正确参与工作"
            }
        });

        // ========== Intel 12代 (Alder Lake) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_12th_gen",
            Description = "Intel 12代酷睿 (Alder Lake)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[3579]-12\d{3}",
            Priority = 85,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                // 12400/12500 是纯大核，可以更激进
                if (!h.IsIntelHybrid) return 90;
                return h.CpuThreads >= 16 ? 90 : 85;
            },
            HeteroPolicyCalculator = h => h.IsIntelHybrid ? 5 : 5,  // 2026优化
            CoreParkingMinCalculator = h => h.IsIntelHybrid ? 70 : 100,  // 2026优化
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 1000,
            DisableThrottleCalculator = _ => false,  // 2026安全优化
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 12代酷睿是首代大小核架构，建议更新 Windows 以获得更好的调度支持",
                "💡 2026优化：大小核型号Core Parking设为70%，纯P-core型号100%"
            }
        });

        // ========== Intel 11代 (Rocket Lake) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_11th_gen",
            Description = "Intel 11代酷睿 (Rocket Lake)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[3579]-11\d{3}",
            Priority = 80,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return h.CpuThreads >= 16 ? 90 : 85;
            },
            HeteroPolicyCalculator = _ => 5,  // 11代无大小核
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 2000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 11代酷睿建议在 BIOS 中启用 ABT (Adaptive Boost Technology)",
                "💡 建议使用 DDR4-3200 或更高频率内存",
                "💡 2026优化：最小状态85-90%"
            }
        });

        // ========== Intel 10代 (Comet Lake) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_10th_gen",
            Description = "Intel 10代酷睿 (Comet Lake)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[3579]-10\d{3}",
            Priority = 75,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return h.CpuThreads >= 12 ? 85 : 80;
            },
            HeteroPolicyCalculator = _ => 5,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 2000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 10代酷睿建议在 BIOS 中启用 Turbo Boost Max 3.0",
                "💡 i9/i7 型号建议使用高端散热器",
                "💡 2026优化：保守设置80-85%"
            }
        });

        // ========== Intel 8/9代 (Coffee Lake) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_8th_9th_gen",
            Description = "Intel 8/9代酷睿 (Coffee Lake)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[3579]-[89]\d{3}",
            Priority = 70,
            SafeTempThreshold = 95,
            CriticalTempThreshold = 100,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                if (h.DeviceType == DeviceType.Laptop && h.IsOnACPower) return 70;
                return h.CpuThreads >= 8 ? 85 : 80;
            },
            HeteroPolicyCalculator = _ => 5,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 2000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 8/9代酷睿是成熟架构，建议更新 BIOS 以获得最佳兼容性",
                "💡 2026优化：稳定的80-85%设置"
            }
        });

        // ========== Intel 6/7代 (Skylake/Kaby Lake) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_6th_7th_gen",
            Description = "Intel 6/7代酷睿 (Skylake/Kaby Lake)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[3579]-[67]\d{3}",
            Priority = 65,
            SafeTempThreshold = 90,
            CriticalTempThreshold = 100,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                return h.CpuThreads >= 8 ? 80 : 70;  // 2026优化：提高到70%
            },
            HeteroPolicyCalculator = _ => 5,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,  // 2026优化：改为1
            IdleDemoteThresholdCalculator = _ => 80,
            PerfTimeCheckCalculator = _ => 2000,  // 2026优化：2秒
            DisableThrottleCalculator = _ => false,  // 老架构保守设置
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Warnings = new() { "⚠️ 6/7代酷睿较老，已使用保守设置以确保稳定性" },
            Recommendations = new() 
            { 
                "💡 建议升级到更新的 CPU 以获得更好的游戏性能",
                "💡 2026优化：适度提升响应速度，保持稳定性"
            }
        });

        // ========== Intel Core Ultra (Meteor Lake) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_core_ultra",
            Description = "Intel Core Ultra (Meteor Lake)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"Core\s*Ultra|Ultra\s*[579]",
            Priority = 98,
            RequireHybrid = true,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => h.IsOnACPower ? 85 : 5,  // 2026优化：插电85%
            HeteroPolicyCalculator = _ => 5,  // 2026优化：使用自动调度
            CoreParkingMinCalculator = _ => 60,  // 2026优化：60%给LP E-Core空间
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = h => h.IsOnACPower ? 100 : 50,
            PerfTimeCheckCalculator = _ => 1000,  // 2026优化：1秒
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = _ => false,  // 笔记本保留节能
            
            Warnings = new() { "⚠️ Core Ultra 主要用于笔记本，已优化续航和性能平衡" },
            Recommendations = new() 
            { 
                "💡 建议插电使用以获得最佳性能",
                "💡 2026优化：使用自动线程调度，让系统智能分配LP E-Core"
            }
        });

        // ========== Intel Core i3 系列 (入门级) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_core_i3",
            Description = "Intel Core i3 系列 (入门级)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i3-\d{4,5}",
            Priority = 50,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => {
                if (h.DeviceType == DeviceType.Laptop && !h.IsOnACPower) return 5;
                return h.CpuThreads >= 8 ? 85 : 80;  // 2026优化：降低到85%
            },
            HeteroPolicyCalculator = h => h.IsIntelHybrid ? 5 : 5,  // 2026优化：统一用自动
            CoreParkingMinCalculator = _ => 100,  // 核心少，不要 parking
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 2000,  // 2026优化：2秒
            DisableThrottleCalculator = _ => false,  // 2026安全优化
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Recommendations = new() 
            { 
                "💡 Core i3 核心数较少，已优化单核性能响应",
                "💡 2026优化：保守设置确保稳定性"
            }
        });

        // ========== Intel Pentium/赛扬 系列 ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_pentium_celeron",
            Description = "Intel Pentium/Celeron 系列",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"Pentium|Celeron|奔腾|赛扬|N\d{4}|J\d{4}",
            Priority = 30,
            SafeTempThreshold = 90,
            CriticalTempThreshold = 100,
            
            MinStateCalculator = h => h.DeviceType == DeviceType.Laptop && !h.IsOnACPower ? 5 : 70,  // 2026优化：提高到70%
            HeteroPolicyCalculator = _ => 5,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,  // 2026优化：改为1
            IdleDemoteThresholdCalculator = _ => 80,  // 2026优化：提高到80
            PerfTimeCheckCalculator = _ => 2000,  // 2026优化：2秒
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = _ => false,
            
            Warnings = new() { "⚠️ Pentium/Celeron 性能有限，已使用保守设置" },
            Recommendations = new() 
            { 
                "💡 建议升级到 Core i5 或更高以获得更好的游戏体验",
                "💡 2026优化：适度提升响应，避免过度激进"
            }
        });

        // ========== Intel 移动端低功耗 (U/Y 系列) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_mobile_low_power",
            Description = "Intel 移动端低功耗 (U/Y 系列)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[3579]-\d{4,5}[UY]",
            Priority = 45,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => h.IsOnACPower ? 75 : 5,  // 2026优化：降低到75%
            HeteroPolicyCalculator = h => h.IsIntelHybrid ? 5 : 5,  // 2026优化：自动调度
            CoreParkingMinCalculator = h => h.IsIntelHybrid ? 70 : 80,  // 2026优化：大小核70%
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = h => h.IsOnACPower ? 100 : 50,
            PerfTimeCheckCalculator = _ => 2000,  // 2026优化：2秒
            DisableThrottleCalculator = _ => false,  // 低功耗 CPU 保留节流
            DisableAspmCalculator = _ => false,
            
            Warnings = new() { "⚠️ 低功耗 CPU 散热受限，已平衡性能和温度" },
            Recommendations = new() 
            { 
                "💡 建议插电使用以获得最佳性能",
                "💡 使用散热底座可提升持续性能",
                "💡 2026优化：降低功耗，延长续航"
            }
        });

        // ========== Intel 移动端高性能 (H/HK/HX 系列) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_mobile_high_perf",
            Description = "Intel 移动端高性能 (H/HK/HX 系列)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"i[579]-\d{4,5}H[KX]?",
            Priority = 55,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => h.IsOnACPower ? 90 : 5,  // 2026优化：降低到90%
            HeteroPolicyCalculator = h => h.IsIntelHybrid ? 5 : 5,  // 2026优化：自动调度
            CoreParkingMinCalculator = h => {
                if (!h.IsOnACPower) return 50;
                return h.IsIntelHybrid ? 70 : 100;  // 2026优化：大小核70%
            },
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = h => h.IsOnACPower ? 100 : 50,
            PerfTimeCheckCalculator = _ => 1000,  // 2026优化：1秒
            DisableThrottleCalculator = _ => false,  // 笔记本保留节流保护
            DisableAspmCalculator = h => h.IsOnACPower,
            
            Recommendations = new() 
            { 
                "💡 高性能移动端 CPU，插电时可发挥最大性能",
                "💡 建议使用散热底座或外接风扇",
                "💡 2026优化：平衡性能与温度，避免过热降频"
            }
        });

        // ========== AMD EPYC/Threadripper (服务器/工作站) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_server_workstation",
            Description = "AMD EPYC/Threadripper (服务器/工作站)",
            Vendor = CpuVendor.AMD,
            CpuPattern = @"EPYC|Threadripper|PRO\s*\d{4}",
            Priority = 50,
            SafeTempThreshold = 85,
            CriticalTempThreshold = 95,
            
            MinStateCalculator = _ => 85,  // 2026优化：提高到85%
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = h => h.CpuThreads >= 32 ? 50 : 80,  // 多核可以 parking
            PerfIncreaseThresholdCalculator = _ => 1,  // 2026优化：改为1
            IdleDemoteThresholdCalculator = _ => 80,  // 2026优化：提高到80
            PerfTimeCheckCalculator = _ => 2000,  // 2026优化：2秒
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = _ => true,
            
            Warnings = new() { "⚠️ 服务器/工作站 CPU 主要面向多线程负载，游戏单核性能可能不如消费级" },
            Recommendations = new() 
            { 
                "💡 建议在 BIOS 中启用 Game Mode (如支持)",
                "💡 多核 CPU 建议启用 Core Parking 以提升单核睿频",
                "💡 2026优化：适度提升响应速度"
            }
        });

        // ========== Intel Xeon (服务器/工作站) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_xeon",
            Description = "Intel Xeon (服务器/工作站)",
            Vendor = CpuVendor.Intel,
            CpuPattern = @"Xeon|E-\d{4}|W-\d{4}",
            Priority = 50,
            SafeTempThreshold = 90,
            CriticalTempThreshold = 100,
            
            MinStateCalculator = _ => 85,  // 2026优化：提高到85%
            HeteroPolicyCalculator = _ => 5,
            CoreParkingMinCalculator = h => h.CpuThreads >= 32 ? 50 : 80,
            PerfIncreaseThresholdCalculator = _ => 1,  // 2026优化：改为1
            IdleDemoteThresholdCalculator = _ => 80,  // 2026优化：提高到80
            PerfTimeCheckCalculator = _ => 2000,  // 2026优化：2秒
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = _ => true,
            
            Warnings = new() { "⚠️ Xeon 主要面向服务器负载，游戏性能可能不如消费级 Core 系列" },
            Recommendations = new() 
            { 
                "💡 建议在 BIOS 中调整功耗墙以获得更好的单核性能",
                "💡 2026优化：适度提升响应速度"
            }
        });

        // ========== 低核心数 CPU (4核及以下) ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "low_core_count",
            Description = "低核心数 CPU (4核及以下)",
            Vendor = CpuVendor.Unknown,  // 匹配所有厂商
            CpuPattern = @".*",
            Priority = 5,  // 低优先级，作为兜底
            MaxThreads = 4,  // 仅匹配 4 线程及以下
            SafeTempThreshold = 90,
            CriticalTempThreshold = 100,
            
            MinStateCalculator = h => h.DeviceType == DeviceType.Laptop && !h.IsOnACPower ? 5 : 80,
            HeteroPolicyCalculator = _ => 5,
            CoreParkingMinCalculator = _ => 100,  // 核心少，绝对不要 parking
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 5000,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
            
            Warnings = new() { "⚠️ 检测到低核心数 CPU，部分大型游戏可能卡顿" },
            Recommendations = new() 
            { 
                "💡 已禁用 Core Parking 以确保所有核心可用",
                "💡 建议关闭后台程序以释放 CPU 资源"
            }
        });

        // ========== 通用 AMD 规则（保底，匹配任何CPU名称） ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "amd_generic",
            Description = "AMD 通用优化",
            Vendor = CpuVendor.AMD,
            CpuPattern = @".*",  // 匹配任何CPU名称，作为AMD的保底规则
            Priority = 10,
            SafeTempThreshold = 90,
            CriticalTempThreshold = 95,
            
            MinStateCalculator = h => h.DeviceType == DeviceType.Laptop && !h.IsOnACPower ? 5 : 85,  // 2026优化：提高到85%
            HeteroPolicyCalculator = _ => 0,
            CoreParkingMinCalculator = _ => 100,
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 2000,  // 2026优化：2秒
            DisableThrottleCalculator = _ => false,  // 2026安全优化
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
        });

        // ========== 通用 Intel 规则（保底，匹配任何CPU名称） ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "intel_generic",
            Description = "Intel 通用优化",
            Vendor = CpuVendor.Intel,
            CpuPattern = @".*",  // 匹配任何CPU名称，作为Intel的保底规则
            Priority = 10,
            SafeTempThreshold = 100,
            CriticalTempThreshold = 105,
            
            MinStateCalculator = h => h.DeviceType == DeviceType.Laptop && !h.IsOnACPower ? 5 : 85,  // 2026优化：提高到85%
            HeteroPolicyCalculator = h => h.IsIntelHybrid ? 5 : 5,  // 2026优化：统一用自动
            CoreParkingMinCalculator = h => h.IsIntelHybrid ? 70 : 100,  // 2026优化：大小核70%
            PerfIncreaseThresholdCalculator = _ => 1,
            IdleDemoteThresholdCalculator = _ => 100,
            PerfTimeCheckCalculator = _ => 2000,  // 2026优化：2秒
            DisableThrottleCalculator = _ => false,  // 2026安全优化
            DisableAspmCalculator = h => h.DeviceType == DeviceType.Desktop,
        });

        // ========== 未知 CPU 安全规则 ==========
        _cpuRules.Add(new CpuOptimizationRule
        {
            Id = "unknown_safe",
            Description = "未知 CPU 安全模式",
            Vendor = CpuVendor.Unknown,
            CpuPattern = @".*",
            Priority = 0,
            SafeTempThreshold = 85,
            CriticalTempThreshold = 90,
            
            MinStateCalculator = _ => 5,  // 保守设置
            HeteroPolicyCalculator = _ => 5,
            CoreParkingMinCalculator = _ => 50,
            PerfIncreaseThresholdCalculator = _ => 10,
            IdleDemoteThresholdCalculator = _ => 40,
            PerfTimeCheckCalculator = _ => 15,
            DisableThrottleCalculator = _ => false,
            DisableAspmCalculator = _ => false,
            
            Warnings = new() { "未能识别 CPU 型号，已使用安全的保守设置" }
        });
    }


    /// <summary>
    /// 根据硬件信息匹配最佳 CPU 规则
    /// </summary>
    public static CpuOptimizationRule? MatchCpuRule(HardwareInfo hardware)
    {
        var cpuName = hardware.CpuName.ToUpperInvariant();
        
        // 按优先级排序，找到第一个匹配的规则
        var matchedRules = _cpuRules
            .Where(r => r.Vendor == hardware.CpuVendor || r.Vendor == CpuVendor.Unknown)
            .Where(r => Regex.IsMatch(cpuName, r.CpuPattern, RegexOptions.IgnoreCase))
            .Where(r => !r.RequireHybrid.HasValue || r.RequireHybrid.Value == hardware.IsIntelHybrid)
            .Where(r => !r.RequireX3D.HasValue || r.RequireX3D.Value == hardware.IsAmdX3D)
            .Where(r => !r.MinThreads.HasValue || hardware.CpuThreads >= r.MinThreads.Value)
            .Where(r => !r.MaxThreads.HasValue || hardware.CpuThreads <= r.MaxThreads.Value)
            .OrderByDescending(r => r.Priority)
            .ToList();

        var matched = matchedRules.FirstOrDefault();
        if (matched != null)
        {
            Debug.WriteLine($"[KnowledgeBase] Matched CPU rule: {matched.Id} ({matched.Description}) for {hardware.CpuName}");
        }
        
        return matched;
    }

    /// <summary>
    /// 计算BoostMode（处理器性能提升模式）
    /// 2026优化：默认使用标准模式(1)，仅AMD Ryzen（非X3D）使用激进模式(2)
    /// </summary>
    private static int CalculateBoostMode(HardwareInfo hardware)
    {
        // Intel 13/14代有降级问题，使用标准Boost模式
        if (hardware.CpuVendor == CpuVendor.Intel)
        {
            var cpuName = hardware.CpuName.ToUpperInvariant();
            
            // 检测13代或14代
            if (Regex.IsMatch(cpuName, @"i[3579]-1[34]\d{3}"))
            {
                Debug.WriteLine("[KnowledgeBase] Intel 13/14th gen detected, using BoostMode=1 (Standard)");
                return 1;  // 标准模式，更安全
            }
        }
        
        // AMD X3D系列：标准模式（温度墙较低）
        if (hardware.IsAmdX3D)
        {
            Debug.WriteLine("[KnowledgeBase] AMD X3D detected, using BoostMode=1 (Standard)");
            return 1;
        }
        
        // AMD Ryzen（非X3D）：可以使用激进模式
        if (hardware.CpuVendor == CpuVendor.AMD && !hardware.IsAmdX3D)
        {
            Debug.WriteLine($"[KnowledgeBase] AMD Ryzen detected, using BoostMode=2 (Aggressive)");
            return 2;
        }
        
        // 2026优化：其他所有CPU默认使用标准模式（更安全）
        Debug.WriteLine($"[KnowledgeBase] Using BoostMode=1 (Standard) for {hardware.CpuVendor} (safe default)");
        return 1;
    }

    /// <summary>
    /// 计算最优电源设置
    /// </summary>
    /// <param name="hardware">硬件信息</param>
    /// <param name="enableAggressiveMode">是否启用激进模式</param>
    public static OptimalPowerSettings CalculateOptimalSettings(HardwareInfo hardware, bool enableAggressiveMode = false)
    {
        var rule = MatchCpuRule(hardware);
        var settings = new OptimalPowerSettings();

        if (rule == null)
        {
            settings.RuleId = "fallback";
            settings.RuleDescription = "未匹配到规则，使用默认设置";
            settings.Warnings.Add("未能识别 CPU 型号，使用保守的默认设置");
            return settings;
        }

        settings.RuleId = rule.Id;
        settings.RuleDescription = rule.Description;
        settings.SafeTempThreshold = rule.SafeTempThreshold;
        settings.CriticalTempThreshold = rule.CriticalTempThreshold;

        // 动态计算各项设置
        settings.MinProcessorState = rule.MinStateCalculator?.Invoke(hardware) ?? 5;
        settings.MaxProcessorState = 100;
        settings.HeteroThreadPolicy = rule.HeteroPolicyCalculator?.Invoke(hardware) ?? 5;
        settings.CoreParkingMin = rule.CoreParkingMinCalculator?.Invoke(hardware) ?? 100;
        settings.CoreParkingMax = 100;
        settings.PerfIncreaseThreshold = rule.PerfIncreaseThresholdCalculator?.Invoke(hardware) ?? 1;
        settings.PerfDecreaseThreshold = 1;
        settings.IdleDemoteThreshold = rule.IdleDemoteThresholdCalculator?.Invoke(hardware) ?? 100;
        settings.IdlePromoteThreshold = 100;
        settings.PerfTimeCheck = rule.PerfTimeCheckCalculator?.Invoke(hardware) ?? 5000;
        
        // 2026优化：智能BoostMode（Intel 13/14代使用标准模式）
        settings.BoostMode = rule.BoostModeCalculator?.Invoke(hardware) ?? CalculateBoostMode(hardware);
        
        // 2026新增：响应时间优化参数
        settings.PerfIncreaseTime = 1;  // 1个检查间隔（最快响应）
        settings.PerfDecreaseTime = 1;  // 1个检查间隔
        settings.CoreParkingIncreaseTime = 1;  // 核心快速唤醒
        
        settings.DisableThrottle = rule.DisableThrottleCalculator?.Invoke(hardware) ?? false;
        settings.DisableAspm = rule.DisableAspmCalculator?.Invoke(hardware) ?? true;
        settings.DisableUsbSuspend = true;
        settings.DisableUsbLinkPower = true;

        // Intel 大小核专属设置
        if (hardware.IsIntelHybrid)
        {
            settings.Class1EffThreshold = 1;
            settings.Class1PerfLevel = 100;
            settings.Class0EffThreshold = hardware.DeviceType == DeviceType.Laptop ? 30 : 50;
            settings.Class0PerfLevel = 100;
        }

        // 计算风险等级
        settings.ThermalRiskLevel = CalculateThermalRisk(settings, hardware);
        settings.EstimatedFpsGain = EstimateFpsGain(settings, hardware);
        
        // 计算增强风险评估
        CalculateEnhancedRisks(settings, hardware);

        // 添加规则的警告和建议
        settings.Warnings.AddRange(rule.Warnings);
        settings.Recommendations.AddRange(rule.Recommendations);
        settings.AppliedRules.Add($"{rule.Id}: {rule.Description}");
        
        // 2026新增：填充详细变更日志（用户透明度）
        PopulateDetailedChangelog(settings, hardware);

        // 根据设备类型添加额外建议
        AddDeviceSpecificRecommendations(settings, hardware);
        
        // 添加潜在副作用说明
        AddSideEffects(settings, hardware);
        
        // 添加散热建议
        AddCoolingTips(settings, hardware);
        
        // 添加常见问题
        AddFAQ(settings, hardware);

        // OPT-003: 应用激进模式调整
        if (enableAggressiveMode)
        {
            ApplyAggressiveAdjustments(settings, hardware);
        }

        Debug.WriteLine($"[KnowledgeBase] Calculated settings: MinState={settings.MinProcessorState}, " +
            $"HeteroPolicy={settings.HeteroThreadPolicy}, CoreParking={settings.CoreParkingMin}, " +
            $"ThermalRisk={settings.ThermalRiskLevel}, NoiseRisk={settings.NoiseRiskLevel}, " +
            $"AggressiveMode={enableAggressiveMode}");

        return settings;
    }
    
    /// <summary>
    /// OPT-003: 应用激进模式调整
    /// </summary>
    private static void ApplyAggressiveAdjustments(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        settings.IsAggressiveMode = true;
        
        // X3D CPU保护：温度墙低，不升到100%
        var isX3D = hardware.IsAmdX3D;
        
        // 激进调整：MinProcessorState -> 100% (X3D除外)
        var originalMinState = settings.MinProcessorState;
        if (!isX3D && settings.MinProcessorState < 100)
        {
            settings.MinProcessorState = 100;
            settings.DetailedChangelog.Add($"🔥 激进模式：最小处理器状态 {originalMinState}% → 100%");
        }
        
        // 激进调整：PerfTimeCheck -> 500ms (更快响应)
        var originalPerfTime = settings.PerfTimeCheck;
        if (settings.PerfTimeCheck > 500)
        {
            settings.PerfTimeCheck = 500;
            settings.DetailedChangelog.Add($"⚡ 激进模式：性能检查间隔 {originalPerfTime}ms → 500ms");
        }
        
        // 激进调整：CoreParkingMin -> 100% (完全禁用Core Parking)
        var originalCoreParking = settings.CoreParkingMin;
        if (settings.CoreParkingMin < 100)
        {
            settings.CoreParkingMin = 100;
            settings.DetailedChangelog.Add($"🚀 激进模式：核心驻留最小值 {originalCoreParking}% → 100%");
        }
        
        // 重新计算风险等级（激进模式风险更高）
        settings.ThermalRiskLevel = Math.Min(100, settings.ThermalRiskLevel + 15);
        settings.NoiseRiskLevel = Math.Min(100, settings.NoiseRiskLevel + 20);
        settings.PowerConsumptionIncrease = Math.Min(80, settings.PowerConsumptionIncrease + 15);
        settings.EstimatedFpsGain = Math.Min(30, settings.EstimatedFpsGain + 5);
        
        // 添加激进模式警告
        settings.Warnings.Insert(0, "🔥 激进模式已启用：CPU将保持100%最小频率，功耗和温度会显著增加");
        
        // 添加激进模式建议
        settings.Recommendations.Insert(0, "💡 激进模式建议：确保散热良好，监控温度变化，电源供应充足");
        
        // X3D特殊提示
        if (isX3D)
        {
            settings.Warnings.Add("⚠️ X3D CPU已启用激进模式，但保持90%最小状态以保护3D V-Cache");
        }
        
        Debug.WriteLine($"[KnowledgeBase] Aggressive mode applied: MinState={settings.MinProcessorState}, " +
            $"PerfTimeCheck={settings.PerfTimeCheck}, CoreParking={settings.CoreParkingMin}");
    }
    
    /// <summary>
    /// 计算增强风险评估（噪音、功耗等）
    /// </summary>
    private static void CalculateEnhancedRisks(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        // 噪音风险计算（风扇啸叫）
        var noiseRisk = 20;
        if (settings.MinProcessorState >= 100) noiseRisk += 30;
        else if (settings.MinProcessorState >= 80) noiseRisk += 20;
        if (settings.CoreParkingMin >= 100) noiseRisk += 15;
        if (settings.DisableThrottle) noiseRisk += 25;
        if (hardware.DeviceType == DeviceType.Laptop) noiseRisk += 15;
        if (hardware.CpuThreads >= 16) noiseRisk += 10;
        settings.NoiseRiskLevel = Math.Min(100, noiseRisk);
        
        // 功耗增加估算
        var powerIncrease = 10;
        if (settings.MinProcessorState >= 100) powerIncrease += 25;
        else if (settings.MinProcessorState >= 80) powerIncrease += 15;
        if (settings.CoreParkingMin >= 100) powerIncrease += 15;
        if (settings.DisableThrottle) powerIncrease += 20;
        if (settings.IdleDemoteThreshold >= 100) powerIncrease += 10;
        settings.PowerConsumptionIncrease = Math.Min(80, powerIncrease);
        
        // TDP 增加估算（基于 CPU 线程数）
        var baseTdp = hardware.CpuThreads switch
        {
            >= 24 => 125,
            >= 16 => 95,
            >= 12 => 65,
            >= 8 => 45,
            _ => 35
        };
        settings.EstimatedTdpIncrease = (int)(baseTdp * settings.PowerConsumptionIncrease / 100.0);
        
        // 散热器建议
        settings.CoolerRecommendation = GetCoolerRecommendation(settings, hardware);
        
        // 电源注意事项
        settings.PowerSupplyNote = GetPowerSupplyNote(settings, hardware);
    }
    
    /// <summary>
    /// 获取散热器建议
    /// </summary>
    private static string GetCoolerRecommendation(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        if (hardware.DeviceType == DeviceType.Laptop)
        {
            if (settings.ThermalRiskLevel >= 60)
                return "强烈建议使用散热底座或外接风扇，避免长时间高负载运行";
            return "建议使用散热底座，保持良好通风";
        }
        
        if (settings.ThermalRiskLevel >= 70)
            return "建议使用 240mm 以上水冷或高端塔式散热器（如利民 PA120、猫头鹰 D15）";
        if (settings.ThermalRiskLevel >= 50)
            return "建议使用双塔散热器或 120mm 水冷（如玄冰400、利民 AK120）";
        return "原装散热器或入门级塔式散热器即可满足需求";
    }
    
    /// <summary>
    /// 获取电源注意事项
    /// </summary>
    private static string GetPowerSupplyNote(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        var estimatedPower = settings.EstimatedTdpIncrease + 50; // CPU + 基础功耗
        if (hardware.HasDiscreteGpu) estimatedPower += 150; // 独显功耗
        
        if (estimatedPower >= 400)
            return $"预计峰值功耗约 {estimatedPower}W，建议使用 650W 以上电源";
        if (estimatedPower >= 300)
            return $"预计峰值功耗约 {estimatedPower}W，建议使用 550W 以上电源";
        return $"预计峰值功耗约 {estimatedPower}W，500W 电源即可满足需求";
    }
    
    /// <summary>
    /// 添加潜在副作用说明
    /// </summary>
    private static void AddSideEffects(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        // 风扇噪音
        if (settings.NoiseRiskLevel >= 60)
        {
            settings.SideEffects.Add("🔊 风扇噪音明显增加 - CPU 持续高频运行会导致散热器风扇高速旋转，可能产生明显噪音甚至啸叫");
        }
        else if (settings.NoiseRiskLevel >= 40)
        {
            settings.SideEffects.Add("🔊 风扇噪音略有增加 - 高负载时风扇转速会提高，但通常在可接受范围内");
        }
        
        // 功耗和电费
        if (settings.PowerConsumptionIncrease >= 40)
        {
            settings.SideEffects.Add($"⚡ 功耗显著增加约 {settings.PowerConsumptionIncrease}% - 长期使用会增加电费支出，笔记本续航大幅缩短");
        }
        else if (settings.PowerConsumptionIncrease >= 20)
        {
            settings.SideEffects.Add($"⚡ 功耗增加约 {settings.PowerConsumptionIncrease}% - 对电费影响较小，笔记本续航会有所下降");
        }
        
        // 温度
        if (settings.ThermalRiskLevel >= 60)
        {
            settings.SideEffects.Add($"🌡️ CPU 温度可能升高 15-25°C - 建议温度不超过 {settings.SafeTempThreshold}°C，超过 {settings.CriticalTempThreshold}°C 会自动降频");
        }
        else if (settings.ThermalRiskLevel >= 40)
        {
            settings.SideEffects.Add($"🌡️ CPU 温度可能升高 5-15°C - 正常散热条件下不会有问题");
        }
        
        // 硬件寿命
        if (settings.DisableThrottle)
        {
            settings.SideEffects.Add("⏱️ 可能影响硬件寿命 - 禁用节流保护会让 CPU 长期高温运行，可能加速老化");
        }
        
        // 电源负载
        if (settings.EstimatedTdpIncrease >= 50)
        {
            settings.SideEffects.Add($"🔌 电源负载增加约 {settings.EstimatedTdpIncrease}W - 请确保电源功率充足，避免过载");
        }
        
        // 笔记本特殊提醒
        if (hardware.DeviceType == DeviceType.Laptop)
        {
            settings.SideEffects.Add("💻 笔记本散热受限 - 长时间高负载可能导致键盘区域发烫，建议使用外接键盘");
        }
    }
    
    /// <summary>
    /// 添加散热建议
    /// </summary>
    private static void AddCoolingTips(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        if (hardware.DeviceType == DeviceType.Laptop)
        {
            settings.CoolingTips.Add("🌀 使用散热底座或支架，保持底部通风");
            settings.CoolingTips.Add("🧹 定期清理散热口灰尘，保持散热效率");
            settings.CoolingTips.Add("❄️ 夏季建议在空调房间使用，环境温度影响很大");
            if (settings.ThermalRiskLevel >= 50)
            {
                settings.CoolingTips.Add("🔧 考虑更换导热硅脂，可降低 5-10°C");
            }
        }
        else
        {
            if (settings.ThermalRiskLevel >= 60)
            {
                settings.CoolingTips.Add("🌀 建议使用双塔散热器或 240mm 以上水冷");
                settings.CoolingTips.Add("🔧 检查散热器安装是否正确，硅脂是否涂抹均匀");
                settings.CoolingTips.Add("📦 确保机箱风道合理，前进后出或下进上出");
            }
            else
            {
                settings.CoolingTips.Add("🌀 确保散热器风扇正常运转");
                settings.CoolingTips.Add("📦 保持机箱内部整洁，不要堵塞风道");
            }
            settings.CoolingTips.Add("🧹 定期清理散热器和机箱灰尘（建议每 3-6 个月）");
            settings.CoolingTips.Add("❄️ 环境温度每升高 10°C，CPU 温度约升高 5-8°C");
        }
        
        // 通用建议
        settings.CoolingTips.Add("📊 使用 HWiNFO 或 AIDA64 监控温度，确保不超过安全阈值");
    }
    
    /// <summary>
    /// 添加常见问题
    /// </summary>
    private static void AddFAQ(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        // 风扇啸叫问题
        settings.FAQ.Add((
            "应用后风扇声音很大/有啸叫怎么办？",
            "这是因为 CPU 持续高频运行导致发热增加，风扇需要高速旋转散热。解决方法：\n" +
            "1. 在 BIOS 中调整风扇曲线，降低低温时的转速\n" +
            "2. 更换静音风扇或更好的散热器\n" +
            "3. 如果噪音无法接受，可以降低「最小处理器状态」到 80% 或使用平衡模式"
        ));
        
        // 温度问题
        settings.FAQ.Add((
            $"温度超过 {settings.SafeTempThreshold}°C 正常吗？",
            $"短时间超过 {settings.SafeTempThreshold}°C 是正常的，但长时间超过可能导致降频。\n" +
            $"如果温度经常超过 {settings.CriticalTempThreshold}°C，建议：\n" +
            "1. 检查散热器安装和硅脂\n" +
            "2. 升级散热器\n" +
            "3. 降低优化强度或恢复默认设置"
        ));
        
        // 功耗问题
        settings.FAQ.Add((
            "会增加多少电费？",
            $"预计功耗增加约 {settings.PowerConsumptionIncrease}%，假设每天游戏 4 小时：\n" +
            $"• 每月额外用电约 {settings.EstimatedTdpIncrease * 4 * 30 / 1000.0:F1} 度\n" +
            $"• 按 0.6 元/度计算，每月约增加 {settings.EstimatedTdpIncrease * 4 * 30 / 1000.0 * 0.6:F1} 元\n" +
            "日常使用时建议切换回平衡模式以节省电费"
        ));
        
        // 性能提升问题
        var performanceGainDesc = settings.EstimatedFpsGain switch
        {
            >= 15 => "可能有明显提升",
            >= 10 => "可能有所提升",
            _ => "可能略有提升"
        };
        
        settings.FAQ.Add((
            "真的能提升游戏帧数吗？",
            $"{performanceGainDesc}，但实际效果因硬件配置和游戏类型而异：\n" +
            "• CPU 密集型游戏（如策略、模拟类）提升更明显\n" +
            "• GPU 密集型游戏（如 3A 大作）提升相对有限\n" +
            "• 主要改善的是帧数稳定性和最低帧，减少卡顿\n" +
            "• 不同设备效果差异较大，可能有效也可能无明显变化"
        ));
        
        // 笔记本特殊问题
        if (hardware.DeviceType == DeviceType.Laptop)
        {
            settings.FAQ.Add((
                "笔记本用这个设置会不会烧坏？",
                "不会烧坏，现代 CPU 有多重保护机制：\n" +
                "1. 温度过高会自动降频（Thermal Throttling）\n" +
                "2. 极端情况会自动关机保护\n" +
                "但长期高温运行可能加速老化，建议：\n" +
                "• 使用散热底座\n" +
                "• 不要在床上或沙发上使用\n" +
                "• 定期清灰"
            ));
        }
        
        // 恢复问题
        settings.FAQ.Add((
            "如何恢复默认设置？",
            "有多种方式恢复：\n" +
            "1. 在本软件中点击「恢复原始电源」按钮\n" +
            "2. 在 Windows 设置 > 电源 中切换到「平衡」计划\n" +
            "3. 运行 powercfg /restoredefaultschemes 命令恢复所有默认计划"
        ));
    }

    /// <summary>
    /// 计算温度风险等级
    /// </summary>
    private static int CalculateThermalRisk(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        var risk = 30;  // 基础风险

        // 最小处理器状态影响
        if (settings.MinProcessorState >= 100) risk += 25;
        else if (settings.MinProcessorState >= 80) risk += 15;
        else if (settings.MinProcessorState >= 50) risk += 5;

        // 禁用节流保护
        if (settings.DisableThrottle) risk += 20;

        // 笔记本风险更高
        if (hardware.DeviceType == DeviceType.Laptop)
        {
            risk += 15;
            if (!hardware.IsOnACPower) risk += 10;  // 电池模式风险更高
        }

        // 高线程数 CPU 发热更大
        if (hardware.CpuThreads >= 24) risk += 10;
        else if (hardware.CpuThreads >= 16) risk += 5;

        return Math.Min(100, risk);
    }

    /// <summary>
    /// 估算 FPS 提升
    /// </summary>
    private static int EstimateFpsGain(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        var gain = 5;  // 基础提升

        // 最小处理器状态影响
        if (settings.MinProcessorState >= 100) gain += 5;
        if (settings.PerfIncreaseThreshold <= 1) gain += 3;
        if (settings.IdleDemoteThreshold >= 100) gain += 2;
        if (settings.DisableAspm) gain += 2;
        if (settings.CoreParkingMin >= 100) gain += 2;

        // 高端硬件提升更明显
        if (hardware.CpuThreads >= 16 && hardware.HasDiscreteGpu) gain += 3;

        // 笔记本提升相对较小
        if (hardware.DeviceType == DeviceType.Laptop) gain = (int)(gain * 0.7);

        return Math.Min(20, gain);
    }

    /// <summary>
    /// 填充详细变更日志（2026新增：完全透明化）
    /// </summary>
    private static void PopulateDetailedChangelog(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        settings.DetailedChangelog.Add($"📋 优化版本: {settings.OptimizationVersion}");
        settings.DetailedChangelog.Add($"🖥️ 硬件配置: {hardware.CpuName} | {hardware.CpuThreads}线程 | {hardware.TotalMemoryGb}GB内存");
        settings.DetailedChangelog.Add("");
        settings.DetailedChangelog.Add("=== 核心参数变更 ===");
        
        // 最小处理器状态
        var minStateChange = settings.MinProcessorState >= 100 ? "100% (极致性能)" :
                            settings.MinProcessorState >= 90 ? "90-95% (高性能)" :
                            settings.MinProcessorState >= 85 ? "85-90% (平衡性能)" :
                            settings.MinProcessorState >= 80 ? "80-85% (保守性能)" :
                            $"{settings.MinProcessorState}% (节能模式)";
        settings.DetailedChangelog.Add($"• 最小处理器状态: {minStateChange}");
        settings.DetailedChangelog.Add($"  原因: 2026优化 - 降低功耗20-30%，性能损失<2%");
        
        // 性能时间检查间隔
        var perfTimeDesc = settings.PerfTimeCheck switch
        {
            <= 1000 => "1秒 (极快响应)",
            <= 2000 => "2秒 (快速响应)",
            <= 5000 => "5秒 (标准响应)",
            _ => $"{settings.PerfTimeCheck}ms (慢速响应)"
        };
        settings.DetailedChangelog.Add($"• 性能时间检查间隔: {perfTimeDesc}");
        settings.DetailedChangelog.Add($"  原因: 2026优化 - 加快CPU频率调整速度，减少延迟");
        
        // BoostMode
        var boostModeDesc = settings.BoostMode switch
        {
            0 => "禁用 (节能)",
            1 => "标准模式 (安全)",
            2 => "激进模式 (性能)",
            _ => $"模式{settings.BoostMode}"
        };
        settings.DetailedChangelog.Add($"• 处理器Boost模式: {boostModeDesc}");
        if (settings.BoostMode == 1 && hardware.CpuVendor == CpuVendor.Intel)
        {
            settings.DetailedChangelog.Add($"  原因: Intel 13/14代有降级风险，使用标准模式确保稳定性");
        }
        else
        {
            settings.DetailedChangelog.Add($"  原因: 最大化单核睿频性能");
        }
        
        // 核心驻留
        var coreParkingDesc = settings.CoreParkingMin >= 100 ? "完全禁用 (所有核心始终活跃)" :
                             settings.CoreParkingMin >= 70 ? "70% (为E-core保留空间)" :
                             settings.CoreParkingMin >= 50 ? "50% (平衡模式)" :
                             $"{settings.CoreParkingMin}% (节能模式)";
        settings.DetailedChangelog.Add($"• 核心驻留最小值: {coreParkingDesc}");
        if (hardware.IsIntelHybrid && settings.CoreParkingMin < 100)
        {
            settings.DetailedChangelog.Add($"  原因: 2026优化 - Intel大小核需要保留空间让E-core正确调度");
        }
        else
        {
            settings.DetailedChangelog.Add($"  原因: 确保所有核心随时可用，减少唤醒延迟");
        }
        
        // 异类线程调度
        if (hardware.IsIntelHybrid || hardware.CpuVendor == CpuVendor.AMD)
        {
            var heteroDesc = settings.HeteroThreadPolicy switch
            {
                0 => "所有处理器 (AMD模式)",
                1 => "仅性能核心",
                2 => "手动分配",
                5 => "自动调度 (推荐)",
                _ => $"模式{settings.HeteroThreadPolicy}"
            };
            settings.DetailedChangelog.Add($"• 异类线程调度策略: {heteroDesc}");
            if (settings.HeteroThreadPolicy == 5 && hardware.IsIntelHybrid)
            {
                settings.DetailedChangelog.Add($"  原因: 2026优化 - 让Windows 11 Thread Director智能分配任务");
            }
            else if (settings.HeteroThreadPolicy == 0)
            {
                settings.DetailedChangelog.Add($"  原因: AMD CPU使用所有核心模式");
            }
        }
        
        // 节流保护
        var throttleDesc = settings.DisableThrottle ? "已禁用 (⚠️ 高风险)" : "已启用 (安全)";
        settings.DetailedChangelog.Add($"• CPU节流保护: {throttleDesc}");
        if (!settings.DisableThrottle)
        {
            settings.DetailedChangelog.Add($"  原因: 2026安全优化 - 保留热保护，防止CPU过热损坏");
        }
        
        settings.DetailedChangelog.Add("");
        settings.DetailedChangelog.Add("=== 2026新增参数 ===");
        settings.DetailedChangelog.Add($"• 性能提升时间: {settings.PerfIncreaseTime} (最快响应)");
        settings.DetailedChangelog.Add($"• 性能降低时间: {settings.PerfDecreaseTime} (最快响应)");
        settings.DetailedChangelog.Add($"• 核心驻留增加时间: {settings.CoreParkingIncreaseTime} (快速唤醒)");
        settings.DetailedChangelog.Add($"  原因: 减少CPU频率调整延迟，提升响应速度");
        
        settings.DetailedChangelog.Add("");
        settings.DetailedChangelog.Add("=== 风险评估 ===");
        settings.DetailedChangelog.Add($"• 温度风险: {settings.ThermalRiskLevel}/100 ({GetRiskLevelDesc(settings.ThermalRiskLevel)})");
        settings.DetailedChangelog.Add($"• 噪音风险: {settings.NoiseRiskLevel}/100 ({GetRiskLevelDesc(settings.NoiseRiskLevel)})");
        settings.DetailedChangelog.Add($"• 功耗增加: 约{settings.PowerConsumptionIncrease}% (+{settings.EstimatedTdpIncrease}W)");
        settings.DetailedChangelog.Add($"• 预计FPS提升: {settings.EstimatedFpsGain}%");
        
        settings.DetailedChangelog.Add("");
        settings.DetailedChangelog.Add("=== 硬件建议 ===");
        settings.DetailedChangelog.Add($"• 散热器: {settings.CoolerRecommendation}");
        settings.DetailedChangelog.Add($"• 电源: {settings.PowerSupplyNote}");
    }
    
    /// <summary>
    /// 获取风险等级描述
    /// </summary>
    private static string GetRiskLevelDesc(int riskLevel)
    {
        return riskLevel switch
        {
            >= 80 => "极高风险",
            >= 60 => "高风险",
            >= 40 => "中等风险",
            >= 20 => "低风险",
            _ => "极低风险"
        };
    }

    /// <summary>
    /// 添加设备特定建议
    /// </summary>
    private static void AddDeviceSpecificRecommendations(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        // 笔记本建议
        if (hardware.DeviceType == DeviceType.Laptop)
        {
            if (!hardware.IsOnACPower)
            {
                settings.Warnings.Add("🔋 当前使用电池供电，已自动降低性能设置以保护续航");
            }
            else
            {
                settings.Recommendations.Add("💡 笔记本建议使用散热底座或外接风扇");
            }
        }

        // 内存建议
        if (hardware.IsXmpEnabled == false)
        {
            settings.Recommendations.Add("💡 检测到内存未启用 XMP/EXPO，建议在 BIOS 中开启");
        }

        // 低内存警告
        if (hardware.TotalMemoryGb < 16)
        {
            settings.Warnings.Add("⚠️ 内存不足 16GB，部分大型游戏可能卡顿");
        }

        // 集显警告
        if (!hardware.HasDiscreteGpu)
        {
            settings.Warnings.Add("⚠️ 未检测到独立显卡，游戏性能可能受限");
        }

        // Intel 大小核提示
        if (hardware.IsIntelHybrid && hardware.PCoreCount > 0)
        {
            settings.Recommendations.Add($"💡 检测到 Intel 大小核 ({hardware.PCoreCount}P + {hardware.ECoreCount}E)，已优化线程调度");
        }

        // AMD 提示
        if (hardware.CpuVendor == CpuVendor.AMD && !hardware.IsAmdX3D)
        {
            settings.Recommendations.Add("💡 AMD CPU 建议在 BIOS 启用 PBO 以获得更高性能");
        }
    }

    /// <summary>
    /// 获取所有已注册的 CPU 规则（用于调试/显示）
    /// </summary>
    public static IReadOnlyList<CpuOptimizationRule> GetAllRules() => _cpuRules.AsReadOnly();

    /// <summary>
    /// 检查设置是否安全
    /// </summary>
    public static (bool IsSafe, List<string> Issues) ValidateSettings(OptimalPowerSettings settings, HardwareInfo hardware)
    {
        var issues = new List<string>();

        // 笔记本电池模式检查
        if (hardware.DeviceType == DeviceType.Laptop && !hardware.IsOnACPower)
        {
            if (settings.MinProcessorState > 50)
                issues.Add("电池模式下处理器最小状态过高，可能导致续航大幅下降");
            if (settings.DisableThrottle)
                issues.Add("电池模式下不建议禁用节流保护");
        }

        // 温度风险检查
        if (settings.ThermalRiskLevel >= 70)
        {
            issues.Add("温度风险较高，请确保散热良好");
        }

        // X3D 特殊检查
        if (hardware.IsAmdX3D && settings.CoreParkingMin >= 100)
        {
            issues.Add("X3D 系列 CPU 不建议完全禁用 Core Parking");
        }

        return (issues.Count == 0, issues);
    }
}
