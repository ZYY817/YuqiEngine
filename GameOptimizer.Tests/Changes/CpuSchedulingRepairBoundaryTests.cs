using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class CpuSchedulingRepairBoundaryTests
{
    [Fact]
    public void Catalog_SeparatesSystemSchedulingFromProcessAffinity()
    {
        var items = CpuSchedulingRepairCatalog.All;

        Assert.Equal(2, items.Count);
        Assert.Equal(2, items.Select(item => item.RepairItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, items.Select(item => item.CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, items.Select(item => item.ResourceKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(items, item => item.ResourceKey == "cpu-scheduling:win32-priority-separation");
        Assert.Contains(items, item => item.ResourceKey == "cpu-affinity:all-processes");
        Assert.All(items, item => Assert.True(UnavailableRepairChangeExecutor.Instance.IsManaged(item.RepairItemId)));
    }

    [Fact]
    public void Infrastructure_RegistersOneFailClosedCapabilityPerSemantic()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();

        Assert.All(CpuSchedulingRepairCatalog.All, item =>
        {
            var capability = Assert.Single(capabilities, candidate =>
                string.Equals(candidate.Descriptor.CapabilityId, item.CapabilityId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ChangeRisk.HighSystem, capability.Descriptor.Risk);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
        });
    }

    [Theory]
    [InlineData("repair.cpu_scheduling")]
    [InlineData("repair.cpu_affinity")]
    public async Task LegacyDelegates_FailClosedWithoutMutation(string repairItemId)
    {
        var registry = new RepairItemRegistry();
        registry.Initialize();
        var item = Assert.Single(registry.GetAll(), candidate => candidate.Id == repairItemId);

        var result = await item.RepairAsync(null);

        Assert.False(result.Success);
        Assert.Contains("Owner", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("阻断", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("process-exited")]
    [InlineData("topology-changed")]
    [InlineData("access-denied")]
    [InlineData("state-drift")]
    [InlineData("restore-identity-missing")]
    public async Task UnsupportedRuntimeConditions_RemainBlockedBeforeReadOrWrite(string scenario)
    {
        var capability = new UnavailableRepairPlatformCapability(
            "repair.cpu-affinity.clear-all",
            $"cpu-affinity:test:{scenario}");

        var assessment = await capability.ProbeAsync(TargetScope.Machine("test"), CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Unsupported, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        Assert.Equal("platform.adapter-not-verified", assessment.ReasonCode);
    }
}
