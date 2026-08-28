// File: GameOptimizer.Core/Diagnostics/DiagnosticService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Diagnostics;

public class DiagnosticService : IDiagnosticService
{
    private readonly ErrorDiagnosticEngine _diagnosticEngine;
    private readonly SolutionProvider _solutionProvider;
    private readonly ErrorReportGenerator _reportGenerator;

    public DiagnosticService()
    {
        _solutionProvider = new SolutionProvider();
        _diagnosticEngine = new ErrorDiagnosticEngine(_solutionProvider);
        _reportGenerator = new ErrorReportGenerator();
    }

    public DiagnosticResult Diagnose(Exception exception, DiagnosticContext context)
    {
        return _diagnosticEngine.Diagnose(exception, context);
    }

    public List<SolutionStep> GetSolutions(ErrorCode errorCode, DiagnosticContext context)
    {
        return _solutionProvider.GetSolutions(errorCode, context);
    }

    public ErrorReport GenerateReport(List<DiagnosticResult> diagnostics)
    {
        return _reportGenerator.GenerateReport(diagnostics);
    }

    public Task<string> ExportReportAsync(ErrorReport report, string format, CancellationToken cancellationToken = default)
    {
        return _reportGenerator.ExportReportAsync(report, format, cancellationToken);
    }

    public Task SaveReportToFileAsync(ErrorReport report, string filePath, string format, CancellationToken cancellationToken = default)
    {
        return _reportGenerator.SaveReportToFileAsync(report, filePath, format, cancellationToken);
    }
}
