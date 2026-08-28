// File: GameOptimizer.Core/Diagnostics/DiagnosticPackageService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Core.Diagnostics;

/// <summary>
/// 问题包生成服务 - 仅收集白名单运行日志和最小版本信息用于问题排查。
/// </summary>
public class DiagnosticPackageService
{
    private const string LogBackupDirectorySettingKey = "LogBackupDirectory";
    private const int MaximumLogFileCount = 64;
    private const long MaximumSingleLogBytes = 10 * 1024 * 1024;
    private const long MaximumTotalLogBytes = 40 * 1024 * 1024;
    private static readonly string[] AllowedLogExtensions = [".log", ".txt", ".json", ".old"];
    private static readonly string[] AllowedLogNamePrefixes =
    [
        "optimizer-", "activity", "operation_history", "transaction", "audio_debug",
        "backup_errors", "system_restore", "repair_errors", "diagnostic", "page_diagnostics",
        "startup_diagnostic", "crash"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<IReadOnlyList<string>> _outputDirectoryCandidates;
    private readonly Func<IReadOnlyList<string>> _logDirectoryCandidates;
    private readonly Action<string, string> _createArchive;
    private readonly Func<string> _temporaryDirectoryFactory;
    private readonly Func<CancellationToken, Task> _flushLogs;

    public DiagnosticPackageService()
        : this(CreateProductionOutputDirectoryCandidates, CreateArchive, CreateTemporaryDirectory,
            CreateProductionLogDirectoryCandidates)
    {
    }

    public DiagnosticPackageService(IDiagnosticLogService logService)
        : this(
            CreateProductionOutputDirectoryCandidates,
            CreateArchive,
            CreateTemporaryDirectory,
            () => CreateProductionLogDirectoryCandidates(logService.LogDirectory),
            logService.FlushAsync)
    {
        ArgumentNullException.ThrowIfNull(logService);
    }

    internal DiagnosticPackageService(
        Func<IReadOnlyList<string>> outputDirectoryCandidates,
        Action<string, string>? createArchive = null,
        Func<string>? temporaryDirectoryFactory = null,
        Func<IReadOnlyList<string>>? logDirectoryCandidates = null,
        Func<CancellationToken, Task>? flushLogs = null)
    {
        _outputDirectoryCandidates = outputDirectoryCandidates ?? throw new ArgumentNullException(nameof(outputDirectoryCandidates));
        _logDirectoryCandidates = logDirectoryCandidates ?? CreateProductionLogDirectoryCandidates;
        _createArchive = createArchive ?? CreateArchive;
        _temporaryDirectoryFactory = temporaryDirectoryFactory ?? CreateTemporaryDirectory;
        _flushLogs = flushLogs ?? (_ => Task.CompletedTask);
    }

    public DiagnosticPackagePreview GetPreview()
    {
        var files = EnumerateEligibleLogFiles();
        return new DiagnosticPackagePreview(
            files.Count,
            files.Sum(file => file.Length),
            files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray(),
            "仅包含宇奇引擎运行日志、崩溃日志和最小版本信息；不包含配置、备份、密钥或用户文档。");
    }

    public async Task<(bool Success, string PackagePath, string Error)> CreateDiagnosticPackageAsync(
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var packageName = $"YuqiEngine_ProblemReport_{timestamp}.zip";
        var tempDir = _temporaryDirectoryFactory();

        try
        {
            await _flushLogs(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // 创建临时目录
            if (!SafeDirectoryOperations.TryCreateDirectory(tempDir, out var error))
            {
                return (false, string.Empty, $"无法创建临时目录: {error}");
            }

            // 1. 收集最小运行环境信息
            await CollectSystemInfoAsync(tempDir, cancellationToken).ConfigureAwait(false);

            // 2. 收集日志文件
            var collection = await CollectLogFilesAsync(tempDir, cancellationToken).ConfigureAwait(false);

            // 3. 创建问题包清单。崩溃日志已通过同一白名单、脱敏和容量上限链路收集。
            await CreateManifestAsync(tempDir, collection, cancellationToken).ConfigureAwait(false);

            // 4. 打包为 ZIP。候选目录按“配置目录/应用默认目录 → 报告目录 → 临时目录”
            // 依次尝试；目录写探针成功不代表随后写入一定成功，因此 ZIP 写入失败也必须继续回退。
            var (packagePath, packageError) = TryCreatePackageWithFallback(tempDir, packageName);
            if (string.IsNullOrEmpty(packagePath))
            {
                return (false, string.Empty, packageError);
            }

            return (true, packagePath, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiagnosticPackage] 创建诊断包失败: {ex}");
            return (false, string.Empty, ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiagnosticPackage] 清理临时目录失败: {ex.Message}");
            }
        }
    }

    private static async Task CollectSystemInfoAsync(string targetDir, CancellationToken cancellationToken)
    {
        var info = new Dictionary<string, object>
        {
            ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["AppVersion"] = GetAppVersion(),
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["OSArchitecture"] = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            ["ProcessorCount"] = Environment.ProcessorCount,
            ["IsAdministrator"] = SystemGuard.IsAdministrator,
            ["CLRVersion"] = Environment.Version.ToString(),
            ["WorkingSet"] = $"{Environment.WorkingSet / 1024 / 1024} MB"
        };

        var json = JsonSerializer.Serialize(info, JsonOptions);
        var filePath = Path.Combine(targetDir, "system_info.json");
        cancellationToken.ThrowIfCancellationRequested();
        await SafeFileHelper.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
    }

    private async Task<DiagnosticLogCollection> CollectLogFilesAsync(string targetDir, CancellationToken cancellationToken)
    {
        var logsDir = Path.Combine(targetDir, "logs");
        SafeDirectoryOperations.TryCreateDirectory(logsDir, out _);
        var included = new List<string>();
        var skipped = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var logFile in EnumerateEligibleLogFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fileName = GetUniqueFileName(logFile.Name, usedNames);
                var destPath = Path.Combine(logsDir, fileName);
                var content = await ReadLiveLogTextAsync(logFile.FullName, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(content))
                {
                    await SafeFileHelper.WriteAllTextAsync(destPath, SanitizeSupportContent(content)).ConfigureAwait(false);
                    included.Add(fileName);
                }
            }
            catch (Exception ex)
            {
                skipped.Add($"{logFile.Name}: 无法读取");
                Debug.WriteLine($"[DiagnosticPackage] 收集日志失败 {logFile.FullName}: {ex.Message}");
            }
        }

