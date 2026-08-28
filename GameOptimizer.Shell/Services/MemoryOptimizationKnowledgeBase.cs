using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 内存优化规则
/// </summary>
public class MemoryOptimizationRule
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public int Priority { get; init; }
    
    // 条件判断
    public int? MinMemoryGb { get; init; }
    public int? MaxMemoryGb { get; init; }
    public DeviceType? RequireDeviceType { get; init; }
    public bool? RequireXmp { get; init; }
    
    // 推荐设置
    public bool DisableCompression { get; init; }
    public bool DisableSuperFetch { get; init; }
    public bool DisableKernelPaging { get; init; }
    public bool EnableLargePages { get; init; }
    public bool OptimizeSystemCache { get; init; }
    public bool OptimizePageFile { get; init; }
    public bool OptimizePriority { get; init; }
    
    // 页面文件设置
    public Func<int, int>? PageFileSizeCalculator { get; init; }  // 输入内存GB，输出页面文件MB
    
    // 说明
    public List<string> Warnings { get; init; } = new();
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// 计算出的最优内存设置
/// </summary>
public class OptimalMemorySettings
{
    public string RuleId { get; set; } = "";
    public string RuleDescription { get; set; } = "";
    
    // 优化开关
    public bool DisableCompression { get; set; }
    public bool DisableSuperFetch { get; set; }
    public bool DisableKernelPaging { get; set; }
    public bool EnableLargePages { get; set; }
    public bool OptimizeSystemCache { get; set; }
    public bool OptimizePageFile { get; set; }
    public bool OptimizePriority { get; set; }
    
    // 页面文件
    public int RecommendedPageFileMb { get; set; }
    
    // 风险评估
    public int MemoryPressureRisk { get; set; }  // 内存压力风险 0-100
    public int EstimatedGain { get; set; }       // 预计提升百分比
    
    // 说明
    public List<string> EnabledOperations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// 内存优化知识库 - 基于硬件特征动态计算最优设置
/// </summary>
public static class MemoryOptimizationKnowledgeBase
{
    private static readonly List<MemoryOptimizationRule> _rules = new();
    
    static MemoryOptimizationKnowledgeBase()
    {
        InitializeRules();
    }

