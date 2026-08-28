using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairRegistryCapabilityDefinitionTests
{
    [Fact]
    public void RepairDefinitions_AreUniqueAndExplicitlyConfirmed()
    {
        var definitions = new[]
        {
            RepairRegistryCapabilityDefinitions.GameModeAutoEnable,
            RepairRegistryCapabilityDefinitions.HardwareAccelerationReset,
            RepairRegistryCapabilityDefinitions.FeedbackReset
        };

        Assert.Equal(3, definitions.Select(value => value.CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(definitions, value => Assert.True(value.RequiresExplicitConfirmation));
        Assert.DoesNotContain(definitions, value =>
            string.Equals(value.CapabilityId, RepairChangeCapabilityIds.ExistingGameModeEnable, StringComparison.OrdinalIgnoreCase));
        Assert.True(RepairRegistryCapabilityDefinitions.DpiReset.RequiresExplicitConfirmation);
        Assert.True(RepairRegistryCapabilityDefinitions.VsyncReset.RequiresExplicitConfirmation);
    }

    [Fact]
    public void GameModeSecondValue_DoesNotDuplicateExistingValueOwner()
    {
        var definition = RepairRegistryCapabilityDefinitions.GameModeAutoEnable;

        Assert.Equal(@"SOFTWARE\Microsoft\GameBar", definition.SubKeyPath, ignoreCase: true);
        Assert.Equal("AutoGameModeEnabled", definition.ValueName);
        Assert.NotEqual("AllowAutoGameMode", definition.ValueName);
    }

    [Fact]
    public void InfrastructureRegistration_HasOneOwnerPerCapabilityAndDistinctGameModeValueLocks()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();

        Assert.Equal(
            capabilities.Length,
            capabilities.Select(value => value.Descriptor.CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var existing = Assert.Single(capabilities, value =>
            value.Descriptor.CapabilityId == RepairChangeCapabilityIds.ExistingGameModeEnable);
        var added = Assert.Single(capabilities, value =>
            value.Descriptor.CapabilityId == RepairChangeCapabilityIds.GameModeAutoEnable);
        Assert.False(string.Equals(
            existing.Descriptor.ResourceKey,
            added.Descriptor.ResourceKey,
            StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("/AllowAutoGameMode", existing.Descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("/AutoGameModeEnabled", added.Descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase);
    }
}
