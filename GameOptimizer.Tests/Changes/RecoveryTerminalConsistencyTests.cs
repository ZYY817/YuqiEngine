using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class RecoveryTerminalConsistencyTests
{
    [Fact]
    public async Task NoChangeReadback_PersistsTerminalReceiptAndRestartCannotReplay()
    {
        var fixture = new Fixture(EvidenceDirectory(nameof(NoChangeReadback_PersistsTerminalReceiptAndRestartCannotReplay)));
        var execution = await fixture.ExecuteAsync();
        fixture.Capability.State = "0";

        var noChange = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.NoChange, noChange.Outcome);
        Assert.Equal("restore.no-op", noChange.Operation.ReasonCode);
        Assert.Equal(0, fixture.Capability.RestoreCount);
        var persisted = await fixture.Audit.LoadAsync(noChange.Operation.ReceiptId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(ChangeOutcome.NoChange, persisted.Outcome);
        Assert.Equal("restore.no-op", persisted.ReasonCode);

        var restarted = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restarted.Outcome);
        Assert.Equal("restore.already-restored", restarted.Operation.ReasonCode);
        Assert.Equal(0, fixture.Capability.RestoreCount);
        Assert.Equal("0", fixture.Capability.State);
        Assert.NotNull(await fixture.Recovery.LoadAsync(execution.Operation.RecoveryId!.Value, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownReadback_PersistsRecoveryRequiredAndBlocksReplayAcrossInstances()
    {
        var fixture = new Fixture(EvidenceDirectory(nameof(UnknownReadback_PersistsRecoveryRequiredAndBlocksReplayAcrossInstances)));
        var execution = await fixture.ExecuteAsync();
        fixture.Capability.RestoreState = "external";

        var unknown = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, unknown.Outcome);
        Assert.Equal("restore.state-unknown", unknown.Operation.ReasonCode);
        Assert.Equal(1, fixture.Capability.RestoreCount);
        var persisted = await fixture.Audit.LoadAsync(unknown.Operation.ReceiptId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(ChangeOutcome.RecoveryRequired, persisted.Outcome);
        Assert.True(persisted.MutationOccurred);

        var driftedReplay = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Conflict, driftedReplay.Outcome);
        Assert.Equal("restore.state-drift", driftedReplay.Operation.ReasonCode);
        Assert.Equal(1, fixture.Capability.RestoreCount);

        fixture.Capability.State = "1";
        var targetReplay = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, targetReplay.Outcome);
        Assert.Equal("restore.already-restoring", targetReplay.Operation.ReasonCode);
        Assert.Equal(1, fixture.Capability.RestoreCount);
        Assert.NotNull(await fixture.Recovery.LoadAsync(execution.Operation.RecoveryId!.Value, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownReadback_WhenTerminalReceiptPersistenceFails_RestartStillCannotReplay()
    {
        var fixture = new Fixture(EvidenceDirectory(nameof(UnknownReadback_WhenTerminalReceiptPersistenceFails_RestartStillCannotReplay)));
        var execution = await fixture.ExecuteAsync();
        fixture.Capability.RestoreState = "external";
        fixture.Audit.FailNextSave();

        var persistenceFailed = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, persistenceFailed.Outcome);
        Assert.Equal("audit.persist-failed", persistenceFailed.Operation.ReasonCode);
        Assert.Equal(1, fixture.Capability.RestoreCount);
        Assert.Null(await fixture.Audit.LoadAsync(persistenceFailed.Operation.ReceiptId, CancellationToken.None));

        fixture.Capability.State = "1";
        var restarted = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restarted.Outcome);
        Assert.Equal("restore.already-restoring", restarted.Operation.ReasonCode);
        Assert.Equal(1, fixture.Capability.RestoreCount);
        Assert.NotNull(await fixture.Recovery.LoadAsync(execution.Operation.RecoveryId!.Value, CancellationToken.None));
    }

    [Fact]
    public async Task NoChangeReadback_WhenTerminalReceiptPersistenceFails_RestartUsesRestoredClaim()
    {
        var fixture = new Fixture(EvidenceDirectory(nameof(NoChangeReadback_WhenTerminalReceiptPersistenceFails_RestartUsesRestoredClaim)));
        var execution = await fixture.ExecuteAsync();
        fixture.Capability.State = "0";
        fixture.Audit.FailNextSave();

        var persistenceFailed = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.FailedNoChange, persistenceFailed.Outcome);
        Assert.Equal("audit.persist-failed", persistenceFailed.Operation.ReasonCode);
        Assert.Equal(0, fixture.Capability.RestoreCount);
        Assert.Null(await fixture.Audit.LoadAsync(persistenceFailed.Operation.ReceiptId, CancellationToken.None));

        var restarted = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restarted.Outcome);
        Assert.Equal("restore.already-restored", restarted.Operation.ReasonCode);
        Assert.Equal(0, fixture.Capability.RestoreCount);
        Assert.NotNull(await fixture.Recovery.LoadAsync(execution.Operation.RecoveryId!.Value, CancellationToken.None));
    }

    [Fact]
    public async Task NoChangeReadback_WhenJournalAndAmendedReceiptPersistenceFail_RestartStillCannotReplay()
    {
        var fixture = new Fixture(EvidenceDirectory(nameof(NoChangeReadback_WhenJournalAndAmendedReceiptPersistenceFail_RestartStillCannotReplay)));
        var execution = await fixture.ExecuteAsync();
        fixture.Capability.State = "0";
        fixture.Journal.FailNextAppend();
        fixture.Audit.FailAmendedSaveAfterCurrentReceipt();

        var terminalPersistenceFailed = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.FailedNoChange, terminalPersistenceFailed.Outcome);
        Assert.Equal("journal.terminal-persist-failed", terminalPersistenceFailed.Operation.ReasonCode);
        Assert.Equal(0, fixture.Capability.RestoreCount);
        var durableReceipt = await fixture.Audit.LoadAsync(terminalPersistenceFailed.Operation.ReceiptId, CancellationToken.None);
        Assert.NotNull(durableReceipt);
        Assert.Equal(ChangeOutcome.NoChange, durableReceipt.Outcome);

        var restarted = await fixture.CreateOrchestrator().RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restarted.Outcome);
        Assert.Equal("restore.already-restored", restarted.Operation.ReasonCode);
        Assert.Equal(0, fixture.Capability.RestoreCount);
        Assert.NotNull(await fixture.Recovery.LoadAsync(execution.Operation.RecoveryId!.Value, CancellationToken.None));
    }

    private sealed class Fixture
    {
        private readonly string _claimRoot;

        public Fixture(string claimRoot)
        {
            _claimRoot = claimRoot;
        }

        public TestCapability Capability { get; } = new();
        public MemoryRecoveryRepository Recovery { get; } = new();
        public MemoryAuditStore Audit { get; } = new();
        public MemoryJournal Journal { get; } = new();

        public ChangeOrchestrator CreateOrchestrator() =>
            new(
                [Capability],
                Recovery,
                Journal,
                Audit,
                TimeProvider.System,
                new InMemoryChangePlanStore(),
                new InProcessChangeResourceLockProvider(),
                null,
                new FileRecoveryClaimStore(_claimRoot),
                ChangeExecutionPolicy.Default,
                ChangeExecutionGateTestDouble.Allowed());

        public async Task<BatchReceipt> ExecuteAsync()
        {
            var orchestrator = CreateOrchestrator();
            var preparation = await orchestrator.PrepareAsync(
                new ChangeRequest(TestCapability.Id, TestCapability.Scope, "1"),
                CancellationToken.None);
            var plan = Assert.IsType<PreparedChangePlan>(preparation.Plan);
            var execution = await orchestrator.ExecuteAsync(
                plan,
                ConfirmationToken.ForPreparedPlan(plan),
                CancellationToken.None);
            Assert.Equal(ChangeOutcome.SucceededVerified, execution.Outcome);
            return execution;
        }
    }

    private sealed class TestCapability : IChangeCapability
    {
        private int _restoreCount;

        public const string Id = "test.recovery-terminal-consistency";
        public static TargetScope Scope { get; } = TargetScope.CurrentUser("test-user");
        public string State { get; set; } = "0";
        public string? RestoreState { get; set; }
        public int RestoreCount => Volatile.Read(ref _restoreCount);
        public CapabilityDescriptor Descriptor { get; } = new(
            Id,
            "1",
            ChangeRisk.LowReversible,
            [TargetScopeKind.CurrentUser],
            false,
            false,
            true,
            false,
            "test.recovery-terminal-consistency.resource");

        public Task<CapabilityAssessment> ProbeAsync(
            TargetScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilityAssessment(CapabilityApplicability.Applicable, true, "ok", "ok"));

        public Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(new StateObservation(
                scope,
                State,
                Fingerprint(State),
                DateTimeOffset.UtcNow,
                "memory",
                "authoritative"));

        public Task<PreparedCapabilityChange> PrepareAsync(
            StateObservation current,
            string targetPayload,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PreparedCapabilityChange(
                Id,
                "1",
                current.Scope,
                current.Payload,
                current.Fingerprint,
                targetPayload,
                Fingerprint(targetPayload),
                Descriptor.ResourceKey));

        public Task<PlatformExecutionResult> ExecuteAsync(
            PreparedCapabilityChange change,
            CancellationToken cancellationToken)
        {
            State = change.TargetPayload;
            return Task.FromResult(new PlatformExecutionResult(true, true, "ok", "ok"));
        }

        public Task<PlatformExecutionResult> RestoreAsync(
            RecoveryMaterial material,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _restoreCount);
            State = RestoreState ?? material.BeforePayload;
            return Task.FromResult(new PlatformExecutionResult(true, true, "ok", "ok"));
        }

        private static string Fingerprint(string value) => $"fp:{value}";
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
        private int _failNextSave;
        private int _saveCount;
        private int _failOnSaveCount;

        public void FailNextSave() => Interlocked.Exchange(ref _failNextSave, 1);

        public void FailAmendedSaveAfterCurrentReceipt() =>
            Interlocked.Exchange(ref _failOnSaveCount, Volatile.Read(ref _saveCount) + 2);

        public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
        {
            var saveCount = Interlocked.Increment(ref _saveCount);
            if (Interlocked.Exchange(ref _failNextSave, 0) == 1
                || saveCount == Volatile.Read(ref _failOnSaveCount))
            {
                throw new IOException("terminal audit persistence failed");
            }

            _receipts[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }

        public Task<OperationReceipt?> LoadAsync(Guid receiptId, CancellationToken cancellationToken) =>
            Task.FromResult(_receipts.GetValueOrDefault(receiptId));
    }

    private sealed class MemoryJournal : IChangeJournal
    {
        private int _failNextAppend;

        public void FailNextAppend() => Interlocked.Exchange(ref _failNextAppend, 1);

        public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _failNextAppend, 0) == 1)
            {
                throw new IOException("terminal journal persistence failed");
            }

            return Task.CompletedTask;
        }
    }

    private static string EvidenceDirectory(string testName) =>
        Path.Combine(AppContext.BaseDirectory, "recovery-terminal-consistency", testName, Guid.NewGuid().ToString("N"));
}