    private static void InitializeRules()
    {
        // ========== 高端台式机 (32GB+) ==========
        _rules.Add(new MemoryOptimizationRule
        {
            Id = "desktop_high_end",
            Description = "高端台式机 (32GB+)",
            Priority = 100,
            MinMemoryGb = 32,
            RequireDeviceType = DeviceType.Desktop,
            
            DisableCompression = true,      // 内存充足，禁用压缩减少CPU开销
            DisableSuperFetch = true,       // 内存充足，禁用预读
            DisableKernelPaging = true,     // 内核常驻内存
            EnableLargePages = true,        // 启用大页面
            OptimizeSystemCache = true,
            OptimizePageFile = true,
            OptimizePriority = true,
            
            PageFileSizeCalculator = memGb => memGb * 1024,  // 等于物理内存
            
            Recommendations = new() 
            { 
                "💡 32GB+ 内存可以完全禁用内存压缩，释放 CPU 资源",
                "💡 建议在 BIOS 中启用 XMP/EXPO 以获得最佳内存性能"
            }
        });

        // ========== 中端台式机 (16-31GB) ==========
        _rules.Add(new MemoryOptimizationRule
        {
            Id = "desktop_mid_range",
            Description = "中端台式机 (16-31GB)",
            Priority = 90,
            MinMemoryGb = 16,
            MaxMemoryGb = 31,
            RequireDeviceType = DeviceType.Desktop,
            
            DisableCompression = true,
            DisableSuperFetch = false,      // 保留预读，加快应用启动
            DisableKernelPaging = true,
            EnableLargePages = true,
            OptimizeSystemCache = true,
            OptimizePageFile = true,
            OptimizePriority = true,
            
            PageFileSizeCalculator = memGb => (int)(memGb * 1024 * 0.5),  // 物理内存的一半
            
            Recommendations = new() 
            { 
                "💡 16GB 内存足够大多数游戏，建议保留 SuperFetch 加快启动",
                "💡 如果同时运行多个大型程序，可考虑升级到 32GB"
            }
        });

        // ========== 入门台式机 (8-15GB) ==========
        _rules.Add(new MemoryOptimizationRule
        {
            Id = "desktop_entry",
            Description = "入门台式机 (8-15GB)",
            Priority = 80,
            MinMemoryGb = 8,
            MaxMemoryGb = 15,
            RequireDeviceType = DeviceType.Desktop,
            
            DisableCompression = false,     // 内存紧张，保留压缩
            DisableSuperFetch = false,
            DisableKernelPaging = false,    // 保留分页
            EnableLargePages = false,       // 大页面会占用更多内存
            OptimizeSystemCache = true,
            OptimizePageFile = true,
            OptimizePriority = true,
            
            PageFileSizeCalculator = memGb => memGb * 1024 * 2,  // 物理内存的两倍
            
            Warnings = new() { "⚠️ 8GB 内存可能不足以运行大型游戏，建议升级" },
            Recommendations = new() 
            { 
                "💡 内存较少时保留压缩功能可以有效扩展可用内存",
                "💡 建议关闭不必要的后台程序以释放内存"
            }
        });

        // ========== 高端笔记本 (32GB+) ==========
        _rules.Add(new MemoryOptimizationRule
        {
            Id = "laptop_high_end",
            Description = "高端笔记本 (32GB+)",
            Priority = 95,
            MinMemoryGb = 32,
            RequireDeviceType = DeviceType.Laptop,
            
            DisableCompression = true,
            DisableSuperFetch = false,      // 笔记本保留预读，SSD寿命
            DisableKernelPaging = true,
            EnableLargePages = true,
            OptimizeSystemCache = true,
            OptimizePageFile = true,
            OptimizePriority = true,
            
            PageFileSizeCalculator = memGb => memGb * 1024,
            
            Recommendations = new() 
            { 
                "💡 笔记本建议保留 SuperFetch，减少 SSD 写入",
                "💡 游戏时建议插电使用以获得最佳性能"
            }
        });

        // ========== 中端笔记本 (16-31GB) ==========
        _rules.Add(new MemoryOptimizationRule
        {
            Id = "laptop_mid_range",
            Description = "中端笔记本 (16-31GB)",
            Priority = 85,
            MinMemoryGb = 16,
            MaxMemoryGb = 31,
            RequireDeviceType = DeviceType.Laptop,
            
            DisableCompression = false,     // 笔记本保守设置
            DisableSuperFetch = false,
            DisableKernelPaging = true,
            EnableLargePages = true,
            OptimizeSystemCache = true,
            OptimizePageFile = true,
            OptimizePriority = true,
            
            PageFileSizeCalculator = memGb => (int)(memGb * 1024 * 0.5),
            
            Recommendations = new() 
            { 
                "💡 笔记本内存优化采用保守策略，平衡性能和续航"
            }
        });

        // ========== 入门笔记本 (8-15GB) ==========
        _rules.Add(new MemoryOptimizationRule
        {
            Id = "laptop_entry",
            Description = "入门笔记本 (8-15GB)",
            Priority = 75,
            MinMemoryGb = 8,
            MaxMemoryGb = 15,
            RequireDeviceType = DeviceType.Laptop,
            
            DisableCompression = false,
            DisableSuperFetch = false,
            DisableKernelPaging = false,
            EnableLargePages = false,
            OptimizeSystemCache = true,
            OptimizePageFile = true,
            OptimizePriority = true,
            
            PageFileSizeCalculator = memGb => memGb * 1024 * 2,
            
            Warnings = new() { "⚠️ 8GB 内存的笔记本运行大型游戏可能卡顿" },
            Recommendations = new() 
            { 
                "💡 建议关闭 Chrome 等内存大户后再玩游戏",
                "💡 如果笔记本支持，可考虑升级内存"
            }
        });

        // ========== XMP/EXPO 已启用 ==========
        _rules.Add(new MemoryOptimizationRule
        {
            Id = "xmp_enabled",
            Description = "XMP/EXPO 高频内存",
            Priority = 50,  // 作为附加规则
            RequireXmp = true,
            
            EnableLargePages = true,
            OptimizeSystemCache = true,
            
            Recommendations = new() 
            { 
                "💡 检测到 XMP/EXPO 已启用，内存运行在高频模式",
                "💡 高频内存配合大页面可获得最佳性能"
            }
        });

        // ========== 通用安全规则 ==========
        _rules.Add(new MemoryOptimizationRule
        {
            Id = "generic_safe",
            Description = "通用安全模式",
            Priority = 0,
            
            DisableCompression = false,
            DisableSuperFetch = false,
            DisableKernelPaging = false,
            EnableLargePages = false,
            OptimizeSystemCache = true,
            OptimizePageFile = true,
            OptimizePriority = true,
            
            PageFileSizeCalculator = memGb => memGb * 1024,
            
            Warnings = new() { "⚠️ 未能识别硬件配置，使用保守的安全设置" }
        });
    }

    /// <summary>
    /// 根据硬件信息匹配最佳内存规则
    /// </summary>
    public static MemoryOptimizationRule? MatchRule(HardwareInfo hardware)
    {
        var memoryGb = (int)hardware.TotalMemoryGb;
        
        var matchedRules = _rules
            .Where(r => !r.MinMemoryGb.HasValue || memoryGb >= r.MinMemoryGb.Value)
            .Where(r => !r.MaxMemoryGb.HasValue || memoryGb <= r.MaxMemoryGb.Value)
            .Where(r => !r.RequireDeviceType.HasValue || r.RequireDeviceType.Value == hardware.DeviceType)
            .Where(r => !r.RequireXmp.HasValue || r.RequireXmp.Value == hardware.IsXmpEnabled)
            .OrderByDescending(r => r.Priority)
            .ToList();

        var matched = matchedRules.FirstOrDefault();
        if (matched != null)
        {
            Debug.WriteLine($"[MemoryKnowledgeBase] Matched rule: {matched.Id} ({matched.Description}) for {memoryGb}GB {hardware.DeviceType}");
        }
        
        return matched;
    }

