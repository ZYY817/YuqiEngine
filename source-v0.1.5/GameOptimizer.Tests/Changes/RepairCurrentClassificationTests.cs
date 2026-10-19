using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairCurrentClassificationTests
{
    [Fact]
    public void StaticRegistry_HasExpectedManagedAndLegacyCounts()
    {
        var registry = new RepairItemRegistry();
        registry.Initialize();
        var items = registry.GetAll();
        static bool IsManaged(string id) =>
            UnavailableRepairChangeExecutor.Instance.IsManaged(id) || RepairSpecializedOwnerCatalog.Contains(id);

        var executorManaged = items.Where(item => UnavailableRepairChangeExecutor.Instance.IsManaged(item.Id)).ToArray();
        var specializedManaged = items.Where(item => RepairSpecializedOwnerCatalog.Contains(item.Id)).ToArray();
        var managed = items.Where(item => IsManaged(item.Id)).ToArray();
        var legacy = items.Where(item => !IsManaged(item.Id))
            .Select(item => item.Id)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(123, items.Count);
        Assert.Equal(123, items.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(98, executorManaged.Length);
        Assert.Equal(25, specializedManaged.Length);
        Assert.Empty(executorManaged.Select(item => item.Id)
            .Intersect(specializedManaged.Select(item => item.Id), StringComparer.OrdinalIgnoreCase));
        Assert.Equal(123, managed.Length);
        Assert.Empty(legacy);
        Assert.Equal(25, RepairSpecializedOwnerCatalog.All.Count);
        Assert.Equal(18, items.Count(item => item.CanMutate));
        Assert.DoesNotContain(items.Where(item => !item.CanMutate), item => item.Availability == RepairExecutionAvailability.WorkingBidirectional);
        foreach (var uwpId in new[]
                 {
                     "repair.store", "repair.uwp_apps", "repair.calculator",
                     "repair.photos", "repair.game_bar", "repair.cortana"
                 })
        {
            Assert.True(RepairSpecializedOwnerCatalog.Contains(uwpId));
            Assert.DoesNotContain(uwpId, legacy, StringComparer.OrdinalIgnoreCase);
        }
    }
}
