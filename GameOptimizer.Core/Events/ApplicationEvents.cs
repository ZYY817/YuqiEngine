// File: GameOptimizer.Core/Events/ApplicationEvents.cs
namespace GameOptimizer.Core.Events;

/// <summary>
/// VM-002: 优化完成事件 - 通知备份模块更新备份建议
/// </summary>
public sealed record OptimizationCompletedEvent(
    string ModuleKey,
    int SuccessCount,
    int FailCount,
    int ChangedCount);

/// <summary>
/// VM-008: 快照恢复事件 - 通知仪表盘刷新模块状态
/// </summary>
public sealed record SnapshotRestoredEvent(
    string SnapshotId,
    int RestoredItemCount);

/// <summary>
/// VM-013: 修复完成事件 - 通知优化模块刷新状态
/// </summary>
public sealed record RepairCompletedEvent(
    string RepairItemId,
    bool Success,
    bool MayAffectOptimization);

/// <summary>
/// VM-022: 清理完成事件 - 通知仪表盘刷新磁盘使用
/// </summary>
public sealed record CleanupCompletedEvent(
    long BytesCleaned,
    int FilesDeleted);

/// <summary>
/// 系统还原点创建/还原事件
/// </summary>
public sealed record SystemRestoreEvent(
    bool IsRestore,
    string? RestorePointDescription);

/// <summary>
/// 模块状态变更事件 - 通用状态同步
/// </summary>
public sealed record ModuleStatusChangedEvent(
    string ModuleKey,
    string Reason);
