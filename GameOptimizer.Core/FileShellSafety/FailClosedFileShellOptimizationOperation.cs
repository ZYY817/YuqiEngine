using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.FileShellSafety;

public sealed class FailClosedFileShellOptimizationOperation : IOptimizationOperation
{
    private readonly IFileShellSafetyOwner _owner;
    private readonly FileShellMutationKind _kind;

    public FailClosedFileShellOptimizationOperation(
        IFileShellSafetyOwner owner,
        FileShellMutationKind kind,
        string id,
        string title,
        string description,
        string category,
        string? drawback = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _kind = kind;
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A catalog ID is required.", nameof(id)) : id;
        Title = title;
        Description = description;
        Category = category;
        Drawback = drawback;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel => OperationRiskLevel.Extreme;
    public string? Benefit => null;
    public string? Drawback { get; }
    public OperationType OperationType => OperationType.Other;
    public bool RequiresAdmin => true;
    public bool RequiresExplicitConfirmation => true;
    public bool RequiresSnapshotForRestore => false;
    public string? TargetName => FileShellSafetyIdentity.ResourceKey;
    public string? TargetValue => "FailClosed";
    public string? ImpactWarning => "The legacy entry cannot mutate files or restart Explorer without a recoverable typed Owner.";

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var decision = await EvaluateAsync(cancellationToken).ConfigureAwait(false);
        return new OperationStatus(false, $"{decision.Status}: {decision.Message}");
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = await EvaluateAsync(cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(false, false, $"{decision.Status}: {decision.Message}");
    }

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(new OperationInvokeResult(false, false,
            "RestoreUnproven: no quarantine manifest or typed restore receipt exists for this blocked legacy entry."));
    }

    private Task<FileShellSafetyDecision> EvaluateAsync(CancellationToken cancellationToken) =>
        _owner.EvaluateAsync(new FileShellSafetyRequest(Id, _kind), cancellationToken);
}
