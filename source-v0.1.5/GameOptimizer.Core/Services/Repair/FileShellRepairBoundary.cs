using GameOptimizer.Core.FileShellSafety;

namespace GameOptimizer.Core.Services.Repair;

internal static class FileShellRepairBoundary
{
    public static async Task<RepairResult> DenyAsync(
        string repairId,
        FileShellMutationKind kind,
        IProgress<RepairProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var decision = await UnavailableFileShellSafetyOwner.Instance.EvaluateAsync(
            new FileShellSafetyRequest(repairId, kind), cancellationToken).ConfigureAwait(false);
        progress?.Report(new RepairProgress
        {
            Message = "操作已由文件与 Shell 安全边界阻断",
            PercentComplete = 100
        });
        return RepairResult.FromReasonCode(
            "此修复入口当前不可执行",
            $"file-shell.{decision.Status}",
            $"{decision.Status}: {decision.Message}",
            RepairExecutionAvailability.SafetyBlocked);
    }

    public static Task<RepairResult> DenyRestoreAsync(
        string repairId,
        IProgress<RepairProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var status = cancellationToken.IsCancellationRequested
            ? FileShellSafetyStatus.Cancelled
            : FileShellSafetyStatus.RestoreUnproven;
        progress?.Report(new RepairProgress
        {
            Message = "恢复已由文件与 Shell 安全边界阻断",
            PercentComplete = 100
        });
        return Task.FromResult(status == FileShellSafetyStatus.Cancelled
            ? RepairResult.Cancelled(
                "此修复入口没有可信的 durable quarantine 恢复回执",
                "cancelled.file-shell-restore",
                $"{status}: 不会把通用 Repair 回执当作文件或 Shell 恢复材料。",
                RepairExecutionAvailability.SafetyBlocked)
            : RepairResult.Blocked(
                "此修复入口没有可信的 durable quarantine 恢复回执",
                "file-shell.restore-unproven",
                $"{status}: 不会把通用 Repair 回执当作文件或 Shell 恢复材料。",
                RepairExecutionAvailability.SafetyBlocked));
    }
}
