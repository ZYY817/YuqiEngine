using System.IO.Enumeration;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services.SystemCleanup;

public sealed class DenyCleanupAuthorizationGate : ICleanupAuthorizationGate
{
    public Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CleanupAuthorizationDecision(
            false,
            "cleanup.baseline-gate-not-connected",
            "Cleanup writes remain blocked until the verified first-baseline gate is connected by the main composition root."));
    }
}

public static class CleanupPathPolicy
{
    public static bool TryValidateRoot(string path, out string canonicalRoot, out string reason)
    {
        canonicalRoot = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "cleanup.path-empty";
            return false;
        }

        try
        {
            canonicalRoot = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? volumeRoot = Path.GetPathRoot(canonicalRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(canonicalRoot) || string.Equals(canonicalRoot, volumeRoot, StringComparison.OrdinalIgnoreCase))
            {
                reason = "cleanup.root-forbidden";
                return false;
            }

            string? ancestor = canonicalRoot;
            while (!string.IsNullOrEmpty(ancestor))
            {
                if (Directory.Exists(ancestor) && IsReparsePoint(ancestor))
                {
                    reason = "cleanup.root-reparse-point";
                    return false;
                }
                string? parent = Path.GetDirectoryName(ancestor);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, ancestor, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                ancestor = parent;
            }

            return true;
        }
        catch (Exception)
        {
            reason = "cleanup.root-invalid";
            canonicalRoot = string.Empty;
            return false;
        }
    }

    public static bool TryValidateCandidate(
        string canonicalRoot,
        string candidate,
        out string canonicalCandidate,
        out string reason,
        Func<string, FileAttributes>? attributes = null)
    {
        canonicalCandidate = string.Empty;
        reason = string.Empty;
        try
        {
            canonicalCandidate = Path.GetFullPath(candidate);
            string prefix = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!canonicalCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = "cleanup.path-outside-allowlist";
                return false;
            }

            attributes ??= File.GetAttributes;
            string current = canonicalCandidate;
            while (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if ((attributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    reason = "cleanup.path-reparse-point";
                    return false;
                }
                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, canonicalRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = parent;
            }
            return true;
        }
        catch (Exception)
        {
            reason = "cleanup.path-invalid";
            canonicalCandidate = string.Empty;
            return false;
        }
    }

    public static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

public interface ICleanupQuarantine
{
    Task<string> MoveAsync(CleanupPreviewEntry entry, string itemId, CancellationToken cancellationToken);

    async Task<CleanupQuarantineMove> MoveWithReceiptAsync(
        CleanupPreviewEntry entry,
        string itemId,
        string operationId,
        CancellationToken cancellationToken)
    {
        string quarantinePath = await MoveAsync(entry, itemId, cancellationToken).ConfigureAwait(false);
        return new CleanupQuarantineMove(quarantinePath, operationId, null);
    }
}

public sealed record CleanupQuarantineMove(string QuarantinePath, string OperationId, string? ManifestPath);

public sealed class CleanupQuarantinePersistenceException : IOException
{
    public CleanupQuarantinePersistenceException(string reasonCode, string message, bool mutationOccurred, Exception? innerException = null)
        : base(message, innerException)
    {
        ReasonCode = reasonCode;
        MutationOccurred = mutationOccurred;
    }

    public string ReasonCode { get; }
    public bool MutationOccurred { get; }
}

public sealed class FileSystemCleanupQuarantine : ICleanupQuarantine
{
    private readonly string _root;

    public FileSystemCleanupQuarantine(string? root = null)
    {
        // Why: 隔离区属于恢复关键状态，必须与 CleanupQuarantineManifestStore.DefaultRoot
        // 同根（StateDataRoot/系统盘）。此前默认落在 AppDataRoot（可选数据根，如 F:\），
        // 当两根植不一致时，写入端隔离的文件对恢复端不可见——恢复预览永远为空、
        // 文件被困在旧根里。统一指向 StateDataRoot 后，写入与恢复同根互见。
        _root = Path.GetFullPath(root ?? Path.Combine(AppPaths.StateDataRoot, "CleanupQuarantine"));
    }

    public async Task<string> MoveAsync(CleanupPreviewEntry entry, string itemId, CancellationToken cancellationToken)
    {
        CleanupQuarantineMove move = await MoveWithReceiptAsync(
            entry,
            itemId,
            Guid.NewGuid().ToString("N"),
            cancellationToken).ConfigureAwait(false);
        return move.QuarantinePath;
    }

