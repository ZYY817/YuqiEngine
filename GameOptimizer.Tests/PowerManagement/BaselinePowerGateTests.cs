using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Shell.Services.Baseline;
using GameOptimizer.Shell.Services.PowerManagement;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class BaselinePowerGateTests
{
    [Theory]
    [InlineData(BaselineGateDecisionKind.BlockedNeedBaseline, FirstRunBaselineState.NotStarted, false)]
    [InlineData(BaselineGateDecisionKind.ReadonlyOnly, FirstRunBaselineState.CompletedVerified, true)]
    [InlineData(BaselineGateDecisionKind.BlockedRiskNotAcknowledged, FirstRunBaselineState.PartialFailed, false)]
    public async Task NonAcknowledgedDecisionFailsClosed(
        BaselineGateDecisionKind kind,
        FirstRunBaselineState state,
        bool readOnly)
    {
        var service = new FakeBaselineGateService(CreateDecision(kind, state, readOnly));
        var gate = new BaselinePowerGate(service);
        var result = await gate.EnsureBaselineAsync(CreatePlan(), default);
        Assert.False(result.Allowed);
        Assert.Null(result.BaselineId);
    }

    [Fact]
    public async Task MissingBaselineWithRiskAcknowledgementProducesPlanBoundEvidenceId()
    {
        var gate = new BaselinePowerGate(new FakeBaselineGateService(
            CreateDecision(
                BaselineGateDecisionKind.AllowedWithRiskAcknowledgement,
                FirstRunBaselineState.NotStarted,
                false)));

        var result = await gate.EnsureBaselineAsync(CreatePlan(), default);

        Assert.True(result.Allowed);
        Assert.Equal(64, result.BaselineId?.Length);
        Assert.Contains("baseline decision", result.Reason);
    }

    [Fact]
    public async Task MissingAuthorizationEvidenceFailsClosed()
    {
        var decision = CreateDecision(
            BaselineGateDecisionKind.Allowed,
            FirstRunBaselineState.CompletedVerified,
            false) with { AuthorizationEvidenceId = null };
        var gate = new BaselinePowerGate(new FakeBaselineGateService(decision));
        var result = await gate.EnsureBaselineAsync(CreatePlan(), default);
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task VerifiedBaselineProducesStablePlanBoundEvidenceId()
    {
        var gate = new BaselinePowerGate(new FakeBaselineGateService(
            CreateDecision(BaselineGateDecisionKind.Allowed, FirstRunBaselineState.CompletedVerified, false)));
        var plan = CreatePlan();
        var first = await gate.EnsureBaselineAsync(plan, default);
        var second = await gate.EnsureBaselineAsync(plan, default);
        Assert.True(first.Allowed);
        Assert.Equal(64, first.BaselineId!.Length);
        Assert.Equal(first.BaselineId, second.BaselineId);
        var other = await gate.EnsureBaselineAsync(plan with { PlanHash = new string('A', 64) }, default);
        Assert.NotEqual(first.BaselineId, other.BaselineId);
    }

    private static BaselineGateDecision CreateDecision(
        BaselineGateDecisionKind kind,
        FirstRunBaselineState state,
        bool readOnly) =>
        new(kind, "baseline decision", new BaselineSafetyState
        {
            FirstRunState = state,
            IsReadonlyMode = readOnly,
            AuthorizedPackageId = "package-1",
            AuthorizedManifestSha256 = new string('B', 64),
            AuthorizedRecoverabilityCheckedUtc = new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc)
        }, new string('E', 64));

    private static PowerPreparedPlan CreatePlan()
    {
        var target = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        var plan = new PowerPreparedPlan(Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5),
            new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), target, new(600, 300), new(900, 420),
            "risk", "drawback", true, true, false, false, ResourceKey: PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource);
        return plan with { PlanHash = PowerManagementOwner.ComputeHash(plan) };
    }

    private sealed class FakeBaselineGateService : IBaselineGateService
    {
        private readonly BaselineGateDecision _decision;
        public FakeBaselineGateService(BaselineGateDecision decision) => _decision = decision;
        public Task<BaselineGateDecision> EnsureCanOptimizeAsync(string entryPoint, CancellationToken ct = default) => Task.FromResult(_decision);
        public Task<BaselineSafetyState> GetStateAsync(CancellationToken ct = default) => Task.FromResult(_decision.State);
        public Task SetReadonlyModeAsync(bool isReadonly, string entryPoint, string? reason = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkBaselineCompletedVerifiedAsync(string entryPoint, string packageId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkBaselineFailureAsync(string entryPoint, FirstRunBaselineState state, string? reason = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
