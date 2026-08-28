namespace GameOptimizer.Tests.Shell;

public sealed class StartupResponsivenessContractTests
{
    [Fact]
    public void DashboardModuleConstructionDoesNotSynchronouslyWaitForHardwareDetection()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "GameOptimizer.Shell",
            "ViewModels",
            "DashboardViewModel.cs"));

        Assert.DoesNotContain("ResolveHardwareFilter()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("orDetectAsync.Wait", source, StringComparison.Ordinal);
        Assert.DoesNotContain("orDetectAsync.Result", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestAdvertisesOnlyTheSupportedWindowsGeneration()
    {
        var root = RepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "app.manifest"));

        Assert.Contains("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{1f676c76-80e1-4239-95bb-83d0f6d0da78}", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{35138b9a-5d96-4fbd-8e2d-a2440225f93a}", manifest, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Directory.GetParent(Path.GetDirectoryName(sourceFile)!)!.Parent!.FullName;
}
