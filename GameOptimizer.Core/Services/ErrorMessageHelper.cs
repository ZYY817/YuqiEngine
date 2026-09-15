// File: GameOptimizer.Core/Services/ErrorMessageHelper.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services;

public static class ErrorMessageHelper
{
    public static string GetFriendlyMessage(Exception ex)
    {
        var loc = LocalizationService.Instance;
        
        return ex switch
        {
            UnauthorizedAccessException uae => GetUnauthorizedMessage(uae, loc),
            SecurityException se => ElevationGate.DescribeWriteDenial("访问受保护资源", se.Message),
            Win32Exception w32 when w32.NativeErrorCode == 1223 => loc.Get("UAC.Denied"), // ERROR_CANCELLED
            FileNotFoundException fnf => GetFileNotFoundMessage(fnf, loc),
            DirectoryNotFoundException => loc.Get("Error.Friendly.DirectoryNotFound"),
            IOException ioe when ioe.Message.Contains("being used") || ioe.Message.Contains("another process") => GetFileInUseMessage(ioe, loc),
            IOException ioe when ioe.Message.Contains("disk") => loc.Get("Error.Friendly.DiskFull"),
            IOException ioe when ioe.HResult == unchecked((int)0x80070070) => loc.Get("Error.Friendly.DiskFull"), // ERROR_DISK_FULL
            TimeoutException => loc.Get("Error.Friendly.NetworkTimeout"),
            OperationCanceledException => loc.Get("Common.Cancelled"),
            InvalidOperationException => loc.Get("Error.Friendly.InvalidOperation"),
            System.Management.ManagementException => loc.Get("Error.Friendly.WmiTimeout"),
            ArgumentException ae when ae.Message.Contains("process") => loc.Get("Error.Friendly.ProcessNotFound"),
            PathTooLongException => loc.Get("Resource.PathTooLong", 260),
            OutOfMemoryException => loc.Get("Resource.MemoryLow", 0, 0),
            _ => GetGenericFriendlyMessage(ex, loc)
        };
    }
    
    private static string GetFileNotFoundMessage(FileNotFoundException ex, LocalizationService loc)
    {
        var fileName = ex.FileName;
        if (!string.IsNullOrEmpty(fileName))
        {
            var shortName = Path.GetFileName(fileName);
            return loc.Get("Error.Friendly.FileNotFound", shortName);
        }
        return loc.Get("Error.Friendly.FileNotFoundGeneric");
    }
    
    private static string GetFileInUseMessage(IOException ex, LocalizationService loc)
    {
        var msg = ex.Message;
        var pathStart = msg.IndexOf("'");
        var pathEnd = msg.LastIndexOf("'");
        
        if (pathStart >= 0 && pathEnd > pathStart)
        {
            var filePath = msg.Substring(pathStart + 1, pathEnd - pathStart - 1);
            var fileName = Path.GetFileName(filePath);
            return loc.Get("Error.Friendly.FileInUseWithName", fileName);
        }
        
        return loc.Get("Error.Friendly.FileInUse");
    }
    
    private static string GetUnauthorizedMessage(UnauthorizedAccessException ex, LocalizationService loc)
    {
        return ElevationGate.DescribeWriteDenial("访问系统资源", ex.Message);
    }
    
    private static string GetGenericFriendlyMessage(Exception ex, LocalizationService loc)
    {
        var msg = ex.Message.ToLowerInvariant();
        
        if (msg.Contains("could not find") && msg.Contains("file"))
            return loc.Get("Error.Friendly.FileNotFoundGeneric");
        if (msg.Contains("access") && msg.Contains("denied"))
            return ElevationGate.DescribeWriteDenial("访问系统资源", ex.Message);
        if (msg.Contains("timeout") || msg.Contains("timed out"))
            return loc.Get("Error.Friendly.NetworkTimeout");
        if (msg.Contains("not found") || msg.Contains("does not exist"))
            return loc.Get("Error.Friendly.ProcessNotFound");
        if (msg.Contains("restore point"))
            return loc.Get("Error.Friendly.RestorePointFailed");
        if (msg.Contains("disk") && (msg.Contains("full") || msg.Contains("space")))
            return loc.Get("Error.Friendly.DiskFull");
        if (msg.Contains("cancelled") || msg.Contains("canceled"))
            return loc.Get("UAC.Denied");
        if (msg.Contains("being used") || msg.Contains("another process"))
            return loc.Get("Error.Friendly.FileInUse");
            
        Debug.WriteLine($"[ErrorHelper] Unhandled exception type: {ex.GetType().Name}, Message: {ex.Message}");
        return loc.Get("Error.Friendly.Unknown");
    }
    
    public static string GetFriendlyMessageWithDetails(Exception ex)
    {
        var friendly = GetFriendlyMessage(ex);
        
        #if DEBUG
        return $"{friendly}\n\n[Debug] {ex.GetType().Name}: {ex.Message}";
        #else
        return friendly;
        #endif
    }
    
    public static string GetAdminRetryMessage()
    {
        return ElevationGate.DescribeWriteDenial("系统写入");
    }
    
    public static bool IsPermissionError(Exception ex)
    {
        if (ex is UnauthorizedAccessException or SecurityException)
            return true;
        if (ex is Win32Exception w32 && w32.NativeErrorCode == 5) // ERROR_ACCESS_DENIED
            return true;
        return false;
    }
    
    public static bool IsUacCancelled(Exception ex)
    {
        return ex is Win32Exception w32 && w32.NativeErrorCode == 1223; // ERROR_CANCELLED
    }
}
