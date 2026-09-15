// File: GameOptimizer.Core/Services/SystemResourceChecker.cs
using System;
using System.Diagnostics;
using System.IO;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services;

public static class SystemResourceChecker
{
    private const long MinDiskSpaceBytes = 100 * 1024 * 1024; // 100MB
    private const long MinMemoryBytes = 50 * 1024 * 1024; // 50MB
    private const int MaxPathLength = 260;
    private const int LongPathMaxLength = 32767;
    
    public static ResourceCheckResult CheckDiskSpace(string path, long requiredBytes = 0)
    {
        var loc = LocalizationService.Instance;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return ResourceCheckResult.Fail(loc.Get("Resource.InvalidPath"));
            
            var driveInfo = new DriveInfo(root);
            if (!driveInfo.IsReady)
                return ResourceCheckResult.Fail(loc.Get("Resource.DriveNotReady", root));
            
            var required = requiredBytes > 0 ? requiredBytes : MinDiskSpaceBytes;
            if (driveInfo.AvailableFreeSpace < required)
            {
                var availableMb = driveInfo.AvailableFreeSpace / (1024 * 1024);
                var requiredMb = required / (1024 * 1024);
                return ResourceCheckResult.Fail(loc.Get("Resource.DiskSpaceLow", availableMb, requiredMb));
            }
            
            return ResourceCheckResult.Ok();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceChecker] CheckDiskSpace failed: {ex.Message}");
            return ResourceCheckResult.Fail(loc.Get("Resource.CheckFailed", ex.Message));
        }
    }
    
    public static ResourceCheckResult CheckMemory(long requiredBytes = 0)
    {
        var loc = LocalizationService.Instance;
        try
        {
            var required = requiredBytes > 0 ? requiredBytes : MinMemoryBytes;
            
            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            
            // 获取系统可用内存
            var memInfo = GC.GetGCMemoryInfo();
            var availableMemory = memInfo.TotalAvailableMemoryBytes;
            
            if (availableMemory < required)
            {
                var availableMb = availableMemory / (1024 * 1024);
                var requiredMb = required / (1024 * 1024);
                return ResourceCheckResult.Fail(loc.Get("Resource.MemoryLow", availableMb, requiredMb));
            }
            
            return ResourceCheckResult.Ok();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceChecker] CheckMemory failed: {ex.Message}");
            // 内存检查失败不应阻止操作
            return ResourceCheckResult.Ok();
        }
    }
    
    public static ResourceCheckResult CheckPath(string path)
    {
        var loc = LocalizationService.Instance;
        
        if (string.IsNullOrWhiteSpace(path))
            return ResourceCheckResult.Fail(loc.Get("Resource.PathEmpty"));
        
        // 检查路径长度
        if (path.Length > MaxPathLength)
        {
            // 检查是否支持长路径
            if (!IsLongPathEnabled())
                return ResourceCheckResult.Fail(loc.Get("Resource.PathTooLong", MaxPathLength));
            
            if (path.Length > LongPathMaxLength)
                return ResourceCheckResult.Fail(loc.Get("Resource.PathTooLong", LongPathMaxLength));
        }
        
        // 检查非法字符
        var invalidChars = Path.GetInvalidPathChars();
        foreach (var c in path)
        {
            if (Array.IndexOf(invalidChars, c) >= 0)
                return ResourceCheckResult.Fail(loc.Get("Resource.PathInvalidChars"));
        }
        
        return ResourceCheckResult.Ok();
    }
    
    public static string NormalizeLongPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        
        // 如果路径已经是长路径格式或者不需要转换
        if (path.StartsWith(@"\\?\") || path.Length <= MaxPathLength)
            return path;
        
        // 转换为长路径格式
        if (Path.IsPathRooted(path))
            return @"\\?\" + path;
        
        return path;
    }
    
    private static bool IsLongPathEnabled()
    {
        try
        {
            // Windows 10 1607+ 支持长路径
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\FileSystem");
            if (key != null)
            {
                var value = key.GetValue("LongPathsEnabled");
                return value is int intValue && intValue == 1;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceChecker] IsLongPathEnabled check failed: {ex.Message}");
        }
        return false;
    }
    
    public static ResourceCheckResult CheckAll(string? targetPath = null, long requiredDiskSpace = 0, long requiredMemory = 0)
    {
        // 检查内存
        var memResult = CheckMemory(requiredMemory);
        if (!memResult.Success)
            return memResult;
        
        // 检查磁盘空间
        if (!string.IsNullOrEmpty(targetPath))
        {
            var pathResult = CheckPath(targetPath);
            if (!pathResult.Success)
                return pathResult;
            
            var diskResult = CheckDiskSpace(targetPath, requiredDiskSpace);
            if (!diskResult.Success)
                return diskResult;
        }
        
        return ResourceCheckResult.Ok();
    }
}

public readonly struct ResourceCheckResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }
    
    private ResourceCheckResult(bool success, string? errorMessage)
    {
        Success = success;
        ErrorMessage = errorMessage;
    }
    
    public static ResourceCheckResult Ok() => new(true, null);
    public static ResourceCheckResult Fail(string message) => new(false, message);
    
    public void ThrowIfFailed()
    {
        if (!Success)
            throw new InvalidOperationException(ErrorMessage);
    }
}
