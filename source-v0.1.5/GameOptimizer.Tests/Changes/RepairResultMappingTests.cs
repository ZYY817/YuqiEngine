using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairResultMappingTests
{
    [Fact]
    public void StaticRegistry_UsesExclusiveAvailabilityRoutes()
    {
        var registry = new RepairItemRegistry();
        registry.Initialize();
        var counts = RepairExecutionAvailabilityCatalog.Count(registry.GetAll());

        Assert.Equal(123, registry.GetAll().Count);
        Assert.Equal(18, counts[RepairExecutionAvailability.WorkingBidirectional]);
        Assert.Equal(25, counts[RepairExecutionAvailability.ReadOnly]);
        Assert.Equal(51, counts[RepairExecutionAvailability.FailClosed]);
        Assert.Equal(29, counts[RepairExecutionAvailability.SafetyBlocked]);
        Assert.Equal(123, counts.Values.Sum());

        Assert.All(registry.GetAll().Where(item => item.Availability != RepairExecutionAvailability.WorkingBidirectional),
            item => Assert.False(item.CanMutate));
    }

    [Theory]
    [InlineData("permission.administrator-required", RepairResultCode.PermissionDenied)]
    [InlineData("os.unsupported", RepairResultCode.VersionUnsupported)]
    [InlineData("target.missing", RepairResultCode.NotApplicable)]
    [InlineData("policy.managed", RepairResultCode.PolicyDenied)]
    public async Task PreparationBoundary_MapsApplicabilityAndPermissionWithoutExecute(
        string reasonCode,
        RepairResultCode expectedCode)
    {
        var orchestrator = new FakeOrchestrator
        {
            Preparation = request => new PreparationResult(
                ChangePreparationStatus.Blocked,
                null,
                reasonCode,
                reasonCode,
                null)
        };
        var executor = new OrchestratedRepairChangeExecutor(
            orchestrator,
            new NoopConfirmationAuthority());

        var result = await executor.ExecuteAsync(Item("repair.hardware_acceleration"), null, default);

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(reasonCode, result.ReasonCode);
        Assert.Equal(0, orchestrator.ExecuteCount);
    }

    [Fact]
    public async Task PermissionRevokedAfterPrepare_IsDeniedWithoutWriteSuccess()
    {
        var orchestrator = new FakeOrchestrator
        {
            Execute = plan => Receipt(plan, ChangeOutcome.Denied, "permission.denied", false)
        };
        var executor = new OrchestratedRepairChangeExecutor(
            orchestrator,
            new NoopConfirmationAuthority());

        var result = await executor.ExecuteAsync(Item("repair.hardware_acceleration"), null, default);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.PermissionDenied, result.Code);
        Assert.Equal("permission.denied", result.ReasonCode);
        Assert.Equal(1, orchestrator.ExecuteCount);
    }

    [Fact]
    public async Task PermissionExceptionDuringExecute_IsDeniedAndNotReportedAsSuccess()
    {
        var orchestrator = new FakeOrchestrator
        {
            Execute = _ => throw new UnauthorizedAccessException("administrator required")
        };
        var executor = new OrchestratedRepairChangeExecutor(
            orchestrator,
            new NoopConfirmationAuthority());

        var result = await executor.ExecuteAsync(Item("repair.hardware_acceleration"), null, default);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.PermissionDenied, result.Code);
        Assert.Equal("permission.denied", result.ReasonCode);
    }

    [Fact]
    public async Task CancellationBeforePrepare_IsCancelledAndZeroWrite()
    {
        var orchestrator = new FakeOrchestrator();
        var executor = new OrchestratedRepairChangeExecutor(
            orchestrator,
            new NoopConfirmationAuthority());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await executor.ExecuteAsync(
            Item("repair.hardware_acceleration"),
            null,
            cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.Cancelled, result.Code);
        Assert.Equal(0, orchestrator.PrepareCount);
        Assert.Equal(0, orchestrator.ExecuteCount);
    }

    [Fact]
    public async Task RepeatedNoChange_IsNotReportedAsFailureOrSecondWrite()
    {
        var prepareCount = 0;
        var orchestrator = new FakeOrchestrator
        {
            Preparation = request => ++prepareCount == 1
                ? FakeOrchestrator.Ready(request)
                : new PreparationResult(ChangePreparationStatus.NoChange, null, "change.no-op", "already applied", null)
        };
        var executor = new OrchestratedRepairChangeExecutor(
            orchestrator,
            new NoopConfirmationAuthority());

        var first = await executor.ExecuteAsync(Item("repair.hardware_acceleration"), null, default);
        var second = await executor.ExecuteAsync(Item("repair.hardware_acceleration"), null, default);

        Assert.Equal(RepairResultCode.Succeeded, first.Code);
        Assert.True(first.Success);
        Assert.Equal(RepairResultCode.NoChange, second.Code);
        Assert.True(second.Success);
        Assert.Equal(1, orchestrator.ExecuteCount);
    }

    private static RepairItem Item(string id) => new()
    {
        Id = id,
        Name = id,
        Description = id,
        CheckHealthAsync = () => Task.FromResult(false),
        RepairAsync = _ => Task.FromResult(RepairResult.Failed("legacy path must not run"))
    };

    private static BatchReceipt Receipt(
        PreparedChangePlan plan,
        ChangeOutcome outcome,
        string reasonCode,
        bool mutationOccurred) =>
        new(new OperationReceipt(
            Guid.NewGuid(),
            plan.PlanId,
            mutationOccurred ? Guid.NewGuid() : null,
            plan.Change.CapabilityId,
            plan.Change.Scope,
            outcome,
            plan.Change.BeforeFingerprint,
            plan.Change.TargetFingerprint,
            outcome == ChangeOutcome.SucceededVerified ? plan.Change.TargetFingerprint : null,
            reasonCode,
            reasonCode,
            mutationOccurred,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

    private sealed class NoopConfirmationAuthority : IChangeConfirmationAuthority
    {
        public Task<ConfirmationToken?> RequestAsync(
            PreparedChangePlan plan,
            string title,
            string summary,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConfirmationToken?>(null);

        public bool TryConsume(PreparedChangePlan plan, ConfirmationToken token) => true;
    }

    private sealed class FakeOrchestrator : IChangeOrchestrator
    {
        public Func<ChangeRequest, PreparationResult>? Preparation { get; init; }
        public Func<PreparedChangePlan, BatchReceipt>? Execute { get; init; }
        public int PrepareCount { get; private set; }
        public int ExecuteCount { get; private set; }

        public static PreparationResult Ready(ChangeRequest request)
        {
            var now = DateTimeOffset.UtcNow;
            var change = new PreparedCapabilityChange(
                request.CapabilityId,
                "1.0.0",
                request.Scope,
                "before",
                "before-fingerprint",
                "target",
                "target-fingerprint",
                $"resource:{request.CapabilityId}");
            var plan = new PreparedChangePlan(
                Guid.NewGuid(),
                change,
                Guid.NewGuid().ToString("N"),
                now,
                now.AddMinutes(5),
                false,
                false,
                true,
                ChangeRisk.LowReversible);
            return new PreparationResult(ChangePreparationStatus.Ready, plan, "ready", "ready", null);
        }

        public Task<StateObservation?> ObserveAsync(
            string capabilityId,
            TargetScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult<StateObservation?>(null);

        public Task<PreparationResult> PrepareAsync(
            ChangeRequest request,
            CancellationToken cancellationToken,
            bool registerPlan = true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareCount++;
            return Task.FromResult(Preparation?.Invoke(request) ?? Ready(request));
        }

        public Task<BatchReceipt> ExecuteAsync(
            PreparedChangePlan plan,
            ConfirmationToken confirmation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
            return Task.FromResult(Execute?.Invoke(plan)
                ?? Receipt(plan, ChangeOutcome.SucceededVerified, "execution.verified", true));
        }

        public Task<BatchReceipt> RestoreAsync(
            RestoreRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
