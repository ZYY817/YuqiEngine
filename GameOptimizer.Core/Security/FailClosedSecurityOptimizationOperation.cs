using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.Security;

public sealed class FailClosedSecurityOptimizationOperation : IOptimizationOperation
{
    private readonly ISecurityProtectionOwner _owner;
    private readonly SecurityProtectionArea _area;

    public FailClosedSecurityOptimizationOperation(
        ISecurityProtectionOwner owner,
        SecurityProtectionArea area,
        string id,
        string title,
        string description,
        string category,
        string? drawback = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _area = area;
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
    public string? TargetName => _area.ToString();
    public string? TargetValue => "PolicyDenied";
    public string? ImpactWarning => "This optimization would reduce a Windows security protection and is forbidden.";

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var decision = await EvaluateAsync(SecurityProtectionIntent.WeakenProtection, cancellationToken).ConfigureAwait(false);
        return new OperationStatus(false, decision.Message);
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = await EvaluateAsync(SecurityProtectionIntent.WeakenProtection, cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(false, false, $"{decision.Status}: {decision.Message}");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = await EvaluateAsync(SecurityProtectionIntent.RestoreProtection, cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(false, false, $"{decision.Status}: {decision.Message}");
    }

    private Task<SecurityProtectionDecision> EvaluateAsync(
        SecurityProtectionIntent intent,
        CancellationToken cancellationToken) =>
        _owner.EvaluateAsync(new SecurityProtectionRequest(_area, intent), cancellationToken);
}
