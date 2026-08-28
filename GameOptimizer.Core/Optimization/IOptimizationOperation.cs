using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Optimization;

public enum OperationType
{
    Registry,
    Service,
    ScheduledTask,
    PowerPlan,
    Command,
    SystemApi,
    Other
}

public interface IOptimizationOperation
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    string Category { get; }

    OperationRiskLevel RiskLevel { get; }

    string? Benefit => null;
    string? Drawback => null;

    OperationType OperationType => OperationType.Other;
    string? ManualPath => null;
    string? ManualSteps => null;
    string? TargetName => null;
    string? TargetValue => null;

    string? RegistryPath => null;
    string? RegistryHiveText => null;
    string? RegistryValueName => null;
    string? OptimizedValue => null;
    string? DefaultValue => null;
    string? ValueType => null;

    string? ImpactWarning => null;

    bool RequiresExplicitConfirmation => false;
    bool RequiresRestorePoint => false;
    bool RequiresAdmin => true;
    bool RequiresSnapshotForRestore => false;
    bool RestoreMayLeaveStatusCompliant => false;
    bool AffectsUserFeeling => false;

    string? HardwareCompatibilityHint => null;

    Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken);
    Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken);
}

public interface IOptimizationCompatibilityPolicy
{
    Task<OperationStatus?> ProbeAsync(
        string moduleKey,
        IOptimizationOperation operation,
        CancellationToken cancellationToken);
}

public sealed record OperationCompatibilityMetadata(
    string VariantId,
    string VariantLabel,
    string Method,
    string Information);

/// <summary>
/// Describes the version-specific implementation selected for a supported operation.
/// This is presentation metadata only and never grants execution permission.
/// </summary>
public interface IOptimizationCompatibilityMetadataProvider
{
    Task<OperationCompatibilityMetadata> DescribeAsync(
        string moduleKey,
        IOptimizationOperation operation,
        CancellationToken cancellationToken);
}
