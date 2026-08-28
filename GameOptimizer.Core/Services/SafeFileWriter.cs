// File: GameOptimizer.Core/Services/SafeFileWriter.cs
using System.Text;
using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Core.Services;

public static class SafeFileWriter
{
    private static readonly Dictionary<string, object> _fileLocks = new();
    private static readonly object _lockMapLock = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);
    private const int MaxLockEntries = 100;
    
    /// <summary>
    /// 全局写入失败回调（可选）
    /// </summary>
    public static Action<string, Exception>? OnWriteFailed { get; set; }

    private static object GetFileLock(string path)
    {
        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        lock (_lockMapLock)
        {
            // Why: _fileLocks.Clear() 有竞态条件——线程A已获取旧锁对象，Clear后线程B
            //      对同一文件获取新锁对象，两个线程各持不同锁同时写同一文件。
            //      改为逐个移除超量条目，保留当前请求的路径不被误删。
            if (DateTime.UtcNow - _lastCleanup > CleanupInterval || _fileLocks.Count > MaxLockEntries)
            {
                var keysToRemove = _fileLocks.Keys
                    .Where(k => k != normalizedPath)
                    .Take(Math.Max(1, _fileLocks.Count - MaxLockEntries / 2))
                    .ToList();
                foreach (var key in keysToRemove)
                {
                    _fileLocks.Remove(key);
                }
                _lastCleanup = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Lock dictionary trimmed, removed {keysToRemove.Count}, remaining {_fileLocks.Count}");
            }
            
            if (!_fileLocks.TryGetValue(normalizedPath, out var fileLock))
            {
                fileLock = new object();
                _fileLocks[normalizedPath] = fileLock;
            }
            return fileLock;
        }
    }

    public static async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        
        var fileLock = GetFileLock(path);
        
        await Task.Run(() =>
        {
            lock (fileLock)
            {
                WriteAllTextInternal(path, content);
            }
        }, cancellationToken).ConfigureAwait(false);
    }
    
    /// <summary>
    /// 异步写入，带失败回调
    /// </summary>
    public static async Task<bool> TryWriteAllTextAsync(string path, string content, Action<Exception>? onError = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        
        var fileLock = GetFileLock(path);
        
        return await Task.Run(() =>
        {
            lock (fileLock)
            {
                return TryWriteAllTextInternal(path, content, onError);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public static void WriteAllText(string path, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        
        var fileLock = GetFileLock(path);
        lock (fileLock)
        {
            WriteAllTextInternal(path, content);
        }
    }
    
    /// <summary>
    /// 同步写入，返回是否成功
    /// </summary>
    public static bool TryWriteAllText(string path, string content, Action<Exception>? onError = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        
        var fileLock = GetFileLock(path);
        lock (fileLock)
        {
            return TryWriteAllTextInternal(path, content, onError);
        }
    }
    
    private static bool TryWriteAllTextInternal(string path, string content, Action<Exception>? onError)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            if (!SafeDirectoryOperations.TryCreateDirectory(directory, out var error))
            {
                var ex = new IOException($"无法创建目录: {error}");
                onError?.Invoke(ex);
                OnWriteFailed?.Invoke(path, ex);
                return false;
            }
        }

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        const int maxRetries = 3;
        Exception? lastException = null;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } 
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Cleanup temp failed: {ex.Message}"); }
                try { if (File.Exists(backupPath)) File.Delete(backupPath); } 
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Cleanup backup failed: {ex.Message}"); }

                File.WriteAllText(tempPath, content, Encoding.UTF8);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, backupPath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                    try { File.Delete(backupPath); } 
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Delete backup failed: {ex.Message}"); }
                }
                else
                {
                    File.Move(tempPath, path);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastException = ex;
                if (retry < maxRetries - 1)
                    Thread.Sleep(100 * (retry + 1));
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } 
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Final cleanup failed: {ex.Message}"); }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Failed to write {path}: {lastException?.Message}");
        if (lastException != null)
        {
            onError?.Invoke(lastException);
            OnWriteFailed?.Invoke(path, lastException);
        }
        return false;
    }

    private static void WriteAllTextInternal(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            if (!SafeDirectoryOperations.TryCreateDirectory(directory, out var error))
            {
                var ex = new IOException($"无法创建目录: {error}");
                OnWriteFailed?.Invoke(path, ex);
                System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Directory creation failed: {error}");
                return;
            }
        }

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        const int maxRetries = 3;
        Exception? lastException = null;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                // 清理可能存在的临时文件
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } 
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Cleanup temp failed: {ex.Message}"); }
                try { if (File.Exists(backupPath)) File.Delete(backupPath); } 
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Cleanup backup failed: {ex.Message}"); }

                File.WriteAllText(tempPath, content, Encoding.UTF8);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, backupPath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Replace 失败时，尝试直接删除后移动
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                    try { File.Delete(backupPath); } 
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Delete backup failed: {ex.Message}"); }
                }
                else
                {
                    File.Move(tempPath, path);
                }
                return; // 成功，退出
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastException = ex;
                if (retry < maxRetries - 1)
                    Thread.Sleep(100 * (retry + 1)); // 递增等待
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } 
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Final cleanup failed: {ex.Message}"); }
            }
        }

        // 所有重试都失败，静默忽略（不影响程序启动）
        System.Diagnostics.Debug.WriteLine($"[SafeFileWriter] Failed to write {path}: {lastException?.Message}");
    }
}
