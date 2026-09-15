// File: GameOptimizer.Core/Services/SafeServiceProvider.cs
using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 线程安全的服务提供者包装器
/// 解决 P0-001: DI 容器空引用炸弹
/// </summary>
public static class SafeServiceProvider
{
    private static readonly object _lock = new();
    private static volatile bool _isInitialized;
    private static IServiceProvider? _serviceProvider;
    
    /// <summary>
    /// 初始化服务提供者（仅在 App 启动时调用一次）
    /// </summary>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        lock (_lock)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _isInitialized = true;
            Debug.WriteLine("[SafeServiceProvider] Initialized successfully");
        }
    }
    
    /// <summary>
    /// 安全获取服务（线程安全，带降级策略）
    /// </summary>
    public static T? GetService<T>() where T : class
    {
        if (!_isInitialized)
        {
            Debug.WriteLine($"[SafeServiceProvider] Not initialized when requesting {typeof(T).Name}");
            return null;
        }
        
        try
        {
            return _serviceProvider?.GetService<T>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeServiceProvider] Failed to get service {typeof(T).Name}: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// 安全获取必需服务（失败时抛出友好异常）
    /// </summary>
    public static T GetRequiredService<T>() where T : class
    {
        var service = GetService<T>();
        if (service == null)
        {
            var errorMsg = $"无法获取必需服务 {typeof(T).Name}。请确保程序正确启动。";
            Debug.WriteLine($"[SafeServiceProvider] {errorMsg}");
            throw new InvalidOperationException(errorMsg);
        }
        return service;
    }
    
    /// <summary>
    /// 检查服务是否可用
    /// </summary>
    public static bool IsServiceAvailable<T>() where T : class
    {
        return GetService<T>() != null;
    }
    
    /// <summary>
    /// 重置服务提供者（仅用于测试）
    /// </summary>
    internal static void Reset()
    {
        lock (_lock)
        {
            _serviceProvider = null;
            _isInitialized = false;
        }
    }
}
