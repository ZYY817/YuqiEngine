using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services.SystemCleanup;

public enum CleanupQuarantineState
{
    MovePrepared,
    QuarantinedVerified,
    RestoreClaimed,
    RestoredVerified,
    RetentionClaimed,
    RetentionStaged,
    FailedNoChange,
    InterruptedManual
}

public sealed record CleanupQuarantineStepReceipt(
    Guid ReceiptId,
    CleanupQuarantineState State,
    DateTimeOffset RecordedAtUtc,
    string ReasonCode,
    string Message,
    bool MutationOccurred,
    Guid? ClaimId);

public sealed record CleanupQuarantineManifest(
    int Version,
    string OperationId,
    Guid PreviewId,
    string ItemId,
    string OriginalPath,
    string OriginalAllowlistedRoot,
    string QuarantinePath,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    string ContentSha256,
    string ResourceKey,
    CleanupQuarantineState State,
    Guid? ActiveClaimId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<CleanupQuarantineStepReceipt> Receipts,
    string ManifestSha256);

public sealed record CleanupQuarantineEntry(string ManifestPath, CleanupQuarantineManifest Manifest);

public sealed class CleanupQuarantinePreview
{
    public CleanupQuarantinePreview(string action, IEnumerable<CleanupQuarantineEntry> entries, DateTimeOffset createdAtUtc)
    {
        Action = action;
        Entries = Array.AsReadOnly(entries.ToArray());
        CreatedAtUtc = createdAtUtc;
        PreviewId = Guid.NewGuid();
        TotalBytes = Entries.Sum(entry => entry.Manifest.SizeBytes);
        Fingerprint = CleanupQuarantineManifestStore.ComputePreviewFingerprint(action, Entries);
    }

    public Guid PreviewId { get; }
    public string Action { get; }
    public IReadOnlyList<CleanupQuarantineEntry> Entries { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public long TotalBytes { get; }
    public string Fingerprint { get; }
}

public sealed record CleanupQuarantineConfirmation(
    Guid PreviewId,
    string Action,
    string Fingerprint,
    DateTimeOffset ConfirmedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public static CleanupQuarantineConfirmation Create(CleanupQuarantinePreview preview, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        return new(preview.PreviewId, preview.Action, preview.Fingerprint, now, now.Add(lifetime));
    }

    internal bool Matches(CleanupQuarantinePreview preview, DateTimeOffset now) =>
        PreviewId == preview.PreviewId &&
        string.Equals(Action, preview.Action, StringComparison.Ordinal) &&
        string.Equals(Fingerprint, preview.Fingerprint, StringComparison.Ordinal) &&
        ConfirmedAtUtc <= now && now <= ExpiresAtUtc;
}

public enum CleanupQuarantineOutcome
{
    Restored,
    StagedForRetention,
    ReconciledQuarantined,
    ReconciledRestored,
    ReconciledRetention,
    TargetConflict,
    InvalidManifest,
    ContentMismatch,
    BlockedPath,
    CrossVolumeBlocked,
    PermissionDenied,
    Locked,
    Cancelled,
    RecoveryRequired,
    Failed
}

public sealed record CleanupQuarantineItemResult(
    string OperationId,
    string OriginalPath,
    CleanupQuarantineOutcome Outcome,
    string ReasonCode,
    string Message,
    string? CurrentPath = null,
    string? ManifestPath = null);

public sealed class CleanupQuarantineResult
{
    public List<CleanupQuarantineItemResult> Items { get; } = [];
    public int Succeeded => Items.Count(item => item.Outcome is CleanupQuarantineOutcome.Restored or CleanupQuarantineOutcome.StagedForRetention);
    public int Failed => Items.Count - Succeeded;
}

public sealed class CleanupQuarantineReconciliationResult
{
    public List<CleanupQuarantineItemResult> Items { get; } = [];
    public int Reconciled => Items.Count(item => item.Outcome is CleanupQuarantineOutcome.ReconciledQuarantined or CleanupQuarantineOutcome.ReconciledRestored or CleanupQuarantineOutcome.ReconciledRetention);
    public int RequiresManualRecovery => Items.Count(item => item.Outcome == CleanupQuarantineOutcome.RecoveryRequired);
}

public interface ICleanupQuarantineManager
{
    Task<CleanupQuarantinePreview> CreateRestorePreviewAsync(CancellationToken cancellationToken = default);
    Task<CleanupQuarantineResult> RestoreAsync(CleanupQuarantinePreview preview, CleanupQuarantineConfirmation confirmation, CancellationToken cancellationToken = default);
    Task<CleanupQuarantinePreview> CreateRetentionPreviewAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default);
    Task<CleanupQuarantineResult> StageRetentionAsync(CleanupQuarantinePreview preview, CleanupQuarantineConfirmation confirmation, CancellationToken cancellationToken = default);
    Task<CleanupQuarantineReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default);
}

