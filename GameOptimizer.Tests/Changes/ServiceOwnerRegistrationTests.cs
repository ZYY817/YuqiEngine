using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.SystemServices;
using GameOptimizer.Infrastructure.Windows.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class ServiceOwnerRegistrationTests
{
    [Fact]
    public void Infrastructure_RegistersExactlyOneOwnerPerRepairServiceCapability()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();
        var ids = new[]
        {
            RepairChangeCapabilityIds.ServicesReset,
            RepairChangeCapabilityIds.BitsEnsureRunning,
            RepairChangeCapabilityIds.CryptSvcEnsureRunning,
            RepairChangeCapabilityIds.AppInfoEnsureRunning,
            RepairChangeCapabilityIds.ThemesEnsureRunning,
            RepairChangeCapabilityIds.AudioServiceEnsureRunning,
            RepairChangeCapabilityIds.ShellHardwareDetectionEnsureRunning,
            RepairChangeCapabilityIds.PlugPlayEnsureRunning,
            RepairChangeCapabilityIds.NetworkListEnsureRunning,
            RepairChangeCapabilityIds.NetworkLocationEnsureRunning,
            RepairChangeCapabilityIds.RpcSsEnsureRunning
        };

        Assert.All(ids, id => Assert.Single(capabilities, capability =>
            string.Equals(capability.Descriptor.CapabilityId, id, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(capabilities.Length, capabilities
            .Select(value => value.Descriptor.CapabilityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
    }

    [Fact]
    public void TypedServiceOwners_HaveFixedServiceResourceAndHighRiskConfirmation()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var typed = provider.GetServices<IChangeCapability>().OfType<ServiceChangeCapability>().ToArray();

        Assert.Equal(28, typed.Length);
        Assert.Equal(28, typed.Select(value => value.Descriptor.ResourceKey).Distinct(StringComparer.Ordinal).Count());
        Assert.All(typed, capability =>
        {
            Assert.Equal(ChangeRisk.HighSystem, capability.Descriptor.Risk);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
            Assert.StartsWith("service:", capability.Descriptor.ResourceKey, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AuthorizedRepairServiceSource_HasNoLegacyMutationCommand()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Core\Services\Repair\RepairOperations\ServicesRepairs.cs"));
        var forbidden = new[]
        {
            "RunCommandAsync(\"sc\", $\"config",
            "RunCommandAsync(\"sc\", \"config",
            "RunCommandAsync(\"net\", \"start",
            "RunCommandAsync(\"net\", \"stop",
            "TryStartService(",
            "TryStopService(",
            "ServiceController("
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuiltInCatalog_HasNoLegacyServiceToggleWriter()
    {
        var operations = new BuiltInOptimizationOperationProvider()
            .GetModules()
            .SelectMany(module => module.Operations)
            .ToArray();

        Assert.DoesNotContain(operations, operation => operation is ServiceToggleOperation or MultiServiceToggleOperation);
        Assert.Equal(15, operations.OfType<OrchestratedServiceOptimizationOperation>().Count());
        Assert.Equal(3, operations.OfType<MultiServiceCompositeOperation>().Count());
        Assert.Empty(operations.OfType<FailClosedServiceOptimizationOperation>());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
