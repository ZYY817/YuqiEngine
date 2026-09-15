// File: GameOptimizer.Core/Services/SafeRegistryHelper.cs
using System;
using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 安全的注册表操作助手
/// 解决 P0-002: 注册表操作权限地雷
/// </summary>
public static class SafeRegistryHelper
{
    /// <summary>
    /// 安全打开注册表键（只读）
    /// </summary>
    public static (RegistryKey? Key, string? Error) OpenKey(RegistryKey root, string subKeyPath)
    {
        try
        {
            var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key == null)
            {
                return (null, $"注册表项不存在: {subKeyPath}");
            }
            return (key, null);
        }
        catch (SecurityException ex)
        {
            Debug.WriteLine($"[SafeRegistry] No permission to read {subKeyPath}: {ex.Message}");
            return (null, $"无权限读取注册表项: {subKeyPath}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeRegistry] Access denied to {subKeyPath}: {ex.Message}");
            return (null, $"访问被拒绝: {subKeyPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeRegistry] Failed to open {subKeyPath}: {ex.Message}");
            return (null, $"打开注册表项失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全打开注册表键（可写）
    /// </summary>
    public static (RegistryKey? Key, string? Error) OpenKeyWritable(RegistryKey root, string subKeyPath)
    {
        try
        {
            var key = root.OpenSubKey(subKeyPath, writable: true);
            if (key == null)
            {
                return (null, $"注册表项不存在或无写入权限: {subKeyPath}");
            }
            return (key, null);
        }
        catch (SecurityException ex)
        {
            Debug.WriteLine($"[SafeRegistry] No permission to write {subKeyPath}: {ex.Message}");
            return (null, ElevationGate.DescribeWriteDenial($"无权限修改注册表项: {subKeyPath}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeRegistry] Write access denied to {subKeyPath}: {ex.Message}");
            return (null, ElevationGate.DescribeWriteDenial($"写入访问被拒绝: {subKeyPath}"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeRegistry] Failed to open writable {subKeyPath}: {ex.Message}");
            return (null, $"打开注册表项失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全创建或打开注册表键
    /// </summary>
    public static (RegistryKey? Key, string? Error) CreateOrOpenKey(RegistryKey root, string subKeyPath)
    {
        try
        {
            var key = root.CreateSubKey(subKeyPath, writable: true);
            if (key == null)
            {
                return (null, $"无法创建注册表项: {subKeyPath}");
            }
            return (key, null);
        }
        catch (SecurityException ex)
        {
            Debug.WriteLine($"[SafeRegistry] No permission to create {subKeyPath}: {ex.Message}");
            return (null, ElevationGate.DescribeWriteDenial($"无权限创建注册表项: {subKeyPath}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeRegistry] Create access denied to {subKeyPath}: {ex.Message}");
            return (null, ElevationGate.DescribeWriteDenial($"创建访问被拒绝: {subKeyPath}"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeRegistry] Failed to create {subKeyPath}: {ex.Message}");
            return (null, $"创建注册表项失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全读取注册表值
    /// </summary>
    public static (T? Value, string? Error) GetValue<T>(RegistryKey root, string subKeyPath, string valueName, T? defaultValue = default)
    {
        var (key, error) = OpenKey(root, subKeyPath);
        if (key == null)
        {
            return (defaultValue, error);
        }
        
        try
        {
            using (key)
            {
                var value = key.GetValue(valueName);
                if (value == null)
                {
                    return (defaultValue, null);
                }
                
                if (value is T typedValue)
                {
                    return (typedValue, null);
                }
                
                // 尝试类型转换
                try
                {
                    var converted = (T)Convert.ChangeType(value, typeof(T));
                    return (converted, null);
                }
                catch
                {
                    return (defaultValue, $"值类型不匹配: 期望 {typeof(T).Name}，实际 {value.GetType().Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeRegistry] Failed to read value {valueName} from {subKeyPath}: {ex.Message}");
            return (defaultValue, $"读取注册表值失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全写入注册表值
    /// </summary>
    public static (bool Success, string? Error) SetValue(RegistryKey root, string subKeyPath, string valueName, object value, RegistryValueKind kind = RegistryValueKind.Unknown)
    {
        var (key, error) = CreateOrOpenKey(root, subKeyPath);
        if (key == null)
        {
            return (false, error);
        }
        
        try
        {
            using (key)
            {
                if (kind == RegistryValueKind.Unknown)
                {
                    key.SetValue(valueName, value);
                }
                else
                {
                    key.SetValue(valueName, value, kind);
                }
                return (true, null);
            }
        }
        catch (SecurityException ex)
        {
            Debug.WriteLine($"[SafeRegistry] No permission to set value {valueName} in {subKeyPath}: {ex.Message}");
            return (false, ElevationGate.DescribeWriteDenial($"无权限修改注册表值: {valueName}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeRegistry] Write access denied for {valueName} in {subKeyPath}: {ex.Message}");
            return (false, ElevationGate.DescribeWriteDenial($"写入访问被拒绝: {valueName}"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeRegistry] Failed to set value {valueName} in {subKeyPath}: {ex.Message}");
            return (false, $"写入注册表值失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全删除注册表值
    /// </summary>
    public static (bool Success, string? Error) DeleteValue(RegistryKey root, string subKeyPath, string valueName, bool throwOnMissingValue = false)
    {
        var (key, error) = OpenKeyWritable(root, subKeyPath);
        if (key == null)
        {
            return (false, error);
        }
        
        try
        {
            using (key)
            {
                key.DeleteValue(valueName, throwOnMissingValue);
                return (true, null);
            }
        }
        catch (ArgumentException)
        {
            // 值不存在
            return (true, null);
        }
        catch (SecurityException ex)
        {
            Debug.WriteLine($"[SafeRegistry] No permission to delete value {valueName} from {subKeyPath}: {ex.Message}");
            return (false, ElevationGate.DescribeWriteDenial($"无权限删除注册表值: {valueName}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeRegistry] Delete access denied for {valueName} in {subKeyPath}: {ex.Message}");
            return (false, ElevationGate.DescribeWriteDenial($"删除访问被拒绝: {valueName}"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeRegistry] Failed to delete value {valueName} from {subKeyPath}: {ex.Message}");
            return (false, $"删除注册表值失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 检查注册表键是否存在
    /// </summary>
    public static bool KeyExists(RegistryKey root, string subKeyPath)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            return key != null;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查是否有写入权限
    /// </summary>
    public static bool HasWritePermission(RegistryKey root, string subKeyPath)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath, writable: true);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 事务性写入多个注册表值（原子性保证）
    /// P0-4: 防止部分写入导致数据不一致
    /// </summary>
    public static (bool Success, string? Error) SetValuesTransactional(
        RegistryKey root, 
        string subKeyPath, 
        Dictionary<string, (object Value, RegistryValueKind Kind)> values)
    {
        try
        {
            using var transaction = new RegistryTransaction(root, subKeyPath);
            
            foreach (var kvp in values)
            {
                transaction.SetValue(kvp.Key, kvp.Value.Value, kvp.Value.Kind);
            }
            
            return transaction.Commit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeRegistry] Transactional write failed: {ex.Message}");
            return (false, $"事务性写入失败: {ex.Message}");
        }
    }
}
