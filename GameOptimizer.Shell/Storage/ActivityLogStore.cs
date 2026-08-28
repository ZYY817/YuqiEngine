using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Storage;

public sealed class ActivityLogStore : IDisposable
{
    private readonly string _storePath;
    private const int MaxRecords = 500;
    private const long MaxFileSizeBytes = 1024 * 1024; // 1MB
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public ActivityLogStore()
    {
        _storePath = AppPaths.ActivityLogFilePath;
        var logDir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(logDir))
        {
            if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(logDir, out var error))
            {
                System.Diagnostics.Debug.WriteLine($"[ActivityLogStore] 创建日志目录失败: {error}");
            }
        }
    }

    public IReadOnlyList<ActivityRecord> Load(int take = 100)
    {
        var (content, error) = SafeFileHelper.ReadAllText(_storePath);
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(content))
        {
            return Array.Empty<ActivityRecord>();
        }

        try
        {
            // P2-030: 检查文件大小，过大则截断
            var fileInfo = new FileInfo(_storePath);
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                System.Diagnostics.Debug.WriteLine($"[ActivityLogStore] File too large ({fileInfo.Length} bytes), truncating");
                TruncateLog();
            }
            
            var records = JsonSerializer.Deserialize<List<ActivityEntryDto>>(content) ?? new List<ActivityEntryDto>();
            return records
                .OrderByDescending(r => r.Timestamp)
                .Take(take)
                .Select(dto => new ActivityRecord(
                    dto.Timestamp, 
                    dto.CategoryKey ?? "Log.Category.System", 
                    dto.MessageKey ?? string.Empty,
                    dto.MessageArgs))
                .ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActivityLogStore] Load failed: {ex.Message}");
            return Array.Empty<ActivityRecord>();
        }
    }

    public void Save(IEnumerable<ActivityRecord> records)
    {
        if (_disposed) return;
        
        // Why: 超时防止死锁，5秒足够完成文件写入
        if (!_writeLock.Wait(TimeSpan.FromSeconds(5)))
        {
            System.Diagnostics.Debug.WriteLine("[ActivityLogStore][Save] 获取写锁超时，跳过本次保存");
            return;
        }
        try
        {
            // P2-030: 限制保存的记录数量
            var limitedRecords = records.Take(MaxRecords);
            var payload = limitedRecords
                .Select(r => new ActivityEntryDto(r.Timestamp, r.CategoryKey, r.MessageKey, r.MessageArgs))
                .ToList();
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            var success = SafeFileWriter.TryWriteAllText(_storePath, json);
            if (!success)
            {
                System.Diagnostics.Debug.WriteLine($"[ActivityLogStore] 保存失败");
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    private void TruncateLog()
    {
        if (_disposed) return;
        
        // Why: 超时防止死锁
        if (!_writeLock.Wait(TimeSpan.FromSeconds(5)))
        {
            System.Diagnostics.Debug.WriteLine("[ActivityLogStore][TruncateLog] 获取写锁超时，跳过截断");
            return;
        }
        try
        {
            var (content, readError) = SafeFileHelper.ReadAllText(_storePath);
            if (!string.IsNullOrEmpty(readError) || string.IsNullOrEmpty(content))
            {
                System.Diagnostics.Debug.WriteLine($"[ActivityLogStore] Truncate read failed: {readError}");
                return;
            }
            
            var records = JsonSerializer.Deserialize<List<ActivityEntryDto>>(content) ?? new List<ActivityEntryDto>();
            var truncated = records.OrderByDescending(r => r.Timestamp).Take(MaxRecords / 2).ToList();
            var newJson = JsonSerializer.Serialize(truncated, new JsonSerializerOptions { WriteIndented = true });
            var writeSuccess = SafeFileWriter.TryWriteAllText(_storePath, newJson);
            if (!writeSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"[ActivityLogStore] Truncate write failed");
                return;
            }
            System.Diagnostics.Debug.WriteLine($"[ActivityLogStore] Truncated to {truncated.Count} records");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActivityLogStore] Truncate failed: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();
    }

    private sealed record ActivityEntryDto(DateTime Timestamp, string CategoryKey, string MessageKey, object[]? MessageArgs);
}
