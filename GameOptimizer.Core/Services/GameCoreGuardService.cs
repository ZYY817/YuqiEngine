// File: GameOptimizer.Core/Services/GameCoreGuardService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

public class GameCoreGuardService : IDisposable
{
    private readonly CpuTopologyService _topologyService;
    private readonly CoreAffinityService _affinityService;
    private readonly string _settingsPath;
    private readonly string _profilesPath;
    
    private CoreAcceleratorSettings _settings;
    private List<GameCoreProfile> _builtInProfiles = new();
    private Timer? _guardTimer;
    private bool _isRunning;
    private readonly object _lock = new();
    
    public event EventHandler<GameOptimizedEventArgs>? GameOptimized;
    public event EventHandler<GameOptimizedEventArgs>? GameExited;
    public event EventHandler<string>? StatusChanged;
    
    public bool IsGuardRunning => _isRunning;
    public CoreAcceleratorSettings Settings => _settings;
    public CpuTopology Topology => _topologyService.GetTopology();
    
    public GameCoreGuardService(CpuTopologyService topologyService, CoreAffinityService affinityService)
    {
        _topologyService = topologyService;
        _affinityService = affinityService;
        
        // P2-006: 使用 AppPaths 常量替代硬编码路径
        var appData = Constants.AppPaths.CoreAcceleratorDir;
        if (!Utilities.SafeDirectoryOperations.TryCreateDirectory(appData, out var dirError))
        {
            System.Diagnostics.Debug.WriteLine($"[GameCoreGuardService] Failed to create directory: {dirError}");
        }
        _settingsPath = Constants.AppPaths.CoreAcceleratorSettingsFilePath;
        _profilesPath = Constants.AppPaths.GameCoreProfilesFilePath;
        
        _settings = LoadSettings();
        LoadBuiltInProfiles();
    }
    
    public void StartGuard()
    {
        lock (_lock)
        {
            if (_isRunning) return;
            _isRunning = true;
            _guardTimer = new Timer(GuardCallback, null, 0, _settings.GuardIntervalMs);
            StatusChanged?.Invoke(this, "守护已启动");
        }
    }
    
