using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 游戏优化配置文件
/// </summary>
public class GameProfile
{
    /// <summary>
    /// 配置文件唯一ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 游戏名称
    /// </summary>
    public string GameName { get; set; } = string.Empty;

    /// <summary>
    /// 游戏可执行文件路径
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// 游戏图标路径
    /// </summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>
    /// 游戏类型（用于智能推荐优化项）
    /// </summary>
    public GameType GameType { get; set; } = GameType.General;

    /// <summary>
    /// 是否启用自动优化（启动游戏时自动应用）
    /// </summary>
    public bool AutoOptimizeOnLaunch { get; set; } = true;

    /// <summary>
    /// 是否在游戏退出后恢复设置
    /// </summary>
    public bool RestoreOnExit { get; set; } = true;

    /// <summary>
    /// 是否自动设置进程为高优先级
    /// </summary>
    public bool AutoSetHighPriority { get; set; } = false;

    /// <summary>
    /// 启用的优化操作ID列表
    /// </summary>
    public List<string> EnabledOperationIds { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 最后使用时间
    /// </summary>
    public DateTime LastUsedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 使用次数
    /// </summary>
    public int UsageCount { get; set; } = 0;
}


/// <summary>
/// 游戏配置文件管理器（单例模式，确保数据同步）
/// </summary>
public class GameProfileManager
{
    private static readonly object _instanceLock = new();
    private static GameProfileManager? _instance;
    
    public static GameProfileManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new GameProfileManager();
                }
            }
            return _instance;
        }
    }

    private static readonly string ProfilesPath = GameOptimizer.Core.Constants.AppPaths.GameProfilesFilePath;

    private readonly object _fileLock = new();
    private List<GameProfile> _profiles = new();
    private volatile bool _isLoaded;  // P2-032: 使用volatile确保线程可见性

    /// <summary>
    /// 所有配置文件
    /// </summary>
    public IReadOnlyList<GameProfile> Profiles
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
                if (!_isLoaded) Load();  // P2-032: 双重检查锁定
            }
        }
    }

    /// <summary>
    /// 加载所有配置文件（P1-002: 失败时自动从备份恢复）
    /// </summary>
    public void Load()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(ProfilesPath))
                {
                    var (content, error) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(ProfilesPath);
                    if (string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(content))
                    {
                        try
                        {
                            _profiles = JsonSerializer.Deserialize<List<GameProfile>>(content) ?? new List<GameProfile>();
                            _isLoaded = true;
                            return;
                        }
                        catch (JsonException jsonEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[GameProfileManager] JSON解析失败: {jsonEx.Message}");
                            // P1-002: JSON损坏，尝试从备份恢复
                            if (TryRestoreFromBackup())
                            {
                                _isLoaded = true;
                                return;
                            }
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 读取文件失败: {error}");
                        // P1-002: 读取失败，尝试从备份恢复
                        if (TryRestoreFromBackup())
                        {
                            _isLoaded = true;
                            return;
                        }
                    }
                }
                
                _profiles = new List<GameProfile>();
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 加载配置失败: {ex.Message}");
                _profiles = new List<GameProfile>();
                _isLoaded = true;
            }
        }
    }

    /// <summary>
    /// P1-002: 尝试从备份文件恢复配置
    /// </summary>
    private bool TryRestoreFromBackup()
    {
        try
        {
            var directory = Path.GetDirectoryName(ProfilesPath);
            if (string.IsNullOrEmpty(directory)) return false;

            var fileName = Path.GetFileName(ProfilesPath);
            var backupFiles = Directory.GetFiles(directory, $"{fileName}.backup.*")
                .OrderByDescending(f => f)
                .ToList();

            foreach (var backupFile in backupFiles)
            {
                try
                {
                    var (content, error) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(backupFile);
                    if (string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(content))
                    {
                        var profiles = JsonSerializer.Deserialize<List<GameProfile>>(content);
                        if (profiles != null)
                        {
                            _profiles = profiles;
                            System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 从备份恢复成功: {backupFile}");
                            
                            // 恢复成功后，将备份复制回主文件
                            File.Copy(backupFile, ProfilesPath, overwrite: true);
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 备份文件损坏 {backupFile}: {ex.Message}");
                    continue;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 所有备份文件都无法恢复");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 从备份恢复失败: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 强制重新加载（线程安全）
    /// </summary>
    public void Reload()
    {
        lock (_fileLock)
        {
            _isLoaded = false;
            Load();
        }
    }

    /// <summary>
    /// 保存所有配置文件（P1-002: 添加自动备份机制）
    /// </summary>
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
                        System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 创建目录失败: {dirError}");
                        return;
                    }
                }

                // P1-002: 保存前先创建备份
                CreateBackup();

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_profiles, options);
                var success = SafeFileWriter.TryWriteAllText(ProfilesPath, json);
                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 保存配置失败");
                }
                else
                {
                    // P1-002: 保存成功后清理旧备份
                    CleanOldBackups();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 保存配置失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// P1-002: 创建配置文件备份
    /// </summary>
    private void CreateBackup()
    {
        try
        {
            if (!File.Exists(ProfilesPath)) return;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = $"{ProfilesPath}.backup.{timestamp}";
            
            File.Copy(ProfilesPath, backupPath, overwrite: false);
            System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 备份创建成功: {backupPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 创建备份失败: {ex.Message}");
        }
    }

    /// <summary>
    /// P1-002: 清理旧备份文件（保留最近3个）
    /// </summary>
    private void CleanOldBackups()
    {
        try
        {
            var directory = Path.GetDirectoryName(ProfilesPath);
            if (string.IsNullOrEmpty(directory)) return;

            var fileName = Path.GetFileName(ProfilesPath);
            var backupFiles = Directory.GetFiles(directory, $"{fileName}.backup.*")
                .OrderByDescending(f => f)
                .ToList();

            // 保留最近3个备份
            foreach (var oldBackup in backupFiles.Skip(3))
            {
                try
                {
                    File.Delete(oldBackup);
                    System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 删除旧备份: {oldBackup}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 删除旧备份失败 {oldBackup}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameProfileManager] 清理旧备份失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 添加配置文件
    /// </summary>
    public void Add(GameProfile profile)
    {
        _profiles.Add(profile);
        Save();
    }

    /// <summary>
    /// 删除配置文件
    /// </summary>
    public void Remove(string profileId)
    {
        _profiles.RemoveAll(p => p.Id == profileId);
        Save();
    }

    /// <summary>
    /// 更新配置文件
    /// </summary>
    public void Update(GameProfile profile)
    {
        var index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
        {
            _profiles[index] = profile;
            Save();
        }
    }

    /// <summary>
    /// 根据可执行文件路径查找配置
    /// </summary>
    public GameProfile? FindByExecutable(string executablePath)
    {
        return _profiles.Find(p => 
            string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 根据进程名查找配置（不含扩展名）
    /// </summary>
    public GameProfile? FindByProcessName(string processName)
    {
        EnsureLoaded();
        return _profiles.Find(p =>
        {
            var exeName = Path.GetFileNameWithoutExtension(p.ExecutablePath);
            return string.Equals(exeName, processName, StringComparison.OrdinalIgnoreCase);
        });
    }
    
    /// <summary>
    /// 根据进程ID查找配置（用于轮询模式退出检测）
    /// </summary>
    public GameProfile? FindByProcessId(int processId)
    {
        EnsureLoaded();
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(processId);
            var processName = proc.ProcessName;
            return FindByProcessName(processName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 记录使用
    /// </summary>
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
