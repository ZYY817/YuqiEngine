using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// 禁用 Intel C-States - 锁定 CPU 最高频率
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisableIntelCStatesOperation : IOptimizationOperation
{
    private static readonly Guid ProcessorSubgroup = new("54533251-82BE-4824-96C1-47B60B740D00");
    private static readonly Guid IdleDisableSetting = new("5d76a2ca-e8c0-402f-a133-2158492d58ad");

    public string Id => "intel.disable_cstates";
    public string Title => "禁用 CPU C-States";
    public string Description => "禁用 CPU 空闲状态（C-States），让 CPU 始终保持最高频率，减少进入/退出低功耗状态的延迟";
    public string Category => "Intel 优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit => "减少 CPU 进入低功耗状态的延迟";
    public string? Drawback => "显著增加功耗与发热";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.PowerPlan;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var scheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return new OperationStatus(false, "无法读取");
        }

        var (ac, dc) = await PowerCfgUtility.QuerySettingValueAsync(scheme.Value, ProcessorSubgroup, IdleDisableSetting, ct).ConfigureAwait(false);
        var disabled = MatchesTarget(ac, dc, 1);
        return new OperationStatus(disabled, disabled ? "C-States 已禁用" : $"AC={FormatValue(ac)}/DC={FormatValue(dc)}");
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var result = await CreateDelegate().ApplyAsync(ctx, ct).ConfigureAwait(false);
        return result.Success && result.Changed
            ? new OperationInvokeResult(true, true, "已禁用 C-States")
            : result;
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var result = await CreateDelegate().RestoreAsync(ctx, ct).ConfigureAwait(false);
        return result.Success && result.Changed
            ? new OperationInvokeResult(true, true, "已恢复 C-States")
            : result;
    }

    private PowerSettingIndexOperation CreateDelegate()
    {
        return new PowerSettingIndexOperation(
            Id,
            Title,
            Description,
            Category,
            ProcessorSubgroup,
            IdleDisableSetting,
            1,
            1)
        {
            RiskLevel = RiskLevel,
            Benefit = Benefit,
            Drawback = Drawback
        };
    }

    private static bool MatchesTarget(int? ac, int? dc, int target)
    {
        if (ac is null && dc is null)
        {
            return false;
        }

        if (ac is null)
        {
            return dc == target;
        }

        if (dc is null)
        {
            return ac == target;
        }

        return ac == target && dc == target;
    }

    private static string FormatValue(int? value) => value?.ToString() ?? "?";
}
