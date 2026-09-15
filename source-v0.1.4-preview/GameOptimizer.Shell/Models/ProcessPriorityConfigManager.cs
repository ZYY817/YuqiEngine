// File: GameOptimizer.Shell/Models/ProcessPriorityConfigManager.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.ProcessPriority;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 进程优先级配置管理器（单例模式，线程安全）
/// </summary>
public sealed class ProcessPriorityConfigManager
{
    private static readonly object _instanceLock = new();
    private static ProcessPriorityConfigManager? _instance;
    
    public static ProcessPriorityConfigManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new ProcessPriorityConfigManager();
                }
            }
            return _instance;
        }
    }

    private static readonly string ConfigPath = Path.Combine(AppPaths.ConfigDir, "process_priority_configs.json");

    private readonly object _fileLock = new();
    private List<ProcessPriorityConfig> _configs = new();
    private volatile bool _isLoaded;

    private ProcessPriorityConfigManager() { }

    public IReadOnlyList<ProcessPriorityConfig> Configs
    {
        get
        {
            EnsureLoaded();
            return _configs.AsReadOnly();
        }
    }

    private void EnsureLoaded()
    {
        if (!_isLoaded)
        {
            lock (_fileLock)
            {
                if (!_isLoaded) Load();
            }
        }
    }

    public void Load()
    {
        lock (_fileLock)
        {
            try
            {
                var readPath = File.Exists(ConfigPath)
                    ? ConfigPath
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GameOptimizer",
                        "process_priority_configs.json");
                if (File.Exists(readPath))
                {
                    var json = File.ReadAllText(readPath);
                    _configs = JsonSerializer.Deserialize<List<ProcessPriorityConfig>>(json) 
                        ?? new List<ProcessPriorityConfig>();
                    
                    Debug.WriteLine($"[ProcessPriorityConfigManager] Loaded {_configs.Count} config(s)");
                }
                else
                {
                    _configs = new List<ProcessPriorityConfig>();
                    Debug.WriteLine("[ProcessPriorityConfigManager] No config file found, starting fresh");
                }
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessPriorityConfigManager] Load failed: {ex.Message}");
                _configs = new List<ProcessPriorityConfig>();
                _isLoaded = true;
            }
        }
    }

    public void Reload()
    {
        lock (_fileLock)
        {
            _isLoaded = false;
            Load();
        }
    }

    private void Save()
    {
        lock (_fileLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_configs, options);
                SafeFileWriter.WriteAllText(ConfigPath, json);
                
                Debug.WriteLine($"[ProcessPriorityConfigManager] Saved {_configs.Count} config(s)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessPriorityConfigManager] Save failed: {ex.Message}");
            }
        }
    }

    public void Add(ProcessPriorityConfig config)
    {
        Guard.NotNull(config, nameof(config));
        
        EnsureLoaded();
        lock (_fileLock)
        {
            if (_configs.Any(c => c.ExecutableName.Equals(config.ExecutableName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Configuration for '{config.ExecutableName}' already exists");
            }
            
            config.CreatedAt = DateTime.Now;
            config.ModifiedAt = DateTime.Now;
            _configs.Add(config);
            
            Debug.WriteLine($"[ProcessPriorityConfigManager] Added: {config.GameName} ({config.ExecutableName})");
            Save();
        }
    }

    public void Update(ProcessPriorityConfig config)
    {
        Guard.NotNull(config, nameof(config));
        
        EnsureLoaded();
        lock (_fileLock)
        {
            var index = _configs.FindIndex(c => c.Id == config.Id);
            if (index < 0)
            {
                throw new InvalidOperationException($"Configuration with ID '{config.Id}' not found");
            }
            
            config.ModifiedAt = DateTime.Now;
            _configs[index] = config;
            
            Debug.WriteLine($"[ProcessPriorityConfigManager] Updated: {config.GameName} ({config.ExecutableName})");
            Save();
        }
    }

    public void Remove(string configId)
    {
        Guard.NotNullOrWhiteSpace(configId, nameof(configId));
        
        EnsureLoaded();
        lock (_fileLock)
        {
            var removed = _configs.RemoveAll(c => c.Id == configId);
            if (removed > 0)
            {
                Debug.WriteLine($"[ProcessPriorityConfigManager] Removed config: {configId}");
                Save();
            }
        }
    }

    public ProcessPriorityConfig? FindById(string configId)
    {
        EnsureLoaded();
        return _configs.FirstOrDefault(c => c.Id == configId);
    }

    public ProcessPriorityConfig? FindByExecutableName(string executableName)
    {
        EnsureLoaded();
        return _configs.FirstOrDefault(c => 
            c.ExecutableName.Equals(executableName, StringComparison.OrdinalIgnoreCase));
    }

    public int GetTotalCount()
    {
        EnsureLoaded();
        return _configs.Count;
    }

    public int GetEnabledCount()
    {
        EnsureLoaded();
        return _configs.Count(c => c.IsEnabled);
    }

    public DateTime? GetLastModifiedDate()
    {
        EnsureLoaded();
        return _configs.Count > 0 
            ? _configs.Max(c => c.ModifiedAt) 
            : null;
    }
}
