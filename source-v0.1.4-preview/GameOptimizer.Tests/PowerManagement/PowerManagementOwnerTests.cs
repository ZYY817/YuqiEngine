using System.Collections.Concurrent;
using System.Text;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class PowerManagementOwnerTests
{
    private static readonly Guid OriginalScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid TargetScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid AlternateTargetScheme = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Inspect_EnumeratesSchemesAndReadsActiveAndDisplayValues()
    {
        var fixture = new Fixture();
        var state = await fixture.Owner.InspectAsync(TargetScheme, default);
        Assert.Equal(3, state.Schemes.Count);
        Assert.Equal(OriginalScheme, state.ActiveSchemeId);
        Assert.Equal(new PowerSettingValues(600, 300), state.DisplayTimeout);
        Assert.True(state.DisplayTimeoutCapability.IsAvailable);
    }

    [Theory]
    [InlineData(false, false, false, PowerWorkflowStatus.PermissionDenied)]
    [InlineData(true, true, false, PowerWorkflowStatus.PolicyManaged)]
    [InlineData(true, false, true, PowerWorkflowStatus.Unsupported)]
    public async Task Prepare_FailsClosedForPermissionPolicyAndModernStandby(
        bool admin, bool policy, bool modernStandby, PowerWorkflowStatus expected)
    {
        var fixture = new Fixture();
        fixture.Platform.Environment = fixture.Platform.Environment with
        {
            IsAdministrator = admin,
            IsPolicyManaged = policy,
            IsModernStandby = modernStandby
        };
        var result = await fixture.Owner.PrepareAsync(new PowerPrepareRequest(TargetScheme, 900, 420), default);
        Assert.False(result.Success);
        Assert.Equal(expected, result.Status);
        Assert.Empty(fixture.Store.Plans);
    }

    [Fact]
    public async Task Prepare_RequiresDcOnBatteryAndRejectsDcOnDesktop()
    {
        var battery = new Fixture();
        var missingDc = await battery.Owner.PrepareAsync(new PowerPrepareRequest(TargetScheme, 900, null), default);
        Assert.Equal(PowerWorkflowStatus.InvalidRequest, missingDc.Status);

        var desktop = new Fixture();
        desktop.Platform.Environment = desktop.Platform.Environment with { HasBattery = false };
        desktop.Platform.Values[TargetScheme] = new PowerSettingValues(600, null);
        var unexpectedDc = await desktop.Owner.PrepareAsync(new PowerPrepareRequest(TargetScheme, 900, 420), default);
        Assert.Equal(PowerWorkflowStatus.InvalidRequest, unexpectedDc.Status);
    }

    [Fact]
    public async Task Execute_BlocksPlanDriftBeforeAnyWrite()
    {
        var fixture = new Fixture();
        var plan = await fixture.PrepareAsync();
        var token = fixture.Confirmation.Issue(plan, Now, TimeSpan.FromMinutes(1));
        fixture.Platform.ActiveScheme = TargetScheme;
        var result = await fixture.Owner.ExecuteAsync(plan, token, default);
        Assert.Equal(PowerWorkflowStatus.DriftBlocked, result.Status);
        Assert.Equal(PowerExecutionPhase.DriftCheck, result.Phase);
        Assert.Equal(0, fixture.Platform.ApplyCalls);
    }

    [Fact]
    public async Task Execute_RejectsForgedExpiredAndReplayedConfirmation()
    {
        var forgedFixture = new Fixture();
        var forgedPlan = await forgedFixture.PrepareAsync();
        var token = forgedFixture.Confirmation.Issue(forgedPlan, Now, TimeSpan.FromMinutes(1));
        var forged = token with { Signature = new string('0', 64) };
        Assert.Equal(PowerWorkflowStatus.ConfirmationRequired,
            (await forgedFixture.Owner.ExecuteAsync(forgedPlan, forged, default)).Status);

        var expiredFixture = new Fixture();
        var expiredPlan = await expiredFixture.PrepareAsync();
        var expired = expiredFixture.Confirmation.Issue(expiredPlan, Now, TimeSpan.FromSeconds(1));
        expiredFixture.Clock.UtcNow = Now.AddSeconds(2);
        Assert.Equal(PowerWorkflowStatus.ConfirmationRequired,
            (await expiredFixture.Owner.ExecuteAsync(expiredPlan, expired, default)).Status);

        var replayFixture = new Fixture();
        var replayPlan = await replayFixture.PrepareAsync();
        var firstToken = replayFixture.Confirmation.Issue(replayPlan, Now, TimeSpan.FromMinutes(1));
        Assert.True((await replayFixture.Owner.ExecuteAsync(replayPlan, firstToken, default)).Success);
        Assert.Equal(PowerWorkflowStatus.ConfirmationRequired,
            (await replayFixture.Owner.ExecuteAsync(replayPlan, firstToken, default)).Status);
        var secondToken = replayFixture.Confirmation.Issue(replayPlan, Now, TimeSpan.FromMinutes(1));
        Assert.Equal(PowerWorkflowStatus.ReplayBlocked,
            (await replayFixture.Owner.ExecuteAsync(replayPlan, secondToken, default)).Status);
    }

    [Fact]
    public async Task Execute_BaselineFailureBlocksMutation()
    {
        var fixture = new Fixture { Baseline = { Decision = new(false, null, "backup unavailable") } };
        var plan = await fixture.PrepareAsync();
        var result = await fixture.Owner.ExecuteAsync(plan, fixture.Confirmation.Issue(plan, Now, TimeSpan.FromMinutes(1)), default);
        Assert.Equal(PowerWorkflowStatus.BaselineBlocked, result.Status);
        Assert.Equal(0, fixture.Platform.ApplyCalls);
    }

    [Theory]
    [InlineData(PowerCommandOutcome.Failed, PowerWorkflowStatus.CommandFailed)]
    [InlineData(PowerCommandOutcome.TimedOut, PowerWorkflowStatus.TimedOut)]
    [InlineData(PowerCommandOutcome.Cancelled, PowerWorkflowStatus.Cancelled)]
    public async Task Execute_CommandFailureTimeoutAndCancelRollbackAndPersistReceipt(
        PowerCommandOutcome outcome, PowerWorkflowStatus expected)
    {
        var fixture = new Fixture();
        fixture.Platform.MutationOutcomes.Enqueue(outcome);
        fixture.Platform.MutationOutcomes.Enqueue(PowerCommandOutcome.Success);
        var plan = await fixture.PrepareAsync();
        var result = await fixture.Owner.ExecuteAsync(plan, fixture.Confirmation.Issue(plan, Now, TimeSpan.FromMinutes(1)), default);
        Assert.Equal(expected, result.Status);
        Assert.Equal(PowerExecutionPhase.Rollback, result.Phase);
        Assert.NotNull(result.Receipt);
        Assert.True(result.Receipt!.RestoreVerified);
        Assert.Equal(2, fixture.Platform.ApplyCalls);
    }

    [Fact]
    public async Task Execute_ReadBackMismatchTriggersVerifiedRollback()
    {
        var fixture = new Fixture();
        fixture.Platform.SuppressNextSuccessfulMutation = true;
        var plan = await fixture.PrepareAsync();
        var result = await fixture.Owner.ExecuteAsync(plan, fixture.Confirmation.Issue(plan, Now, TimeSpan.FromMinutes(1)), default);
        Assert.Equal(PowerWorkflowStatus.ReadBackMismatch, result.Status);
        Assert.True(result.Receipt!.RestoreVerified);
    }

    [Fact]
    public async Task Execute_ProcessStartExceptionIsReportedAndRolledBack()
    {
        var fixture = new Fixture();
        fixture.Platform.ThrowOnNextApply = true;
        var plan = await fixture.PrepareAsync();
        var result = await fixture.Owner.ExecuteAsync(plan, fixture.Confirmation.Issue(plan, Now, TimeSpan.FromMinutes(1)), default);
        Assert.Equal(PowerWorkflowStatus.CommandFailed, result.Status);
        Assert.Equal(PowerExecutionPhase.Rollback, result.Phase);
        Assert.True(result.Receipt!.RestoreVerified);
    }

    [Fact]
    public async Task DifferentPlansForSameResourceAreSerializedAndSecondDriftBlocks()
    {
        var fixture = new Fixture();
        var firstPlan = await fixture.PrepareAsync();
        var secondPlan = await fixture.PrepareAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Platform.BeforeMutation = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            fixture.Platform.BeforeMutation = null;
        };
        var first = fixture.Owner.ExecuteAsync(firstPlan, fixture.Confirmation.Issue(firstPlan, Now, TimeSpan.FromMinutes(1)), default);
        await entered.Task;
        var second = fixture.Owner.ExecuteAsync(secondPlan, fixture.Confirmation.Issue(secondPlan, Now, TimeSpan.FromMinutes(1)), default);
        await Task.Delay(30);
        Assert.Equal(1, fixture.Platform.ApplyCalls);
        release.TrySetResult();
        Assert.True((await first).Success);
        var secondResult = await second;
        Assert.Equal(PowerWorkflowStatus.DriftBlocked, secondResult.Status);
        Assert.Equal(1, fixture.Platform.ApplyCalls);
    }

    [Fact]
    public async Task DifferentTargetSchemesAcrossOwnerInstancesShareMachineGlobalLease()
    {
        var fixture = new Fixture();
        var secondOwner = new PowerManagementOwner(
            fixture.Platform, fixture.Baseline, fixture.Store, fixture.Confirmation, fixture.Clock);
        var firstPlan = (await fixture.Owner.PrepareAsync(
            new PowerPrepareRequest(TargetScheme, 900, 420), default)).Plan!;
        var secondPlan = (await secondOwner.PrepareAsync(
            new PowerPrepareRequest(AlternateTargetScheme, 1200, 600), default)).Plan!;
        Assert.Equal(PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource, firstPlan.ResourceKey);
        Assert.Equal(firstPlan.ResourceKey, secondPlan.ResourceKey);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Platform.BeforeMutation = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            fixture.Platform.BeforeMutation = null;
        };

        var first = fixture.Owner.ExecuteAsync(
            firstPlan, fixture.Confirmation.Issue(firstPlan, Now, TimeSpan.FromMinutes(1)), default);
        await entered.Task;
        var second = secondOwner.ExecuteAsync(
            secondPlan, fixture.Confirmation.Issue(secondPlan, Now, TimeSpan.FromMinutes(1)), default);
        await Task.Delay(30);
        Assert.Equal(1, fixture.Platform.ApplyCalls);

        release.TrySetResult();
        Assert.True((await first).Success);
        Assert.Equal(PowerWorkflowStatus.DriftBlocked, (await second).Status);
        Assert.Equal(1, fixture.Platform.ApplyCalls);
    }

    [Fact]
    public async Task CallerCancellationAfterExecutingIsRecordedButTerminalReadbackAndPersistenceComplete()
    {
        var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        fixture.Platform.BeforeMutation = internalToken =>
        {
            cts.Cancel();
            Assert.False(internalToken.IsCancellationRequested);
            return Task.CompletedTask;
        };
        var plan = await fixture.PrepareAsync();
        var result = await fixture.Owner.ExecuteAsync(plan, fixture.Confirmation.Issue(plan, Now, TimeSpan.FromMinutes(1)), cts.Token);
        Assert.True(result.Success, result.Message);
        Assert.True(result.Receipt!.CallerCancellationRequested);
        Assert.Equal(PowerReceiptState.Applied, result.Receipt.State);
        Assert.True(fixture.Store.Receipts.TryGetValue(result.Receipt.ReceiptId, out var persisted));
        Assert.Equal(PowerReceiptState.Applied, persisted!.State);
    }

    [Fact]
    public async Task TerminalPersistenceTimeoutFailsClosedAndRollsBackBeforeLeaseRelease()
    {
        var fixture = new Fixture();
        fixture.Store.BlockSaveState = PowerReceiptState.Applied;
        var owner = new PowerManagementOwner(fixture.Platform, fixture.Baseline, fixture.Store, fixture.Confirmation,
            fixture.Clock, new PowerManagementTimingOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(30)));
        var prepared = await owner.PrepareAsync(new PowerPrepareRequest(TargetScheme, 900, 420), default);
        var plan = prepared.Plan!;
        var result = await owner.ExecuteAsync(plan, fixture.Confirmation.Issue(plan, Now, TimeSpan.FromMinutes(1)), default);
        Assert.Equal(PowerWorkflowStatus.StoreFailure, result.Status);
        Assert.Equal(2, fixture.Platform.ApplyCalls);
        Assert.Equal(OriginalScheme, fixture.Platform.ActiveScheme);
        Assert.Equal(PowerReceiptState.ApplyFailed, result.Receipt!.State);
    }

    [Fact]
    public async Task Restore_BlocksDriftAndReplayAndRestoresOriginalSchemeAndValues()
    {
        var fixture = new Fixture();
        var plan = await fixture.PrepareAsync();
        var applied = await fixture.Owner.ExecuteAsync(plan, fixture.Confirmation.Issue(plan, Now, TimeSpan.FromMinutes(1)), default);
        Assert.True(applied.Success);

        fixture.Platform.Values[TargetScheme] = new PowerSettingValues(901, 420);
        var drifted = await fixture.Owner.RestoreAsync(applied.Receipt!.ReceiptId, default);
        Assert.Equal(PowerWorkflowStatus.DriftBlocked, drifted.Status);

        fixture.Platform.Values[TargetScheme] = plan.TargetDisplayTimeout;
        var restored = await fixture.Owner.RestoreAsync(applied.Receipt.ReceiptId, default);
        Assert.True(restored.Success);
        Assert.Equal(OriginalScheme, fixture.Platform.ActiveScheme);
        Assert.Equal(plan.OriginalDisplayTimeout, fixture.Platform.Values[TargetScheme]);

        var replay = await fixture.Owner.RestoreAsync(applied.Receipt.ReceiptId, default);
        Assert.Equal(PowerWorkflowStatus.ReplayBlocked, replay.Status);
        Assert.Equal(2, fixture.Platform.ApplyCalls);
    }

    [Fact]
    public async Task RecoverableReceiptHistory_ListsOnlyVerifiedAppliedReceiptsNewestFirst()
    {
        var fixture = new Fixture();
        var firstPlan = await fixture.PrepareAsync();
        var first = await fixture.Owner.ExecuteAsync(firstPlan,
            fixture.Confirmation.Issue(firstPlan, Now, TimeSpan.FromMinutes(1)), default);
        Assert.True(first.Success);

        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var secondPlan = await fixture.PrepareAsync();
        var second = await fixture.Owner.ExecuteAsync(secondPlan,
            fixture.Confirmation.Issue(secondPlan, fixture.Clock.UtcNow, TimeSpan.FromMinutes(1)), default);
        Assert.True(second.Success);

        var history = await fixture.Owner.ListRecoverableReceiptsAsync(default);

        Assert.Equal(2, history.Count);
        Assert.Equal(second.Receipt!.ReceiptId, history[0].ReceiptId);
        Assert.Equal(first.Receipt!.ReceiptId, history[1].ReceiptId);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Confirmation = new PowerConfirmationAuthority(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
            Owner = new PowerManagementOwner(Platform, Baseline, Store, Confirmation, Clock);
        }
        public FakePlatform Platform { get; } = new();
        public FakeBaselineGate Baseline { get; } = new();
        public MemoryStore Store { get; } = new();
        public FakeTimeProvider Clock { get; } = new(Now);
        public PowerConfirmationAuthority Confirmation { get; }
        public PowerManagementOwner Owner { get; }

        public async Task<PowerPreparedPlan> PrepareAsync()
        {
            var result = await Owner.PrepareAsync(new PowerPrepareRequest(TargetScheme, 900, 420), default);
            Assert.True(result.Success, result.Message);
            return result.Plan!;
        }
    }

    private sealed class FakePlatform : IPowerPlatform
    {
        public PowerEnvironment Environment { get; set; } = new(true, 22631, true, true, false, false);
        public Guid ActiveScheme { get; set; } = OriginalScheme;
        public Dictionary<Guid, PowerSettingValues> Values { get; } = new()
        {
            [OriginalScheme] = new(600, 300),
            [TargetScheme] = new(600, 300),
            [AlternateTargetScheme] = new(600, 300)
        };
        public Queue<PowerCommandOutcome> MutationOutcomes { get; } = new();
        public bool SuppressNextSuccessfulMutation { get; set; }
        public bool ThrowOnNextApply { get; set; }
        public Func<CancellationToken, Task>? BeforeMutation { get; set; }
        private int _applyCalls;
        public int ApplyCalls => Volatile.Read(ref _applyCalls);
        public Task<PowerEnvironment> GetEnvironmentAsync(CancellationToken cancellationToken) => Task.FromResult(Environment);
        public Task<IReadOnlyList<PowerScheme>> EnumerateSchemesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PowerScheme>>(
            new[]
            {
                new PowerScheme(OriginalScheme, "Balanced", ActiveScheme == OriginalScheme),
                new PowerScheme(TargetScheme, "High performance", ActiveScheme == TargetScheme),
                new PowerScheme(AlternateTargetScheme, "Power saver", ActiveScheme == AlternateTargetScheme)
            });
        public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken) => Task.FromResult(ActiveScheme);
        public Task<PowerSettingCapability> GetDisplayTimeoutCapabilityAsync(Guid schemeId, CancellationToken cancellationToken) =>
            Task.FromResult(new PowerSettingCapability(true, false, 0, 86_400));
        public Task<PowerSettingValues> ReadDisplayTimeoutAsync(Guid schemeId, bool includeDc, CancellationToken cancellationToken) =>
            Task.FromResult(Values[schemeId]);
        public async Task<PowerMutationResult> ApplyDisplayTimeoutAsync(Guid schemeId, PowerSettingValues values, Guid schemeToActivate, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _applyCalls);
            if (BeforeMutation is not null) await BeforeMutation(cancellationToken);
            if (ThrowOnNextApply)
            {
                ThrowOnNextApply = false;
                throw new InvalidOperationException("fake process start failure");
            }
            var outcome = MutationOutcomes.Count == 0 ? PowerCommandOutcome.Success : MutationOutcomes.Dequeue();
            if (outcome == PowerCommandOutcome.Success && !SuppressNextSuccessfulMutation)
            {
                Values[schemeId] = values;
                ActiveScheme = schemeToActivate;
            }
            SuppressNextSuccessfulMutation = false;
            var command = new PowerCommandResult(outcome, "fake", outcome == PowerCommandOutcome.Success ? 0 : 1, string.Empty, string.Empty);
            return new PowerMutationResult(outcome, new[] { command }, outcome == PowerCommandOutcome.Success ? null : "fake failure");
        }
    }

    private sealed class FakeBaselineGate : IPowerBaselineGate
    {
        public PowerBaselineDecision Decision { get; set; } = new(true, "baseline-1", null);
        public Task<PowerBaselineDecision> EnsureBaselineAsync(PowerPreparedPlan plan, CancellationToken cancellationToken) => Task.FromResult(Decision);
    }

    private sealed class MemoryStore : IPowerWorkflowStore
    {
        public ConcurrentDictionary<Guid, PowerPreparedPlan> Plans { get; } = new();
        public ConcurrentDictionary<Guid, PowerReceipt> Receipts { get; } = new();
        private readonly ConcurrentDictionary<string, byte> _claims = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new();
        public PowerReceiptState? BlockSaveState { get; set; }
        public Task SavePreparedAsync(PowerPreparedPlan plan, CancellationToken cancellationToken) { Plans[plan.PlanId] = plan; return Task.CompletedTask; }
        public Task<PowerPreparedPlan?> GetPreparedAsync(Guid planId, CancellationToken cancellationToken) { Plans.TryGetValue(planId, out var value); return Task.FromResult(value); }
        public Task<bool> TryClaimPlanAsync(Guid planId, string planHash, CancellationToken cancellationToken) => Task.FromResult(_claims.TryAdd("p:" + planId, 0));
        public async Task SaveReceiptAsync(PowerReceipt receipt, CancellationToken cancellationToken)
        {
            if (BlockSaveState == receipt.State)
            {
                BlockSaveState = null;
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            Receipts[receipt.ReceiptId] = receipt;
        }
        public Task<PowerReceipt?> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken) { Receipts.TryGetValue(receiptId, out var value); return Task.FromResult(value); }
        public Task<IReadOnlyList<PowerReceipt>> ListReceiptsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PowerReceipt>>(Receipts.Values.OrderByDescending(item => item.CreatedAtUtc).ToArray());
        public Task<bool> TryClaimRecoveryAsync(Guid receiptId, CancellationToken cancellationToken) => Task.FromResult(_claims.TryAdd("r:" + receiptId, 0));
        public async Task<IPowerResourceLease> AcquireResourceLeaseAsync(string resourceKey, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var semaphore = _leases.GetOrAdd(resourceKey, _ => new SemaphoreSlim(1, 1));
            if (!await semaphore.WaitAsync(timeout, cancellationToken)) throw new TimeoutException();
            return new MemoryLease(resourceKey, semaphore);
        }
        private sealed class MemoryLease : IPowerResourceLease
        {
            private SemaphoreSlim? _semaphore;
            public MemoryLease(string resourceKey, SemaphoreSlim semaphore) { ResourceKey = resourceKey; _semaphore = semaphore; }
            public string ResourceKey { get; }
            public ValueTask DisposeAsync() { Interlocked.Exchange(ref _semaphore, null)?.Release(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public FakeTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