    /// <summary>
    /// 计算最优内存设置
    /// </summary>
    public static OptimalMemorySettings CalculateOptimalSettings(HardwareInfo hardware)
    {
        var rule = MatchRule(hardware);
        var settings = new OptimalMemorySettings();
        var memoryGb = (int)hardware.TotalMemoryGb;

        if (rule == null)
        {
            settings.RuleId = "fallback";
            settings.RuleDescription = "未匹配到规则，使用默认设置";
            settings.Warnings.Add("未能识别硬件配置，使用保守的默认设置");
            return settings;
        }

        settings.RuleId = rule.Id;
        settings.RuleDescription = rule.Description;

        // 应用规则设置
        settings.DisableCompression = rule.DisableCompression;
        settings.DisableSuperFetch = rule.DisableSuperFetch;
        settings.DisableKernelPaging = rule.DisableKernelPaging;
        settings.EnableLargePages = rule.EnableLargePages;
        settings.OptimizeSystemCache = rule.OptimizeSystemCache;
        settings.OptimizePageFile = rule.OptimizePageFile;
        settings.OptimizePriority = rule.OptimizePriority;

        // 计算页面文件大小
        settings.RecommendedPageFileMb = rule.PageFileSizeCalculator?.Invoke(memoryGb) ?? memoryGb * 1024;

        // 生成启用的操作列表
        if (settings.DisableCompression) settings.EnabledOperations.Add("memory.disable_compression");
        if (settings.DisableSuperFetch) settings.EnabledOperations.Add("memory.disable_superfetch");
        if (settings.DisableKernelPaging) settings.EnabledOperations.Add("memory.disable_kernel_paging");
        if (settings.EnableLargePages) settings.EnabledOperations.Add("memory.enable_large_pages");
        if (settings.OptimizeSystemCache) settings.EnabledOperations.Add("memory.optimize_system_cache");
        if (settings.OptimizePageFile) settings.EnabledOperations.Add("memory.optimize_pagefile");
        if (settings.OptimizePriority) settings.EnabledOperations.Add("memory.optimize_priority");

        // 计算风险等级
        settings.MemoryPressureRisk = CalculateMemoryPressureRisk(settings, hardware);
        settings.EstimatedGain = EstimateGain(settings, hardware);

        // 添加规则的警告和建议
        settings.Warnings.AddRange(rule.Warnings);
        settings.Recommendations.AddRange(rule.Recommendations);

        // 添加 XMP 相关建议
        if (hardware.IsXmpEnabled == false && memoryGb >= 16)
        {
            settings.Recommendations.Add("💡 检测到内存未启用 XMP/EXPO，建议在 BIOS 中开启以获得更高频率");
        }

        Debug.WriteLine($"[MemoryKnowledgeBase] Calculated settings: Compression={!settings.DisableCompression}, " +
            $"SuperFetch={!settings.DisableSuperFetch}, LargePages={settings.EnableLargePages}, " +
            $"Risk={settings.MemoryPressureRisk}");

        return settings;
    }

    private static int CalculateMemoryPressureRisk(OptimalMemorySettings settings, HardwareInfo hardware)
    {
        var risk = 20;  // 基础风险
        var memoryGb = hardware.TotalMemoryGb;

        // 内存越少风险越高
        if (memoryGb < 8) risk += 40;
        else if (memoryGb < 16) risk += 20;
        else if (memoryGb < 32) risk += 10;

        // 禁用压缩增加风险
        if (settings.DisableCompression && memoryGb < 32) risk += 15;

        // 禁用 SuperFetch 增加风险
        if (settings.DisableSuperFetch && memoryGb < 16) risk += 10;

        // 笔记本风险更高
        if (hardware.DeviceType == DeviceType.Laptop) risk += 10;

        return Math.Min(100, risk);
    }

    private static int EstimateGain(OptimalMemorySettings settings, HardwareInfo hardware)
    {
        var gain = 2;  // 基础提升

        if (settings.DisableCompression) gain += 3;
        if (settings.EnableLargePages) gain += 3;
        if (settings.DisableKernelPaging) gain += 2;
        if (settings.OptimizePriority) gain += 2;

        // 高端配置提升更明显
        if (hardware.TotalMemoryGb >= 32) gain += 2;

        return Math.Min(15, gain);
    }

    /// <summary>
    /// 获取所有规则（用于调试）
    /// </summary>
    public static IReadOnlyList<MemoryOptimizationRule> GetAllRules() => _rules.AsReadOnly();
}
