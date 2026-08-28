using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services;

public sealed class OperationLock : IDisposable
{
    private static readonly Dictionary<string, LockEntry> _locks = new();
    private static readonly object _globalLock = new();
    private static Timer? _cleanupTimer;
    private static readonly TimeSpan StaleTimeout = TimeSpan.FromMinutes(10);
    private static bool _isShutdown;
    private static CancellationTokenSource _shutdownCancellation = new();
    
    private readonly string _resourceId;
    private readonly LockEntry _entry;
    private readonly SemaphoreSlim _semaphore;
    private bool _acquired;
    private bool _disposed;

    private class LockEntry
    {
        public SemaphoreSlim Semaphore { get; }
        public DateTime LastAccess { get; set; }
        public int RefCount { get; set; }
        
        public LockEntry(SemaphoreSlim semaphore)
        {
            Semaphore = semaphore;
            LastAccess = DateTime.UtcNow;
            RefCount = 1;
        }
    }

    static OperationLock()
    {
        _cleanupTimer = new Timer(_ => CleanupStaleLocks(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }
    
    /// <summary>
    /// 应用退出时调用，停止清理定时器并释放所有锁
    /// </summary>
    public static void Shutdown()
    {
        lock (_globalLock)
        {
            _isShutdown = true;
            _shutdownCancellation.Cancel();
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;

            var idleKeys = _locks
                .Where(kv => kv.Value.RefCount == 0)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in idleKeys)
            {
                if (_locks.Remove(key, out var entry))
                {
                    try { entry.Semaphore.Dispose(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[OperationLock] Semaphore dispose failed: {ex.Message}"); }
                }
            }
        }
    }

    private static void CleanupStaleLocks()
    {
        if (_isShutdown) return;
        
        lock (_globalLock)
        {
            if (_isShutdown) return;
            
            var staleKeys = _locks
                .Where(kv => kv.Value.RefCount == 0 && DateTime.UtcNow - kv.Value.LastAccess > StaleTimeout)
                .Select(kv => kv.Key)
                .ToList();
            
            foreach (var key in staleKeys)
            {
                if (_locks.TryGetValue(key, out var entry))
                {
                    _locks.Remove(key);
                    entry.Semaphore.Dispose();
                }
            }
        }
    }

    private OperationLock(string resourceId, LockEntry entry)
    {
        _resourceId = resourceId;
        _entry = entry;
        _semaphore = entry.Semaphore;
    }

    public static async Task<OperationLock?> TryAcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(resourceId, nameof(resourceId));
        
        // Why: CleanupStaleLocks 可能在 lock 外的 WaitAsync 之前 Dispose 了 semaphore，
        // 导致 ObjectDisposedException。重试一次即可拿到新的 semaphore。
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (_isShutdown) return null;
            
            LockEntry entry;
            lock (_globalLock)
            {
                if (_isShutdown) return null;
                
                if (!_locks.TryGetValue(resourceId, out var existingEntry))
                {
                    entry = new LockEntry(new SemaphoreSlim(1, 1));
                    _locks[resourceId] = entry;
                }
                else
                {
                    entry = existingEntry;
                    entry.RefCount++;
                    entry.LastAccess = DateTime.UtcNow;
                }
            }

            var semaphore = entry.Semaphore;
            var operationLock = new OperationLock(resourceId, entry);
            
            try
            {
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _shutdownCancellation.Token);
                operationLock._acquired = await semaphore.WaitAsync(timeout, waitCancellation.Token).ConfigureAwait(false);
                if (operationLock._acquired && _isShutdown)
                {
                    operationLock._acquired = false;
                    try { semaphore.Release(); }
                    catch (ObjectDisposedException) { }
                    ReleaseReference(resourceId, entry);
                    return null;
                }
                if (!operationLock._acquired)
                {
                    ReleaseReference(resourceId, entry);
                }
                return operationLock._acquired ? operationLock : null;
            }
            catch (OperationCanceledException)
            {
                ReleaseReference(resourceId, entry);
                return null;
            }
            catch (ObjectDisposedException)
            {
                // Why: CleanupStaleLocks 或 Shutdown 释放了 semaphore。
                // 递减引用计数后重试（下次循环会创建新的 LockEntry）。
                ReleaseReference(resourceId, entry);
                if (_isShutdown) return null;
                // 第一次重试，第二次放弃
                continue;
            }
        }
        
        return null;
    }

