using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class ChangeOrchestratorBoundedExecutionTests
{
    private static readonly ChangeExecutionPolicy ShortPolicy = new(
        "bounded-test.v1",
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(40));

    [Theory]
    [InlineData("before", ChangeOutcome.FailedNoChange, "execution.timeout-no-write")]
    [InlineData("target", ChangeOutcome.SucceededVerified, "execution.verified-after-interruption")]
    [InlineData("other", ChangeOutcome.RecoveryRequired, "execution.state-unknown")]
    public async Task ExecuteDeadline_WaitsForPlatformThenClassifiesIndependentReadback(
        string finalState,
        ChangeOutcome expectedOutcome,
        string expectedReason)
    {
        var harness = new Harness(ShortPolicy);
        var plan = await harness.PrepareAsync("target");
        harness.Capability.ExecuteBehavior = async (_, token) =>
        {
            await WaitUntilCancelledAsync(token);
            harness.Capability.State = finalState;
            throw new OperationCanceledException(token);
        };

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(expectedOutcome, receipt.Outcome);
        Assert.Equal(expectedReason, receipt.Operation.ReasonCode);
        Assert.Contains(receipt.Operation.ReceiptId, harness.Audit.Receipts.Keys);
    }

    [Fact]
    public async Task CallerCancellationAfterExecuting_DoesNotCancelPlatformAndStillVerifies()
    {
        var harness = new Harness(ChangeExecutionPolicy.Default);
        var plan = await harness.PrepareAsync("target");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var platformTokenCancelled = false;
        harness.Capability.ExecuteBehavior = async (_, token) =>
        {
            entered.SetResult();
            await release.Task;
            platformTokenCancelled = token.IsCancellationRequested;
            harness.Capability.State = "target";
            return new PlatformExecutionResult(true, true, "ok", "ok");
        };
        using var callerSource = new CancellationTokenSource();
        var executionTask = harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            callerSource.Token);
        await entered.Task;

        callerSource.Cancel();
        Assert.False(executionTask.IsCompleted);
        release.SetResult();
        var receipt = await executionTask;

        Assert.False(platformTokenCancelled);
        Assert.Equal(ChangeOutcome.SucceededVerified, receipt.Outcome);
        Assert.Equal("execution.verified-after-interruption", receipt.Operation.ReasonCode);
    }

    [Fact]
    public async Task CallerCancellationWhileWaitingForResourceLock_IsBeforeWrite()
    {
        var harness = new Harness(ChangeExecutionPolicy.Default);
        var plan = await harness.PrepareAsync("target");
        var heldLease = await harness.Locks.TryAcquireAsync(
            harness.Capability.Descriptor.ResourceKey,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        Assert.NotNull(heldLease);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            caller.Token);
        await heldLease!.DisposeAsync();

        Assert.Equal(ChangeOutcome.CancelledBeforeWrite, receipt.Outcome);
        Assert.Equal("cancelled.waiting-lock", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task IgnoredDeadlineAndDelayedCompensation_DoesNotReleaseResourceLeaseEarly()
    {
        var policy = ShortPolicy with { PlatformTimeout = TimeSpan.FromMilliseconds(20) };
        var harness = new Harness(policy);
        var firstPlan = await harness.PrepareAsync("target");
        var secondPlan = await harness.PrepareAsync("target");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCompensation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Capability.ExecuteBehavior = async (_, token) =>
        {
            harness.Capability.EnterPlatform();
            entered.TrySetResult();
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2);
            }

            await finishCompensation.Task;
            harness.Capability.State = "before";
            harness.Capability.ExitPlatform();
            throw new OperationCanceledException(token);
        };

        var firstTask = harness.Orchestrator.ExecuteAsync(
            firstPlan,
            ConfirmationToken.ForPreparedPlan(firstPlan),
            CancellationToken.None);
        await entered.Task;
        await Task.Delay(60);
        Assert.False(firstTask.IsCompleted);

        var secondTask = harness.Orchestrator.ExecuteAsync(
            secondPlan,
            ConfirmationToken.ForPreparedPlan(secondPlan),
            CancellationToken.None);
        await Task.Delay(30);
        Assert.Equal(1, harness.Capability.MaximumConcurrentPlatformCalls);
        Assert.False(secondTask.IsCompleted);

        finishCompensation.SetResult();
        await Task.WhenAll(firstTask, secondTask);
        Assert.Equal(1, harness.Capability.MaximumConcurrentPlatformCalls);
    }

    [Fact]
    public async Task SameVersionWithDifferentTimeoutFingerprint_IsRejected()
    {
        var firstPolicy = ShortPolicy;
        var harness = new Harness(firstPolicy);
        var plan = await harness.PrepareAsync("target");
        var incompatiblePolicy = firstPolicy with { PlatformTimeout = TimeSpan.FromSeconds(2) };
        var incompatible = harness.CreateOrchestrator(incompatiblePolicy);

        var receipt = await incompatible.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Blocked, receipt.Outcome);
        Assert.Equal("plan.execution-policy-mismatch", receipt.Operation.ReasonCode);
        Assert.Equal(0, harness.Capability.ExecuteCount);
    }

    [Fact]
    public async Task TimeoutAndCallerCancellationRace_WaitsForPlatformAndUsesReadbackTruth()
    {
        var harness = new Harness(ShortPolicy);
        var plan = await harness.PrepareAsync("target");
        var deadlineObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Capability.ExecuteBehavior = async (_, token) =>
        {
            await WaitUntilCancelledAsync(token);
            deadlineObserved.SetResult();
            await finish.Task;
            harness.Capability.State = "target";
            return new PlatformExecutionResult(true, true, "ok", "ok");
        };
        using var caller = new CancellationTokenSource();
        var task = harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            caller.Token);
        await deadlineObserved.Task;

        caller.Cancel();
        Assert.False(task.IsCompleted);
        finish.SetResult();
        var receipt = await task;

        Assert.Equal(ChangeOutcome.SucceededVerified, receipt.Outcome);
        Assert.Equal("execution.verified-after-interruption", receipt.Operation.ReasonCode);
        Assert.Contains(receipt.Operation.ReceiptId, harness.Audit.Receipts.Keys);
    }

    [Fact]
    public async Task CallerCancellationAfterPlatformBeforeVerification_DoesNotCancelReadback()
    {
        var harness = new Harness(ChangeExecutionPolicy.Default);
        var plan = await harness.PrepareAsync("target");
        var verificationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishVerification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verificationTokenCancelled = false;
        harness.Capability.VerificationReadBehavior = async token =>
        {
            verificationEntered.SetResult();
            await finishVerification.Task;
            verificationTokenCancelled = token.IsCancellationRequested;
            return harness.Capability.Observe();
        };
        using var caller = new CancellationTokenSource();
        var task = harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            caller.Token);
        await verificationEntered.Task;

        caller.Cancel();
        Assert.False(task.IsCompleted);
        finishVerification.SetResult();
        var receipt = await task;

        Assert.False(verificationTokenCancelled);
        Assert.Equal(ChangeOutcome.SucceededVerified, receipt.Outcome);
        Assert.Equal("execution.verified-after-interruption", receipt.Operation.ReasonCode);
    }

    [Fact]
    public async Task VerificationFailureAfterPlatform_IsRecoveryRequiredAndDurable()
    {
        var harness = new Harness(ShortPolicy);
        var plan = await harness.PrepareAsync("target");
        harness.Capability.FailReadsAfterExecute = true;

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, receipt.Outcome);
        Assert.Equal("verification.read-failed", receipt.Operation.ReasonCode);
        Assert.Contains(receipt.Operation.ReceiptId, harness.Audit.Receipts.Keys);
    }

    [Fact]
    public async Task ExecuteDeadlineWithReadbackFailure_IsRecoveryRequiredAndDurable()
    {
        var harness = new Harness(ShortPolicy);
        var plan = await harness.PrepareAsync("target");
        harness.Capability.ExecuteBehavior = async (_, token) =>
        {
            await WaitUntilCancelledAsync(token);
            harness.Capability.FailReadsAfterExecute = true;
            throw new OperationCanceledException(token);
        };

        var receipt = await harness.Orchestrator.ExecuteAsync(
            plan,
            ConfirmationToken.ForPreparedPlan(plan),
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.RecoveryRequired, receipt.Outcome);
        Assert.Equal("verification.read-failed", receipt.Operation.ReasonCode);
        Assert.Contains(receipt.Operation.ReceiptId, harness.Audit.Receipts.Keys);
    }

    private sealed class Harness
    {
        private readonly IChangePlanStore _plans = new InMemoryChangePlanStore();
        private readonly IChangeResourceLockProvider _locks = new InProcessChangeResourceLockProvider();
        private readonly IRecoveryClaimStore _claims = new InMemoryRecoveryClaimStore();

        public Harness(ChangeExecutionPolicy policy)
        {
            Policy = policy;
            Capability = new TestCapability(policy.Version);
            Orchestrator = CreateOrchestrator(policy);
        }

        public ChangeExecutionPolicy Policy { get; }
        public IChangeResourceLockProvider Locks => _locks;
        public TestCapability Capability { get; }
        public MemoryRecoveryRepository Recovery { get; } = new();
        public MemoryJournal Journal { get; } = new();
        public MemoryAudit Audit { get; } = new();
        public ChangeOrchestrator Orchestrator { get; }

        public ChangeOrchestrator CreateOrchestrator(ChangeExecutionPolicy policy) =>
            new(
                [Capability],
                Recovery,
                Journal,
                Audit,
                TimeProvider.System,
                _plans,
                _locks,
                null,
                _claims,
                policy,
                ChangeExecutionGateTestDouble.Allowed());

        public async Task<PreparedChangePlan> PrepareAsync(string target)
        {
            var result = await Orchestrator.PrepareAsync(
                new ChangeRequest(TestCapability.Id, TestCapability.Scope, target),
                CancellationToken.None);
            return Assert.IsType<PreparedChangePlan>(result.Plan);
        }
    }

    private sealed class TestCapability : IChangeCapability
    {
        public const string Id = "test.bounded";
        public static TargetScope Scope { get; } = TargetScope.CurrentUser("test-user");
        private int _activePlatformCalls;
        private int _maximumConcurrentPlatformCalls;
        private int _executeCount;

        public TestCapability(string executionPolicyVersion)
        {
            Descriptor = new CapabilityDescriptor(
                Id, "1", ChangeRisk.LowReversible, [TargetScopeKind.CurrentUser], false, false, true, false, "test.bounded-resource", executionPolicyVersion);
        }

        public string State { get; set; } = "before";
        public int ExecuteCount => Volatile.Read(ref _executeCount);
        public int MaximumConcurrentPlatformCalls => Volatile.Read(ref _maximumConcurrentPlatformCalls);
        public bool FailReadsAfterExecute { get; set; }
        public Func<PreparedCapabilityChange, CancellationToken, Task<PlatformExecutionResult>>? ExecuteBehavior { get; set; }
        public Func<CancellationToken, Task<StateObservation>>? VerificationReadBehavior { get; set; }
        public CapabilityDescriptor Descriptor { get; }

        public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilityAssessment(CapabilityApplicability.Applicable, true, "ok", "ok"));

        public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
        {
            if (FailReadsAfterExecute && ExecuteCount > 0)
            {
                throw new IOException("readback failed");
            }

            if (ExecuteCount > 0 && VerificationReadBehavior is not null)
            {
                return await VerificationReadBehavior(cancellationToken);
            }

            return Observe();
        }

        public StateObservation Observe() =>
            new(Scope, State, Fingerprint(State), DateTimeOffset.UtcNow, "memory", "authoritative");

        public Task<PreparedCapabilityChange> PrepareAsync(StateObservation current, string targetPayload, CancellationToken cancellationToken) =>
            Task.FromResult(new PreparedCapabilityChange(
                Id, "1", current.Scope, current.Payload, current.Fingerprint, targetPayload, Fingerprint(targetPayload), Descriptor.ResourceKey));

        public async Task<PlatformExecutionResult> ExecuteAsync(PreparedCapabilityChange change, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executeCount);
            if (ExecuteBehavior is not null)
            {
                return await ExecuteBehavior(change, cancellationToken);
            }

            State = change.TargetPayload;
            return new PlatformExecutionResult(true, true, "ok", "ok");
        }

        public Task<PlatformExecutionResult> RestoreAsync(RecoveryMaterial material, CancellationToken cancellationToken)
        {
            State = material.BeforePayload;
            return Task.FromResult(new PlatformExecutionResult(true, true, "ok", "ok"));
        }

        public void EnterPlatform()
        {
            var active = Interlocked.Increment(ref _activePlatformCalls);
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrentPlatformCalls);
                if (active <= maximum || Interlocked.CompareExchange(ref _maximumConcurrentPlatformCalls, active, maximum) == maximum)
                {
                    return;
                }
            }
        }

        public void ExitPlatform() => Interlocked.Decrement(ref _activePlatformCalls);
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

    private sealed class MemoryJournal : IChangeJournal
    {
        public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemoryAudit : IChangeAuditStore
    {
        public ConcurrentDictionary<Guid, OperationReceipt> Receipts { get; } = new();
        public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
        {
            Receipts[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }
        public Task<OperationReceipt?> LoadAsync(Guid receiptId, CancellationToken cancellationToken) =>
            Task.FromResult(Receipts.GetValueOrDefault(receiptId));
    }

    private static async Task WaitUntilCancelledAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(2);
        }
    }
}
