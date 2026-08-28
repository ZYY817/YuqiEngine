// File: GameOptimizer.Shell/Models/EnhancedProgressInfo.cs
namespace GameOptimizer.Shell.Models;

/// <summary>
/// 增强的进度信息 - 用于详细的进度反馈
/// </summary>
public class EnhancedProgressInfo
{
    /// <summary>
    /// 当前索引（从1开始）
    /// </summary>
    public int CurrentIndex { get; set; }
    
    /// <summary>
    /// 总数
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// 当前操作标题
    /// </summary>
    public string CurrentOperationTitle { get; set; } = string.Empty;
    
    /// <summary>
    /// 当前操作类别
    /// </summary>
    public string CurrentOperationCategory { get; set; } = string.Empty;
    
    /// <summary>
    /// 类别图标（Segoe MDL2 Assets）
    /// </summary>
    public string CategoryIcon { get; set; } = "\uE946";
    
    /// <summary>
    /// 预计剩余秒数
    /// </summary>
    public int EstimatedRemainingSeconds { get; set; }
    
    /// <summary>
    /// 进度百分比（0-100）
    /// </summary>
    public double ProgressPercentage => TotalCount > 0 
        ? (double)CurrentIndex / TotalCount * 100 
        : 0;
    
    /// <summary>
    /// 进度文本（如"6/15"）
    /// </summary>
    public string ProgressText => $"{CurrentIndex}/{TotalCount}";
    
    /// <summary>
    /// 预计剩余时间文本（如"约 30 秒"）
    /// </summary>
    public string EstimatedTimeText => EstimatedRemainingSeconds > 60
        ? $"约 {EstimatedRemainingSeconds / 60} 分钟"
        : $"约 {EstimatedRemainingSeconds} 秒";
}
