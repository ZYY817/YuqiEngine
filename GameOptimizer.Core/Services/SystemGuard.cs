using System.Diagnostics;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services;

public sealed class SystemGuard
{
    private static readonly HashSet<string> KnownGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // 主流游戏进程
        "csgo", "cs2", "valorant", "valorant-win64-shipping",
        "leagueclient", "league of legends",
        "overwatch", "overwatch 2",
        "fortnite", "fortniteclient-win64-shipping",
        "pubg", "tslgame",
        "apex_legends", "r5apex",
        "dota2", "gta5", "gtav",
        "cod", "modernwarfare", "blackops",
        "battlefield", "bf2042",
        "rocketleague",
        "minecraft", "javaw",
        "steam", "epicgameslauncher", "origin", "battlenet",
        // 国产游戏
        "dnf", "crossfire", "cf",
        "lol", "tgp", "wegame",
        "genshinimpact", "yuanshen", "starrail",
        "naraka", "永劫无间",
        "pubgmobile", "gameloop"
    };

    private static readonly object _lock = new();
    private static bool? _isAdminCached;
    private static DateTime _lastAdminCheck = DateTime.MinValue;
    private static readonly TimeSpan AdminCacheExpiry = TimeSpan.FromMinutes(5);

    public static bool IsAdministrator
    {
        get
        {
            lock (_lock)
            {
                // 缓存过期或未初始化时重新检查
                if (!_isAdminCached.HasValue || DateTime.UtcNow - _lastAdminCheck > AdminCacheExpiry)
                {
                    _isAdminCached = CheckAdministrator();
                    _lastAdminCheck = DateTime.UtcNow;
                }
            }
            return _isAdminCached.Value;
        }
    }

    public static void RefreshPrivilegeStatus()
    {
        lock (_lock)
        {
            _isAdminCached = null;
            _lastAdminCheck = DateTime.MinValue;
        }
    }

    private static bool CheckAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsGameRunning()
    {
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        var name = process.ProcessName;
                        if (KnownGameProcesses.Contains(name))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // 某些进程可能无法访问
                    }
                }
                return false;
            }
            finally
            {
                foreach (var p in processes) { try { p.Dispose(); } catch { } }
            }
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> GetRunningGames()
    {
        var runningGames = new List<string>();
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        var name = process.ProcessName;
                        if (KnownGameProcesses.Contains(name) && !runningGames.Contains(name))
                        {
                            runningGames.Add(name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SystemGuard] Error accessing process: {ex.Message}");
                    }
                }
            }
            finally
            {
                foreach (var p in processes) { try { p.Dispose(); } catch { } }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemGuard] Error getting running games: {ex.Message}");
        }
        return runningGames;
    }

    public static long GetAvailableDiskSpace(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return 0;
            
            var driveInfo = new DriveInfo(root);
            return driveInfo.AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    public static bool HasSufficientDiskSpace(string path, long requiredBytes)
    {
        var available = GetAvailableDiskSpace(path);
        return available >= requiredBytes;
    }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public static OperationResult CheckPrerequisites(string operationId, bool requiresAdmin = false, long? requiredDiskSpace = null)
    {
        var loc = LocalizationService.Instance;
        
        if (requiresAdmin && !IsAdministrator)
        {
            return OperationResult.PermissionDenied(loc.Get("Guard.NeedAdmin"));
        }

        if (IsGameRunning())
        {
            var games = string.Join(", ", GetRunningGames().Take(3));
            return OperationResult.Failed(loc.Get("Guard.GameRunning", games));
        }

        if (requiredDiskSpace.HasValue)
        {
            var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!HasSufficientDiskSpace(systemDrive, requiredDiskSpace.Value))
            {
                return OperationResult.Failed(loc.Get("Guard.DiskSpaceLow", FormatBytes(requiredDiskSpace.Value)));
            }
        }

        return OperationResult.Success();
    }
}
