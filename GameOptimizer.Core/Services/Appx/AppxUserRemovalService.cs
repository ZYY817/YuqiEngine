// File: GameOptimizer.Core/Services/Appx/AppxUserRemovalService.cs
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Appx;

public sealed record AppxUserRemovalOutcome(
    string FamilyName,
    string DisplayName,
    AppxRecoveryClass RecoveryClass,
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message)
{
    /// <summary>null=未尝试补偿；true=已自动恢复；false=自动恢复失败（含 StoreOnly 无恢复材料）。</summary>
    public bool? CompensationSucceeded { get; init; }

    public string? CompensationMessage { get; init; }
}

/// <summary>批量结果：Interrupted=因失败/取消未执行完全部条目；CompensatedAny=至少一项成功补偿。</summary>
public sealed record AppxUserRemovalBatchResult(
    bool Interrupted,
    bool CompensatedAny,
    IReadOnlyList<AppxUserRemovalOutcome> Outcomes);

/// <summary>
/// AppX 自选卸载页的用例服务。本功能不走固定目录，按用户勾选的包逐项组装
/// <see cref="AppxRemovalChangeCapability"/> 并驱动 Probe→Read→Prepare→Execute 链；
/// 安全语义（拒绝名单、漂移阻断、回读确认、恢复材料校验）全部复用能力层，不在此重复实现。
/// 某项失败时停止后续项，并对已完成移除的包按逆序执行 Restore 补偿，使系统回到批量操作前状态；
/// StoreOnly 类包无自动恢复材料，Restore 如实返回 manual-compensation-required，不伪造恢复成功。
/// 取消语义：取消后停止后续写入，并返回 Interrupted=true 与已经完成的逐项结果；
/// 已完成移除的项保留（与用户卸载意图一致），调用方据此报告并刷新真实状态。
/// </summary>
public interface IAppxUserRemovalService
{
    Task<AppxUserRemovalBatchResult> RemoveAsync(
        IReadOnlyList<AppxCatalogEntry> entries,
        Action<int, int, string>? progress,
        CancellationToken cancellationToken);
}

public sealed class AppxUserRemovalService : IAppxUserRemovalService
{
    private const string CapabilityId = "appx.remove_current_user";
    private const string DefinitionVersion = "1";
    private const string TargetPayloadRemove = "remove";
    private const string ResourceKeyPrefix = "appx:";

    private readonly IAppxRemovalOwner _owner;
    private readonly TimeProvider _timeProvider;

