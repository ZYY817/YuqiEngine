// File: GameOptimizer.Shell/Models/ModuleConfiguration.cs
using System;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 模块配置元数据，定义优化页面的核心属性
/// </summary>
public sealed class ModuleConfiguration
{
    /// <summary>
    /// 模块唯一标识符 (例如: "performance", "memory")
    /// </summary>
    public required string ModuleKey { get; init; }
    
    /// <summary>
    /// 标题本地化键
    /// </summary>
    public required string TitleKey { get; init; }
    
    /// <summary>
    /// 副标题本地化键
    /// </summary>
    public required string SubtitleKey { get; init; }
    
    /// <summary>
    /// 数据源路径或标识符
    /// </summary>
    public required string DataSourcePath { get; init; }
    
    /// <summary>
    /// 是否有模块特定扩展控件
    /// </summary>
    public bool HasExtension { get; init; }
    
    /// <summary>
    /// 扩展控件类型 (当HasExtension为true时使用)
    /// </summary>
    public Type? ExtensionType { get; init; }
}
