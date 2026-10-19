// File: GameOptimizer.Shell/Services/OptimizationExecutionStateEvaluator.cs
using System;
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Shell.Services;

internal static class OptimizationExecutionStateEvaluator
{
    public static bool DidApplyReachTarget(OperationExecutionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (!summary.Success)
        {
            return false;
        }

        if (summary.After.IsCompliant)
        {
            return true;
        }

        return !summary.Changed && summary.Before.IsCompliant;
    }

    public static bool DidRestoreReachTarget(OperationExecutionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (!summary.Success)
        {
            return false;
        }

        if (!summary.After.IsCompliant)
        {
            return true;
        }

        return !summary.Changed && !summary.Before.IsCompliant;
    }

    public static string GetBestRuntimeDetails(OperationExecutionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (!string.IsNullOrWhiteSpace(summary.After.Details))
        {
            return summary.After.Details;
        }

        if (!string.IsNullOrWhiteSpace(summary.Message))
        {
            return summary.Message;
        }

        if (!string.IsNullOrWhiteSpace(summary.Before.Details))
        {
            return summary.Before.Details;
        }

        return string.Empty;
    }
}