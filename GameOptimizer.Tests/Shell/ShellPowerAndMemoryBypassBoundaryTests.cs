using GameOptimizer.Shell.Services;

namespace GameOptimizer.Tests.Shell;

public sealed class ShellPowerAndMemoryBypassBoundaryTests
{
    [Fact]
    public async Task Legacy_power_facade_fails_closed_without_starting_a_platform_command()
    {
        var service = new UnifiedPowerService();

        var plan = await service.GetOrCreateGameOptimizerPlanAsync();
        var activated = await service.ActivatePlanAsync(Guid.NewGuid());
        var activePlan = await service.GetActivePlanGuidAsync();

        Assert.False(plan.IsSuccess);
        Assert.False(activated);
        Assert.Null(activePlan);
        Assert.Contains("Power owner contract", plan.ErrorMessage, StringComparison.Ordinal);

        var source = ReadShellSource("Services", "UnifiedPowerService.cs");
        Assert.DoesNotContain("RunProcessAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("powercfg", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PowerPlanTemplateService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerServiceDiagnostics", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_power_facade_preserves_cancellation_without_a_fail_open_result()
    {
        var service = new UnifiedPowerService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetOrCreateGameOptimizerPlanAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ActivatePlanAsync(Guid.NewGuid(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetActivePlanGuidAsync(cts.Token));
    }

    [Fact]
    public void Game_mode_memory_entry_is_a_safe_block_and_contains_no_process_or_gc_execution()
    {
        var source = ReadShellSource("Views", "GameModeHubPage.xaml.cs");

        Assert.Contains("安全阻断", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.GetCurrentProcess", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileName = \"powershell\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EmptyWorkingSet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DllImport(\"psapi.dll\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.Collect", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.WaitForPendingFinalizers", source, StringComparison.Ordinal);
    }

    private static string ReadShellSource(params string[] segments)
    {
        var parts = new[] { FindRepositoryRoot(), "GameOptimizer.Shell" }.Concat(segments).ToArray();
        return File.ReadAllText(Path.Combine(parts));
    }

    private static string FindRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var sourceDirectory = new FileInfo(sourceFile).Directory?.Parent?.Parent;
        if (sourceDirectory is not null && File.Exists(Path.Combine(sourceDirectory.FullName, "GameOptimizer.sln")))
            return sourceDirectory.FullName;
        var workingDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        if (File.Exists(Path.Combine(workingDirectory.FullName, "GameOptimizer.sln")))
            return workingDirectory.FullName;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate GameOptimizer.sln.");
    }
}
