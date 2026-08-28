using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class ChangeOrchestratorTests
{
    private static readonly TargetScope Scope = TargetScope.CurrentUser("test-user");

    [Fact]
    public async Task ExecuteAsync_PersistsRecoveryAndVerifiesResult()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.SucceededVerified, receipt.Outcome);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(1, harness.Capability.ExecuteCount);
        Assert.Single(harness.Recovery.Materials);
        var material = Assert.Single(harness.Recovery.Materials.Values);
        Assert.Equal(receipt.Operation.ReceiptId, material.ReceiptId);
        Assert.Single(harness.Audit.Receipts);
    }

    [Fact]
    public async Task PrepareAsync_WhenTargetAlreadyMatches_ReturnsNoChange()
    {
        var harness = new Harness("1");

        var result = await harness.Orchestrator.PrepareAsync(
            new ChangeRequest(TestCapability.Id, Scope, "1"),
            CancellationToken.None);

        Assert.Equal(ChangePreparationStatus.NoChange, result.Status);
        Assert.Null(result.Plan);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task PrepareAsync_WhenPermissionIsMissing_BlocksBeforeReadOrWrite()
    {
        var harness = new Harness("0");
        harness.Capability.Assessment = new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            false,
            "permission.denied",
            "permission required");

        var result = await harness.Orchestrator.PrepareAsync(
            new ChangeRequest(TestCapability.Id, Scope, "1"),
            CancellationToken.None);

        Assert.Equal(ChangePreparationStatus.Blocked, result.Status);
        Assert.Equal("permission.denied", result.ReasonCode);
        Assert.Equal(0, harness.Capability.ReadCount);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecoveryPersistenceFails_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Recovery.SaveException = new IOException("store unavailable");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("recovery.persist-failed", receipt.Operation.ReasonCode);
        Assert.Equal("0", harness.Capability.State);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPermissionChangesAfterPrepare_DeniesAndInvalidatesPlan()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Capability.Assessment = new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            false,
            "permission.denied",
            "permission revoked");

        var denied = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Denied, denied.Outcome);
        Assert.Equal("permission.denied", denied.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
        Assert.Equal("0", harness.Capability.State);

        Assert.Single(harness.Audit.Receipts);
        harness.Capability.Assessment = TestCapability.AllowedAssessment;
        var retried = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, retried.Outcome);
        Assert.Equal("plan.replayed", retried.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecoveryReadbackIsCorrupted_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Recovery.LoadTransform = material => material with { BeforePayload = "corrupted" };

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("recovery.verify-failed", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPlatformReportsNoMutation_ReturnsFailedNoChange()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Capability.ExecuteBehavior = _ =>
            Task.FromResult(new PlatformExecutionResult(false, false, "platform.failed", "write rejected"));

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.FailedNoChange, receipt.Outcome);
        Assert.False(receipt.Operation.MutationOccurred);
        Assert.Equal("0", harness.Capability.State);
    }

    [Fact]
    public async Task ExecuteAsync_WhenReadbackDoesNotMatch_DoesNotReportSuccess()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Capability.ExecuteBehavior = _ =>
            Task.FromResult(new PlatformExecutionResult(true, true, "platform.accepted", "accepted without observable change"));

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.AppliedButUnverified, receipt.Outcome);
        Assert.NotEqual(ChangeOutcome.SucceededVerified, receipt.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledBeforeWrite_ReturnsCancelledWithoutMutation()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            cancellation.Token);

        Assert.Equal(ChangeOutcome.CancelledBeforeWrite, receipt.Outcome);
        Assert.False(receipt.Operation.MutationOccurred);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSamePlanIsRepeated_DoesNotWriteTwice()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var confirmation = ConfirmationToken.ForPreparedPlan(plan);

        var first = await harness.Orchestrator.ExecuteAsync(plan, confirmation, CancellationToken.None);
        var second = await harness.Orchestrator.ExecuteAsync(plan, confirmation, CancellationToken.None);

        Assert.Equal(ChangeOutcome.SucceededVerified, first.Outcome);
        Assert.Equal(ChangeOutcome.Blocked, second.Outcome);
        Assert.Equal("plan.replayed", second.Operation.ReasonCode);
        Assert.Equal(1, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPlanPayloadIsTampered_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var tampered = plan with
        {
            Change = plan.Change with { TargetPayload = "tampered" }
        };

        var receipt = await harness.Orchestrator.ExecuteAsync(
            tampered,
            ConfirmationToken.ForPreparedPlan(tampered),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("plan.hash-invalid", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecoveryPolicyIsDowngraded_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var tampered = plan with { RequiresRecoveryMaterial = false };

        var receipt = await harness.Orchestrator.ExecuteAsync(
            tampered,
            ConfirmationToken.ForPreparedPlan(tampered),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("plan.policy-mismatch", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRiskIsTampered_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var tampered = plan with { Risk = ChangeRisk.ReadOnly };

        var receipt = await harness.Orchestrator.ExecuteAsync(
            tampered,
            ConfirmationToken.ForPreparedPlan(tampered),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("plan.policy-mismatch", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCapabilityContractChangesAfterPrepare_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Capability.Descriptor = harness.Capability.Descriptor with { DefinitionVersion = "2" };

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("plan.contract-mismatch", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecoveryIsNotRequired_DoesNotExposeUnusableRecoveryId()
    {
        var capability = new TestCapability("0", requiresRecoveryMaterial: false);
        var harness = new Harness("0", capability);
        var plan = await harness.PrepareReadyAsync("1");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.SucceededVerified, receipt.Outcome);
        Assert.Null(receipt.Operation.RecoveryId);
        Assert.Empty(harness.Recovery.Materials);
    }

    [Fact]
    public async Task RestoreAsync_WhenAuditReadFails_UsesExplicitRecoveryIdFallback()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        harness.Audit.LoadException = new IOException("audit unavailable");

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId, execution.Operation.RecoveryId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RestoredVerified, restored.Outcome);
        Assert.Equal("0", harness.Capability.State);
        Assert.Equal(1, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenAuditReadFailsAndReceiptIdIsNotBound_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        harness.Audit.LoadException = new IOException("audit unavailable");

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(Guid.NewGuid(), execution.Operation.RecoveryId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("restore.receipt-binding-missing", restored.Operation.ReasonCode);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenRecoveryReceiptBindingIsTampered_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        harness.Recovery.LoadTransform = material => material with { ReceiptId = Guid.NewGuid() };

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId, execution.Operation.RecoveryId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("restore.material-invalid", restored.Operation.ReasonCode);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenRecoveryMaterialIsMissing_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        harness.Recovery.Materials.Clear();

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId, execution.Operation.RecoveryId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("restore.material-invalid", restored.Operation.ReasonCode);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenReceiptDoesNotMatchRecoveryMaterial_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        harness.Audit.Receipts[execution.Operation.ReceiptId] = execution.Operation with
        {
            TargetFingerprint = "forged-target"
        };

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("restore.receipt-material-mismatch", restored.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.RestoreCount);
        Assert.Equal("1", harness.Capability.State);
    }

    [Fact]
    public async Task RestoreAsync_WhenAuditReturnsDifferentReceiptId_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        var requestedReceiptId = Guid.NewGuid();
        harness.Audit.Receipts[requestedReceiptId] = execution.Operation;

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(requestedReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("restore.receipt-id-mismatch", restored.Operation.ReasonCode);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenResourceLockAcquisitionIsCancelled_ReturnsWithoutWrite()
    {
        var lockProvider = new ThrowingResourceLockProvider(new OperationCanceledException());
        var harness = new Harness("0", resourceLockProvider: lockProvider);
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.CancelledBeforeWrite, restored.Outcome);
        Assert.Equal("restore.cancelled-waiting-lock", restored.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenResourceLockAcquisitionFails_BlocksWithoutWrite()
    {
        var lockProvider = new ThrowingResourceLockProvider(new IOException("lock unavailable"));
        var harness = new Harness("0", resourceLockProvider: lockProvider);
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("restore.lock-failed", restored.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenDeviceDisappearsAfterApply_UsesRestoreIntentAndCanRecover()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        harness.Capability.IsPresent = false;

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RestoredVerified, restored.Outcome);
        Assert.Equal("0", harness.Capability.State);
        Assert.Contains(ChangeIntent.Prepare, harness.Capability.ProbeIntents);
        Assert.Contains(ChangeIntent.Execute, harness.Capability.ProbeIntents);
        Assert.Contains(ChangeIntent.Restore, harness.Capability.ProbeIntents);
    }

    [Fact]
    public async Task PrepareAsync_WhenCapabilityChangesResourceKey_BlocksContractMismatch()
    {
        var harness = new Harness("0");
        harness.Capability.PrepareTransform = change => change with { ResourceKey = "different.resource" };

        var result = await harness.Orchestrator.PrepareAsync(
            new ChangeRequest(TestCapability.Id, Scope, "1"),
            CancellationToken.None);

        Assert.Equal(ChangePreparationStatus.Blocked, result.Status);
        Assert.Equal("prepare.contract-mismatch", result.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenJournalPersistenceFails_BlocksBeforeWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Journal.AppendException = new IOException("journal unavailable");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.FailedNoChange, receipt.Outcome);
        Assert.Equal("journal.terminal-persist-failed", receipt.Operation.ReasonCode);
        Assert.Single(harness.Audit.Receipts);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConcurrentOnSameResource_SerializesAndAvoidsDuplicateWrite()
    {
        var harness = new Harness("0");
        var firstPlan = await harness.PrepareReadyAsync("1");
        var secondPlan = await harness.PrepareReadyAsync("1");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Capability.ExecuteBehavior = async change =>
        {
            entered.TrySetResult();
            await release.Task;
            harness.Capability.State = change.TargetPayload;
            return new PlatformExecutionResult(true, true, "ok", "ok");
        };

        var firstTask = harness.Orchestrator.ExecuteAsync(
            firstPlan,
            ConfirmationToken.ForPreparedPlan(firstPlan),
            CancellationToken.None);
        await entered.Task;
        var secondTask = harness.Orchestrator.ExecuteAsync(
            secondPlan,
            ConfirmationToken.ForPreparedPlan(secondPlan),
            CancellationToken.None);
        release.TrySetResult();

        var receipts = await Task.WhenAll(firstTask, secondTask);

        Assert.Contains(receipts, receipt => receipt.Outcome == ChangeOutcome.SucceededVerified);
        Assert.Contains(receipts, receipt => receipt.Outcome == ChangeOutcome.NoChange);
        Assert.Equal(1, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_AcrossTwoOrchestratorsWithSharedStores_SerializesAndAvoidsDuplicateWrite()
    {
        var capability = new TestCapability("0");
        var planStore = new MemoryPlanStore();
        var locks = new MemoryResourceLockProvider();
        var firstHarness = new Harness("0", capability, planStore, locks);
        var secondHarness = new Harness("0", capability, planStore, locks);
        var firstPlan = await firstHarness.PrepareReadyAsync("1");
        var secondPlan = await secondHarness.PrepareReadyAsync("1");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capability.ExecuteBehavior = async change =>
        {
            entered.TrySetResult();
            await release.Task;
            capability.State = change.TargetPayload;
            return new PlatformExecutionResult(true, true, "ok", "ok");
        };

        var firstTask = firstHarness.Orchestrator.ExecuteAsync(
            firstPlan,
            ConfirmationToken.ForPreparedPlan(firstPlan),
            CancellationToken.None);
        await entered.Task;
        var secondTask = secondHarness.Orchestrator.ExecuteAsync(
            secondPlan,
            ConfirmationToken.ForPreparedPlan(secondPlan),
            CancellationToken.None);
        release.TrySetResult();

        var receipts = await Task.WhenAll(firstTask, secondTask);

        Assert.Contains(receipts, receipt => receipt.Outcome == ChangeOutcome.SucceededVerified);
        Assert.Contains(receipts, receipt => receipt.Outcome == ChangeOutcome.NoChange);
        Assert.Equal(1, capability.ExecuteCount);
    }

    [Fact]
    public async Task PrepareAsync_WhenPlanStoreFails_DoesNotReturnExecutablePlan()
    {
        var planStore = new MemoryPlanStore { RegisterException = new IOException("plan store unavailable") };
        var harness = new Harness("0", planStore: planStore);

        var result = await harness.Orchestrator.PrepareAsync(
            new ChangeRequest(TestCapability.Id, Scope, "1"),
            CancellationToken.None);

        Assert.Equal(ChangePreparationStatus.Blocked, result.Status);
        Assert.Null(result.Plan);
        Assert.Equal("plan.register-failed", result.ReasonCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStateDrifts_BlocksWithoutWrite()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Capability.State = "external-change";

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Conflict, receipt.Outcome);
        Assert.Equal("state.drift", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task RestoreAsync_AfterVerifiedExecution_RestoresAndVerifiesOriginalState()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        var restore = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RestoredVerified, restore.Outcome);
        Assert.Equal("0", harness.Capability.State);
        Assert.Equal(1, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenAppliedStateDrifts_BlocksWithoutOverwritingExternalChange()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        harness.Capability.State = "external-change";

        var restore = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Conflict, restore.Outcome);
        Assert.Equal("restore.state-drift", restore.Operation.ReasonCode);
        Assert.Equal("external-change", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenPermissionChangesAfterExecution_DeniesWithoutRestoring()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        var execution = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        harness.Capability.Assessment = new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            false,
            "permission.denied",
            "permission revoked");

        var restore = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(execution.Operation.ReceiptId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Denied, restore.Outcome);
        Assert.Equal("permission.denied", restore.Operation.ReasonCode);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAuditPersistenceFailsAfterMutation_RequiresRecovery()
    {
        var harness = new Harness("0");
        var plan = await harness.PrepareReadyAsync("1");
        harness.Audit.SaveException = new IOException("audit unavailable");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, receipt.Outcome);
        Assert.Equal("1", harness.Capability.State);
        Assert.NotNull(receipt.Operation.RecoveryId);
        var material = Assert.Single(harness.Recovery.Materials.Values);
        Assert.Equal(receipt.Operation.ReceiptId, material.ReceiptId);
        harness.Audit.SaveException = null;

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(receipt.Operation.ReceiptId, receipt.Operation.RecoveryId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RestoredVerified, restored.Outcome);
        Assert.Equal("0", harness.Capability.State);
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitConfirmation_IsIssuedByAuthorityAndConsumedOnce()
    {
        var authority = new ChangeConfirmationAuthority(new AlwaysConfirmPrompt());
        var capability = new TestCapability("0", requiresExplicitConfirmation: true);
        var harness = new Harness("0", capability, confirmationAuthority: authority);
        var plan = await harness.PrepareReadyAsync("1");
        var confirmation = await authority.RequestAsync(
            plan,
            "Confirm test change",
            "Test impact",
            CancellationToken.None);

        Assert.NotNull(confirmation);
        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            confirmation,
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.SucceededVerified, receipt.Outcome);
        Assert.Equal("1", capability.State);
        Assert.False(authority.TryConsume(plan, confirmation));
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutionGateBlocks_DeniesBeforeWrite()
    {
        var harness = new Harness("0", executionGate: ChangeExecutionGateTestDouble.Denied(ChangeExecutionGateDecisionKind.BlockedNeedBaseline));
        var plan = await harness.PrepareReadyAsync("1");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Denied, receipt.Outcome);
        Assert.Equal("execution-gate.blocked", receipt.Operation.ReasonCode);
        Assert.Equal("0", harness.Capability.State);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutionGateReadonlyOnly_DeniesBeforeWrite()
    {
        var harness = new Harness("0", executionGate: ChangeExecutionGateTestDouble.Denied(ChangeExecutionGateDecisionKind.ReadonlyOnly));
        var plan = await harness.PrepareReadyAsync("1");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Denied, receipt.Outcome);
        Assert.Equal("execution-gate.blocked", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutionGateThrows_BlocksBeforeWrite()
    {
        var harness = new Harness("0", executionGate: ChangeExecutionGateTestDouble.Throwing(new IOException("gate unavailable")));
        var plan = await harness.PrepareReadyAsync("1");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("execution-gate.failed", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutionGateCancelled_CancelsBeforeWrite()
    {
        var harness = new Harness("0", executionGate: ChangeExecutionGateTestDouble.Cancelling());
        var plan = await harness.PrepareReadyAsync("1");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.CancelledBeforeWrite, receipt.Outcome);
        Assert.Equal("cancelled.execution-gate", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutionGateAllows_ExecutesNormally()
    {
        var harness = new Harness("0", executionGate: ChangeExecutionGateTestDouble.Allowed());
        var plan = await harness.PrepareReadyAsync("1");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.SucceededVerified, receipt.Outcome);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(1, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task CentralCompatibilityPolicy_RechecksAtExecuteAndBlocksBeforeWriteWhenFactsDrift()
    {
        var policy = new SequencedCompatibilityPolicy(
            null,
            new CapabilityAssessment(
                CapabilityApplicability.Unsupported,
                false,
                "compatibility.os.build-too-old",
                "Windows build changed and is unsupported."));
        var harness = new Harness("0", compatibilityPolicy: policy);
        var plan = await harness.PrepareReadyAsync("1");

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("compatibility.os.build-too-old", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
        Assert.Equal([ChangeIntent.Prepare, ChangeIntent.Execute], policy.Intents);
    }

    [Fact]
    public async Task CentralCompatibilityPolicy_RechecksAtRestoreAndPreservesAppliedStateWhenUnsupported()
    {
        var policy = new SequencedCompatibilityPolicy(
            null,
            null,
            new CapabilityAssessment(
                CapabilityApplicability.Unsupported,
                false,
                "compatibility.component.missing",
                "Required component disappeared."));
        var harness = new Harness("0", compatibilityPolicy: policy);
        var plan = await harness.PrepareReadyAsync("1");
        var executed = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        Assert.Equal(ChangeOutcome.SucceededVerified, executed.Outcome);

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(executed.Operation.ReceiptId, executed.Operation.RecoveryId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("compatibility.component.missing", restored.Operation.ReasonCode);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
        Assert.Equal([ChangeIntent.Prepare, ChangeIntent.Execute, ChangeIntent.Restore], policy.Intents);
    }

    [Fact]
    public async Task RestoreAsync_WhenExecutionGateBlocksRestore_DeniesWithoutWrite()
    {
        var harness = new Harness(
            "0",
            executionGate: ChangeExecutionGateTestDouble.ForIntents(
                ChangeExecutionGateDecision.Allowed("apply is authorized"),
                new ChangeExecutionGateDecision(ChangeExecutionGateDecisionKind.BlockedNeedBaseline, "restore baseline required")));
        var plan = await harness.PrepareReadyAsync("1");
        var executed = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);
        Assert.Equal(ChangeOutcome.SucceededVerified, executed.Outcome);

        var restored = await harness.Orchestrator.RestoreAsync(
            new RestoreRequest(executed.Operation.ReceiptId, executed.Operation.RecoveryId),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, restored.Outcome);
        Assert.Equal("restore.execution-gate-blocked", restored.Operation.ReasonCode);
        Assert.Equal("1", harness.Capability.State);
        Assert.Equal(0, harness.Capability.RestoreCount);
    }

    private sealed class Harness
    {
        public Harness(
            string initialState,
            TestCapability? capability = null,
            IChangePlanStore? planStore = null,
            IChangeResourceLockProvider? resourceLockProvider = null,
            IChangeConfirmationAuthority? confirmationAuthority = null,
            IChangeExecutionGate? executionGate = null,
            IChangeCompatibilityPolicy? compatibilityPolicy = null)
        {
            Capability = capability ?? new TestCapability(initialState);
            executionGate ??= ChangeExecutionGateTestDouble.Allowed();
            Orchestrator = new ChangeOrchestrator(
                [Capability],
                Recovery,
                Journal,
                Audit,
                TimeProvider.System,
                planStore,
                resourceLockProvider,
                confirmationAuthority,
                new InMemoryRecoveryClaimStore(),
                executionPolicy: null,
                executionGate,
                compatibilityPolicy);
        }

        public TestCapability Capability { get; }
        public MemoryRecoveryRepository Recovery { get; } = new();
        public MemoryJournal Journal { get; } = new();
        public MemoryAuditStore Audit { get; } = new();
        public ChangeOrchestrator Orchestrator { get; }

        public async Task<PreparedChangePlan> PrepareReadyAsync(string target)
        {
            var result = await Orchestrator.PrepareAsync(
                new ChangeRequest(TestCapability.Id, Scope, target),
                CancellationToken.None);
            return Assert.IsType<PreparedChangePlan>(result.Plan);
        }
    }

    private sealed class SequencedCompatibilityPolicy(params CapabilityAssessment?[] results)
        : IChangeCompatibilityPolicy
    {
        private int _index;
        public List<ChangeIntent> Intents { get; } = [];

        public Task<CapabilityAssessment?> ProbeAsync(
            CapabilityDescriptor descriptor,
            TargetScope scope,
            ChangeIntent intent,
            CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            var index = Math.Min(_index++, results.Length - 1);
            return Task.FromResult(results[index]);
        }
    }

    private sealed class TestCapability : IChangeCapability
    {
        public const string Id = "test.capability";

        public TestCapability(
            string initialState,
            bool requiresExplicitConfirmation = false,
            bool requiresRecoveryMaterial = true)
        {
            State = initialState;
            Descriptor = new CapabilityDescriptor(
                Id,
                "1",
                ChangeRisk.LowReversible,
                [TargetScopeKind.CurrentUser],
                false,
                requiresExplicitConfirmation,
                requiresRecoveryMaterial,
                false,
                "test.resource");
        }

        public string State { get; set; }
        public int ReadCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public int RestoreCount { get; private set; }
        public bool IsPresent { get; set; } = true;
        public List<ChangeIntent> ProbeIntents { get; } = [];
        public static CapabilityAssessment AllowedAssessment { get; } = new(
            CapabilityApplicability.Applicable,
            true,
            "applicable",
            "applicable");
        public CapabilityAssessment Assessment { get; set; } = AllowedAssessment;
        public Func<PreparedCapabilityChange, Task<PlatformExecutionResult>>? ExecuteBehavior { get; set; }
        public Func<PreparedCapabilityChange, PreparedCapabilityChange>? PrepareTransform { get; set; }

        public CapabilityDescriptor Descriptor { get; set; }

        public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(Assessment);

        public Task<CapabilityAssessment> ProbeAsync(
            TargetScope scope,
            ChangeIntent intent,
            CancellationToken cancellationToken)
        {
            ProbeIntents.Add(intent);
            if (!IsPresent && intent != ChangeIntent.Restore)
            {
                return Task.FromResult(new CapabilityAssessment(
                    CapabilityApplicability.NotApplicable,
                    true,
                    "device.not-present",
                    "device disconnected"));
            }

            return Task.FromResult(Assessment);
        }

        public Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(Observe(scope, State));
        }

        public Task<PreparedCapabilityChange> PrepareAsync(
            StateObservation current,
            string targetPayload,
            CancellationToken cancellationToken)
        {
            var change = new PreparedCapabilityChange(
                Id,
                "1",
                current.Scope,
                current.Payload,
                current.Fingerprint,
                targetPayload,
                Fingerprint(targetPayload),
                Descriptor.ResourceKey);
            return Task.FromResult(PrepareTransform?.Invoke(change) ?? change);
        }

        public async Task<PlatformExecutionResult> ExecuteAsync(
            PreparedCapabilityChange change,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            if (ExecuteBehavior is not null)
            {
                return await ExecuteBehavior(change);
            }

            State = change.TargetPayload;
            return new PlatformExecutionResult(true, true, "ok", "ok");
        }

        public Task<PlatformExecutionResult> RestoreAsync(
            RecoveryMaterial material,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            State = material.BeforePayload;
            return Task.FromResult(new PlatformExecutionResult(true, true, "ok", "ok"));
        }

        private static StateObservation Observe(TargetScope scope, string state) =>
            new(scope, state, Fingerprint(state), DateTimeOffset.UtcNow, "memory", "authoritative");

        private static string Fingerprint(string state) => $"fp:{state}";
    }

    private sealed class MemoryRecoveryRepository : IRecoveryRepository
    {
        public Dictionary<Guid, RecoveryMaterial> Materials { get; } = [];
        public Exception? SaveException { get; set; }
        public Func<RecoveryMaterial, RecoveryMaterial>? LoadTransform { get; set; }

        public Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            Materials[material.RecoveryId] = material;
            return Task.CompletedTask;
        }

        public Task<RecoveryMaterial?> LoadAsync(Guid recoveryId, CancellationToken cancellationToken)
        {
            var material = Materials.GetValueOrDefault(recoveryId);
            return Task.FromResult(material is null ? null : LoadTransform?.Invoke(material) ?? material);
        }
    }

    private sealed class MemoryJournal : IChangeJournal
    {
        public List<JournalEntry> Entries { get; } = [];
        public Exception? AppendException { get; set; }

        public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken)
        {
            if (AppendException is not null)
            {
                throw AppendException;
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryAuditStore : IChangeAuditStore
    {
        public Dictionary<Guid, OperationReceipt> Receipts { get; } = [];
        public Exception? SaveException { get; set; }
        public Exception? LoadException { get; set; }

        public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            Receipts[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }

        public Task<OperationReceipt?> LoadAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return Task.FromResult(Receipts.GetValueOrDefault(receiptId));
        }
    }

    private sealed class ThrowingResourceLockProvider(Exception exception) : IChangeResourceLockProvider
    {
        private int _acquisitionCount;

        public Task<IChangeResourceLease?> TryAcquireAsync(
            string resourceKey,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _acquisitionCount) == 1)
            {
                return Task.FromResult<IChangeResourceLease?>(new NoOpLease());
            }

            return Task.FromException<IChangeResourceLease?>(exception);
        }

        private sealed class NoOpLease : IChangeResourceLease
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryPlanStore : IChangePlanStore
    {
        private readonly Dictionary<Guid, string> _plans = [];
        private readonly HashSet<Guid> _claimed = [];
        private readonly object _sync = new();

        public Exception? RegisterException { get; set; }

        public Task RegisterAsync(PreparedChangePlan plan, CancellationToken cancellationToken)
        {
            if (RegisterException is not null)
            {
                throw RegisterException;
            }

            lock (_sync)
            {
                _plans.Add(plan.PlanId, plan.PlanHash);
            }

            return Task.CompletedTask;
        }

        public Task<PlanClaimStatus> TryClaimAsync(
            Guid planId,
            string planHash,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_plans.TryGetValue(planId, out var issuedHash))
                {
                    return Task.FromResult(PlanClaimStatus.Missing);
                }

                if (!string.Equals(issuedHash, planHash, StringComparison.Ordinal))
                {
                    return Task.FromResult(PlanClaimStatus.HashMismatch);
                }

                return Task.FromResult(
                    _claimed.Add(planId)
                        ? PlanClaimStatus.Claimed
                        : PlanClaimStatus.AlreadyClaimed);
            }
        }
    }

    private sealed class MemoryResourceLockProvider : IChangeResourceLockProvider
    {
        private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        public async Task<IChangeResourceLease?> TryAcquireAsync(
            string resourceKey,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            SemaphoreSlim semaphore;
            lock (_sync)
            {
                if (!_locks.TryGetValue(resourceKey, out semaphore!))
                {
                    semaphore = new SemaphoreSlim(1, 1);
                    _locks[resourceKey] = semaphore;
                }
            }

            return await semaphore.WaitAsync(timeout, cancellationToken)
                ? new MemoryLease(semaphore)
                : null;
        }

        private sealed class MemoryLease : IChangeResourceLease
        {
            private SemaphoreSlim? _semaphore;

            public MemoryLease(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _semaphore, null)?.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class AlwaysConfirmPrompt : IChangeConfirmationPrompt
    {
        public Task<bool> ConfirmAsync(
            ChangeConfirmationPrompt prompt,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
