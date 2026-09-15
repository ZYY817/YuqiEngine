using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Utilities;
using Microsoft.Extensions.Logging;
using ErrorMapper = GameOptimizer.Core.Diagnostics.UserFriendlyErrorMapper;

namespace GameOptimizer.Shell.Services;

public record ExportedConfiguration
{
    public string Version { get; init; } = "1.0";
    public string Scope { get; init; } = "optimization-profile";
    public DateTime ExportedAt { get; init; } = DateTime.Now;
    public string MachineName { get; init; } = Environment.MachineName;
    public string WindowsVersion { get; init; } = WindowsVersionService.GetFriendlyName();
    // Legacy 1.0 metadata is accepted on import for compatibility, but is not exported or applied.
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string> AppliedOptimizations { get; init; } = new();
    public Dictionary<string, string>? PowerPlanSettings { get; init; }
}

public sealed class ConfigurationExportService : IConfigurationExportService
{
    private const string OptimizationProfileScope = "optimization-profile";
    private const int MaxImportedOperationCount = 2048;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _configDirectory;
    private readonly IOptimizationEngine? _optimizationEngine;
    private readonly ILogger<ConfigurationExportService> _logger;
    private readonly Lazy<(bool Success, string? Error)> _directoryInitialized;

    public ConfigurationExportService(
        ILogger<ConfigurationExportService> logger,
        IOptimizationEngine? optimizationEngine = null)
    {
        _logger = logger;
        _optimizationEngine = optimizationEngine;
        _configDirectory = AppPaths.ConfigsDir;
        
        // P2-001: 使用 Lazy<T> 确保线程安全的单次初始化
        _directoryInitialized = new Lazy<(bool Success, string? Error)>(() =>
        {
            if (!SafeDirectoryOperations.TryCreateDirectory(_configDirectory, out var error))
            {
                var friendlyError = ErrorMapper.GetFriendlyMessage(
                    new IOException(error ?? "Unknown error"), $"创建配置目录: {_configDirectory}");
                _logger.LogError("[ConfigExport] Failed to create config directory: {Error}", friendlyError);
                return (false, friendlyError);
            }
            return (true, null);
        });
    }
    
    /// <summary>
    /// 确保配置目录存在（线程安全的延迟初始化）
    /// </summary>
    private (bool Success, string? Error) EnsureConfigDirectory()
    {
        return _directoryInitialized.Value;
    }

