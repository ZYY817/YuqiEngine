// File: GameOptimizer.Shell/Interfaces/IModuleExtension.cs
using System.Windows;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Interfaces;

/// <summary>
/// 模块扩展接口 - 允许模块添加自定义UI内容到MasterModuleTemplate
/// </summary>
/// <remarks>
/// 此接口实现策略模式,允许不同模块在统一模板中注入特定功能控件。
/// 例如:内存优化模块可以添加内存监控和清理按钮。
/// </remarks>
public interface IModuleExtension
{
    /// <summary>
    /// 模块键 - 标识此扩展对应的模块
    /// </summary>
    /// <remarks>
    /// 必须与ModuleRegistry中注册的ModuleKey匹配
    /// </remarks>
    string ModuleKey { get; }
    
    /// <summary>
    /// 创建扩展内容UI元素
    /// </summary>
    /// <param name="viewModel">Dashboard视图模型,提供数据上下文和命令绑定</param>
    /// <returns>要渲染的UI元素,如果无内容返回null</returns>
    /// <remarks>
    /// 此方法在MasterModuleTemplate加载时调用。
    /// 返回的UIElement将被插入到模板的扩展点位置。
    /// 实现必须是线程安全的,因为可能在UI线程外调用。
    /// </remarks>
    UIElement? CreateExtensionContent(DashboardViewModel viewModel);
}
