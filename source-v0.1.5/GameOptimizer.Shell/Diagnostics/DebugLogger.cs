// File: GameOptimizer.Shell/Diagnostics/DebugLogger.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Diagnostics;

/// <summary>
/// 临时诊断日志 — 修复完成后删除此文件
/// </summary>
public static class DebugLogger
{
    private static readonly Stopwatch _globalSw = Stopwatch.StartNew();
    private static readonly string _logPath;
    private static readonly object _lock = new();

    static DebugLogger()
    {
        var dir = Path.Combine(AppPaths.LogsDir, "diag");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, $"freeze-diag-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Log("DebugLogger", "=== 诊断日志启动 ===");
    }

    public static string LogPath => _logPath;

    public static void Log(string tag, string message, [CallerMemberName] string? caller = null)
    {
        var threadType = Thread.CurrentThread.ManagedThreadId == 1 ? "UI" : "BG";
        var tid = Thread.CurrentThread.ManagedThreadId;
        var elapsed = _globalSw.ElapsedMilliseconds;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{elapsed,6}ms] [T{tid:D3}/{threadType}] [{tag}] {caller}: {message}";

        Debug.WriteLine(line);

        lock (_lock)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); }
            catch { /* ignore */ }
        }
    }

    public static IDisposable Timed(string tag, string operation, [CallerMemberName] string? caller = null)
    {
        return new TimedScope(tag, operation, caller);
    }

    private sealed class TimedScope : IDisposable
    {
        private readonly string _tag, _operation, _caller;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public TimedScope(string tag, string operation, string? caller)
        {
            _tag = tag;
            _operation = operation;
            _caller = caller ?? "?";
            Log(_tag, $"▶ 开始: {_operation}", _caller);
        }

        public void Dispose()
        {
            _sw.Stop();
            Log(_tag, $"◀ 完成: {_operation} ({_sw.ElapsedMilliseconds}ms)", _caller);
        }
    }
}
