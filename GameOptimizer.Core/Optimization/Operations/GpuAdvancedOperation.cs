using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Utilities;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class DisableDwmAnimationsOperation : FailClosedGraphicsOptimizationOperation
{
    public DisableDwmAnimationsOperation() : base(
        "gpu.disable_dwm_animations",
        "禁用 DWM 动画（安全阻断）",
        "DWM 合成体验设置缺少版本适用性、策略检测与可验证恢复链，本版本不写入。",
        GraphicsMutationKind.DwmLifecycle,
        "可能导致桌面体验异常或与辅助功能设置冲突。") { }
}

[SupportedOSPlatform("windows")]
public sealed class DisableGameDvrCompleteOperation : OrchestratedChangeOperation
{
    private const string BenefitText = "减少录制与叠加层的后台资源占用。";
    private const string DrawbackText = "无法使用 Win+G 捕获游戏片段。";

    private readonly IOptimizationOperation[] _steps;

    public DisableGameDvrCompleteOperation(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority? confirmationAuthority)
        : base(
            orchestrator,
            "gpu.disable_game_dvr_complete",
            "彻底禁用 Game DVR",
            "完全关闭 Windows 游戏录制功能，释放后台资源占用。",
            "GPU 深度优化",
            OperationType.Registry,
            OperationRiskLevel.Safe,
            BenefitText,
            DrawbackText,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.CurrentUser,
            requiresAdministrator: false)
    {
        _steps =
        [
            new OrchestratedRegistryDwordOperation(
                orchestrator, Services.Repair.RepairChangeCapabilityIds.GameDvrEnabled,
                "Disable Game DVR", "Disable current-user Game DVR recording.", "GPU 深度优化",
                @"System\GameConfigStore", "GameDVR_Enabled", 0, OperationRiskLevel.Balanced,
                BenefitText, DrawbackText, confirmationAuthority, requiresExplicitConfirmation: true),
            new OrchestratedRegistryDwordOperation(
                orchestrator, Services.Repair.RepairChangeCapabilityIds.GameDvrAppCapture,
                "Disable game capture", "Disable current-user game capture.", "GPU 深度优化",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, OperationRiskLevel.Balanced,
                BenefitText, DrawbackText, confirmationAuthority, requiresExplicitConfirmation: true)
        ];
    }

    public override async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var statuses = new List<OperationStatus>(_steps.Length);
        foreach (var step in _steps)
        {
            statuses.Add(await step.GetStatusAsync(ct).ConfigureAwait(false));
        }
        var disabled = statuses.All(status => status.IsCompliant);
        return OperationStatus.Aggregate(statuses, disabled, "Game DVR 已完全禁用。", "Game DVR 仍有项目启用。");
    }

    public override async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        try
        {
            var completed = new List<IOptimizationOperation>(_steps.Length);
            foreach (var step in _steps)
            {
                var result = await step.ApplyAsync(ctx, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    var compensationSucceeded = true;
                    if (result.Changed)
                    {
                        var currentRestore = await step.RestoreAsync(ctx, CancellationToken.None).ConfigureAwait(false);
                        compensationSucceeded &= currentRestore.Success;
                    }
                    foreach (var prior in completed.AsEnumerable().Reverse())
                    {
                        var priorRestore = await prior.RestoreAsync(ctx, CancellationToken.None).ConfigureAwait(false);
                        compensationSucceeded &= priorRestore.Success;
                    }
                    return new OperationInvokeResult(
                        false,
                        !compensationSucceeded,
                        compensationSucceeded ? $"{result.Message} Previous changes were restored." : $"{result.Message} Compensation was incomplete.");
                }
                if (result.Changed) completed.Add(step);
            }
            return new OperationInvokeResult(true, completed.Count > 0, completed.Count > 0 ? "已彻底禁用 Game DVR。" : "Game DVR 已处于禁用状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "GPU优化");
        }
    }

    public override async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        try
        {
            var changed = false;
            foreach (var step in _steps.Reverse())
            {
                var result = await step.RestoreAsync(ctx, ct).ConfigureAwait(false);
                if (!result.Success) return result;
                changed |= result.Changed;
            }
            return new OperationInvokeResult(true, changed, changed ? "已恢复 Game DVR 设置。" : "Game DVR 没有可恢复的快照或已是默认状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "GPU优化");
        }
    }

}

[SupportedOSPlatform("windows")]
public sealed class DisableXboxGameMonitoringOperation : FailClosedGraphicsOptimizationOperation
{
    public DisableXboxGameMonitoringOperation() : base(
        "gpu.disable_xbox_game_monitoring",
        "禁用 Xbox Game Monitoring（安全阻断）",
        "服务启动类型属于共享 Services Owner，本图形目录不创建第二写入者。",
        GraphicsMutationKind.DriverLifecycle,
        "可能破坏 Xbox/Game Bar 录制、叠加层或游戏兼容性。") { }
}

[SupportedOSPlatform("windows")]
public sealed class DisableVrrOperation : FailClosedGraphicsOptimizationOperation
{
    public DisableVrrOperation() : base(
        "gpu.disable_vrr",
        "禁用 VRR（安全阻断）",
        "全局可变刷新率覆盖缺少显示器能力、Remote/VM、多显示器与驱动版本证明。",
        GraphicsMutationKind.DisplayTopology,
        "可能降低显示流畅度并造成撕裂或显示器兼容问题。") { }
}

[SupportedOSPlatform("windows")]
public sealed class ClearShaderCacheOperation : IOptimizationOperation
{
    private readonly FailClosedFileShellOptimizationOperation _boundary = new(
        UnavailableFileShellSafetyOwner.Instance,
        FileShellMutationKind.PermanentDelete,
        "gpu.clear_shader_cache",
        "清理 Shader 缓存（安全阻断）",
        "旧入口会递归永久删除驱动缓存文件，缺少预览哈希、隔离清单与恢复证明，现已由文件安全边界阻断。",
        "GPU 深度优化",
        "如需清理，必须由可恢复的隔离 Owner 接管，不能直接删除文件。");

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

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _boundary.GetStatusAsync(cancellationToken);

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _boundary.ApplyAsync(context, cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _boundary.RestoreAsync(context, cancellationToken);
}
[SupportedOSPlatform("windows")]
public sealed class AmdAntiLagOperation : FailClosedGraphicsOptimizationOperation
{
    public AmdAntiLagOperation() : base(
        "gpu.amd_anti_lag",
        "AMD Anti-Lag 私有设置（安全阻断）",
        "驱动类注册表中的 ULPS/thermal 值不等同于官方 ADL Anti-Lag API，不能猜测适用设备与默认值。",
        GraphicsMutationKind.VendorPrivateSetting,
        "错误写入可能影响功耗、温控、休眠或多 GPU 稳定性。") { }
}
