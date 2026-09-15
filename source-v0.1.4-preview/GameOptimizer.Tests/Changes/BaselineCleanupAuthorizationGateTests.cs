using GameOptimizer.Core.Services.SystemCleanup;
using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Tests.Changes;

public sealed class BaselineCleanupAuthorizationGateTests
{
    [Theory]
    [InlineData(BaselineGateDecisionKind.Allowed, true)]
    [InlineData(BaselineGateDecisionKind.AllowedWithRiskAcknowledgement, true)]
    [InlineData(BaselineGateDecisionKind.ReadonlyOnly, false)]
    [InlineData(BaselineGateDecisionKind.BlockedNeedBaseline, false)]
    [InlineData(BaselineGateDecisionKind.BlockedRiskNotAcknowledged, false)]
    public async Task AdapterMapsOnlyVerifiedAllowedDecision(
        BaselineGateDecisionKind kind,
        bool expectedAllowed)
    {
        var baseline = new FakeBaselineGate(kind);
        var adapter = new BaselineCleanupAuthorizationGate(baseline);
        var preview = new CleanupPreview(
            "user_temp",
            [new CleanupPreviewEntry(@"C:\temp\a.bin", @"C:\temp", 3, DateTime.UnixEpoch)],
            DateTimeOffset.UnixEpoch);

        CleanupAuthorizationDecision decision = await adapter.AuthorizeAsync(preview, CancellationToken.None);

        Assert.Equal(expectedAllowed, decision.Allowed);
        Assert.Equal($"system-cleanup:{preview.ItemId}:{preview.Fingerprint}", baseline.LastEntryPoint);
    }

    private sealed class FakeBaselineGate : IBaselineGateService
    {
        private readonly BaselineGateDecisionKind _kind;
        public FakeBaselineGate(BaselineGateDecisionKind kind) => _kind = kind;
        public string? LastEntryPoint { get; private set; }

        public Task<BaselineGateDecision> EnsureCanOptimizeAsync(string entryPoint, CancellationToken ct = default)
        {
            LastEntryPoint = entryPoint;
            return Task.FromResult(new BaselineGateDecision(_kind, "test", new BaselineSafetyState()));
        }

        public Task<BaselineSafetyState> GetStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new BaselineSafetyState());

        public Task SetReadonlyModeAsync(bool isReadonly, string entryPoint, string? reason = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkBaselineCompletedVerifiedAsync(string entryPoint, string packageId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkBaselineFailureAsync(string entryPoint, FirstRunBaselineState state, string? reason = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
