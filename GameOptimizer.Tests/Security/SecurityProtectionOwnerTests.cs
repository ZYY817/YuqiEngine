using GameOptimizer.Core.Security;

namespace GameOptimizer.Tests.Security;

public sealed class SecurityProtectionOwnerTests
{
    [Fact]
    public async Task WeakeningProtectionIsPolicyDeniedWithoutInspectingPlatform()
    {
        var inspector = new FakeInspector();
        var owner = new SecurityProtectionOwner(inspector, new FakeTimeProvider());

        var result = await owner.EvaluateAsync(
            new SecurityProtectionRequest(SecurityProtectionArea.DefenderRealtime, SecurityProtectionIntent.WeakenProtection),
            default);

        Assert.Equal(SecurityProtectionStatus.PolicyDenied, result.Status);
        Assert.False(result.Allowed);
        Assert.Equal(0, inspector.Calls);
        Assert.Equal(SecurityProtectionIdentity.ResourceKey, result.ResourceKey);
    }

    [Theory]
    [InlineData(false, 22631, true, false, false, false, SecurityProtectionStatus.Unsupported)]
    [InlineData(true, 18362, true, false, false, false, SecurityProtectionStatus.Unsupported)]
    [InlineData(true, 22631, false, false, false, false, SecurityProtectionStatus.PermissionDenied)]
    [InlineData(true, 22631, true, true, false, false, SecurityProtectionStatus.ThirdPartySecurityProvider)]
    [InlineData(true, 22631, true, false, true, false, SecurityProtectionStatus.ManagedByPolicy)]
    [InlineData(true, 22631, true, false, false, true, SecurityProtectionStatus.TamperProtected)]
    public async Task RestoreEvaluationFailsClosedForEnvironmentOwnership(
        bool windows,
        int build,
        bool admin,
        bool thirdParty,
        bool policy,
        bool tamper,
        SecurityProtectionStatus expected)
    {
        var inspector = new FakeInspector
        {
            Environment = new(windows, build, admin, thirdParty, policy, tamper, true, "state-A")
        };
        var result = await new SecurityProtectionOwner(inspector).EvaluateAsync(
            new SecurityProtectionRequest(SecurityProtectionArea.Firewall, SecurityProtectionIntent.RestoreProtection), default);
        Assert.Equal(expected, result.Status);
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task PartialStateAndRestoreDriftAreBlocked()
    {
        var partialInspector = new FakeInspector
        {
            Environment = HealthyEnvironment() with { IsStateComplete = false }
        };
        var partial = await new SecurityProtectionOwner(partialInspector).EvaluateAsync(
            new SecurityProtectionRequest(SecurityProtectionArea.Firewall, SecurityProtectionIntent.RestoreProtection), default);
        Assert.Equal(SecurityProtectionStatus.PartialState, partial.Status);

        var driftInspector = new FakeInspector { Environment = HealthyEnvironment() };
        var drift = await new SecurityProtectionOwner(driftInspector).EvaluateAsync(
            new SecurityProtectionRequest(SecurityProtectionArea.SmartScreen, SecurityProtectionIntent.RestoreProtection, "state-before"), default);
        Assert.Equal(SecurityProtectionStatus.DriftBlocked, drift.Status);
    }

    [Fact]
    public async Task EligibleRestoreStillFailsClosedWhenRecoveryAdapterIsUnproven()
    {
        var result = await new SecurityProtectionOwner(new FakeInspector { Environment = HealthyEnvironment() })
            .EvaluateAsync(new SecurityProtectionRequest(
                SecurityProtectionArea.ControlledFolderAccess,
                SecurityProtectionIntent.RestoreProtection,
                "state-A"), default);
        Assert.Equal(SecurityProtectionStatus.RecoveryUnproven, result.Status);
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task TimeoutCancellationAndPlatformFailureAreDistinctAndNeverAllowed()
    {
        var timeoutInspector = new FakeInspector { WaitForCancellation = true };
        var timeoutOwner = new SecurityProtectionOwner(timeoutInspector, timing: new(TimeSpan.FromMilliseconds(30)));
        var timedOut = await timeoutOwner.EvaluateAsync(
            new SecurityProtectionRequest(SecurityProtectionArea.Firewall, SecurityProtectionIntent.RestoreProtection), default);
        Assert.Equal(SecurityProtectionStatus.TimedOut, timedOut.Status);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = await timeoutOwner.EvaluateAsync(
            new SecurityProtectionRequest(SecurityProtectionArea.Firewall, SecurityProtectionIntent.RestoreProtection), cts.Token);
        Assert.Equal(SecurityProtectionStatus.Cancelled, cancelled.Status);

        var failed = await new SecurityProtectionOwner(new FakeInspector { Failure = new InvalidOperationException("fake") })
            .EvaluateAsync(new SecurityProtectionRequest(SecurityProtectionArea.Firewall, SecurityProtectionIntent.RestoreProtection), default);
        Assert.Equal(SecurityProtectionStatus.PlatformFailure, failed.Status);
        Assert.All(new[] { timedOut, cancelled, failed }, decision => Assert.False(decision.Allowed));
    }

    private static SecurityProtectionEnvironment HealthyEnvironment() =>
        new(true, 22631, true, false, false, false, true, "state-A");

    private sealed class FakeInspector : ISecurityProtectionInspector
    {
        public SecurityProtectionEnvironment Environment { get; set; } = HealthyEnvironment();
        public bool WaitForCancellation { get; set; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }

        public async Task<SecurityProtectionEnvironment> InspectAsync(SecurityProtectionArea area, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Environment;
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
    }
}
