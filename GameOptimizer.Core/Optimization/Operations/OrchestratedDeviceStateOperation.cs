using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class OrchestratedDeviceStateOperation : OrchestratedChangeOperation
{
    public OrchestratedDeviceStateOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
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
            OperationType.Command,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
    }

    public override string OptimizedValue => "Disabled";
}
