using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Tests.Snapshot;

public sealed class SnapshotP1CrashRecoveryTests
{
    private const string Identity = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Target = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void V1_through_V3_remain_rejected_by_the_typed_restore_schema_gate(int version) =>
        Assert.False(SnapshotService.IsTypedRestoreSchemaVersionSupported(version));

    [Fact]
    public async Task Restart_during_writer_is_projected_unknown_manual_without_marking_restored()
    {
        var claims = new FaultClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var plan = Plan("snapshot_restart_writer");
        var claimId = await BeginAsync(plan, claims, batches);
        await batches.AppendReceiptAsync(
            plan.PlanId,
            plan.PlanSha256,
            Receipt(SnapshotRestoreStepState.Executing, mutation: SnapshotMutationStatus.Unknown),
            default);

        var report = await Coordinator(claims, batches).ReconcileAsync(plan.PlanId, plan.PlanSha256, default);
        var batch = await batches.LoadAsync(plan.PlanId, plan.PlanSha256, default);

        Assert.Equal(SnapshotRestoreRecoveryStatus.ManualInterventionRequired, report.Status);
        Assert.Equal(SnapshotRestoreBatchState.InterruptedManual, report.BatchState);
        Assert.Equal(SnapshotRestoreBatchClaimState.ManualRequired, report.ClaimState);
        Assert.Equal(SnapshotMutationStatus.Unknown, report.MutationStatus);
        Assert.Equal(1, report.TypedResourceCount);
        Assert.Equal(0, report.RecoverableResourceCount);
        Assert.Equal(1, report.UnknownResourceCount);
        Assert.Equal(1, report.ManualResourceCount);
        Assert.Equal(0, report.UnclassifiedResourceCount);
        Assert.Equal(claimId, batch!.ClaimId);
        Assert.Equal(0, claims.MarkRestoredCount);
        Assert.Equal(0, claims.ReleaseCount);
    }

    [Fact]
    public async Task Restart_does_not_mark_restored_without_post_restart_readback()
    {
        var claims = new FaultClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var plan = Plan("snapshot_restart_completing");
        await BeginAsync(plan, claims, batches);
        var receiptId = Guid.NewGuid();
        var recoveryId = Guid.NewGuid();
        await batches.AppendReceiptAsync(plan.PlanId, plan.PlanSha256, Receipt(SnapshotRestoreStepState.Executing), default);
        await batches.AppendReceiptAsync(
            plan.PlanId,
            plan.PlanSha256,
            Receipt(
                SnapshotRestoreStepState.RestoredVerified,
                receiptId,
                recoveryId,
                SnapshotMutationStatus.Known),
            default);
        await batches.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.CompletingVerified,
            SnapshotMutationStatus.Known,
            "snapshot.completing",
            default);

        var report = await Coordinator(claims, batches).ReconcileAsync(plan.PlanId, plan.PlanSha256, default);

