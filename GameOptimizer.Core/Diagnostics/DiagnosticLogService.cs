using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Diagnostics;

public sealed class DiagnosticLogService : IDiagnosticLogService, IDisposable
{
    private const int MaxEntries = 120;
    private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10MB
    private readonly ConcurrentQueue<DiagnosticLogEntry> _entries = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _logFilePath;
    private bool _disposed;

    public DiagnosticLogService() : this(null)
    {
    }

    public DiagnosticLogService(string? configuredLogDirectory)
    {
        LogDirectory = ResolveWritableLogDirectory(configuredLogDirectory);

        if (!Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(LogDirectory, out var dirError))
        {
            System.Diagnostics.Debug.WriteLine($"[DiagnosticLog] Failed to create log directory: {dirError}");
        }
        
        var stamp = DateTime.Now.ToString("yyyyMMdd");
        _logFilePath = Path.Combine(LogDirectory, $"optimizer-{stamp}.log");
        if (!File.Exists(_logFilePath))
        {
            // P2-004: 使用 SafeFileWriter 创建初始日志文件
            var success = Core.Services.SafeFileWriter.TryWriteAllText(
                _logFilePath, 
                $"# GameOptimizer diagnostics created {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
            if (!success)
            {
                System.Diagnostics.Debug.WriteLine($"[DiagnosticLog] Failed to create log file");
            }
        }
    }

    private static string ResolveWritableLogDirectory(string? configuredLogDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredLogDirectory))
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(configuredLogDirectory.Trim());
                if (!expanded.StartsWith(@"\\", StringComparison.Ordinal) &&
                    !expanded.StartsWith("//", StringComparison.Ordinal))
                {
                    var candidate = Path.GetFullPath(expanded);
                    if (Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(candidate, out _))
                    {
                        var probePath = Path.Combine(candidate, $".yuqi-log-probe-{Guid.NewGuid():N}.tmp");
                        try
                        {
                            using (new FileStream(
                                       probePath,
                                       FileMode.CreateNew,
                                       FileAccess.Write,
                                       FileShare.None,
                                       1,
                                       FileOptions.DeleteOnClose))
                            {
                            }
                            return candidate;
                        }
                        catch
                        {
                            Core.Services.SafeFileHelper.DeleteFile(probePath);
                        }
                    }
                }
            }
            catch
            {
                // Invalid or unavailable user paths fall back to the application data log directory.
            }
        }

        return Constants.AppPaths.LogsDir;
    }

    public event EventHandler<DiagnosticLogEntry>? EntryPublished;

    public string LogDirectory { get; }

    public void Append(LogLevel level, string source, string message, string? detail = null)
    {
        if (_disposed)
        {
            return;
        }

        var entry = new DiagnosticLogEntry(DateTime.Now, level, source, message, detail);
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
        }

        _ = WriteAsync(entry).ContinueWith(t => 
        {
            if (t.IsFaulted && t.Exception != null)
                System.Diagnostics.Debug.WriteLine($"[DiagnosticLog] WriteAsync failed: {t.Exception.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
        EntryPublished?.Invoke(this, entry);
    }

    public IReadOnlyList<DiagnosticLogEntry> GetRecentEntries(int count = 32)
    {
        return _entries.Reverse().Take(count).ToList();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _writeLock.Release();
    }

    private async Task WriteAsync(DiagnosticLogEntry entry)
    {
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            
            // 检查日志文件大小，超过限制则轮转
            await CheckAndRotateLogFileAsync().ConfigureAwait(false);
            
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Level,-11} {entry.Source} :: {entry.Message}";
            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                line += $" | {entry.Detail}";
            }
            // 使用 UTF8 编码写入，防止中文乱码
            await File.AppendAllTextAsync(_logFilePath, line + Environment.NewLine, System.Text.Encoding.UTF8).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiagnosticLog] Write failed: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    private async Task CheckAndRotateLogFileAsync()
    {
        try
        {
            if (!File.Exists(_logFilePath)) return;
            
            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length < MaxLogFileSizeBytes) return;
            
            // P2-004: 使用 SafeFileHelper 轮转日志文件
            var rotatedPath = Path.Combine(LogDirectory, 
                $"optimizer-{DateTime.Now:yyyyMMdd-HHmmss}.log.old");
            var (moveSuccess, moveError) = Core.Services.SafeFileHelper.MoveFile(_logFilePath, rotatedPath);
            if (!moveSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"[DiagnosticLog] Failed to rotate log file: {moveError}");
                return;
            }
            
            // P2-004: 使用 SafeFileHelper 创建新日志文件
            var (writeSuccess, writeError) = await Core.Services.SafeFileHelper.WriteAllTextAsync(_logFilePath, 
                $"# GameOptimizer diagnostics rotated {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}")
                .ConfigureAwait(false);
            if (!writeSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"[DiagnosticLog] Failed to create new log file: {writeError}");
            }
            
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiagnosticLog] Rotate failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();
    }
}
