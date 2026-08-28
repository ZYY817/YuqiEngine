// File: GameOptimizer.Shell/Services/DebugLogger.cs
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services;

// Why: 临时诊断类，用于精准定位 UI 卡死根因。修复完成后安全删除此文件。
public static class DebugLogger
{
    private static readonly Stopwatch _globalStopwatch = Stopwatch.StartNew();
    private static readonly ConcurrentQueue<string> _buffer = new();
    private static readonly object _flushLock = new();
    private static readonly string _logPath = Path.Combine(AppPaths.LogsDir, "ui-freeze-debug.log");

    private static string ThreadTag
    {
        get
        {
            var threadId = Environment.CurrentManagedThreadId;
            var isUiThread = System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true;
            return isUiThread ? $"UI-Thread({threadId})" : $"BG-Thread({threadId})";
        }
    }

    public static IDisposable Measure(string module, string operation)
    {
        return new MeasureScope(module, operation);
    }

    public static void Log(string module, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{_globalStopwatch.ElapsedMilliseconds,6}ms] [{ThreadTag}] [{module}] {message}";
        _buffer.Enqueue(line);
        Debug.WriteLine(line);
        FlushIfNeeded();
    }

    public static void LogWindowState(string module, string dialogName, System.Windows.Window? window)
    {
        if (window == null)
        {
            Log(module, $"{dialogName}: window=null");
            return;
        }

        try
        {
            var ownerInfo = window.Owner != null ? $"Owner={window.Owner.GetType().Name}(IsActive={window.Owner.IsActive})" : "Owner=null";
            Log(module, $"{dialogName}: IsActive={window.IsActive}, IsVisible={window.IsVisible}, Topmost={window.Topmost}, " +
                        $"WindowState={window.WindowState}, Left={window.Left:F0}, Top={window.Top:F0}, {ownerInfo}");
        }
        catch (Exception ex)
        {
            Log(module, $"{dialogName}: 读取窗口状态失败 - {ex.Message}");
        }
    }

    public static void Flush()
    {
        lock (_flushLock)
        {
            try
            {
                var lines = new System.Text.StringBuilder();
                while (_buffer.TryDequeue(out var line))
                    lines.AppendLine(line);

                if (lines.Length > 0)
                    File.AppendAllText(_logPath, lines.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DebugLogger] Flush failed: {ex.Message}");
            }
        }
    }

    private static int _pendingCount;
    private static void FlushIfNeeded()
    {
        if (Interlocked.Increment(ref _pendingCount) >= 10)
        {
            Interlocked.Exchange(ref _pendingCount, 0);
            Flush();
        }
    }

    private sealed class MeasureScope : IDisposable
    {
        private readonly string _module;
        private readonly string _operation;
        private readonly long _startMs;

        public MeasureScope(string module, string operation)
        {
            _module = module;
            _operation = operation;
            _startMs = _globalStopwatch.ElapsedMilliseconds;
            Log(module, $"▶ START: {operation}");
        }

        public void Dispose()
        {
            var elapsed = _globalStopwatch.ElapsedMilliseconds - _startMs;
            var tag = elapsed > 500 ? "🔴 SLOW" : elapsed > 100 ? "🟡 WARN" : "🟢 OK";
            Log(_module, $"◀ END: {_operation} [{elapsed}ms] {tag}");
            Flush();
        }
    }
}
