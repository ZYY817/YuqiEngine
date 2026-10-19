using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举

namespace GameOptimizer.Core.Services.CpuAffinity
{
    public interface IProcessAffinityService
    {
        CpuInfo GetCpuInfo();
        Task<IReadOnlyList<ProcessAffinityProfile>> GetProfilesAsync();
        Task SaveProfileAsync(ProcessAffinityProfile profile);
        Task DeleteProfileAsync(string processName);
        Task<bool> ApplyProfileAsync(ProcessAffinityProfile profile);
        Task ApplyAllProfilesAsync();
        ProcessAffinityProfile CreateRecommendedProfile(string processName, ProcessType type);
        IReadOnlyList<RunningProcess> GetRunningProcesses();
        long CalculateAffinityMask(IEnumerable<int> threads);
        IEnumerable<int> GetThreadsFromMask(long mask);
        ProfileStatus CheckProfileStatus(ProcessAffinityProfile profile);
    }

    public enum ProfileStatus
    {
        NotRunning,     // 进程未运行
        Applied,        // 已生效
        Pending,        // 待应用（进程运行但亲和性未设置）
        Unknown         // 进程存在，但权限或状态读取失败
    }

    public enum ProcessType
    {
        Game,           // 游戏 - 使用P核心
        Background,     // 后台程序 - 使用E核心
        Browser,        // 浏览器 - 使用E核心
        Communication,  // 通讯软件 - 使用E核心末端
        System,         // 系统进程 - 不修改
        Custom          // 自定义
    }