        return new DiagnosticLogCollection(included, skipped);
    }

    private static async Task CreateManifestAsync(
        string targetDir,
        DiagnosticLogCollection collection,
        CancellationToken cancellationToken)
    {
        var manifest = new
        {
            PackageVersion = "1.0",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            AppVersion = GetAppVersion(),
            Purpose = "此问题包用于排查宇奇引擎运行故障，包含白名单运行日志和最小版本信息。",
            Contents = new
            {
                SystemInfo = "system_info.json - 系统基本信息",
                Logs = "logs/ - 应用日志文件",
                collection.Included,
                collection.Skipped
            },
            Instructions = new[]
            {
                "1. 发送前可先解压并检查内容",
                "2. 问题包不会自动上传或发送",
                "3. 请由用户自行将 ZIP 文件发送给作者"
            }
        };

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var filePath = Path.Combine(targetDir, "README.txt");
        cancellationToken.ThrowIfCancellationRequested();
        await SafeFileHelper.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
    }

    private static string GetAppVersion()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private (string PackagePath, string Error) TryCreatePackageWithFallback(string sourceDirectory, string packageName)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in _outputDirectoryCandidates())
        {
            if (!TryNormalizeLocalDirectory(candidate, out var directory, out var validationError) ||
                !seen.Add(directory))
            {
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    errors.Add(validationError);
                }
                continue;
            }

            if (!TryEnsureWritableDirectory(directory, out var writeError))
            {
                errors.Add($"{directory}: {writeError}");
                continue;
            }

            var packagePath = GetAvailablePackagePath(directory, packageName);
            try
            {
                _createArchive(sourceDirectory, packagePath);
                if (!File.Exists(packagePath))
                {
                    throw new IOException("ZIP 创建后未找到输出文件。");
                }

                return (packagePath, string.Empty);
            }
            catch (Exception ex)
            {
                errors.Add($"{directory}: {ex.Message}");
                Debug.WriteLine($"[DiagnosticPackage] 输出失败，尝试下一安全位置: {ex.Message}");
                SafeFileHelper.DeleteFile(packagePath);
            }
        }

        return (string.Empty, $"无法将诊断包写入任何安全的本地目录。{string.Join(" | ", errors)}");
    }

    private IReadOnlyList<FileInfo> EnumerateEligibleLogFiles()
    {
        var files = new List<FileInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var candidate in _logDirectoryCandidates())
        {
            if (!TryNormalizeLocalDirectory(candidate, out var directory, out _) || !Directory.Exists(directory))
                continue;

            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var path in paths.OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (files.Count >= MaximumLogFileCount)
                    return files;
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (!seen.Add(fullPath) || !IsContainedPath(directory, fullPath))
                        continue;
                    var info = new FileInfo(fullPath);
                    if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                        || !AllowedLogExtensions.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)
                        || !AllowedLogNamePrefixes.Any(prefix => info.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        || info.Length <= 0 || info.Length > MaximumSingleLogBytes
                        || totalBytes + info.Length > MaximumTotalLogBytes)
                        continue;
                    files.Add(info);
                    totalBytes += info.Length;
                }
                catch
                {
                    // A live log can disappear or rotate during enumeration; skip it safely.
                }
            }
        }

        return files;
    }

    private static IReadOnlyList<string> CreateProductionLogDirectoryCandidates() =>
        CreateProductionLogDirectoryCandidates(LoadConfiguredLogBackupDirectory());

    private static IReadOnlyList<string> CreateProductionLogDirectoryCandidates(string? activeLogDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(activeLogDirectory))
            candidates.Add(activeLogDirectory);
        candidates.Add(AppPaths.LogsDir);
        var crashDirectory = Path.GetDirectoryName(AppPaths.CrashLogPath);
        if (!string.IsNullOrWhiteSpace(crashDirectory))
            candidates.Add(crashDirectory);
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<string> ReadLiveLogTextAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string SanitizeSupportContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;
        var sanitized = CommandSanitizer.SanitizePathForLogging(content);
        var machineName = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machineName))
            sanitized = sanitized.Replace(machineName, "[MACHINE]", StringComparison.OrdinalIgnoreCase);
        sanitized = Regex.Replace(sanitized,
            @"(?i)([""']?\bauthorization\b[""']?\s*[:=]\s*)(?:[""']?bearer\s+)?[A-Z0-9._~+/=-]+[""']?",
            "$1[REDACTED]");
        sanitized = Regex.Replace(sanitized,
            @"(?i)([""']?\b(?:access[_-]?token|refresh[_-]?token|api[_-]?key|password|passwd|secret)\b[""']?\s*[:=]\s*)(?:""[^""\r\n]*""|'[^'\r\n]*'|[^\s,;]+)",
            "$1[REDACTED]");
        sanitized = Regex.Replace(sanitized,
            @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
            "[EMAIL_REDACTED]");
        return sanitized;
    }

    private static bool IsContainedPath(string root, string path)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueFileName(string requested, ISet<string> used)
    {
        var safe = Path.GetFileName(requested);
        if (used.Add(safe))
            return safe;
        var stem = Path.GetFileNameWithoutExtension(safe);
        var extension = Path.GetExtension(safe);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{stem}-{index}{extension}";
            if (used.Add(candidate))
                return candidate;
        }
        throw new IOException("无法为重复日志文件生成安全名称。");
    }

    private static string GetAvailablePackagePath(string directory, string packageName)
    {
        var initial = Path.Combine(directory, packageName);
        if (!File.Exists(initial))
            return initial;

        var stem = Path.GetFileNameWithoutExtension(packageName);
        var extension = Path.GetExtension(packageName);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{index}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}{extension}");
    }

    private static IReadOnlyList<string> CreateProductionOutputDirectoryCandidates()
    {
        var configuredDirectory = LoadConfiguredLogBackupDirectory();
        var preferredDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? AppPaths.LogsDir
            : configuredDirectory;

        return
        [
            preferredDirectory,
            AppPaths.ReportsExportDir,
            AppPaths.TempDir
        ];
    }

    private static bool TryNormalizeLocalDirectory(string? path, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (expanded.StartsWith(@"\\", StringComparison.Ordinal) ||
                expanded.StartsWith("//", StringComparison.Ordinal))
            {
                error = $"不支持网络路径: {path}";
                return false;
            }

            normalized = Path.GetFullPath(expanded);
            if (!Path.IsPathRooted(normalized))
            {
                error = $"不是有效的本地绝对路径: {path}";
                normalized = string.Empty;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"路径无效: {ex.Message}";
            return false;
        }
    }

    private static bool TryEnsureWritableDirectory(string directory, out string error)
    {
        error = string.Empty;
        if (!SafeDirectoryOperations.TryCreateDirectory(directory, out var createError))
        {
            error = createError ?? "无法创建目录。";
            return false;
        }

        var probePath = Path.Combine(directory, $".yuqi-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
            {
            }
            File.Delete(probePath);
            return true;
        }
        catch (Exception ex)
        {
            error = $"目录不可写: {ex.Message}";
            SafeFileHelper.DeleteFile(probePath);
            return false;
        }
    }

    private static void CreateArchive(string sourceDirectory, string packagePath) =>
        ZipFile.CreateFromDirectory(sourceDirectory, packagePath, CompressionLevel.Optimal, false);

    private static string CreateTemporaryDirectory() =>
        Path.Combine(AppPaths.TempDir, $"YuqiEngine_Diag_{Guid.NewGuid():N}");

    private static string LoadConfiguredLogBackupDirectory()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFilePath))
            {
                return string.Empty;
            }

            var (json, readError) = SafeFileHelper.ReadAllText(AppPaths.SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(readError) || string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(LogBackupDirectorySettingKey, out var pathProperty))
            {
                return string.Empty;
            }

            if (pathProperty.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return NormalizeDirectoryPath(pathProperty.GetString());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiagnosticPackage] load output directory failed: {ex.Message}");
            return string.Empty;
        }
    }

    private static string NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string GetLogDirectory() => AppPaths.LogsDir;

    public static List<LogFileInfo> GetAvailableLogFiles()
    {
        var logFiles = new List<LogFileInfo>();

        var logPaths = new Dictionary<string, string>
        {
            ["活动日志"] = AppPaths.ActivityLogFilePath,
            ["操作历史"] = AppPaths.OperationHistoryFilePath,
            ["事务日志"] = AppPaths.TransactionLogFilePath,
            ["音频调试"] = AppPaths.AudioDebugLogFilePath,
            ["备份错误"] = Path.Combine(AppPaths.LogsDir, "backup_errors.log"),
            ["还原日志"] = Path.Combine(AppPaths.LogsDir, "system_restore.log"),
            ["修复错误"] = Path.Combine(AppPaths.LogsDir, "repair_errors.log"),
            ["诊断日志"] = Path.Combine(AppPaths.LogsDir, "diagnostic.log")
        };

        foreach (var (name, path) in logPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var fileInfo = new FileInfo(path);
                    logFiles.Add(new LogFileInfo
                    {
                        Name = name,
                        Path = path,
                        Size = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DiagnosticPackage][GetAvailableLogFiles] {name}: {ex.Message}");
                }
            }
        }

        return logFiles;
    }
}

public sealed record DiagnosticPackagePreview(
    int LogFileCount,
    long TotalLogBytes,
    IReadOnlyList<string> LogFileNames,
    string PrivacyNotice)
{
    public string TotalSizeText => TotalLogBytes < 1024 * 1024
        ? $"{TotalLogBytes / 1024d:0.#} KB"
        : $"{TotalLogBytes / 1024d / 1024d:0.#} MB";
}

internal sealed record DiagnosticLogCollection(
    IReadOnlyList<string> Included,
    IReadOnlyList<string> Skipped);

public class LogFileInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }

    public string SizeText => Size < 1024 ? $"{Size} B" :
                              Size < 1024 * 1024 ? $"{Size / 1024.0:F1} KB" :
                              $"{Size / 1024.0 / 1024.0:F1} MB";

    public string LastModifiedText
    {
        get
        {
            var span = DateTime.Now - LastModified;
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
            return LastModified.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
