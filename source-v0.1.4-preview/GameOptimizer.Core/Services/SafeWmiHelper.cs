// File: GameOptimizer.Core/Services/SafeWmiHelper.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 安全的 WMI 查询助手
/// 解决 WMI 查询超时、服务未运行等问题
/// </summary>
public static class SafeWmiHelper
{
    private const int DEFAULT_TIMEOUT_MS = 5000;
    
    /// <summary>
    /// 检查 WMI 服务是否运行
    /// </summary>
    public static bool CheckWmiServiceRunning()
    {
        return SafeServiceHelper.ServiceExists("Winmgmt") &&
               SafeServiceHelper.TryGetServiceStatus("Winmgmt").Status == System.ServiceProcess.ServiceControllerStatus.Running;
    }
    
    /// <summary>
    /// 检查 WMI 是否可用
    /// </summary>
    public static bool IsWmiAvailable()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            searcher.Options.Timeout = TimeSpan.FromMilliseconds(3000);
            using var results = searcher.Get();
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 带超时的 WMI 查询
    /// </summary>
    public static (List<T> Results, string? Error) QueryWithTimeout<T>(
        string query, 
        int timeoutMs, 
        Func<ManagementObject, T> selector)
    {
        var results = new List<T>();
        
        if (!CheckWmiServiceRunning())
        {
            return (results, "WMI 服务未运行");
        }
        
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            searcher.Options.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
            
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                try
                {
                    var item = selector(obj);
                    results.Add(item);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SafeWmi] Error processing WMI object: {ex.Message}");
                }
                finally
                {
                    obj?.Dispose();
                }
            }
            
            return (results, null);
        }
        catch (ManagementException ex)
        {
            Debug.WriteLine($"[SafeWmi] WMI query failed: {ex.Message}");
            return (results, $"WMI 查询失败: {ex.Message}");
        }
        catch (TimeoutException)
        {
            Debug.WriteLine($"[SafeWmi] WMI query timeout: {query}");
            return (results, $"WMI 查询超时（{timeoutMs}ms）");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeWmi] Unexpected error in WMI query: {ex.Message}");
            return (results, $"WMI 查询错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 安全获取单个值
    /// </summary>
    public static (T Value, string? Error) TryGetSingleValue<T>(
        string query, 
        string propertyName, 
        T defaultValue, 
        int timeoutMs = DEFAULT_TIMEOUT_MS)
    {
        if (!CheckWmiServiceRunning())
        {
            return (defaultValue, "WMI 服务未运行");
        }
        
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            searcher.Options.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
            
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                try
                {
                    var value = obj[propertyName];
                    if (value != null && value is T typedValue)
                    {
                        return (typedValue, null);
                    }
                    
                    // 尝试类型转换
                    if (value != null)
                    {
                        try
                        {
                            var converted = (T)Convert.ChangeType(value, typeof(T));
                            return (converted, null);
                        }
                        catch
                        {
                            // 转换失败，返回默认值
                        }
                    }
                }
                finally
                {
                    obj?.Dispose();
                }
            }
            
            return (defaultValue, "未找到匹配的 WMI 对象");
        }
        catch (ManagementException ex)
        {
            Debug.WriteLine($"[SafeWmi] WMI query failed: {ex.Message}");
            return (defaultValue, $"WMI 查询失败: {ex.Message}");
        }
        catch (TimeoutException)
        {
            Debug.WriteLine($"[SafeWmi] WMI query timeout: {query}");
            return (defaultValue, $"WMI 查询超时（{timeoutMs}ms）");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeWmi] Unexpected error in WMI query: {ex.Message}");
            return (defaultValue, $"WMI 查询错误: {ex.Message}");
        }
    }
}
