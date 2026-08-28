using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairSixthBatchCapabilityTests
{
    [Fact]
    public void RegistryDefinitions_AreTypedMachineOwnersWithConfirmationAndUniqueResources()
    {
        var definitions = new[]
        {
            RepairRegistryCapabilityDefinitions.HagsEnable,
            RepairRegistryCapabilityDefinitions.DiagnosticDataReset,
            RepairRegistryCapabilityDefinitions.FastStartupEnable
        };

        Assert.Equal(3, definitions.Select(value => value.CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(definitions, definition =>
        {
            Assert.Equal(RegistryDwordHive.LocalMachine, definition.Hive);
            Assert.True(definition.RequiresExplicitConfirmation);
        });
        Assert.Equal(3, definitions.Select(value => $"{value.SubKeyPath}/{value.ValueName}").Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            RepairRegistryCapabilityDefinitions.DiagnosticDataReset.SubKeyPath,
            ignoreCase: true);
    }

    [Fact]
    public void Infrastructure_HasOneOwnerForEverySixthBatchCapability()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();
        var ids = new[]
        {
            RepairChangeCapabilityIds.HagsEnable,
            RepairChangeCapabilityIds.DiagnosticDataReset,
            RepairChangeCapabilityIds.FastStartupEnable,
            RepairChangeCapabilityIds.TelemetryServiceReset,
            RepairChangeCapabilityIds.MemoryCompressionEnable,
            RepairChangeCapabilityIds.HibernationEnable,
            RepairChangeCapabilityIds.PowerPlansReset,
            RepairChangeCapabilityIds.HighPerformancePlanRestore,
            RepairChangeCapabilityIds.UltimatePerformancePlanRestore,
            RepairChangeCapabilityIds.SleepPolicyReset,
            RepairChangeCapabilityIds.CoreParkingReset,
            RepairChangeCapabilityIds.CpuFrequencyReset,
            RepairChangeCapabilityIds.BatteryReportGenerate,
            RepairChangeCapabilityIds.PowerTroubleshooterLaunch
        };

        Assert.All(ids, id => Assert.Single(capabilities, capability =>
            string.Equals(capability.Descriptor.CapabilityId, id, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(capabilities.Length, capabilities.Select(value => value.Descriptor.CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(RepairChangeCapabilityIds.TelemetryServiceReset)]
    [InlineData(RepairChangeCapabilityIds.MemoryCompressionEnable)]
    [InlineData(RepairChangeCapabilityIds.HibernationEnable)]
    [InlineData(RepairChangeCapabilityIds.PowerPlansReset)]
    [InlineData(RepairChangeCapabilityIds.HighPerformancePlanRestore)]
    [InlineData(RepairChangeCapabilityIds.UltimatePerformancePlanRestore)]
    [InlineData(RepairChangeCapabilityIds.SleepPolicyReset)]
    [InlineData(RepairChangeCapabilityIds.CoreParkingReset)]
    [InlineData(RepairChangeCapabilityIds.CpuFrequencyReset)]
    [InlineData(RepairChangeCapabilityIds.BatteryReportGenerate)]
    [InlineData(RepairChangeCapabilityIds.PowerTroubleshooterLaunch)]
    public async Task UnverifiedPlatformAdapter_FailsClosedAtProbe(string capabilityId)
    {
        var capability = new UnavailableRepairPlatformCapability(capabilityId, $"test:{capabilityId}");

        var assessment = await capability.ProbeAsync(TargetScope.Machine(Environment.MachineName), CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Unsupported, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        Assert.Equal("platform.adapter-not-verified", assessment.ReasonCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            capability.ReadAsync(TargetScope.Machine(Environment.MachineName), CancellationToken.None));
    }
}