    public void StopGuard()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            _guardTimer?.Dispose();
            _guardTimer = null;
            StatusChanged?.Invoke(this, "守护已停止");
        }
    }
    
    public void SetMode(AcceleratorMode mode)
    {
        _settings.Mode = mode;
        SaveSettings();
        
        if (mode == AcceleratorMode.AutoGuard && !_isRunning)
            StartGuard();
        else if (mode != AcceleratorMode.AutoGuard && _isRunning)
            StopGuard();
    }
    
    private void GuardCallback(object? state)
    {
        if (!_isRunning) return;
        
        try
        {
            ScanAndOptimize();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameCoreGuardService] 守护回调异常: {ex.Message}");
        }
    }
    
    public void ScanAndOptimize()
    {
        var allProfiles = GetAllProfiles();
        var runningProcesses = Process.GetProcesses();
        try
        {
            var optimizedPids = new HashSet<int>();
            
            foreach (var profile in allProfiles.Where(p => p.IsEnabled))
            {
                var matchingProcesses = runningProcesses
                    .Where(p => MatchesProfile(p, profile))
                    .ToList();
                
                foreach (var process in matchingProcesses)
                {
                    try
                    {
                        if (_affinityService.IsOptimized(process.Id))
                        {
                            optimizedPids.Add(process.Id);
                            continue;
                        }
                        
                        var result = _affinityService.ApplyAffinity(process, profile);
                        if (result.IsSuccess)
                        {
                            profile.IsRunning = true;
                            profile.IsOptimized = true;
                            profile.ProcessId = process.Id;
                            profile.LastUsed = DateTime.Now;
                            optimizedPids.Add(process.Id);
                            
                            GameOptimized?.Invoke(this, new GameOptimizedEventArgs
                            {
                                Profile = profile,
                                ProcessId = process.Id,
                                Message = result.Message,
                                CoreCount = result.CoreCount
                            });
                            
                            StatusChanged?.Invoke(this, $"已优化: {profile.GameName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GameCoreGuardService] 优化 {profile.GameName} 失败: {ex.Message}");
                    }
                }
                
                // 检查游戏是否已退出
                if (profile.IsOptimized && profile.ProcessId.HasValue && !optimizedPids.Contains(profile.ProcessId.Value))
                {
                    profile.IsRunning = false;
                    profile.IsOptimized = false;
                    _affinityService.RestoreAffinity(profile.ProcessId.Value);
                    
                    GameExited?.Invoke(this, new GameOptimizedEventArgs
                    {
                        Profile = profile,
                        ProcessId = profile.ProcessId.Value,
                        Message = "游戏已退出"
                    });
                    
                    profile.ProcessId = null;
                }
            }
        }
        finally
        {
            foreach (var p in runningProcesses)
            {
                try { p.Dispose(); } catch { }
            }
        }
    }
    
    public AffinityResult OptimizeProcess(Process process, GameCoreProfile? profile = null)
    {
        if (profile == null)
        {
            profile = new GameCoreProfile
            {
                GameName = process.ProcessName,
                ExeName = process.ProcessName + ".exe",
                Strategy = _topologyService.GetRecommendedStrategy(Topology)
            };
        }
        
        return _affinityService.ApplyAffinity(process, profile);
    }
    
    public AffinityResult OptimizeByExeName(string exeName, GameCoreProfile? profile = null)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName));
        if (processes.Length == 0)
            return AffinityResult.Failed($"未找到进程: {exeName}");
        
        try
        {
            return OptimizeProcess(processes[0], profile);
        }
        finally
        {
            // Dispose unused processes (skip [0] which is used by OptimizeProcess)
            for (var i = 1; i < processes.Length; i++)
            {
                try { processes[i].Dispose(); } catch { }
            }
        }
    }
    
    public bool StopOptimization(int processId)
    {
        return _affinityService.RestoreAffinity(processId);
    }
    
    public void StopAllOptimizations()
    {
        _affinityService.RestoreAll();
        foreach (var profile in GetAllProfiles())
        {
            profile.IsRunning = false;
            profile.IsOptimized = false;
            profile.ProcessId = null;
        }
    }
    
    public List<GameCoreProfile> GetAllProfiles()
    {
        var profiles = new List<GameCoreProfile>();
        profiles.AddRange(_builtInProfiles);
        profiles.AddRange(_settings.CustomProfiles);
        return profiles;
    }
    
    public List<GameCoreProfile> GetRunningOptimizedGames()
    {
        return GetAllProfiles().Where(p => p.IsOptimized && p.IsRunning).ToList();
    }
    
    public void AddCustomProfile(GameCoreProfile profile)
    {
        profile.IsBuiltIn = false;
        _settings.CustomProfiles.Add(profile);
        SaveSettings();
    }
    
    public void UpdateProfile(GameCoreProfile profile)
    {
        if (profile.IsBuiltIn)
        {
            var existing = _builtInProfiles.FirstOrDefault(p => p.Id == profile.Id);
            if (existing != null)
            {
                existing.Strategy = profile.Strategy;
                existing.CustomCoreIndices = profile.CustomCoreIndices;
                existing.PreferredCore = profile.PreferredCore;
                existing.DisableSmt = profile.DisableSmt;
                existing.Priority = profile.Priority;
                existing.IsEnabled = profile.IsEnabled;
            }
        }
        else
        {
            var idx = _settings.CustomProfiles.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0)
            {
                _settings.CustomProfiles[idx] = profile;
                SaveSettings();
            }
        }
    }
    
    public void RemoveCustomProfile(string profileId)
    {
        _settings.CustomProfiles.RemoveAll(p => p.Id == profileId);
        SaveSettings();
    }
    
    public GameCoreProfile? FindProfileByExe(string exeName)
    {
        var name = Path.GetFileNameWithoutExtension(exeName).ToLowerInvariant();
        return GetAllProfiles().FirstOrDefault(p => 
            Path.GetFileNameWithoutExtension(p.ExeName).ToLowerInvariant() == name);
    }
    
    private bool MatchesProfile(Process process, GameCoreProfile profile)
    {
        try
        {
            var processName = process.ProcessName.ToLowerInvariant();
            var profileExe = Path.GetFileNameWithoutExtension(profile.ExeName).ToLowerInvariant();
            return processName == profileExe;
        }
        catch
        {
            return false;
        }
    }
    
    private CoreAcceleratorSettings LoadSettings()
    {
        try
        {
            var (content, error) = SafeFileHelper.ReadAllText(_settingsPath);
            if (!string.IsNullOrEmpty(content))
            {
                return JsonSerializer.Deserialize<CoreAcceleratorSettings>(content) ?? new CoreAcceleratorSettings();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameCoreGuardService] 加载设置失败: {ex.Message}");
        }
        return new CoreAcceleratorSettings();
    }
    
    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            string? errorMsg = null;
            var success = SafeFileWriter.TryWriteAllText(_settingsPath, json, ex => errorMsg = ex.Message);
            if (!success)
            {
                Debug.WriteLine($"[GameCoreGuardService] 保存设置失败: {errorMsg}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameCoreGuardService] 保存设置失败: {ex.Message}");
        }
    }
    
    private void LoadBuiltInProfiles()
    {
        _builtInProfiles = GetDefaultGameProfiles();
    }
    
    private List<GameCoreProfile> GetDefaultGameProfiles()
    {
        var topology = _topologyService.GetTopology();
        var defaultStrategy = _topologyService.GetRecommendedStrategy(topology);
        
        return new List<GameCoreProfile>
        {
            // 热门游戏
            CreateProfile("永劫无间", "NarakaBladepoint.exe", "动作"),
            CreateProfile("原神", "GenshinImpact.exe", "RPG"),
            CreateProfile("原神", "YuanShen.exe", "RPG"),
            CreateProfile("崩坏：星穹铁道", "StarRail.exe", "RPG"),
            CreateProfile("绝地求生", "TslGame.exe", "射击"),
            CreateProfile("CS2", "cs2.exe", "射击"),
            CreateProfile("CSGO", "csgo.exe", "射击"),
            CreateProfile("Apex英雄", "r5apex.exe", "射击"),
            CreateProfile("守望先锋2", "Overwatch.exe", "射击"),
            CreateProfile("无畏契约", "VALORANT-Win64-Shipping.exe", "射击"),
            CreateProfile("英雄联盟", "League of Legends.exe", "MOBA"),
            CreateProfile("DOTA2", "dota2.exe", "MOBA"),
            CreateProfile("王者荣耀", "sgame.exe", "MOBA"),
            CreateProfile("赛博朋克2077", "Cyberpunk2077.exe", "RPG"),
            CreateProfile("艾尔登法环", "eldenring.exe", "RPG"),
            CreateProfile("黑神话：悟空", "b1.exe", "动作"),
            CreateProfile("GTA5", "GTA5.exe", "开放世界"),
            CreateProfile("荒野大镖客2", "RDR2.exe", "开放世界"),
            CreateProfile("战地2042", "BF2042.exe", "射击"),
            CreateProfile("使命召唤", "cod.exe", "射击"),
            CreateProfile("彩虹六号：围攻", "RainbowSix.exe", "射击"),
            CreateProfile("逃离塔科夫", "EscapeFromTarkov.exe", "射击"),
            CreateProfile("暗黑破坏神4", "Diablo IV.exe", "RPG"),
            CreateProfile("魔兽世界", "Wow.exe", "MMORPG"),
            CreateProfile("最终幻想14", "ffxiv_dx11.exe", "MMORPG"),
            CreateProfile("命运2", "destiny2.exe", "射击"),
            CreateProfile("怪物猎人：世界", "MonsterHunterWorld.exe", "动作"),
            CreateProfile("怪物猎人：崛起", "MonsterHunterRise.exe", "动作"),
            CreateProfile("只狼", "sekiro.exe", "动作"),
            CreateProfile("极限竞速：地平线5", "ForzaHorizon5.exe", "竞速"),
            CreateProfile("极限竞速", "ForzaMotorsport.exe", "竞速"),
            CreateProfile("F1 23", "F1_23.exe", "竞速"),
            CreateProfile("神界：原罪2", "bg3.exe", "RPG"),
            CreateProfile("博德之门3", "bg3_dx11.exe", "RPG"),
            CreateProfile("星空", "Starfield.exe", "RPG"),
            CreateProfile("霍格沃茨之遗", "HogwartsLegacy.exe", "RPG"),
            CreateProfile("死亡搁浅", "DeathStranding.exe", "动作"),
            CreateProfile("地平线：零之曙光", "HorizonZeroDawn.exe", "动作"),
            CreateProfile("战神", "GoW.exe", "动作"),
            CreateProfile("蜘蛛侠", "Spider-Man.exe", "动作"),
            CreateProfile("生化危机4", "re4.exe", "恐怖"),
            CreateProfile("生化危机8", "re8.exe", "恐怖"),
            CreateProfile("艾伦·韦克2", "AlanWake2.exe", "恐怖"),
            CreateProfile("心灵杀手2", "Remedy.exe", "恐怖"),
            CreateProfile("我的世界", "javaw.exe", "沙盒"),
            CreateProfile("泰拉瑞亚", "Terraria.exe", "沙盒"),
            CreateProfile("饥荒", "dontstarve_steam.exe", "生存"),
            CreateProfile("方舟：生存进化", "ShooterGame.exe", "生存"),
            CreateProfile("帕鲁世界", "Palworld-Win64-Shipping.exe", "生存"),
        };
    }
    
    private GameCoreProfile CreateProfile(string name, string exe, string category)
    {
        return new GameCoreProfile
        {
            Id = $"builtin_{exe.ToLowerInvariant()}",
            GameName = name,
            ExeName = exe,
            IsBuiltIn = true,
            Strategy = CoreStrategy.Auto,
            Priority = ProcessPriorityLevel.High
        };
    }
    
    public void Dispose()
    {
        StopGuard();
        _guardTimer?.Dispose();
    }
}

public class GameOptimizedEventArgs : EventArgs
{
    public GameCoreProfile Profile { get; set; } = null!;
    public int ProcessId { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CoreCount { get; set; }
}