public sealed class CleanupQuarantineManager : ICleanupQuarantineManager
{
    private const string RestoreAction = "restore";
    private const string RetentionAction = "retention-stage";
    private readonly string _root;
    private readonly string _retentionRoot;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, CancellationToken, Task<string>> _contentHasher;
    private readonly ConcurrentDictionary<Guid, string> _issued = new();

    public CleanupQuarantineManager(string? root = null, TimeProvider? timeProvider = null)
        : this(root, timeProvider, CleanupQuarantineManifestStore.ComputeFileSha256Async)
    {
    }

    internal CleanupQuarantineManager(
        string? root,
        TimeProvider? timeProvider,
        Func<string, CancellationToken, Task<string>> contentHasher)
    {
        _root = Path.GetFullPath(root ?? CleanupQuarantineManifestStore.DefaultRoot);
        _retentionRoot = Path.Combine(_root, "RetentionHold");
        _timeProvider = timeProvider ?? TimeProvider.System;
        _contentHasher = contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));
    }

    public Task<CleanupQuarantinePreview> CreateRestorePreviewAsync(CancellationToken cancellationToken = default) =>
        CreatePreviewAsync(RestoreAction, manifest => manifest.State is CleanupQuarantineState.QuarantinedVerified or CleanupQuarantineState.RetentionStaged, includeRetention: true, cancellationToken);

    public Task<CleanupQuarantinePreview> CreateRetentionPreviewAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        if (minimumAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumAge));
        DateTimeOffset cutoff = _timeProvider.GetUtcNow().Subtract(minimumAge);
        return CreatePreviewAsync(
            RetentionAction,
            manifest => manifest.State == CleanupQuarantineState.QuarantinedVerified && manifest.CreatedAtUtc <= cutoff,
            includeRetention: false,
            cancellationToken);
    }

    public Task<CleanupQuarantineResult> RestoreAsync(
        CleanupQuarantinePreview preview,
        CleanupQuarantineConfirmation confirmation,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(preview, confirmation, restore: true, cancellationToken);

    public Task<CleanupQuarantineResult> StageRetentionAsync(
        CleanupQuarantinePreview preview,
        CleanupQuarantineConfirmation confirmation,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(preview, confirmation, restore: false, cancellationToken);

    public async Task<CleanupQuarantineReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var result = new CleanupQuarantineReconciliationResult();
        if (!Directory.Exists(_root)) return result;

        foreach (string manifestPath in CleanupQuarantineManifestStore.EnumerateManifestFiles(_root, excludedRoot: null, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CleanupQuarantineEntry? entry = await CleanupQuarantineManifestStore.TryReadVerifiedAsync(_root, manifestPath, cancellationToken).ConfigureAwait(false);
            if (entry is null) continue;
            CleanupQuarantineItemResult? item = await ReconcileEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            if (item is not null) result.Items.Add(item);
        }
        return result;
    }

    private async Task<CleanupQuarantinePreview> CreatePreviewAsync(
        string action,
        Func<CleanupQuarantineManifest, bool> predicate,
        bool includeRetention,
        CancellationToken cancellationToken)
    {
        await ReconcileAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<CleanupQuarantineEntry>();
        if (!Directory.Exists(_root)) return Issue(new CleanupQuarantinePreview(action, entries, _timeProvider.GetUtcNow()));

        foreach (string manifestPath in CleanupQuarantineManifestStore.EnumerateManifestFiles(
                     _root,
                     includeRetention ? null : _retentionRoot,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CleanupQuarantineEntry? entry = await CleanupQuarantineManifestStore.TryReadVerifiedAsync(_root, manifestPath, cancellationToken).ConfigureAwait(false);
            if (entry is not null && predicate(entry.Manifest) && File.Exists(entry.Manifest.QuarantinePath)) entries.Add(entry);
        }
        return Issue(new CleanupQuarantinePreview(action, entries, _timeProvider.GetUtcNow()));
    }

    private CleanupQuarantinePreview Issue(CleanupQuarantinePreview preview)
    {
        if (!_issued.TryAdd(preview.PreviewId, preview.Fingerprint)) throw new InvalidOperationException("Quarantine preview identity collision.");
        return preview;
    }

    private async Task<CleanupQuarantineResult> ExecuteAsync(
        CleanupQuarantinePreview preview,
        CleanupQuarantineConfirmation confirmation,
        bool restore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(confirmation);
        var result = new CleanupQuarantineResult();
        string expectedAction = restore ? RestoreAction : RetentionAction;
        if (!string.Equals(preview.Action, expectedAction, StringComparison.Ordinal) ||
            !confirmation.Matches(preview, _timeProvider.GetUtcNow()) ||
            !_issued.TryRemove(preview.PreviewId, out string? issued) ||
            !string.Equals(issued, preview.Fingerprint, StringComparison.Ordinal))
        {
            result.Items.Add(new("", "", CleanupQuarantineOutcome.InvalidManifest, "cleanup.quarantine-confirmation-invalid", "Preview confirmation is invalid, expired, forged, or replayed."));
            return result;
        }

        foreach (CleanupQuarantineEntry entry in preview.Entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.Items.Add(Result(entry, CleanupQuarantineOutcome.Cancelled, "cleanup.quarantine-cancelled", "Operation cancelled before this item."));
                break;
            }
            await ExecuteEntryAsync(entry, restore, result, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private async Task ExecuteEntryAsync(CleanupQuarantineEntry entry, bool restore, CleanupQuarantineResult result, CancellationToken cancellationToken)
    {
        CleanupQuarantineEntry? current = await CleanupQuarantineManifestStore.TryReadVerifiedAsync(_root, entry.ManifestPath, cancellationToken).ConfigureAwait(false);
        if (current is null || !string.Equals(current.Manifest.ManifestSha256, entry.Manifest.ManifestSha256, StringComparison.Ordinal))
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.InvalidManifest, "cleanup.quarantine-manifest-drift", "Manifest is invalid or changed after preview."));
            return;
        }

        await using CleanupQuarantineResourceLease? lease = await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(
            _root,
            current.Manifest.ResourceKey,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            result.Items.Add(Result(current, CleanupQuarantineOutcome.Locked, "cleanup.resource-lease-unavailable", "Another cleanup operation currently owns this resource."));
            return;
        }

        current = await CleanupQuarantineManifestStore.TryReadVerifiedAsync(_root, entry.ManifestPath, cancellationToken).ConfigureAwait(false);
        if (current is null || !string.Equals(current.Manifest.ManifestSha256, entry.Manifest.ManifestSha256, StringComparison.Ordinal))
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.InvalidManifest, "cleanup.quarantine-manifest-drift", "Manifest changed while waiting for its resource lease."));
            return;
        }

        try
        {
            string contentHash = await _contentHasher(current.Manifest.QuarantinePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(contentHash, current.Manifest.ContentSha256, StringComparison.Ordinal))
            {
                result.Items.Add(Result(current, CleanupQuarantineOutcome.ContentMismatch, "cleanup.quarantine-content-mismatch", "Quarantined content hash no longer matches the manifest."));
                return;
            }

            if (restore) await RestoreEntryAsync(current, result, cancellationToken).ConfigureAwait(false);
            else await StageRetentionEntryAsync(current, result, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result.Items.Add(Result(current, CleanupQuarantineOutcome.Cancelled, "cleanup.quarantine-cancelled", "Operation cancelled before mutation completed."));
        }
        catch (CleanupPreviewDriftException ex)
        {
            result.Items.Add(Result(current, CleanupQuarantineOutcome.ContentMismatch, ex.ReasonCode, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Items.Add(Result(current, CleanupQuarantineOutcome.PermissionDenied, "cleanup.quarantine-permission-denied", ex.Message));
        }
        catch (CleanupQuarantinePersistenceException ex)
        {
            result.Items.Add(Result(current, CleanupQuarantineOutcome.RecoveryRequired, ex.ReasonCode, ex.Message));
        }
        catch (IOException ex) when (ex.Message.Contains("cross-volume", StringComparison.Ordinal))
        {
            result.Items.Add(Result(current, CleanupQuarantineOutcome.CrossVolumeBlocked, "cleanup.cross-volume-blocked", ex.Message));
        }
        catch (IOException ex)
        {
            result.Items.Add(Result(current, CleanupQuarantineOutcome.Locked, "cleanup.quarantine-io", ex.Message));
        }
        catch (Exception ex)
        {
            result.Items.Add(Result(current, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.quarantine-state-unknown", ex.Message));
        }
    }

    private async Task RestoreEntryAsync(CleanupQuarantineEntry entry, CleanupQuarantineResult result, CancellationToken cancellationToken)
    {
        CleanupQuarantineManifest manifest = entry.Manifest;
        if (manifest.State is not (CleanupQuarantineState.QuarantinedVerified or CleanupQuarantineState.RetentionStaged))
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.InvalidManifest, "cleanup.restore-state-invalid", "Manifest is not in a restorable verified state."));
            return;
        }
        string rootReason = string.Empty;
        string pathReason = string.Empty;
        string root = string.Empty;
        string target = string.Empty;
        bool targetAllowed = CleanupPathPolicy.TryValidateRoot(manifest.OriginalAllowlistedRoot, out root, out rootReason) &&
            CleanupPathPolicy.TryValidateCandidate(root, manifest.OriginalPath, out target, out pathReason, SafeAttributes);
        if (!targetAllowed)
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.BlockedPath, rootReason.Length > 0 ? rootReason : pathReason, "Original target failed containment or reparse validation."));
            return;
        }
        if (File.Exists(target) || Directory.Exists(target))
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.TargetConflict, "cleanup.restore-target-conflict", "Restore target exists; overwrite is forbidden."));
            return;
        }
        if (!CleanupQuarantineManifestStore.AreOnSameVolume(manifest.QuarantinePath, target))
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.CrossVolumeBlocked, "cleanup.restore-cross-volume-blocked", "Cross-volume restore is blocked because it cannot preserve an atomic recoverable move."));
            return;
        }

        CleanupQuarantineManifest claimed = await CleanupQuarantineManifestStore.TransitionAsync(
            entry.ManifestPath,
            manifest,
            CleanupQuarantineState.RestoreClaimed,
            "cleanup.restore-claimed",
            "Restore resource lease and durable claim acquired.",
            mutationOccurred: false,
            claimId: Guid.NewGuid(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(claimed.QuarantinePath, target, overwrite: false);
        try
        {
            string restoredHash = await _contentHasher(target, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(restoredHash, claimed.ContentSha256, StringComparison.Ordinal))
            {
                throw new CleanupPreviewDriftException("cleanup.restore-content-mismatch", "Restore target hash differs from the quarantined manifest.");
            }
            await CleanupQuarantineManifestStore.TransitionAsync(
                entry.ManifestPath,
                claimed,
                CleanupQuarantineState.RestoredVerified,
                "cleanup.restored-verified",
                "File restored without overwrite and independently hash-verified.",
                mutationOccurred: true,
                claimId: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.Restored, "cleanup.restored", "File restored without overwrite and verified.", target));
        }
        catch (OperationCanceledException ex)
        {
            throw new CleanupQuarantinePersistenceException("cleanup.restore-cancelled-after-move", ex.Message, mutationOccurred: true, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CleanupQuarantinePersistenceException("cleanup.restore-receipt-failed", ex.Message, mutationOccurred: true, ex);
        }
    }

    private async Task StageRetentionEntryAsync(CleanupQuarantineEntry entry, CleanupQuarantineResult result, CancellationToken cancellationToken)
    {
        CleanupQuarantineManifest manifest = entry.Manifest;
        if (manifest.State != CleanupQuarantineState.QuarantinedVerified)
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.InvalidManifest, "cleanup.retention-state-invalid", "Only a verified quarantine entry can move to retention hold."));
            return;
        }
        string batchDirectory = Path.GetDirectoryName(entry.ManifestPath)!;
        string rootReason = string.Empty;
        string batchReason = string.Empty;
        string root = string.Empty;
        string batch = string.Empty;
        bool batchAllowed = CleanupPathPolicy.TryValidateRoot(_root, out root, out rootReason) &&
            CleanupPathPolicy.TryValidateCandidate(root, batchDirectory, out batch, out batchReason, SafeAttributes);
        if (!batchAllowed)
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.BlockedPath, rootReason.Length > 0 ? rootReason : batchReason, "Retention source failed containment validation."));
            return;
        }
        if (CleanupQuarantineManifestStore.IsPathUnder(_retentionRoot, batch))
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.TargetConflict, "cleanup.retention-already-staged", "The entry is already in second-level retention hold."));
            return;
        }
        Directory.CreateDirectory(_retentionRoot);
        string destination = Path.Combine(_retentionRoot, manifest.OperationId);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.TargetConflict, "cleanup.retention-target-conflict", "Retention hold target exists."));
            return;
        }
        if (!CleanupQuarantineManifestStore.AreOnSameVolume(batch, destination))
        {
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.CrossVolumeBlocked, "cleanup.retention-cross-volume-blocked", "Cross-volume retention staging is blocked."));
            return;
        }

        CleanupQuarantineManifest claimed = await CleanupQuarantineManifestStore.TransitionAsync(
            entry.ManifestPath,
            manifest,
            CleanupQuarantineState.RetentionClaimed,
            "cleanup.retention-claimed",
            "Retention resource lease and durable claim acquired.",
            mutationOccurred: false,
            claimId: Guid.NewGuid(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Directory.Move(batch, destination);
        string stagedManifestPath = Path.Combine(destination, Path.GetFileName(entry.ManifestPath));
        string stagedQuarantinePath = Path.Combine(destination, Path.GetFileName(claimed.QuarantinePath));
        try
        {
            string stagedHash = await _contentHasher(stagedQuarantinePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(stagedHash, claimed.ContentSha256, StringComparison.Ordinal))
            {
                throw new CleanupPreviewDriftException("cleanup.retention-content-mismatch", "Retention hold content hash differs from the quarantine manifest.");
            }
            CleanupQuarantineManifest staged = claimed with { QuarantinePath = stagedQuarantinePath };
            await CleanupQuarantineManifestStore.TransitionAsync(
                stagedManifestPath,
                staged,
                CleanupQuarantineState.RetentionStaged,
                "cleanup.retention-staged",
                "Moved to second-level retention hold; no permanent deletion occurred.",
                mutationOccurred: true,
                claimId: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            result.Items.Add(Result(entry, CleanupQuarantineOutcome.StagedForRetention, "cleanup.retention-staged", "Moved to second-level retention hold; no permanent deletion occurred.", destination, stagedManifestPath));
        }
        catch (OperationCanceledException ex)
        {
            throw new CleanupQuarantinePersistenceException("cleanup.retention-cancelled-after-move", ex.Message, mutationOccurred: true, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CleanupQuarantinePersistenceException("cleanup.retention-receipt-failed", ex.Message, mutationOccurred: true, ex);
        }
    }

    private async Task<CleanupQuarantineItemResult?> ReconcileEntryAsync(CleanupQuarantineEntry entry, CancellationToken cancellationToken)
    {
        CleanupQuarantineState state = entry.Manifest.State;
        if (state is CleanupQuarantineState.QuarantinedVerified or CleanupQuarantineState.RetentionStaged or CleanupQuarantineState.RestoredVerified or CleanupQuarantineState.FailedNoChange or CleanupQuarantineState.InterruptedManual)
        {
            return null;
        }

        await using CleanupQuarantineResourceLease? lease = await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(
            _root,
            entry.Manifest.ResourceKey,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        if (lease is null) return Result(entry, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-lease-unavailable", "Resource is currently leased; reconciliation remains pending.");

        CleanupQuarantineEntry? current = await CleanupQuarantineManifestStore.TryReadVerifiedAsync(_root, entry.ManifestPath, cancellationToken).ConfigureAwait(false);
        if (current is null) return Result(entry, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-manifest-invalid", "Manifest cannot be re-read for reconciliation.");

        try
        {
            return current.Manifest.State switch
            {
                CleanupQuarantineState.MovePrepared => await ReconcileMoveAsync(current, cancellationToken).ConfigureAwait(false),
                CleanupQuarantineState.RestoreClaimed => await ReconcileRestoreAsync(current, cancellationToken).ConfigureAwait(false),
                CleanupQuarantineState.RetentionClaimed => await ReconcileRetentionAsync(current, cancellationToken).ConfigureAwait(false),
                _ => null
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(current, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-failed", ex.Message);
        }
    }

    private async Task<CleanupQuarantineItemResult> ReconcileMoveAsync(CleanupQuarantineEntry entry, CancellationToken cancellationToken)
    {
        bool sourceExists = File.Exists(entry.Manifest.OriginalPath);
        bool quarantineExists = File.Exists(entry.Manifest.QuarantinePath);
        if (!sourceExists && quarantineExists && await HashMatchesAsync(entry.Manifest.QuarantinePath, entry.Manifest.ContentSha256, cancellationToken).ConfigureAwait(false))
        {
            await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, entry.Manifest, CleanupQuarantineState.QuarantinedVerified, "cleanup.reconcile-quarantined", "Restart reconciliation verified the quarantined file hash.", true, null, cancellationToken).ConfigureAwait(false);
            return Result(entry, CleanupQuarantineOutcome.ReconciledQuarantined, "cleanup.reconcile-quarantined", "Quarantine was verified after an interrupted receipt write.");
        }
        if (sourceExists && !quarantineExists)
        {
            await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, entry.Manifest, CleanupQuarantineState.FailedNoChange, "cleanup.reconcile-move-no-change", "Source remained in place; no cleanup move completed.", false, null, cancellationToken).ConfigureAwait(false);
            return Result(entry, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-move-no-change", "No move was completed; a new preview is required before retry.");
        }
        await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, entry.Manifest, CleanupQuarantineState.InterruptedManual, "cleanup.reconcile-move-ambiguous", "Both or neither source and quarantine paths exist after interruption.", true, null, cancellationToken).ConfigureAwait(false);
        return Result(entry, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-move-ambiguous", "Cleanup state is ambiguous and requires manual recovery.");
    }

    private async Task<CleanupQuarantineItemResult> ReconcileRestoreAsync(CleanupQuarantineEntry entry, CancellationToken cancellationToken)
    {
        bool originalExists = File.Exists(entry.Manifest.OriginalPath);
        bool quarantineExists = File.Exists(entry.Manifest.QuarantinePath);
        if (originalExists && !quarantineExists && await HashMatchesAsync(entry.Manifest.OriginalPath, entry.Manifest.ContentSha256, cancellationToken).ConfigureAwait(false))
        {
            await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, entry.Manifest, CleanupQuarantineState.RestoredVerified, "cleanup.reconcile-restored", "Restart reconciliation verified the restored target hash.", true, null, cancellationToken).ConfigureAwait(false);
            return Result(entry, CleanupQuarantineOutcome.ReconciledRestored, "cleanup.reconcile-restored", "Restore was verified after an interrupted receipt write.", entry.Manifest.OriginalPath);
        }
        if (!originalExists && quarantineExists && await HashMatchesAsync(entry.Manifest.QuarantinePath, entry.Manifest.ContentSha256, cancellationToken).ConfigureAwait(false))
        {
            await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, entry.Manifest, CleanupQuarantineState.QuarantinedVerified, "cleanup.reconcile-restore-no-change", "Quarantine source remained in place; restore did not complete.", false, null, cancellationToken).ConfigureAwait(false);
            return Result(entry, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-restore-no-change", "Restore did not complete; a new confirmed preview is required before retry.");
        }
        await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, entry.Manifest, CleanupQuarantineState.InterruptedManual, "cleanup.reconcile-restore-ambiguous", "Restore state is ambiguous after interruption.", true, null, cancellationToken).ConfigureAwait(false);
        return Result(entry, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-restore-ambiguous", "Restore state is ambiguous and requires manual recovery.");
    }

    private async Task<CleanupQuarantineItemResult> ReconcileRetentionAsync(CleanupQuarantineEntry entry, CancellationToken cancellationToken)
    {
        string manifestDirectory = Path.GetDirectoryName(entry.ManifestPath)!;
        string stagedPath = Path.Combine(manifestDirectory, Path.GetFileName(entry.Manifest.QuarantinePath));
        if (CleanupQuarantineManifestStore.IsPathUnder(_retentionRoot, manifestDirectory) &&
            File.Exists(stagedPath) &&
            await HashMatchesAsync(stagedPath, entry.Manifest.ContentSha256, cancellationToken).ConfigureAwait(false))
        {
            CleanupQuarantineManifest staged = entry.Manifest with { QuarantinePath = stagedPath };
            await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, staged, CleanupQuarantineState.RetentionStaged, "cleanup.reconcile-retention", "Restart reconciliation verified second-level retention hold.", true, null, cancellationToken).ConfigureAwait(false);
            return Result(entry, CleanupQuarantineOutcome.ReconciledRetention, "cleanup.reconcile-retention", "Retention staging was verified after an interrupted receipt write.", manifestDirectory);
        }
        if (File.Exists(entry.Manifest.QuarantinePath) && await HashMatchesAsync(entry.Manifest.QuarantinePath, entry.Manifest.ContentSha256, cancellationToken).ConfigureAwait(false))
        {
            await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, entry.Manifest, CleanupQuarantineState.QuarantinedVerified, "cleanup.reconcile-retention-no-change", "Quarantine source remained in place; retention staging did not complete.", false, null, cancellationToken).ConfigureAwait(false);
            return Result(entry, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-retention-no-change", "Retention staging did not complete; a new confirmed preview is required before retry.");
        }
        await CleanupQuarantineManifestStore.TransitionAsync(entry.ManifestPath, entry.Manifest, CleanupQuarantineState.InterruptedManual, "cleanup.reconcile-retention-ambiguous", "Retention state is ambiguous after interruption.", true, null, cancellationToken).ConfigureAwait(false);
        return Result(entry, CleanupQuarantineOutcome.RecoveryRequired, "cleanup.reconcile-retention-ambiguous", "Retention state is ambiguous and requires manual recovery.");
    }

    private async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken cancellationToken) =>
        string.Equals(await _contentHasher(path, cancellationToken).ConfigureAwait(false), expected, StringComparison.Ordinal);

    private static FileAttributes SafeAttributes(string path) =>
        File.Exists(path) || Directory.Exists(path) ? File.GetAttributes(path) : FileAttributes.Normal;

    private static CleanupQuarantineItemResult Result(CleanupQuarantineEntry entry, CleanupQuarantineOutcome outcome, string code, string message, string? path = null, string? manifestPath = null) =>
        new(entry.Manifest.OperationId, entry.Manifest.OriginalPath, outcome, code, message, path ?? entry.Manifest.QuarantinePath, manifestPath ?? entry.ManifestPath);
}

public sealed class CleanupPreviewDriftException : IOException
{
    public CleanupPreviewDriftException(string reasonCode, string message) : base(message) => ReasonCode = reasonCode;
    public string ReasonCode { get; }
}

internal sealed class CleanupQuarantineResourceLease : IAsyncDisposable
{
    private FileStream? _stream;

    public CleanupQuarantineResourceLease(FileStream stream) => _stream = stream;

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stream, null)?.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal static class CleanupQuarantineManifestStore
{
    internal const int CurrentVersion = 2;
    private static readonly TimeSpan ResourceLeaseRetryDelay = TimeSpan.FromMilliseconds(25);
    internal static string DefaultRoot => Path.Combine(AppPaths.AppDataRoot, "CleanupQuarantine");
    private const int MaximumManifestBytes = 128 * 1024;
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly JsonSerializerOptions WriteOptions = new(ReadOptions) { WriteIndented = true };

    internal static async Task<CleanupQuarantineMove> MoveIntoQuarantineAsync(
        CleanupPreviewEntry entry,
        string itemId,
        string destination,
        string operationId,
        CancellationToken cancellationToken)
    {
        await EnsureEntryStillMatchesAsync(entry, cancellationToken).ConfigureAwait(false);
        string manifestPath = destination + ".manifest.json";
        var manifest = CreateManifest(entry, itemId, destination, operationId, CleanupQuarantineState.MovePrepared, Guid.NewGuid(), "cleanup.move-prepared", "Durable quarantine move receipt written before source mutation.", mutationOccurred: false);
        await WriteManifestAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        try
        {
            File.Move(entry.CanonicalPath, destination, overwrite: false);
        }
        catch
        {
            await TransitionAsync(manifestPath, manifest, CleanupQuarantineState.FailedNoChange, "cleanup.move-failed-no-change", "Source move did not complete.", false, null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        try
        {
            string movedHash = await ComputeFileSha256Async(destination, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(movedHash, entry.ContentSha256, StringComparison.Ordinal))
            {
                throw new CleanupPreviewDriftException("cleanup.quarantine-content-mismatch", "Quarantine destination hash differs from the immutable preview.");
            }
            CleanupQuarantineManifest verified = await TransitionAsync(
                manifestPath,
                manifest,
                CleanupQuarantineState.QuarantinedVerified,
                "cleanup.quarantined-verified",
                "File moved to quarantine and independently hash-verified.",
                true,
                null,
                cancellationToken).ConfigureAwait(false);
            return new CleanupQuarantineMove(destination, operationId, manifestPath);
        }
        catch (OperationCanceledException ex)
        {
            throw new CleanupQuarantinePersistenceException("cleanup.quarantine-cancelled-after-move", ex.Message, mutationOccurred: true, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CleanupQuarantinePersistenceException("cleanup.quarantine-receipt-failed", ex.Message, mutationOccurred: true, ex);
        }
    }

    internal static async Task EnsureEntryStillMatchesAsync(CleanupPreviewEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.PreviewId == Guid.Empty || !CleanupResourceIdentity.IsSha256(entry.ContentSha256))
        {
            throw new CleanupPreviewDriftException("cleanup.preview-identity-invalid", "Cleanup preview has no complete immutable resource identity.");
        }
        string rootReason = string.Empty;
        string pathReason = string.Empty;
        string root = string.Empty;
        string path = string.Empty;
        bool pathAllowed = CleanupPathPolicy.TryValidateRoot(entry.AllowlistedRoot, out root, out rootReason) &&
            CleanupPathPolicy.TryValidateCandidate(root, entry.CanonicalPath, out path, out pathReason, SafeAttributes);
        if (!pathAllowed)
        {
            throw new CleanupPreviewDriftException(rootReason.Length > 0 ? rootReason : pathReason, "Cleanup resource failed containment or reparse validation.");
        }
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != entry.SizeBytes || info.LastWriteTimeUtc != entry.LastWriteTimeUtc)
        {
            throw new CleanupPreviewDriftException("cleanup.preview-drift", "File size or last-write time changed after preview.");
        }
        string hash = await ComputeFileSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(hash, entry.ContentSha256, StringComparison.Ordinal))
        {
            throw new CleanupPreviewDriftException("cleanup.preview-content-drift", "File content changed after preview.");
        }
    }

    internal static async Task<CleanupQuarantineEntry?> TryReadVerifiedAsync(string root, string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!CleanupPathPolicy.TryValidateRoot(root, out string canonicalRoot, out _) ||
                !CleanupPathPolicy.TryValidateCandidate(canonicalRoot, manifestPath, out string canonicalManifest, out _, SafeAttributes)) return null;
            FileInfo info = new(canonicalManifest);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumManifestBytes) return null;
            await using var stream = new FileStream(canonicalManifest, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            CleanupQuarantineManifest? manifest = await JsonSerializer.DeserializeAsync<CleanupQuarantineManifest>(stream, ReadOptions, cancellationToken).ConfigureAwait(false);
            if (!IsValidManifest(manifest) ||
                !string.Equals(manifest!.ManifestSha256, ComputeManifestHash(manifest with { ManifestSha256 = "" }), StringComparison.Ordinal) ||
                !CleanupPathPolicy.TryValidateRoot(manifest.OriginalAllowlistedRoot, out string originalRoot, out _) ||
                !CleanupPathPolicy.TryValidateCandidate(originalRoot, manifest.OriginalPath, out _, out _, SafeAttributes) ||
                !CleanupPathPolicy.TryValidateCandidate(canonicalRoot, manifest.QuarantinePath, out string quarantinePath, out _, SafeAttributes)) return null;
            return new(canonicalManifest, manifest with { QuarantinePath = quarantinePath });
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    internal static async Task<CleanupQuarantineManifest> TransitionAsync(
        string manifestPath,
        CleanupQuarantineManifest current,
        CleanupQuarantineState state,
        string reasonCode,
        string message,
        bool mutationOccurred,
        Guid? claimId,
        CancellationToken cancellationToken)
    {
        CleanupQuarantineManifest next = current with
        {
            State = state,
            ActiveClaimId = claimId,
            Receipts = current.Receipts
                .Append(new CleanupQuarantineStepReceipt(Guid.NewGuid(), state, DateTimeOffset.UtcNow, reasonCode, message, mutationOccurred, claimId))
                .ToArray(),
            ManifestSha256 = ""
        };
        next = next with { ManifestSha256 = ComputeManifestHash(next) };
        await WriteManifestAsync(manifestPath, next, cancellationToken).ConfigureAwait(false);
        return next;
    }

    internal static async Task<CleanupQuarantineResourceLease?> TryAcquireResourceLeaseAsync(
        string root,
        string resourceKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (string.IsNullOrWhiteSpace(resourceKey)) throw new ArgumentException("A resource key is required.", nameof(resourceKey));
        Directory.CreateDirectory(root);
        if (!CleanupPathPolicy.TryValidateRoot(root, out string canonicalRoot, out string reason)) throw new IOException(reason);
        string leases = Path.Combine(canonicalRoot, "leases");
        Directory.CreateDirectory(leases);
        if (!CleanupPathPolicy.TryValidateCandidate(canonicalRoot, leases, out string canonicalLeases, out string leaseReason, SafeAttributes)) throw new IOException(leaseReason);
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey))) + ".lease";
        string leasePath = Path.Combine(canonicalLeases, name);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new CleanupQuarantineResourceLease(new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough));
            }
            catch (IOException ex) when (IsResourceLeaseContention(ex))
            {
                TimeSpan remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero) return null;
                await Task.Delay(remaining < ResourceLeaseRetryDelay ? remaining : ResourceLeaseRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsResourceLeaseContention(IOException exception) =>
        (exception.HResult & 0xFFFF) is 32 or 33;

    internal static IEnumerable<string> EnumerateManifestFiles(string root, string? excludedRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) yield break;
        string? excluded = excludedRoot is null ? null : Path.GetFullPath(excludedRoot);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> children;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.manifest.json", SearchOption.TopDirectoryOnly).ToArray();
                children = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            foreach (string file in files) yield return file;
            foreach (string child in children)
            {
                if ((excluded is null || !string.Equals(Path.GetFullPath(child), excluded, StringComparison.OrdinalIgnoreCase)) && !CleanupPathPolicy.IsReparsePoint(child)) pending.Push(child);
            }
        }
    }

    internal static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    internal static string ComputePreviewFingerprint(string action, IEnumerable<CleanupQuarantineEntry> entries)
    {
        var payload = new StringBuilder(action).Append('\n');
        foreach (CleanupQuarantineEntry entry in entries.OrderBy(entry => entry.Manifest.OperationId, StringComparer.Ordinal))
            payload.Append(entry.Manifest.OperationId).Append('\0').Append(entry.Manifest.ManifestSha256).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())));
    }

    internal static bool AreOnSameVolume(string source, string destination) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(source)), Path.GetPathRoot(Path.GetFullPath(destination)), StringComparison.OrdinalIgnoreCase);

    internal static bool IsPathUnder(string root, string candidate)
    {
        string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static CleanupQuarantineManifest CreateManifest(CleanupPreviewEntry entry, string itemId, string destination, string operationId, CleanupQuarantineState state, Guid claimId, string code, string message, bool mutationOccurred)
    {
        var unsigned = new CleanupQuarantineManifest(
            CurrentVersion,
            operationId,
            entry.PreviewId,
            itemId,
            entry.CanonicalPath,
            entry.AllowlistedRoot,
            destination,
            entry.SizeBytes,
            entry.LastWriteTimeUtc,
            entry.ContentSha256,
            entry.ResourceKey,
            state,
            claimId,
            DateTimeOffset.UtcNow,
            [new CleanupQuarantineStepReceipt(Guid.NewGuid(), state, DateTimeOffset.UtcNow, code, message, mutationOccurred, claimId)],
            "");
        return unsigned with { ManifestSha256 = ComputeManifestHash(unsigned) };
    }

    private static async Task WriteManifestAsync(string path, CleanupQuarantineManifest manifest, CancellationToken cancellationToken)
    {
        if (!IsValidManifest(manifest) || !string.Equals(manifest.ManifestSha256, ComputeManifestHash(manifest with { ManifestSha256 = "" }), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The cleanup manifest is incomplete or has an invalid integrity hash.");
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, WriteOptions);
        if (bytes.Length > MaximumManifestBytes) throw new InvalidDataException("The cleanup manifest exceeds its size limit.");
        string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The cleanup manifest has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N"));
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static bool IsValidManifest(CleanupQuarantineManifest? manifest) =>
        manifest is not null &&
        manifest.Version == CurrentVersion &&
        !string.IsNullOrWhiteSpace(manifest.OperationId) &&
        manifest.PreviewId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(manifest.ItemId) &&
        !string.IsNullOrWhiteSpace(manifest.OriginalPath) &&
        !string.IsNullOrWhiteSpace(manifest.OriginalAllowlistedRoot) &&
        !string.IsNullOrWhiteSpace(manifest.QuarantinePath) &&
        manifest.SizeBytes >= 0 &&
        CleanupResourceIdentity.IsSha256(manifest.ContentSha256) &&
        string.Equals(manifest.ResourceKey, CleanupResourceIdentity.CreateResourceKey(manifest.OriginalAllowlistedRoot, manifest.OriginalPath), StringComparison.Ordinal) &&
        Enum.IsDefined(manifest.State) &&
        manifest.Receipts is { Count: > 0 } &&
        manifest.Receipts.All(receipt => receipt.ReceiptId != Guid.Empty && Enum.IsDefined(receipt.State) && !string.IsNullOrWhiteSpace(receipt.ReasonCode)) &&
        !string.IsNullOrWhiteSpace(manifest.ManifestSha256);

    private static string ComputeManifestHash(CleanupQuarantineManifest manifest) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest with { ManifestSha256 = "" }, ReadOptions)));

    private static FileAttributes SafeAttributes(string path) =>
        File.Exists(path) || Directory.Exists(path) ? File.GetAttributes(path) : FileAttributes.Normal;
}
