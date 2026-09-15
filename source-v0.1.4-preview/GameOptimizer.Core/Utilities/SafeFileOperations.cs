// File: GameOptimizer.Core/Utilities/SafeFileOperations.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Utilities;

public static class SafeFileOperations
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(100);

    public static string GenerateUniqueTempFileName(string baseName)
    {
        var processId = Environment.ProcessId;
        var guid = Guid.NewGuid().ToString("N")[..8];
        var extension = Path.GetExtension(baseName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(baseName);
        return $"{nameWithoutExt}_{processId}_{guid}{extension}";
    }

    public static async Task<T?> ReadJsonWithRetryAsync<T>(string filePath, CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return default;
                }

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (attempt < MaxRetries - 1)
            {
                Debug.WriteLine($"[SafeFileOps] Read attempt {attempt + 1} failed: {ex.Message}");
                await Task.Delay(InitialDelay * (int)Math.Pow(2, attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        return default;
    }

    public static async Task WriteJsonWithRetryAsync<T>(string filePath, T data, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            if (!SafeDirectoryOperations.TryCreateDirectory(directory, out var error))
            {
                throw new IOException($"无法创建目录 {directory}: {error}");
            }
        }

        var tempFileName = GenerateUniqueTempFileName(Path.GetFileName(filePath));
        var tempFilePath = Path.Combine(directory ?? ".", tempFileName);

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, data, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
                }

                // Keep the previous durable value until the replacement rename succeeds.
                // Deleting first creates a data-loss window when the rename is rejected
                // (for example by permissions, antivirus or a concurrent reader).
                File.Move(tempFilePath, filePath, overwrite: true);
                return;
            }
            catch (IOException ex) when (attempt < MaxRetries - 1)
            {
                Debug.WriteLine($"[SafeFileOps] Write attempt {attempt + 1} failed: {ex.Message}");
                
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch (Exception cleanupEx) { Debug.WriteLine($"[SafeFileOps] Temp cleanup failed: {cleanupEx.Message}"); }

                await Task.Delay(InitialDelay * (int)Math.Pow(2, attempt), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch (Exception cleanupEx) { Debug.WriteLine($"[SafeFileOps] Temp cleanup failed: {cleanupEx.Message}"); }
                throw;
            }
        }

        throw new IOException($"无法写入文件 {filePath}，已重试 {MaxRetries} 次");
    }

    public static async Task WriteTextWithRetryAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            if (!SafeDirectoryOperations.TryCreateDirectory(directory, out var error))
            {
                throw new IOException($"无法创建目录 {directory}: {error}");
            }
        }

        var tempFileName = GenerateUniqueTempFileName(Path.GetFileName(filePath));
        var tempFilePath = Path.Combine(directory ?? ".", tempFileName);

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                await File.WriteAllTextAsync(tempFilePath, content, cancellationToken).ConfigureAwait(false);

                // Same-directory overwrite is a single replacement boundary: a failed
                // rename leaves the existing destination intact for recovery/retry.
                File.Move(tempFilePath, filePath, overwrite: true);
                return;
            }
            catch (IOException ex) when (attempt < MaxRetries - 1)
            {
                Debug.WriteLine($"[SafeFileOps] WriteText attempt {attempt + 1} failed: {ex.Message}");
                
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch (Exception cleanupEx) { Debug.WriteLine($"[SafeFileOps] Temp cleanup failed: {cleanupEx.Message}"); }

                await Task.Delay(InitialDelay * (int)Math.Pow(2, attempt), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch (Exception cleanupEx) { Debug.WriteLine($"[SafeFileOps] Temp cleanup failed: {cleanupEx.Message}"); }
                throw;
            }
        }

        throw new IOException($"无法写入文件 {filePath}，已重试 {MaxRetries} 次");
    }

    public static async Task<bool> TryDeleteWithRetryAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return true;
        }

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch (IOException ex) when (attempt < MaxRetries - 1)
            {
                Debug.WriteLine($"[SafeFileOps] Delete attempt {attempt + 1} failed: {ex.Message}");
                await Task.Delay(InitialDelay * (int)Math.Pow(2, attempt), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
