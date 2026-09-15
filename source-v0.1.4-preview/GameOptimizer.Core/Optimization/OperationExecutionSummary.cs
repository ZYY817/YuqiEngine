using GameOptimizer.Core.Diagnostics;
using System.Collections.Generic;

namespace GameOptimizer.Core.Optimization;

public sealed record OperationExecutionSummary(
    string OperationId,
    string Title,
    bool Success,
    bool Changed,
    string Message,
    OperationStatus Before,
    OperationStatus After,
    Exception? Exception = null,
    DiagnosticResult? Diagnostic = null,
    List<AutoRepairOption>? RepairOptions = null);
