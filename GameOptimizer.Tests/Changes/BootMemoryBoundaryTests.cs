using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class BootMemoryBoundaryTests
{
    [Fact]
    public void Catalog_HasFourUniqueFixedFailClosedBindings()
    {
        var items = BootMemoryRepairCatalog.All;

        Assert.Equal(4, items.Count);
        Assert.Equal(4, items.Select(item => item.RepairItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(4, items.Select(item => item.CapabilityId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(4, items.Select(item => item.ResourceKey)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(items, item =>
        {
            Assert.StartsWith("repair.boot-memory.", item.CapabilityId, StringComparison.Ordinal);
            Assert.StartsWith("boot-memory:", item.ResourceKey, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(item.BlockReason));
            Assert.True(UnavailableRepairChangeExecutor.Instance.IsManaged(item.RepairItemId));
        });
    }

    [Fact]
    public void Infrastructure_RegistersExactlyOneFailClosedCapabilityPerCatalogItem()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();

        Assert.All(BootMemoryRepairCatalog.All, item =>
        {
            var capability = Assert.Single(capabilities, candidate =>
                string.Equals(candidate.Descriptor.CapabilityId, item.CapabilityId,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ChangeRisk.HighSystem, capability.Descriptor.Risk);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
        });
    }

    [Fact]
    public async Task RegistryItems_AreFailClosedEvenWhenLegacyDelegateIsCalledDirectly()
    {
        var registry = new RepairItemRegistry();
        registry.Initialize();
        var ids = BootMemoryRepairCatalog.All.Select(item => item.RepairItemId)
            .Append("repair.memory_compression");

        foreach (var id in ids)
        {
            var item = Assert.Single(registry.GetAll(), value =>
                string.Equals(value.Id, id, StringComparison.OrdinalIgnoreCase));
            var result = await item.RepairAsync(null);
            Assert.False(result.Success);
            Assert.Contains("Owner", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BootAndMemoryOptimizations_ArePolicyDenied()
    {
        var deniedIds = new[]
        {
            "cpu.force_high_timer_resolution"
        };
        var operations = new BuiltInOptimizationOperationProvider()
            .GetModules().SelectMany(module => module.Operations).ToArray();

        Assert.All(deniedIds, id =>
            Assert.IsType<PolicyDeniedOptimizationOperation>(
                Assert.Single(operations, operation => operation.Id == id)));

        // memory.enable_large_pages 与 memory.optimize_pagefile 已迁移为 typed
        // OrchestratedRegistryDwordOperation（纯注册表值 + 完整闭环），不再是 PolicyDenied。
        // memory.disable_compression 已迁移为 typed OrchestratedMmAgentOperation
        //（MMAgent cmdlet + 精确回读恢复闭环），不再是 PolicyDenied。
        // cpu.disable_dynamic_tick / cpu.configure_hpet / cpu.use_platform_tick / cpu.disable_tsc_sync
        // 已迁移为 typed OrchestratedBcdSettingOperation（SecureBoot 门禁 + 精确原值恢复），
        // 不再是 PolicyDenied。
        foreach (var id in new[]
                 {
                     "memory.enable_large_pages",
                     "memory.optimize_pagefile",
                     "memory.disable_compression",
                     "cpu.disable_dynamic_tick",
                     "cpu.configure_hpet",
                     "cpu.use_platform_tick",
                     "cpu.disable_tsc_sync"
                 })
        {
            var operation = Assert.Single(operations, operation => operation.Id == id);
            Assert.IsAssignableFrom<OrchestratedChangeOperation>(operation);
        }
    }
}