    public async Task<CleanupQuarantineMove> MoveWithReceiptAsync(
        CleanupPreviewEntry entry,
        string itemId,
        string operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("A cleanup item identifier is required.", nameof(itemId));
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("A durable cleanup operation identifier is required.", nameof(operationId));

        // Validate the quarantine root before the lease/manifest layer can create any
        // files below it. An elevated process must never follow a pre-planted junction
        // and create its bookkeeping data outside the configured quarantine boundary.
        if (!CleanupPathPolicy.TryValidateRoot(_root, out string quarantineRoot, out string rootReason))
        {
            throw new IOException(rootReason);
        }

        await CleanupQuarantineManifestStore.EnsureEntryStillMatchesAsync(entry, cancellationToken, requireContentHash: false).ConfigureAwait(false);
        await using CleanupQuarantineResourceLease? lease = await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(
            _root,
            entry.ResourceKey,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            throw new IOException("cleanup.resource-lease-unavailable");
        }

        await CleanupQuarantineManifestStore.EnsureEntryStillMatchesAsync(entry, cancellationToken, requireContentHash: false).ConfigureAwait(false);
        string batch = Path.Combine(_root, DateTime.UtcNow.ToString("yyyyMMdd"), operationId);
        Directory.CreateDirectory(batch);
        string batchReason = string.Empty;
        string canonicalBatch = string.Empty;
        bool batchAllowed = CleanupPathPolicy.TryValidateCandidate(
            quarantineRoot,
            batch,
            out canonicalBatch,
            out batchReason);
        if (!batchAllowed)
        {
            throw new IOException(batchReason);
        }

        string fileName = Path.GetFileName(entry.CanonicalPath);
        string suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(entry.ResourceKey)))[..16];
        string destination = Path.Combine(canonicalBatch, suffix + "-" + fileName);
        if (!CleanupQuarantineManifestStore.AreOnSameVolume(entry.CanonicalPath, destination))
        {
            throw new IOException("cleanup.cross-volume-quarantine-blocked");
        }

        return await CleanupQuarantineManifestStore.MoveIntoQuarantineAsync(
            entry,
            itemId,
            destination,
            operationId,
            cancellationToken).ConfigureAwait(false);
    }
}

internal static class CleanupPreviewBuilder
{
    // Preview is an interaction, not a background inventory job. These bounds
    // keep a single cache (especially Windows Update) from holding the whole UI
    // hostage. Only files actually found before the bound are returned, so an
    // incomplete scan cannot expand the cleanup scope.
    private const int DefaultFileLimit = 25_000;
    private const int UpdateCacheFileLimit = 12_000;
    private static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan UpdateCacheTimeBudget = TimeSpan.FromSeconds(3);

