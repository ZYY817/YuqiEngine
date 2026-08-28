using GameOptimizer.Core.Services.DeviceRepair;
using GameOptimizer.Core.Services.MaintenanceDiagnostics;
using GameOptimizer.Core.Services.StorageSafety;

namespace GameOptimizer.Core.Services.Repair;

/// <summary>
/// Single classification source for the static Repair registry. This is a
/// capability classification, not a second execution owner.
/// </summary>
public static class RepairExecutionAvailabilityCatalog
{
    public static RepairExecutionAvailability Get(string repairItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repairItemId);

        if (OrchestratedRepairChangeExecutor.IsBidirectional(repairItemId)
            || (NetworkRepairCatalog.TryGet(repairItemId, out var network)
                && network.UsesNetworkResetOwner))
        {
            return RepairExecutionAvailability.WorkingBidirectional;
        }

        if (RepairSpecializedOwnerCatalog.IsReadOnlyDiagnostic(repairItemId))
        {
            return RepairExecutionAvailability.ReadOnly;
        }

        if (NetworkRepairCatalog.TryGet(repairItemId, out network)
            && !network.UsesNetworkResetOwner)
        {
            return RepairExecutionAvailability.SafetyBlocked;
        }

        if (FileShellRepairCatalog.Contains(repairItemId)
            || GraphicsDisplayRepairCatalog.Contains(repairItemId)
            || ResidualRepairCatalog.Contains(repairItemId))
        {
            return RepairExecutionAvailability.SafetyBlocked;
        }

        return RepairExecutionAvailability.FailClosed;
    }

    public static IReadOnlyDictionary<RepairExecutionAvailability, int> Count(
        IEnumerable<RepairItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .GroupBy(item => item.Availability)
            .ToDictionary(group => group.Key, group => group.Count());
    }
}
