using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Tests.Snapshot;

public sealed class SnapshotRestoreBatchStateMachineTests
{
    private const string Identity = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Target = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public async Task Service_boundary_requires_explicit_confirmation_for_the_same_plan()
    {
        var claims = new ClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var confirmationCalls = 0;
        var coordinator = Coordinator(
            claims,
            batches,
            (plan, _) =>
            {
                confirmationCalls++;
                Assert.Equal(Identity, plan.SnapshotIdentitySha256);
                return Task.FromResult(false);
            });
        var plan = await coordinator.PrepareAsync("snapshot_1", SnapshotRestoreKind.Normal, default);
        var writes = 0;

        var result = await coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(Verified(++writes)),
            Blocked,
            default);

        Assert.False(result.Success);
        Assert.Contains("显式确认", result.Message);
        Assert.Equal(1, confirmationCalls);
        Assert.Equal(0, writes);
        Assert.Equal(0, claims.BeginCount);
    }

    [Fact]
    public async Task Tampered_expired_and_replayed_plans_never_execute()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-13T00:00:00Z"));
        var claims = new ClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore(clock);
        var coordinator = Coordinator(claims, batches, Confirm, clock);
        var plan = await coordinator.PrepareAsync("snapshot_1", SnapshotRestoreKind.Normal, default);
        var writes = 0;

        var tampered = plan with
        {
            Steps = [plan.Steps[0] with { ReasonCode = "snapshot.tampered" }]
        };
        Assert.False((await coordinator.ExecuteAsync(
            tampered,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(Verified(++writes)),
            Blocked,
            default)).Success);

        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.False((await coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(Verified(++writes)),
            Blocked,
            default)).Success);

        clock.SetUtcNow(DateTimeOffset.Parse("2026-08-13T00:01:00Z"));
        var fresh = await coordinator.PrepareAsync("snapshot_2", SnapshotRestoreKind.Normal, default);
        Assert.True((await coordinator.ExecuteAsync(
            fresh,
            SnapshotRestoreKind.Normal,
            async _ =>
            {
                await AppendVerifiedReceiptsAsync(batches, fresh);
                return Verified(++writes);
            },
            Blocked,
            default)).Success);
        Assert.False((await coordinator.ExecuteAsync(
            fresh,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(Verified(++writes)),
            Blocked,
            default)).Success);

        Assert.Equal(1, writes);
        Assert.Equal(1, claims.MarkRestoredCount);
    }

    [Fact]
    public async Task Failure_partial_exception_and_cancel_never_mark_restored_or_release_unknown()
    {
        foreach (var mode in new[] { "failure", "partial", "compensation-failed", "exception", "cancel" })
        {
            var claims = new ClaimStore();
            var batches = new InMemorySnapshotRestoreBatchStore();
            var coordinator = Coordinator(claims, batches, Confirm);
            var plan = await coordinator.PrepareAsync("snapshot_" + mode, SnapshotRestoreKind.Normal, default);

            try
            {
                _ = await coordinator.ExecuteAsync(
                    plan,
                    SnapshotRestoreKind.Normal,
                    _ => mode switch
                    {
                        "failure" => Task.FromResult(Failed(SnapshotMutationStatus.None)),
                        "partial" => Task.FromResult(Failed(SnapshotMutationStatus.Known)),
                        "compensation-failed" => Task.FromResult(FailedCompensation()),
                        "cancel" => Task.FromException<SnapshotRestoreResult>(new OperationCanceledException()),
                        _ => Task.FromException<SnapshotRestoreResult>(new InvalidOperationException("boom"))
                    },
                    Blocked,
                    default);
            }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
            {
            }

            Assert.Equal(0, claims.MarkRestoredCount);
            if (mode == "failure")
            {
                Assert.Equal(1, claims.ReleaseCount);
                Assert.Equal(
                    SnapshotRestoreBatchState.Available,
                    (await batches.LoadAsync(plan.PlanId, plan.PlanSha256, default))!.State);
            }
            else
            {
                Assert.Equal(0, claims.ReleaseCount);
                Assert.Equal(
                    SnapshotRestoreBatchState.InterruptedManual,
                    (await batches.LoadAsync(plan.PlanId, plan.PlanSha256, default))!.State);
            }
        }
    }

    [Fact]
    public async Task Warning_result_is_not_verified_success_and_keeps_claim_for_manual_recovery()
    {
        var claims = new ClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var coordinator = Coordinator(claims, batches, Confirm);
        var plan = await coordinator.PrepareAsync("snapshot_warning", SnapshotRestoreKind.Normal, default);
        var result = Verified(1);
        result.Warnings.Add("readback mismatch");

        result = await coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(result),
            Blocked,
            default);

        Assert.Equal(SnapshotRestoreBatchState.InterruptedManual, result.BatchState);
        Assert.Equal(0, claims.MarkRestoredCount);
        Assert.Equal(0, claims.ReleaseCount);
    }

    [Fact]
    public async Task Verified_compensation_is_terminal_failed_no_change_not_restored_or_interrupted()
    {
        var claims = new ClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var coordinator = Coordinator(claims, batches, Confirm);
        var plan = await coordinator.PrepareAsync("snapshot_compensated", SnapshotRestoreKind.Normal, default);
        var compensated = Failed(SnapshotMutationStatus.Known);
        compensated.CompensationStatus = SnapshotCompensationStatus.VerifiedBeforeState;
        compensated.OutcomeReasonCode = "snapshot.failed-no-change";

        var result = await coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            async _ =>
            {
                var receiptId = Guid.NewGuid();
                var recoveryId = Guid.NewGuid();
                await batches.AppendReceiptAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    Receipt(plan.Steps[0].StepId, SnapshotRestoreStepState.Executing),
                    default);
                await batches.AppendReceiptAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    Receipt(plan.Steps[0].StepId, SnapshotRestoreStepState.RestoredVerified) with
                    {
                        ReceiptId = receiptId,
                        RecoveryId = recoveryId,
                        MutationStatus = SnapshotMutationStatus.Known
                    },
                    default);
                await batches.AppendReceiptAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    Receipt(plan.Steps[0].StepId, SnapshotRestoreStepState.Compensating) with
                    {
                        ReceiptId = receiptId,
                        RecoveryId = recoveryId,
                        MutationStatus = SnapshotMutationStatus.Known
                    },
                    default);
                await batches.AppendReceiptAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    Receipt(plan.Steps[0].StepId, SnapshotRestoreStepState.CompensatedVerified) with
                    {
                        ReceiptId = Guid.NewGuid(),
                        RecoveryId = recoveryId,
                        MutationStatus = SnapshotMutationStatus.Known
                    },
                    default);
                return compensated;
            },
            Blocked,
            default);

        Assert.False(result.Success);
        Assert.Equal(SnapshotRestoreBatchState.CompensatedVerified, result.BatchState);
        Assert.Equal(SnapshotCompensationStatus.VerifiedBeforeState, result.CompensationStatus);
        Assert.Equal("snapshot.failed-no-change", result.OutcomeReasonCode);
        Assert.Equal(0, claims.MarkRestoredCount);
        Assert.Equal(0, claims.ReleaseCount);
        Assert.Equal(
            SnapshotRestoreBatchState.CompensatedVerified,
            (await batches.LoadAsync(plan.PlanId, plan.PlanSha256, default))!.State);
    }

    [Fact]
    public async Task Concurrent_claim_allows_only_one_executor()
    {
        var claims = new ClaimStore();
        var batches = new InMemorySnapshotRestoreBatchStore();
        var coordinator = Coordinator(claims, batches, Confirm);
        var plan = await coordinator.PrepareAsync("snapshot_concurrent", SnapshotRestoreKind.Normal, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;

        var first = coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            async _ =>
            {
                Interlocked.Increment(ref writes);
                entered.SetResult();
                await release.Task;
                await AppendVerifiedReceiptsAsync(batches, plan);
                return Verified(1);
            },
            Blocked,
            default);
        await entered.Task;
        var second = await coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            _ => Task.FromResult(Verified(Interlocked.Increment(ref writes))),
            Blocked,
            default);
        release.SetResult();
        var firstResult = await first;

        Assert.True(firstResult.Success);
        Assert.False(second.Success);
        Assert.Equal(1, writes);
        Assert.Equal(1, claims.MarkRestoredCount);
    }

    [Fact]
    public async Task Durable_batch_reopens_in_restoring_state_after_crash_and_detects_tampering()
    {
        var root = Path.Combine(Path.GetTempPath(), "yuqi-snapshot-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plan = Plan("snapshot_crash");
            var first = new FileSnapshotRestoreBatchStore(root);
            await first.RegisterAsync(plan, default);
            await first.SetClaimingAsync(plan, default);
            await first.BeginAsync(plan, Guid.NewGuid(), default);
            await first.AppendReceiptAsync(
                plan.PlanId,
                plan.PlanSha256,
                Receipt(plan.Steps[0].StepId, SnapshotRestoreStepState.Executing),
                default);

            var restarted = new FileSnapshotRestoreBatchStore(root);
            var recovered = await restarted.LoadAsync(plan.PlanId, plan.PlanSha256, default);
            Assert.NotNull(recovered);
            Assert.Equal(SnapshotRestoreBatchState.Restoring, recovered.State);
            Assert.Single(recovered.Receipts);

            var path = Path.Combine(root, $"{plan.PlanId:N}.batch.json");
            var json = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, json.Replace(
                "snapshot.batch-restoring",
                "snapshot.batch-tampered",
                StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                restarted.LoadAsync(plan.PlanId, plan.PlanSha256, default));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Receipt_persistence_failure_leaves_claim_restoring_and_never_marks_success()
    {
        var claims = new ClaimStore();
        var inner = new InMemorySnapshotRestoreBatchStore();
        var batches = new FailingReceiptStore(inner);
        var coordinator = Coordinator(claims, batches, Confirm);
        var plan = await coordinator.PrepareAsync("snapshot_receipt_failure", SnapshotRestoreKind.Normal, default);

        await Assert.ThrowsAsync<IOException>(() => coordinator.ExecuteAsync(
            plan,
            SnapshotRestoreKind.Normal,
            async _ =>
            {
                await batches.AppendReceiptAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    Receipt(plan.Steps[0].StepId, SnapshotRestoreStepState.Executing),
                    default);
                return Verified(1);
            },
            Blocked,
            default));

        Assert.Equal(0, claims.MarkRestoredCount);
        Assert.Equal(0, claims.ReleaseCount);
        Assert.Equal(RecoveryClaimStatus.AlreadyRestoring, (await claims.TryBeginRestoreAsync(
            plan.PlanId,
            plan.PlanSha256,
            default)).Status);
    }

    private static SnapshotRestoreCoordinator Coordinator(
        ClaimStore claims,
        ISnapshotRestoreBatchStore batches,
        Func<SnapshotRestorePlan, CancellationToken, Task<bool>> confirmation,
        TimeProvider? timeProvider = null) =>
        new(
            new AllowGate(),
            (_, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new SnapshotRestorePreparation(Identity, [Step()]));
            },
            confirmation,
            claims,
            batches,
            timeProvider);

    private static Task<bool> Confirm(SnapshotRestorePlan _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    private static SnapshotRestoreResult Verified(int count) => new()
    {
        Success = true,
        RestoredCount = count,
        MutationStatus = SnapshotMutationStatus.Known,
        OutcomeReasonCode = "snapshot.restored-verified"
    };

    private static async Task AppendVerifiedReceiptsAsync(
        ISnapshotRestoreBatchStore batches,
        SnapshotRestorePlan plan)
    {
        await batches.AppendReceiptAsync(
            plan.PlanId,
            plan.PlanSha256,
            Receipt(plan.Steps[0].StepId, SnapshotRestoreStepState.Executing),
            default);
        await batches.AppendReceiptAsync(
            plan.PlanId,
            plan.PlanSha256,
            Receipt(plan.Steps[0].StepId, SnapshotRestoreStepState.RestoredVerified) with
            {
                ReceiptId = Guid.NewGuid(),
                RecoveryId = Guid.NewGuid(),
                MutationStatus = SnapshotMutationStatus.Known
            },
            default);
    }

    private static SnapshotRestoreResult Failed(SnapshotMutationStatus mutation) => new()
    {
        Success = false,
        FailedCount = 1,
        MutationStatus = mutation,
        OutcomeReasonCode = "snapshot.failed",
        Message = "failed"
    };

    private static SnapshotRestoreResult FailedCompensation() => new()
    {
        Success = false,
        FailedCount = 1,
        MutationStatus = SnapshotMutationStatus.Unknown,
        CompensationStatus = SnapshotCompensationStatus.FailedOrUnknown,
        OutcomeReasonCode = "snapshot.compensation-failed",
        Message = "compensation failed"
    };

    private static SnapshotRestoreResult Blocked(string message) => new()
    {
        Success = false,
        OutcomeReasonCode = "snapshot.blocked",
        Message = message
    };

    private static SnapshotRestoreStepPlan Step() => new(
        "typed:test.capability",
        SnapshotRestoreResourceKind.TypedCapability,
        "test.capability",
        Target,
        true,
        "snapshot.typed-owner");

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

    private static SnapshotRestoreStepReceipt Receipt(
        string stepId,
        SnapshotRestoreStepState state) =>
        new(
            0,
            stepId,
            "test.capability",
            state,
            Guid.Empty,
            null,
            SnapshotMutationStatus.None,
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

    private sealed class ClaimStore : IRecoveryClaimStore
    {
        private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
        public int BeginCount;
        public int MarkRestoredCount;
        public int ReleaseCount;

        public Task RegisterAvailableAsync(Guid id, string hash, CancellationToken cancellationToken)
        {
            if (!_entries.TryAdd(id, new Entry(hash, State.Available, null)))
            {
                throw new InvalidOperationException("duplicate");
            }
            return Task.CompletedTask;
        }

        public Task<RecoveryClaimResult> TryBeginRestoreAsync(Guid id, string hash, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref BeginCount);
            while (true)
            {
                if (!_entries.TryGetValue(id, out var current))
                    return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Missing, null));
                if (current.Hash != hash)
                    return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.IntegrityMismatch, null));
                if (current.State == State.Restoring)
                    return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestoring, null));
                if (current.State == State.Restored)
                    return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestored, null));
                var claim = Guid.NewGuid();
                if (_entries.TryUpdate(id, new Entry(hash, State.Restoring, claim), current))
                    return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Claimed, claim));
            }
        }

        public Task MarkRestoredAsync(Guid id, string hash, Guid claimId, CancellationToken cancellationToken)
        {
            var current = _entries[id];
            Assert.Equal(new Entry(hash, State.Restoring, claimId), current);
            _entries[id] = new Entry(hash, State.Restored, null);
            Interlocked.Increment(ref MarkRestoredCount);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid id, string hash, Guid claimId, CancellationToken cancellationToken)
        {
            var current = _entries[id];
            Assert.Equal(new Entry(hash, State.Restoring, claimId), current);
            _entries[id] = new Entry(hash, State.Available, null);
            Interlocked.Increment(ref ReleaseCount);
            return Task.CompletedTask;
        }

        private enum State { Available, Restoring, Restored }
        private sealed record Entry(string Hash, State State, Guid? ClaimId);
    }

    private sealed class FailingReceiptStore(ISnapshotRestoreBatchStore inner) : ISnapshotRestoreBatchStore
    {
        public Task RegisterAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken) =>
            inner.RegisterAsync(plan, cancellationToken);
        public Task SetClaimingAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken) =>
            inner.SetClaimingAsync(plan, cancellationToken);
        public Task BeginAsync(SnapshotRestorePlan plan, Guid claimId, CancellationToken cancellationToken) =>
            inner.BeginAsync(plan, claimId, cancellationToken);
        public Task AppendReceiptAsync(Guid planId, string planSha256, SnapshotRestoreStepReceipt receipt, CancellationToken cancellationToken) =>
            throw new IOException("receipt persistence failed");
        public Task SetStateAsync(Guid planId, string planSha256, SnapshotRestoreBatchState state, SnapshotMutationStatus mutationStatus, string reasonCode, CancellationToken cancellationToken) =>
            inner.SetStateAsync(planId, planSha256, state, mutationStatus, reasonCode, cancellationToken);
        public Task<SnapshotRestoreBatchDocument?> LoadAsync(Guid planId, string planSha256, CancellationToken cancellationToken) =>
            inner.LoadAsync(planId, planSha256, cancellationToken);
        public Task<IReadOnlyList<SnapshotRestoreBatchIdentity>> EnumeratePendingAsync(CancellationToken cancellationToken) =>
            inner.EnumeratePendingAsync(cancellationToken);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan value) => _now += value;
        internal void SetUtcNow(DateTimeOffset value) => _now = value;
    }
}
