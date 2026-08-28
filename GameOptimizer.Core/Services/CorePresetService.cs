// File: GameOptimizer.Core/Services/CorePresetService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Utilities;
using GameOptimizer.Core.Diagnostics;

namespace GameOptimizer.Core.Services;

public class CorePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "⚙️";
    public string Description { get; set; } = string.Empty;
    public List<int> SelectedCores { get; set; } = new();
    public int? PreferredCore { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class CorePresetService
{
    private readonly string _configPath;
    private readonly string _lastConfigPath;
    private readonly string _configDir;
    private List<CorePreset> _userPresets = new();
    private readonly Lazy<bool> _directoryInitialized;
    
    public CorePresetService()
    {
        _configDir = AppPaths.ConfigDir;
        _configPath = Path.Combine(_configDir, "core_presets.json");
        _lastConfigPath = Path.Combine(_configDir, "last_core_config.json");
        
        // P2-001: 使用 Lazy<T> 确保线程安全的单次初始化
        _directoryInitialized = new Lazy<bool>(() =>
        {
            if (!SafeDirectoryOperations.TryCreateDirectory(_configDir, out var error))
            {
                var friendlyError = UserFriendlyErrorMapper.GetFriendlyMessage(
                    new IOException(error ?? "Unknown error"), $"创建配置目录: {_configDir}");
                System.Diagnostics.Debug.WriteLine($"[CorePresetService] {friendlyError}");
                return false;
            }
            return true;
        });
        
        Load();
    }
    
    /// <summary>
    /// 确保配置目录存在（线程安全的延迟初始化）
    /// </summary>
    private bool EnsureConfigDirectory()
    {
        return _directoryInitialized.Value;
    }
    
    public List<CorePreset> GetUserPresets() => _userPresets;
    
    public void SavePreset(CorePreset preset)
    {
        // 【P0-004 修复】：保存前确保目录存在
        if (!EnsureConfigDirectory()) return;
        
        var existing = _userPresets.FindIndex(p => p.Id == preset.Id);
        if (existing >= 0)
            _userPresets[existing] = preset;
        else
            _userPresets.Add(preset);
        Save();
    }
    
    public void DeletePreset(string id)
    {
        _userPresets.RemoveAll(p => p.Id == id);
        Save();
    }
    
    public CorePreset? GetLastUsedConfig()
    {
        var readPath = File.Exists(_lastConfigPath)
            ? _lastConfigPath
            : Path.Combine(AppPaths.LegacyAppDataRoot, "last_core_config.json");
        var (content, error) = SafeFileHelper.ReadAllText(readPath);
        if (content == null)
        {
            if (error != null && !error.Contains("不存在"))
            {
                System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to load last config: {error}");
            }
            return null;
        }
        
        try
        {
            return JsonSerializer.Deserialize<CorePreset>(content);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to deserialize last config: {ex.Message}");
            return null;
        }
    }
    
    public void SaveLastUsedConfig(CorePreset config)
    {
        if (!EnsureConfigDirectory()) return;
        
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            if (!SafeFileWriter.TryWriteAllText(_lastConfigPath, json, ex =>
            {
                System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to save last config: {ex.Message}");
            }))
            {
                System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to save last config (atomic write failed)");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to serialize last config: {ex.Message}");
        }
    }
    
    private void Load()
    {
        var readPath = File.Exists(_configPath)
            ? _configPath
            : Path.Combine(AppPaths.LegacyAppDataRoot, "core_presets.json");
        var (content, error) = SafeFileHelper.ReadAllText(readPath);
        if (content == null)
        {
            if (error != null && !error.Contains("不存在"))
            {
                System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to load presets: {error}");
            }
            _userPresets = new();
            return;
        }
        
        try
        {
            _userPresets = JsonSerializer.Deserialize<List<CorePreset>>(content) ?? new();
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to deserialize presets: {ex.Message}");
            _userPresets = new();
        }
    }
    
    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_userPresets, new JsonSerializerOptions { WriteIndented = true });
            if (!SafeFileWriter.TryWriteAllText(_configPath, json, ex =>
            {
                System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to save presets: {ex.Message}");
            }))
            {
                System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to save presets (atomic write failed)");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CorePresetService] Failed to serialize presets: {ex.Message}");
        }
    }
}
