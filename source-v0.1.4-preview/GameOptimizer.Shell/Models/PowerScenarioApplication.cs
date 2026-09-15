// File: GameOptimizer.Shell/Models/PowerScenarioApplication.cs
using System.Collections.Generic;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 表示一个电源场景应用请求
/// </summary>
public record PowerScenarioApplication
{
    /// <summary>
    /// 场景 ID (如 "gaming", "competitive")
    /// </summary>
    public string ScenarioId { get; init; } = "";
    
    /// <summary>
    /// 场景名称 (本地化后的显示名称)
    /// </summary>
    public string ScenarioName { get; init; } = "";
    
    /// <summary>
    /// 要创建的电源计划名称
    /// </summary>
    public string PowerPlanName { get; init; } = "";
    
    /// <summary>
    /// 场景的电源参数设置
    /// </summary>
    public Dictionary<string, int> Settings { get; init; } = new();
    
    /// <summary>
    /// 硬件上下文信息 (用于硬件自适应优化)
    /// </summary>
    public HardwareInfo? HardwareContext { get; init; }
    
    /// <summary>
    /// 如果存在同名电源计划，是否覆盖
    /// </summary>
    public bool OverwriteExisting { get; init; }
}
