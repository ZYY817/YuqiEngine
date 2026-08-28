// File: GameOptimizer.Shell/Models/PowerSettingComparison.cs
namespace GameOptimizer.Shell.Models;

/// <summary>
/// 表示单个电源参数的对比结果
/// </summary>
public record PowerSettingComparison
{
    /// <summary>
    /// 参数键 (如 "proc_min_state")
    /// </summary>
    public string SettingKey { get; init; } = "";
    
    /// <summary>
    /// 参数显示名称 (本地化)
    /// </summary>
    public string DisplayName { get; init; } = "";
    
    /// <summary>
    /// 参数类别 (如 "处理器设置", "低延迟设置")
    /// </summary>
    public string Category { get; init; } = "";
    
    /// <summary>
    /// 当前值
    /// </summary>
    public int CurrentValue { get; init; }
    
    /// <summary>
    /// 新值
    /// </summary>
    public int NewValue { get; init; }
    
    /// <summary>
    /// 当前值的显示文本 (格式化后，如 "5%", "关闭")
    /// </summary>
    public string CurrentDisplayValue { get; init; } = "";
    
    /// <summary>
    /// 新值的显示文本 (格式化后)
    /// </summary>
    public string NewDisplayValue { get; init; } = "";
    
    /// <summary>
    /// 变化方向
    /// </summary>
    public ChangeDirection Direction { get; init; }
    
    /// <summary>
    /// 是否有变化
    /// </summary>
    public bool IsChanged => CurrentValue != NewValue;
}

/// <summary>
/// 参数变化方向
/// </summary>
public enum ChangeDirection
{
    /// <summary>
    /// 无变化
    /// </summary>
    NoChange,
    
    /// <summary>
    /// 增加
    /// </summary>
    Increase,
    
    /// <summary>
    /// 减少
    /// </summary>
    Decrease
}
