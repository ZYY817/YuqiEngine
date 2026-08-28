using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services.Memory;

public enum ProcessCategory
{
    SafeToClean,      // 可安全清理
    CautionClean,     // 谨慎清理
    RecommendKeep,    // 建议保留
    SystemCritical,   // 系统关键
    Whitelisted       // 白名单保护
}

public record ProcessGroup(
    string Name,
    string DisplayName,
    string Description,
    ProcessCategory Category,
    int InstanceCount,
    long TotalMemoryBytes,
    List<int> Pids,
    bool IsResponding
);

[SupportedOSPlatform("windows")]
public interface IProcessCleanerService
{
    List<ProcessGroup> GetProcessGroups();
    long CleanProcessGroup(ProcessGroup group);
    string LastMutationBlockReason { get; }
    void AddToWhitelist(string processName);
    void RemoveFromWhitelist(string processName);
    HashSet<string> GetWhitelist();
    void SaveWhitelist();
    void LoadWhitelist();
}

[SupportedOSPlatform("windows")]
public sealed class ProcessCleanerService : IProcessCleanerService
{
    public const string MutationBlockedReason =
        "Working Set 清理没有可证明的精确逆操作；当前仅允许进程只读枚举，未执行进程写入。";

    private readonly HashSet<string> _whitelist = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _whitelistPath;
    
    // 系统关键进程 - 绝对不能清理
    
    // 建议保留的常用软件
    private static readonly HashSet<string> RecommendKeepProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WeChat", "QQ", "TIM", "DingTalk", "WXWork", "Telegram", "Discord", "Slack",
        "Code", "devenv", "idea64", "pycharm64", "webstorm64", "rider64",
        "chrome", "firefox", "msedge", "opera", "brave",
        "Spotify", "CloudMusic", "QQMusic", "KuGou",
        "Steam", "EpicGamesLauncher", "Origin", "Battle.net",
        "GameOptimizer.Shell"
    };
    
    // 可安全清理的后台进程
    private static readonly HashSet<string> SafeToCleanProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "OneDrive", "GoogleDriveFS", "Dropbox", "iCloudServices",
        "AdobeUpdateService", "AdobeARM", "AGSService", "AGMService",
        "jusched", "jucheck", "UpdateService",
        "CCleanerBrowser", "CCleaner64", "CCleaner",
        "NVDisplay.Container", "NVIDIA Web Helper",
        "RtkAudUService64", "RtkUService64",
        "PnkBstrA", "PnkBstrB", "EasyAntiCheat",
        "GameBarPresenceWriter", "gamingservices", "gamingservicesnet"
    };

    public ProcessCleanerService()
    {
        var configDir = AppPaths.ConfigDir;
        Directory.CreateDirectory(configDir);
        _whitelistPath = Path.Combine(configDir, "process_whitelist.json");
        LoadWhitelist();
    }

    public string LastMutationBlockReason => MutationBlockedReason;

    public List<ProcessGroup> GetProcessGroups()
    {
        var groups = new Dictionary<string, List<(int Pid, long Memory, bool Responding, string Desc)>>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var name = proc.ProcessName;
                var memory = proc.WorkingSet64;
                var responding = true;
                var desc = "";
                
                try { responding = proc.Responding; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProcessCleanerService] Error getting Responding for {name}: {ex.Message}");
                }
                try { desc = proc.MainModule?.FileVersionInfo?.FileDescription ?? ""; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProcessCleanerService] Error getting FileDescription for {name}: {ex.Message}");
                }
                
                if (!groups.ContainsKey(name))
                    groups[name] = new List<(int, long, bool, string)>();
                
                groups[name].Add((proc.Id, memory, responding, desc));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessCleanerService] Error processing process: {ex.Message}");
            }
            finally { proc.Dispose(); }
        }
        
        var result = new List<ProcessGroup>();
        foreach (var (name, instances) in groups)
        {
            if (instances.Count == 0) continue;
            
            var totalMemory = instances.Sum(x => x.Memory);
            if (totalMemory < 5 * 1024 * 1024) continue; // 忽略 < 5MB
            
            var category = CategorizeProcess(name);
            var displayName = instances.Count > 1 ? $"{name} × {instances.Count}" : name;
            var desc = instances.FirstOrDefault(x => !string.IsNullOrEmpty(x.Desc)).Desc ?? "";
            var allResponding = instances.All(x => x.Responding);
            
            result.Add(new ProcessGroup(
                name, displayName, desc, category,
                instances.Count, totalMemory,
                instances.Select(x => x.Pid).ToList(),
                allResponding
            ));
        }
        
        return result.OrderByDescending(g => g.TotalMemoryBytes).ToList();
    }

    public ProcessCategory CategorizeProcess(string processName)
    {
        using var current = Process.GetCurrentProcess();
        if (string.Equals(processName, current.ProcessName, StringComparison.OrdinalIgnoreCase))
            return ProcessCategory.SystemCritical;
        if (_whitelist.Contains(processName))
            return ProcessCategory.Whitelisted;
        if (ProcessTerminationSafety.SystemCriticalProcessNames.Contains(processName))
            return ProcessCategory.SystemCritical;
        if (RecommendKeepProcesses.Contains(processName))
            return ProcessCategory.RecommendKeep;
        if (SafeToCleanProcesses.Contains(processName))
            return ProcessCategory.SafeToClean;
        return ProcessCategory.CautionClean;
    }

    public void AddToWhitelist(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        _whitelist.Add(processName);
        SaveWhitelist();
    }

    public void RemoveFromWhitelist(string processName)
    {
        _whitelist.Remove(processName);
        SaveWhitelist();
    }

    public HashSet<string> GetWhitelist() => new(_whitelist, StringComparer.OrdinalIgnoreCase);

    public void SaveWhitelist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_whitelist.ToList());
            // P3-002: 使用 SafeFileWriter 替代裸露的 File.WriteAllText
            var success = SafeFileWriter.TryWriteAllText(_whitelistPath, json);
            if (!success)
            {
                Debug.WriteLine($"[ProcessCleanerService] SaveWhitelist failed");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessCleanerService] SaveWhitelist error: {ex.Message}");
        }
    }

    public void LoadWhitelist()
    {
        try
        {
            if (!File.Exists(_whitelistPath)) return;
            var json = File.ReadAllText(_whitelistPath);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list != null)
            {
                _whitelist.Clear();
                foreach (var name in list)
                    _whitelist.Add(name);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessCleanerService] LoadWhitelist error: {ex.Message}");
        }
    }

    public long CleanProcessGroup(ProcessGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Debug.WriteLine($"[ProcessCleanerService] {MutationBlockedReason}");
        return 0;
    }
}
