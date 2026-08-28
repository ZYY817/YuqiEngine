// File: GameOptimizer.Shell/Services/ApplyOperationTracer.cs
using System;
using System.IO;
using System.Text;
using System.Threading;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services;

public static class ApplyOperationTracer
{
    private static readonly string LogFilePath = Path.Combine(AppPaths.LogsDir, "apply-operation-trace.log");
    
    private static readonly object _lock = new object();
    private static int _operationCounter = 0;
    
    static ApplyOperationTracer()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            File.WriteAllText(LogFilePath, $"=== Apply Operation Trace Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===\n");
        }
        catch { }
    }
    
    public static int StartOperation(string moduleKey)
    {
        var opId = Interlocked.Increment(ref _operationCounter);
        Log(opId, "START", $"Module={moduleKey}, Thread={Thread.CurrentThread.ManagedThreadId}");
        return opId;
    }
    
    public static void Log(int operationId, string step, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var isUiThread = System.Windows.Application.Current?.Dispatcher.CheckAccess() ?? false;
            
            var logLine = $"[{timestamp}] [Op{operationId}] [T{threadId}] [{(isUiThread ? "UI" : "BG")}] [{step}] {message}\n";
            
            lock (_lock)
            {
                File.AppendAllText(LogFilePath, logLine, Encoding.UTF8);
            }
        }
        catch { }
    }
    
    public static void LogException(int operationId, string step, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"EXCEPTION: {ex.GetType().Name}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine($"StackTrace: {ex.StackTrace}");
        
        if (ex.InnerException != null)
        {
            sb.AppendLine($"InnerException: {ex.InnerException.GetType().Name}");
            sb.AppendLine($"InnerMessage: {ex.InnerException.Message}");
        }
        
        Log(operationId, step, sb.ToString());
    }
    
    public static string GetLogFilePath() => LogFilePath;
}
