using System.Diagnostics;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.ProcessTuning;

namespace GameOptimizer.Tests.ProcessTuning;

/// <summary>
/// These tests use an in-memory platform only. They never open or mutate a user process.
/// Windows adapter validation is intentionally a separate isolated-VM gate.
/// </summary>
public sealed class ProcessTuningOwnerTests
{
    [Fact]
    public async Task Affinity_SucceedsWithDurableRecovery_AndRestoresOnlyExactTargetState()
    {
        var harness = new Harness();

        var result = await harness.Owner.RunInteractiveAsync(ProcessTuningRequest.Affinity(4242, 3));

        Assert.True(
            result.Kind == ProcessTuningResultKind.SucceededVerified,
            $"Expected SucceededVerified, actual {result.Kind}: {result.ReasonCode} - {result.Message}");
        var receipt = Assert.IsType<OperationReceipt>(result.Receipt);
        Assert.Single(harness.Recovery.Materials);
        Assert.Equal(3, harness.Platform.State.AffinityMask);

        var restored = await harness.Owner.RestoreAsync(new RestoreRequest(receipt.ReceiptId));

        Assert.Equal(ProcessTuningResultKind.RestoredVerified, restored.Kind);
        Assert.Equal(1, harness.Platform.State.AffinityMask);
    }

    [Fact]
    public async Task Execute_WhenPidIdentityOrTopologyDrifts_ConflictsBeforeMutation()
    {
        var harness = new Harness();
        var prepared = await harness.Owner.PrepareAsync(ProcessTuningRequest.Priority(4242, ProcessPriorityClass.High));
        var plan = Assert.IsType<PreparedChangePlan>(prepared.Plan);
        var confirmation = await harness.Confirmation.RequestAsync(plan, "test", "test", CancellationToken.None);
        Assert.NotNull(confirmation);

        harness.Platform.State = harness.Platform.State with
        {
            Identity = harness.Platform.State.Identity with { TopologyFingerprint = "topology-changed" }
        };

        var result = await harness.Owner.ExecuteAsync(plan, confirmation!, CancellationToken.None);

        Assert.Equal(ProcessTuningResultKind.Conflict, result.Kind);
        Assert.Equal(0, harness.Platform.ApplyCount);
    }

    [Fact]
    public async Task Execute_WhenProcessExited_IsNotApplicableWithoutMutation()
    {
        var harness = new Harness();
        var prepared = await harness.Owner.PrepareAsync(ProcessTuningRequest.Affinity(4242, 3));
        var plan = Assert.IsType<PreparedChangePlan>(prepared.Plan);
        var confirmation = await harness.Confirmation.RequestAsync(plan, "test", "test", CancellationToken.None);
        harness.Platform.Exited = true;

        var result = await harness.Owner.ExecuteAsync(plan, confirmation!, CancellationToken.None);

        Assert.Equal(ProcessTuningResultKind.NotApplicable, result.Kind);
        Assert.Equal(0, harness.Platform.ApplyCount);
    }

    [Fact]
    public async Task Prepare_WhenProtectedOrPermissionDenied_BlocksBeforeRead()
    {
        var harness = new Harness();
        harness.Platform.Assessment = new CapabilityAssessment(
            CapabilityApplicability.NotApplicable,
            false,
            "process.protected",
            "protected test process");

        var result = await harness.Owner.PrepareAsync(ProcessTuningRequest.Affinity(4242, 3));

        Assert.Equal(ProcessTuningResultKind.Blocked, result.Kind);
        Assert.Equal(0, harness.Platform.ReadCount);
        Assert.Equal(0, harness.Platform.ApplyCount);
    }

    [Fact]
    public async Task IdealProcessor_StaysBlockedBecauseReadbackAndRestoreAreNotReliable()
    {
        var harness = new Harness();

        var result = await harness.Owner.PrepareAsync(ProcessTuningRequest.IdealProcessor(4242, 1));

        Assert.Equal(ProcessTuningResultKind.Blocked, result.Kind);
        Assert.Equal("ideal-processor.readback-unavailable", result.ReasonCode);
        Assert.Equal(0, harness.Platform.ApplyCount);
    }

