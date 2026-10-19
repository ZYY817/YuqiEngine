using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services.Snapshot;

internal sealed record SnapshotRestoreStepReceipt(
    int Sequence,
    string StepId,
    string? CapabilityId,
    SnapshotRestoreStepState State,
    Guid ReceiptId,
    Guid? RecoveryId,
    SnapshotMutationStatus MutationStatus,
    string ReasonCode,
    string Message,
    DateTimeOffset RecordedAtUtc);

internal sealed record SnapshotRestoreBatchDocument(
    int SchemaVersion,
    Guid PlanId,
    string PlanSha256,
    string SnapshotId,
    SnapshotRestoreKind Kind,
    SnapshotRestoreBatchState State,
    SnapshotMutationStatus MutationStatus,
    Guid? ClaimId,
    SnapshotRestoreBatchClaimState ClaimState,
    string ReasonCode,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<SnapshotRestoreStepPlan> Steps,
    IReadOnlyList<SnapshotRestoreStepReceipt> Receipts);

internal interface ISnapshotRestoreBatchStore
{
    Task RegisterAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken);
    Task SetClaimingAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken);
    Task BeginAsync(SnapshotRestorePlan plan, Guid claimId, CancellationToken cancellationToken);
    Task AppendReceiptAsync(
        Guid planId,
        string planSha256,
        SnapshotRestoreStepReceipt receipt,
        CancellationToken cancellationToken);
    Task SetStateAsync(
        Guid planId,
        string planSha256,
        SnapshotRestoreBatchState state,
        SnapshotMutationStatus mutationStatus,
        string reasonCode,
        CancellationToken cancellationToken);
    Task<SnapshotRestoreBatchDocument?> LoadAsync(
        Guid planId,
        string planSha256,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SnapshotRestoreBatchIdentity>> EnumeratePendingAsync(CancellationToken cancellationToken);
}

