// File: GameOptimizer.Core/Services/SafeFileHelper.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 安全的文件操作助手
/// 解决 P0-004: 文件操作路径陷阱
/// </summary>
public static class SafeFileHelper
{
    /// <summary>
    /// 安全读取文件内容
    /// </summary>
    public static (string? Content, string? Error) ReadAllText(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (null, "文件路径为空");
            }
            
            if (!File.Exists(path))
            {
                return (null, $"文件不存在: {path}");
            }
            
            var content = File.ReadAllText(path);
            return (content, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeFile] No permission to read {path}: {ex.Message}");
            return (null, $"无权限读取文件: {path}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[SafeFile] IO error reading {path}: {ex.Message}");
            return (null, $"读取文件失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFile] Failed to read {path}: {ex.Message}");
            return (null, $"读取文件时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全写入文件内容（自动创建目录）
    /// </summary>
    public static (bool Success, string? Error) WriteAllText(string path, string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (false, "文件路径为空");
            }
            
            // 【P0-横向修复】：使用 SafeDirectoryOperations 确保目录存在
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                if (!SafeDirectoryOperations.TryCreateDirectory(directory, out var dirError))
                {
                    return (false, $"无法创建目录: {dirError}");
                }
            }
            
            File.WriteAllText(path, content);
            return (true, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeFile] No permission to write {path}: {ex.Message}");
            return (false, $"无权限写入文件: {path}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[SafeFile] IO error writing {path}: {ex.Message}");
            return (false, $"写入文件失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFile] Failed to write {path}: {ex.Message}");
            return (false, $"写入文件时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全异步读取文件内容
    /// </summary>
    public static async Task<(string? Content, string? Error)> ReadAllTextAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (null, "文件路径为空");
            }
            
            if (!File.Exists(path))
            {
                return (null, $"文件不存在: {path}");
            }
            
            var content = await File.ReadAllTextAsync(path);
            return (content, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeFile] No permission to read {path}: {ex.Message}");
            return (null, $"无权限读取文件: {path}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[SafeFile] IO error reading {path}: {ex.Message}");
            return (null, $"读取文件失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFile] Failed to read {path}: {ex.Message}");
            return (null, $"读取文件时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全异步写入文件内容（自动创建目录）
    /// </summary>
    public static async Task<(bool Success, string? Error)> WriteAllTextAsync(string path, string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (false, "文件路径为空");
            }
            
            // 【P0-横向修复】：使用 SafeDirectoryOperations 确保目录存在
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                if (!SafeDirectoryOperations.TryCreateDirectory(directory, out var dirError))
                {
                    return (false, $"无法创建目录: {dirError}");
                }
            }
            
            await File.WriteAllTextAsync(path, content);
            return (true, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeFile] No permission to write {path}: {ex.Message}");
            return (false, $"无权限写入文件: {path}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[SafeFile] IO error writing {path}: {ex.Message}");
            return (false, $"写入文件失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFile] Failed to write {path}: {ex.Message}");
            return (false, $"写入文件时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全删除文件
    /// </summary>
    public static (bool Success, string? Error) DeleteFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (false, "文件路径为空");
            }
            
            if (!File.Exists(path))
            {
                return (true, null); // 文件不存在视为成功
            }
            
