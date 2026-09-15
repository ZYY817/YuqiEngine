// File: GameOptimizer.Shell/Models/ExecutionResultItem.cs
using System.Collections.Generic;
using System.Windows.Media;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// Represents the result of an independently executed optimization operation.
/// This DTO is deliberately separate from the retired batch/guided optimization
/// pipeline so result presentation cannot recreate that entry point.
/// </summary>
public sealed class ExecutionResultItem
{
    public string OperationId { get; init; } = "";

    public string Title { get; init; } = "";

    public string ModuleName { get; init; } = "";

    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsSkipped { get; set; }

    public string? SkipReason { get; set; }

    public string? NavigateTarget { get; init; }

    public DiagnosticResult? Diagnostic { get; init; }

    public List<AutoRepairOption>? RepairOptions { get; init; }

    public bool HasDiagnostic => Diagnostic is not null;

    public bool HasRepairOptions => RepairOptions is { Count: > 0 };

    public string StatusIcon => IsSkipped ? "\uE893" : (IsSuccess ? "\uE73E" : "\uE711");

    public Brush StatusForeground => IsSkipped
        ? ThemeColors.MutedPurpleBrush
        : (IsSuccess ? ThemeColors.EmeraldBrush : ThemeColors.DangerBrush);
}