        Assert.Equal(SnapshotRestoreRecoveryStatus.ManualInterventionRequired, report.Status);
        Assert.Equal(SnapshotRestoreBatchState.InterruptedManual, report.BatchState);
        Assert.Equal(SnapshotRestoreBatchClaimState.ManualRequired, report.ClaimState);
        Assert.Equal(1, report.TypedResourceCount);
        Assert.Equal(1, report.RecoverableResourceCount);
        Assert.Equal(1, report.UnknownResourceCount);
        Assert.Equal(1, report.ManualResourceCount);
        Assert.Equal(0, claims.MarkRestoredCount);
        Assert.Equal(0, claims.ReleaseCount);
    }

    [Fact]
    public async Task Completing_state_with_missing_resource_receipt_becomes_manual_and_never_marks_restored()
    {
        var claims = new FaultClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var plan = Plan("snapshot_incomplete_completing");
        await BeginAsync(plan, claims, batches);
        await batches.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.CompletingVerified,
            SnapshotMutationStatus.Known,
            "snapshot.completing-without-receipt",
            default);

        var report = await Coordinator(claims, batches).ReconcileAsync(plan.PlanId, plan.PlanSha256, default);

        Assert.Equal(SnapshotRestoreRecoveryStatus.ManualInterventionRequired, report.Status);
        Assert.Equal(SnapshotMutationStatus.Unknown, report.MutationStatus);
        Assert.Equal(1, report.UnclassifiedResourceCount);
        Assert.Equal(0, claims.MarkRestoredCount);
        Assert.Equal(0, claims.ReleaseCount);
    }

    [Fact]
    public async Task Concurrent_restart_reconciliation_converges_on_one_manual_terminal_without_readback()
    {
        var claims = new FaultClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var plan = Plan("snapshot_concurrent_reconcile");
        await BeginAsync(plan, claims, batches);
        await batches.AppendReceiptAsync(plan.PlanId, plan.PlanSha256, Receipt(SnapshotRestoreStepState.Executing), default);
        await batches.AppendReceiptAsync(
            plan.PlanId,
            plan.PlanSha256,
            Receipt(SnapshotRestoreStepState.RestoredVerified, Guid.NewGuid(), Guid.NewGuid(), SnapshotMutationStatus.Known),
            default);
        await batches.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.CompletingVerified,
            SnapshotMutationStatus.Known,
            "snapshot.completing",
            default);
        var coordinator = Coordinator(claims, batches);

        var reports = await Task.WhenAll(
            coordinator.ReconcileAsync(plan.PlanId, plan.PlanSha256, default),
            coordinator.ReconcileAsync(plan.PlanId, plan.PlanSha256, default));

        Assert.All(reports, report => Assert.Equal(SnapshotRestoreRecoveryStatus.ManualInterventionRequired, report.Status));
        Assert.Equal(SnapshotRestoreBatchState.InterruptedManual,
            (await batches.LoadAsync(plan.PlanId, plan.PlanSha256, default))!.State);
        Assert.Equal(0, claims.MarkRestoredCount);
        Assert.Equal(0, claims.ReleaseCount);
    }

    [Fact]
    public async Task Restart_during_zero_write_release_retries_release_without_writer_or_mark_restored()
    {
        var claims = new FaultClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var plan = Plan("snapshot_restart_release");
        await BeginAsync(plan, claims, batches);
        await batches.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.ReleasingZeroWrite,
            SnapshotMutationStatus.None,
            "snapshot.release-pending",
            default);

        var report = await Coordinator(claims, batches).ReconcileAsync(plan.PlanId, plan.PlanSha256, default);

        Assert.Equal(SnapshotRestoreRecoveryStatus.Available, report.Status);
        Assert.Equal(SnapshotRestoreBatchClaimState.Released, report.ClaimState);
        Assert.Equal(SnapshotMutationStatus.None, report.MutationStatus);
        Assert.Equal(1, claims.ReleaseCount);
        Assert.Equal(0, claims.MarkRestoredCount);
    }

    [Theory]
    [InlineData(RecoveryClaimStatus.AlreadyRestoring)]
    [InlineData(RecoveryClaimStatus.AlreadyRestored)]
    [InlineData(RecoveryClaimStatus.IntegrityMismatch)]
    [InlineData(RecoveryClaimStatus.Missing)]
    public async Task Claim_conflict_is_durable_manual_and_never_invokes_writer_or_mark(
        RecoveryClaimStatus conflict)
    {
        var claims = new FaultClaimStore { ForcedBeginStatus = conflict };
        var batches = new InMemorySnapshotRestoreBatchStore();
        var coordinator = Coordinator(claims, batches);
        var plan = await coordinator.PrepareAsync("snapshot_claim_conflict", SnapshotRestoreKind.Normal, default);
        var writes = 0;

        var result = await coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(Verified(++writes)),
            Blocked,
            default);
        var report = await coordinator.ReconcileAsync(plan.PlanId, plan.PlanSha256, default);

        Assert.False(result.Success);
        Assert.Equal(0, writes);
        Assert.Equal(SnapshotRestoreBatchState.InterruptedManual, report.BatchState);
        Assert.Equal(SnapshotRestoreBatchClaimState.Conflict, report.ClaimState);
        Assert.Equal(SnapshotMutationStatus.None, report.MutationStatus);
        Assert.Equal(0, claims.MarkRestoredCount);
    }

    [Fact]
    public async Task Claim_store_failure_is_fail_closed_and_persisted_for_manual_review()
    {
        var claims = new FaultClaimStore { ThrowOnBegin = true };
        var batches = new InMemorySnapshotRestoreBatchStore();
        var coordinator = Coordinator(claims, batches);
        var plan = await coordinator.PrepareAsync("snapshot_claim_store_failure", SnapshotRestoreKind.Normal, default);
        var writes = 0;

        var result = await coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(Verified(++writes)),
            Blocked,
            default);
        var report = await coordinator.ReconcileAsync(plan.PlanId, plan.PlanSha256, default);

        Assert.False(result.Success);
        Assert.Equal(0, writes);
        Assert.Equal(SnapshotRestoreRecoveryStatus.ManualInterventionRequired, report.Status);
        Assert.Equal(SnapshotRestoreBatchClaimState.Conflict, report.ClaimState);
        Assert.Equal(0, claims.MarkRestoredCount);
    }

    [Fact]
    public async Task Success_flag_without_complete_per_resource_receipts_never_marks_restored()
    {
        var claims = new FaultClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var coordinator = Coordinator(claims, batches);
        var plan = await coordinator.PrepareAsync("snapshot_false_success", SnapshotRestoreKind.Normal, default);

        var result = await coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(Verified(1)),
            Blocked,
            default);
        var report = await coordinator.ReconcileAsync(plan.PlanId, plan.PlanSha256, default);

        Assert.False(result.Success);
        Assert.Equal("snapshot.completion-evidence-incomplete", result.OutcomeReasonCode);
        Assert.Equal(SnapshotRestoreRecoveryStatus.ManualInterventionRequired, report.Status);
        Assert.Equal(SnapshotMutationStatus.Unknown, report.MutationStatus);
        Assert.Equal(0, claims.MarkRestoredCount);
    }

    [Fact]
    public async Task Manual_acknowledgement_is_terminal_but_never_claims_verified_restore()
    {
        var claims = new FaultClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var plan = Plan("snapshot_manual_ack");
        await BeginAsync(plan, claims, batches);
        await batches.AppendReceiptAsync(
            plan.PlanId,
            plan.PlanSha256,
            Receipt(SnapshotRestoreStepState.Executing, mutation: SnapshotMutationStatus.Unknown),
            default);
        await batches.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.InterruptedManual,
            SnapshotMutationStatus.Unknown,
            "snapshot.interrupted",
            default);

        var report = await Coordinator(claims, batches).ResolveManualAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreManualDecision.AcknowledgeManualRecovery,
            default);

        Assert.Equal(SnapshotRestoreRecoveryStatus.ManualResolved, report.Status);
        Assert.Equal(SnapshotRestoreBatchClaimState.ManualResolved, report.ClaimState);
        Assert.Equal(1, report.UnknownResourceCount);
        Assert.Equal(1, report.ManualResourceCount);
        Assert.Equal(0, claims.MarkRestoredCount);
        Assert.Equal(0, claims.ReleaseCount);
    }

    [Fact]
    public async Task Manual_release_requires_durable_zero_write_terminal_evidence()
    {
        var claims = new FaultClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var plan = Plan("snapshot_manual_zero");
        await BeginAsync(plan, claims, batches);
        await batches.AppendReceiptAsync(plan.PlanId, plan.PlanSha256, Receipt(SnapshotRestoreStepState.Executing), default);
        await batches.AppendReceiptAsync(plan.PlanId, plan.PlanSha256, Receipt(SnapshotRestoreStepState.Failed), default);
        await batches.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.InterruptedManual,
            SnapshotMutationStatus.None,
            "snapshot.failed-zero-write",
            default);

        var report = await Coordinator(claims, batches).ResolveManualAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreManualDecision.ReleaseVerifiedZeroWrite,
            default);

        Assert.Equal(SnapshotRestoreRecoveryStatus.Available, report.Status);
        Assert.Equal(SnapshotRestoreBatchClaimState.Released, report.ClaimState);
        Assert.Equal(1, claims.ReleaseCount);
        Assert.Equal(0, claims.MarkRestoredCount);
    }

    [Fact]
    public async Task Claim_release_failure_keeps_zero_write_evidence_and_can_be_manually_retried()
    {
        var claims = new FaultClaimStore { ThrowOnRelease = true };
        var batches = new InMemorySnapshotRestoreBatchStore();
        var coordinator = Coordinator(claims, batches);
        var plan = await coordinator.PrepareAsync("snapshot_release_failure", SnapshotRestoreKind.Normal, default);

        await Assert.ThrowsAsync<IOException>(() => coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            async _ =>
            {
                await batches.AppendReceiptAsync(plan.PlanId, plan.PlanSha256, Receipt(SnapshotRestoreStepState.Executing), default);
                await batches.AppendReceiptAsync(plan.PlanId, plan.PlanSha256, Receipt(SnapshotRestoreStepState.Failed), default);
                return new SnapshotRestoreResult
                {
                    Success = false,
                    FailedCount = 1,
                    MutationStatus = SnapshotMutationStatus.None,
                    OutcomeReasonCode = "snapshot.failed-zero-write"
                };
            },
            Blocked,
            default));
        var interrupted = await batches.LoadAsync(plan.PlanId, plan.PlanSha256, default);
        Assert.Equal(SnapshotRestoreBatchState.InterruptedManual, interrupted!.State);
        Assert.Equal(SnapshotMutationStatus.None, interrupted.MutationStatus);
        Assert.Equal(0, claims.MarkRestoredCount);

        claims.ThrowOnRelease = false;
        var report = await coordinator.ResolveManualAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreManualDecision.ReleaseVerifiedZeroWrite,
            default);

        Assert.Equal(SnapshotRestoreRecoveryStatus.Available, report.Status);
        Assert.Equal(SnapshotRestoreBatchClaimState.Released, report.ClaimState);
        Assert.Equal(1, claims.ReleaseCount);
    }

    [Fact]
    public async Task Corrupt_batch_reports_unknown_counts_and_never_touches_claim_terminal()
    {
        var root = Path.Combine(Path.GetTempPath(), "yuqi-snapshot-p1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var claims = new FaultClaimStore();
            var store = new FileSnapshotRestoreBatchStore(root);
            var plan = Plan("snapshot_corrupt");
            await store.RegisterAsync(plan, default);
            var path = Path.Combine(root, $"{plan.PlanId:N}.batch.json");
            var json = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, json.Replace("snapshot.batch-available", "snapshot.batch-corrupt", StringComparison.Ordinal));

            var report = await Coordinator(claims, store).ReconcileAsync(plan.PlanId, plan.PlanSha256, default);

            Assert.Equal(SnapshotRestoreRecoveryStatus.StoreFailure, report.Status);
            Assert.Null(report.TypedResourceCount);
            Assert.Null(report.RecoverableResourceCount);
            Assert.Null(report.UnknownResourceCount);
            Assert.Null(report.ManualResourceCount);
            Assert.Null(report.UnclassifiedResourceCount);
            Assert.Equal(0, claims.MarkRestoredCount);
            Assert.Equal(0, claims.ReleaseCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SnapshotRestoreCoordinator Coordinator(
        FaultClaimStore claims,
        ISnapshotRestoreBatchStore batches) => new(
        new AllowGate(),
        (_, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SnapshotRestorePreparation(Identity, [Step()]));
        },
        (_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        },
        claims,
        batches);

    private static async Task<Guid> BeginAsync(
        SnapshotRestorePlan plan,
        FaultClaimStore claims,
        ISnapshotRestoreBatchStore batches)
    {
        await batches.RegisterAsync(plan, default);
        await claims.RegisterAvailableAsync(plan.PlanId, plan.PlanSha256, default);
        await batches.SetClaimingAsync(plan, default);
        var claim = await claims.TryBeginRestoreAsync(plan.PlanId, plan.PlanSha256, default);
        var claimId = Assert.IsType<Guid>(claim.ClaimId);
        await batches.BeginAsync(plan, claimId, default);
        return claimId;
    }

    private static SnapshotRestoreResult Verified(int count) => new()
    {
        Success = true,
        RestoredCount = count,
        MutationStatus = SnapshotMutationStatus.Known,
        OutcomeReasonCode = "snapshot.restored-verified"
    };

    private static SnapshotRestoreResult Blocked(string message) => new()
    {
        Success = false,
        OutcomeReasonCode = "snapshot.blocked",
        Message = message
    };

    private static SnapshotRestorePlan Plan(string snapshotId)
    {
        var now = DateTimeOffset.UtcNow;
        return new SnapshotRestorePlan(
            SnapshotRestoreCoordinator.CurrentSchemaVersion,
            Guid.NewGuid(),
            snapshotId,
            SnapshotRestoreKind.Normal,
            Identity,
            [Step()],
            Target,
            now,
            now.AddMinutes(2));
    }

    private static SnapshotRestoreStepPlan Step() => new(
        "typed:test.capability",
        SnapshotRestoreResourceKind.TypedCapability,
        "test.capability",
        Target,
        true,
        "snapshot.typed-owner");

    private static SnapshotRestoreStepReceipt Receipt(
        SnapshotRestoreStepState state,
        Guid? receiptId = null,
        Guid? recoveryId = null,
        SnapshotMutationStatus mutation = SnapshotMutationStatus.None) => new(
        0,
        "typed:test.capability",
        "test.capability",
        state,
        receiptId ?? Guid.Empty,
        recoveryId,
        mutation,
        "snapshot.test",
        "test",
        DateTimeOffset.UtcNow);

    private sealed class AllowGate : ISnapshotRestoreAuthorizationGate
    {
        public Task<SnapshotRestoreAuthorizationDecision> AuthorizeAsync(
            SnapshotRestorePlan plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SnapshotRestoreAuthorizationDecision(true, "allowed", "allowed"));
    }

    private sealed class FaultClaimStore : IRecoveryClaimStore
    {
        private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
        public RecoveryClaimStatus? ForcedBeginStatus { get; set; }
        public bool ThrowOnBegin { get; set; }
        public bool ThrowOnRelease { get; set; }
        public int MarkRestoredCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task RegisterAvailableAsync(Guid id, string hash, CancellationToken cancellationToken)
        {
            _entries[id] = new Entry(hash, State.Available, null);
            return Task.CompletedTask;
        }

        public Task<RecoveryClaimResult> TryBeginRestoreAsync(Guid id, string hash, CancellationToken cancellationToken)
        {
            if (ThrowOnBegin)
            {
                throw new InvalidDataException("claim store corrupt");
            }
            if (ForcedBeginStatus.HasValue)
            {
                return Task.FromResult(new RecoveryClaimResult(ForcedBeginStatus.Value, null));
            }
            if (!_entries.TryGetValue(id, out var current) || current.Hash != hash)
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Missing, null));
            }
            if (current.State == State.Restoring)
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestoring, null));
            }
            if (current.State == State.Restored)
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestored, null));
            }
            var claimId = Guid.NewGuid();
            _entries[id] = new Entry(hash, State.Restoring, claimId);
            return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Claimed, claimId));
        }

        public Task MarkRestoredAsync(Guid id, string hash, Guid claimId, CancellationToken cancellationToken)
        {
            var current = _entries[id];
            if (current.Hash == hash && current.State == State.Restored)
            {
                MarkRestoredCount++;
                return Task.CompletedTask;
            }
            if (current.Hash != hash || current.State != State.Restoring || current.ClaimId != claimId)
            {
                throw new InvalidDataException("claim mismatch");
            }
            _entries[id] = new Entry(hash, State.Restored, null);
            MarkRestoredCount++;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid id, string hash, Guid claimId, CancellationToken cancellationToken)
        {
            if (ThrowOnRelease)
            {
                throw new IOException("claim release persistence failed");
            }
            if (!_entries.TryGetValue(id, out var current) || current.State == State.Available)
            {
                return Task.CompletedTask;
            }
            if (current.Hash != hash || current.State != State.Restoring || current.ClaimId != claimId)
            {
                throw new InvalidDataException("release mismatch");
            }
            _entries[id] = new Entry(hash, State.Available, null);
            ReleaseCount++;
            return Task.CompletedTask;
        }

        private enum State { Available, Restoring, Restored }
        private sealed record Entry(string Hash, State State, Guid? ClaimId);
    }
}
