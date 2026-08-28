// File: GameOptimizer.Core/Diagnostics/IDiagnosticService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Diagnostics;

public interface IDiagnosticService
{
    DiagnosticResult Diagnose(Exception exception, DiagnosticContext context);
    List<SolutionStep> GetSolutions(ErrorCode errorCode, DiagnosticContext context);
    ErrorReport GenerateReport(List<DiagnosticResult> diagnostics);
    Task<string> ExportReportAsync(ErrorReport report, string format, CancellationToken cancellationToken = default);
}
