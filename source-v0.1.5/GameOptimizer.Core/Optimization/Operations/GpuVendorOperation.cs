using System.Runtime.Versioning;
using GameOptimizer.Core.GraphicsSafety;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class NvidiaLowLatencyOperation : IOptimizationOperation
{
    private readonly FailClosedGraphicsOptimizationOperation _boundary = new(
        "gpu.nvidia_low_latency",
        "NVIDIA 低延迟模式（安全阻断）",
        "NVIDIA 私有注册表值并非稳定 NVAPI 契约，无法证明驱动版本、策略与恢复语义。",
        GraphicsMutationKind.VendorPrivateSetting,
        "错误的私有值可能被驱动忽略、覆盖或造成配置漂移。");

    public string Id => _boundary.Id;
    public string Title => _boundary.Title;
    public string Description => _boundary.Description;
    public string Category => _boundary.Category;
    public OperationRiskLevel RiskLevel => _boundary.RiskLevel;
    public string? Benefit => _boundary.Benefit;
    public string? Drawback => _boundary.Drawback;
    public OperationType OperationType => _boundary.OperationType;
    public bool RequiresAdmin => _boundary.RequiresAdmin;
    public bool RequiresExplicitConfirmation => _boundary.RequiresExplicitConfirmation;
    public bool RequiresSnapshotForRestore => _boundary.RequiresSnapshotForRestore;
    public string? TargetName => _boundary.TargetName;
    public string? TargetValue => _boundary.TargetValue;
    public string? ImpactWarning => _boundary.ImpactWarning;
    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) => _boundary.GetStatusAsync(cancellationToken);
    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) => _boundary.ApplyAsync(context, cancellationToken);
    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) => _boundary.RestoreAsync(context, cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class DisableMpoOperation : IOptimizationOperation
{
    private readonly FailClosedGraphicsOptimizationOperation _boundary = new(
        "gpu.disable_mpo",
        "禁用 MPO（安全阻断）",
        "OverlayTestMode 属于未证明跨版本稳定的 DWM/MPO 覆盖值，本版本不写入。",
        GraphicsMutationKind.MpoOrTdrOverride,
        "可能造成黑屏、合成异常、功耗增加或驱动更新后状态漂移。");

    public string Id => _boundary.Id;
    public string Title => _boundary.Title;
    public string Description => _boundary.Description;
    public string Category => _boundary.Category;
    public OperationRiskLevel RiskLevel => _boundary.RiskLevel;
    public string? Benefit => _boundary.Benefit;
    public string? Drawback => _boundary.Drawback;
    public OperationType OperationType => _boundary.OperationType;
    public bool RequiresAdmin => _boundary.RequiresAdmin;
    public bool RequiresExplicitConfirmation => _boundary.RequiresExplicitConfirmation;
    public bool RequiresSnapshotForRestore => _boundary.RequiresSnapshotForRestore;
    public string? TargetName => _boundary.TargetName;
    public string? TargetValue => _boundary.TargetValue;
    public string? ImpactWarning => _boundary.ImpactWarning;
    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) => _boundary.GetStatusAsync(cancellationToken);
    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) => _boundary.ApplyAsync(context, cancellationToken);
    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) => _boundary.RestoreAsync(context, cancellationToken);
}
