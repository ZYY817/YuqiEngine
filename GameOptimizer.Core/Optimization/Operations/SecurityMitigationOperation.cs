using GameOptimizer.Core.Security;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Compatibility surface for historic security-reduction operation types.
/// Every action delegates to the central fail-closed security boundary.
/// </summary>
public abstract class SecurityMitigationCompatibilityOperation : IOptimizationOperation
{
    private readonly FailClosedSecurityOptimizationOperation _inner;

    protected SecurityMitigationCompatibilityOperation(
        SecurityProtectionArea area,
        string id,
        string title,
        string description,
        string drawback)
    {
        _inner = new FailClosedSecurityOptimizationOperation(
            UnavailableSecurityProtectionOwner.Instance,
            area,
            id,
            title,
            description,
            "安全缓解",
            drawback);
    }

    public string Id => _inner.Id;
    public string Title => _inner.Title;
    public string Description => _inner.Description;
    public string Category => _inner.Category;
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit => null;
    public string? Drawback => _inner.Drawback;
    public OperationType OperationType => _inner.OperationType;
    public bool RequiresAdmin => _inner.RequiresAdmin;
    public bool RequiresExplicitConfirmation => true;
    public bool RequiresRestorePoint => false;
    public bool RequiresSnapshotForRestore => false;
    public string? ImpactWarning => _inner.ImpactWarning;
    public string? TargetName => _inner.TargetName;
    public string? TargetValue => _inner.TargetValue;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _inner.GetStatusAsync(cancellationToken);

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _inner.ApplyAsync(context, cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _inner.RestoreAsync(context, cancellationToken);
}

public sealed class DisableVbsOperation : SecurityMitigationCompatibilityOperation
{
    public DisableVbsOperation() : base(SecurityProtectionArea.VirtualizationBasedSecurity,
        "security.disable_vbs", "VBS 保护不可关闭", "虚拟化安全不会作为性能优化被关闭。", "关闭 VBS 会降低系统隔离保护。") { }
}

public sealed class DisableHvciOperation : SecurityMitigationCompatibilityOperation
{
    public DisableHvciOperation() : base(SecurityProtectionArea.HypervisorCodeIntegrity,
        "security.disable_hvci", "内存完整性不可关闭", "HVCI 不会作为性能优化被关闭。", "关闭 HVCI 会降低内核代码完整性保护。") { }
}

public sealed class DisableCfgOperation : SecurityMitigationCompatibilityOperation
{
    public DisableCfgOperation() : base(SecurityProtectionArea.ControlFlowGuard,
        "security.disable_cfg", "CFG 保护不可关闭", "控制流保护不会作为性能优化被关闭。", "关闭 CFG 会降低漏洞利用防护。") { }
}

public sealed class DisableDefenderRealtimeOperation : SecurityMitigationCompatibilityOperation
{
    public DisableDefenderRealtimeOperation() : base(SecurityProtectionArea.DefenderRealtime,
        "security.disable_defender_realtime", "Defender 实时保护不可关闭", "Defender 实时监控不会作为性能优化被关闭。", "关闭实时保护会失去恶意软件实时检测。") { }
}

public sealed class DisableSmartScreenOperation : SecurityMitigationCompatibilityOperation
{
    public DisableSmartScreenOperation() : base(SecurityProtectionArea.SmartScreen,
        "security.disable_smartscreen", "SmartScreen 不可关闭", "应用信誉检查不会作为性能优化被关闭。", "关闭 SmartScreen 会降低未知程序和下载防护。") { }
}

public sealed class DisableDepOperation : SecurityMitigationCompatibilityOperation
{
    public DisableDepOperation() : base(SecurityProtectionArea.DataExecutionPrevention,
        "security.disable_dep", "DEP 保护不可关闭", "数据执行保护不会作为性能优化被关闭。", "关闭 DEP 会降低恶意代码防护。") { }
}

public sealed class DisableSehopOperation : SecurityMitigationCompatibilityOperation
{
    public DisableSehopOperation() : base(SecurityProtectionArea.StructuredExceptionProtection,
        "security.disable_sehop", "SEHOP 保护不可关闭", "异常处理保护不会作为性能优化被关闭。", "关闭 SEHOP 会降低漏洞利用防护。") { }
}
