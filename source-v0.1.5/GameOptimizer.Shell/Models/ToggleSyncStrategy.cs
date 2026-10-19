// File: GameOptimizer.Shell/Models/ToggleSyncStrategy.cs
namespace GameOptimizer.Shell.Models;

/// <summary>
/// Toggle状态同步策略
/// 定义在不同场景下如何同步 IsEnabled 和 IsApplied 属性
/// </summary>
public enum ToggleSyncStrategy
{
    /// <summary>
    /// 仅同步 IsApplied，保持 IsEnabled 不变
    /// 用于：后台状态检查、定时刷新
    /// </summary>
    AppliedOnly,
    
    /// <summary>
    /// 完全同步：IsApplied 和 IsEnabled 都设置为系统实际状态
    /// 用于：启动时扫描、全局模式切换
    /// </summary>
    Full,
    
    /// <summary>
    /// 用户操作后同步：
    /// - Apply成功：保持 IsEnabled=true, 设置 IsApplied=true
    /// - Restore成功：设置 IsEnabled=false, IsApplied=false
    /// 用于：用户手动Apply/Restore操作后的状态刷新
    /// </summary>
    UserOperation,
    
    /// <summary>
    /// OPT-015: 首次加载策略 - 全部设为 false（未优化状态）
    /// 用于：首次打开页面，无用户配置时
    /// 确保首次打开时所有开关都是关闭的，显示"0 项待处理"
    /// </summary>
    FirstLoad
}
