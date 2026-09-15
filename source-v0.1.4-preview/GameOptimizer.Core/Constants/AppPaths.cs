// File: GameOptimizer.Core/Constants/AppPaths.cs
using System;
using System.IO;
using System.Collections.Generic;

namespace GameOptimizer.Core.Constants;

public static class AppPaths
{
    public const string AppDataFolderName = "YuqiEngine";
    private const string AppDataRootOverrideVariable = "YUQIENGINE_DATA_ROOT";
    private static readonly Lazy<AppDataRootResolution> RootResolution = new(ResolveAppDataRoot);
    private static string? _configuredLogsDirectory;

    public static string PreferredAppDataRoot => Path.Combine("F:\\", AppDataFolderName);

    public static string LegacyAppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDataFolderName);
    
    public static string AppDataRoot => RootResolution.Value.Path;

    public static bool IsFallbackDataRoot => RootResolution.Value.IsFallback;

    public static string? DataRootFallbackReason => RootResolution.Value.FallbackReason;

    // 核心目录
    public static string SnapshotsDir => Path.Combine(AppDataRoot, "Snapshots");
    public static string DefaultLogsDir => Path.Combine(AppDataRoot, "Logs");
    public static string LogsDir => Volatile.Read(ref _configuredLogsDirectory) ?? DefaultLogsDir;
    public static string ConfigDir => Path.Combine(AppDataRoot, "Config");
    public static string BackupsDir => Path.Combine(AppDataRoot, "Backups");
    /// <summary>完整用户配置备份包目录；与首次运行基线和网络诊断备份隔离。</summary>
    public static string ComprehensiveBackupsDir => Path.Combine(BackupsDir, "Comprehensive");
    
    // 特定功能目录
    public static string NetworkBackupsDir => Path.Combine(BackupsDir, "Network");
    public static string FirstRunBackupDir => Path.Combine(BackupsDir, "FirstRun");
    public static string CoreAcceleratorDir => Path.Combine(AppDataRoot, "CoreAccelerator");
    public static string AffinityProfilesDir => Path.Combine(AppDataRoot, "AffinityProfiles");
    public static string OptimizationProfilesDir => Path.Combine(AppDataRoot, "profiles");
    public static string CleanupBackupsDir => Path.Combine(AppDataRoot, "cleanup");
    
    // 配置文件路径
    public static string SettingsFilePath => Path.Combine(ConfigDir, "settings.json");
    public static string GameProfilesFilePath => Path.Combine(ConfigDir, "game_profiles.json");
    public static string GamePriorityProfilesFilePath => Path.Combine(ConfigDir, "game_priority_profiles.json");
    public static string PeripheralSettingsFilePath => Path.Combine(ConfigDir, "peripheral_settings.json");
    public static string GuideProgressFilePath => Path.Combine(ConfigDir, "guide_progress.json");
    public static string ScanCacheFilePath => Path.Combine(ConfigDir, "scan_cache.json");
    public static string CoreAcceleratorSettingsFilePath => Path.Combine(CoreAcceleratorDir, "core_accelerator_settings.json");
    public static string GameCoreProfilesFilePath => Path.Combine(CoreAcceleratorDir, "game_core_profiles.json");
    public static string AffinityProfilesFilePath => Path.Combine(AffinityProfilesDir, "profiles.json");
    
    // 日志文件路径
    public static string ActivityLogFilePath => Path.Combine(LogsDir, "activity.json");
    public static string ActivityHistoryPath => Path.Combine(AppDataRoot, "activity_history.json");
    public static string OperationHistoryFilePath => Path.Combine(LogsDir, "operation_history.json");
    public static string TransactionLogFilePath => Path.Combine(LogsDir, "transaction.log");
    public static string AudioDebugLogFilePath => Path.Combine(LogsDir, "audio_debug.log");
    public static string CrashLogPath => Path.Combine(AppDataRoot, "crash.log");
    // 配置导出目录
    public static string ConfigsDir => Path.Combine(AppDataRoot, "configs");
    public static string TempDir => Path.Combine(AppDataRoot, "Temp");
    public static string ExportsDir => Path.Combine(AppDataRoot, "Exports");
    
    // 诊断和调试
    public static string PageDiagnosticLogPath => Path.Combine(LogsDir, "page_diagnostics.log");
    
    // 导出目录
    public static string ReportsExportDir => Path.Combine(ExportsDir, "Reports");
    
    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(SnapshotsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(ComprehensiveBackupsDir);
        Directory.CreateDirectory(NetworkBackupsDir);
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(ExportsDir);
    }

    /// <summary>
    /// Configures the process-wide log root before logging services are created. Invalid or
    /// unavailable paths fail closed and leave the default application-data directory active.
    /// </summary>
    public static bool TryConfigureLogsDirectory(string? configuredPath, out string? error)
    {
        Interlocked.Exchange(ref _configuredLogsDirectory, null);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            error = null;
            return true;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            if (!Path.IsPathFullyQualified(expanded) ||
                expanded.StartsWith(@"\\", StringComparison.Ordinal) ||
                expanded.StartsWith("//", StringComparison.Ordinal))
            {
                error = "The configured log directory must be a fully-qualified local path.";
                return false;
            }

            var candidate = Path.GetFullPath(expanded);
            var root = Path.GetPathRoot(candidate);
            if (!string.IsNullOrWhiteSpace(root) &&
                string.Equals(
                    candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "A drive root cannot be used as the log directory.";
                return false;
            }

            if (!TryProbeWritableDirectory(candidate, out error))
                return false;

            Interlocked.Exchange(ref _configuredLogsDirectory, candidate);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Returns the primary F-drive path when it exists, otherwise the matching historical
    /// LocalAppData path. This method is read-only and never copies or moves historical data.
    /// New writes must continue to use the original primary path.
    /// </summary>
    public static string ResolveReadPath(string primaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryPath);

        var canonicalRoot = Path.GetFullPath(AppDataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalPrimary = Path.GetFullPath(primaryPath);
        if (!canonicalPrimary.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)
            && !canonicalPrimary.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Read compatibility is limited to the application data root.", nameof(primaryPath));
        }

        if (File.Exists(canonicalPrimary) || Directory.Exists(canonicalPrimary) || IsFallbackDataRoot)
            return canonicalPrimary;

        var relative = Path.GetRelativePath(canonicalRoot, canonicalPrimary);
        var legacyPath = Path.GetFullPath(Path.Combine(LegacyAppDataRoot, relative));
        return File.Exists(legacyPath) || Directory.Exists(legacyPath)
            ? legacyPath
            : canonicalPrimary;
    }

    private static AppDataRootResolution ResolveAppDataRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable(AppDataRootOverrideVariable);
        var preferred = !string.IsNullOrWhiteSpace(overridePath) && Path.IsPathFullyQualified(overridePath)
            ? Path.GetFullPath(overridePath)
            : PreferredAppDataRoot;

        if (TryProbeWritableDirectory(preferred, out var preferredError))
            return new AppDataRootResolution(preferred, false, null);

        var fallback = LegacyAppDataRoot;
        if (TryProbeWritableDirectory(fallback, out var fallbackError))
        {
            return new AppDataRootResolution(
                fallback,
                true,
                $"Preferred data root unavailable: {preferredError}");
        }

        throw new IOException(
            $"Neither the preferred nor fallback application data root is writable. " +
            $"Preferred: {preferredError}; fallback: {fallbackError}");
    }

    private static bool TryProbeWritableDirectory(string path, out string? error)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".root_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed record AppDataRootResolution(string Path, bool IsFallback, string? FallbackReason);
    
    public static (bool Success, List<string> Errors) ValidateDirectories()
    {
        var errors = new List<string>();
        
        try
        {
            if (!Directory.Exists(AppDataRoot))
            {
                Directory.CreateDirectory(AppDataRoot);
            }
            
            var testFile = Path.Combine(AppDataRoot, $".write_test_{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add($"No write permission: {AppDataRoot}");
            }
            catch (IOException ex)
            {
                errors.Add($"IO error in {AppDataRoot}: {ex.Message}");
            }
            
            var requiredDirs = new[] { SnapshotsDir, LogsDir, ConfigDir, BackupsDir };
            foreach (var dir in requiredDirs)
            {
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Cannot create {dir}: {ex.Message}");
                }
            }
            
            var drive = new DriveInfo(Path.GetPathRoot(AppDataRoot) ?? "C:\\");
            if (!drive.IsReady)
            {
                errors.Add($"Drive not ready: {drive.Name}");
            }
            else
            {
                var freeSpaceMB = drive.AvailableFreeSpace / 1024 / 1024;
                const int MinRequiredMB = 500;
                if (freeSpaceMB < MinRequiredMB)
                {
                    errors.Add($"Insufficient disk space: {freeSpaceMB} MB available, recommend at least {MinRequiredMB} MB");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: {ex.Message}");
        }
        
        return (errors.Count == 0, errors);
    }
    
    /// <summary>
    /// 迁移旧版本数据（从 GameOptimizer 和 ZYCore 文件夹迁移到 YuqiEngine）
    /// </summary>
    public static void MigrateLegacyData()
    {
        var legacyFolders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameOptimizer"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameOptimizer"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZYCore")
        };
        
        foreach (var legacyRoot in legacyFolders)
        {
            if (!Directory.Exists(legacyRoot))
            {
                continue; // 没有旧数据，跳过
            }
        
        try
        {
            EnsureDirectoriesExist();
            
            // 迁移配置文件
            MigrateFileIfExists(Path.Combine(legacyRoot, "settings.json"), SettingsFilePath);
            MigrateFileIfExists(Path.Combine(legacyRoot, "profiles.json"), GameProfilesFilePath);
            MigrateFileIfExists(Path.Combine(legacyRoot, "game_priority_profiles.json"), GamePriorityProfilesFilePath);
            
            // P2-006: 迁移核心加速器配置
            MigrateFileIfExists(Path.Combine(legacyRoot, "core_accelerator_settings.json"), CoreAcceleratorSettingsFilePath);
            MigrateFileIfExists(Path.Combine(legacyRoot, "game_core_profiles.json"), GameCoreProfilesFilePath);
            
            // P2-006: 迁移亲和性配置
            MigrateFileIfExists(Path.Combine(legacyRoot, "AffinityProfiles", "profiles.json"), AffinityProfilesFilePath);
            
            // 迁移日志
            MigrateFileIfExists(Path.Combine(legacyRoot, "logs", "activity.json"), ActivityLogFilePath);
            MigrateFileIfExists(Path.Combine(legacyRoot, "operation_history.json"), OperationHistoryFilePath);
            MigrateFileIfExists(Path.Combine(legacyRoot, "transaction.log"), TransactionLogFilePath);
            
            // 迁移备份
            var legacyBackups = Path.Combine(legacyRoot, "NetworkBackups");
            if (Directory.Exists(legacyBackups))
            {
                MigrateDirectoryIfExists(legacyBackups, NetworkBackupsDir);
            }
            
            var legacyFirstRunBackup = Path.Combine(legacyRoot, "FirstRunBackup");
            if (Directory.Exists(legacyFirstRunBackup))
            {
                MigrateDirectoryIfExists(legacyFirstRunBackup, FirstRunBackupDir);
            }
            
            // P2-006: 迁移优化配置文件目录
            var legacyProfiles = Path.Combine(legacyRoot, "profiles");
            if (Directory.Exists(legacyProfiles))
            {
                MigrateDirectoryIfExists(legacyProfiles, OptimizationProfilesDir);
            }
            
            // P2-006: 迁移清理备份目录
            var legacyCleanup = Path.Combine(legacyRoot, "cleanup");
            if (Directory.Exists(legacyCleanup))
            {
                MigrateDirectoryIfExists(legacyCleanup, CleanupBackupsDir);
            }
            
            System.Diagnostics.Debug.WriteLine($"[AppPaths] Legacy data migration completed from {legacyRoot}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppPaths] Migration failed from {legacyRoot}: {ex.Message}");
        }
        }
        
        // P2-006: 迁移注册表键
        MigrateRegistryKey();
    }
    
    /// <summary>
    /// 迁移注册表键（从 GameOptimizer 到 YuqiEngine）
    /// </summary>
    private static void MigrateRegistryKey()
    {
        try
        {
            using var legacyKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\GameOptimizer\PowerPlanBackup");
            if (legacyKey == null) return; // 没有旧注册表键
            
            var originalPlanGuid = legacyKey.GetValue("OriginalPlanGuid") as string;
            var backupTime = legacyKey.GetValue("BackupTime") as string;
            
            if (string.IsNullOrEmpty(originalPlanGuid)) return;
            
            // 创建新注册表键
            using var newKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\YuqiEngine\PowerPlanBackup");
            if (newKey == null) return;
            
            // 只有新键不存在时才迁移（防止覆盖）
            if (newKey.GetValue("OriginalPlanGuid") == null)
            {
                newKey.SetValue("OriginalPlanGuid", originalPlanGuid);
                if (!string.IsNullOrEmpty(backupTime))
                {
                    newKey.SetValue("BackupTime", backupTime);
                }
                System.Diagnostics.Debug.WriteLine($"[AppPaths] Migrated registry key: PowerPlanBackup");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppPaths] Registry migration failed: {ex.Message}");
        }
    }
    
    private static void MigrateFileIfExists(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(destPath))
        {
            return;
        }
        
        try
        {
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
            File.Copy(sourcePath, destPath, overwrite: false);
            System.Diagnostics.Debug.WriteLine($"[AppPaths] Migrated: {sourcePath} -> {destPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppPaths] Failed to migrate {sourcePath}: {ex.Message}");
        }
    }
    
    private static void MigrateDirectoryIfExists(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir) || Directory.Exists(destDir))
        {
            return;
        }
        
        try
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, file);
                var destFile = Path.Combine(destDir, relativePath);
                var destFileDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destFileDir))
                {
                    Directory.CreateDirectory(destFileDir);
                }
                File.Copy(file, destFile, overwrite: false);
            }
            System.Diagnostics.Debug.WriteLine($"[AppPaths] Migrated directory: {sourceDir} -> {destDir}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppPaths] Failed to migrate directory {sourceDir}: {ex.Message}");
        }
    }
}
