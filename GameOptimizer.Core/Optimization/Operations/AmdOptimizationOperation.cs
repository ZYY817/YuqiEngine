using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Services;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class AmdRadeonAntiLagOperation : FailClosedGraphicsOptimizationOperation
{
    public AmdRadeonAntiLagOperation() : base(
        "amd.radeon_anti_lag",
        "AMD Radeon Anti-Lag（安全阻断）",
        "旧实现修改驱动类 ULPS/DeepSleep 私有值，无法证明等同于官方 ADL Anti-Lag 能力。",
        GraphicsMutationKind.VendorPrivateSetting,
        "可能影响多 GPU、电源管理、温控与驱动升级兼容性。") { }
}

/// <summary>
/// AMD Precision Boost Overdrive 优化
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AmdPrecisionBoostOperation : IOptimizationOperation
{
    private static readonly Guid ProcessorSubgroup = new("54533251-82BE-4824-96C1-47B60B740D00");
    private static readonly Guid BoostModeSetting = new("be337238-0d82-4146-a960-4f3749d470c7");

    public string Id => "amd.precision_boost";
    public string Title => "AMD Precision Boost 优化";
    public string Description => "调整 AMD Precision Boost 参数以提升 CPU 性能，适用于 AMD Ryzen 处理器。";
    public string Category => "AMD 优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit => "提升 CPU 性能与游戏帧率稳定性";
    public string? Drawback => "可能增加功耗与发热";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.PowerPlan;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var scheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return new OperationStatus(false, "无法获取电源方案");
        }

        var (ac, dc) = await PowerCfgUtility.QuerySettingValueAsync(scheme.Value, ProcessorSubgroup, BoostModeSetting, ct).ConfigureAwait(false);
        var optimized = MatchesTarget(ac, dc, 2);
        return new OperationStatus(optimized, optimized ? "Precision Boost 已启用" : $"AC={FormatValue(ac)}/DC={FormatValue(dc)}");
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var result = await CreateDelegate().ApplyAsync(ctx, ct).ConfigureAwait(false);
        return result.Success && result.Changed
            ? new OperationInvokeResult(true, true, "已启用 Precision Boost")
            : result;
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var result = await CreateDelegate().RestoreAsync(ctx, ct).ConfigureAwait(false);
        return result.Success && result.Changed
            ? new OperationInvokeResult(true, true, "已恢复 Precision Boost")
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
            BoostModeSetting,
            2,
            2)
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

[SupportedOSPlatform("windows")]
public sealed class DisableAmdCoolNQuietOperation : IOptimizationOperation
{
    private static readonly Guid ProcessorSubgroup = new("54533251-82BE-4824-96C1-47B60B740D00");
    private static readonly Guid MinStateSetting = new("893dee8e-2bef-41e0-89c6-b55d0929964c");

    public string Id => "amd.disable_coolnquiet";
    public string Title => "禁用 Cool'n'Quiet";
    public string Description => "将处理器最小状态设为 100%，禁用 AMD Cool'n'Quiet 降频，使 CPU 保持满频运行。";
    public string Category => "AMD 优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit => "提升 CPU 性能与游戏帧率稳定性";
    public string? Drawback => "显著增加功耗与发热，可能缩短 CPU 寿命";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.PowerPlan;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var scheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return new OperationStatus(false, "无法获取电源方案");
        }

        var (ac, dc) = await PowerCfgUtility.QuerySettingValueAsync(scheme.Value, ProcessorSubgroup, MinStateSetting, ct).ConfigureAwait(false);
        var disabled = MatchesTarget(ac, dc, 100);
        return new OperationStatus(disabled, disabled ? "Cool'n'Quiet 已禁用" : $"AC={FormatValue(ac)}/DC={FormatValue(dc)}");
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var result = await CreateDelegate().ApplyAsync(ctx, ct).ConfigureAwait(false);
        return result.Success && result.Changed
            ? new OperationInvokeResult(true, true, "已禁用 Cool'n'Quiet")
            : result;
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var result = await CreateDelegate().RestoreAsync(ctx, ct).ConfigureAwait(false);
        return result.Success && result.Changed
            ? new OperationInvokeResult(true, true, "已恢复 Cool'n'Quiet")
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
            MinStateSetting,
            100,
            100)
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
