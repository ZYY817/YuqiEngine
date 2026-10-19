using GameOptimizer.Shell.Services.Baseline;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Baseline;

public sealed class BaselineGateRiskAcknowledgementTests
{
    [Fact]
    public async Task MissingBaseline_Acknowledged_AllowsOnlyThroughExplicitRiskDecision()
    {
        var prompt = new FakePrompt(true);
        var service = CreateService(new BaselineSafetyState
        {
            FirstRunState = FirstRunBaselineState.NotStarted
        }, prompt);

        var decision = await service.EnsureCanOptimizeAsync("test:apply");

        Assert.Equal(BaselineGateDecisionKind.AllowedWithRiskAcknowledgement, decision.Kind);
        Assert.True(decision.CanProceed);
        Assert.False(decision.HasVerifiedRecoveryBaseline);
        Assert.Equal(1, prompt.CallCount);
        Assert.Equal("test:apply", prompt.LastRequest?.EntryPoint);
        Assert.Contains("恢复", prompt.LastRequest?.RecoveryCapabilityDescription);
        Assert.Equal(64, decision.AuthorizationEvidenceId?.Length);
    }

    [Fact]
    public async Task MissingBaseline_Declined_BlocksWithoutPretendingBackupIsRequired()
    {
        var prompt = new FakePrompt(false);
        var service = CreateService(new BaselineSafetyState
        {
            FirstRunState = FirstRunBaselineState.PartialFailed
        }, prompt);

        var decision = await service.EnsureCanOptimizeAsync("test:apply");

        Assert.Equal(BaselineGateDecisionKind.BlockedRiskNotAcknowledged, decision.Kind);
        Assert.False(decision.CanProceed);
        Assert.Contains("未确认", decision.Reason);
        Assert.Equal(1, prompt.CallCount);
    }

    [Fact]
    public async Task VerifiedBaseline_AllowsWithoutShowingRiskPrompt()
    {
        var checkedUtc = new DateTime(2026, 8, 27, 1, 2, 3, DateTimeKind.Utc);
        var state = new BaselineSafetyState
        {
            FirstRunState = FirstRunBaselineState.CompletedVerified,
            AuthorizedPackageId = "PKG-1",
            AuthorizedManifestSha256 = "ABC",
            AuthorizedRecoverabilityCheckedUtc = checkedUtc
        };
        var prompt = new FakePrompt(false);
        var verifier = new FakeVerifier(new BaselineAuthorizationVerification(
            true,
            new BaselineAuthorizationEvidence("PKG-1", "ABC", checkedUtc),
            string.Empty,
            "ok"));
        var service = new BaselineGateService(
            new FakeRepository(state),
            verifier,
            prompt,
            NullLogger<BaselineGateService>.Instance);

        var decision = await service.EnsureCanOptimizeAsync("test:apply");

        Assert.Equal(BaselineGateDecisionKind.Allowed, decision.Kind);
        Assert.True(decision.CanProceed);
        Assert.True(decision.HasVerifiedRecoveryBaseline);
        Assert.Equal(0, prompt.CallCount);
    }

    [Fact]
    public async Task PromptFailure_BlocksBecauseConfirmationWasNotObtained()
    {
        var service = CreateService(
            new BaselineSafetyState { FirstRunState = FirstRunBaselineState.NotStarted },
            new FakePrompt(new InvalidOperationException("UI unavailable")));

        var decision = await service.EnsureCanOptimizeAsync("test:apply");

        Assert.Equal(BaselineGateDecisionKind.BlockedRiskNotAcknowledged, decision.Kind);
        Assert.False(decision.CanProceed);
        Assert.Contains("无法取得", decision.Reason);
    }

    private static BaselineGateService CreateService(
        BaselineSafetyState state,
        IBaselineRiskAcknowledgementPrompt prompt) =>
        new(
            new FakeRepository(state),
            new FakeVerifier(BaselineAuthorizationVerification.Failed("baseline.gate_not_completed", "not completed")),
            prompt,
            NullLogger<BaselineGateService>.Instance);

    private sealed class FakeRepository : IBaselineAuthorizationRepository
    {
        private readonly BaselineSafetyState _state;
        public FakeRepository(BaselineSafetyState state) => _state = state;
        public Task<BaselineSafetyState> LoadAsync(CancellationToken ct = default) => Task.FromResult(_state);
        public Task SaveAsync(BaselineSafetyState state, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeVerifier : IBaselineAuthorizationPackageVerifier
    {
        private readonly BaselineAuthorizationVerification _result;
        public FakeVerifier(BaselineAuthorizationVerification result) => _result = result;
        public Task<BaselineAuthorizationVerification> VerifyAsync(string packageId, CancellationToken ct = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakePrompt : IBaselineRiskAcknowledgementPrompt
    {
        private readonly bool _result;
        private readonly Exception? _exception;

        public FakePrompt(bool result) => _result = result;
        public FakePrompt(Exception exception) => _exception = exception;

        public int CallCount { get; private set; }
        public BaselineRiskAcknowledgementRequest? LastRequest { get; private set; }

        public Task<bool> ConfirmAsync(
            BaselineRiskAcknowledgementRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return _exception is null
                ? Task.FromResult(_result)
                : Task.FromException<bool>(_exception);
        }
    }
}
