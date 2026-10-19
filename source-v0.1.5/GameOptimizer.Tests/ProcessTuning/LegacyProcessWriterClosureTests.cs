using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Tests.ProcessTuning;

public sealed class LegacyProcessWriterClosureTests
{
    [Fact]
    public void CoreAffinityCompatibilityFacade_IsFailClosedWithoutPidOnlyRecoveryState()
    {
        var service = new CoreAffinityService(new CpuTopologyService());
        var profile = new GameCoreProfile();

        var apply = service.ApplyAffinity(null, profile);
        var applyByMask = service.ApplyAffinityByMask(null, 3);

        Assert.False(apply.IsSuccess);
        Assert.False(applyByMask.IsSuccess);
        Assert.Contains("ProcessTuningOwner", apply.Message, StringComparison.Ordinal);
        Assert.False(service.RestoreAffinity(4242));
        Assert.False(service.IsOptimized(4242));
        Assert.Null(service.GetState(4242));
        Assert.Empty(service.GetAllStates());
    }

    [Fact]
    public void LegacyCoreAndPriorityServices_ContainNoDirectProcessWriter()
    {
        var root = FindRepositoryRoot();
        var coreAffinity = File.ReadAllText(Path.Combine(
            root, "GameOptimizer.Core", "Services", "CoreAffinityService.cs"));
        var priority = File.ReadAllText(Path.Combine(
            root, "GameOptimizer.Shell", "Services", "GameProcessPriorityService.cs"));

        Assert.DoesNotContain("ProcessorAffinity =", coreAffinity, StringComparison.Ordinal);
        Assert.DoesNotContain("PriorityClass =", coreAffinity, StringComparison.Ordinal);
        Assert.DoesNotContain("SetThreadIdealProcessor", coreAffinity, StringComparison.Ordinal);
        Assert.DoesNotContain("DllImport", coreAffinity, StringComparison.Ordinal);

        Assert.DoesNotContain("SafeProcessHelper.SetPriorityClass", priority, StringComparison.Ordinal);
        Assert.DoesNotContain(".PriorityClass =", priority, StringComparison.Ordinal);
        Assert.DoesNotContain("GameStarted +=", priority, StringComparison.Ordinal);
        Assert.DoesNotContain("GameExited +=", priority, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate GameOptimizer.sln.");
    }
}
