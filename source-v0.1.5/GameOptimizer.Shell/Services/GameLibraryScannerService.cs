using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services;

public interface IGameLibraryScannerService
{
    Task<List<DetectedGameInfo>> ScanAllLibrariesAsync();
    Task<List<DetectedGameInfo>> ScanSteamGamesAsync();
    Task<List<DetectedGameInfo>> ScanEpicGamesAsync();
    Task<List<DetectedGameInfo>> ScanCustomPathAsync(string path);
}

public class DetectedGameInfo
{
    public required string Name { get; init; }
    public required string ExecutablePath { get; init; }
    public required string InstallPath { get; init; }
    public string? IconPath { get; init; }
    public string Source { get; init; } = "Unknown";
    public long SizeBytes { get; init; }
}

public sealed class GameLibraryScannerService : IGameLibraryScannerService
{
    private readonly ILogger<GameLibraryScannerService> _logger;

    // 常见游戏可执行文件名模式（排除启动器和工具）
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "unins000", "uninstall", "setup", "installer", "launcher", "updater",
        "crashhandler", "crashreporter", "ue4prereqsetup", "dxsetup", "vcredist",
        "dotnetfx", "physx", "easyanticheat", "battleye", "eac_launcher"
    };

    public GameLibraryScannerService(ILogger<GameLibraryScannerService> logger)
    {
        _logger = logger;
    }

    public async Task<List<DetectedGameInfo>> ScanAllLibrariesAsync()
    {
        var results = new List<DetectedGameInfo>();

        var tasks = new[]
        {
            ScanSteamGamesAsync(),
            ScanEpicGamesAsync()
        };

        var allResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var list in allResults)
        {
            results.AddRange(list);
        }

        // 去重
        return results
            .GroupBy(g => g.ExecutablePath.ToLowerInvariant())
            .Select(g => g.First())
            .ToList();
    }

    public async Task<List<DetectedGameInfo>> ScanSteamGamesAsync()
    {
        var results = new List<DetectedGameInfo>();

        try
        {
            // 获取Steam安装路径
            var steamPath = GetSteamInstallPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                _logger.LogInformation("[GameScanner] 未找到Steam安装");
                return results;
            }

            // 获取所有Steam库文件夹
            var libraryFolders = GetSteamLibraryFolders(steamPath);
            _logger.LogInformation("[GameScanner] 找到 {Count} 个Steam库文件夹", libraryFolders.Count);

            foreach (var libraryPath in libraryFolders)
            {
                var steamAppsPath = Path.Combine(libraryPath, "steamapps", "common");
                if (!Directory.Exists(steamAppsPath)) continue;

                foreach (var gameDir in Directory.GetDirectories(steamAppsPath))
                {
                    var gameName = Path.GetFileName(gameDir);
                    var exeFiles = FindGameExecutables(gameDir);

                    foreach (var exePath in exeFiles)
                    {
                        results.Add(new DetectedGameInfo
                        {
                            Name = gameName,
                            ExecutablePath = exePath,
                            InstallPath = gameDir,
                            Source = "Steam",
                            SizeBytes = GetDirectorySize(gameDir)
                        });
                    }
                }
            }

            _logger.LogInformation("[GameScanner] Steam扫描完成，找到 {Count} 个游戏", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GameScanner] Steam扫描失败");
        }

        return await Task.FromResult(results);
    }

    public async Task<List<DetectedGameInfo>> ScanEpicGamesAsync()
    {
        var results = new List<DetectedGameInfo>();

        try
        {
            // Epic Games默认安装路径 - 动态获取所有驱动器
            var epicPathsList = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games")
            };
            // 扫描所有固定驱动器
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                epicPathsList.Add(Path.Combine(drive.RootDirectory.FullName, "Epic Games"));
            }
            var epicPaths = epicPathsList.ToArray();

            // 从注册表获取Epic安装路径
            var epicInstallPath = GetEpicInstallPath();
            if (!string.IsNullOrEmpty(epicInstallPath))
            {
                epicPaths = epicPaths.Prepend(epicInstallPath).ToArray();
            }

            foreach (var epicPath in epicPaths.Where(Directory.Exists))
            {
                foreach (var gameDir in Directory.GetDirectories(epicPath))
                {
                    var gameName = Path.GetFileName(gameDir);
                    if (gameName.Equals("Launcher", StringComparison.OrdinalIgnoreCase)) continue;

                    var exeFiles = FindGameExecutables(gameDir);
                    foreach (var exePath in exeFiles)
                    {
                        results.Add(new DetectedGameInfo
                        {
                            Name = gameName,
                            ExecutablePath = exePath,
                            InstallPath = gameDir,
                            Source = "Epic Games",
                            SizeBytes = GetDirectorySize(gameDir)
                        });
                    }
                }
            }

            _logger.LogInformation("[GameScanner] Epic Games扫描完成，找到 {Count} 个游戏", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GameScanner] Epic Games扫描失败");
        }

        return await Task.FromResult(results);
    }

    public async Task<List<DetectedGameInfo>> ScanCustomPathAsync(string path)
    {
        var results = new List<DetectedGameInfo>();

        if (!Directory.Exists(path)) return results;

        try
        {
            foreach (var gameDir in Directory.GetDirectories(path))
            {
                var gameName = Path.GetFileName(gameDir);
                var exeFiles = FindGameExecutables(gameDir);

                foreach (var exePath in exeFiles)
                {
                    results.Add(new DetectedGameInfo
                    {
                        Name = gameName,
                        ExecutablePath = exePath,
                        InstallPath = gameDir,
                        Source = LocalizationService.Instance.Get("GameScanner.CustomPath"),
                        SizeBytes = GetDirectorySize(gameDir)
                    });
                }
            }

            _logger.LogInformation("[GameScanner] 自定义路径扫描完成，找到 {Count} 个游戏", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GameScanner] 自定义路径扫描失败");
        }

        return await Task.FromResult(results);
    }

    private static string? GetSteamInstallPath()
    {
        try
        {
            return WindowsRegistryInfoReader.Instance.Read(
                "HKCU",
                @"Software\Valve\Steam",
                "SteamPath").Value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetSteamLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        try
        {
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) return folders;

            var content = File.ReadAllText(vdfPath);
            // 简单解析VDF格式，提取路径
            var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
            foreach (Match match in matches)
            {
                var path = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameLibraryScanner] 解析Steam库文件夹失败: {ex.Message}");
        }

        return folders;
    }

    private static string? GetEpicInstallPath()
    {
        try
        {
            return WindowsRegistryInfoReader.Instance.Read(
                "HKCU",
                @"Software\Epic Games\EOS",
                "ModSdkMetadataDir").Value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static List<string> FindGameExecutables(string gameDir)
    {
        var results = new List<string>();

        try
        {
            var exeFiles = Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories);

            foreach (var exePath in exeFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(exePath);

                // 排除工具和安装程序
                if (ExcludedNames.Any(n => fileName.Contains(n, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // 排除子目录中的工具（如 _CommonRedist, Engine, Binaries/Win64 下的非主程序）
                var relativePath = exePath.Substring(gameDir.Length).TrimStart(Path.DirectorySeparatorChar);
                if (relativePath.Contains("_CommonRedist", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.Contains("Redist", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.Contains("Prerequisites", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 优先选择根目录或一级子目录的exe
                var depth = relativePath.Count(c => c == Path.DirectorySeparatorChar);
                if (depth <= 2)
                {
                    results.Add(exePath);
                }
            }

            // 如果找到多个，优先返回与目录名相似的
            if (results.Count > 1)
            {
                var dirName = Path.GetFileName(gameDir);
                var bestMatch = results.FirstOrDefault(e =>
                    Path.GetFileNameWithoutExtension(e).Contains(dirName, StringComparison.OrdinalIgnoreCase));
                if (bestMatch != null)
                {
                    return new List<string> { bestMatch };
                }
            }

            // 最多返回一个最可能的主程序
            return results.Take(1).ToList();
        }
        catch
        {
            return results;
        }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            long totalSize = 0;
            var dirInfo = new DirectoryInfo(path);
            
            // 限制扫描深度和文件数量，避免卡顿
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Take(10000))
            {
                try { totalSize += file.Length; }
                catch { /* 忽略无法访问的文件 */ }
            }
            return totalSize;
        }
        catch
        {
            return 0;
        }
    }
}
