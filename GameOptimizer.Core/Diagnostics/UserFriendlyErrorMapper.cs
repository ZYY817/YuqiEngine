// File: GameOptimizer.Core/Diagnostics/UserFriendlyErrorMapper.cs
using System;
using System.IO;
using System.Security;
using System.Text;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Diagnostics;

/// <summary>
/// 将技术性异常转换为用户友好的错误消息（支持本地化）
/// </summary>
public static class UserFriendlyErrorMapper
{
    /// <summary>
    /// 将异常转换为用户友好的消息
    /// </summary>
    public static string GetFriendlyMessage(Exception ex, string? context = null)
    {
        var loc = LocalizationService.Instance;
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(context))
        {
            sb.AppendLine(loc.Get("Error.OperationFailed", context));
            sb.AppendLine();
        }

        sb.AppendLine(GetErrorDescription(ex));
        
        var solution = GetSolution(ex);
        if (!string.IsNullOrEmpty(solution))
        {
            sb.AppendLine();
            sb.AppendLine(loc.Get("Error.SolutionHeader"));
            sb.AppendLine(solution);
        }

        return sb.ToString();
    }

    private static string GetErrorDescription(Exception ex)
    {
        var loc = LocalizationService.Instance;
        
        return ex switch
        {
            UnauthorizedAccessException => 
                $"{loc.Get("Error.UnauthorizedAccess.Title")}\n{loc.Get("Error.UnauthorizedAccess.Message")}",
            
            DirectoryNotFoundException => 
                $"{loc.Get("Error.DirectoryNotFound.Title")}\n{loc.Get("Error.DirectoryNotFound.Message")}",
            
            FileNotFoundException => 
                $"{loc.Get("Error.FileNotFound.Title")}\n{loc.Get("Error.FileNotFound.Message")}",
            
            PathTooLongException => 
                $"{loc.Get("Error.PathTooLong.Title")}\n{loc.Get("Error.PathTooLong.Message")}",
            
            IOException ioEx when ioEx.Message.Contains("space") || ioEx.Message.Contains("磁盘") => 
                $"{loc.Get("Error.DiskFull.Title")}\n{loc.Get("Error.DiskFull.Message")}",
            
            IOException ioEx when ioEx.Message.Contains("used") || ioEx.Message.Contains("占用") => 
                $"{loc.Get("Error.FileInUse.Title")}\n{loc.Get("Error.FileInUse.Message")}",
            
            IOException => 
                $"{loc.Get("Error.IOException.Title")}\n{ex.Message}",
            
            NotSupportedException => 
                $"{loc.Get("Error.NotSupported.Title")}\n{loc.Get("Error.NotSupported.Message")}",
            
            SecurityException => 
                $"{loc.Get("Error.Security.Title")}\n{loc.Get("Error.Security.Message")}",
            
            ArgumentException => 
                $"{loc.Get("Error.Argument.Title")}\n{loc.Get("Error.Argument.Message")}",
            
            InvalidOperationException => 
                $"{loc.Get("Error.InvalidOperation.Title")}\n{ex.Message}",
            
            _ => $"{loc.Get("Error.Generic.Title")}\n{ex.Message}"
        };
    }

    private static string? GetSolution(Exception ex)
    {
        var loc = LocalizationService.Instance;
        
        return ex switch
        {
            UnauthorizedAccessException => loc.Get("Error.UnauthorizedAccess.Solution"),
            DirectoryNotFoundException => loc.Get("Error.DirectoryNotFound.Solution"),
            PathTooLongException => loc.Get("Error.PathTooLong.Solution"),
            
            IOException ioEx when ioEx.Message.Contains("space") || ioEx.Message.Contains("磁盘") => 
                loc.Get("Error.DiskFull.Solution"),
            
            IOException ioEx when ioEx.Message.Contains("used") || ioEx.Message.Contains("占用") => 
                loc.Get("Error.FileInUse.Solution"),
            
            NotSupportedException => 
                loc.Get("Error.NotSupported.Solution"),
            
            SecurityException => 
                loc.Get("Error.Security.Solution"),
            
            ArgumentException => 
                loc.Get("Error.Argument.Solution"),
            
            _ => null
        };
    }

    /// <summary>
    /// 获取简短的错误摘要（用于日志或通知）
    /// </summary>
    public static string GetShortMessage(Exception ex)
    {
        var loc = LocalizationService.Instance;
        
        return ex switch
        {
            UnauthorizedAccessException => loc.Get("Error.UnauthorizedAccess.Short"),
            DirectoryNotFoundException => loc.Get("Error.DirectoryNotFound.Short"),
            FileNotFoundException => loc.Get("Error.FileNotFound.Short"),
            PathTooLongException => loc.Get("Error.PathTooLong.Short"),
            IOException ioEx when ioEx.Message.Contains("space") || ioEx.Message.Contains("磁盘") => 
                loc.Get("Error.DiskFull.Short"),
            IOException ioEx when ioEx.Message.Contains("used") || ioEx.Message.Contains("占用") => 
                loc.Get("Error.FileInUse.Short"),
            IOException => loc.Get("Error.IOException.Short"),
            NotSupportedException => loc.Get("Error.NotSupported.Short"),
            SecurityException => loc.Get("Error.Security.Short"),
            ArgumentException => loc.Get("Error.Argument.Short"),
            _ => loc.Get("Error.Generic.Short")
        };
    }
}
