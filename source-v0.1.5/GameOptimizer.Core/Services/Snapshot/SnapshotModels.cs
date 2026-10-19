using System;
using System.Collections.Generic;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.Snapshot
{
    public class OptimizationSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
        public int Version { get; set; } = 5;
        
        // V1: 注册表设置 (keyPath|valueName|category -> value)
        public Dictionary<string, object?> Settings { get; set; } = new();
        
        // V2: 操作状态快照 (OperationId -> IsCompliant)
        public Dictionary<string, bool> OperationStates { get; set; } = new();
        
        // V2: 当前活动电源计划 GUID
        public string? ActivePowerPlanGuid { get; set; }
        
        // V2: 服务状态 (ServiceName -> StartType as int)
        public Dictionary<string, int> ServiceStates { get; set; } = new();
        
        // V2: BCD 设置 (SettingName -> Value)
        public Dictionary<string, string?> BcdSettings { get; set; } = new();
        
        // V3: 数据完整性校验
        public string? Checksum { get; set; }
        
        // V3: 注册表 .reg 文件备份标记（默认 false，旧快照反序列化自动兼容）
        public bool HasRegistryBackup { get; set; }

        // V4: 可由 ChangeOrchestrator 精确恢复的类型化 HKCU DWORD 清单。
        public List<SnapshotTypedRegistryDwordState> TypedRegistryDwordStates { get; set; } = new();

        // V5: 单值 REG_SZ 与同键多值 String/DWORD 能力的完整目标清单。
        public List<SnapshotTypedRegistryCapabilityState> TypedRegistryCapabilityStates { get; set; } = new();
        
        // 兼容旧版本
        public List<string> AppliedOperations { get; set; } = new();
        
        // 统计信息
        public int TotalOperations => OperationStates.Count;
        public int AppliedCount => OperationStates.Count > 0 
            ? System.Linq.Enumerable.Count(OperationStates.Values, v => v) 
            : AppliedOperations.Count;
    }

    public class SnapshotInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int SettingsCount { get; set; }
        public int OperationsCount { get; set; }
        public int AppliedCount { get; set; }
        public string? PowerPlanName { get; set; }
        public bool HasRegistryBackup { get; set; }
        
        public string RegBackupText => HasRegistryBackup
            ? LocalizationService.Instance.Get("Backup.RegBackupTag")
            : string.Empty;
        
        public string SettingsCountText => LocalizationService.Instance.Get("Backup.Settings");
        public string OperationsCountText => LocalizationService.Instance.Get("Backup.Operations");
        public string RegistryText => LocalizationService.Instance.Get("Backup.Registry");
        public string ServiceConfigText => LocalizationService.Instance.Get("Backup.ServiceConfig");
        public string RestoreText => LocalizationService.Instance.Get("Backup.Restore");
    }

    public class SnapshotRestoreResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string OutcomeReasonCode { get; set; } = "snapshot.not-started";
        public Guid BatchPlanId { get; set; }
        public SnapshotRestoreBatchState BatchState { get; set; } = SnapshotRestoreBatchState.Available;
        public SnapshotMutationStatus MutationStatus { get; set; } = SnapshotMutationStatus.None;
        public SnapshotCompensationStatus CompensationStatus { get; set; } = SnapshotCompensationStatus.NotRequired;
        public int RestoredCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public int UnsupportedCount { get; set; }
        public int CompensatedCount { get; set; }
        public bool RequiresReboot { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<SnapshotTypedRestoreReceipt> TypedReceipts { get; set; } = new();
        public List<SnapshotTypedRestoreReceipt> CompensationReceipts { get; set; } = new();
    }

    public sealed record SnapshotCatalogDiagnostic(
        string FileName,
        string ReasonCode,
        string Message);

    public sealed record SnapshotCatalogResult(
        IReadOnlyList<SnapshotInfo> Snapshots,
        IReadOnlyList<SnapshotCatalogDiagnostic> Diagnostics);

    public sealed record SnapshotRestoreBatchIdentity(
        Guid PlanId,
        string PlanSha256,
        string SnapshotId,
        SnapshotRestoreBatchState State,
        SnapshotMutationStatus MutationStatus,
        string ReasonCode,
        DateTimeOffset UpdatedAtUtc,
        bool StoreFailure,
        string Diagnostic);

    public sealed record SnapshotTypedRegistryDwordState(
        string CapabilityId,
        string SourceKey,
        bool ValueExists,
        int? Value);

    public enum SnapshotRegistryScalarKind
    {
        String,
        DWord
    }

    public sealed record SnapshotTypedRegistryValueState(
        string ValueName,
        SnapshotRegistryScalarKind Kind,
        bool ValueExists,
        string? StringValue,
        int? DwordValue);

    public sealed record SnapshotTypedRegistryCapabilityState(
        string CapabilityId,
        string SubKeyPath,
        IReadOnlyList<SnapshotTypedRegistryValueState> Values);

    public sealed record SnapshotTypedRestoreReceipt(
        string CapabilityId,
        Guid ReceiptId,
        Guid? RecoveryId,
        string Outcome,
        string ReasonCode,
        string Message,
        bool MutationOccurred);

    public class SnapshotOperationDetail
    {
        public string OperationId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool WasApplied { get; set; }
        public bool CurrentlyApplied { get; set; }
        public string Action { get; set; } = string.Empty; // "Apply", "Restore", "NoChange"
    }

    /// <summary>
    /// 旧深度注册表恢复兼容结果；当前入口永久 fail-closed。
    /// </summary>
    public class RegistryBackupRestoreResult
    {
        public bool Success { get; set; }
        public Guid BatchPlanId { get; set; }
        public SnapshotRestoreBatchState BatchState { get; set; } = SnapshotRestoreBatchState.Available;
        public SnapshotMutationStatus MutationStatus { get; set; } = SnapshotMutationStatus.None;
        public int RestoredCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> RestoredFiles { get; set; } = new();
        public List<string> FailedFiles { get; set; } = new();
        public bool RequiresReboot { get; set; }
    }
}
