using System.Collections;
using System.Reflection;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Tests.Optimization;

public sealed class OperationLockShutdownRaceTests
{
    [Fact]
    public async Task StaleCleanupDoesNotRemoveActiveLeaseAndReleasesIdleEntry()
    {
        var activeResource = $"test:stale-active:{Guid.NewGuid():N}";
        using var activeOwner = await OperationLock.TryAcquireAsync(
            activeResource,
            TimeSpan.FromSeconds(2));
        Assert.NotNull(activeOwner);

        AgeEntry(activeResource);
        InvokeCleanup();
        Assert.True(EntryExists(activeResource));

        var blockedWhileActive = await OperationLock.TryAcquireAsync(
            activeResource,
            TimeSpan.FromMilliseconds(50));
        Assert.Null(blockedWhileActive);

        activeOwner!.Dispose();
        var idleResource = $"test:stale-idle:{Guid.NewGuid():N}";
        using var idleOwner = await OperationLock.TryAcquireAsync(
            idleResource,
            TimeSpan.FromSeconds(2));
        Assert.NotNull(idleOwner);
        idleOwner!.Dispose();

        AgeEntry(idleResource);
        InvokeCleanup();
        Assert.False(EntryExists(idleResource));

        using var reacquired = await OperationLock.TryAcquireAsync(
            idleResource,
            TimeSpan.FromSeconds(2));
        Assert.NotNull(reacquired);
    }

    private static void AgeEntry(string resourceId)
    {
        var locks = (IDictionary)typeof(OperationLock)
            .GetField("_locks", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var entry = locks[resourceId];
        Assert.NotNull(entry);
        entry!.GetType().GetProperty("LastAccess")!.SetValue(
            entry,
            DateTime.UtcNow - TimeSpan.FromMinutes(11));
    }

    private static void InvokeCleanup() =>
        typeof(OperationLock)
            .GetMethod("CleanupStaleLocks", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null);

    private static bool EntryExists(string resourceId)
    {
        var locks = (IDictionary)typeof(OperationLock)
            .GetField("_locks", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        return locks.Contains(resourceId);
    }

}