            File.Delete(path);
            return (true, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeFile] No permission to delete {path}: {ex.Message}");
            return (false, $"无权限删除文件: {path}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[SafeFile] IO error deleting {path}: {ex.Message}");
            return (false, $"删除文件失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFile] Failed to delete {path}: {ex.Message}");
            return (false, $"删除文件时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 安全创建目录
    /// </summary>
    public static (bool Success, string? Error) CreateDirectory(string path)
    {
        // 【P0-横向修复】：直接使用 SafeDirectoryOperations
        return SafeDirectoryOperations.TryCreateDirectory(path, out var error) 
            ? (true, null) 
            : (false, error);
    }
    
    /// <summary>
    /// 检查文件是否存在且可读
    /// </summary>
    public static bool IsFileAccessible(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }
            
            // 尝试打开文件以验证访问权限
            using var stream = File.OpenRead(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查目录是否存在且可写
    /// </summary>
    public static bool IsDirectoryWritable(string path)
    {
        // 【P0-横向修复】：直接使用 SafeDirectoryOperations 的权限检查
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        
        if (!Directory.Exists(path))
        {
            if (!SafeDirectoryOperations.TryCreateDirectory(path, out _))
            {
                return false;
            }
        }
        
        return SafeDirectoryOperations.CheckWritePermission(path, out _);
    }
    
    /// <summary>
    /// 安全复制文件（自动创建目标目录）
    /// </summary>
    public static (bool Success, string? Error) CopyFile(string sourcePath, string destPath, bool overwrite = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return (false, "源文件路径为空");
            }
            
            if (string.IsNullOrWhiteSpace(destPath))
            {
                return (false, "目标文件路径为空");
            }
            
            if (!File.Exists(sourcePath))
            {
                return (false, $"源文件不存在: {sourcePath}");
            }
            
            // 【P0-横向修复】：使用 SafeDirectoryOperations 确保目标目录存在
            var destDirectory = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDirectory))
            {
                if (!SafeDirectoryOperations.TryCreateDirectory(destDirectory, out var dirError))
                {
                    return (false, $"无法创建目标目录: {dirError}");
                }
            }
            
            File.Copy(sourcePath, destPath, overwrite);
            return (true, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeFile] No permission to copy {sourcePath} to {destPath}: {ex.Message}");
            return (false, $"无权限复制文件");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[SafeFile] IO error copying {sourcePath} to {destPath}: {ex.Message}");
            return (false, $"复制文件失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFile] Failed to copy {sourcePath} to {destPath}: {ex.Message}");
            return (false, $"复制文件时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 获取文件大小（字节）
    /// </summary>
    public static (long Size, string? Error) GetFileSize(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (0, "文件路径为空");
            }
            
            if (!File.Exists(path))
            {
                return (0, $"文件不存在: {path}");
            }
            
            var fileInfo = new FileInfo(path);
            return (fileInfo.Length, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFile] Failed to get file size for {path}: {ex.Message}");
            return (0, $"获取文件大小失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// P2-004: 安全移动文件（自动创建目标目录）
    /// </summary>
    public static (bool Success, string? Error) MoveFile(string sourcePath, string destPath, bool overwrite = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return (false, "源文件路径为空");
            }
            
            if (string.IsNullOrWhiteSpace(destPath))
            {
                return (false, "目标文件路径为空");
            }
            
            if (!File.Exists(sourcePath))
            {
                return (false, $"源文件不存在: {sourcePath}");
            }
            
            // 确保目标目录存在
            var destDirectory = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDirectory))
            {
                if (!SafeDirectoryOperations.TryCreateDirectory(destDirectory, out var dirError))
                {
                    return (false, $"无法创建目标目录: {dirError}");
                }
            }
            
            // 如果目标文件存在且需要覆盖，先删除
            if (overwrite && File.Exists(destPath))
            {
                var (deleteSuccess, deleteError) = DeleteFile(destPath);
                if (!deleteSuccess)
                {
                    return (false, $"无法删除目标文件: {deleteError}");
                }
            }
            
            File.Move(sourcePath, destPath);
            return (true, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[SafeFile] No permission to move {sourcePath} to {destPath}: {ex.Message}");
            return (false, $"无权限移动文件");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[SafeFile] IO error moving {sourcePath} to {destPath}: {ex.Message}");
            return (false, $"移动文件失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFile] Failed to move {sourcePath} to {destPath}: {ex.Message}");
            return (false, $"移动文件时发生错误: {ex.Message}");
        }
    }
}
