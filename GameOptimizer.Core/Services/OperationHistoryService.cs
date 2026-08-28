using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Services;

public sealed class OperationHistoryService : IOperationHistoryService
{
    private readonly ILogger<OperationHistoryService> _logger;
    private readonly string _historyPath;
    private readonly object _lock = new();
    private List<OperationHistoryRecord> _records = new();
    private bool _isLoaded;
    private const int MaxRecords = 1000;

    // P0-4: 路径脱敏 - 在记录前清理敏感信息
    private static OperationHistoryRecord SanitizeRecord(OperationHistoryRecord record)
    {
        return record with 
        { 
            Message = CommandSanitizer.SanitizePathForLogging(record.Message),
            ErrorDetail = record.ErrorDetail != null ? CommandSanitizer.SanitizePathForLogging(record.ErrorDetail) : null
        };
    }

    public int TotalCount
    {
        get
        {
            EnsureLoaded();
            lock (_lock) { return _records.Count; }
        }
    }

    public OperationHistoryService(ILogger<OperationHistoryService> logger)
    {
        _logger = logger;
        _historyPath = AppPaths.OperationHistoryFilePath;
    }

    public void Record(OperationHistoryRecord record)
    {
        EnsureLoaded();
        var sanitized = SanitizeRecord(record); // P0-4: 脱敏
        lock (_lock)
        {
            _records.Insert(0, sanitized);
            TrimIfNeeded();
        }
        QueueSave();
    }

    public void RecordBatch(IEnumerable<OperationHistoryRecord> records)
    {
        EnsureLoaded();
        lock (_lock)
        {
            foreach (var record in records)
            {
                _records.Insert(0, SanitizeRecord(record)); // P0-4: 脱敏
            }
            TrimIfNeeded();
        }
        QueueSave();
    }

    private int _pendingSaves;
    private void QueueSave()
    {
        if (Interlocked.CompareExchange(ref _pendingSaves, 1, 0) == 0)
        {
            Task.Run(async () =>
            {
                await Task.Delay(500).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingSaves, 0);
                await SaveAsync().ConfigureAwait(false);
            });
        }
    }

    public IReadOnlyList<OperationHistoryRecord> GetRecent(int count = 50)
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _records.Take(count).ToList();
        }
    }

    public IReadOnlyList<OperationHistoryRecord> GetByDateRange(DateTime from, DateTime to)
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _records
                .Where(r => r.Timestamp >= from && r.Timestamp <= to)
                .ToList();
        }
    }

    public IReadOnlyList<OperationHistoryRecord> GetByModule(string moduleKey, int count = 50)
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _records
                .Where(r => r.ModuleKey == moduleKey)
                .Take(count)
                .ToList();
        }
    }

    public IReadOnlyList<OperationHistorySummary> GetDailySummary(int days = 7)
    {
        EnsureLoaded();
        var cutoff = DateTime.Now.Date.AddDays(-days + 1);
        lock (_lock)
        {
            return _records
                .Where(r => r.Timestamp >= cutoff)
                .GroupBy(r => r.Timestamp.Date)
                .Select(g => new OperationHistorySummary
                {
                    Date = g.Key,
                    TotalOperations = g.Count(),
                    SuccessCount = g.Count(r => r.Success),
                    FailedCount = g.Count(r => !r.Success),
                    ApplyCount = g.Count(r => r.Action == OperationAction.Apply),
                    RestoreCount = g.Count(r => r.Action == OperationAction.Restore)
                })
                .OrderByDescending(s => s.Date)
                .ToList();
        }
    }

    public void ClearOlderThan(DateTime cutoff)
    {
        EnsureLoaded();
        lock (_lock)
        {
            var before = _records.Count;
            _records = _records.Where(r => r.Timestamp >= cutoff).ToList();
            var removed = before - _records.Count;
            if (removed > 0)
            {
                _logger.LogInformation("[OperationHistory] 清理了 {Count} 条过期记录", removed);
            }
        }
        QueueSave();
    }

    private void EnsureLoaded()
    {
        if (_isLoaded) return;
        lock (_lock)
        {
            if (_isLoaded) return;
            Load();
            _isLoaded = true;
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_historyPath))
            {
                var json = File.ReadAllText(_historyPath);
                _records = JsonSerializer.Deserialize<List<OperationHistoryRecord>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[OperationHistory] 加载历史记录失败");
            _records = new();
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                if (!SafeDirectoryOperations.TryCreateDirectory(dir, out var error))
                {
                    _logger.LogError("[OperationHistory] 创建目录失败: {Error}", error);
                    return;
                }
            }

            List<OperationHistoryRecord> snapshot;
            lock (_lock) { snapshot = _records.ToList(); }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await SafeFileWriter.WriteAllTextAsync(_historyPath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OperationHistory] 保存历史记录失败");
        }
    }

    private void TrimIfNeeded()
    {
        if (_records.Count > MaxRecords)
        {
            _records = _records.Take(MaxRecords).ToList();
        }
    }
}
