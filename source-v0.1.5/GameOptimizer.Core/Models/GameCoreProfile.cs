// File: GameOptimizer.Core/Models/GameCoreProfile.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameOptimizer.Core.Models;

public enum AcceleratorMode
{
    Manual,         // 手动加速
    AutoGuard,      // 自动守护
    OnLaunch        // 启动时加速
}

public enum CoreStrategy
{
    Auto,               // 自动选择最优
    VCacheCcdOnly,      // 仅V-Cache CCD
    SingleCcd,          // 单CCD
    PCoreOnly,          // 仅P-Core
    PhysicalOnly,       // 仅物理核心
    AllCores,           // 全部核心
    Custom              // 自定义
}

public class GameCoreProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string GameName { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public string? ExePath { get; set; }
    public string? IconPath { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastUsed { get; set; }
    
    // 核心配置
    public CoreStrategy Strategy { get; set; } = CoreStrategy.Auto;
    public List<int> CustomCoreIndices { get; set; } = new();
    public int? PreferredCore { get; set; }
    public bool DisableSmt { get; set; }
    public bool DisableECores { get; set; }
    public ProcessPriorityLevel Priority { get; set; } = ProcessPriorityLevel.High;
    
    // 运行时状态（不持久化）
    [JsonIgnore]
    public bool IsRunning { get; set; }
    [JsonIgnore]
    public bool IsOptimized { get; set; }
    [JsonIgnore]
    public int? ProcessId { get; set; }
    
    public string StrategyDescription => Strategy switch
    {
        CoreStrategy.VCacheCcdOnly => "V-Cache CCD",
        CoreStrategy.SingleCcd => "单CCD模式",
        CoreStrategy.PCoreOnly => "仅P-Core",
        CoreStrategy.PhysicalOnly => "仅物理核心",
        CoreStrategy.AllCores => "全部核心",
        CoreStrategy.Custom => "自定义配置",
        _ => "自动优化"
    };
}

public enum ProcessPriorityLevel
{
    Normal,
    AboveNormal,
    High,
    RealTime
}

public class CoreAcceleratorSettings
{
    public AcceleratorMode Mode { get; set; } = AcceleratorMode.AutoGuard;
    public bool EnableOnStartup { get; set; } = true;
    public int GuardIntervalMs { get; set; } = 3000;
    public bool ShowNotifications { get; set; } = true;
    public bool AutoDetectGames { get; set; } = true;
    public List<GameCoreProfile> CustomProfiles { get; set; } = new();
}

public class BuiltInGameDatabase
{
    public List<BuiltInGameEntry> Games { get; set; } = new();
}

public class BuiltInGameEntry
{
    public string Name { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public string[] AltExeNames { get; set; } = Array.Empty<string>();
    public CoreStrategy X3DStrategy { get; set; } = CoreStrategy.VCacheCcdOnly;
    public CoreStrategy HybridStrategy { get; set; } = CoreStrategy.PCoreOnly;
    public CoreStrategy DefaultStrategy { get; set; } = CoreStrategy.PhysicalOnly;
    public bool DisableSmt { get; set; }
    public string Category { get; set; } = "其他";
}
