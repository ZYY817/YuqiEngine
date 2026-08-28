using GameOptimizer.Core.Services.DeviceRepair;
using GameOptimizer.Core.Services.MaintenanceDiagnostics;
using GameOptimizer.Core.Services.StorageSafety;

namespace GameOptimizer.Core.Services.Repair;

/// <summary>
/// Repair items that are intentionally routed through a specialized typed Owner rather than
/// <see cref="IRepairChangeExecutor"/>. These items must not be counted as legacy writers.
/// </summary>
public static class RepairSpecializedOwnerCatalog
{
    private static readonly HashSet<string> ApplicationPackageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "repair.store",
        "repair.uwp_apps",
        "repair.calculator",
        "repair.photos",
        "repair.game_bar",
        "repair.cortana"
    };

    public static bool Contains(string repairItemId) =>
        ApplicationPackageIds.Contains(repairItemId)
        || (!OrchestratedRepairChangeExecutor.IsBidirectional(repairItemId)
            && DeviceRepairCatalog.TryGet(repairItemId, out _))
        || MaintenanceDiagnosticCatalog.TryGet(repairItemId, out _)
        || StorageFeatureRepairCatalog.Contains(repairItemId);

    public static bool IsReadOnlyDiagnostic(string repairItemId) =>
        ApplicationPackageIds.Contains(repairItemId)
        || (!OrchestratedRepairChangeExecutor.IsBidirectional(repairItemId)
            && DeviceRepairCatalog.TryGet(repairItemId, out _))
        || MaintenanceDiagnosticCatalog.TryGet(repairItemId, out _)
        || StorageFeatureRepairCatalog.Contains(repairItemId);

    public static IReadOnlyCollection<string> All => ApplicationPackageIds
        .Concat(DeviceRepairCatalog.All
            .Select(value => value.RepairItemId)
            .Where(id => !OrchestratedRepairChangeExecutor.IsBidirectional(id)))
        .Concat(MaintenanceDiagnosticCatalog.All.Select(value => value.RepairItemId))
        .Concat(StorageFeatureRepairCatalog.All)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