    [Fact]
    public async Task ConfirmationReplay_AndReadbackMismatch_DoNotReportSuccess()
    {
        var harness = new Harness();
        var prepared = await harness.Owner.PrepareAsync(ProcessTuningRequest.Priority(4242, ProcessPriorityClass.High));
        var plan = Assert.IsType<PreparedChangePlan>(prepared.Plan);
        var confirmation = await harness.Confirmation.RequestAsync(plan, "test", "test", CancellationToken.None);
        Assert.NotNull(confirmation);
        harness.Platform.ApplyWithoutReadback = true;

        var first = await harness.Owner.ExecuteAsync(plan, confirmation!, CancellationToken.None);
        var replay = await harness.Owner.ExecuteAsync(plan, confirmation!, CancellationToken.None);

        Assert.NotEqual(ProcessTuningResultKind.SucceededVerified, first.Kind);
        Assert.Equal(ProcessTuningResultKind.Conflict, replay.Kind);
        Assert.Equal(1, harness.Platform.ApplyCount);
    }

    [Fact]
    public async Task Execute_EveryFrozenIdentityComponentDrift_ConflictsBeforeMutation()
    {
        (string Name, Func<ProcessIdentity, ProcessIdentity> Mutate)[] mutations =
        [
            ("pid", identity => identity with { ProcessId = identity.ProcessId + 1 }),
            ("creation", identity => identity with { CreationTimeUtcTicks = identity.CreationTimeUtcTicks + 1 }),
            ("executable", identity => identity with { ExecutableName = "replacement.exe" }),
            ("hash", identity => identity with { ExecutableHash = new string('B', 64) }),
            ("hash-availability", identity => identity with { ExecutableHash = null, ExecutableHashAvailable = false }),
            ("session", identity => identity with { SessionId = identity.SessionId + 1 }),
            ("user", identity => identity with { UserSecurityIdentifier = "S-1-5-21-other" }),
            ("architecture", identity => identity with { Architecture = "arm64" }),
            ("topology", identity => identity with { TopologyFingerprint = "groups:1;logical:4" })
        ];

        foreach (var mutation in mutations)
        {
            var harness = new Harness();
            var prepared = await harness.Owner.PrepareAsync(ProcessTuningRequest.Priority(4242, ProcessPriorityClass.High));
            var plan = Assert.IsType<PreparedChangePlan>(prepared.Plan);
            var confirmation = await harness.Confirmation.RequestAsync(plan, "test", mutation.Name, CancellationToken.None);
            Assert.NotNull(confirmation);
            harness.Platform.State = harness.Platform.State with
            {
                Identity = mutation.Mutate(harness.Platform.State.Identity)
            };

            var result = await harness.Owner.ExecuteAsync(plan, confirmation!, CancellationToken.None);

            Assert.Equal(ProcessTuningResultKind.Conflict, result.Kind);
            Assert.Equal(0, harness.Platform.ApplyCount);
        }
    }

    [Fact]
    public async Task Cancellation_IsExplicitBeforePrepareAndExecute_WithoutMutation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var prepareHarness = new Harness();
        var prepare = await prepareHarness.Owner.PrepareAsync(
            ProcessTuningRequest.Affinity(4242, 3),
            cancelled.Token);
        Assert.Equal(ProcessTuningResultKind.Cancelled, prepare.Kind);
        Assert.Equal(0, prepareHarness.Platform.ApplyCount);

        var executeHarness = new Harness();
        var prepared = await executeHarness.Owner.PrepareAsync(ProcessTuningRequest.Affinity(4242, 3));
        var plan = Assert.IsType<PreparedChangePlan>(prepared.Plan);
        var confirmation = await executeHarness.Confirmation.RequestAsync(plan, "test", "test", CancellationToken.None);
        Assert.NotNull(confirmation);

