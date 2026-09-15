namespace GameOptimizer.Core.Optimization;

/// <summary>
/// 操作风险等级
/// </summary>
public enum OperationRiskLevel
{
    /// <summary>
    /// 安全 - 低风险操作，可随时恢复
    /// </summary>
    Safe = 0,

    /// <summary>
    /// 平衡 - 中等风险，推荐大多数用户使用
    /// </summary>
    Balanced = 1,

    /// <summary>
    /// 激进 - 高风险操作，可能影响系统稳定性
    /// </summary>
    Extreme = 2
}
