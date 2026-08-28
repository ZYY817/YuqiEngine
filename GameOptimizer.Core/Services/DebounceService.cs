// File: GameOptimizer.Core/Services/DebounceService.cs
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Services;

public static class DebounceService
{
    private static readonly ConcurrentDictionary<string, DebounceEntry> _entries = new();
    private static readonly TimeSpan DefaultDebounceTime = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StaleEntryTtl = TimeSpan.FromMinutes(10);
    private static int _operationCount;
    private const int CleanupInterval = 100;
    
    private class DebounceEntry
    {
        public DateTime LastInvoke { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        public readonly object Lock = new();
    }
    
    public static bool CanExecute(string key, TimeSpan? debounceTime = null)
    {
        // Why: 每 100 次调用清理一次超过 10 分钟未使用的条目，防止内存泄漏
        if (Interlocked.Increment(ref _operationCount) % CleanupInterval == 0)
            PurgeStaleEntries();

        var interval = debounceTime ?? DefaultDebounceTime;
        var now = DateTime.UtcNow;
        
        var entry = _entries.GetOrAdd(key, _ => new DebounceEntry { LastInvoke = DateTime.MinValue });
        
        lock (entry.Lock)
        {
            if (now - entry.LastInvoke < interval)
                return false;
            
            entry.LastInvoke = now;
            return true;
        }
    }
    
    public static async Task<bool> ExecuteAsync(string key, Func<Task> action, TimeSpan? debounceTime = null)
    {
        if (!CanExecute(key, debounceTime))
            return false;
        
        await action().ConfigureAwait(false);
        return true;
    }
    
    public static async Task<T?> ExecuteAsync<T>(string key, Func<Task<T>> action, TimeSpan? debounceTime = null)
    {
        if (!CanExecute(key, debounceTime))
            return default;
        
        return await action().ConfigureAwait(false);
    }
    
    public static void Debounce(string key, Action action, TimeSpan? delay = null)
    {
        var interval = delay ?? DefaultDebounceTime;
        var entry = _entries.GetOrAdd(key, _ => new DebounceEntry());
        
        lock (entry.Lock)
        {
            // P2-3: Cancel 后立即 Dispose 旧 CTS，防止内存泄漏
            var oldCts = entry.Cts;
            oldCts?.Cancel();
            oldCts?.Dispose();
            entry.Cts = new CancellationTokenSource();
            var token = entry.Cts.Token;
            
            Task.Delay(interval, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    action();
                }
            }, TaskScheduler.Default);
        }
    }
    
    public static void DebounceAsync(string key, Func<Task> action, TimeSpan? delay = null)
    {
        var interval = delay ?? DefaultDebounceTime;
        var entry = _entries.GetOrAdd(key, _ => new DebounceEntry());
        
        lock (entry.Lock)
        {
            // P2-3: Cancel 后立即 Dispose 旧 CTS，防止内存泄漏
            var oldCts = entry.Cts;
            oldCts?.Cancel();
            oldCts?.Dispose();
            entry.Cts = new CancellationTokenSource();
            var token = entry.Cts.Token;
            
            Task.Delay(interval, token).ContinueWith(async t =>
            {
                if (!t.IsCanceled)
                {
                    await action().ConfigureAwait(false);
                }
            }, TaskScheduler.Default);
        }
    }
    
    private static void PurgeStaleEntries()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _entries)
        {
            var entry = kvp.Value;
            lock (entry.Lock)
            {
                if (now - entry.LastInvoke > StaleEntryTtl && entry.Cts == null)
                    _entries.TryRemove(kvp.Key, out _);
            }
        }
    }

    public static void Reset(string key)
    {
        if (_entries.TryRemove(key, out var entry))
        {
            lock (entry.Lock)
            {
                entry.Cts?.Cancel();
                entry.Cts?.Dispose();
            }
        }
    }
    
    public static void Clear()
    {
        foreach (var key in _entries.Keys)
        {
            Reset(key);
        }
    }
}
