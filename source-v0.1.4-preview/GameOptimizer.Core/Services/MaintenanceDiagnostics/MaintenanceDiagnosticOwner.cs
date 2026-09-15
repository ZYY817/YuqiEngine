using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Core.Services.MaintenanceDiagnostics;

public enum MaintenanceDiagnosticTargetKind
{
    SystemVolume,
    WindowsService
}

public enum MaintenanceDiagnosticOutcome
{
    Healthy,
    Degraded,
    Observed,
    NotApplicable,
    PermissionDenied,
    Unknown
}

public sealed record MaintenanceDiagnosticDefinition(
    string RepairItemId,
    string CapabilityId,
    MaintenanceDiagnosticTargetKind TargetKind,
    string Target,
    string BlockReason);

public sealed record MaintenanceDiagnosticSnapshot(
    string RepairItemId,
    MaintenanceDiagnosticOutcome Outcome,
    bool? IsHealthy,
    string? ResourceIdentity,
    IReadOnlyDictionary<string, string> Evidence,
    string Message)
{
    public static MaintenanceDiagnosticSnapshot Failed(
        string repairItemId,
        MaintenanceDiagnosticOutcome outcome,
        string message) =>
        new(repairItemId, outcome, null, null, new Dictionary<string, string>(), message);
}

public interface IMaintenanceDiagnosticPlatform
{
    Task<MaintenanceDiagnosticSnapshot> InspectAsync(
        MaintenanceDiagnosticDefinition definition,
        CancellationToken cancellationToken);
}

public interface IMaintenanceDiagnosticOwner
{
    Task<MaintenanceDiagnosticSnapshot> DiagnoseAsync(string repairItemId, CancellationToken cancellationToken);

    Task<GameOptimizer.Core.Services.Repair.RepairResult> RepairAsync(
        string repairItemId,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken);
}

public static class MaintenanceDiagnosticCatalog
{
    private static readonly IReadOnlyDictionary<string, MaintenanceDiagnosticDefinition> Definitions =
        CreateDefinitions().ToDictionary(value => value.RepairItemId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<MaintenanceDiagnosticDefinition> All => Definitions.Values.ToArray();

    public static bool TryGet(string repairItemId, out MaintenanceDiagnosticDefinition definition) =>
        Definitions.TryGetValue(repairItemId, out definition!);

    private static IEnumerable<MaintenanceDiagnosticDefinition> CreateDefinitions()
    {
        yield return Volume(
            "repair.chkdsk",
            "maintenance.volume.dirty-state",
            "系统卷只允许读取 DirtyBit 与卷状态；调度 chkdsk 修复需要独立、可恢复的磁盘 Owner。");
        yield return Service(
            "repair.location_service",
            "maintenance.service.location",
            "lfsvc",
            "位置服务启停与同意设置是不同资源；缺少完整快照与恢复链时禁止修改。");
        yield return Service(
            "repair.memory_service",
            "maintenance.service.memory",
            "SysMain",
            "SysMain 当前状态只能作为诊断证据，不能证明重启服务等同于修复内存管理。");
        yield return Service(
            "repair.search_index",
            "maintenance.service.search",
            "WSearch",
            "重建搜索索引会删除持久数据；缺少隔离、恢复和回读契约时禁止执行。");
        yield return Service(
            "repair.storage_service",
            "maintenance.service.storage",
            "StorSvc",
            "存储服务重启缺少依赖服务、启动类型与执行前后状态的完整恢复材料。");
        yield return Service(
            "repair.superfetch",
            "maintenance.service.superfetch",
            "SysMain",
            "Superfetch/SysMain 启动类型和运行状态可能是用户策略；未证明目标语义前禁止重置。");
    }

    private static MaintenanceDiagnosticDefinition Volume(string id, string capability, string reason) =>
        new(id, capability, MaintenanceDiagnosticTargetKind.SystemVolume, "system-volume", reason);

    private static MaintenanceDiagnosticDefinition Service(
        string id,
        string capability,
        string serviceName,
        string reason) =>
        new(id, capability, MaintenanceDiagnosticTargetKind.WindowsService, serviceName, reason);
}

public sealed class MaintenanceDiagnosticOwner : IMaintenanceDiagnosticOwner
{
    private readonly IMaintenanceDiagnosticPlatform _platform;

    public MaintenanceDiagnosticOwner(IMaintenanceDiagnosticPlatform platform) =>
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public async Task<MaintenanceDiagnosticSnapshot> DiagnoseAsync(
        string repairItemId,
        CancellationToken cancellationToken)
    {
        if (!MaintenanceDiagnosticCatalog.TryGet(repairItemId, out var definition))
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                repairItemId,
                MaintenanceDiagnosticOutcome.NotApplicable,
                "未知维护诊断项，已拒绝诊断与写入。");
        }

        try
        {
            return await _platform.InspectAsync(definition, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                repairItemId,
                MaintenanceDiagnosticOutcome.PermissionDenied,
                $"只读诊断权限不足：{ex.Message}");
        }
        catch (Exception ex)
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                repairItemId,
                MaintenanceDiagnosticOutcome.Unknown,
                $"只读诊断失败：{ex.Message}");
        }
    }

    public async Task<GameOptimizer.Core.Services.Repair.RepairResult> RepairAsync(
        string repairItemId,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!MaintenanceDiagnosticCatalog.TryGet(repairItemId, out var definition))
        {
            return GameOptimizer.Core.Services.Repair.RepairResult.Failed("未知维护诊断项，已拒绝执行。");
        }

        progress?.Report(new RepairProgress { Message = "正在执行权威只读诊断…", PercentComplete = 20 });
        var snapshot = await DiagnoseAsync(repairItemId, cancellationToken).ConfigureAwait(false);
        progress?.Report(new RepairProgress { Message = "系统写入已由安全边界阻断。", PercentComplete = 100 });

        var identity = string.IsNullOrWhiteSpace(snapshot.ResourceIdentity)
            ? "未解析"
            : snapshot.ResourceIdentity;
        return GameOptimizer.Core.Services.Repair.RepairResult.Failed(
            $"{definition.BlockReason} 当前仅开放只读诊断，不会修改服务、卷、索引或注册表。",
            detail: $"Outcome={snapshot.Outcome}; Resource={identity}; {snapshot.Message}");
    }
}

internal sealed class UnavailableMaintenanceDiagnosticPlatform : IMaintenanceDiagnosticPlatform
{
    public Task<MaintenanceDiagnosticSnapshot> InspectAsync(
        MaintenanceDiagnosticDefinition definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MaintenanceDiagnosticSnapshot.Failed(
            definition.RepairItemId,
            MaintenanceDiagnosticOutcome.Unknown,
            "维护诊断平台未接入。"));
    }
}

public static class MaintenanceDiagnosticOwnerFallback
{
    public static IMaintenanceDiagnosticOwner Instance { get; } =
        new MaintenanceDiagnosticOwner(new UnavailableMaintenanceDiagnosticPlatform());
}
