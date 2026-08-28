using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

public sealed class ManagedPowerPlanService : IManagedPowerPlanService
{
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromMinutes(5);
    private readonly IManagedPowerPlanPlatform _platform;
    private readonly IManagedPowerPlanStore _store;
    private readonly IPowerPlatform _powerPlatform;
    private readonly IPowerSettingAccessor _settings;
    private readonly IPowerSettingCapabilityGuard _guard;
    private readonly IPowerSchemeAccessor _schemes;
    private readonly IPowerPlatformFactsProvider _factsProvider;
    private readonly Func<CpuTopology> _topologyReader;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, string> _pending = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public ManagedPowerPlanService(
        IManagedPowerPlanPlatform platform,
        IManagedPowerPlanStore store,
        IPowerPlatform powerPlatform,
        IPowerSettingAccessor settings,
        IPowerSettingCapabilityGuard guard,
        IPowerSchemeAccessor schemes,
        IPowerPlatformFactsProvider factsProvider,
        CpuTopologyService topologyService,
        TimeProvider? timeProvider = null,
        Func<CpuTopology>? topologyReader = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _powerPlatform = powerPlatform ?? throw new ArgumentNullException(nameof(powerPlatform));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _schemes = schemes ?? throw new ArgumentNullException(nameof(schemes));
        _factsProvider = factsProvider ?? throw new ArgumentNullException(nameof(factsProvider));
        ArgumentNullException.ThrowIfNull(topologyService);
        _topologyReader = topologyReader ?? topologyService.GetTopology;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ManagedPowerPlanOverview> InspectAsync(CancellationToken cancellationToken)
    {
        var facts = _factsProvider.GetCurrent();
        var topology = GetTopologySafe();
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var registered = await _powerPlatform.EnumerateSchemesAsync(cancellationToken).ConfigureAwait(false);
        var ids = registered.Select(item => item.Id).ToHashSet();
        var plans = state.Plans.Where(item => ids.Contains(item.PlanId)).ToArray();
        var managedIds = plans.Select(item => item.PlanId).ToHashSet();
        var restorableBackups = state.Backups.Where(item => managedIds.Contains(item.PlanId)).ToArray();
        var canMutate = CanMutate(facts, out var status);
        var profiles = ManagedPowerPlanCatalog.Profiles.Select(profile =>
        {
            var available = canMutate
                && (profile.Vendor == CpuVendor.Unknown || profile.Vendor == topology.Vendor)
                && (!profile.DesktopOnly || !facts.HasBattery);
            var reason = available ? "可创建受管副本" : GetProfileBlockReason(profile, facts, topology, status);
            return new ManagedPowerProfileAvailability(profile, available, reason);
        }).ToArray();
        return new ManagedPowerPlanOverview(profiles, plans, restorableBackups, topology.Vendor, topology.Architecture, canMutate, status);
    }

    public async Task<IReadOnlyList<ManagedPowerSettingValue>> ReadSettingsAsync(Guid managedPlanId, CancellationToken cancellationToken)
    {
        var plan = await GetManagedPlanAsync(managedPlanId, cancellationToken).ConfigureAwait(false);
        var topology = GetTopologySafe();
        var result = new List<ManagedPowerSettingValue>();
        foreach (var definition in ManagedPowerPlanCatalog.Settings)
        {
            if (definition.IntelHybridOnly && topology.Architecture != CpuArchitecture.IntelHybrid)
                continue;
            var current = await _settings.ReadValueAsync(plan.PlanId, definition.SubgroupGuid, definition.SettingGuid, cancellationToken).ConfigureAwait(false);
            if (current.AcValue.HasValue)
                result.Add(new(definition.Id, current.AcValue.Value, current.DcValue));
        }
        return result;
    }

    public async Task<ManagedPowerPlanDraft> PrepareCreateAsync(
        Guid sourcePlanId,
        ManagedPowerProfileId profileId,
        CancellationToken cancellationToken)
        => await PrepareCreateAsync(sourcePlanId, profileId, customName: null, cancellationToken: cancellationToken).ConfigureAwait(false);

    public async Task<ManagedPowerPlanDraft> PrepareCreateAsync(
        Guid sourcePlanId,
        ManagedPowerProfileId profileId,
        string? customName,
        CancellationToken cancellationToken)
    {
        var overview = await InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!overview.CanMutate)
            throw new InvalidOperationException(overview.Status);
        var availability = overview.Profiles.SingleOrDefault(item => item.Profile.Id == profileId)
            ?? throw new InvalidDataException("所选内置电源策略不存在。");
        if (!availability.IsAvailable)
            throw new InvalidOperationException(availability.Status);
        await EnsureRegisteredSchemeAsync(sourcePlanId, cancellationToken).ConfigureAwait(false);

        var supported = await GetSupportedSettingsAsync(sourcePlanId, availability.Profile.Settings, requireAll: false, cancellationToken)
            .ConfigureAwait(false);
        if (supported.Count < 2 || supported.All(item => item.SettingId is not "cpu.maximum" and not "cpu.epp"))
            throw new InvalidOperationException("源计划没有公开足够的已验证 CPU 参数，未创建不完整策略。");

        var target = Guid.NewGuid();
        var name = NormalizeCreatePlanName(customName, availability.Profile.Name);
        var skipped = availability.Profile.Settings.Count - supported.Count;
        return RegisterDraft(new ManagedPowerPlanDraft(
            Guid.NewGuid(), string.Empty, ManagedPowerOperationKind.Create, sourcePlanId, target, name, profileId,
            supported, null, _timeProvider.GetUtcNow(), _timeProvider.GetUtcNow() + DraftLifetime,
            $"先导出源计划备份，再创建“{name}”受管副本、写入 {supported.Count} 项已支持参数并激活{(skipped > 0 ? $"；{skipped} 项不受支持将跳过" : string.Empty)}。",
            availability.Profile.Tradeoff));
    }

