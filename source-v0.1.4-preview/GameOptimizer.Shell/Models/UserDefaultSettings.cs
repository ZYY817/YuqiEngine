// File: GameOptimizer.Shell/Models/UserDefaultSettings.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 用户自定义默认配置 - 存储用户为特定模块保存的默认开关状态
/// </summary>
public sealed class UserDefaultSettings
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
    /// 保存时间戳
    /// </summary>
    public DateTime SavedAt { get; init; }
    
    /// <summary>
    /// 获取用户默认设置文件路径
    /// </summary>
    private static string GetFilePath(string moduleKey)
    {
        var dir = Path.Combine(AppPaths.ConfigDir, "UserDefaults");
        return Path.Combine(dir, $"{moduleKey}_defaults.json");
    }
    
    /// <summary>
    /// 加载指定模块的用户默认设置
    /// </summary>
    /// <param name="moduleKey">模块标识符</param>
    /// <returns>用户默认设置，如果不存在则返回null</returns>
    public static UserDefaultSettings? Load(string moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            System.Diagnostics.Debug.WriteLine("[UserDefaultSettings] ModuleKey is null or empty");
            return null;
        }
        
        var filePath = GetFilePath(moduleKey);
        
        if (!File.Exists(filePath))
        {
            return null;
        }
        
        try
        {
            var (content, error) = SafeFileHelper.ReadAllText(filePath);
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(content))
            {
                System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Failed to read {moduleKey}: {error ?? "empty content"}");
                return null;
            }
            
            var settings = JsonSerializer.Deserialize<UserDefaultSettings>(content);
            if (settings == null)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Deserialization returned null for {moduleKey}");
                return null;
            }
            
            return settings;
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] JSON parse error for {moduleKey}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Load failed for {moduleKey}: {ex.Message}");
            return null;
        }
    }
    
    public bool Save()
    {
        if (string.IsNullOrWhiteSpace(ModuleKey))
        {
            System.Diagnostics.Debug.WriteLine("[UserDefaultSettings] Cannot save: ModuleKey is null or empty");
            return false;
        }
        
        try
        {
            var filePath = GetFilePath(ModuleKey);
            var directory = Path.GetDirectoryName(filePath);
            
            if (!string.IsNullOrEmpty(directory))
            {
                if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(directory, out var dirError))
                {
                    System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Failed to create directory: {dirError}");
                    return false;
                }
            }
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            
            var writeSuccess = SafeFileWriter.TryWriteAllText(filePath, json);
            if (!writeSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Failed to write file for {ModuleKey}");
                return false;
            }
            
            System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Saved defaults for {ModuleKey} with {ItemStates.Count} items");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Save failed for {ModuleKey}: {ex.Message}");
            return false;
        }
    }
    
    public static bool Delete(string moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            return false;
        }
        
        try
        {
            var filePath = GetFilePath(moduleKey);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Deleted defaults for {moduleKey}");
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDefaultSettings] Delete failed for {moduleKey}: {ex.Message}");
            return false;
        }
    }
}
