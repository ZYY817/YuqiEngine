// File: GameOptimizer.Core/Optimization/Operations/OrchestratedPowerSettingOperation.cs
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Catalog adapter for a typed AC/DC power-setting capability.  It routes through the unified
/// change orchestrator; the capability itself owns the PowrProf active-scheme writes and never
/// issues SETACTIVE, so it does not contend with the machine-global active-scheme resource.
/// </summary>
public sealed class OrchestratedPowerSettingOperation : OrchestratedChangeOperation
{
    public OrchestratedPowerSettingOperation(
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
            OperationType.PowerPlan,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
    }

    public override string? OptimizedValue => "Power setting (typed owner)";
}

/// <summary>
/// Catalog adapter for the active-scheme switch capability (High Performance plan).
/// </summary>
public sealed class OrchestratedPowerSchemeOperation : OrchestratedChangeOperation
{
    public OrchestratedPowerSchemeOperation(
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
            OperationType.PowerPlan,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
    }

    public override string? OptimizedValue => "Windows High Performance (capability matched; AC desktop only)";
}
