// File: GameOptimizer.Core/Optimization/Operations/DirectoryCleanupOperation.cs
using GameOptimizer.Core.FileShellSafety;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Compatibility surface for legacy callers. File mutation is intentionally unavailable;
/// production catalog entries use the central file/Shell safety boundary directly.
/// </summary>
public sealed class DirectoryCleanupOperation : IOptimizationOperation
{
    private readonly FailClosedFileShellOptimizationOperation _inner;

    public DirectoryCleanupOperation(
        string id,
        string title,
        string description,
        string category,
        IEnumerable<string> directories,
        IEnumerable<string>? patterns = null,
        bool includeSubdirectories = true)
    {
        ArgumentNullException.ThrowIfNull(directories);
        _ = directories.ToArray();
        _ = patterns?.ToArray();
        _ = includeSubdirectories;
        _inner = new FailClosedFileShellOptimizationOperation(
            UnavailableFileShellSafetyOwner.Instance,
            FileShellMutationKind.QuarantineFiles,
            id,
            title,
            description,
            category,
            "Legacy direct deletion is disabled until a recoverable quarantine Owner is connected.");
    }

    public string Id => _inner.Id;
    public string Title => _inner.Title;
    public string Description => _inner.Description;
    public string Category => _inner.Category;
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public OperationType OperationType => _inner.OperationType;
    public bool RequiresAdmin => _inner.RequiresAdmin;
    public bool RequiresExplicitConfirmation => _inner.RequiresExplicitConfirmation;
    public bool RequiresSnapshotForRestore => false;
    public string? TargetName => _inner.TargetName;
    public string? TargetValue => _inner.TargetValue;
    public string? ImpactWarning => _inner.ImpactWarning;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _inner.GetStatusAsync(cancellationToken);

    public Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken) =>
        _inner.ApplyAsync(context, cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken) =>
        _inner.RestoreAsync(context, cancellationToken);
}
