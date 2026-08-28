// File: GameOptimizer.Shell/Models/ApplyConfirmInfo.cs
using System.Collections.Generic;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 预确认信息 - 用于显示待应用的优化项统计
/// </summary>
public class ApplyConfirmInfo
{
    /// <summary>
    /// 待应用的优化项总数
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// 当前策略名称（如"狂暴模式"）
    /// </summary>
    public string StrategyName { get; set; } = string.Empty;
    
    /// <summary>
    /// 安全优化数量
    /// </summary>
    public int SafeCount { get; set; }
    
    /// <summary>
    /// 平衡优化数量
    /// </summary>
    public int BalancedCount { get; set; }
    
    /// <summary>
    /// 极限优化数量
    /// </summary>
    public int ExtremeCount { get; set; }
    
    /// <summary>
    /// 按类别分组的数量（类别名 -> 数量）
    /// </summary>
    public Dictionary<string, int> CategoryCounts { get; set; } = new();
    
    /// <summary>
    /// 待应用的操作ID列表
    /// </summary>
    public List<string> PendingOperationIds { get; set; } = new();
    
    /// <summary>
    /// 详细列表（用于"查看详细列表"）
    /// </summary>
    public List<PendingOperationItem> DetailedList { get; set; } = new();
}
