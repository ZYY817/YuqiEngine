using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Typed, orchestrated wrapper for per-interface machine registry capabilities (NetBIOS,
/// Nagle, etc.). It reuses the exact same prepare → confirm → execute → read-back → save-pointer
/// chain as every other <see cref="OrchestratedChangeOperation"/>; the dynamic per-interface
/// enumeration and recovery material live in the capability, not here.
/// </summary>
public sealed class OrchestratedPerInterfaceRegistryOperation : OrchestratedChangeOperation
{
    public OrchestratedPerInterfaceRegistryOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        string registryPath,
        string registryValueNames,
        string optimizedValues,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        IChangeConfirmationAuthority? confirmationAuthority = null,
        bool requiresExplicitConfirmation = true)
        : base(
            orchestrator,
            id,
            title,
            description,
            category,
            OperationType.Registry,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation,
            TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        RegistryPath = registryPath;
        RegistryValueName = registryValueNames;
        OptimizedValue = optimizedValues;
        ValueType = "REGISTRY PER-INTERFACE SET";
        RegistryHiveText = "HKLM";
    }

    public override string RegistryHiveText { get; }
    public override string RegistryPath { get; }
    public override string RegistryValueName { get; }
    public override string OptimizedValue { get; }
    public override string ValueType { get; }
}
