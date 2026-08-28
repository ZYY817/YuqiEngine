using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

public enum ManagedPowerProfileId
{
    IntelGaming,
    AmdGaming,
    MaximumPerformance
}

public sealed record ManagedPowerSettingDefinition(
    string Id,
    string Name,
    string Description,
    Guid SubgroupGuid,
    Guid SettingGuid,
    int Minimum,
    int Maximum,
    string Unit,
    bool IntelHybridOnly = false);

public sealed record ManagedPowerSettingValue(string SettingId, int AcValue, int? DcValue);

public sealed record ManagedPowerProfile(
    ManagedPowerProfileId Id,
    string Name,
    string Description,
    CpuVendor Vendor,
    IReadOnlyList<ManagedPowerSettingValue> Settings,
    bool DesktopOnly,
    string Tradeoff);

public sealed record ManagedPowerPlanRecord(
    Guid PlanId,
    Guid SourcePlanId,
    string Name,
    ManagedPowerProfileId ProfileId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ManagedPowerPlanBackup(
    Guid BackupId,
    Guid PlanId,
    Guid ActivePlanBefore,
    string Operation,
    string FilePath,
    string Sha256,
    DateTimeOffset CreatedAtUtc);

public sealed record ManagedPowerPlanOverview(
    IReadOnlyList<ManagedPowerProfileAvailability> Profiles,
    IReadOnlyList<ManagedPowerPlanRecord> Plans,
    IReadOnlyList<ManagedPowerPlanBackup> Backups,
    CpuVendor CpuVendor,
    CpuArchitecture CpuArchitecture,
    bool CanMutate,
    string Status);

public sealed record ManagedPowerProfileAvailability(
    ManagedPowerProfile Profile,
    bool IsAvailable,
    string Status);

public enum ManagedPowerOperationKind
{
    Create,
    Update,
    Activate,
    Restore,
    Delete
}

public sealed record ManagedPowerPlanDraft(
    Guid OperationId,
    string OperationHash,
    ManagedPowerOperationKind Kind,
    Guid SourcePlanId,
    Guid TargetPlanId,
    string TargetName,
    ManagedPowerProfileId ProfileId,
    IReadOnlyList<ManagedPowerSettingValue> Settings,
    Guid? BackupId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Summary,
    string Risk);

public sealed record ManagedPowerPlanResult(
    bool Success,
    bool Changed,
    string Status,
    string Message,
    ManagedPowerPlanRecord? Plan = null,
    ManagedPowerPlanBackup? Backup = null);

public interface IManagedPowerPlanService
{
    Task<ManagedPowerPlanOverview> InspectAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedPowerSettingValue>> ReadSettingsAsync(Guid managedPlanId, CancellationToken cancellationToken);
    Task<ManagedPowerPlanDraft> PrepareCreateAsync(Guid sourcePlanId, ManagedPowerProfileId profileId, CancellationToken cancellationToken);
    Task<ManagedPowerPlanDraft> PrepareCreateAsync(Guid sourcePlanId, ManagedPowerProfileId profileId, string? customName, CancellationToken cancellationToken);
    Task<ManagedPowerPlanDraft> PrepareUpdateAsync(Guid managedPlanId, IReadOnlyList<ManagedPowerSettingValue> settings, CancellationToken cancellationToken);
    Task<ManagedPowerPlanDraft> PrepareActivateAsync(Guid managedPlanId, CancellationToken cancellationToken);
    Task<ManagedPowerPlanDraft> PrepareRestoreAsync(Guid backupId, CancellationToken cancellationToken);
    Task<ManagedPowerPlanDraft> PrepareDeleteAsync(Guid managedPlanId, CancellationToken cancellationToken);
    Task<ManagedPowerPlanResult> ExecuteAsync(ManagedPowerPlanDraft draft, CancellationToken cancellationToken);
}

public static class ManagedPowerPlanCatalog
{
    public static readonly Guid ProcessorEnergyPreference = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");

    public static IReadOnlyList<ManagedPowerSettingDefinition> Settings { get; } =
    [
        new("cpu.minimum", "处理器最小状态", "空闲时允许处理器降低到的性能百分比。", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorMinState, 0, 100, "%"),
        new("cpu.maximum", "处理器最大状态", "处理器允许达到的最高性能百分比。", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorMaxState, 1, 100, "%"),
        new("cpu.boost", "性能提升模式", "0 禁用、1 启用、2 激进、3 高效启用、4 高效激进。", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorBoostMode, 0, 4, "模式"),
        new("cpu.epp", "能效性能偏好（EPP）", "0 偏向性能，100 偏向节能；仅在平台支持 CPPC/HWP 时生效。", PowerGuids.ProcessorSubgroup, ProcessorEnergyPreference, 0, 100, "%"),
        new("cpu.hetero-policy", "异构长线程调度", "0 全部、1 仅性能核、2 偏好性能核、3 仅能效核、4 偏好能效核、5 自动。", PowerGuids.ProcessorSubgroup, PowerGuids.HeteroThreadSchedulingPolicy, 0, 5, "策略", IntelHybridOnly: true)
    ];

    public static IReadOnlyList<ManagedPowerProfile> Profiles { get; } =
    [
        new(ManagedPowerProfileId.IntelGaming, "Intel 游戏性能", "为 Intel HWP/CPPC 保留动态降频，同时提高前台负载响应。", CpuVendor.Intel,
            [new("cpu.minimum", 5, 5), new("cpu.maximum", 100, 100), new("cpu.boost", 2, 2), new("cpu.epp", 15, 35)],
            DesktopOnly: false, "可能增加温度、风扇噪声与耗电；不改写 P/E 核调度策略。"),
        new(ManagedPowerProfileId.AmdGaming, "AMD 游戏性能", "面向 AMD CPPC 的响应型方案，保留空闲降频与 OEM 调度。", CpuVendor.AMD,
            [new("cpu.minimum", 5, 5), new("cpu.maximum", 100, 100), new("cpu.boost", 2, 2), new("cpu.epp", 20, 40)],
            DesktopOnly: false, "可能增加温度、风扇噪声与耗电；不会关闭 C-State、P-State 或核心停放。"),
        new(ManagedPowerProfileId.MaximumPerformance, "极限性能（台式机）", "锁定较高性能倾向，仅用于散热和供电充足的台式机。", CpuVendor.Unknown,
            [new("cpu.minimum", 100, null), new("cpu.maximum", 100, null), new("cpu.boost", 2, null), new("cpu.epp", 0, null)],
            DesktopOnly: true, "显著增加待机功耗、温度和噪声；不保证提升帧率。")
    ];

    public static ManagedPowerSettingDefinition GetSetting(string id) =>
        Settings.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"未知的受管电源设置：{id}");
}
