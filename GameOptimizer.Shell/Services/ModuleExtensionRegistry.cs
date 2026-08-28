// File: GameOptimizer.Shell/Services/ModuleExtensionRegistry.cs
using System;
using System.Collections.Concurrent;
using GameOptimizer.Shell.Interfaces;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 模块扩展注册表 - 管理所有模块扩展的注册和检索
/// </summary>
public sealed class ModuleExtensionRegistry
{
    private static readonly Lazy<ModuleExtensionRegistry> _instance = 
        new(() => new ModuleExtensionRegistry());
    
    private readonly ConcurrentDictionary<string, IModuleExtension> _extensions = new();
    
    public static ModuleExtensionRegistry Instance => _instance.Value;
    
    private ModuleExtensionRegistry() { }
    
    /// <summary>
    /// 注册模块扩展
    /// </summary>
    public void Register(IModuleExtension extension)
    {
        if (extension == null)
            throw new ArgumentNullException(nameof(extension));
            
        if (string.IsNullOrWhiteSpace(extension.ModuleKey))
            throw new ArgumentException("ModuleKey不能为空", nameof(extension));
        
        _extensions[extension.ModuleKey] = extension;
    }
    
    /// <summary>
    /// 获取指定模块的扩展
    /// </summary>
    public IModuleExtension? GetExtension(string moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
            return null;
            
        return _extensions.TryGetValue(moduleKey, out var extension) ? extension : null;
    }
    
    /// <summary>
    /// 检查指定模块是否有注册的扩展
    /// </summary>
    public bool HasExtension(string moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
            return false;
            
        return _extensions.ContainsKey(moduleKey);
    }
    
    /// <summary>
    /// 取消注册指定模块的扩展
    /// </summary>
    public bool Unregister(string moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
            return false;
            
        return _extensions.TryRemove(moduleKey, out _);
    }
    
    /// <summary>
    /// 清空所有已注册的扩展
    /// </summary>
    public void Clear()
    {
        _extensions.Clear();
    }
}
