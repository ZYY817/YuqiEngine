namespace GameOptimizer.Core.Optimization.Operations;

public sealed class PolicyDeniedOptimizationOperation : IOptimizationOperation
{
    private readonly string _reason;

    public PolicyDeniedOptimizationOperation(
        string id, string title, string description, string category,
        OperationType operationType, OperationRiskLevel riskLevel,
        string? benefit, string? drawback, string reason)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        OperationType = operationType;
        RiskLevel = riskLevel;
        Benefit = benefit;
        Drawback = drawback;
        _reason = reason;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationType OperationType { get; }
    public OperationRiskLevel RiskLevel { get; }
    public string? Benefit { get; }
    public string? Drawback { get; }
    public bool RequiresAdmin => true;
    public bool RequiresExplicitConfirmation => false;
    public bool RequiresSnapshotForRestore => false;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationStatus(false, _reason));
    }

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationInvokeResult(false, false, _reason));
    }

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationInvokeResult(false, false, _reason));
    }
}
