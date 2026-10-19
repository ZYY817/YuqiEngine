// File: GameOptimizer.Shell/Models/PendingOperationItem.cs
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 待应用的优化项
/// </summary>
public class PendingOperationItem
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
    /// 所属类别（如"网络优化"）
    /// </summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>
    /// 风险等级
    /// </summary>
    public OperationRiskLevel RiskLevel { get; set; }
    
    /// <summary>
    /// 是否兼容当前硬件
    /// </summary>
    public bool IsCompatible { get; set; }
    
    /// <summary>
    /// 风险等级文本（用于UI显示）
    /// </summary>
    public string RiskLevelText => RiskLevel switch
    {
        OperationRiskLevel.Safe => "安全",
        OperationRiskLevel.Balanced => "平衡",
        OperationRiskLevel.Extreme => "极限",
        _ => "未知"
    };
    
    /// <summary>
    /// 风险等级颜色
    /// </summary>
    public string RiskColor => RiskLevel switch
    {
        OperationRiskLevel.Safe => "#4ADE80",
        OperationRiskLevel.Balanced => "#F59E0B",
        OperationRiskLevel.Extreme => "#F43F5E",
        _ => "#8B94C6"
    };
}
