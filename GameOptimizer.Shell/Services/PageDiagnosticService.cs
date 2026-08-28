using System;
using System.IO;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services;

public static class PageDiagnosticService
{
    private static string LogPath => AppPaths.PageDiagnosticLogPath;
    
    private static readonly object _lock = new();
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5MB
    
    public static void Log(string pageName, string message)
    {
        try
        {
            lock (_lock)
            {
                // 日志文件超过 5MB 时截断
                if (File.Exists(LogPath))
                {
                    var fileInfo = new FileInfo(LogPath);
                    if (fileInfo.Length > MaxLogSizeBytes)
                    {
                        try { File.Delete(LogPath); } catch { }
                    }
                }
                
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{pageName}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine, System.Text.Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PageDiagnosticService] Log error: {ex.Message}");
        }
    }
    
    public static void LogError(string pageName, string message, Exception? ex = null)
    {
        var errorMsg = ex != null ? $"{message}: {ex.Message}" : message;
        Log(pageName, $"ERROR - {errorMsg}");
    }
    
    public static void LogLoaded(string pageName, bool success, string details = "")
    {
        var status = success ? "SUCCESS" : "FAILED";
        Log(pageName, $"Page Loaded: {status} {details}");
    }
    
    public static void ClearLog()
    {
        try { File.Delete(LogPath); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PageDiagnosticService] ClearLog error: {ex.Message}");
        }
    }
}