    private string NormalizeCreatePlanName(string? customName, string profileName)
    {
        if (string.IsNullOrWhiteSpace(customName))
        {
            var suffix = _timeProvider.GetUtcNow().ToLocalTime().ToString("MMdd-HHmm");
            return $"宇奇 · {profileName} · {suffix}";
        }

        var name = customName.Trim();
        if (name.Length is < 1 or > 80)
            throw new InvalidDataException("自定义电源计划名称应为 1 到 80 个字符。");
        if (name.Any(char.IsControl))
            throw new InvalidDataException("自定义电源计划名称不能包含控制字符。");
        return name;
    }

    public async Task<ManagedPowerPlanDraft> PrepareUpdateAsync(
        Guid managedPlanId,
        IReadOnlyList<ManagedPowerSettingValue> settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var plan = await GetManagedPlanAsync(managedPlanId, cancellationToken).ConfigureAwait(false);
        EnsureCanMutate();
        if (settings.Count == 0)
            throw new InvalidDataException("至少选择一个高级电源参数。");
        var normalized = ValidateValues(settings, GetTopologySafe()).ToArray();
        ValidateProcessorRange(normalized);
        await GetSupportedSettingsAsync(managedPlanId, normalized, requireAll: true, cancellationToken).ConfigureAwait(false);
        return RegisterDraft(new ManagedPowerPlanDraft(
            Guid.NewGuid(), string.Empty, ManagedPowerOperationKind.Update, plan.SourcePlanId, plan.PlanId, plan.Name,
            plan.ProfileId, normalized, null, _timeProvider.GetUtcNow(), _timeProvider.GetUtcNow() + DraftLifetime,
            $"先完整备份“{plan.Name}”，再写入并回读 {normalized.Length} 项高级参数，成功后激活此受管计划。",
            "更高性能倾向可能增加功耗、温度和风扇噪声；原 Windows/OEM 计划不会被修改。"));
    }

    public async Task<ManagedPowerPlanDraft> PrepareActivateAsync(Guid managedPlanId, CancellationToken cancellationToken)
    {
        var plan = await GetManagedPlanAsync(managedPlanId, cancellationToken).ConfigureAwait(false);
        EnsureCanMutate();
        return RegisterDraft(new ManagedPowerPlanDraft(
            Guid.NewGuid(), string.Empty, ManagedPowerOperationKind.Activate, plan.SourcePlanId, plan.PlanId, plan.Name,
            plan.ProfileId, [], null, _timeProvider.GetUtcNow(), _timeProvider.GetUtcNow() + DraftLifetime,
            $"先完整备份“{plan.Name}”，再将其设为活动电源计划并回读确认。",
            "切换计划会立即改变整机功耗策略；不改写原 Windows/OEM 计划。"));
    }

