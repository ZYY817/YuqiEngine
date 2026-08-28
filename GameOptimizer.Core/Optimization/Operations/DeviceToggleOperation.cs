namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Compatibility shell for legacy device-toggle catalog entries. Device mutation is intentionally
/// unavailable until a typed device owner can prove stable identity, full recovery and hot-plug safety.
/// </summary>
public sealed class DeviceToggleOperation : IOptimizationOperation
{
    private const string BlockReason =
        "设备禁用/启用缺少稳定设备身份、跨进程资源锁、完整快照和热拔插恢复契约，当前保持阻断。";

    public DeviceToggleOperation(
        string id,
        string title,
        string description,
        string category,
        string deviceNamePattern,
        bool targetEnabled = false)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        DeviceNamePattern = deviceNamePattern;
        TargetEnabled = targetEnabled;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public string DeviceNamePattern { get; }
    public bool TargetEnabled { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.Command;
    public string? ManualPath => "devmgmt.msc";
    public string? TargetName => DeviceNamePattern;
    public string? TargetValue => TargetEnabled ? "Enabled" : "Disabled";
    public string? ManualSteps => "请使用受控设备修复流程或联系管理员；本入口不会修改设备状态。";

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
        return Task.FromResult(new OperationInvokeResult(false, false, "没有由 typed Device Owner 签发的恢复材料，拒绝恢复。"));
    }
}
