// File: GameOptimizer.Core/Services/Snapshot/SnapshotFileWriter.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Services.Snapshot
{
    /// <summary>
    /// 安全的快照文件写入器 - 带文件锁和原子操作
    /// </summary>
    public static class SnapshotFileWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 安全写入快照文件（带文件锁和原子操作）
        /// </summary>
        public static async Task<(bool Success, string? Error, OptimizationSnapshot? WrittenSnapshot)> 
            WriteSnapshotSafelyAsync(
                string filePath, 
                OptimizationSnapshot snapshot, 
                CancellationToken ct = default)
        {
            var tempPath = filePath + ".tmp";
            var lockPath = filePath + ".lock";
            FileStream? lockStream = null;
            
            try
            {
                // 0. 清理上次崩溃可能残留的 .tmp 文件
                CleanupStaleFile(tempPath);
                
                // 1. 获取文件锁（防止并发写入）
                lockStream = await AcquireFileLockAsync(lockPath, ct);
                if (lockStream == null)
                {
                    return (false, "无法获取文件锁，可能有其他进程正在操作", null);
                }
                
                Debug.WriteLine($"[SnapshotFileWriter] 已获取文件锁: {lockPath}");
                
                // 2. 序列化为JSON
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                
                // 3. 写入临时文件（带重试）
                var writeResult = await WriteWithRetryAsync(tempPath, json, ct);
                if (!writeResult.Success)
                {
                    return (false, $"写入临时文件失败: {writeResult.Error}", null);
                }
                
                // 4. 立即读取验证（带延迟）
                await Task.Delay(200, ct);
                
                var (readJson, readError) = await SafeFileHelper.ReadAllTextAsync(tempPath);
                if (string.IsNullOrEmpty(readJson))
                {
                    return (false, $"读取临时文件失败: {readError}", null);
                }
                
                // 5. 反序列化验证
                OptimizationSnapshot? writtenSnapshot;
                try
                {
                    writtenSnapshot = JsonSerializer.Deserialize<OptimizationSnapshot>(readJson, JsonOptions);
                }
                catch (JsonException ex)
                {
                    return (false, $"JSON反序列化失败: {ex.Message}", null);
                }
                
                if (writtenSnapshot == null)
                {
                    return (false, "反序列化返回null", null);
                }
                
                // 6. 校验和验证
                var originalChecksum = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
                var writtenChecksum = SnapshotChecksumCalculator.ComputeChecksum(writtenSnapshot);
                
                if (originalChecksum != writtenChecksum)
                {
                    var differences = SnapshotChecksumCalculator.CompareSnapshots(snapshot, writtenSnapshot);
                    var diffMsg = string.Join("; ", differences);
                    return (false, $"校验和不匹配 (原始={originalChecksum}, 写入={writtenChecksum}). 差异: {diffMsg}", writtenSnapshot);
                }
                
                Debug.WriteLine($"[SnapshotFileWriter] 校验通过: {originalChecksum}");
                
                // 7. 原子替换（删除旧文件，重命名临时文件）
                if (File.Exists(filePath))
                {
                    SafeFileHelper.DeleteFile(filePath);
                    await Task.Delay(100, ct);
                }
                
                File.Move(tempPath, filePath);
                Debug.WriteLine($"[SnapshotFileWriter] 文件写入成功: {filePath}");
                
                return (true, null, writtenSnapshot);
            }
            catch (OperationCanceledException)
            {
                CleanupStaleFile(tempPath);
                return (false, "操作已取消", null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SnapshotFileWriter] 写入失败: {ex.Message}");
                CleanupStaleFile(tempPath);
                return (false, ex.Message, null);
            }
            finally
            {
                if (lockStream != null)
                {
                    try
                    {
                        lockStream.Close();
                        lockStream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SnapshotFileWriter] 释放文件锁失败: {ex.Message}");
                    }
                }
                // Why: lock 文件由 AcquireFileLockAsync 手动管理，
                // 必须在 lockStream 关闭后再删除，确保无残留
                CleanupStaleFile(lockPath);
            }
        }
        
        private static void CleanupStaleFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Debug.WriteLine($"[SnapshotFileWriter] 已清理残留文件: {Path.GetFileName(path)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SnapshotFileWriter] 清理残留文件失败 {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取文件锁（带超时）
        /// </summary>
        private static async Task<FileStream?> AcquireFileLockAsync(string lockPath, CancellationToken ct)
        {
            const int maxAttempts = 10;
            const int delayMs = 500;
            
            // Why: 上次崩溃可能残留 lock 文件，先尝试清理
            CleanupStaleFile(lockPath);
            
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    // Why: 不用 DeleteOnClose，因为崩溃时它不会触发删除，
                    // 改为在 finally 中手动清理
                    var lockStream = new FileStream(
                        lockPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);
                    
                    return lockStream;
                }
                catch (IOException) when (i < maxAttempts - 1)
                {
                    Debug.WriteLine($"[SnapshotFileWriter] 文件锁被占用，重试 {i + 1}/{maxAttempts}");
                    await Task.Delay(delayMs, ct);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SnapshotFileWriter] 获取文件锁失败: {ex.Message}");
                    return null;
                }
            }
            
            return null;
        }

        /// <summary>
        /// 带重试的文件写入
        /// </summary>
        private static async Task<(bool Success, string? Error)> WriteWithRetryAsync(
            string path, 
            string content, 
            CancellationToken ct)
        {
            const int maxRetries = 3;
            
            for (int retry = 0; retry < maxRetries; retry++)
            {
                if (retry > 0)
                {
                    Debug.WriteLine($"[SnapshotFileWriter] 写入重试 {retry}/{maxRetries}");
                    await Task.Delay(500 * retry, ct);
                }
                
                var (success, error) = await SafeFileHelper.WriteAllTextAsync(path, content);
                if (success)
                {
                    return (true, null);
                }
                
                Debug.WriteLine($"[SnapshotFileWriter] 写入失败: {error}");
                
                if (retry == maxRetries - 1)
                {
                    return (false, error);
                }
            }
            
            return (false, "未知错误");
        }
    }
}