    public async Task<ManagedPowerPlanDraft> PrepareRestoreAsync(Guid backupId, CancellationToken cancellationToken)
    {
        EnsureCanMutate();
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var backup = state.Backups.SingleOrDefault(item => item.BackupId == backupId)
            ?? throw new InvalidDataException("找不到所选受管电源计划备份。");
        var plan = state.Plans.SingleOrDefault(item => item.PlanId == backup.PlanId)
            ?? throw new InvalidOperationException("备份目标不再是宇奇受管计划，已阻止恢复。");
        VerifyBackupFile(backup);
        return RegisterDraft(new ManagedPowerPlanDraft(
            Guid.NewGuid(), string.Empty, ManagedPowerOperationKind.Restore, plan.SourcePlanId, plan.PlanId, plan.Name,
            plan.ProfileId, [], backup.BackupId, _timeProvider.GetUtcNow(), _timeProvider.GetUtcNow() + DraftLifetime,
            $"先备份当前“{plan.Name}”，再用 {backup.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} 的完整 .pow 备份恢复并回读计划存在性。",
            "恢复会短暂切换活动计划；只允许恢复宇奇受管计划，Windows/OEM 原计划不受影响。"));
    }

    public async Task<ManagedPowerPlanDraft> PrepareDeleteAsync(Guid managedPlanId, CancellationToken cancellationToken)
    {
        var plan = await GetManagedPlanAsync(managedPlanId, cancellationToken).ConfigureAwait(false);
        EnsureCanMutate();
        await EnsureRegisteredSchemeAsync(plan.SourcePlanId, cancellationToken).ConfigureAwait(false);
        return RegisterDraft(new ManagedPowerPlanDraft(
            Guid.NewGuid(), string.Empty, ManagedPowerOperationKind.Delete, plan.SourcePlanId, plan.PlanId, plan.Name,
            plan.ProfileId, [], null, _timeProvider.GetUtcNow(), _timeProvider.GetUtcNow() + DraftLifetime,
            $"先完整备份“{plan.Name}”，再切回其源计划并删除这个宇奇受管副本；随后回读活动计划与副本不存在性。",
            "不会改写 Windows/OEM 源计划。删除前导出的完整 .pow 备份会保留在本地备份历史中。"));
    }