        var execute = await executeHarness.Owner.ExecuteAsync(plan, confirmation!, cancelled.Token);
        Assert.Equal(ProcessTuningResultKind.Cancelled, execute.Kind);
        Assert.Equal(0, executeHarness.Platform.ApplyCount);
    }

    [Fact]
    public async Task Restore_WhenIdentityDrifts_ConflictsAndPreservesCurrentTarget()
    {
        var harness = new Harness();
        var applied = await harness.Owner.RunInteractiveAsync(ProcessTuningRequest.Affinity(4242, 3));
        var receipt = Assert.IsType<OperationReceipt>(applied.Receipt);
        harness.Platform.State = harness.Platform.State with
        {
            Identity = harness.Platform.State.Identity with { CreationTimeUtcTicks = 101 }
        };

        var restored = await harness.Owner.RestoreAsync(new RestoreRequest(receipt.ReceiptId));

        Assert.Equal(ProcessTuningResultKind.Conflict, restored.Kind);
        Assert.Equal(3, harness.Platform.State.AffinityMask);
    }

    [Fact]
    public async Task PlatformTimeout_IsExplicitAndDoesNotMutateState()
    {
        var policy = new ChangeExecutionPolicy(
            "bounded-platform.v1",
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50));
        var harness = new Harness(policy);
        harness.Platform.WaitForApplyCancellation = true;

        var result = await harness.Owner.RunInteractiveAsync(ProcessTuningRequest.Affinity(4242, 3));

        Assert.Equal(ProcessTuningResultKind.Failed, result.Kind);
        Assert.Contains("timeout", result.ReasonCode, StringComparison.Ordinal);
        Assert.Equal(1, harness.Platform.State.AffinityMask);
        Assert.Equal(0, harness.Platform.MutationCount);
    }

    [Theory]
    [InlineData("platform.win10-build-unsupported", CapabilityApplicability.Unsupported, ProcessTuningResultKind.Blocked, "platform.win10-build-unsupported")]
    [InlineData("platform.win11-build-unsupported", CapabilityApplicability.Unsupported, ProcessTuningResultKind.Blocked, "platform.win11-build-unsupported")]
    [InlineData("topology.processor-group-unsupported", CapabilityApplicability.Unsupported, ProcessTuningResultKind.Blocked, "topology.processor-group-unsupported")]
    [InlineData("process.wow64-unsupported", CapabilityApplicability.Unsupported, ProcessTuningResultKind.Blocked, "process.wow64-unsupported")]
    [InlineData("process.ppl", CapabilityApplicability.NotApplicable, ProcessTuningResultKind.NotApplicable, "process.ppl")]
    [InlineData("process.permission-denied", CapabilityApplicability.Applicable, ProcessTuningResultKind.Blocked, "permission.denied")]
    [InlineData("process.exited", CapabilityApplicability.NotApplicable, ProcessTuningResultKind.NotApplicable, "process.exited")]
    public async Task F029ToF032_PlatformMatrix_IsFailClosedBeforeMutation(
        string reasonCode,
        CapabilityApplicability applicability,
        ProcessTuningResultKind expectedKind,
        string expectedReasonCode)
    {
        var harness = new Harness();
        harness.Platform.Assessment = new CapabilityAssessment(
            applicability,
            false,
            reasonCode,
            "contract matrix row");

        var result = await harness.Owner.PrepareAsync(ProcessTuningRequest.Affinity(4242, 3));

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
        Assert.Equal(0, harness.Platform.ApplyCount);
        Assert.Equal(0, harness.Platform.MutationCount);
    }

    [Theory]
    [InlineData("creation-time", 101L, "controlled-test.exe")]
    [InlineData("pid-reuse-executable", 100L, "replacement.exe")]
    public async Task F030AndF032_PidReuseIdentityDrift_IsConflictBeforeWrite(
        string _,
        long creationTimeUtcTicks,
        string executableName)
    {
        var harness = new Harness();
        var prepared = await harness.Owner.PrepareAsync(ProcessTuningRequest.Affinity(4242, 3));
        var plan = Assert.IsType<PreparedChangePlan>(prepared.Plan);
        var confirmation = await harness.Confirmation.RequestAsync(plan, "test", "pid-reuse", CancellationToken.None);
        Assert.NotNull(confirmation);

        harness.Platform.State = harness.Platform.State with
        {
            Identity = harness.Platform.State.Identity with
            {
                CreationTimeUtcTicks = creationTimeUtcTicks,
                ExecutableName = executableName
            }
        };

        var result = await harness.Owner.ExecuteAsync(plan, confirmation!, CancellationToken.None);

        Assert.Equal(ProcessTuningResultKind.Conflict, result.Kind);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, harness.Platform.ApplyCount);
        Assert.Equal(0, harness.Platform.MutationCount);
    }

    [Fact]
    public async Task F030AndF032_RestoreAfterProcessExit_IsNotApplicableAndNeverReportsSuccess()
    {
        var harness = new Harness();
        var applied = await harness.Owner.RunInteractiveAsync(ProcessTuningRequest.Affinity(4242, 3));
        var receipt = Assert.IsType<OperationReceipt>(applied.Receipt);
        harness.Platform.Exited = true;

        var restored = await harness.Owner.RestoreAsync(new RestoreRequest(receipt.ReceiptId));

        Assert.Equal(ProcessTuningResultKind.NotApplicable, restored.Kind);
        Assert.NotEqual(ProcessTuningResultKind.RestoredVerified, restored.Kind);
        Assert.Equal(3, harness.Platform.State.AffinityMask);
    }

    [Theory]
    [InlineData(0, false, "")]
    [InlineData(4242, true, "not-a-sha256")]
    [InlineData(4242, false, "hash-present-without-availability")]
    public void F030AndF032_ProcessIdentity_InvalidIdentityIsNeverUsable(
        int processId,
        bool hashAvailable,
        string hash)
    {
        var identity = new ProcessIdentity(
            processId,
            100,
            "controlled-test.exe",
            string.IsNullOrEmpty(hash) ? null : hash,
            hashAvailable,
            1,
            "S-1-5-21-test",
            "x64",
            "groups:1;logical:8");

        Assert.False(identity.IsUsable);
    }

    [Fact]
    public async Task ActiveReceipt_BlocksSecondMutationForSamePid_UntilVerifiedRestore()
    {
        var harness = new Harness();
        var first = await harness.Owner.RunInteractiveAsync(ProcessTuningRequest.Affinity(4242, 3));
        var receipt = Assert.IsType<OperationReceipt>(first.Receipt);

        var blocked = await harness.Owner.RunInteractiveAsync(ProcessTuningRequest.Priority(4242, ProcessPriorityClass.High));
        Assert.Equal(ProcessTuningResultKind.Conflict, blocked.Kind);
        Assert.Equal("process.active-receipt", blocked.ReasonCode);

        var restored = await harness.Owner.RestoreAsync(new RestoreRequest(receipt.ReceiptId));
        Assert.Equal(ProcessTuningResultKind.RestoredVerified, restored.Kind);

        var afterRestore = await harness.Owner.RunInteractiveAsync(ProcessTuningRequest.Priority(4242, ProcessPriorityClass.High));
        Assert.Equal(ProcessTuningResultKind.SucceededVerified, afterRestore.Kind);
    }

    private sealed class Harness
    {
        public Harness(ChangeExecutionPolicy? executionPolicy = null)
        {
            Platform = new FakePlatform();
            Confirmation = new ChangeConfirmationAuthority(new AllowPrompt());
            var capabilities = new IChangeCapability[]
            {
                new ProcessTuningCapability(ProcessTuningOperation.Affinity, Platform),
                new ProcessTuningCapability(ProcessTuningOperation.Priority, Platform),
                new ProcessTuningCapability(ProcessTuningOperation.IdealProcessor, Platform)
            };
            Orchestrator = new ChangeOrchestrator(
                capabilities,
                Recovery,
                new MemoryJournal(),
                new MemoryAudit(),
                TimeProvider.System,
                confirmationAuthority: Confirmation,
                recoveryClaimStore: new InMemoryRecoveryClaimStore(),
                executionPolicy: executionPolicy,
                executionGate: ChangeExecutionGateTestDouble.Allowed());
            Owner = new ProcessTuningOwner(Orchestrator, Confirmation);
        }

        public FakePlatform Platform { get; }
        public MemoryRecovery Recovery { get; } = new();
        public ChangeConfirmationAuthority Confirmation { get; }
        public ChangeOrchestrator Orchestrator { get; }
        public ProcessTuningOwner Owner { get; }
    }

    private sealed class FakePlatform : IProcessTuningPlatform
    {
        public FakePlatform()
        {
            var identity = new ProcessIdentity(
                4242,
                100,
                "controlled-test.exe",
                new string('A', 64),
                true,
                1,
                "S-1-5-21-test",
                "x64",
                "groups:1;logical:8");
            State = new ProcessTuningState(identity, 1, ProcessPriorityClass.Normal, null, false);
        }

        public ProcessTuningState State { get; set; }
        public CapabilityAssessment Assessment { get; set; } = new(CapabilityApplicability.Applicable, true, "ok", "ok");
        public bool Exited { get; set; }
        public bool ApplyWithoutReadback { get; set; }
        public bool WaitForApplyCancellation { get; set; }
        public int ReadCount { get; private set; }
        public int ApplyCount { get; private set; }
        public int MutationCount { get; private set; }

        public Task<CapabilityAssessment> ProbeAsync(int processId, ProcessTuningOperation operation, ChangeIntent intent, CancellationToken cancellationToken)
        {
            _ = processId;
            if (Exited)
            {
                return Task.FromResult(new CapabilityAssessment(CapabilityApplicability.NotApplicable, false, "process.exited", "controlled process exited"));
            }

            if (operation == ProcessTuningOperation.IdealProcessor)
            {
                return Task.FromResult(new CapabilityAssessment(CapabilityApplicability.NotApplicable, false, "ideal-processor.readback-unavailable", "blocked"));
            }

            return Task.FromResult(Assessment);
        }

        public Task<ProcessTuningPlatformReadResult> ReadAsync(int processId, CancellationToken cancellationToken)
        {
            _ = processId;
            ReadCount++;
            return Task.FromResult(Exited
                ? ProcessTuningPlatformReadResult.Failure("process.exited", "controlled process exited")
                : ProcessTuningPlatformReadResult.Success(State));
        }

        public async Task<PlatformExecutionResult> ApplyAsync(ProcessTuningOperation operation, ProcessTuningState expectedCurrent, ProcessTuningState desired, CancellationToken cancellationToken)
        {
            ApplyCount++;
            if (WaitForApplyCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (Exited)
            {
                return new PlatformExecutionResult(false, false, "process.exited", "controlled process exited");
            }

            if (State != expectedCurrent)
            {
                return new PlatformExecutionResult(false, false, "process.identity-drift", "state drifted");
            }

            if (!ApplyWithoutReadback)
            {
                State = desired;
                MutationCount++;
            }

            return new PlatformExecutionResult(true, true, "ok", "ok");
        }

        public Task<PlatformExecutionResult> RestoreAsync(ProcessTuningOperation operation, string expectedTargetFingerprint, ProcessTuningState original, CancellationToken cancellationToken)
        {
            if (Exited)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "process.exited", "controlled process exited"));
            }

            if (ProcessTuningStateFingerprint.Hash(State) != expectedTargetFingerprint)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "restore.target-drift", "current state is not durable target"));
            }

            State = original;
            return Task.FromResult(new PlatformExecutionResult(true, true, "ok", "ok"));
        }
    }

    private sealed class AllowPrompt : IChangeConfirmationPrompt
    {
        public Task<bool> ConfirmAsync(ChangeConfirmationPrompt prompt, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class MemoryRecovery : IRecoveryRepository
    {
        public Dictionary<Guid, RecoveryMaterial> Materials { get; } = [];
        public Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken)
        {
            Materials[material.RecoveryId] = material;
            return Task.CompletedTask;
        }

        public Task<RecoveryMaterial?> LoadAsync(Guid recoveryId, CancellationToken cancellationToken) =>
            Task.FromResult(Materials.GetValueOrDefault(recoveryId));
    }

    private sealed class MemoryJournal : IChangeJournal
    {
        public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemoryAudit : IChangeAuditStore
    {
        private readonly Dictionary<Guid, OperationReceipt> _receipts = [];
        public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
        {
            _receipts[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }

        public Task<OperationReceipt?> LoadAsync(Guid receiptId, CancellationToken cancellationToken) =>
            Task.FromResult(_receipts.GetValueOrDefault(receiptId));
    }
}
