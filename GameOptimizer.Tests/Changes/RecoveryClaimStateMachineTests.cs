using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class RecoveryClaimStateMachineTests
{
    [Fact]
    public async Task FileStore_AvailableRestoringRestored_IsDurableAcrossInstances()
    {
        var root = EvidenceDirectory(nameof(FileStore_AvailableRestoringRestored_IsDurableAcrossInstances));
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        var first = new FileRecoveryClaimStore(root);
        var second = new FileRecoveryClaimStore(root);

        await first.RegisterAvailableAsync(recoveryId, hash, CancellationToken.None);
        var claim = await first.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None);
        Assert.Equal(RecoveryClaimStatus.Claimed, claim.Status);
        Assert.NotNull(claim.ClaimId);
        Assert.Equal(
            RecoveryClaimStatus.AlreadyRestoring,
            (await second.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None)).Status);

        await first.MarkRestoredAsync(recoveryId, hash, claim.ClaimId.Value, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(root, $"{recoveryId:N}.restored")));
        Assert.False(File.Exists(Path.Combine(root, $"{recoveryId:N}.restoring")));
        await first.ReleaseAsync(recoveryId, hash, claim.ClaimId.Value, CancellationToken.None);

        Assert.Equal(
            RecoveryClaimStatus.AlreadyRestored,
            (await second.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task FileStore_ConcurrentRestoreClaimsAcrossInstances_AreSerializedAndDurable()
    {
        var root = EvidenceDirectory(nameof(FileStore_ConcurrentRestoreClaimsAcrossInstances_AreSerializedAndDurable));
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        var first = new FileRecoveryClaimStore(root);
        var second = new FileRecoveryClaimStore(root);
        await first.RegisterAvailableAsync(recoveryId, hash, CancellationToken.None);

        var results = await Task.WhenAll(
            first.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None),
            second.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None));

        Assert.Single(results, result => result.Status == RecoveryClaimStatus.Claimed);
        Assert.Single(results, result => result.Status == RecoveryClaimStatus.AlreadyRestoring);
        var claimId = results.Single(result => result.Status == RecoveryClaimStatus.Claimed).ClaimId!.Value;
        Assert.Equal(
            RecoveryClaimReadStatus.Restoring,
            (await second.ReadStateAsync(recoveryId, hash, CancellationToken.None)).Status);

        await first.MarkRestoredAsync(recoveryId, hash, claimId, CancellationToken.None);
        Assert.Equal(
            RecoveryClaimReadStatus.Restored,
            (await second.ReadStateAsync(recoveryId, hash, CancellationToken.None)).Status);
        Assert.Equal(
            RecoveryClaimStatus.AlreadyRestored,
            (await first.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task FileStore_RestoredMarkerRemainsTerminalWhenAvailabilityMarkerIsMissing()
    {
        var root = EvidenceDirectory(nameof(FileStore_RestoredMarkerRemainsTerminalWhenAvailabilityMarkerIsMissing));
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        var store = new FileRecoveryClaimStore(root);
        await store.RegisterAvailableAsync(recoveryId, hash, CancellationToken.None);
        var claim = await store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None);
        await store.MarkRestoredAsync(recoveryId, hash, claim.ClaimId!.Value, CancellationToken.None);
        File.Delete(Path.Combine(root, $"{recoveryId:N}.available"));

        Assert.Equal(
            RecoveryClaimStatus.AlreadyRestored,
            (await store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task FileStore_ReleaseAfterKnownZeroWrite_AllowsRetry()
    {
        var root = EvidenceDirectory(nameof(FileStore_ReleaseAfterKnownZeroWrite_AllowsRetry));
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        var store = new FileRecoveryClaimStore(root);
        await store.RegisterAvailableAsync(recoveryId, hash, CancellationToken.None);

        var firstClaim = await store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None);
        Assert.Equal(RecoveryClaimStatus.Claimed, firstClaim.Status);
        await store.ReleaseAsync(recoveryId, hash, firstClaim.ClaimId!.Value, CancellationToken.None);

        Assert.Equal(RecoveryClaimStatus.Claimed, (await store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task RestoreAcrossOrchestratorInstances_CallsPlatformOnceAndRejectsReplay()
    {
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestoreAcrossOrchestratorInstances_CallsPlatformOnceAndRejectsReplay)));
        harness.Capability.RestoreBehavior = async (material, _) =>
        {
            await Task.Delay(100);
            harness.Capability.State = material.BeforePayload;
            return new PlatformExecutionResult(true, true, "ok", "ok");
        };

        var first = harness.CreateOrchestrator();
        var second = harness.CreateOrchestrator();
        var request = new RestoreRequest(harness.ExecutionReceiptId);
        var results = await Task.WhenAll(
            first.RestoreAsync(request, CancellationToken.None),
            second.RestoreAsync(request, CancellationToken.None));

        Assert.Equal(1, harness.Capability.RestoreCount);
        Assert.Single(results, result => result.Outcome == ChangeOutcome.RestoredVerified);
        Assert.Single(results, result => result.Operation.ReasonCode is "restore.already-restoring" or "restore.already-restored");

        harness.Capability.State = "1";
        var replay = await first.RestoreAsync(request, CancellationToken.None);
        Assert.Equal(ChangeOutcome.Blocked, replay.Outcome);
        Assert.Equal("restore.already-restored", replay.Operation.ReasonCode);
        Assert.Equal(1, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestartedRestore_ReadsCurrentStateAndBlocksDriftWithoutConsumingClaim()
    {
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestartedRestore_ReadsCurrentStateAndBlocksDriftWithoutConsumingClaim)));
        harness.Capability.State = "external-change";
        var request = new RestoreRequest(harness.ExecutionReceiptId);

        var drifted = await harness.CreateOrchestrator().RestoreAsync(request, CancellationToken.None);

        Assert.Equal(ChangeOutcome.Conflict, drifted.Outcome);
        Assert.Equal("restore.state-drift", drifted.Operation.ReasonCode);
        Assert.Equal("external-change", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);

        harness.Capability.State = "1";
        var restored = await harness.CreateOrchestrator().RestoreAsync(request, CancellationToken.None);
        Assert.Equal(ChangeOutcome.RestoredVerified, restored.Outcome);
        Assert.Equal("0", harness.Capability.State);
        Assert.Equal(1, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreKnownZeroWriteFailure_ReleasesClaimForRetry()
    {
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestoreKnownZeroWriteFailure_ReleasesClaimForRetry)));
        harness.Capability.RestoreBehavior = (_, _) =>
            Task.FromResult(new PlatformExecutionResult(false, false, "platform.no-write", "no write"));
        var orchestrator = harness.CreateOrchestrator();
        var request = new RestoreRequest(harness.ExecutionReceiptId);

        var first = await orchestrator.RestoreAsync(request, CancellationToken.None);
        var second = await orchestrator.RestoreAsync(request, CancellationToken.None);

        Assert.Equal(ChangeOutcome.RestoreFailed, first.Outcome);
        Assert.Equal(ChangeOutcome.RestoreFailed, second.Outcome);
        Assert.Equal(2, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreFailureAfterReportedMutation_KeepsRecoveryRequiredAndBlocksReplay()
    {
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestoreFailureAfterReportedMutation_KeepsRecoveryRequiredAndBlocksReplay)));
        harness.Capability.RestoreBehavior = (_, _) =>
            Task.FromResult(new PlatformExecutionResult(false, true, "platform.partial", "mutation outcome is uncertain"));
        var orchestrator = harness.CreateOrchestrator();
        var request = new RestoreRequest(harness.ExecutionReceiptId);

        var first = await orchestrator.RestoreAsync(request, CancellationToken.None);
        var replay = await harness.CreateOrchestrator().RestoreAsync(request, CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, first.Outcome);
        Assert.Equal("restore.execution-mutation-unknown", first.Operation.ReasonCode);
        Assert.Equal(ChangeOutcome.Blocked, replay.Outcome);
        Assert.Equal("restore.already-restoring", replay.Operation.ReasonCode);
        Assert.Equal(1, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreUnknownFailure_KeepsClaimAndBlocksRetry()
    {
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestoreUnknownFailure_KeepsClaimAndBlocksRetry)));
        harness.Capability.RestoreBehavior = (_, _) => throw new IOException("unknown write state");
        var orchestrator = harness.CreateOrchestrator();
        var request = new RestoreRequest(harness.ExecutionReceiptId);

        var first = await orchestrator.RestoreAsync(request, CancellationToken.None);
        var second = await harness.CreateOrchestrator().RestoreAsync(request, CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, first.Outcome);
        Assert.Equal("restore.execution-exception", first.Operation.ReasonCode);
        Assert.Equal(ChangeOutcome.Blocked, second.Outcome);
        Assert.Equal("restore.already-restoring", second.Operation.ReasonCode);
        Assert.Equal(1, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreClaimTerminalWriteHalfCompleted_RestartKeepsRecoveryRequiredAndBlocksDowngrade()
    {
        var claims = new HalfCompletedRecoveryClaimStore();
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestoreClaimTerminalWriteHalfCompleted_RestartKeepsRecoveryRequiredAndBlocksDowngrade)),
            claimStore: claims);
        var request = new RestoreRequest(harness.ExecutionReceiptId);

        var first = await harness.CreateOrchestrator(claims).RestoreAsync(request, CancellationToken.None);
        var restarted = await harness.CreateOrchestrator(claims).RestoreAsync(request, CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, first.Outcome);
        Assert.Equal("restore.complete-state-failed", first.Operation.ReasonCode);
        Assert.Equal(ChangeOutcome.Blocked, restarted.Outcome);
        Assert.Equal("restore.already-restored", restarted.Operation.ReasonCode);
        Assert.Equal(1, harness.Capability.RestoreCount);
        Assert.Equal(HalfCompletedRecoveryClaimStore.ClaimState.Restored, claims.State);
        Assert.Contains(first.Operation.ReceiptId, harness.Audit.ReceiptIds);
        var persisted = await harness.Audit.LoadAsync(first.Operation.ReceiptId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(ChangeOutcome.RecoveryRequired, persisted.Outcome);
        Assert.NotNull(await harness.Recovery.LoadAsync(first.Operation.RecoveryId!.Value, CancellationToken.None));
    }

    [Theory]
    [InlineData("0", ChangeOutcome.RestoredVerified, "restore.verified-after-interruption")]
    [InlineData("1", ChangeOutcome.RestoreFailed, "restore.timeout-no-write")]
    [InlineData("other", ChangeOutcome.RecoveryRequired, "restore.state-unknown")]
    public async Task RestoreDeadline_WaitsForPlatformThenClassifiesAndPersistsClaimState(
        string finalState,
        ChangeOutcome expectedOutcome,
        string expectedReason)
    {
        var policy = ChangeExecutionPolicy.Default with
        {
            PlatformTimeout = TimeSpan.FromMilliseconds(30),
            VerificationTimeout = TimeSpan.FromMilliseconds(30),
            PersistenceTimeout = TimeSpan.FromMilliseconds(30)
        };
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestoreDeadline_WaitsForPlatformThenClassifiesAndPersistsClaimState)),
            policy: policy);
        harness.Capability.RestoreBehavior = async (material, token) =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2);
            }

            harness.Capability.State = finalState;
            throw new OperationCanceledException(token);
        };
        var request = new RestoreRequest(harness.ExecutionReceiptId);

        var first = await harness.CreateOrchestrator().RestoreAsync(request, CancellationToken.None);
        Assert.Equal(expectedOutcome, first.Outcome);
        Assert.Equal(expectedReason, first.Operation.ReasonCode);

        harness.Capability.State = "1";
        var replay = await harness.CreateOrchestrator().RestoreAsync(request, CancellationToken.None);
        if (finalState == "0")
        {
            Assert.Equal("restore.already-restored", replay.Operation.ReasonCode);
        }
        else if (finalState == "1")
        {
            Assert.NotEqual("restore.already-restoring", replay.Operation.ReasonCode);
        }
        else
        {
            Assert.Equal("restore.already-restoring", replay.Operation.ReasonCode);
        }
    }

    [Fact]
    public async Task RestoreMaterialWithoutClaimState_BlocksWithoutRecreatingReplayAuthority()
    {
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestoreMaterialWithoutClaimState_BlocksWithoutRecreatingReplayAuthority)),
            registerClaimDuringExecute: false);

        var restored = await harness.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(harness.ExecutionReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("restore.claim-missing", restored.Operation.ReasonCode);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreDeadlineWithReadbackFailure_KeepsClaimAndPersistsRecoveryRequired()
    {
        var policy = ChangeExecutionPolicy.Default with
        {
            PlatformTimeout = TimeSpan.FromMilliseconds(30),
            VerificationTimeout = TimeSpan.FromMilliseconds(30),
            PersistenceTimeout = TimeSpan.FromMilliseconds(30)
        };
        var harness = await Harness.CreateExecutedAsync(
            EvidenceDirectory(nameof(RestoreDeadlineWithReadbackFailure_KeepsClaimAndPersistsRecoveryRequired)),
            policy: policy);
        harness.Capability.RestoreBehavior = async (_, token) =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2);
            }

            harness.Capability.FailReadsAfterRestore = true;
            throw new OperationCanceledException(token);
        };
        var request = new RestoreRequest(harness.ExecutionReceiptId);

        var first = await harness.CreateOrchestrator().RestoreAsync(request, CancellationToken.None);
        harness.Capability.FailReadsAfterRestore = false;
        var replay = await harness.CreateOrchestrator().RestoreAsync(request, CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, first.Outcome);
        Assert.Equal("restore.verification-failed", first.Operation.ReasonCode);
        Assert.Contains(first.Operation.ReceiptId, harness.Audit.ReceiptIds);
        Assert.Equal("restore.already-restoring", replay.Operation.ReasonCode);
    }

    private sealed class Harness
    {
        private Harness(string claimRoot, ChangeExecutionPolicy? policy = null)
        {
            ClaimRoot = claimRoot;
            Policy = policy ?? ChangeExecutionPolicy.Default;
        }

        public string ClaimRoot { get; }
        public ChangeExecutionPolicy Policy { get; }
        public TestCapability Capability { get; } = new("0");
        public MemoryRecoveryRepository Recovery { get; } = new();
        public MemoryAuditStore Audit { get; } = new();
        public MemoryJournal Journal { get; } = new();
        public Guid ExecutionReceiptId { get; private set; }

        public static async Task<Harness> CreateExecutedAsync(
            string claimRoot,
            bool registerClaimDuringExecute = true,
            ChangeExecutionPolicy? policy = null,
            IRecoveryClaimStore? claimStore = null)
        {
            var harness = new Harness(claimRoot, policy);
            var orchestrator = registerClaimDuringExecute
                ? harness.CreateOrchestrator(claimStore)
                : harness.CreateOrchestrator(new DiscardingRecoveryClaimStore());
            var preparation = await orchestrator.PrepareAsync(
                new ChangeRequest(TestCapability.Id, TestCapability.Scope, "1"),
                CancellationToken.None);
            var plan = Assert.IsType<PreparedChangePlan>(preparation.Plan);
            var execution = await orchestrator.ExecuteAsync(
                plan,
                ConfirmationToken.ForPreparedPlan(plan),
                CancellationToken.None);
            Assert.Equal(ChangeOutcome.SucceededVerified, execution.Outcome);
            harness.ExecutionReceiptId = execution.Operation.ReceiptId;
            return harness;
        }

        public ChangeOrchestrator CreateOrchestrator(IRecoveryClaimStore? claimStore = null) =>
            new(
                [Capability],
                Recovery,
                Journal,
                Audit,
                TimeProvider.System,
                new InMemoryChangePlanStore(),
                new InProcessChangeResourceLockProvider(),
                null,
                claimStore ?? new FileRecoveryClaimStore(ClaimRoot),
                Policy,
                ChangeExecutionGateTestDouble.Allowed());
    }

    private sealed class TestCapability : IChangeCapability
    {
        public const string Id = "test.recovery-claim";
        public static TargetScope Scope { get; } = TargetScope.CurrentUser("test-user");
        private int _restoreCount;

        public TestCapability(string state)
        {
            State = state;
        }

        public string State { get; set; }
        public int RestoreCount => Volatile.Read(ref _restoreCount);
        public Func<RecoveryMaterial, CancellationToken, Task<PlatformExecutionResult>>? RestoreBehavior { get; set; }
        public bool FailReadsAfterRestore { get; set; }
        public CapabilityDescriptor Descriptor { get; } = new(
            Id, "1", ChangeRisk.LowReversible, [TargetScopeKind.CurrentUser], false, false, true, false, "test.recovery-resource");

        public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilityAssessment(CapabilityApplicability.Applicable, true, "ok", "ok"));

        public Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
        {
            if (FailReadsAfterRestore && RestoreCount > 0)
            {
                throw new IOException("restore readback failed");
            }

            return Task.FromResult(new StateObservation(scope, State, Fingerprint(State), DateTimeOffset.UtcNow, "memory", "authoritative"));
        }

        public Task<PreparedCapabilityChange> PrepareAsync(StateObservation current, string targetPayload, CancellationToken cancellationToken) =>
            Task.FromResult(new PreparedCapabilityChange(
                Id, "1", current.Scope, current.Payload, current.Fingerprint, targetPayload, Fingerprint(targetPayload), Descriptor.ResourceKey));

        public Task<PlatformExecutionResult> ExecuteAsync(PreparedCapabilityChange change, CancellationToken cancellationToken)
        {
            State = change.TargetPayload;
            return Task.FromResult(new PlatformExecutionResult(true, true, "ok", "ok"));
        }

        public async Task<PlatformExecutionResult> RestoreAsync(RecoveryMaterial material, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _restoreCount);
            if (RestoreBehavior is not null)
            {
                return await RestoreBehavior(material, cancellationToken);
            }

            State = material.BeforePayload;
            return new PlatformExecutionResult(true, true, "ok", "ok");
        }

        private static string Fingerprint(string state) => $"fp:{state}";
    }

    private sealed class MemoryRecoveryRepository : IRecoveryRepository
    {
        private readonly ConcurrentDictionary<Guid, RecoveryMaterial> _materials = new();
        public Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken)
        {
            _materials[material.RecoveryId] = material;
            return Task.CompletedTask;
        }

        public Task<RecoveryMaterial?> LoadAsync(Guid recoveryId, CancellationToken cancellationToken) =>
            Task.FromResult(_materials.GetValueOrDefault(recoveryId));
    }

    private sealed class MemoryAuditStore : IChangeAuditStore
    {
        private readonly ConcurrentDictionary<Guid, OperationReceipt> _receipts = new();
        public IReadOnlyCollection<Guid> ReceiptIds => _receipts.Keys.ToArray();
        public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
        {
            _receipts[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }

        public Task<OperationReceipt?> LoadAsync(Guid receiptId, CancellationToken cancellationToken) =>
            Task.FromResult(_receipts.GetValueOrDefault(receiptId));
    }

    private sealed class MemoryJournal : IChangeJournal
    {
        public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DiscardingRecoveryClaimStore : IRecoveryClaimStore
    {
        public Task RegisterAvailableAsync(Guid recoveryId, string integrityHash, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RecoveryClaimResult> TryBeginRestoreAsync(Guid recoveryId, string integrityHash, CancellationToken cancellationToken) =>
            Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Missing, null));

        public Task MarkRestoredAsync(Guid recoveryId, string integrityHash, Guid claimId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ReleaseAsync(Guid recoveryId, string integrityHash, Guid claimId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class HalfCompletedRecoveryClaimStore : IRecoveryClaimStore
    {
        private Guid? _claimId;
        private bool _failTerminalWrite = true;

        public ClaimState State { get; private set; }

        public Task RegisterAvailableAsync(Guid recoveryId, string integrityHash, CancellationToken cancellationToken)
        {
            if (State == ClaimState.None)
            {
                State = ClaimState.Available;
            }

            return Task.CompletedTask;
        }

        public Task<RecoveryClaimResult> TryBeginRestoreAsync(
            Guid recoveryId,
            string integrityHash,
            CancellationToken cancellationToken)
        {
            if (State == ClaimState.Restored)
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestored, null));
            }

            if (State == ClaimState.Restoring)
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestoring, null));
            }

            _claimId = Guid.NewGuid();
            State = ClaimState.Restoring;
            return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Claimed, _claimId));
        }

        public Task MarkRestoredAsync(
            Guid recoveryId,
            string integrityHash,
            Guid claimId,
            CancellationToken cancellationToken)
        {
            State = ClaimState.Restored;
            if (_failTerminalWrite)
            {
                _failTerminalWrite = false;
                throw new IOException("restored marker cleanup failed after terminal marker was durable");
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(
            Guid recoveryId,
            string integrityHash,
            Guid claimId,
            CancellationToken cancellationToken)
        {
            if (State == ClaimState.Restoring && _claimId == claimId)
            {
                State = ClaimState.Available;
                _claimId = null;
            }

            return Task.CompletedTask;
        }

        public enum ClaimState
        {
            None,
            Available,
            Restoring,
            Restored
        }
    }

    private static string EvidenceDirectory(string testName) =>
        Path.Combine(AppContext.BaseDirectory, "recovery-claim-evidence", testName, Guid.NewGuid().ToString("N"));
}
