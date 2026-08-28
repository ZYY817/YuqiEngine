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
        _root = Path.GetFullPath(root ?? Path.Combine(AppPaths.AppDataRoot, "CleanupQuarantine"));
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

        await CleanupQuarantineManifestStore.EnsureEntryStillMatchesAsync(entry, cancellationToken).ConfigureAwait(false);
        await using CleanupQuarantineResourceLease? lease = await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(
            _root,
            entry.ResourceKey,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            throw new IOException("cleanup.resource-lease-unavailable");
        }

        await CleanupQuarantineManifestStore.EnsureEntryStillMatchesAsync(entry, cancellationToken).ConfigureAwait(false);
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
    public static async Task<CleanupPreview> BuildAsync(CleanupItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var entries = new List<CleanupPreviewEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metrics = new CleanupPreviewScanMetrics();

        foreach (string configuredRoot in item.TargetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CleanupPathPolicy.TryValidateRoot(configuredRoot, out string root, out _))
            {
                continue;
            }
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in EnumerateFiles(root, item.Patterns, item.IncludeSubdirectories, metrics, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CleanupPathPolicy.TryValidateCandidate(root, file, out string candidate, out _) || !seen.Add(candidate))
                {
                    continue;
                }
                try
                {
                    FileInfo info = new(candidate);
                    if (!info.Exists) continue;
                    string hash = await CleanupQuarantineManifestStore.ComputeFileSha256Async(candidate, cancellationToken).ConfigureAwait(false);
                    entries.Add(new(candidate, root, info.Length, info.LastWriteTimeUtc, hash));
                }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException) { metrics.SkippedFiles++; }
                catch (IOException) { metrics.SkippedFiles++; }
            }
        }

        return new CleanupPreview(
            item.Id,
            entries,
            DateTimeOffset.UtcNow,
            skippedDirectoryCount: metrics.SkippedDirectories,
            skippedFileCount: metrics.SkippedFiles);
    }

    private static IEnumerable<string> EnumerateFiles(
        string root,
        string[] patterns,
        bool recursive,
        CleanupPreviewScanMetrics metrics,
        CancellationToken cancellationToken)
    {
        string[] effectivePatterns = patterns.Length == 0 ? ["*"] : patterns;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
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

            foreach (string file in files)
            {
                if (effectivePatterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(file), ignoreCase: true)))
                {
                    yield return file;
                }
            }

            if (!recursive)
            {
                continue;
            }
            string[] children;
            try
            {
                children = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly);
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

            foreach (string child in children)
            {
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

    private sealed class CleanupPreviewScanMetrics
    {
        public int SkippedDirectories { get; set; }
        public int SkippedFiles { get; set; }
    }
}
