using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class FileChangeCoordinationTests
{
    [Fact]
    public async Task PlanClaim_IsDurableAcrossStoreInstances()
    {
        var root = EvidenceDirectory(nameof(PlanClaim_IsDurableAcrossStoreInstances));
        var firstStore = new FileChangeStateStore(root);
        var secondStore = new FileChangeStateStore(root);
        var plan = FileChangeCoordinationTestPlan.Create();

        await firstStore.RegisterAsync(plan, CancellationToken.None);
        var firstClaim = await firstStore.TryClaimAsync(plan.PlanId, plan.PlanHash, CancellationToken.None);
        var repeatedClaim = await secondStore.TryClaimAsync(plan.PlanId, plan.PlanHash, CancellationToken.None);

        Assert.Equal(PlanClaimStatus.Claimed, firstClaim);
        Assert.Equal(PlanClaimStatus.AlreadyClaimed, repeatedClaim);
    }

    [Fact]
    public async Task ResourceLock_IsExclusiveAcrossProviderInstances()
    {
        var root = EvidenceDirectory(nameof(ResourceLock_IsExclusiveAcrossProviderInstances));
        var firstProvider = new FileChangeResourceLockProvider(root);
        var secondProvider = new FileChangeResourceLockProvider(root);
        var firstLease = await firstProvider.TryAcquireAsync(
            "registry:test/value",
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.NotNull(firstLease);
        var blockedLease = await secondProvider.TryAcquireAsync(
            "registry:test/value",
            TimeSpan.FromMilliseconds(120),
            CancellationToken.None);
        Assert.Null(blockedLease);

        await firstLease!.DisposeAsync();
        var acquiredAfterRelease = await secondProvider.TryAcquireAsync(
            "registry:test/value",
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        Assert.NotNull(acquiredAfterRelease);
        await acquiredAfterRelease!.DisposeAsync();
    }

    private static string EvidenceDirectory(string testName) =>
        Path.Combine(AppContext.BaseDirectory, "coordination-test-evidence", testName, Guid.NewGuid().ToString("N"));
}
