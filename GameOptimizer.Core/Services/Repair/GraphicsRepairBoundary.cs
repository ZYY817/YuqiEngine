using GameOptimizer.Core.GraphicsSafety;

namespace GameOptimizer.Core.Services.Repair;

public static class GraphicsRepairBoundary
{
    private static readonly GraphicsEnvironmentSnapshot UnknownEnvironment = new(
        [GraphicsVendor.Unknown], false, false, false, false, null, string.Empty, string.Empty, false);

    public static async Task<RepairResult> DenyAsync(
        string operationId,
        GraphicsMutationKind kind,
        IProgress<RepairProgress>? progress)
        => await DenyAsync(operationId, kind, progress, CancellationToken.None).ConfigureAwait(false);

    public static async Task<RepairResult> DenyAsync(
        string operationId,
        GraphicsMutationKind kind,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new RepairProgress
        {
            Message = "正在验证图形与驱动安全边界…",
            PercentComplete = 10
        });
        var decision = await UnavailableGraphicsSafetyBoundary.Instance.EvaluateAsync(
            new GraphicsSafetyRequest(operationId, kind),
            UnknownEnvironment,
            cancellationToken).ConfigureAwait(false);
        progress?.Report(new RepairProgress
        {
            Message = "安全阻断：未执行驱动、显示或注册表写入。",
            PercentComplete = 100
        });
        return RepairResult.FromReasonCode(
            decision.Message,
            $"graphics.{decision.Status}",
            decision.Status.ToString(),
            RepairExecutionAvailability.SafetyBlocked);
    }

    public static Task<RepairResult> DenyRestoreAsync(
        string operationId,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(RepairResult.Cancelled(
                "图形/显示恢复已取消；未执行任何写入。",
                "cancelled.graphics-restore",
                GraphicsSafetyStatus.Cancelled.ToString(),
                RepairExecutionAvailability.SafetyBlocked));
        }

        progress?.Report(new RepairProgress
        {
            Message = "安全阻断：该图形/显示项没有可验证的恢复回执。",
            PercentComplete = 100
        });
        return Task.FromResult(RepairResult.Blocked(
            $"{operationId} 没有专属图形/显示恢复 Owner；通用 Repair 回执不能作为恢复授权。",
            "graphics.restore-unproven",
            GraphicsSafetyStatus.PolicyDenied.ToString(),
            RepairExecutionAvailability.SafetyBlocked));
    }

    public static Task<RepairResult> RequireExistingOwnerAsync(
        string operationId,
        IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress
        {
            Message = "该修复项必须通过 RepairService 的现有 typed capability 执行。",
            PercentComplete = 100
        });
        return Task.FromResult(RepairResult.Blocked(
            "ExistingOwnerRequired: direct repair delegate is disabled; use the unified RepairService entry.",
            "graphics.existing-owner-required",
            availability: RepairExecutionAvailability.SafetyBlocked));
    }
}
