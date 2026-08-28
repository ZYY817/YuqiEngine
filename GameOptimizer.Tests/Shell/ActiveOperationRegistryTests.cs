using GameOptimizer.Shell.Services;

namespace GameOptimizer.Tests.Shell;

public sealed class ActiveOperationRegistryTests
{
    [Fact]
    public async Task CancelableOperationIsCancelledAndDrainedBeforeClose()
    {
        var registry = new ActiveOperationRegistry();
        IActiveOperationLease? lease = null;
        var cancelCalls = 0;
        lease = registry.Begin("cleanup", "Cleanup", ActiveOperationRisk.Mutating,
            ActiveOperationClosePolicy.CancelAndDrain, () =>
            {
                Interlocked.Increment(ref cancelCalls);
                lease!.Complete(new ActiveOperationHandoff(["receipt-1"], "resume cleanup recovery"));
            });

        var result = await registry.CancelAndDrainAsync(TimeSpan.FromSeconds(1));

        Assert.True(result.CanClose);
        Assert.Equal(1, cancelCalls);
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task RecoveryCriticalBlockerPreventsCloseWithoutCancelling()
    {
        var registry = new ActiveOperationRegistry();
        var cancelCalls = 0;
        using var lease = registry.Begin("restore", "Restore", ActiveOperationRisk.RecoveryCritical,
            ActiveOperationClosePolicy.BlockUntilComplete);

        var result = await registry.CancelAndDrainAsync(TimeSpan.FromMilliseconds(20));

        Assert.False(result.CanClose);
        Assert.False(result.TimedOut);
        Assert.Single(result.Remaining);
        Assert.Equal(0, cancelCalls);
    }

    [Fact]
    public async Task DrainTimeoutKeepsOperationAndHandoffVisible()
    {
        var registry = new ActiveOperationRegistry();
        using var lease = registry.Begin("appx", "App removal", ActiveOperationRisk.Mutating,
            ActiveOperationClosePolicy.CancelAndDrain, () => { });
        lease.UpdateHandoff(new ActiveOperationHandoff(["receipt-2"], "review partial outcomes"));

        var result = await registry.CancelAndDrainAsync(TimeSpan.FromMilliseconds(20));

        Assert.False(result.CanClose);
        Assert.True(result.TimedOut);
        Assert.Equal("receipt-2", Assert.Single(Assert.Single(result.Remaining).Handoff!.ReceiptIds));
    }

    [Fact]
    public async Task CancellationCallbackFailureDoesNotSkipOtherOperations()
    {
        var registry = new ActiveOperationRegistry();
        using var failed = registry.Begin("one", "One", ActiveOperationRisk.Mutating,
            ActiveOperationClosePolicy.CancelAndDrain, () => throw new InvalidOperationException("synthetic"));
        IActiveOperationLease? completed = null;
        var secondCancelled = false;
        completed = registry.Begin("two", "Two", ActiveOperationRisk.Mutating,
            ActiveOperationClosePolicy.CancelAndDrain, () => { secondCancelled = true; completed!.Complete(); });

        var result = await registry.CancelAndDrainAsync(TimeSpan.FromMilliseconds(20));

        Assert.False(result.CanClose);
        Assert.True(secondCancelled);
        Assert.Contains(result.Errors, error => error.Contains("synthetic", StringComparison.Ordinal));
    }
}
