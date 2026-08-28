// File: GameOptimizer.Shell/Models/ScenarioComparisonResult.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 表示场景对比的完整结果
/// </summary>
public record ScenarioComparisonResult
{
    /// <summary>
    /// 场景名称
    /// </summary>
    public string ScenarioName { get; init; } = "";
    
    /// <summary>
    /// 场景描述
    /// </summary>
    public string ScenarioDescription { get; init; } = "";
    
    /// <summary>
    /// 当前电源计划名称
    /// </summary>
    public string CurrentPowerPlanName { get; init; } = "";
    
    /// <summary>
    /// 新电源计划名称
    /// </summary>
    public string NewPowerPlanName { get; init; } = "";
    
    /// <summary>
    /// 参数对比列表
    /// </summary>
    public IReadOnlyList<PowerSettingComparison> Comparisons { get; init; } = Array.Empty<PowerSettingComparison>();
    
    /// <summary>
    /// 总变化数量
    /// </summary>
    public int TotalChanges => Comparisons.Count(c => c.IsChanged);
    
    /// <summary>
    /// 是否存在冲突 (同名电源计划已存在)
    /// </summary>
    public bool HasConflict { get; init; }
    
    /// <summary>
    /// 冲突消息
    /// </summary>
    public string? ConflictMessage { get; init; }
}