    public static async Task<OperationLock> AcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(resourceId, nameof(resourceId));
        
        // Why: 与 TryAcquireAsync 相同，CleanupStaleLocks 可能导致 ObjectDisposedException，重试一次
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (_isShutdown)
                throw new ObjectDisposedException(nameof(OperationLock), LocalizationService.Instance.Get("OperationLock.Disposed"));
            
            LockEntry entry;
            lock (_globalLock)
            {
                if (_isShutdown)
                    throw new ObjectDisposedException(nameof(OperationLock), LocalizationService.Instance.Get("OperationLock.Disposed"));
                
                if (!_locks.TryGetValue(resourceId, out var existingEntry))
                {
                    entry = new LockEntry(new SemaphoreSlim(1, 1));
                    _locks[resourceId] = entry;
                }
                else
                {
                    entry = existingEntry;
                    entry.RefCount++;
                    entry.LastAccess = DateTime.UtcNow;
                }
            }

            var semaphore = entry.Semaphore;
            var operationLock = new OperationLock(resourceId, entry);
            try
            {
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _shutdownCancellation.Token);
                await semaphore.WaitAsync(waitCancellation.Token).ConfigureAwait(false);
                if (_isShutdown)
                {
                    try { semaphore.Release(); }
                    catch (ObjectDisposedException) { }
                    throw new ObjectDisposedException(nameof(OperationLock), LocalizationService.Instance.Get("OperationLock.Disposed"));
                }
                operationLock._acquired = true;
                return operationLock;
            }
            catch (OperationCanceledException) when (_isShutdown)
            {
                ReleaseReference(resourceId, entry);
                throw new ObjectDisposedException(nameof(OperationLock), LocalizationService.Instance.Get("OperationLock.Disposed"));
            }
            catch (ObjectDisposedException) when (!_isShutdown)
            {
                // Why: CleanupStaleLocks 释放了 semaphore，递减引用计数后重试
                ReleaseReference(resourceId, entry);
                continue;
            }
            catch
            {
                ReleaseReference(resourceId, entry);
                throw;
            }
        }
        
        // 两次都失败，抛异常
        throw new InvalidOperationException($"Failed to acquire lock for {resourceId} after retries");
    }

    public static bool IsLocked(string resourceId)
    {
        Guard.NotNullOrWhiteSpace(resourceId, nameof(resourceId));
        lock (_globalLock)
        {
            if (_locks.TryGetValue(resourceId, out var entry))
            {
                return entry.Semaphore.CurrentCount == 0;
            }
            return false;
        }
    }

    private static void ReleaseReference(string resourceId, LockEntry expectedEntry)
    {
        lock (_globalLock)
        {
            if (_locks.TryGetValue(resourceId, out var entry) && ReferenceEquals(entry, expectedEntry))
            {
                entry.RefCount--;
                entry.LastAccess = DateTime.UtcNow;
                // 不立即删除，让清理定时器处理过期锁
                if (entry.RefCount <= 0)
                {
                    entry.RefCount = 0;
                    if (_isShutdown && _locks.Remove(resourceId))
                    {
                        try { entry.Semaphore.Dispose(); }
                        catch (ObjectDisposedException) { }
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_acquired)
        {
            try
            {
                _semaphore.Release();
            }
            catch (ObjectDisposedException) when (_isShutdown)
            {
                // Why: Shutdown may dispose active semaphores before their owning
                // operation unwinds. The lease is already invalidated; still finish
                // the reference-count cleanup without surfacing a shutdown error.
            }
        }
        ReleaseReference(_resourceId, _entry);
    }
}

public static class OperationLockExtensions
{
    public static async Task<OperationResult<T>> WithLockAsync<T>(
        this string resourceId,
        Func<CancellationToken, Task<T>> operation,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
        
        using var operationLock = await OperationLock.TryAcquireAsync(resourceId, actualTimeout, cancellationToken);
        if (operationLock == null)
        {
            return OperationResult<T>.Fail(LocalizationService.Instance.Get("OperationLock.CannotAcquire", resourceId));
        }

        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);
            return OperationResult<T>.Success(result);
        }
        catch (OperationCanceledException)
        {
            return new OperationResult<T> { Code = OperationResultCode.Cancelled, Message = LocalizationService.Instance.Get("OperationLock.Cancelled") };
        }
        catch (Exception ex)
        {
            return OperationResult<T>.Fail(LocalizationService.Instance.Get("OperationLock.Failed"), ex);
        }
    }
}
