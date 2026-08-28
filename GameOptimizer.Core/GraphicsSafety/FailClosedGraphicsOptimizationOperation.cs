using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.GraphicsSafety;

public class FailClosedGraphicsOptimizationOperation : IOptimizationOperation
{
    private static readonly GraphicsEnvironmentSnapshot UnknownEnvironment = new(
        [GraphicsVendor.Unknown], false, false, false, false, null, string.Empty, string.Empty, false);

    private readonly IGraphicsSafetyBoundary _boundary;
    private readonly GraphicsMutationKind _kind;

    public FailClosedGraphicsOptimizationOperation(
        string id,
        string title,
        string description,
        GraphicsMutationKind kind,
        string? drawback = null,
        IGraphicsSafetyBoundary? boundary = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A catalog ID is required.", nameof(id)) : id;
        Title = title;
        Description = description;
        _kind = kind;
        Drawback = drawback;
        _boundary = boundary ?? UnavailableGraphicsSafetyBoundary.Instance;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category => "图形与显示安全边界";
    public OperationRiskLevel RiskLevel => OperationRiskLevel.Extreme;
    public string? Benefit => null;
    public string? Drawback { get; }
    public OperationType OperationType => OperationType.Other;
    public bool RequiresAdmin => true;
    public bool RequiresExplicitConfirmation => true;
    public bool RequiresSnapshotForRestore => false;
    public string? TargetName => GraphicsSafetyIdentity.ResourceKey;
    public string? TargetValue => "FailClosed";
    public string? ImpactWarning => "该入口没有受批准的驱动、显示或厂商私有设置恢复链。";

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var decision = await EvaluateAsync(cancellationToken).ConfigureAwait(false);
        return new OperationStatus(false, $"{decision.Status}: {decision.Message}");
    }

    public async Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = await EvaluateAsync(cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(false, false, $"{decision.Status}: {decision.Message}");
    }

    public Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(new OperationInvokeResult(
            false,
            false,
            "RecoveryUnavailable: no typed recovery receipt exists for this blocked legacy entry."));
    }

    private Task<GraphicsSafetyDecision> EvaluateAsync(CancellationToken cancellationToken) =>
        _boundary.EvaluateAsync(new GraphicsSafetyRequest(Id, _kind), UnknownEnvironment, cancellationToken);
}
