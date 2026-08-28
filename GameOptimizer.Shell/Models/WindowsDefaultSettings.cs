// File: GameOptimizer.Shell/Models/WindowsDefaultSettings.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// Windows原始默认配置 - 存储Windows系统的原始默认开关状态
/// </summary>
public sealed class WindowsDefaultSettings
{
    /// <summary>
    /// 模块唯一标识符 (例如: "performance", "memory")
    /// </summary>
    public required string ModuleKey { get; init; }
    
    /// <summary>
    /// 优化项状态字典 (OperationId -> IsEnabled)
    /// </summary>
    public required Dictionary<string, bool> ItemStates { get; init; }
    
    /// <summary>
    /// 从嵌入资源或预定义配置加载Windows默认设置
    /// </summary>
    /// <param name="moduleKey">模块标识符</param>
    /// <returns>Windows默认设置，如果不存在则返回空配置</returns>
    public static WindowsDefaultSettings Load(string moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            System.Diagnostics.Debug.WriteLine("[WindowsDefaultSettings] ModuleKey is null or empty");
            return new WindowsDefaultSettings
            {
                ModuleKey = moduleKey ?? string.Empty,
                ItemStates = new Dictionary<string, bool>()
            };
        }
        
        var defaultStates = GetDefaultStatesForModule(moduleKey);
        
        return new WindowsDefaultSettings
        {
            ModuleKey = moduleKey,
            ItemStates = defaultStates
        };
    }
    
    private static Dictionary<string, bool> GetDefaultStatesForModule(string moduleKey)
    {
        var defaults = new Dictionary<string, bool>();
        
        switch (moduleKey.ToLowerInvariant())
        {
            case "performance":
            case "memory":
            case "cpu":
            case "graphics":
            case "network":
            case "explorer":
            case "services":
            case "privacy":
            case "peripheral":
            case "system":
                break;
            
            default:
                System.Diagnostics.Debug.WriteLine($"[WindowsDefaultSettings] Unknown module: {moduleKey}");
                break;
        }
        
        return defaults;
    }
}
