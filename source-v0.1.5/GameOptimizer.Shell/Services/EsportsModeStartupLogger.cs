// File: GameOptimizer.Shell/Services/EsportsModeStartupLogger.cs
using System;
using System.IO;
using System.Text;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 电竞模式启动诊断日志服务
/// </summary>
public static class EsportsModeStartupLogger
{
    private static readonly string LogFilePath = Path.Combine(
        AppPaths.LogsDir,
        $"EsportsMode_Startup_{DateTime.Now:yyyyMMdd_HHmmss}.log");

    private static readonly object _lock = new();
    private static bool _initialized;

    static EsportsModeStartupLogger()
    {
        try
        {
            var logDir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            _initialized = true;
        }
        catch
        {
            _initialized = false;
        }
    }

    public static void Log(string stage, string message, Exception? ex = null)
    {
        if (!_initialized) return;

        try
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {message}");
                
                if (ex != null)
                {
                    sb.AppendLine($"  Exception: {ex.GetType().Name}");
                    sb.AppendLine($"  Message: {ex.Message}");
                    sb.AppendLine($"  StackTrace: {ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        sb.AppendLine($"  InnerException: {ex.InnerException.Message}");
                    }
                }

                File.AppendAllText(LogFilePath, sb.ToString());
            }
        }
        catch
        {
            // 静默失败，不影响主流程
        }
    }

    public static void LogSuccess(string stage)
    {
        Log(stage, "✓ SUCCESS");
    }

    public static void LogError(string stage, string error, Exception? ex = null)
    {
        Log(stage, $"✗ ERROR: {error}", ex);
    }

    public static void LogWarning(string stage, string warning)
    {
        Log(stage, $"⚠ WARNING: {warning}");
    }

    public static string GetLogFilePath() => LogFilePath;
}