internal sealed class InMemorySnapshotRestoreBatchStore : ISnapshotRestoreBatchStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, SnapshotRestoreBatchDocument> _documents = [];
    private readonly TimeProvider _timeProvider;

    internal InMemorySnapshotRestoreBatchStore(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public Task RegisterAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_documents.TryAdd(plan.PlanId, NewDocument(plan, _timeProvider.GetUtcNow())))
            {
                throw new InvalidOperationException("Snapshot restore batch plan already exists.");
            }
        }
        return Task.CompletedTask;
    }

    public Task BeginAsync(SnapshotRestorePlan plan, Guid claimId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var current = Require(plan.PlanId, plan.PlanSha256);
            if (current.State != SnapshotRestoreBatchState.Claiming || claimId == Guid.Empty)
            {
                throw new InvalidOperationException("Snapshot restore batch is not waiting for a claimed execution.");
            }
            _documents[plan.PlanId] = current with
            {
                State = SnapshotRestoreBatchState.Restoring,
                ClaimId = claimId,
                ClaimState = SnapshotRestoreBatchClaimState.Active,
                ReasonCode = "snapshot.batch-restoring",
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            };
        }
        return Task.CompletedTask;
    }

    public Task SetClaimingAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var current = Require(plan.PlanId, plan.PlanSha256);
            ValidateStateTransition(current, SnapshotRestoreBatchState.Claiming, SnapshotMutationStatus.None);
            _documents[plan.PlanId] = current with
            {
                State = SnapshotRestoreBatchState.Claiming,
                ClaimState = SnapshotRestoreBatchClaimState.Claiming,
                ReasonCode = "snapshot.batch-claiming",
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            };
        }
        return Task.CompletedTask;
    }

    public Task AppendReceiptAsync(
        Guid planId,
        string planSha256,
        SnapshotRestoreStepReceipt receipt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var current = Require(planId, planSha256);
            EnsureRestoring(current);
            var receipts = current.Receipts.ToList();
            ValidateNextReceipt(current, receipts, receipt);
            receipts.Add(receipt with { Sequence = receipts.Count + 1 });
            _documents[planId] = current with
            {
                MutationStatus = MergeMutation(current.MutationStatus, receipt.MutationStatus),
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                Receipts = receipts.AsReadOnly()
            };
        }
        return Task.CompletedTask;
    }

    public Task SetStateAsync(
        Guid planId,
        string planSha256,
        SnapshotRestoreBatchState state,
        SnapshotMutationStatus mutationStatus,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var current = Require(planId, planSha256);
            ValidateStateTransition(current, state, mutationStatus);
            _documents[planId] = current with
            {
                State = state,
                MutationStatus = MergeMutation(current.MutationStatus, mutationStatus),
                ClaimId = state == SnapshotRestoreBatchState.Available ? null : current.ClaimId,
                ClaimState = ClaimStateFor(current, state),
                ReasonCode = reasonCode,
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            };
        }
        return Task.CompletedTask;
    }

    public Task<SnapshotRestoreBatchDocument?> LoadAsync(
        Guid planId,
        string planSha256,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult(_documents.TryGetValue(planId, out var document)
                && FixedEquals(document.PlanSha256, planSha256)
                    ? document
                    : null);
        }
    }

    public Task<IReadOnlyList<SnapshotRestoreBatchIdentity>> EnumeratePendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            IReadOnlyList<SnapshotRestoreBatchIdentity> result = _documents.Values
                .Where(static document => IsPending(document.State))
                .Select(static document => ToIdentity(document))
                .OrderBy(static item => item.UpdatedAtUtc)
                .ToList();
            return Task.FromResult(result);
        }
    }

    internal static bool IsPending(SnapshotRestoreBatchState state) => state is
        SnapshotRestoreBatchState.Claiming or
        SnapshotRestoreBatchState.Restoring or
        SnapshotRestoreBatchState.ReleasingZeroWrite or
        SnapshotRestoreBatchState.CompletingVerified or
        SnapshotRestoreBatchState.InterruptedManual;

    internal static SnapshotRestoreBatchIdentity ToIdentity(SnapshotRestoreBatchDocument document) =>
        new(document.PlanId, document.PlanSha256, document.SnapshotId, document.State,
            document.MutationStatus, document.ReasonCode, document.UpdatedAtUtc, false, string.Empty);

    private SnapshotRestoreBatchDocument Require(Guid planId, string planSha256)
    {
        if (!_documents.TryGetValue(planId, out var current)
            || !FixedEquals(current.PlanSha256, planSha256))
        {
            throw new InvalidDataException("Snapshot restore batch plan is missing or has a different hash.");
        }
        return current;
    }

    internal static SnapshotRestoreBatchDocument NewDocument(
        SnapshotRestorePlan plan,
        DateTimeOffset now) =>
        new(
            FileSnapshotRestoreBatchStore.CurrentSchemaVersion,
            plan.PlanId,
            plan.PlanSha256,
            plan.SnapshotId,
            plan.Kind,
            SnapshotRestoreBatchState.Available,
            SnapshotMutationStatus.None,
            null,
            SnapshotRestoreBatchClaimState.Available,
            "snapshot.batch-available",
            now,
            plan.Steps,
            Array.Empty<SnapshotRestoreStepReceipt>());

    internal static void ValidateStateTransition(
        SnapshotRestoreBatchDocument current,
        SnapshotRestoreBatchState target,
        SnapshotMutationStatus requestedMutation)
    {
        var idempotent = current.State == target
            && target is SnapshotRestoreBatchState.Available
                or SnapshotRestoreBatchState.ReleasingZeroWrite
                or SnapshotRestoreBatchState.CompletingVerified
                or SnapshotRestoreBatchState.RestoredVerified
                or SnapshotRestoreBatchState.CompensatedVerified
                or SnapshotRestoreBatchState.InterruptedManual
                or SnapshotRestoreBatchState.ManualResolved
            && MergeMutation(current.MutationStatus, requestedMutation) == current.MutationStatus;
        var allowed = idempotent || ((current.State, target) switch
        {
            (SnapshotRestoreBatchState.Available, SnapshotRestoreBatchState.Claiming) =>
                current.MutationStatus == SnapshotMutationStatus.None
                && requestedMutation == SnapshotMutationStatus.None,
            (SnapshotRestoreBatchState.Restoring, SnapshotRestoreBatchState.ReleasingZeroWrite) =>
                current.MutationStatus == SnapshotMutationStatus.None
                && requestedMutation == SnapshotMutationStatus.None,
            (SnapshotRestoreBatchState.InterruptedManual, SnapshotRestoreBatchState.ReleasingZeroWrite) =>
                current.MutationStatus == SnapshotMutationStatus.None
                && requestedMutation == SnapshotMutationStatus.None
                && current.ClaimId.HasValue,
            (SnapshotRestoreBatchState.ReleasingZeroWrite, SnapshotRestoreBatchState.Available) =>
                current.MutationStatus == SnapshotMutationStatus.None
                && requestedMutation == SnapshotMutationStatus.None,
            (SnapshotRestoreBatchState.Restoring, SnapshotRestoreBatchState.CompletingVerified) => true,
            (SnapshotRestoreBatchState.CompletingVerified, SnapshotRestoreBatchState.RestoredVerified) => true,
            (SnapshotRestoreBatchState.Restoring, SnapshotRestoreBatchState.CompensatedVerified) =>
                current.MutationStatus == SnapshotMutationStatus.Known
                && requestedMutation == SnapshotMutationStatus.Known,
            (SnapshotRestoreBatchState.Restoring, SnapshotRestoreBatchState.InterruptedManual) => true,
            (SnapshotRestoreBatchState.Claiming, SnapshotRestoreBatchState.InterruptedManual) => true,
            (SnapshotRestoreBatchState.ReleasingZeroWrite, SnapshotRestoreBatchState.InterruptedManual) => true,
            (SnapshotRestoreBatchState.CompletingVerified, SnapshotRestoreBatchState.InterruptedManual) => true,
            (SnapshotRestoreBatchState.InterruptedManual, SnapshotRestoreBatchState.ManualResolved) => true,
            _ => false
        });
        if (!allowed)
        {
            throw new InvalidOperationException($"Invalid snapshot batch transition: {current.State} -> {target}.");
        }
    }

    internal static SnapshotRestoreBatchClaimState ClaimStateFor(
        SnapshotRestoreBatchDocument current,
        SnapshotRestoreBatchState target) => target switch
    {
        SnapshotRestoreBatchState.Available =>
            current.ClaimState is SnapshotRestoreBatchClaimState.ReleasePending
                or SnapshotRestoreBatchClaimState.Released
                ? SnapshotRestoreBatchClaimState.Released
                : SnapshotRestoreBatchClaimState.Available,
        SnapshotRestoreBatchState.Claiming => SnapshotRestoreBatchClaimState.Claiming,
        SnapshotRestoreBatchState.Restoring => SnapshotRestoreBatchClaimState.Active,
        SnapshotRestoreBatchState.ReleasingZeroWrite => SnapshotRestoreBatchClaimState.ReleasePending,
        SnapshotRestoreBatchState.CompletingVerified => SnapshotRestoreBatchClaimState.CompletingVerified,
        SnapshotRestoreBatchState.RestoredVerified => SnapshotRestoreBatchClaimState.Restored,
        SnapshotRestoreBatchState.CompensatedVerified => SnapshotRestoreBatchClaimState.Compensated,
        SnapshotRestoreBatchState.InterruptedManual => current.ClaimId.HasValue
            ? SnapshotRestoreBatchClaimState.ManualRequired
            : SnapshotRestoreBatchClaimState.Conflict,
        SnapshotRestoreBatchState.ManualResolved => SnapshotRestoreBatchClaimState.ManualResolved,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    internal static void ValidateNextReceipt(
        SnapshotRestoreBatchDocument document,
        IReadOnlyList<SnapshotRestoreStepReceipt> existing,
        SnapshotRestoreStepReceipt next)
    {
        if (!document.Steps.Any(step => string.Equals(step.StepId, next.StepId, StringComparison.Ordinal))
            || string.IsNullOrWhiteSpace(next.StepId)
            || string.IsNullOrWhiteSpace(next.ReasonCode)
            || next.ReceiptId == Guid.Empty && next.State is SnapshotRestoreStepState.RestoredVerified
                or SnapshotRestoreStepState.CompensatedVerified)
        {
            throw new InvalidDataException("Snapshot restore step receipt is incomplete or not part of the plan.");
        }

        var previous = existing.LastOrDefault(receipt =>
            string.Equals(receipt.StepId, next.StepId, StringComparison.Ordinal));
        var allowed = previous is null
            ? next.State is SnapshotRestoreStepState.Executing or SnapshotRestoreStepState.Unsupported
            : (previous.State, next.State) switch
            {
                (SnapshotRestoreStepState.Executing, SnapshotRestoreStepState.NoChange) => true,
                (SnapshotRestoreStepState.Executing, SnapshotRestoreStepState.RestoredVerified) => true,
                (SnapshotRestoreStepState.Executing, SnapshotRestoreStepState.Failed) => true,
                (SnapshotRestoreStepState.Executing, SnapshotRestoreStepState.Cancelled) => true,
                (SnapshotRestoreStepState.Executing, SnapshotRestoreStepState.Unknown) => true,
                (SnapshotRestoreStepState.RestoredVerified, SnapshotRestoreStepState.Compensating) => true,
                (SnapshotRestoreStepState.Failed, SnapshotRestoreStepState.Compensating) =>
                    previous.RecoveryId.HasValue,
                (SnapshotRestoreStepState.Compensating, SnapshotRestoreStepState.CompensatedVerified) => true,
                (SnapshotRestoreStepState.Compensating, SnapshotRestoreStepState.CompensationFailed) => true,
                (SnapshotRestoreStepState.Failed, SnapshotRestoreStepState.Executing) =>
                    previous.MutationStatus == SnapshotMutationStatus.None,
                (SnapshotRestoreStepState.Cancelled, SnapshotRestoreStepState.Executing) =>
                    previous.MutationStatus == SnapshotMutationStatus.None,
                _ => false
            };
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Invalid snapshot step transition for {next.StepId}: {previous?.State.ToString() ?? "none"} -> {next.State}.");
        }
    }

    internal static void EnsureRestoring(SnapshotRestoreBatchDocument document)
    {
        if (document.State != SnapshotRestoreBatchState.Restoring || document.ClaimId is null)
        {
            throw new InvalidOperationException("Snapshot restore batch is not in the Restoring state.");
        }
    }

    internal static SnapshotMutationStatus MergeMutation(
        SnapshotMutationStatus current,
        SnapshotMutationStatus next) =>
        current == SnapshotMutationStatus.Unknown || next == SnapshotMutationStatus.Unknown
            ? SnapshotMutationStatus.Unknown
            : current == SnapshotMutationStatus.Known || next == SnapshotMutationStatus.Known
                ? SnapshotMutationStatus.Known
                : SnapshotMutationStatus.None;

    internal static bool FixedEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed class FileSnapshotRestoreBatchStore : ISnapshotRestoreBatchStore
{
    internal const int CurrentSchemaVersion = 2;
    internal const int MaximumDocumentBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _rootDirectory;
    private readonly string _rootPrefix;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    internal FileSnapshotRestoreBatchStore()
        : this(Path.Combine(AppPaths.SnapshotsDir, "restore-batches"))
    {
    }

    internal FileSnapshotRestoreBatchStore(
        string rootDirectory,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task RegisterAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var document = InMemorySnapshotRestoreBatchStore.NewDocument(plan, _timeProvider.GetUtcNow());
        var path = PathFor(plan.PlanId);
        Directory.CreateDirectory(_rootDirectory);
        try
        {
            await WriteNewAsync(path, document, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(path))
        {
            var existing = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (!InMemorySnapshotRestoreBatchStore.FixedEquals(existing.PlanSha256, plan.PlanSha256))
            {
                throw new InvalidDataException("Existing snapshot restore batch has a different plan hash.");
            }
        }
    }

    public Task BeginAsync(SnapshotRestorePlan plan, Guid claimId, CancellationToken cancellationToken) =>
        UpdateAsync(
            plan.PlanId,
            plan.PlanSha256,
            current =>
            {
                if (current.State != SnapshotRestoreBatchState.Claiming || claimId == Guid.Empty)
                {
                    throw new InvalidOperationException("Snapshot restore batch is not waiting for a claimed execution.");
                }
                return current with
                {
                    State = SnapshotRestoreBatchState.Restoring,
                    ClaimId = claimId,
                    ClaimState = SnapshotRestoreBatchClaimState.Active,
                    ReasonCode = "snapshot.batch-restoring",
                    UpdatedAtUtc = _timeProvider.GetUtcNow()
                };
            },
            cancellationToken);

    public Task SetClaimingAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken) =>
        UpdateAsync(
            plan.PlanId,
            plan.PlanSha256,
            current =>
            {
                InMemorySnapshotRestoreBatchStore.ValidateStateTransition(
                    current,
                    SnapshotRestoreBatchState.Claiming,
                    SnapshotMutationStatus.None);
                return current with
                {
                    State = SnapshotRestoreBatchState.Claiming,
                    ClaimState = SnapshotRestoreBatchClaimState.Claiming,
                    ReasonCode = "snapshot.batch-claiming",
                    UpdatedAtUtc = _timeProvider.GetUtcNow()
                };
            },
            cancellationToken);

    public Task AppendReceiptAsync(
        Guid planId,
        string planSha256,
        SnapshotRestoreStepReceipt receipt,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            planId,
            planSha256,
            current =>
            {
                InMemorySnapshotRestoreBatchStore.EnsureRestoring(current);
                var receipts = current.Receipts.ToList();
                InMemorySnapshotRestoreBatchStore.ValidateNextReceipt(current, receipts, receipt);
                receipts.Add(receipt with { Sequence = receipts.Count + 1 });
                return current with
                {
                    MutationStatus = InMemorySnapshotRestoreBatchStore.MergeMutation(
                        current.MutationStatus,
                        receipt.MutationStatus),
                    UpdatedAtUtc = _timeProvider.GetUtcNow(),
                    Receipts = receipts.AsReadOnly()
                };
            },
            cancellationToken);

    public Task SetStateAsync(
        Guid planId,
        string planSha256,
        SnapshotRestoreBatchState state,
        SnapshotMutationStatus mutationStatus,
        string reasonCode,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            planId,
            planSha256,
            current =>
            {
                InMemorySnapshotRestoreBatchStore.ValidateStateTransition(current, state, mutationStatus);
                return current with
                {
                    State = state,
                    MutationStatus = InMemorySnapshotRestoreBatchStore.MergeMutation(
                        current.MutationStatus,
                        mutationStatus),
                    ClaimId = state == SnapshotRestoreBatchState.Available ? null : current.ClaimId,
                    ClaimState = InMemorySnapshotRestoreBatchStore.ClaimStateFor(current, state),
                    ReasonCode = reasonCode,
                    UpdatedAtUtc = _timeProvider.GetUtcNow()
                };
            },
            cancellationToken);

    public async Task<SnapshotRestoreBatchDocument?> LoadAsync(
        Guid planId,
        string planSha256,
        CancellationToken cancellationToken)
    {
        var path = PathFor(planId);
        if (!File.Exists(path))
        {
            return null;
        }
        var document = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return InMemorySnapshotRestoreBatchStore.FixedEquals(document.PlanSha256, planSha256)
            ? document
            : null;
    }

    public async Task<IReadOnlyList<SnapshotRestoreBatchIdentity>> EnumeratePendingAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<SnapshotRestoreBatchIdentity>();
        if (!Directory.Exists(_rootDirectory))
            return results;

        foreach (var path in Directory.GetFiles(_rootDirectory, "*.batch.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var document = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
                var expectedName = $"{document.PlanId:N}.batch.json";
                if (!string.Equals(Path.GetFileName(path), expectedName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Snapshot restore batch file name does not match its plan id.");
                if (InMemorySnapshotRestoreBatchStore.IsPending(document.State))
                    results.Add(InMemorySnapshotRestoreBatchStore.ToIdentity(document));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var stem = Path.GetFileName(path).Split('.')[0];
                _ = Guid.TryParseExact(stem, "N", out var planId);
                results.Add(new SnapshotRestoreBatchIdentity(
                    planId, string.Empty, string.Empty, SnapshotRestoreBatchState.InterruptedManual,
                    SnapshotMutationStatus.Unknown, "snapshot.batch-store-failure",
                    new FileInfo(path).LastWriteTimeUtc, true, ex.Message));
            }
        }
        return results.OrderBy(static item => item.UpdatedAtUtc).ToList();
    }

    private async Task UpdateAsync(
        Guid planId,
        string planSha256,
        Func<SnapshotRestoreBatchDocument, SnapshotRestoreBatchDocument> update,
        CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(planId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = PathFor(planId);
            var current = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (!InMemorySnapshotRestoreBatchStore.FixedEquals(current.PlanSha256, planSha256))
            {
                throw new InvalidDataException("Snapshot restore batch plan hash does not match.");
            }
            await ReplaceAsync(path, update(current), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private string PathFor(Guid planId)
    {
        if (planId == Guid.Empty)
        {
            throw new InvalidDataException("Snapshot restore batch plan id must not be empty.");
        }
        var candidate = Path.GetFullPath(Path.Combine(_rootDirectory, $"{planId:N}.batch.json"));
        if (!candidate.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Snapshot restore batch path escapes the configured root.");
        }
        return candidate;
    }

    private static async Task<SnapshotRestoreBatchDocument> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > MaximumDocumentBytes)
        {
            throw new InvalidDataException("Snapshot restore batch document is empty or exceeds the size limit.");
        }
        var envelope = await ReadEnvelopeAsync(path, cancellationToken).ConfigureAwait(false);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, SerializerOptions);
        var expected = Convert.ToHexString(SHA256.HashData(payloadBytes));
        if (!InMemorySnapshotRestoreBatchStore.FixedEquals(envelope.PayloadSha256, expected))
        {
            throw new InvalidDataException("Snapshot restore batch document hash does not match its payload.");
        }
        ValidateDocument(envelope.Payload);
        return envelope.Payload;
    }

    private static async Task<BatchEnvelope> ReadEnvelopeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<BatchEnvelope>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Snapshot restore batch envelope is missing.");
    }

    private static Task WriteNewAsync(
        string path,
        SnapshotRestoreBatchDocument document,
        CancellationToken cancellationToken) =>
        WriteAsync(path, document, replace: false, cancellationToken);

    private static Task ReplaceAsync(
        string path,
        SnapshotRestoreBatchDocument document,
        CancellationToken cancellationToken) =>
        WriteAsync(path, document, replace: true, cancellationToken);

    private static async Task WriteAsync(
        string path,
        SnapshotRestoreBatchDocument document,
        bool replace,
        CancellationToken cancellationToken)
    {
        ValidateDocument(document);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        var envelope = new BatchEnvelope(
            document,
            Convert.ToHexString(SHA256.HashData(payloadBytes)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        if (bytes.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException("Snapshot restore batch document exceeds the size limit.");
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Snapshot restore batch path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            _ = await ReadAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, replace);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void ValidateDocument(SnapshotRestoreBatchDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion
            || document.PlanId == Guid.Empty
            || !IsSha256(document.PlanSha256)
            || string.IsNullOrWhiteSpace(document.SnapshotId)
            || !Enum.IsDefined(document.Kind)
            || !Enum.IsDefined(document.State)
            || !Enum.IsDefined(document.MutationStatus)
            || !Enum.IsDefined(document.ClaimState)
            || string.IsNullOrWhiteSpace(document.ReasonCode)
            || (document.State is SnapshotRestoreBatchState.Restoring
                    or SnapshotRestoreBatchState.ReleasingZeroWrite
                    or SnapshotRestoreBatchState.CompletingVerified
                    or SnapshotRestoreBatchState.RestoredVerified
                    or SnapshotRestoreBatchState.CompensatedVerified)
                != document.ClaimId.HasValue
            || document.ClaimId == Guid.Empty
            || document.State == SnapshotRestoreBatchState.Available
                && document.ClaimState is not SnapshotRestoreBatchClaimState.Available
                    and not SnapshotRestoreBatchClaimState.Released
            || document.State == SnapshotRestoreBatchState.Claiming
                && document.ClaimState != SnapshotRestoreBatchClaimState.Claiming
            || document.State == SnapshotRestoreBatchState.Restoring
                && document.ClaimState != SnapshotRestoreBatchClaimState.Active
            || document.State == SnapshotRestoreBatchState.ReleasingZeroWrite
                && document.ClaimState != SnapshotRestoreBatchClaimState.ReleasePending
            || document.State == SnapshotRestoreBatchState.CompletingVerified
                && document.ClaimState != SnapshotRestoreBatchClaimState.CompletingVerified
            || document.State == SnapshotRestoreBatchState.RestoredVerified
                && document.ClaimState != SnapshotRestoreBatchClaimState.Restored
            || document.State == SnapshotRestoreBatchState.CompensatedVerified
                && document.ClaimState != SnapshotRestoreBatchClaimState.Compensated
            || document.State == SnapshotRestoreBatchState.InterruptedManual
                && document.ClaimState is not SnapshotRestoreBatchClaimState.ManualRequired
                    and not SnapshotRestoreBatchClaimState.Conflict
            || (document.State == SnapshotRestoreBatchState.InterruptedManual
                && (document.ClaimState == SnapshotRestoreBatchClaimState.ManualRequired)
                    != document.ClaimId.HasValue)
            || document.State == SnapshotRestoreBatchState.ManualResolved
                && document.ClaimState != SnapshotRestoreBatchClaimState.ManualResolved
            || document.Steps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != document.Steps.Count
            || document.Receipts.Select(receipt => receipt.Sequence).Distinct().Count() != document.Receipts.Count
            || document.Receipts.Where((receipt, index) => receipt.Sequence != index + 1).Any())
        {
            throw new InvalidDataException("Snapshot restore batch document is missing, incompatible, or invalid.");
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static character => char.IsAsciiHexDigit(character));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record BatchEnvelope(
        SnapshotRestoreBatchDocument Payload,
        string PayloadSha256);
}
