using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.PowerManagement;

/// <summary>
/// Catalog-preserving tombstone for a legacy Power operation that has not been
/// proven safe enough to express through <see cref="IPowerManagementOwner"/>.
/// It never reads or writes the platform and never falls back to legacy helpers.
/// </summary>
public sealed class FailClosedPowerOptimizationOperation : IOptimizationOperation
{
    public FailClosedPowerOptimizationOperation(
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        string blockReason)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A catalog ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(blockReason)) throw new ArgumentException("A Power migration block reason is required.", nameof(blockReason));
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        RiskLevel = riskLevel;
        Benefit = benefit;
        Drawback = drawback;
        BlockReason = blockReason;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; }
    public string? Benefit { get; }
    public string? Drawback { get; }
    public string BlockReason { get; }
    public OperationType OperationType => OperationType.PowerPlan;
    public bool RequiresAdmin => true;
    public bool RequiresExplicitConfirmation => true;
    public bool RequiresSnapshotForRestore => false;
    public string? TargetName => "Power setting";
    public string? TargetValue => "Blocked until migrated to the typed Power owner";
    public string? ImpactWarning => BlockReason;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationStatus(false, BlockReason));
    }

    public Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationInvokeResult(false, false, BlockReason));
    }

    public Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationInvokeResult(false, false,
            $"{BlockReason} No typed Power receipt exists for this catalog entry, so restore is unavailable."));
    }
}