    public async Task<ManagedPowerPlanResult> ExecuteAsync(ManagedPowerPlanDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateAndConsumeDraft(draft);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanMutate();
            return draft.Kind switch
            {
                ManagedPowerOperationKind.Create => await ExecuteCreateAsync(draft, cancellationToken).ConfigureAwait(false),
                ManagedPowerOperationKind.Update => await ExecuteUpdateAsync(draft, cancellationToken).ConfigureAwait(false),
                ManagedPowerOperationKind.Activate => await ExecuteActivateAsync(draft, cancellationToken).ConfigureAwait(false),
                ManagedPowerOperationKind.Restore => await ExecuteRestoreAsync(draft, cancellationToken).ConfigureAwait(false),
                ManagedPowerOperationKind.Delete => await ExecuteDeleteAsync(draft, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidDataException("未知的受管电源操作。")
            };
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<ManagedPowerPlanResult> ExecuteCreateAsync(ManagedPowerPlanDraft draft, CancellationToken cancellationToken)
    {
        await EnsureRegisteredSchemeAsync(draft.SourcePlanId, cancellationToken).ConfigureAwait(false);
        await EnsureSchemeAbsentAsync(draft.TargetPlanId, cancellationToken).ConfigureAwait(false);
        var activeBefore = await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var backup = await CreateBackupAsync(draft.SourcePlanId, activeBefore, "create-source", cancellationToken).ConfigureAwait(false);
        var duplicated = false;
        try
        {
            EnsureSuccess(await _platform.DuplicateAsync(draft.SourcePlanId, draft.TargetPlanId, cancellationToken).ConfigureAwait(false), "复制电源计划");
            duplicated = true;
            EnsureSuccess(await _platform.RenameAsync(draft.TargetPlanId, draft.TargetName, cancellationToken).ConfigureAwait(false), "命名受管电源计划");
            await ApplyAndVerifyAsync(draft.TargetPlanId, draft.Settings, cancellationToken).ConfigureAwait(false);
            await _schemes.SetActiveSchemeAsync(draft.TargetPlanId, cancellationToken).ConfigureAwait(false);
            if (await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false) != draft.TargetPlanId)
                throw new InvalidOperationException("激活后的电源计划回读不一致。");

            var now = _timeProvider.GetUtcNow();
            var plan = new ManagedPowerPlanRecord(draft.TargetPlanId, draft.SourcePlanId, draft.TargetName, draft.ProfileId, now, now);
            var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(state with { Plans = state.Plans.Append(plan).ToArray() }, cancellationToken).ConfigureAwait(false);
            return new(true, true, "created.verified", "受管电源计划已创建、写入、激活并回读确认；源计划备份已校验。", plan, backup);
        }
        catch (Exception ex)
        {
            var rollback = await RollbackNewPlanAsync(draft, duplicated, activeBefore).ConfigureAwait(false);
            return new(false, duplicated, rollback ? "create.compensated" : "create.rollback-failed",
                rollback ? $"创建失败，已移除不完整副本并恢复原活动计划：{ex.Message}" : $"创建失败且自动补偿未能确认：{ex.Message}；完整源计划备份位于 {backup.FilePath}", null, backup);
        }
    }

    private async Task<ManagedPowerPlanResult> ExecuteUpdateAsync(ManagedPowerPlanDraft draft, CancellationToken cancellationToken)
    {
        var plan = await GetManagedPlanAsync(draft.TargetPlanId, cancellationToken).ConfigureAwait(false);
        var activeBefore = await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var before = await ReadValuesAsync(plan.PlanId, draft.Settings, cancellationToken).ConfigureAwait(false);
        var backup = await CreateBackupAsync(plan.PlanId, activeBefore, "update", cancellationToken).ConfigureAwait(false);
        try
        {
            await ApplyAndVerifyAsync(plan.PlanId, draft.Settings, cancellationToken).ConfigureAwait(false);
            await _schemes.SetActiveSchemeAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
            if (await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false) != plan.PlanId)
                throw new InvalidOperationException("激活后的电源计划回读不一致。");
            var updated = plan with { UpdatedAtUtc = _timeProvider.GetUtcNow() };
            await ReplacePlanAsync(updated, cancellationToken).ConfigureAwait(false);
            return new(true, true, "updated.verified", "高级参数已仅写入受管副本，完整备份与写后回读均已验证。", updated, backup);
        }
        catch (Exception ex)
        {
            var compensated = await TryRestoreValuesAsync(plan.PlanId, before, activeBefore).ConfigureAwait(false);
            return new(false, true, compensated ? "update.compensated" : "update.rollback-failed",
                compensated ? $"更新失败，已恢复写入前参数并回读确认：{ex.Message}" : $"更新失败且参数补偿未能确认：{ex.Message}；完整 .pow 备份位于 {backup.FilePath}", plan, backup);
        }
    }

