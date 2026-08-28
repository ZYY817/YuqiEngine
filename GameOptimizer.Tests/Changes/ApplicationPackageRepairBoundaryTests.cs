using GameOptimizer.Core.Services.ApplicationRepair;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.ApplicationRepair;
using GameOptimizer.Infrastructure.Windows.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class ApplicationPackageRepairBoundaryTests
{
    [Fact]
    public void CatalogIsFixedUniqueAndMutationDisabled()
    {
        Assert.Equal(6, ApplicationPackageRepairCatalog.All.Count);
        Assert.Equal(6, ApplicationPackageRepairCatalog.All.Select(item => item.Target).Distinct().Count());
        Assert.All(ApplicationPackageRepairCatalog.All, item => Assert.False(item.AllowsMutation));
        Assert.DoesNotContain(ApplicationPackageRepairCatalog.All,
            item => item.PackageName.Contains('*', StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiagnosticFailureAndMissingPackageFailClosed()
    {
        var failedOwner = new ApplicationPackageRepairOwner(new FakePlatform(querySucceeded: false));
        var failed = await failedOwner.DiagnoseAsync(ApplicationPackageRepairTarget.MicrosoftStore, default);
        Assert.Equal("DiagnosticFailed", failed.Status);

        var missingOwner = new ApplicationPackageRepairOwner(new FakePlatform(installed: false));
        var missing = await missingOwner.DiagnoseAsync(ApplicationPackageRepairTarget.MicrosoftStore, default);
        Assert.True(missing.Success);
        Assert.False(missing.IsApplicable);
        Assert.Equal("Unhealthy", missing.Status);
    }

    [Fact]
    public async Task EveryMutationRemainsBlockedAfterAuthoritativeRead()
    {
        var platform = new FakePlatform();
        var owner = new ApplicationPackageRepairOwner(platform);

        foreach (var target in ApplicationPackageRepairCatalog.All.Select(item => item.Target))
        {
            var result = await owner.PrepareAsync(target, ApplicationPackageRepairAction.Reregister, default);
            Assert.Equal("MutationBlocked", result.Status);
            Assert.False(result.Success);
        }

        Assert.Equal(5, platform.Reads);
    }

    [Fact]
    public async Task RepairRegistryExposesFixedItemsButNeverUsesLegacyWriter()
    {
        var owner = new ApplicationPackageRepairOwner(new FakePlatform());
        var registry = new RepairItemRegistry(applicationRepairOwner: owner);
        registry.Initialize();

        foreach (var id in new[] { "repair.store", "repair.uwp_apps", "repair.calculator", "repair.photos", "repair.game_bar", "repair.cortana" })
            Assert.NotNull(registry.GetById(id));

        var storeResult = await registry.GetById("repair.store")!.RepairAsync(null);
        var gameBarResult = await registry.GetById("repair.game_bar")!.RepairAsync(null);
        var cortanaResult = await registry.GetById("repair.cortana")!.RepairAsync(null);
        Assert.False(storeResult.Success);
        Assert.False(gameBarResult.Success);
        Assert.False(cortanaResult.Success);
        Assert.Equal("MutationBlocked", storeResult.Detail);
    }

    [Fact]
    public void CompositionHasOneOfficialReadOnlyPlatformAndOneOwner()
    {
        var services = new ServiceCollection();
        services.AddApplicationPackageRepairWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, item => item.ServiceType == typeof(IApplicationPackageDiagnosticPlatform));
        Assert.Single(services, item => item.ServiceType == typeof(IApplicationPackageRepairOwner));
        Assert.IsType<WindowsApplicationPackageDiagnosticPlatform>(provider.GetRequiredService<IApplicationPackageDiagnosticPlatform>());
        Assert.Same(provider.GetRequiredService<IApplicationPackageRepairOwner>(), provider.GetRequiredService<IApplicationPackageRepairOwner>());
    }

    [Fact]
    public void ProductionHasNoAppxPowerShellOrCacheResetWriter()
    {
        var root = FindRoot();
        var files = new[]
        {
            @"GameOptimizer.Core\Services\Repair\RepairOperations\UwpRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\GameOptimizationRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\PrivacyRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairHelper.cs",
            @"GameOptimizer.Core\Services\ProcessRunner.cs"
        };
        foreach (var file in files)
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("Get-AppxPackage", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Add-AppxPackage", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Remove-AppxPackage", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Reset-AppxPackage", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("wsreset.exe", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AppXManifest.xml", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FakePlatform(
        bool querySucceeded = true,
        bool installed = true,
        bool healthy = true) : IApplicationPackageDiagnosticPlatform
    {
        public int Reads { get; private set; }
        public Task<ApplicationPackageSnapshot> ReadAsync(ApplicationPackageDefinition definition, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            const string scope = "current-user";
            var fullName = installed ? definition.PackageName + "_1.0.0.0_neutral_test" : string.Empty;
            return Task.FromResult(new ApplicationPackageSnapshot(
                definition.Target, querySucceeded, installed, healthy, fullName, installed ? "1.0.0.0" : string.Empty,
                scope, querySucceeded ? ApplicationPackageRepairOwner.Fingerprint(definition.Target, fullName, "1.0.0.0", scope) : string.Empty,
                querySucceeded ? null : "FakeReadFailure"));
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "GameOptimizer.Core"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
