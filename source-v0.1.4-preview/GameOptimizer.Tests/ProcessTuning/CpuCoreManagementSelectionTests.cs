// File: GameOptimizer.Tests/ProcessTuning/CpuCoreManagementSelectionTests.cs
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Tests.ProcessTuning;

public sealed class CpuCoreManagementSelectionTests
{
    [Fact]
    public void ProcessSelection_RequiresPidAndCreationTimeToMatch()
    {
        var original = new ProcessItem { Id = 4242, CreationTimeUtcTicks = 100, Name = "game" };
        var sameInstance = new ProcessItem { Id = 4242, CreationTimeUtcTicks = 100, Name = "game" };
        var reusedPid = new ProcessItem { Id = 4242, CreationTimeUtcTicks = 101, Name = "replacement" };

        Assert.True(sameInstance.IsSameInstance(original));
        Assert.False(reusedPid.IsSameInstance(original));
    }

    [Fact]
    public void ProcessSelection_DoesNotTrustPidWhenCreationTimeIsUnavailable()
    {
        var original = new ProcessItem { Id = 4242, CreationTimeUtcTicks = 0, Name = "game" };
        var candidate = new ProcessItem { Id = 4242, CreationTimeUtcTicks = 0, Name = "game" };

        Assert.False(candidate.IsSameInstance(original));
    }
}
