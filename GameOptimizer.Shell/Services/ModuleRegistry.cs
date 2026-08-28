// File: GameOptimizer.Shell/Services/ModuleRegistry.cs
using GameOptimizer.Shell.Models;
using System;
using System.Collections.Generic;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 模块注册表 - 存储和检索所有优化模块的配置
/// </summary>
public static class ModuleRegistry
{
    private static readonly Dictionary<string, ModuleConfiguration> _configurations = new();
    private static bool _isInitialized = false;
    
    /// <summary>
    /// 初始化所有模块配置
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;
        
        // 性能优化模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "performance",
            TitleKey = "Nav.PerformanceBoost",
            SubtitleKey = "Nav.PerformanceBoost.Subtitle",
            DataSourcePath = "PerformanceBoost",
            HasExtension = false
        });
        
        // 内存优化模块 (有特殊控件)
        Register(new ModuleConfiguration
        {
            ModuleKey = "memory",
            TitleKey = "Nav.MemoryOptimization",
            SubtitleKey = "Nav.MemoryOptimization.Subtitle",
            DataSourcePath = "MemoryOptimization",
            HasExtension = true,
            ExtensionType = null // 后续任务会设置
        });
        
        // CPU优化模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "cpu",
            TitleKey = "Nav.CpuOptimization",
            SubtitleKey = "Nav.CpuOptimization.Subtitle",
            DataSourcePath = "CpuOptimization",
            HasExtension = false
        });
        
        // 图形优化模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "graphics",
            TitleKey = "Nav.GraphicsOptimization",
            SubtitleKey = "Nav.GraphicsOptimization.Subtitle",
            DataSourcePath = "GraphicsOptimization",
            HasExtension = false
        });
        
        // 网络优化模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "network",
            TitleKey = "Nav.NetworkOptimization",
            SubtitleKey = "Nav.NetworkOptimization.Subtitle",
            DataSourcePath = "NetworkOptimization",
            HasExtension = false
        });
        
        // 资源管理器优化模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "explorer",
            TitleKey = "Nav.ExplorerOptimization",
            SubtitleKey = "Nav.ExplorerOptimization.Subtitle",
            DataSourcePath = "ExplorerOptimization",
            HasExtension = false
        });
        
        // 服务优化模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "services",
            TitleKey = "Nav.ServicesOptimization",
            SubtitleKey = "Nav.ServicesOptimization.Subtitle",
            DataSourcePath = "ServicesOptimization",
            HasExtension = false
        });
        
        // 隐私防护模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "privacy",
            TitleKey = "Nav.PrivacyShield",
            SubtitleKey = "Nav.PrivacyShield.Subtitle",
            DataSourcePath = "PrivacyShield",
            HasExtension = false
        });
        
        // 外设优化模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "peripheral",
            TitleKey = "Nav.PeripheralOptimization",
            SubtitleKey = "Nav.PeripheralOptimization.Subtitle",
            DataSourcePath = "PeripheralOptimization",
            HasExtension = false
        });
        
        // 系统调优模块
        Register(new ModuleConfiguration
        {
            ModuleKey = "system",
            TitleKey = "Nav.SystemTuning",
            SubtitleKey = "Nav.SystemTuning.Subtitle",
            DataSourcePath = "SystemTuning",
            HasExtension = false
        });
        
        _isInitialized = true;
    }
    
    /// <summary>
    /// 注册模块配置
    /// </summary>
    private static void Register(ModuleConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.ModuleKey))
            throw new ArgumentException("ModuleKey不能为空", nameof(configuration));
            
        _configurations[configuration.ModuleKey] = configuration;
    }
    
    /// <summary>
    /// 获取模块配置
    /// </summary>
    /// <param name="moduleKey">模块键</param>
    /// <returns>模块配置，如果不存在返回null</returns>
    public static ModuleConfiguration? GetConfiguration(string moduleKey)
    {
        if (!_isInitialized)
            Initialize();
            
        return _configurations.TryGetValue(moduleKey, out var config) ? config : null;
    }
    
    /// <summary>
    /// 检查模块是否存在
    /// </summary>
    public static bool HasModule(string moduleKey)
    {
        if (!_isInitialized)
            Initialize();
            
        return _configurations.ContainsKey(moduleKey);
    }
    
    /// <summary>
    /// 获取所有已注册的模块键
    /// </summary>
    public static IReadOnlyCollection<string> GetAllModuleKeys()
    {
        if (!_isInitialized)
            Initialize();
            
        return _configurations.Keys;
    }
}
