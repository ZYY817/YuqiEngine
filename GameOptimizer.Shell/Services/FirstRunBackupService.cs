using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Optimization;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services;

public interface IFirstRunBackupService
{
    bool HasBackup { get; }
    DateTime? BackupTime { get; }
    Task<bool> CreateBackupIfNeededAsync();
    Task<bool> RestoreBackupAsync();
}

public sealed class FirstRunBackupService : IFirstRunBackupService
{
    private readonly ILogger<FirstRunBackupService> _logger;
    private readonly IOptimizationEngine _engine;
    private readonly string _backupDir;
    private readonly string _backupMetaPath;
    private BackupMetadata? _metadata;

    public bool HasBackup => _metadata != null && File.Exists(_backupMetaPath);
    public DateTime? BackupTime => _metadata?.CreatedAt;

    public FirstRunBackupService(ILogger<FirstRunBackupService> logger, IOptimizationEngine engine)
    {
        _logger = logger;
        _engine = engine;
        _backupDir = AppPaths.FirstRunBackupDir;
        _backupMetaPath = Path.Combine(_backupDir, "backup_meta.json");
        LoadMetadata();
    }

    private void LoadMetadata()
    {
        try
        {
            if (File.Exists(_backupMetaPath))
            {
                var (content, error) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(_backupMetaPath);
                if (string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(content))
                {
                    _metadata = JsonSerializer.Deserialize<BackupMetadata>(content);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FirstRunBackup] 加载备份元数据失败");
        }
    }

    public async Task<bool> CreateBackupIfNeededAsync()
    {
        if (HasBackup)
        {
            _logger.LogInformation("[FirstRunBackup] 已存在首次运行备份，跳过创建");
            return true;
        }

        try
        {
            if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(_backupDir, out var dirError))
            {
                _logger.LogError("[FirstRunBackup] 无法创建备份目录: {Error}", dirError);
                return false;
            }
            
            _logger.LogInformation("[FirstRunBackup] 开始创建首次运行系统状态备份...");

            var backupData = new SystemStateBackup
            {
                CreatedAt = DateTime.Now,
                Modules = new()
            };

            // 遍历所有模块，获取当前状态
            foreach (var module in _engine.Modules)
            {
                var moduleBackup = new ModuleStateBackup
                {
                    ModuleKey = module.Key,
                    Operations = new()
                };

                var statuses = await _engine.GetStatusesAsync(module.Key).ConfigureAwait(false);
                var opIndex = 0;
                foreach (var op in module.Operations)
                {
                    var status = opIndex < statuses.Count ? statuses[opIndex] : null;
                    moduleBackup.Operations.Add(new OperationStateBackup
                    {
                        OperationId = op.Id,
                        IsCompliant = status?.IsCompliant ?? false,
                        Details = status?.Details ?? ""
                    });
                    opIndex++;
                }

                backupData.Modules.Add(moduleBackup);
            }

            // 保存备份数据
            var backupPath = Path.Combine(_backupDir, "system_state.json");
            var json = JsonSerializer.Serialize(backupData, new JsonSerializerOptions { WriteIndented = true });
            await GameOptimizer.Core.Services.SafeFileWriter.WriteAllTextAsync(backupPath, json).ConfigureAwait(false);

            // 保存元数据
            _metadata = new BackupMetadata
            {
                CreatedAt = DateTime.Now,
                Version = "1.0",
                ModuleCount = backupData.Modules.Count,
                TotalOperations = backupData.Modules.Sum(m => m.Operations.Count)
            };
            var metaJson = JsonSerializer.Serialize(_metadata, new JsonSerializerOptions { WriteIndented = true });
            await GameOptimizer.Core.Services.SafeFileWriter.WriteAllTextAsync(_backupMetaPath, metaJson).ConfigureAwait(false);

            _logger.LogInformation("[FirstRunBackup] 首次运行备份创建成功，共 {Count} 个模块", backupData.Modules.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FirstRunBackup] 创建首次运行备份失败");
            return false;
        }
    }

    public async Task<bool> RestoreBackupAsync()
    {
        if (!HasBackup)
        {
            _logger.LogWarning("[FirstRunBackup] 没有可用的首次运行备份");
            return false;
        }

        try
        {
            var backupPath = Path.Combine(_backupDir, "system_state.json");
            if (!File.Exists(backupPath))
            {
                _logger.LogWarning("[FirstRunBackup] 备份文件不存在");
                return false;
            }

            _logger.LogInformation("[FirstRunBackup] 开始恢复首次运行备份...");
            
            var json = await File.ReadAllTextAsync(backupPath).ConfigureAwait(false);
            var backupData = JsonSerializer.Deserialize<SystemStateBackup>(json);
            
            if (backupData == null || backupData.Modules.Count == 0)
            {
                _logger.LogWarning("[FirstRunBackup] 备份数据为空或无效");
                return false;
            }

            var restoredCount = 0;
            var failedCount = 0;

            foreach (var moduleBackup in backupData.Modules)
            {
                try
                {
                    // 获取需要恢复的操作（当前已应用但备份时未应用的）
                    var opsToRestore = moduleBackup.Operations
                        .Where(op => !op.IsCompliant)
                        .Select(op => op.OperationId)
                        .ToList();

                    if (opsToRestore.Count > 0)
                    {
                        var result = await _engine.RestoreAsync(moduleBackup.ModuleKey, opsToRestore).ConfigureAwait(false);
                        restoredCount += result.Operations.Count(r => r.Success);
                        failedCount += result.Operations.Count(r => !r.Success);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[FirstRunBackup] 恢复模块 {Module} 失败", moduleBackup.ModuleKey);
                    failedCount++;
                }
            }

            _logger.LogInformation("[FirstRunBackup] 首次运行备份恢复完成: {Restored} 成功, {Failed} 失败", restoredCount, failedCount);
            return failedCount == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FirstRunBackup] 恢复首次运行备份失败");
            return false;
        }
    }

    private class BackupMetadata
    {
        public DateTime CreatedAt { get; set; }
        public string Version { get; set; } = "1.0";
        public int ModuleCount { get; set; }
        public int TotalOperations { get; set; }
    }

    private class SystemStateBackup
    {
        public DateTime CreatedAt { get; set; }
        public List<ModuleStateBackup> Modules { get; set; } = new();
    }

    private class ModuleStateBackup
    {
        public string ModuleKey { get; set; } = "";
        public List<OperationStateBackup> Operations { get; set; } = new();
    }

    private class OperationStateBackup
    {
        public string OperationId { get; set; } = "";
        public bool IsCompliant { get; set; }
        public string Details { get; set; } = "";
    }
}