    private async Task<ManagedPowerPlanResult> ExecuteActivateAsync(ManagedPowerPlanDraft draft, CancellationToken cancellationToken)
    {
        var plan = await GetManagedPlanAsync(draft.TargetPlanId, cancellationToken).ConfigureAwait(false);
        var activeBefore = await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var backup = await CreateBackupAsync(plan.PlanId, activeBefore, "activate", cancellationToken).ConfigureAwait(false);
        try
        {
            await _schemes.SetActiveSchemeAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
            var verified = await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false) == plan.PlanId;
            if (!verified)
                throw new InvalidOperationException("活动计划回读不一致。");
            return new(true, activeBefore != plan.PlanId, "activated.verified", "受管电源计划已激活并回读确认；激活前完整备份已校验。", plan, backup);
        }
        catch (Exception ex)
        {
            var restored = await TrySetActiveAsync(activeBefore).ConfigureAwait(false);
            return new(false, true, restored ? "activate.compensated" : "activate.rollback-failed",
                restored ? $"激活失败，已恢复原活动计划：{ex.Message}" : $"激活失败且原活动计划恢复未能确认：{ex.Message}", plan, backup);
        }
    }

    private async Task<ManagedPowerPlanResult> ExecuteRestoreAsync(ManagedPowerPlanDraft draft, CancellationToken cancellationToken)
    {
        var plan = await GetManagedPlanAsync(draft.TargetPlanId, cancellationToken).ConfigureAwait(false);
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var selected = state.Backups.Single(item => item.BackupId == draft.BackupId);
        VerifyBackupFile(selected);
        var activeBefore = await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var safetyBackup = await CreateBackupAsync(plan.PlanId, activeBefore, "pre-restore", cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeBefore == plan.PlanId)
                await _schemes.SetActiveSchemeAsync(plan.SourcePlanId, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(await _platform.DeleteAsync(plan.PlanId, cancellationToken).ConfigureAwait(false), "移除待恢复的受管计划");
            EnsureSuccess(await _platform.ImportAsync(selected.FilePath, plan.PlanId, cancellationToken).ConfigureAwait(false), "导入受管计划备份");
            await EnsureRegisteredSchemeAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
            if (activeBefore == plan.PlanId)
                await _schemes.SetActiveSchemeAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
            var updated = plan with { UpdatedAtUtc = _timeProvider.GetUtcNow() };
            await ReplacePlanAsync(updated, cancellationToken).ConfigureAwait(false);
            return new(true, true, "restore.verified", "完整电源计划备份已恢复，计划 GUID 与活动状态已回读确认。", updated, safetyBackup);
        }
        catch (Exception ex)
        {
            return new(false, true, "restore.failed", $"恢复未完成：{ex.Message}；恢复前安全备份位于 {safetyBackup.FilePath}", plan, safetyBackup);
        }
    }

    private async Task<ManagedPowerPlanResult> ExecuteDeleteAsync(ManagedPowerPlanDraft draft, CancellationToken cancellationToken)
    {
        var plan = await GetManagedPlanAsync(draft.TargetPlanId, cancellationToken).ConfigureAwait(false);
        await EnsureRegisteredSchemeAsync(plan.SourcePlanId, cancellationToken).ConfigureAwait(false);
        var activeBefore = await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var backup = await CreateBackupAsync(plan.PlanId, activeBefore, "delete", cancellationToken).ConfigureAwait(false);
        try
        {
            await _schemes.SetActiveSchemeAsync(plan.SourcePlanId, cancellationToken).ConfigureAwait(false);
            if (await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false) != plan.SourcePlanId)
                throw new InvalidOperationException("切回源电源计划的回读不一致。");

            EnsureSuccess(await _platform.DeleteAsync(plan.PlanId, cancellationToken).ConfigureAwait(false), "删除受管电源计划");
            var remaining = await _powerPlatform.EnumerateSchemesAsync(cancellationToken).ConfigureAwait(false);
            if (remaining.Any(item => item.Id == plan.PlanId))
                throw new InvalidOperationException("删除受管电源计划后的计划枚举仍包含该计划。");
            if (await _schemes.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false) != plan.SourcePlanId)
                throw new InvalidOperationException("删除后的活动源计划回读不一致。");

            await RemovePlanAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
            return new(true, true, "deleted.verified", "已切回创建前的源计划并删除受管副本；删除前的完整 .pow 备份已校验并保留。", null, backup);
        }
        catch (Exception ex)
        {
            var restored = await TrySetActiveAsync(activeBefore).ConfigureAwait(false);
            return new(false, true, restored ? "delete.compensated" : "delete.rollback-failed",
                restored
                    ? $"删除未完成，已恢复删除前活动计划：{ex.Message}；删除前安全备份位于 {backup.FilePath}"
                    : $"删除未完成且原活动计划恢复未能确认：{ex.Message}；删除前安全备份位于 {backup.FilePath}",
                plan, backup);
        }
    }

    private async Task<ManagedPowerPlanBackup> CreateBackupAsync(Guid planId, Guid activeBefore, string operation, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_store.BackupDirectory);
        var backupId = Guid.NewGuid();
        var path = Path.GetFullPath(Path.Combine(_store.BackupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{planId:N}-{backupId:N}.pow"));
        EnsureSuccess(await _platform.ExportAsync(planId, path, cancellationToken).ConfigureAwait(false), "导出电源计划备份");
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw new InvalidDataException("powercfg 报告导出成功，但备份文件为空或不存在。");
        var hash = await ComputeFileHashAsync(path, cancellationToken).ConfigureAwait(false);
        var backup = new ManagedPowerPlanBackup(backupId, planId, activeBefore, operation, path, hash, _timeProvider.GetUtcNow());
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _store.SaveAsync(state with { Backups = state.Backups.Append(backup).ToArray() }, cancellationToken).ConfigureAwait(false);
        return backup;
    }

    private async Task<IReadOnlyList<ManagedPowerSettingValue>> GetSupportedSettingsAsync(
        Guid schemeId,
        IReadOnlyList<ManagedPowerSettingValue> values,
        bool requireAll,
        CancellationToken cancellationToken)
    {
        var result = new List<ManagedPowerSettingValue>();
        foreach (var value in ValidateValues(values, GetTopologySafe()))
        {
            var definition = ManagedPowerPlanCatalog.GetSetting(value.SettingId);
            var current = await _settings.ReadValueAsync(schemeId, definition.SubgroupGuid, definition.SettingGuid, cancellationToken).ConfigureAwait(false);
            var guard = await _guard.EvaluateAsync(schemeId, definition.SubgroupGuid, definition.SettingGuid,
                value.AcValue, value.DcValue, $"managed.{value.SettingId}", definition.Name, cancellationToken).ConfigureAwait(false);
            var supported = guard.IsAllowed && current.AcValue.HasValue && (value.DcValue is null || current.DcValue.HasValue);
            if (supported)
                result.Add(value);
            else if (requireAll)
                throw new InvalidOperationException(guard.Reason.Length > 0 ? guard.Reason : $"当前计划不支持“{definition.Name}”。");
        }
        return result;
    }

    private async Task ApplyAndVerifyAsync(Guid schemeId, IReadOnlyList<ManagedPowerSettingValue> values, CancellationToken cancellationToken)
    {
        await GetSupportedSettingsAsync(schemeId, values, requireAll: true, cancellationToken).ConfigureAwait(false);
        foreach (var value in values)
        {
            var definition = ManagedPowerPlanCatalog.GetSetting(value.SettingId);
            await _settings.WriteValueAsync(schemeId, definition.SubgroupGuid, definition.SettingGuid, value.AcValue, value.DcValue, cancellationToken).ConfigureAwait(false);
        }
        var readback = await ReadValuesAsync(schemeId, values, cancellationToken).ConfigureAwait(false);
        if (!ValuesEqual(values, readback))
            throw new InvalidOperationException("高级电源参数写后回读不一致。");
    }

    private async Task<IReadOnlyList<ManagedPowerSettingValue>> ReadValuesAsync(Guid schemeId, IReadOnlyList<ManagedPowerSettingValue> requested, CancellationToken cancellationToken)
    {
        var values = new List<ManagedPowerSettingValue>(requested.Count);
        foreach (var item in requested)
        {
            var definition = ManagedPowerPlanCatalog.GetSetting(item.SettingId);
            var current = await _settings.ReadValueAsync(schemeId, definition.SubgroupGuid, definition.SettingGuid, cancellationToken).ConfigureAwait(false);
            if (!current.AcValue.HasValue)
                throw new InvalidOperationException($"无法读取“{definition.Name}”的 AC 值。");
            values.Add(new(item.SettingId, current.AcValue.Value, item.DcValue.HasValue ? current.DcValue : null));
        }
        return values;
    }

    private async Task<bool> TryRestoreValuesAsync(Guid planId, IReadOnlyList<ManagedPowerSettingValue> values, Guid activeBefore)
    {
        try
        {
            foreach (var value in values)
            {
                var definition = ManagedPowerPlanCatalog.GetSetting(value.SettingId);
                await _settings.WriteValueAsync(planId, definition.SubgroupGuid, definition.SettingGuid, value.AcValue, value.DcValue, CancellationToken.None).ConfigureAwait(false);
            }
            await _schemes.SetActiveSchemeAsync(activeBefore, CancellationToken.None).ConfigureAwait(false);
            var actual = await ReadValuesAsync(planId, values, CancellationToken.None).ConfigureAwait(false);
            return ValuesEqual(values, actual) && await _schemes.GetActiveSchemeAsync(CancellationToken.None).ConfigureAwait(false) == activeBefore;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> RollbackNewPlanAsync(ManagedPowerPlanDraft draft, bool duplicated, Guid activeBefore)
    {
        try
        {
            await _schemes.SetActiveSchemeAsync(activeBefore, CancellationToken.None).ConfigureAwait(false);
            if (duplicated)
                EnsureSuccess(await _platform.DeleteAsync(draft.TargetPlanId, CancellationToken.None).ConfigureAwait(false), "删除不完整受管计划");
            var registered = await _powerPlatform.EnumerateSchemesAsync(CancellationToken.None).ConfigureAwait(false);
            return await _schemes.GetActiveSchemeAsync(CancellationToken.None).ConfigureAwait(false) == activeBefore
                && registered.All(item => item.Id != draft.TargetPlanId);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TrySetActiveAsync(Guid schemeId)
    {
        try
        {
            await _schemes.SetActiveSchemeAsync(schemeId, CancellationToken.None).ConfigureAwait(false);
            return await _schemes.GetActiveSchemeAsync(CancellationToken.None).ConfigureAwait(false) == schemeId;
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<ManagedPowerSettingValue> ValidateValues(IReadOnlyList<ManagedPowerSettingValue> values, CpuTopology topology)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var definition = ManagedPowerPlanCatalog.GetSetting(value.SettingId);
            if (!seen.Add(definition.Id))
                throw new InvalidDataException($"高级参数重复：{definition.Name}");
            if (value.AcValue < definition.Minimum || value.AcValue > definition.Maximum
                || value.DcValue is int dc && (dc < definition.Minimum || dc > definition.Maximum))
                throw new InvalidDataException($"“{definition.Name}”超出允许范围 {definition.Minimum}–{definition.Maximum}。");
            if (definition.IntelHybridOnly && topology.Architecture != CpuArchitecture.IntelHybrid)
                throw new InvalidOperationException($"“{definition.Name}”仅适用于可靠识别的 Intel 混合架构。");
            yield return value;
        }
    }

    private static void ValidateProcessorRange(IReadOnlyList<ManagedPowerSettingValue> values)
    {
        var minimum = values.SingleOrDefault(item => string.Equals(item.SettingId, "cpu.minimum", StringComparison.OrdinalIgnoreCase));
        var maximum = values.SingleOrDefault(item => string.Equals(item.SettingId, "cpu.maximum", StringComparison.OrdinalIgnoreCase));
        if (minimum is not null && maximum is not null && minimum.AcValue > maximum.AcValue)
            throw new InvalidDataException("处理器最小状态不能高于最大状态。");
        if (minimum?.DcValue is int minDc && maximum?.DcValue is int maxDc && minDc > maxDc)
            throw new InvalidDataException("处理器 DC 最小状态不能高于最大状态。");
    }

    private ManagedPowerPlanDraft RegisterDraft(ManagedPowerPlanDraft draft)
    {
        var hash = HashDraft(draft with { OperationHash = string.Empty });
        var frozen = draft with { OperationHash = hash };
        _pending[frozen.OperationId] = hash;
        return frozen;
    }

    private void ValidateAndConsumeDraft(ManagedPowerPlanDraft draft)
    {
        if (draft.ExpiresAtUtc < _timeProvider.GetUtcNow())
            throw new InvalidOperationException("电源操作确认已过期，请重新准备。");
        var actual = HashDraft(draft with { OperationHash = string.Empty });
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(draft.OperationHash))
            || !_pending.TryRemove(draft.OperationId, out var expected)
            || !string.Equals(expected, draft.OperationHash, StringComparison.Ordinal))
            throw new InvalidDataException("电源操作已被修改、重复使用或不是由当前安全链准备。");
    }

    private static string HashDraft(ManagedPowerPlanDraft draft) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft))));

    private async Task<ManagedPowerPlanRecord> GetManagedPlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var plan = state.Plans.SingleOrDefault(item => item.PlanId == planId)
            ?? throw new InvalidOperationException("目标不是宇奇创建并登记的受管电源计划；已阻止写入。");
        await EnsureRegisteredSchemeAsync(planId, cancellationToken).ConfigureAwait(false);
        return plan;
    }

    private async Task ReplacePlanAsync(ManagedPowerPlanRecord plan, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _store.SaveAsync(state with { Plans = state.Plans.Select(item => item.PlanId == plan.PlanId ? plan : item).ToArray() }, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemovePlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _store.SaveAsync(state with { Plans = state.Plans.Where(item => item.PlanId != planId).ToArray() }, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureCanMutate()
    {
        if (!CanMutate(_factsProvider.GetCurrent(), out var status))
            throw new InvalidOperationException(status);
    }

    private static bool CanMutate(PowerPlatformFacts facts, out string status)
    {
        if (!facts.IsWindows || facts.WindowsBuild < 19041) status = "需要 Windows 10 2004 或更高版本。";
        else if (!facts.IsAdministrator) status = "创建或修改受管电源计划需要管理员权限。";
        else if (!facts.IsDetectionReliable) status = "电源、策略或处理器状态无法可靠探测，已按安全策略阻止。";
        else if (facts.IsPolicyRestricted || facts.IsOemRestricted) status = "检测到组织或 OEM 电源策略接管，已阻止覆盖。";
        else if (facts.IsModernStandby) status = "Modern Standby 设备仅保留 Windows/OEM 电源模式管理。";
        else if (facts.HasBattery && !facts.IsOnAcPower) status = "请先接通交流电，再创建或修改性能电源计划。";
        else { status = "受管副本、完整备份与回读验证已就绪"; return true; }
        return false;
    }

    private static string GetProfileBlockReason(ManagedPowerProfile profile, PowerPlatformFacts facts, CpuTopology topology, string common)
    {
        if (profile.Vendor != CpuVendor.Unknown && profile.Vendor != topology.Vendor)
            return $"仅适用于 {profile.Vendor} 处理器";
        if (profile.DesktopOnly && facts.HasBattery)
            return "极限性能策略只向台式机开放";
        return common;
    }

    private CpuTopology GetTopologySafe()
    {
        try { return _topologyReader(); }
        catch { return new CpuTopology(); }
    }

    private async Task EnsureRegisteredSchemeAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        var schemes = await _powerPlatform.EnumerateSchemesAsync(cancellationToken).ConfigureAwait(false);
        if (schemes.All(item => item.Id != schemeId))
            throw new InvalidOperationException("目标电源计划已不存在或无法读取。");
    }

    private async Task EnsureSchemeAbsentAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        var schemes = await _powerPlatform.EnumerateSchemesAsync(cancellationToken).ConfigureAwait(false);
        if (schemes.Any(item => item.Id == schemeId))
            throw new InvalidOperationException("目标电源计划 GUID 已存在，已阻止覆盖。");
    }

    private static void EnsureSuccess(ManagedPowerCommandResult result, string operation)
    {
        if (!result.Success)
            throw new InvalidOperationException($"{operation}失败（exit {result.ExitCode}）：{Sanitize(result.Error)}");
    }

    private static string Sanitize(string value)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }

    private static bool ValuesEqual(IReadOnlyList<ManagedPowerSettingValue> expected, IReadOnlyList<ManagedPowerSettingValue> actual) =>
        expected.Count == actual.Count && expected.Zip(actual).All(pair =>
            string.Equals(pair.First.SettingId, pair.Second.SettingId, StringComparison.OrdinalIgnoreCase)
            && pair.First.AcValue == pair.Second.AcValue && pair.First.DcValue == pair.Second.DcValue);

    private static void VerifyBackupFile(ManagedPowerPlanBackup backup)
    {
        if (!File.Exists(backup.FilePath))
            throw new FileNotFoundException("受管电源计划备份不存在。", backup.FilePath);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backup.FilePath)));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(backup.Sha256)))
            throw new InvalidDataException("受管电源计划备份校验失败，已阻止恢复。");
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}
