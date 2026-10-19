// File: GameOptimizer.Shell/Models/ApplyResultItem.cs
using System.Collections.Generic;
using GameOptimizer.Core.Diagnostics;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 应用结果项 - 用于结果报告对话框
/// </summary>
public class ApplyResultItem
{
    /// <summary>
    /// 操作ID
    /// </summary>
    public string OperationId { get; set; } = string.Empty;
    
    /// <summary>
    /// 显示标题
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// 所属模块名称
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;
    
    /// <summary>
    /// 结果状态
    /// </summary>
    public ApplyResultStatus Status { get; set; }
    
    /// <summary>
    /// 错误消息（仅失败时）
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 跳过原因（仅跳过时）
    /// </summary>
    public string? SkipReason { get; set; }
    
    /// <summary>
    /// 诊断结果（仅失败时）
    /// </summary>
    public DiagnosticResult? Diagnostic { get; set; }
    
    /// <summary>
    /// 修复选项（仅失败时）
    /// </summary>
    public List<AutoRepairOption>? RepairOptions { get; set; }
    
    /// <summary>
    /// 导航目标（用于跳过项的"前往配置"）
    /// </summary>
    public string? NavigateTarget { get; set; }
    
    /// <summary>
    /// 是否有诊断信息
    /// </summary>
    public bool HasDiagnostic => Diagnostic != null;
    
    /// <summary>
    /// 是否有修复选项
    /// </summary>
    public bool HasRepairOptions => RepairOptions != null && RepairOptions.Count > 0;
    
    /// <summary>
    /// 状态图标
    /// </summary>
    public string StatusIcon => Status switch
    {
        ApplyResultStatus.Success => "\uE73E", // CheckMark
        ApplyResultStatus.Failed => "\uE711",  // ErrorBadge
        ApplyResultStatus.Skipped => "\uE893", // CircleRing
        _ => "\uE946"
    };
    
    /// <summary>
    /// 状态前景色
    /// </summary>
    public string StatusForeground => Status switch
    {
        ApplyResultStatus.Success => "#10B981",
        ApplyResultStatus.Failed => "#EF4444",
        ApplyResultStatus.Skipped => "#8B94C6",
        _ => "#A1A9C4"
    };
}

/// <summary>
/// 应用结果状态
/// </summary>
public enum ApplyResultStatus
{
    Success,
    Failed,
    Skipped
}