    public class ProcessAffinityProfile
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public ProcessType Type { get; set; } = ProcessType.Custom;
        public long AffinityMask { get; set; }
        public List<int> AssignedThreads { get; set; } = new();
        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Icon { get; set; } = "📦";
        public string AffinityDescription { get; set; } = string.Empty;
        public Guid? ActiveReceiptId { get; set; }
        public int? ActiveProcessId { get; set; }
    }

    public class RunningProcess
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public string Icon { get; set; } = "📦";
    }

    public class ProcessAffinityService : IProcessAffinityService
    {
        // P2-006: 使用 AppPaths 常量替代硬编码路径
        private static readonly string ProfilesDir = GameOptimizer.Core.Constants.AppPaths.AffinityProfilesDir;
        private static readonly string ProfilesFile = GameOptimizer.Core.Constants.AppPaths.AffinityProfilesFilePath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ICpuAffinityService _cpuService;
        private CpuInfo? _cachedCpuInfo;

        public ProcessAffinityService() : this(new CpuAffinityService()) { }

        public ProcessAffinityService(ICpuAffinityService cpuService)
        {
            _cpuService = cpuService;
        }

        public CpuInfo GetCpuInfo()
        {
            _cachedCpuInfo ??= _cpuService.GetCpuInfo();
            return _cachedCpuInfo;
        }

        public async Task<IReadOnlyList<ProcessAffinityProfile>> GetProfilesAsync()
        {
            if (!File.Exists(ProfilesFile))
                return new List<ProcessAffinityProfile>();

            try
            {
                var json = await File.ReadAllTextAsync(ProfilesFile);
                var profiles = JsonSerializer.Deserialize<List<ProcessAffinityProfile>>(json, JsonOptions);
                return profiles ?? new List<ProcessAffinityProfile>();
            }
            catch
            {
                return new List<ProcessAffinityProfile>();
            }
        }

        public async Task SaveProfileAsync(ProcessAffinityProfile profile)
        {
            Directory.CreateDirectory(ProfilesDir);
            var profiles = (await GetProfilesAsync()).ToList();
            
            var existing = profiles.FindIndex(p => 
                p.ProcessName.Equals(profile.ProcessName, StringComparison.OrdinalIgnoreCase));
            
            if (existing >= 0)
                profiles[existing] = profile;
            else
                profiles.Add(profile);

            var json = JsonSerializer.Serialize(profiles, JsonOptions);
            string? errorMsg = null;
            var success = SafeFileWriter.TryWriteAllText(ProfilesFile, json, ex => errorMsg = ex.Message);
            if (!success)
            {
                throw new IOException($"Failed to save affinity profile: {errorMsg}");
            }
        }

        public async Task DeleteProfileAsync(string processName)
        {
            var profiles = (await GetProfilesAsync()).ToList();
            profiles.RemoveAll(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            
            var json = JsonSerializer.Serialize(profiles, JsonOptions);
            string? errorMsg = null;
            var success = SafeFileWriter.TryWriteAllText(ProfilesFile, json, ex => errorMsg = ex.Message);
            if (!success)
            {
                throw new IOException($"Failed to delete affinity profile: {errorMsg}");
            }
        }

        // 系统关键进程保护列表 - 这些进程不应该被修改亲和性
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit",
            "services", "lsass", "svchost", "dwm", "fontdrvhost",
            "WmiPrvSE", "SearchIndexer", "SecurityHealthService",
            "explorer", "winlogon", "sihost", "taskhostw", "RuntimeBroker",
            "ShellExperienceHost", "StartMenuExperienceHost", "ctfmon",
            "conhost", "dllhost", "audiodg", "spoolsv"
        };

        /// <summary>
        /// Legacy compatibility entry. Profile persistence is not an execution authority:
        /// runtime mutation must go through IProcessTuningOwner with a PID-bound plan.
        /// </summary>
        public Task<bool> ApplyProfileAsync(ProcessAffinityProfile profile)
        {
            System.Diagnostics.Debug.WriteLine("[ProcessAffinityService] Legacy profile apply is fail-closed; use IProcessTuningOwner.");
            return Task.FromResult(false);
        }

        /// <summary>
        /// A name-based batch can match reused or multiple PIDs, so it is permanently blocked.
        /// The Shell must prepare one explicitly confirmed PID-bound plan per target.
        /// </summary>
        public Task ApplyAllProfilesAsync()
        {
            System.Diagnostics.Debug.WriteLine("[ProcessAffinityService] Legacy bulk profile apply is fail-closed; use IProcessTuningOwner.");
            return Task.CompletedTask;
        }

        public ProcessAffinityProfile CreateRecommendedProfile(string processName, ProcessType type)
        {
            var cpuInfo = GetCpuInfo();
            var profile = new ProcessAffinityProfile
            {
                ProcessName = processName,
                DisplayName = Path.GetFileNameWithoutExtension(processName),
                Type = type,
                Icon = GetIconForType(type)
            };

            switch (type)
            {
                case ProcessType.Game:
                    profile.AssignedThreads = GetPCoreThreads(cpuInfo);
                    profile.AffinityDescription = cpuInfo.HasHybridArchitecture 
                        ? "仅P核心 (高性能)" 
                        : $"核心 0-{Math.Min(7, cpuInfo.LogicalProcessors - 1)}";
                    break;

                case ProcessType.Background:
                case ProcessType.Browser:
                    profile.AssignedThreads = GetECoreThreads(cpuInfo);
                    profile.AffinityDescription = cpuInfo.HasHybridArchitecture 
                        ? "仅E核心 (效率)" 
                        : $"核心 {cpuInfo.LogicalProcessors / 2}-{cpuInfo.LogicalProcessors - 1}";
                    break;

                case ProcessType.Communication:
                    profile.AssignedThreads = GetLastECoreThreads(cpuInfo, 4);
                    profile.AffinityDescription = cpuInfo.HasHybridArchitecture 
                        ? "E核心末端 (低优先级)" 
                        : $"核心 {cpuInfo.LogicalProcessors - 4}-{cpuInfo.LogicalProcessors - 1}";
                    break;

                default:
                    profile.AssignedThreads = Enumerable.Range(0, cpuInfo.LogicalProcessors).ToList();
                    profile.AffinityDescription = "所有核心";
                    break;
            }

            profile.AffinityMask = CalculateAffinityMask(profile.AssignedThreads);
            return profile;
        }

        public IReadOnlyList<RunningProcess> GetRunningProcesses()
        {
            var result = new List<RunningProcess>();
            var systemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Idle", "Registry", "smss", "csrss", "wininit", 
                "services", "lsass", "svchost", "dwm", "fontdrvhost",
                "WmiPrvSE", "SearchIndexer", "SecurityHealthService"
            };

            // Prefer safe window-process enumeration to avoid complete failure on restricted machines.
            var safeEnumeration = GameOptimizer.Core.Services.SafeProcessHelper.EnumerateWindowProcesses(
                excludeByName: name => systemProcesses.Contains(name),
                maxWindowTitleLength: 120);

            if (safeEnumeration.Processes.Count > 0)
            {
                foreach (var item in safeEnumeration.Processes)
                {
                    if (item.Id <= 4)
                        continue;

                    long memoryUsage = 0;
                    var (proc, _) = GameOptimizer.Core.Services.SafeProcessHelper.GetProcessById(item.Id);
                    if (proc != null)
                    {
                        try
                        {
                            memoryUsage = proc.WorkingSet64;
                        }
                        catch { }
                        finally
                        {
                            proc.Dispose();
                        }
                    }

                    result.Add(new RunningProcess
                    {
                        Pid = item.Id,
                        Name = item.Name,
                        FileName = item.Name,
                        MemoryUsage = memoryUsage,
                        Icon = GuessProcessIcon(item.Name)
                    });
                }

                return result
                    .OrderByDescending(p => p.MemoryUsage)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(50)
                    .ToList();
            }

            Process[] allProcesses;
            try
            {
                allProcesses = Process.GetProcesses();
            }
            catch
            {
                return result;
            }

            foreach (var proc in allProcesses)
            {
                try
                {
                    if (systemProcesses.Contains(proc.ProcessName))
                        continue;

                    if (proc.Id <= 4)
                        continue;

                    // 快速获取基本信息，不访问 MainModule（可能很慢）
                    string fileName = proc.ProcessName;
                    long memoryUsage = 0;
                    
                    try
                    {
                        memoryUsage = proc.WorkingSet64;
                    }
                    catch { /* 忽略无法访问的进程 */ }

                    result.Add(new RunningProcess
                    {
                        Pid = proc.Id,
                        Name = proc.ProcessName,
                        FileName = fileName,
                        MemoryUsage = memoryUsage,
                        Icon = GuessProcessIcon(proc.ProcessName)
                    });
                }
                catch { /* 忽略无法访问的进程 */ }
                finally
                {
                    proc.Dispose();
                }
            }

            return result.OrderByDescending(p => p.MemoryUsage).Take(50).ToList();
        }

        public long CalculateAffinityMask(IEnumerable<int> threads)
        {
            return _cpuService.CalculateAffinityMask(threads);
        }

        public IEnumerable<int> GetThreadsFromMask(long mask)
        {
            return _cpuService.GetThreadsFromMask(mask);
        }

        public ProfileStatus CheckProfileStatus(ProcessAffinityProfile profile)
        {
            var processBaseName = Path.GetFileNameWithoutExtension(profile.ProcessName);
            
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processBaseName);
            }
            catch
            {
                return ProfileStatus.NotRunning;
            }
            
            if (processes.Length == 0)
                return ProfileStatus.NotRunning;

            var hasAccessibleProcess = false;
            var hasInaccessibleProcess = false;
            try
            {
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.Id <= 4) continue;
                        
                        var currentMask = (long)proc.ProcessorAffinity;
                        hasAccessibleProcess = true;
                        if (currentMask == profile.AffinityMask)
                        {
                            return ProfileStatus.Applied;
                        }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        hasInaccessibleProcess = true;
                    }
                    catch (InvalidOperationException)
                    {
                        // 进程已退出
                    }
                    catch
                    {
                        // 其他异常
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
                // Why: 如果所有进程都因权限无法访问，返回 Pending 而非 Applied
                return hasAccessibleProcess ? ProfileStatus.Pending
                    : hasInaccessibleProcess ? ProfileStatus.Unknown
                    : ProfileStatus.NotRunning;
            }
            catch
            {
                return ProfileStatus.NotRunning;
            }
        }

        private List<int> GetPCoreThreads(CpuInfo cpuInfo)
        {
            if (cpuInfo.HasHybridArchitecture && cpuInfo.Vendor == CpuVendor.Intel)
            {
                return cpuInfo.Threads
                    .Where(t => t.CoreType == CpuCoreType.PerformanceCore)
                    .Select(t => t.Index)
                    .ToList();
            }

            if (cpuInfo.Has3DCache && cpuInfo.Vendor == CpuVendor.AMD)
            {
                return cpuInfo.Threads
                    .Where(t => t.CoreType == CpuCoreType.CacheCore)
                    .Select(t => t.Index)
                    .ToList();
            }

            var halfCount = Math.Max(4, cpuInfo.LogicalProcessors / 2);
            return Enumerable.Range(0, halfCount).ToList();
        }

        private List<int> GetECoreThreads(CpuInfo cpuInfo)
        {
            if (cpuInfo.HasHybridArchitecture && cpuInfo.Vendor == CpuVendor.Intel)
            {
                return cpuInfo.Threads
                    .Where(t => t.CoreType == CpuCoreType.EfficiencyCore)
                    .Select(t => t.Index)
                    .ToList();
            }

            if (cpuInfo.Has3DCache && cpuInfo.Vendor == CpuVendor.AMD)
            {
                return cpuInfo.Threads
                    .Where(t => t.CoreType == CpuCoreType.ComputeCore)
                    .Select(t => t.Index)
                    .ToList();
            }

            var startIndex = cpuInfo.LogicalProcessors / 2;
            return Enumerable.Range(startIndex, cpuInfo.LogicalProcessors - startIndex).ToList();
        }

        private List<int> GetLastECoreThreads(CpuInfo cpuInfo, int count)
        {
            var eCores = GetECoreThreads(cpuInfo);
            if (eCores.Count <= count)
                return eCores;
            return eCores.Skip(eCores.Count - count).ToList();
        }

        private static string GetIconForType(ProcessType type) => type switch
        {
            ProcessType.Game => "🎮",
            ProcessType.Browser => "🌐",
            ProcessType.Communication => "💬",
            ProcessType.Background => "⚙️",
            ProcessType.System => "🔒",
            _ => "📦"
        };

        private static string GuessProcessIcon(string processName)
        {
            var name = processName.ToLowerInvariant();
            
            // 游戏进程识别 - 包含常见游戏和游戏平台
            if (name.Contains("game") || name.Contains("valorant") || name.Contains("csgo") ||
                name.Contains("apex") || name.Contains("fortnite") || name.Contains("pubg") ||
                name.Contains("overwatch") || name.Contains("league") || name.Contains("dota") ||
                name.Contains("steam") || name.Contains("epic") || name.Contains("origin") ||
                name.Contains("battle.net") || name.Contains("uplay") || name.Contains("genshin") ||
                name.Contains("deltaforce") || name.Contains("delta") || name.Contains("三角洲") ||
                name.Contains("client-win") || name.Contains("shipping") ||
                name.Contains("warzone") || name.Contains("callofduty") || name.Contains("cod") ||
                name.Contains("minecraft") || name.Contains("roblox") || name.Contains("gta") ||
                name.Contains("cyberpunk") || name.Contains("eldenring") || name.Contains("darksouls") ||
                name.Contains("diablo") || name.Contains("pathofexile") || name.Contains("poe") ||
                name.Contains("lostark") || name.Contains("newworld") || name.Contains("destiny") ||
                name.Contains("rainbow") || name.Contains("r6") || name.Contains("siege") ||
                name.Contains("tarkov") || name.Contains("escape") || name.Contains("hunt") ||
                name.Contains("dayz") || name.Contains("rust") || name.Contains("ark") ||
                name.Contains("wow") || name.Contains("warcraft") || name.Contains("ffxiv") ||
                name.Contains("naraka") || name.Contains("永劫") || name.Contains("原神") ||
                name.Contains("崩坏") || name.Contains("honkai") || name.Contains("starrail") ||
                name.Contains("wuthering") || name.Contains("鸣潮") || name.Contains("zenless") ||
                name.Contains("绝区零"))
                return "🎮";

            if (name.Contains("chrome") || name.Contains("firefox") || name.Contains("edge") ||
                name.Contains("opera") || name.Contains("brave") || name.Contains("vivaldi"))
                return "🌐";

            if (name.Contains("discord") || name.Contains("telegram") || name.Contains("slack") ||
                name.Contains("teams") || name.Contains("zoom") || name.Contains("skype") ||
                name.Contains("wechat") || name.Contains("qq"))
                return "💬";

            if (name.Contains("code") || name.Contains("visual") || name.Contains("idea") ||
                name.Contains("rider") || name.Contains("webstorm") || name.Contains("pycharm"))
                return "💻";

            if (name.Contains("spotify") || name.Contains("music") || name.Contains("vlc") ||
                name.Contains("media") || name.Contains("player"))
                return "🎵";

            return "📦";
        }
    }
}
