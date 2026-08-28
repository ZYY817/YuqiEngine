// File: GameOptimizer.Core/Utilities/SafeDirectoryOperations.cs
using System;
using System.IO;
using System.Security;

namespace GameOptimizer.Core.Utilities;

/// <summary>
/// Safe directory operations with comprehensive error handling
/// </summary>
public static class SafeDirectoryOperations
{
    /// <summary>
    /// Safely create a directory with full error handling
    /// </summary>
    public static bool TryCreateDirectory(string path, out string? errorMessage)
    {
        errorMessage = null;
        
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Directory path cannot be empty";
            return false;
        }

        try
        {
            if (Directory.Exists(path))
            {
                return true;
            }

            if (!ValidatePath(path, out var pathError))
            {
                errorMessage = pathError;
                return false;
            }

            if (!CheckWritePermission(Path.GetDirectoryName(path) ?? path, out var permError))
            {
                errorMessage = $"Insufficient permissions: {permError}";
                return false;
            }

            Directory.CreateDirectory(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = $"No permission to create directory: {path}\nRun as administrator or choose another location";
            return false;
        }
        catch (PathTooLongException)
        {
            errorMessage = $"Path too long ({path.Length} chars)\nChoose a shorter path";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            errorMessage = $"Parent directory not found: {Path.GetDirectoryName(path)}\nCheck if the path is correct";
            return false;
        }
        catch (IOException ex)
        {
            errorMessage = $"Cannot create directory: {ex.Message}\nPossible cause: disk full, filesystem error, or path in use";
            return false;
        }
        catch (NotSupportedException)
        {
            errorMessage = $"Invalid path format: {path}\nCheck for illegal characters";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Unknown error creating directory: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 检查指定路径的写权限
    /// </summary>
    public static bool CheckWritePermission(string path, out string? errorMessage)
    {
        errorMessage = null;

        if (!Directory.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
            {
                errorMessage = "Cannot determine parent directory";
                return false;
            }
            return CheckWritePermission(parent, out errorMessage);
        }

        var testFile = Path.Combine(path, $".write_test_{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = "No write permission";
            return false;
        }
        catch (IOException ex)
        {
            errorMessage = $"IO 错误: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Permission check failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 检查磁盘空间是否充足
    /// </summary>
    public static bool CheckDiskSpace(string path, long requiredMB, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\";
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                errorMessage = $"Drive {root} not ready";
                return false;
            }

            var availableMB = drive.AvailableFreeSpace / 1024 / 1024;
            if (availableMB < requiredMB)
            {
                errorMessage = $"Insufficient disk space\nRequired: {requiredMB} MB\nAvailable: {availableMB} MB\nFree up disk space or choose another location";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Disk space check failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 验证路径的合法性
    /// </summary>
    public static bool ValidatePath(string path, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Path cannot be empty";
            return false;
        }

        if (path.Length > 248)
        {
            errorMessage = $"Path too long ({path.Length} chars, max 248)\nWindows limits directory paths to 248 characters";
            return false;
        }

        try
        {
            var invalidChars = Path.GetInvalidPathChars();
            foreach (var c in path)
            {
                if (Array.IndexOf(invalidChars, c) >= 0)
                {
                    errorMessage = $"Path contains illegal character: '{c}'";
                    return false;
                }
            }

            Path.GetFullPath(path);
            return true;
        }
        catch (ArgumentException ex)
        {
            errorMessage = $"Invalid path format: {ex.Message}";
            return false;
        }
        catch (SecurityException)
        {
            errorMessage = "No permission to access this path";
            return false;
        }
        catch (NotSupportedException)
        {
            errorMessage = "Path format not supported";
            return false;
        }
        catch (PathTooLongException)
        {
            errorMessage = $"Path too long ({path.Length} chars)";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Path validation failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 确保目录存在，如果不存在则创建（带完整错误处理）
    /// </summary>
    public static (bool Success, string? Error) EnsureDirectoryExists(string path)
    {
        if (Directory.Exists(path))
        {
            return (true, null);
        }

        if (!TryCreateDirectory(path, out var error))
        {
            return (false, error);
        }

        return (true, null);
    }

    /// <summary>
    /// 批量确保多个目录存在
    /// </summary>
    public static (bool Success, string[] Errors) EnsureDirectoriesExist(params string[] paths)
    {
        var errors = new System.Collections.Generic.List<string>();

        foreach (var path in paths)
        {
            var (success, error) = EnsureDirectoryExists(path);
            if (!success && !string.IsNullOrEmpty(error))
            {
                errors.Add($"{path}: {error}");
            }
        }

        return (errors.Count == 0, errors.ToArray());
    }
}
