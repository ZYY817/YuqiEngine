using GameOptimizer.Core.GraphicsSafety;

namespace GameOptimizer.Core.Services.Repair;

public sealed record GraphicsDisplayRepairDefinition(
    string RepairItemId,
    GraphicsMutationKind MutationKind,
    string OwnerClassification);

/// <summary>
/// Fixed owner routing for graphics/display Repair entries that cannot safely
/// fall back to their legacy delegates. Catalog membership is routing evidence,
/// not evidence that a graphics mutation is available in this release.
/// </summary>
public static class GraphicsDisplayRepairCatalog
{
    private static readonly IReadOnlyDictionary<string, GraphicsDisplayRepairDefinition> Definitions =
        new Dictionary<string, GraphicsDisplayRepairDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["repair.amd_settings"] = VendorPrivate("repair.amd_settings"),
            ["repair.display_adapter"] = DriverLifecycle("repair.display_adapter"),
            ["repair.display_driver"] = DriverLifecycle("repair.display_driver"),
            ["repair.display_driver_reset"] = DriverLifecycle("repair.display_driver_reset"),
            ["repair.display_reset"] = DisplayTopology("repair.display_reset"),
            ["repair.dwm_restart"] = DwmLifecycle("repair.dwm_restart"),
            ["repair.fullscreen_optimization"] = FullscreenSemanticMismatch("repair.fullscreen_optimization"),
            ["repair.nvidia_settings"] = VendorPrivate("repair.nvidia_settings")
        };

    public static IReadOnlyCollection<GraphicsDisplayRepairDefinition> All => Definitions.Values.ToArray();

    public static bool Contains(string repairItemId) =>
        !string.IsNullOrWhiteSpace(repairItemId) && Definitions.ContainsKey(repairItemId);

    public static bool TryGet(string repairItemId, out GraphicsDisplayRepairDefinition definition) =>
        Definitions.TryGetValue(repairItemId, out definition!);

    private static GraphicsDisplayRepairDefinition VendorPrivate(string id) =>
        new(id, GraphicsMutationKind.VendorPrivateSetting, "graphics-vendor-private-policy-denied");

    private static GraphicsDisplayRepairDefinition DriverLifecycle(string id) =>
        new(id, GraphicsMutationKind.DriverLifecycle, "graphics-driver-lifecycle-policy-denied");

    private static GraphicsDisplayRepairDefinition DisplayTopology(string id) =>
        new(id, GraphicsMutationKind.DisplayTopology, "graphics-display-topology-policy-denied");

    private static GraphicsDisplayRepairDefinition DwmLifecycle(string id) =>
        new(id, GraphicsMutationKind.DwmLifecycle, "graphics-dwm-lifecycle-policy-denied");

    private static GraphicsDisplayRepairDefinition FullscreenSemanticMismatch(string id) =>
        new(id, GraphicsMutationKind.ExistingTypedRegistryCapability, "graphics-semantic-mismatch-policy-denied");
}
