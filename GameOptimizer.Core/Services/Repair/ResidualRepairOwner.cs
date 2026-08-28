namespace GameOptimizer.Core.Services.Repair;

public enum ResidualRepairSemantic
{
    ActivityHistoryDataDeletion,
    FutureActivityUploadPolicy,
    IsoImageMountLifecycle,
    StandbyListPurge,
    ProcessWorkingSetTrim
}

public enum ResidualRepairOwnerStatus
{
    PolicyDenied,
    Cancelled,
    RestoreUnproven
}

public sealed record ResidualRepairDefinition(
    string RepairItemId,
    ResidualRepairSemantic IntendedSemantic,
    IReadOnlyList<ResidualRepairSemantic> ExcludedSemantics,
    string ResourceKey,
    string OwnerClassification,
    string BlockReason);

public interface IResidualRepairOwner
{
    Task<RepairResult> ExecuteAsync(
        ResidualRepairDefinition definition,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken);

    Task<RepairResult> RestoreAsync(
        ResidualRepairDefinition definition,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// The fixed production owner for residual Repair semantics that have no proven
/// reversible platform contract. It never invokes a platform writer and never
/// creates a change receipt or RecoveryId.
/// </summary>
public sealed class FailClosedResidualRepairOwner : IResidualRepairOwner
{
    public static FailClosedResidualRepairOwner Instance { get; } = new();

    private FailClosedResidualRepairOwner() { }

    public Task<RepairResult> ExecuteAsync(
        ResidualRepairDefinition definition,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var status = cancellationToken.IsCancellationRequested
            ? ResidualRepairOwnerStatus.Cancelled
            : ResidualRepairOwnerStatus.PolicyDenied;
        progress?.Report(new RepairProgress
        {
            Message = status == ResidualRepairOwnerStatus.Cancelled
                ? "修复已取消；未执行任何系统写入。"
                : "该修复缺少可验证的 typed Owner，已安全阻断。",
            PercentComplete = 100
        });
        return Task.FromResult(status == ResidualRepairOwnerStatus.Cancelled
            ? RepairResult.Cancelled(
                "修复已取消；未执行任何系统写入。",
                "cancelled.residual-repair",
                $"{status}: {definition.OwnerClassification}",
                RepairExecutionAvailability.SafetyBlocked)
            : RepairResult.Blocked(
                definition.BlockReason,
                "repair.residual.safety-blocked",
                $"{status}: {definition.OwnerClassification}",
                RepairExecutionAvailability.SafetyBlocked));
    }

    public Task<RepairResult> RestoreAsync(
        ResidualRepairDefinition definition,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var status = cancellationToken.IsCancellationRequested
            ? ResidualRepairOwnerStatus.Cancelled
            : ResidualRepairOwnerStatus.RestoreUnproven;
        progress?.Report(new RepairProgress
        {
            Message = status == ResidualRepairOwnerStatus.Cancelled
                ? "恢复已取消；未执行任何系统写入。"
                : "恢复已阻断；该修复没有可信恢复材料。",
            PercentComplete = 100
        });
        return Task.FromResult(status == ResidualRepairOwnerStatus.Cancelled
            ? RepairResult.Cancelled(
                "恢复已取消；未执行任何系统写入。",
                "cancelled.residual-restore",
                $"{status}: {definition.OwnerClassification}",
                RepairExecutionAvailability.SafetyBlocked)
            : RepairResult.Blocked(
                "通用 Repair 回执不能作为此修复项的恢复授权。",
                "repair.residual.restore-unproven",
                $"{status}: {definition.OwnerClassification}",
                RepairExecutionAvailability.SafetyBlocked));
    }
}

public static class ResidualRepairCatalog
{
    private static readonly IReadOnlyDictionary<string, ResidualRepairDefinition> Definitions =
        new Dictionary<string, ResidualRepairDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["repair.activity_history"] = new(
                "repair.activity_history",
                ResidualRepairSemantic.ActivityHistoryDataDeletion,
                [ResidualRepairSemantic.FutureActivityUploadPolicy],
                "activity-history:current-user:stored-data",
                "activity-history-data-deletion-owner-unavailable",
                "清理既有活动历史与未来活动上传策略是两个独立语义；当前没有可枚举、回读和恢复的历史数据删除 Owner，且不会沿用旧实现把策略写为 1。"),
            ["repair.iso_mount"] = new(
                "repair.iso_mount",
                ResidualRepairSemantic.IsoImageMountLifecycle,
                [],
                "iso-mount:file-identity-and-lifecycle",
                "iso-mount-lifecycle-owner-unavailable",
                "ISO 修复没有绑定规范化文件 identity、当前挂载状态、卸载恢复和冲突处理契约；不会把修改文件关联冒充为挂载修复。"),
            ["repair.standby_list"] = new(
                "repair.standby_list",
                ResidualRepairSemantic.StandbyListPurge,
                [],
                "memory:machine:standby-list",
                "standby-list-irreversible-policy-denied",
                "待机列表清理是不可逆且瞬态的机器内存操作，无法生成真实 RecoveryId；当前禁止执行。"),
            ["repair.working_set"] = new(
                "repair.working_set",
                ResidualRepairSemantic.ProcessWorkingSetTrim,
                [],
                "memory:process-set:working-set",
                "working-set-process-impact-policy-denied",
                "工作集清理会影响用户进程且状态瞬态；缺少固定进程集合、退出冲突、回读和恢复契约时禁止执行。")
        };

    public static IReadOnlyCollection<ResidualRepairDefinition> All => Definitions.Values.ToArray();

    public static bool Contains(string repairItemId) =>
        !string.IsNullOrWhiteSpace(repairItemId) && Definitions.ContainsKey(repairItemId);

    public static bool TryGet(string repairItemId, out ResidualRepairDefinition definition) =>
        Definitions.TryGetValue(repairItemId, out definition!);
}
