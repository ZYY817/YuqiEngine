using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class OrchestratedServiceOptimizationOperation : OrchestratedChangeOperation
{
    public OrchestratedServiceOptimizationOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        string serviceName,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        IChangeConfirmationAuthority? confirmationAuthority)
        : base(
            orchestrator,
            id,
            title,
            description,
            category,
            OperationType.Service,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        ServiceName = serviceName;
    }

    public string ServiceName { get; }
    public override string? OptimizedValue => "Disabled";
}

public sealed class FailClosedServiceOptimizationOperation : IOptimizationOperation
{
    private readonly string _reason;

    public FailClosedServiceOptimizationOperation(
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        string reason)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        RiskLevel = riskLevel;
        Benefit = benefit;
        Drawback = drawback;
        _reason = reason;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; }
    public string? Benefit { get; }
    public string? Drawback { get; }
    public bool RequiresAdmin => true;
    public bool RequiresExplicitConfirmation => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.Service;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationStatus(false, _reason));
    }

    public Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationInvokeResult(false, false, _reason));
    }

    public Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationInvokeResult(false, false, _reason));
    }
}
