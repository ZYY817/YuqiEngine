using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class SystemMaintenanceBoundaryTests
{
    [Fact]
    public void Catalog_HasTwentyFiveUniqueFixedRepairBindings()
    {
        var items = SystemMaintenanceRepairCatalog.All;

        Assert.Equal(25, items.Count);
        Assert.Equal(25, items.Select(item => item.RepairItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(25, items.Select(item => item.CapabilityId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(items, item =>
        {
            Assert.StartsWith("repair.system-maintenance.", item.CapabilityId, StringComparison.Ordinal);
            Assert.StartsWith("system-maintenance:", item.ResourceKey, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(item.BlockReason));
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

        Assert.All(SystemMaintenanceRepairCatalog.All, item =>
        {
            var capability = Assert.Single(capabilities, candidate =>
                string.Equals(candidate.Descriptor.CapabilityId, item.CapabilityId,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ChangeRisk.HighSystem, capability.Descriptor.Risk);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
            Assert.True(UnavailableRepairChangeExecutor.Instance.IsManaged(item.RepairItemId));
        });
    }

    [Fact]
    public async Task RegistryItems_AreFailClosedEvenWhenDelegateIsCalledDirectly()
    {
        var registry = new RepairItemRegistry();
        registry.Initialize();

        foreach (var definition in SystemMaintenanceRepairCatalog.All)
        {
            var item = Assert.Single(registry.GetAll(), value =>
                string.Equals(value.Id, definition.RepairItemId, StringComparison.OrdinalIgnoreCase));
            var result = await item.RepairAsync(null);
            Assert.False(result.Success);
            Assert.Contains("Owner", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WindowsUpdateAndServicingOptimizations_AreTypedHighSystemExplicitlyConfirmed()
    {
        var migratedIds = new[]
        {
            "tasks.disable_cleanup_tasks",
            "tasks.disable_policy_updates",
            "tasks.disable_windows_update"
        };
        var operations = new BuiltInOptimizationOperationProvider()
            .GetModules().SelectMany(module => module.Operations).ToArray();

        // 这三个维护/更新任务已按设计迁移为 typed 能力，但保留最高风险层级 + 显式确认门禁：
        // 它们不再 PolicyDenied，而是可由用户自主决定、必须逐项确认后才写入。
        Assert.All(migratedIds, id =>
        {
            var operation = Assert.Single(operations, candidate => candidate.Id == id);
            Assert.IsType<OrchestratedScheduledTaskOptimizationOperation>(operation);
            Assert.True(operation.RequiresExplicitConfirmation);
            Assert.True(operation.RequiresAdmin);
        });

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();

        Assert.All(migratedIds, id =>
        {
            var capability = Assert.Single(capabilities, candidate =>
                string.Equals(candidate.Descriptor.CapabilityId, id, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ChangeRisk.HighSystem, capability.Descriptor.Risk);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
            Assert.Contains(TargetScopeKind.Machine, capability.Descriptor.SupportedScopes);
        });

        // basic.disable_windows_update 已迁移为 typed 注册表策略（NoAutoUpdate=1），不再阻断。
        Assert.IsType<OrchestratedRegistryDwordOperation>(
            Assert.Single(operations, operation => operation.Id == "basic.disable_windows_update"));
    }
}
