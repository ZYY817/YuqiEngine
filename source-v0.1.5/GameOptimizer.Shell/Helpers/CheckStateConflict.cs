namespace GameOptimizer.Shell.Helpers;

/// <summary>
/// Resolves checkbox/toggle intent when a row is both user-toggled and constrained
/// by compatibility or an already-applied system state.
/// </summary>
public static class CheckStateConflict
{
    public enum Action
    {
        Accept,
        RejectIncompatible,
        RestoreApplied
    }

    public static Action ResolveUserCheck(bool requestedChecked, bool isCompatible, bool isApplied)
    {
        if (requestedChecked && !isCompatible)
            return Action.RejectIncompatible;

        if (!requestedChecked && isApplied)
            return Action.RestoreApplied;

        return Action.Accept;
    }

    public static bool ComputePresetSelected(string mode, Core.Optimization.OperationRiskLevel riskLevel, bool isCompatible)
    {
        if (!isCompatible)
            return false;

        return mode switch
        {
            "all" or "performance" => true,
            "balanced" => riskLevel != Core.Optimization.OperationRiskLevel.Extreme,
            "safe" => riskLevel == Core.Optimization.OperationRiskLevel.Safe,
            _ => false
        };
    }
}
