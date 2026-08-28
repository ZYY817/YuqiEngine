using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class OrchestratedAccessibilityOperation : OrchestratedChangeOperation
{
    public OrchestratedAccessibilityOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string benefit,
        string drawback,
        IChangeConfirmationAuthority? confirmationAuthority)
        : base(
            orchestrator,
            id,
            title,
            description,
            "辅助功能",
            OperationType.SystemApi,
            OperationRiskLevel.Balanced,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation: true)
    {
    }
}
