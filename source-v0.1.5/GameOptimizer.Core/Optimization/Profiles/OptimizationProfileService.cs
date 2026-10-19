using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Optimization;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Optimization.Profiles;

public sealed record OptimizationProfile(DateTime CapturedAtUtc, IReadOnlyDictionary<string, bool> OperationStates);

public sealed record OptimizationStatusSnapshot(
    DateTime CapturedAtUtc,
    int TotalOperations,
    int CompliantOperations,
    IReadOnlyDictionary<string, bool> OperationStates);

public sealed class OptimizationProfileService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IOptimizationEngine _engine;
    private readonly ILogger<OptimizationProfileService> _logger;
    private readonly string _profileDirectory;
    private readonly string _baselinePath;
    private readonly string _statusPath;
    private readonly string _reportsDirectory;

    public OptimizationProfileService(IOptimizationEngine engine, ILogger<OptimizationProfileService> logger)
    {
        _engine = engine;
        _logger = logger;

        // P2-006: 使用 AppPaths 常量替代硬编码路径
        var root = GameOptimizer.Core.Constants.AppPaths.OptimizationProfilesDir;

        _profileDirectory = root;
        _baselinePath = Path.Combine(root, "baseline.json");
        _statusPath = Path.Combine(root, "status.latest.json");
        _reportsDirectory = Path.Combine(root, "reports");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_profileDirectory);
        if (!File.Exists(_baselinePath))
        {
            _logger.LogInformation("未检测到基线备份，正在捕获首次启动配置。");
            await CaptureBaselineAsync(cancellationToken).ConfigureAwait(false);
        }

        await WriteStatusSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CaptureBaselineAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureStatusAsync(cancellationToken).ConfigureAwait(false);
        await WriteProfileAsync(_baselinePath, snapshot, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("基线备份已更新：{Path}", _baselinePath);
    }

    public async Task RestoreUserBaselineAsync(CancellationToken cancellationToken = default)
    {
        var baseline = await LoadProfileAsync(_baselinePath, cancellationToken).ConfigureAwait(false);
        if (baseline is null)
        {
            _logger.LogWarning("未找到基线备份，跳过恢复。");
            return;
        }

        foreach (var module in _engine.Modules)
        {
            var applyList = new List<string>();
            var restoreList = new List<string>();

            foreach (var operation in module.Operations)
            {
                if (!baseline.OperationStates.TryGetValue(operation.Id, out var baselineState))
                {
                    continue;
                }

                bool? current = null;
                try
                {
                    var status = await operation.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                    current = status.IsCompliant;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "无法读取 {OperationId} 的当前状态，跳过差异比较。", operation.Id);
                    continue;
                }

                if (baselineState && current == false)
                {
                    applyList.Add(operation.Id);
                }
                else if (!baselineState && current == true)
                {
                    restoreList.Add(operation.Id);
                }
            }

            if (applyList.Count > 0)
            {
                _logger.LogInformation("[Baseline] {Module} 重新应用 {Count} 项策略。", module.Key, applyList.Count);
                await _engine.ApplyAsync(module.Key, applyList, cancellationToken).ConfigureAwait(false);
            }

            if (restoreList.Count > 0)
            {
                _logger.LogInformation("[Baseline] {Module} 回滚 {Count} 项策略。", module.Key, restoreList.Count);
                await _engine.RestoreAsync(module.Key, restoreList, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task RestoreModuleBaselineAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        return RestoreModuleBaselineInternalAsync(moduleKey, cancellationToken);
    }

    public async Task RestoreWindowsDefaultsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var module in _engine.Modules)
        {
            var operationIds = module.Operations.Select(op => op.Id).ToList();
            if (operationIds.Count == 0)
            {
                continue;
            }

            _logger.LogInformation("[Defaults] {Module} 正在尝试恢复 {Count} 项策略。", module.Key, operationIds.Count);
            await _engine.RestoreAsync(module.Key, operationIds, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RestoreModuleDefaultsAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        var module = _engine.Modules.FirstOrDefault(
            descriptor => string.Equals(descriptor.Key, moduleKey, StringComparison.OrdinalIgnoreCase));
        if (module is null)
        {
            _logger.LogWarning("RestoreModuleDefaultsAsync: module {ModuleKey} not found", moduleKey);
            return;
        }

        var operationIds = module.Operations
            .Select(op => op.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if (operationIds.Count == 0)
        {
            _logger.LogInformation("RestoreModuleDefaultsAsync: module {ModuleKey} has no operations to restore", moduleKey);
            return;
        }

        _logger.LogInformation(
            "[Defaults] Restoring Windows defaults for {ModuleKey} ({Count} operations)",
            moduleKey,
            operationIds.Count);
        await _engine.RestoreAsync(module.Key, operationIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OptimizationStatusSnapshot> RunHealthScanAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureStatusAsync(cancellationToken).ConfigureAwait(false);
        await WriteProfileAsync(_statusPath, snapshot, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[HealthCheck] {Applied}/{Total} 条策略处于“已优化”状态。", snapshot.CompliantOperations, snapshot.TotalOperations);
        return snapshot;
    }

    public async Task<string> ExportStatusReportAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureStatusAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_reportsDirectory);
        var fileName = $"status-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        var fullPath = Path.Combine(_reportsDirectory, fileName);
        await WriteProfileAsync(fullPath, snapshot, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("巡检报告已导出：{Path}", fullPath);
        return fullPath;
    }

    private async Task RestoreModuleBaselineInternalAsync(string moduleKey, CancellationToken cancellationToken)
    {
        var module = _engine.Modules.FirstOrDefault(
            descriptor => string.Equals(descriptor.Key, moduleKey, StringComparison.OrdinalIgnoreCase));
        if (module is null)
        {
            _logger.LogWarning("RestoreModuleBaselineAsync: module {ModuleKey} not found", moduleKey);
            return;
        }

        var baseline = await LoadProfileAsync(_baselinePath, cancellationToken).ConfigureAwait(false);
        if (baseline is null)
        {
            _logger.LogWarning("RestoreModuleBaselineAsync: baseline missing, skip module-level restore");
            return;
        }

        var applyList = new List<string>();
        var restoreList = new List<string>();

        foreach (var operation in module.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.Id) ||
                !baseline.OperationStates.TryGetValue(operation.Id, out var baselineState))
            {
                continue;
            }

            bool? current = null;
            try
            {
                var status = await operation.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                current = status.IsCompliant;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to read {OperationId} status for module {ModuleKey}, skipping", moduleKey, operation.Id);
                continue;
            }

            if (baselineState && current == false)
            {
                applyList.Add(operation.Id);
            }
            else if (!baselineState && current == true)
            {
                restoreList.Add(operation.Id);
            }
        }

        if (applyList.Count == 0 && restoreList.Count == 0)
        {
            _logger.LogInformation("RestoreModuleBaselineAsync: {ModuleKey} already matches baseline", moduleKey);
            return;
        }

        if (applyList.Count > 0)
        {
            _logger.LogInformation("[Baseline] {ModuleKey} reapplied {Count} operations", moduleKey, applyList.Count);
            await _engine.ApplyAsync(module.Key, applyList, cancellationToken).ConfigureAwait(false);
        }

        if (restoreList.Count > 0)
        {
            _logger.LogInformation("[Baseline] {ModuleKey} rolled back {Count} operations", moduleKey, restoreList.Count);
            await _engine.RestoreAsync(module.Key, restoreList, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteStatusSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await CaptureStatusAsync(cancellationToken).ConfigureAwait(false);
        await WriteProfileAsync(_statusPath, snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OptimizationStatusSnapshot> CaptureStatusAsync(CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var compliant = 0;

        foreach (var module in _engine.Modules)
        {
            foreach (var operation in module.Operations)
            {
                total++;
                bool isCompliant = false;
                try
                {
                    var status = await operation.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                    isCompliant = status.IsCompliant;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "无法查询 {OperationId} 的状态，将视为未应用。", operation.Id);
                }

                if (isCompliant)
                {
                    compliant++;
                }

                states[operation.Id] = isCompliant;
            }
        }

        return new OptimizationStatusSnapshot(DateTime.UtcNow, total, compliant, states);
    }

    private static async Task WriteProfileAsync(string path, OptimizationStatusSnapshot snapshot, CancellationToken cancellationToken)
    {
        var profile = new OptimizationProfile(snapshot.CapturedAtUtc, snapshot.OperationStates);
        var payload = JsonSerializer.Serialize(profile, SerializerOptions);
        await Services.SafeFileWriter.WriteAllTextAsync(path, payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OptimizationProfile?> LoadProfileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<OptimizationProfile>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
}
