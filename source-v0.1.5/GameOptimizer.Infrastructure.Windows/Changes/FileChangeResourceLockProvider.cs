using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Changes;

public sealed class FileChangeResourceLockProvider : IChangeResourceLockProvider
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly string _lockDirectory;

    public FileChangeResourceLockProvider()
        : this(Path.Combine(AppPaths.StateDataRoot, "Changes", "locks"))
    {
    }

    internal FileChangeResourceLockProvider(string lockDirectory)
    {
        _lockDirectory = lockDirectory;
    }

    public async Task<IChangeResourceLease?> TryAcquireAsync(
        string resourceKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        Directory.CreateDirectory(_lockDirectory);
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey))) + ".lock";
        var path = Path.Combine(_lockDirectory, fileName);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
                return new FileLease(stream);
            }
            catch (IOException) when (stopwatch.Elapsed < timeout)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private sealed class FileLease : IChangeResourceLease
    {
        private FileStream? _stream;

        public FileLease(FileStream stream)
        {
            _stream = stream;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _stream, null)?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
