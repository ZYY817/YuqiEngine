using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class OrchestratedScheduledTaskOptimizationOperation : OrchestratedChangeOperation
{
    public OrchestratedScheduledTaskOptimizationOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        string taskPath,
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
            OperationType.ScheduledTask,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        TaskPath = taskPath;
    }

    public string TaskPath { get; }
    public string? TargetName => TaskPath;
    public override string? OptimizedValue => "Disabled";
}
