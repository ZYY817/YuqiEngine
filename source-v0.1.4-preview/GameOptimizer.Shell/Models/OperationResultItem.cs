using System;
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Shell.Models;

public sealed class OperationResultItem
{
    public string ModuleTitle { get; }
    public string OperationTitle { get; }
    public string OperationId { get; }
    public bool IsSuccess { get; }
    public bool IsSkipped { get; }
    public bool HasChanged { get; }
    public string Message { get; }
    public string BeforeDetails { get; }
    public string AfterDetails { get; }
    public bool RequiresReboot { get; }

    public string StatusText => IsSkipped ? "Skipped" : (!IsSuccess ? "Failed" : (HasChanged ? "Applied" : "Synced"));
    public bool HasBeforeDetails => !string.IsNullOrWhiteSpace(BeforeDetails);
    public bool HasAfterDetails => !string.IsNullOrWhiteSpace(AfterDetails);

    public OperationResultItem(string moduleTitle, OperationExecutionSummary summary)
    {
        ModuleTitle = moduleTitle;
        OperationTitle = summary.Title;
        OperationId = summary.OperationId;
        IsSkipped = IsSkippedSummary(summary);
        IsSuccess = summary.Success && !IsSkipped;
        HasChanged = summary.Changed;
        Message = OptimizationResultMessageFormatter.Format(
            summary.Message,
            IsSuccess,
            IsSkipped,
            GameOptimizer.Core.Services.PermissionChecker.IsRunningAsAdministrator());
        BeforeDetails = summary.Before?.Details ?? string.Empty;
        AfterDetails = summary.After?.Details ?? string.Empty;
        RequiresReboot = summary.After?.RequiresReboot ?? summary.Before?.RequiresReboot ?? false;
    }

    private static bool IsSkippedSummary(OperationExecutionSummary summary)
    {
        if (summary.Success || summary.Changed)
        {
            return false;
        }

        var message = summary.Message ?? string.Empty;
        return message.Contains("skip", StringComparison.OrdinalIgnoreCase)
            || message.Contains("跳过", StringComparison.OrdinalIgnoreCase);
    }
}