    public AppxUserRemovalService(IAppxRemovalOwner owner, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AppxUserRemovalBatchResult> RemoveAsync(
        IReadOnlyList<AppxCatalogEntry> entries,
        Action<int, int, string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            return new AppxUserRemovalBatchResult(false, false, []);

        var scope = TargetScope.Machine(Environment.MachineName);
        var outcomes = new List<AppxUserRemovalOutcome>();
        var completed = new List<(AppxUserRemovalOutcome Outcome, RecoveryMaterial Material)>();

        try
        {
            foreach (var (entry, index) in entries.Select((item, index) => (item, index)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(index + 1, entries.Count, entry.DisplayName);

                var (outcome, material) = await RemoveSingleAsync(entry, scope, cancellationToken).ConfigureAwait(false);
                if (outcome.Success && material is not null)
                    completed.Add((outcome, material));
                outcomes.Add(outcome);

                if (!outcome.Success)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AppxUserRemovalBatchResult(
                Interrupted: true,
                CompensatedAny: false,
                outcomes);
        }

        // 全部成功则不做任何补偿；仅当批量因失败中断时才回滚已完成项。
        if (outcomes.Count == entries.Count && outcomes.All(item => item.Success))
            return new AppxUserRemovalBatchResult(Interrupted: false, CompensatedAny: false, outcomes);

        cancellationToken.ThrowIfCancellationRequested();

        var compensations = await CompensateAsync(completed, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < outcomes.Count; i++)
        {
            if (compensations.TryGetValue(outcomes[i].FamilyName, out var compensation))
                outcomes[i] = outcomes[i] with
                {
                    CompensationSucceeded = compensation.Succeeded,
                    CompensationMessage = compensation.Message
                };
        }

        return new AppxUserRemovalBatchResult(
            Interrupted: true,
            CompensatedAny: compensations.Values.Any(item => item.Succeeded),
            outcomes);
    }

    private async Task<(AppxUserRemovalOutcome Outcome, RecoveryMaterial? Material)> RemoveSingleAsync(
        AppxCatalogEntry entry,
        TargetScope scope,
        CancellationToken cancellationToken)
    {
        var definition = CreateDefinition(entry.FamilyName, entry.DisplayName, entry.RecoveryClass);
        var capability = new AppxRemovalChangeCapability(definition, _owner, _timeProvider);

        try
        {
            var probe = await capability.ProbeAsync(scope, cancellationToken).ConfigureAwait(false);
            if (probe.Applicability != CapabilityApplicability.Applicable || !probe.HasRequiredPermission)
            {
                return (new AppxUserRemovalOutcome(
                    entry.FamilyName, entry.DisplayName, entry.RecoveryClass,
                    Success: false, MutationOccurred: false,
                    probe.ReasonCode, probe.Details), null);
            }

            var current = await capability.ReadAsync(scope, cancellationToken).ConfigureAwait(false);
            var change = await capability.PrepareAsync(current, TargetPayloadRemove, cancellationToken).ConfigureAwait(false);
            var executed = await capability.ExecuteAsync(change, cancellationToken).ConfigureAwait(false);

            var outcome = new AppxUserRemovalOutcome(
                entry.FamilyName, entry.DisplayName, entry.RecoveryClass,
                executed.Success, executed.MutationOccurred,
                executed.ReasonCode, executed.Message);

            // 补偿材料在 Execute 前固化：即使执行结果不确定（applied-but-unverified）也可尝试精确恢复。
            var material = new RecoveryMaterial(
                Guid.NewGuid(),
                Guid.NewGuid(),
                capability.Descriptor.CapabilityId,
                capability.Descriptor.DefinitionVersion,
                change.Scope,
                current.Payload,
                current.Fingerprint,
                change.TargetFingerprint,
                change.ResourceKey,
                _timeProvider.GetUtcNow(),
                "appx-cleanup-page");

            return (outcome, material);
        }
        catch (InvalidDataException ex)
        {
            return (new AppxUserRemovalOutcome(
                entry.FamilyName, entry.DisplayName, entry.RecoveryClass,
                Success: false, MutationOccurred: false,
                "prepare.rejected", ex.Message), null);
        }
    }

    /// <summary>
    /// 逆序补偿。返回每个已补偿包的结果；快照内固化的 RecoveryClass 决定自动重注册或
    /// manual-compensation-required（如实记录，不算失败伪装成功）。身份校验异常时该项记为补偿失败。
    /// </summary>
    private async Task<Dictionary<string, (bool Succeeded, string Message)>> CompensateAsync(
        List<(AppxUserRemovalOutcome Outcome, RecoveryMaterial Material)> completed,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, (bool Succeeded, string Message)>(StringComparer.OrdinalIgnoreCase);

        for (var i = completed.Count - 1; i >= 0; i--)
        {
            var (_, material) = completed[i];
            var familyName = ExtractFamilyName(material.ResourceKey);
            var capability = new AppxRemovalChangeCapability(CreateDefinition(familyName), _owner, _timeProvider);

            try
            {
                var restored = await capability.RestoreAsync(material, cancellationToken).ConfigureAwait(false);
                results[familyName] = (restored.Success, $"{restored.ReasonCode}: {restored.Message}");
            }
            catch (InvalidDataException ex)
            {
                results[familyName] = (false, $"restore.identity-rejected: {ex.Message}");
            }
        }

        return results;
    }

    private static AppxRemovalChangeCapabilityDefinition CreateDefinition(
        string familyName,
        string displayName = "",
        AppxRecoveryClass recoveryClass = AppxRecoveryClass.StagedRegister) =>
        new(
            CapabilityId,
            DefinitionVersion,
            new AppxRemovalEntry(familyName, displayName, ProductId: string.Empty, recoveryClass, Allowed: true));

    private static string ExtractFamilyName(string resourceKey) =>
        resourceKey.StartsWith(ResourceKeyPrefix, StringComparison.Ordinal)
            ? resourceKey[ResourceKeyPrefix.Length..]
            : resourceKey;
}
