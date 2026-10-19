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

    /// <summary>
    /// 是否具备管理员权限。唯一权威判定收敛到 <see cref="WindowsElevation"/>：
    /// 它读取令牌 TOKEN_ELEVATION 与 TOKEN_GROUPS，而不是仅依赖 WindowsPrincipal.IsInRole
    /// （后者在 UAC 筛选令牌下会误报"不是管理员"）。
    /// </summary>
    public static bool IsAdministrator => WindowsElevation.IsElevated;

    /// <summary>当前账户是否属于管理员组（即使令牌被 UAC 筛选）。</summary>
    public static bool IsAdministratorAccount => WindowsElevation.IsAdministratorAccount;

    /// <summary>进程真实提权级别。</summary>
    public static ElevationLevel ElevationLevel => WindowsElevation.Level;

    /// <summary>提权判定的人类可读原因，用于诊断与错误报告。</summary>
    public static string ElevationDetail => WindowsElevation.Current.Detail;

    public static void RefreshPrivilegeStatus() => WindowsElevation.Refresh();

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
            var elevation = ElevationGate.Evaluate(operationId, operationRequiresAdministrator: true);
            return OperationResult.PermissionDenied(elevation.UserMessage);
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