    public async Task<OperationResult<string>> ExportAsync(
        string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        var loc = LocalizationService.Instance;
        try
        {
            // 【P0-003 修复】：首次使用时确保目录存在
            var (dirSuccess, dirError) = EnsureConfigDirectory();
            if (!dirSuccess)
            {
                return OperationResult<string>.Fail(dirError!);
            }
            
            filePath ??= Path.Combine(_configDirectory, $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            
            // 检查磁盘空间（至少需要1MB）
            var targetDir = Path.GetDirectoryName(filePath) ?? _configDirectory;
            if (!SystemGuard.HasSufficientDiskSpace(targetDir, 1024 * 1024))
            {
                return OperationResult<string>.Fail(loc.Get("Config.DiskSpaceLow"));
            }
            
            var config = await CollectConfigurationAsync(cancellationToken).ConfigureAwait(false);
            
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await SafeFileWriter.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
            
            return OperationResult<string>.Success(filePath, loc.Get("Config.ExportedTo", filePath));
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Fail(loc.Get("Config.ExportFailed"), ex);
        }
    }

    public async Task<OperationResult<ExportedConfiguration>> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var loc = LocalizationService.Instance;
        try
        {
            var (content, error) = await SafeFileHelper.ReadAllTextAsync(filePath).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(error))
            {
                return OperationResult<ExportedConfiguration>.Fail(loc.Get("Config.FileNotFound") + ": " + error);
            }
            
            if (string.IsNullOrEmpty(content))
            {
                return OperationResult<ExportedConfiguration>.Fail(loc.Get("Config.InvalidFormat"));
            }

            var config = JsonSerializer.Deserialize<ExportedConfiguration>(content, JsonOptions);
            
            if (config == null)
            {
                return OperationResult<ExportedConfiguration>.Fail(loc.Get("Config.InvalidFormat"));
            }

            if (!string.Equals(config.Scope, OptimizationProfileScope, StringComparison.Ordinal))
            {
                return OperationResult<ExportedConfiguration>.Fail(
                    LocalizationService.Instance.CurrentLanguage == "en-US"
                        ? "This file is not a YuqiEngine optimization profile."
                        : "该文件不是宇奇引擎优化方案，已拒绝导入。");
            }

            if (config.AppliedOptimizations is null ||
                config.AppliedOptimizations.Count > MaxImportedOperationCount ||
                config.AppliedOptimizations.Any(static id => string.IsNullOrWhiteSpace(id) || id.Length > 256))
            {
                return OperationResult<ExportedConfiguration>.Fail(loc.Get("Config.InvalidFormat"));
            }

            // 验证版本兼容性
            if (!IsVersionCompatible(config.Version))
            {
                return OperationResult<ExportedConfiguration>.Fail(loc.Get("Config.VersionIncompatible", config.Version));
            }

            return OperationResult<ExportedConfiguration>.Success(config, loc.Get("Config.Loaded"));
        }
        catch (JsonException ex)
        {
            return OperationResult<ExportedConfiguration>.Fail(loc.Get("Config.FormatError"), ex);
        }
        catch (Exception ex)
        {
            return OperationResult<ExportedConfiguration>.Fail(loc.Get("Config.ImportFailed"), ex);
        }
    }

    public async Task<OperationResult> ApplyConfigurationAsync(
        ExportedConfiguration config,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        var loc = LocalizationService.Instance;
        if (_optimizationEngine == null)
        {
            return OperationResult.Failed(loc.Get("Config.EngineUnavailable"));
        }
        if (!string.Equals(config.Scope, OptimizationProfileScope, StringComparison.Ordinal))
        {
            return OperationResult.Failed(
                LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? "The imported file has an unsupported scope. No optimization was applied."
                    : "导入文件的作用域不受支持，未应用任何优化。" );
        }

        var requestedOperationIds = config.AppliedOptimizations?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (requestedOperationIds.Length == 0)
        {
            return OperationResult.Failed(
                LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? "The imported optimization profile contains no applicable operations."
                    : "导入的优化方案不包含可应用的优化项。");
        }

        if (requestedOperationIds.Length > MaxImportedOperationCount ||
            requestedOperationIds.Any(static id => id.Length > 256))
        {
            return OperationResult.Failed(loc.Get("Config.InvalidFormat"));
        }

        var operationGroups = _optimizationEngine.Modules
            .SelectMany(module => module.Operations.Select(operation => new
            {
                OperationId = operation.Id,
                ModuleKey = module.Key
            }))
            .GroupBy(static binding => binding.OperationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var ambiguousOperationIds = requestedOperationIds
            .Where(id => operationGroups.TryGetValue(id, out var matches) && matches.Length != 1)
            .ToArray();
        var unknownOperationIds = requestedOperationIds
            .Where(id => !operationGroups.ContainsKey(id))
            .ToArray();

        if (ambiguousOperationIds.Length > 0 || unknownOperationIds.Length > 0)
        {
            var rejected = ambiguousOperationIds.Concat(unknownOperationIds).Distinct(StringComparer.OrdinalIgnoreCase);
            return OperationResult.Failed(
                LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? "The profile contains unavailable or ambiguous operations. Nothing was applied."
                    : "方案包含当前版本不可用或归属不唯一的操作，已在写入前整体阻断。",
                detail: string.Join(", ", rejected.Take(10)));
        }

        var totalSteps = requestedOperationIds.Length;
        var successCount = 0;
        var failedOps = new List<string>();

        progress?.Report(ProgressInfo.Starting("apply_config", loc.Get("Config.ApplyConfig"), totalSteps));

        // 使用当前引擎目录中的真实模块归属；禁止从操作 ID 前缀猜测模块。
        var opsByModule = requestedOperationIds
            .Select(id => new { Id = id, ModuleKey = operationGroups[id][0].ModuleKey })
            .GroupBy(static binding => binding.ModuleKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(binding => binding.Id).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var currentStep = 0;
        foreach (var (moduleKey, operationIds) in opsByModule)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var result = await _optimizationEngine.ApplyAsync(moduleKey, operationIds, cancellationToken).ConfigureAwait(false);
                var returnedOperationIds = result.Operations
                    .Select(static summary => summary.OperationId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                
                foreach (var summary in result.Operations)
                {
                    currentStep++;
                    progress?.Report(ProgressInfo.Running("apply_config", loc.Get("Config.ApplyConfig"), currentStep, totalSteps, summary.Title));
                    
                    if (summary.Success)
                        successCount++;
                    else
                        failedOps.Add(summary.OperationId);
                }

                var missingResults = operationIds.Where(id => !returnedOperationIds.Contains(id)).ToArray();
                failedOps.AddRange(missingResults);
                currentStep += missingResults.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ConfigExport] Failed to apply module {Module}", moduleKey);
                failedOps.AddRange(operationIds);
                currentStep += operationIds.Count;
            }
        }

        progress?.Report(ProgressInfo.Completed("apply_config", loc.Get("Config.ApplyConfig")));

        if (failedOps.Count == 0)
        {
            return OperationResult.Success(loc.Get("Config.Applied", successCount));
        }
        
        return OperationResult.PartialSuccess(loc.Get("Config.PartialSuccess", failedOps.Count), successCount, totalSteps);
    }

    public IEnumerable<string> GetExportedConfigs()
    {
        // 【P0-003 修复】：确保目录存在后再读取
        var (success, _) = EnsureConfigDirectory();
        if (!success || !Directory.Exists(_configDirectory))
        {
            return Enumerable.Empty<string>();
        }

        try
        {
            return Directory.GetFiles(_configDirectory, "*.json")
                .OrderByDescending(f => File.GetCreationTime(f));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConfigExport] Failed to list exported configs");
            return Enumerable.Empty<string>();
        }
    }

    private async Task<ExportedConfiguration> CollectConfigurationAsync(CancellationToken cancellationToken)
    {
        var appliedOptimizations = new List<string>();

        // 从 OptimizationEngine 获取已应用的优化项
        if (_optimizationEngine != null)
        {
            foreach (var module in _optimizationEngine.Modules)
            {
                try
                {
                    var statuses = await _optimizationEngine.GetStatusesAsync(module.Key, cancellationToken).ConfigureAwait(false);
                    for (var i = 0; i < module.Operations.Count && i < statuses.Count; i++)
                    {
                        if (statuses[i].IsCompliant)
                        {
                            appliedOptimizations.Add(module.Operations[i].Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ConfigExport] Failed to get status for module {Module}", module.Key);
                }
            }
        }

        return new ExportedConfiguration
        {
            AppliedOptimizations = appliedOptimizations
        };
    }

    private static bool IsVersionCompatible(string version)
    {
        // 目前只支持 1.x 版本
        return version.StartsWith("1.");
    }
}
