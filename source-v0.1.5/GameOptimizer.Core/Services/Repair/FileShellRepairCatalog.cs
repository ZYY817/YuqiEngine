using GameOptimizer.Core.FileShellSafety;

namespace GameOptimizer.Core.Services.Repair;

public sealed record FileShellRepairDefinition(
    string RepairItemId,
    FileShellMutationKind MutationKind,
    string OwnerClassification);

/// <summary>
/// Static Repair registry entries whose mutation semantics are owned by the single
/// File/Shell safety boundary. The current boundary is intentionally fail-closed:
/// catalog membership prevents fallback to the legacy delegate, but does not claim
/// that a quarantine or Shell writer is available.
/// </summary>
public static class FileShellRepairCatalog
{
    private static readonly IReadOnlyDictionary<string, FileShellRepairDefinition> Definitions =
        new Dictionary<string, FileShellRepairDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["repair.action_center"] = ShellState("repair.action_center"),
            ["repair.context_menu"] = ShellState("repair.context_menu"),
            ["repair.explorer"] = ExplorerLifecycle("repair.explorer"),
            ["repair.explorer_restart"] = ExplorerLifecycle("repair.explorer_restart"),
            ["repair.file_association"] = ShellState("repair.file_association"),
            ["repair.file_history"] = PermanentDelete("repair.file_history"),
            ["repair.game_config"] = Quarantine("repair.game_config"),
            ["repair.gpu_cache"] = Quarantine("repair.gpu_cache"),
            ["repair.recycle_bin"] = PermanentDelete("repair.recycle_bin"),
            ["repair.shortcuts"] = Quarantine("repair.shortcuts"),
            ["repair.start_menu"] = ShellState("repair.start_menu"),
            ["repair.system_tray"] = ShellState("repair.system_tray"),
            ["repair.taskbar"] = ShellState("repair.taskbar"),
            ["repair.temp_cleanup"] = Quarantine("repair.temp_cleanup"),
            ["repair.virtual_desktop"] = ShellState("repair.virtual_desktop")
        };

    public static IReadOnlyCollection<FileShellRepairDefinition> All => Definitions.Values.ToArray();

    public static bool Contains(string repairItemId) =>
        !string.IsNullOrWhiteSpace(repairItemId) && Definitions.ContainsKey(repairItemId);

    public static bool TryGet(string repairItemId, out FileShellRepairDefinition definition) =>
        Definitions.TryGetValue(repairItemId, out definition!);

    private static FileShellRepairDefinition ExplorerLifecycle(string id) =>
        new(id, FileShellMutationKind.ExplorerRestart, "explorer-lifecycle-policy-denied");

    private static FileShellRepairDefinition ShellState(string id) =>
        new(id, FileShellMutationKind.ShellStateReset, "shell-state-policy-denied");

    private static FileShellRepairDefinition Quarantine(string id) =>
        new(id, FileShellMutationKind.QuarantineFiles, "quarantine-owner-unavailable");

    private static FileShellRepairDefinition PermanentDelete(string id) =>
        new(id, FileShellMutationKind.PermanentDelete, "permanent-delete-policy-denied");
}