    public static async Task<CleanupPreview> BuildAsync(
        CleanupItem item,
        IProgress<CleanupScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var entries = new List<CleanupPreviewEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metrics = new CleanupPreviewScanMetrics();
        var budget = CleanupPreviewScanBudget.For(item);
        ReportProgress(item, entries, metrics, budget, progress, force: true);

        foreach (string configuredRoot in item.TargetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.CanContinue())
            {
                metrics.MarkPartial(budget.Reason);
                break;
            }
            if (!CleanupPathPolicy.TryValidateRoot(configuredRoot, out string root, out _))
            {
                continue;
            }
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in EnumerateFiles(root, item.Patterns, item.IncludeSubdirectories, metrics, budget, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!budget.CanContinue())
                {
                    metrics.MarkPartial(budget.Reason);
                    break;
                }
                if (!CleanupPathPolicy.TryValidateCandidate(root, file, out string candidate, out _) || !seen.Add(candidate))
                {
                    continue;
                }
                try
                {
                    FileInfo info = new(candidate);
                    if (!info.Exists) continue;
                    // Capture content hash during preview so that authorization-time
                    // drift (same size / same timestamp but different bytes) can be
                    // detected and blocked before quarantine.
                    string hash = await CleanupQuarantineManifestStore.ComputeFileSha256Async(candidate, cancellationToken).ConfigureAwait(false);
                    entries.Add(new(candidate, root, info.Length, info.LastWriteTimeUtc, hash));
                    budget.RecordFile();
                    ReportProgress(item, entries, metrics, budget, progress, force: false);
                }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException) { metrics.SkippedFiles++; }
                catch (IOException) { metrics.SkippedFiles++; }
            }

            if (metrics.IsPartial)
            {
                break;
            }
        }

        ReportProgress(item, entries, metrics, budget, progress, force: true);

        return new CleanupPreview(
            item.Id,
            entries,
            DateTimeOffset.UtcNow,
            skippedDirectoryCount: metrics.SkippedDirectories,
            skippedFileCount: metrics.SkippedFiles,
            isPartial: metrics.IsPartial,
            partialReason: metrics.PartialReason);
    }

    private static IEnumerable<string> EnumerateFiles(
        string root,
        string[] patterns,
        bool recursive,
        CleanupPreviewScanMetrics metrics,
        CleanupPreviewScanBudget budget,
        CancellationToken cancellationToken)
    {
        string[] effectivePatterns = patterns.Length == 0 ? ["*"] : patterns;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.CanContinue())
            {
                metrics.MarkPartial(budget.Reason);
                yield break;
            }
            string directory = pending.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                metrics.SkippedDirectories++;
                continue;
            }
            catch (IOException)
            {
                metrics.SkippedDirectories++;
                continue;
            }

            IEnumerator<string> fileEnumerator;
            try
            {
                fileEnumerator = files.GetEnumerator();
            }
            catch (UnauthorizedAccessException)
            {
                metrics.SkippedDirectories++;
                continue;
            }
            catch (IOException)
            {
                metrics.SkippedDirectories++;
                continue;
            }

            using (fileEnumerator)
            {
                while (TryMoveNext(fileEnumerator, metrics))
                {
                    string file = fileEnumerator.Current;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!budget.CanContinue())
                    {
                        metrics.MarkPartial(budget.Reason);
                        yield break;
                    }
                    if (effectivePatterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(file), ignoreCase: true)))
                    {
                        yield return file;
                    }
                }
            }

            if (!recursive)
            {
                continue;
            }
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                metrics.SkippedDirectories++;
                continue;
            }
            catch (IOException)
            {
                metrics.SkippedDirectories++;
                continue;
            }

            IEnumerator<string> childEnumerator;
            try
            {
                childEnumerator = children.GetEnumerator();
            }
            catch (UnauthorizedAccessException)
            {
                metrics.SkippedDirectories++;
                continue;
            }
            catch (IOException)
            {
                metrics.SkippedDirectories++;
                continue;
            }

            using (childEnumerator)
            {
                while (TryMoveNext(childEnumerator, metrics))
                {
                    string child = childEnumerator.Current;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!budget.CanContinue())
                    {
                        metrics.MarkPartial(budget.Reason);
                        yield break;
                    }
                    try
                    {
                        if (!CleanupPathPolicy.IsReparsePoint(child))
                        {
                            pending.Push(child);
                        }
                    }
                    catch (UnauthorizedAccessException) { metrics.SkippedDirectories++; }
                    catch (IOException) { metrics.SkippedDirectories++; }
                }
            }
        }
    }

    private static bool TryMoveNext(IEnumerator<string> enumerator, CleanupPreviewScanMetrics metrics)
    {
        try
        {
            return enumerator.MoveNext();
        }
        catch (UnauthorizedAccessException)
        {
            metrics.SkippedDirectories++;
            return false;
        }
        catch (IOException)
        {
            metrics.SkippedDirectories++;
            return false;
        }
    }

    private static void ReportProgress(
        CleanupItem item,
        IReadOnlyCollection<CleanupPreviewEntry> entries,
        CleanupPreviewScanMetrics metrics,
        CleanupPreviewScanBudget budget,
        IProgress<CleanupScanProgress>? progress,
        bool force)
    {
        if (progress is null || (!force && !budget.ShouldReport()))
        {
            return;
        }

        progress.Report(new CleanupScanProgress(
            item.Id,
            entries.Count,
            entries.Sum(entry => entry.SizeBytes),
            metrics.SkippedDirectories,
            metrics.SkippedFiles,
            budget.Elapsed));
    }

    private sealed class CleanupPreviewScanMetrics
    {
        public int SkippedDirectories { get; set; }
        public int SkippedFiles { get; set; }
        public bool IsPartial { get; private set; }
        public string PartialReason { get; private set; } = string.Empty;

        public void MarkPartial(string reason)
        {
            IsPartial = true;
            PartialReason = reason;
        }
    }

    private sealed class CleanupPreviewScanBudget
    {
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private readonly TimeSpan _timeBudget;
        private readonly int _fileLimit;
        private TimeSpan _lastReported;
        private int _filesObserved;

        private CleanupPreviewScanBudget(TimeSpan timeBudget, int fileLimit)
        {
            _timeBudget = timeBudget;
            _fileLimit = fileLimit;
        }

        public TimeSpan Elapsed => _stopwatch.Elapsed;
        public string Reason => _filesObserved >= _fileLimit
            ? $"为避免长时间等待，已在 {_fileLimit:N0} 个文件处停止继续扫描。"
            : $"为避免长时间等待，已在 {_timeBudget.TotalSeconds:N0} 秒后停止继续扫描。";

        public static CleanupPreviewScanBudget For(CleanupItem item) =>
            string.Equals(item.Id, "update_cache", StringComparison.OrdinalIgnoreCase)
                ? new(UpdateCacheTimeBudget, UpdateCacheFileLimit)
                : new(DefaultTimeBudget, DefaultFileLimit);

        public bool CanContinue()
        {
            return _filesObserved < _fileLimit && _stopwatch.Elapsed <= _timeBudget;
        }

        public void RecordFile() => _filesObserved++;

        public bool ShouldReport()
        {
            if (_stopwatch.Elapsed - _lastReported < TimeSpan.FromMilliseconds(250))
            {
                return false;
            }

            _lastReported = _stopwatch.Elapsed;
            return true;
        }
    }
}
