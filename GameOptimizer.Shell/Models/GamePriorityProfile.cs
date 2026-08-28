// File: GameOptimizer.Shell/Models/GamePriorityProfile.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 游戏进程优先级配置（简化版，仅用于优先级管理）
/// </summary>
public class GamePriorityProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GameName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public ProcessPriorityClass Priority { get; set; } = ProcessPriorityClass.High;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
    public int UsageCount { get; set; } = 0;
}

/// <summary>
/// 游戏优先级配置管理器（单例模式，线程安全）
/// </summary>
public class GamePriorityProfileManager
{
    private static readonly object _instanceLock = new();
    private static GamePriorityProfileManager? _instance;
    
    public static GamePriorityProfileManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new GamePriorityProfileManager();
                }
            }
            return _instance;
        }
    }

    private static string ProfilesPath => AppPaths.GamePriorityProfilesFilePath;

    private readonly object _fileLock = new();
    private List<GamePriorityProfile> _profiles = new();
    private volatile bool _isLoaded;

    public IReadOnlyList<GamePriorityProfile> Profiles
    {
        get
        {
            EnsureLoaded();
            return _profiles.AsReadOnly();
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
            LoadInternal();
        }
    }
    
    public void Reload()
    {
        lock (_fileLock)
        {
            _isLoaded = false;
            LoadInternal();
        }
    }

    private void LoadInternal()
    {
        try
        {
            if (File.Exists(ProfilesPath))
            {
                var (content, error) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(ProfilesPath);
                if (string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(content))
                {
                    _profiles = JsonSerializer.Deserialize<List<GamePriorityProfile>>(content) ?? new List<GamePriorityProfile>();
                }
                else
                {
                    _profiles = new List<GamePriorityProfile>();
                }
            }
            else
            {
                _profiles = new List<GamePriorityProfile>();
            }
            _isLoaded = true;
            Debug.WriteLine($"[GamePriorityProfileManager] LoadInternal completed. Profile count: {_profiles.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePriorityProfileManager] LoadInternal failed: {ex.Message}");
            _profiles = new List<GamePriorityProfile>();
            _isLoaded = true;
        }
    }

    public void Save()
    {
        lock (_fileLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(ProfilesPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(directory, out var dirError))
                    {
                        Debug.WriteLine($"[GamePriorityProfileManager] 创建目录失败: {dirError}");
                        return;
                    }
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_profiles, options);
                var success = SafeFileWriter.TryWriteAllText(ProfilesPath, json);
                if (!success)
                {
                    Debug.WriteLine($"[GamePriorityProfileManager] 保存失败");
                    return;
                }
                
                Debug.WriteLine($"[GamePriorityProfileManager] Saved {_profiles.Count} profile(s) to: {ProfilesPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamePriorityProfileManager] Save failed: {ex.Message}");
                Debug.WriteLine($"[GamePriorityProfileManager] Stack trace: {ex.StackTrace}");
            }
        }
    }

    public void Add(GamePriorityProfile profile)
    {
        EnsureLoaded();
        lock (_fileLock)
        {
            _profiles.Add(profile);
            Debug.WriteLine($"[GamePriorityProfileManager] Added profile: {profile.GameName} (ID: {profile.Id})");
            Save();
        }
    }

    public void Remove(string profileId)
    {
        EnsureLoaded();
        lock (_fileLock)
        {
            var removed = _profiles.RemoveAll(p => p.Id == profileId);
            Debug.WriteLine($"[GamePriorityProfileManager] Removed {removed} profile(s) with ID: {profileId}");
            Save();
        }
    }

    public void Update(GamePriorityProfile profile)
    {
        EnsureLoaded();
        lock (_fileLock)
        {
            var index = _profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
            {
                _profiles[index] = profile;
                Debug.WriteLine($"[GamePriorityProfileManager] Updated profile: {profile.GameName} (ID: {profile.Id})");
                Save();
            }
            else
            {
                Debug.WriteLine($"[GamePriorityProfileManager] Update failed: Profile not found (ID: {profile.Id})");
            }
        }
    }

    public GamePriorityProfile? FindByExecutable(string executablePath)
    {
        return _profiles.Find(p => 
            string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public GamePriorityProfile? FindByProcessName(string processName)
    {
        EnsureLoaded();
        return _profiles.Find(p =>
        {
            var exeName = Path.GetFileNameWithoutExtension(p.ExecutablePath);
            return string.Equals(exeName, processName, StringComparison.OrdinalIgnoreCase);
        });
    }
    
    public void RecordUsage(string profileId)
    {
        var profile = _profiles.Find(p => p.Id == profileId);
        if (profile != null)
        {
            profile.LastUsedAt = DateTime.Now;
            profile.UsageCount++;
            Save();
        }
    }
}
