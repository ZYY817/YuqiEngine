using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// 禁用 P-States 动态调频 - 锁定 CPU 最高频率
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisablePStatesOperation : IOptimizationOperation
{
    private sealed class Snapshot
    {
        public Guid? Scheme { get; init; }
        public int? OriginalMinAc { get; init; }
        public int? OriginalMinDc { get; init; }
        public int? OriginalMaxAc { get; init; }
        public int? OriginalMaxDc { get; init; }
    }

    private static readonly Guid ProcessorSubgroup = new("54533251-82BE-4824-96C1-47B60B740D00");
    private static readonly Guid MinStateSetting = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    private static readonly Guid MaxStateSetting = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private static readonly PowerSettingCapabilityGuard CapabilityGuard = new();

    public string Id => "cpu.disable_pstates";
    public string Title => "锁定 CPU 最高频率";
    public string Description => "将处理器最小/最大状态锁定为 100%，禁用 P-States 动态调频，让 CPU 始终运行在最高频率";
    public string Category => "CPU 高级优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit => "CPU 始终运行在最高频率，减少动态调频带来的延迟";
    public string? Drawback => "显著增加功耗与发热，可能缩短 CPU 寿命";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.PowerPlan;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var state = await ReadStateAsync(ct).ConfigureAwait(false);
        if (state.Scheme is null)
        {
            return new OperationStatus(false, "无法读取");
        }

        var expected = BuildTargetSnapshot(state.Scheme.Value);
        var locked = MatchesSnapshot(state, expected);
        return new OperationStatus(
            locked,
            locked
                ? "CPU 已锁定 100%"
                : $"Min AC/DC={FormatValue(state.OriginalMinAc)}/{FormatValue(state.OriginalMinDc)} ? Max AC/DC={FormatValue(state.OriginalMaxAc)}/{FormatValue(state.OriginalMaxDc)}");
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var scheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return new OperationInvokeResult(false, false, "无法读取");
        }

        var minGuard = await CapabilityGuard.EvaluateAsync(scheme.Value, ProcessorSubgroup, MinStateSetting, 100, 100, Id, Title, ct).ConfigureAwait(false);
        if (!minGuard.IsAllowed)
        {
            return new OperationInvokeResult(false, false, minGuard.Reason);
        }

        var maxGuard = await CapabilityGuard.EvaluateAsync(scheme.Value, ProcessorSubgroup, MaxStateSetting, 100, 100, Id, Title, ct).ConfigureAwait(false);
        if (!maxGuard.IsAllowed)
        {
            return new OperationInvokeResult(false, false, maxGuard.Reason);
        }

        var snapshot = await ReadStateAsync(ct).ConfigureAwait(false);
        if (snapshot.Scheme is null)
        {
            return new OperationInvokeResult(false, false, "无法读取");
        }

        if (!HasRecoverablePair(snapshot.OriginalMinAc, snapshot.OriginalMinDc) || !HasRecoverablePair(snapshot.OriginalMaxAc, snapshot.OriginalMaxDc))
        {
            return new OperationInvokeResult(false, false, "无法读取最小/最大状态的 AC/DC 原始值，无法安全写入或恢复");
        }

        var target = BuildTargetSnapshot(snapshot.Scheme.Value);
        if (MatchesSnapshot(snapshot, target))
        {
            return new OperationInvokeResult(true, false, "CPU 已锁定 100%");
        }

        await ctx.SaveSnapshotAsync(Id, snapshot, ct).ConfigureAwait(false);

        var applyResult = await ApplySnapshotAsync(snapshot.Scheme.Value, target, ct).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            return await BuildRollbackFailureResultAsync(ctx, snapshot, applyResult.Message, ct).ConfigureAwait(false);
        }

        var verifyResult = await VerifySnapshotAsync(snapshot.Scheme.Value, target, ct).ConfigureAwait(false);
        if (!verifyResult.Success)
        {
            return await BuildRollbackFailureResultAsync(ctx, snapshot, verifyResult.Message, ct).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, "已锁定 CPU 100% 频率（切换电源计划后可能失效）");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var snap = await ctx.LoadSnapshotAsync<Snapshot>(Id, ct).ConfigureAwait(false);
        if (snap is null)
        {
            return new OperationInvokeResult(false, false, "没有可恢复的 CPU 频率快照，无法恢复原始值");
        }

        if (!HasRecoverablePair(snap.OriginalMinAc, snap.OriginalMinDc) || !HasRecoverablePair(snap.OriginalMaxAc, snap.OriginalMaxDc))
        {
            return new OperationInvokeResult(false, false, "CPU 频率快照缺少可恢复的 AC/DC 原始值");
        }

        var scheme = snap.Scheme ?? await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return new OperationInvokeResult(false, false, "无法读取");
        }

        var restoreResult = await ApplySnapshotAsync(scheme.Value, snap, ct).ConfigureAwait(false);
        if (!restoreResult.Success)
        {
            return new OperationInvokeResult(false, false, restoreResult.Message);
        }

        var verifyResult = await VerifySnapshotAsync(scheme.Value, snap, ct).ConfigureAwait(false);
        if (!verifyResult.Success)
        {
            return new OperationInvokeResult(false, false, verifyResult.Message);
        }

        return new OperationInvokeResult(true, true, "已恢复");
    }

    private async Task<OperationInvokeResult> BuildRollbackFailureResultAsync(
        OperationExecutionContext ctx,
        Snapshot snapshot,
        string failureMessage,
        CancellationToken ct)
    {
        var rollbackResult = await TryRestoreOriginalStateAsync(ctx, snapshot, ct).ConfigureAwait(false);
        var message = rollbackResult.Success
            ? failureMessage + "\nOriginal CPU power settings remained intact or were restored."
            : failureMessage + "\nAutomatic rollback failed: " + rollbackResult.Message;
        return new OperationInvokeResult(false, false, message);
    }

    private async Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext ctx,
        Snapshot snapshot,
        CancellationToken ct)
    {
        var scheme = snapshot.Scheme ?? await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return (false, "Unable to resolve the original CPU power scheme for rollback.");
        }

        var originalState = await VerifySnapshotAsync(scheme.Value, snapshot, ct).ConfigureAwait(false);
        if (originalState.Success)
        {
            await TryDeleteSnapshotAsync(ctx, ct).ConfigureAwait(false);
            return (true, "Original CPU power settings remained intact.");
        }

        var rollbackResult = await RestoreSnapshotInternalAsync(snapshot, ct).ConfigureAwait(false);
        if (!rollbackResult.Success)
        {
            return rollbackResult;
        }

        await TryDeleteSnapshotAsync(ctx, ct).ConfigureAwait(false);
        return (true, "Original CPU power settings restored.");
    }

    private async Task<(bool Success, string Message)> RestoreSnapshotInternalAsync(Snapshot snapshot, CancellationToken ct)
    {
        var scheme = snapshot.Scheme ?? await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return (false, "无法读取");
        }

        var restoreResult = await ApplySnapshotAsync(scheme.Value, snapshot, ct).ConfigureAwait(false);
        if (!restoreResult.Success)
        {
            return restoreResult;
        }

        return await VerifySnapshotAsync(scheme.Value, snapshot, ct).ConfigureAwait(false);
    }

    private static Snapshot BuildTargetSnapshot(Guid scheme)
    {
        return new Snapshot
        {
            Scheme = scheme,
            OriginalMinAc = 100,
            OriginalMinDc = 100,
            OriginalMaxAc = 100,
            OriginalMaxDc = 100
        };
    }

    private async Task<(bool Success, string Message)> ApplySnapshotAsync(Guid scheme, Snapshot snapshot, CancellationToken ct)
    {
        var minResult = await ApplySettingAsync(scheme, MinStateSetting, snapshot.OriginalMinAc, snapshot.OriginalMinDc, ct).ConfigureAwait(false);
        if (!minResult.Success)
        {
            return minResult;
        }

        var maxResult = await ApplySettingAsync(scheme, MaxStateSetting, snapshot.OriginalMaxAc, snapshot.OriginalMaxDc, ct).ConfigureAwait(false);
        if (!maxResult.Success)
        {
            return maxResult;
        }

        var activateResult = await PowerCfgUtility.RunAsync($"/S {scheme:D}", ct).ConfigureAwait(false);
        if (activateResult.ExitCode != 0)
        {
            return (false, BuildCommandFailureMessage("激活电源计划失败", activateResult.StdErr, activateResult.StdOut));
        }

        return (true, string.Empty);
    }

    private static async Task<(bool Success, string Message)> ApplySettingAsync(Guid scheme, Guid setting, int? ac, int? dc, CancellationToken ct)
    {
        if (ac is null && dc is null)
        {
            return (false, $"设置 {setting:D} 缺少可写入的 AC/DC 值");
        }

        if (ac is not null)
        {
            var acResult = await PowerCfgUtility.RunAsync($"/SETACVALUEINDEX {scheme:D} {ProcessorSubgroup:D} {setting:D} {ac.Value}", ct).ConfigureAwait(false);
            if (acResult.ExitCode != 0)
            {
                return (false, BuildCommandFailureMessage("写入 AC 失败", acResult.StdErr, acResult.StdOut));
            }
        }

        if (dc is not null)
        {
            var dcResult = await PowerCfgUtility.RunAsync($"/SETDCVALUEINDEX {scheme:D} {ProcessorSubgroup:D} {setting:D} {dc.Value}", ct).ConfigureAwait(false);
            if (dcResult.ExitCode != 0)
            {
                return (false, BuildCommandFailureMessage("写入 DC 失败", dcResult.StdErr, dcResult.StdOut));
            }
        }

        return (true, string.Empty);
    }

    private static async Task<(bool Success, string Message)> VerifySnapshotAsync(Guid scheme, Snapshot expected, CancellationToken ct)
    {
        var activeScheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (activeScheme is null)
        {
            return (false, "无法读取活动电源计划以验证");
        }

        if (activeScheme.Value != scheme)
        {
            return (false, $"活动电源计划与预期不符 {activeScheme.Value:D}，预期 {scheme:D}");
        }

        var actual = await ReadStateAsync(ct).ConfigureAwait(false);
        if (actual.Scheme == scheme && MatchesSnapshot(actual, expected))
        {
            return (true, string.Empty);
        }

        return (false,
            $"验证失败：Min AC/DC={FormatValue(actual.OriginalMinAc)}/{FormatValue(actual.OriginalMinDc)}，Max AC/DC={FormatValue(actual.OriginalMaxAc)}/{FormatValue(actual.OriginalMaxDc)}；期望 Min={FormatValue(expected.OriginalMinAc)}/{FormatValue(expected.OriginalMinDc)}，Max={FormatValue(expected.OriginalMaxAc)}/{FormatValue(expected.OriginalMaxDc)}");
    }

    private static bool MatchesSnapshot(Snapshot actual, Snapshot expected)
    {
        return MatchesExpected(actual.OriginalMinAc, actual.OriginalMinDc, expected.OriginalMinAc, expected.OriginalMinDc)
            && MatchesExpected(actual.OriginalMaxAc, actual.OriginalMaxDc, expected.OriginalMaxAc, expected.OriginalMaxDc);
    }

    private static bool MatchesExpected(int? actualAc, int? actualDc, int? expectedAc, int? expectedDc)
    {
        if (expectedAc is null && expectedDc is null)
        {
            return false;
        }

        if (actualAc is null && actualDc is null)
        {
            return false;
        }

        if (expectedAc is null)
        {
            return actualDc == expectedDc;
        }

        if (expectedDc is null)
        {
            return actualAc == expectedAc;
        }

        if (actualAc is null)
        {
            return actualDc == expectedDc;
        }

        if (actualDc is null)
        {
            return actualAc == expectedAc;
        }

        return actualAc == expectedAc && actualDc == expectedDc;
    }

    private static bool HasRecoverablePair(int? ac, int? dc) => ac is not null || dc is not null;

    private static async Task<Snapshot> ReadStateAsync(CancellationToken ct)
    {
        var scheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return new Snapshot();
        }

        var (minAc, minDc) = await PowerCfgUtility.QuerySettingValueAsync(scheme.Value, ProcessorSubgroup, MinStateSetting, ct).ConfigureAwait(false);
        var (maxAc, maxDc) = await PowerCfgUtility.QuerySettingValueAsync(scheme.Value, ProcessorSubgroup, MaxStateSetting, ct).ConfigureAwait(false);
        return new Snapshot
        {
            Scheme = scheme,
            OriginalMinAc = minAc,
            OriginalMinDc = minDc,
            OriginalMaxAc = maxAc,
            OriginalMaxDc = maxDc
        };
    }

    private static string BuildCommandFailureMessage(string prefix, string stderr, string stdout)
    {
        var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        details = string.IsNullOrWhiteSpace(details) ? "powercfg 无输出" : details.Trim();
        return prefix + details;
    }

    private static string FormatValue(int? value) => value?.ToString() ?? "?";

    private Task TryDeleteSnapshotAsync(OperationExecutionContext context, CancellationToken ct)
    {
        return context.TryDeleteSnapshotAsync(Id, ct);
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableTscSyncOperation : IOptimizationOperation
{
    public string Id => "cpu.disable_tsc_sync";
    public string Title => "禁用 TSC 同步";
    public string Description => "通过 BCD 禁用 TSC 同步 (tscsyncpolicy=legacy)，减少多核时钟同步开销，可能提升游戏性能";
    public string Category => "CPU 高级优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit => "减少多核时钟同步开销，可能提升游戏性能";
    public string? Drawback => "可能导致计时不准，影响系统稳定性";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public bool RequiresExplicitConfirmation => true;
    public bool RequiresRestorePoint => true;
    public string? ImpactWarning => "⚠️ BCD修改可能导致系统无法启动！请务必先创建系统还原点。";
    public OperationType OperationType => OperationType.Command;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var result = await CreateDelegate().GetStatusAsync(ct).ConfigureAwait(false);
        if (!result.IsCompliant)
        {
            return new OperationStatus(false, result.Details);
        }

        return new OperationStatus(true, "TSC sync disabled");
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var result = await CreateDelegate().ApplyAsync(ctx, ct).ConfigureAwait(false);
        if (result.Success && result.Changed)
        {
            return new OperationInvokeResult(true, true, "已禁用 TSC 同步（需重启生效）");
        }

        return result;
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var result = await CreateDelegate().RestoreAsync(ctx, ct).ConfigureAwait(false);
        if (result.Success && result.Changed)
        {
            return new OperationInvokeResult(true, true, "已恢复 TSC 同步设置（需重启生效）");
        }

        return result;
    }

    private BcdSettingOperation CreateDelegate()
    {
        return new BcdSettingOperation(
            Id,
            Title,
            Description,
            Category,
            "tscsyncpolicy",
            "Legacy",
            "Legacy")
        {
            RiskLevel = RiskLevel,
            Benefit = Benefit,
            Drawback = Drawback
        };
    }
}

[SupportedOSPlatform("windows")]
public sealed class AdjustCsrssPriorityOperation : IOptimizationOperation
{
    private record Snapshot(int? CsrssBasePriority);
    private const string PriorityPath = RegistryPaths.PriorityControl;

    public string Id => "cpu.adjust_csrss_priority";
    public string Title => "调整 CSRSS 优先级";
    public string Description => "降低 CSRSS 系统进程的基础优先级，为游戏进程让出更多 CPU 时间。⚠️ 可能影响系统响应。";
    public string Category => "CPU 高级优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit => "游戏进程可能获得更多 CPU 时间";
    public string? Drawback => "可能影响系统响应速度和稳定性";
    public bool RequiresAdmin => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        // 【P0-002 修复】：使用 SafeRegistryHelper 安全读取注册表
        var (val, error) = SafeRegistryHelper.GetValue<int>(Registry.LocalMachine, PriorityPath, "Win32PrioritySeparation", 0x26);
        if (error != null)
        {
            return Task.FromResult(new OperationStatus(false, $"读取失败: {error}"));
        }
        
        var optimized = val == 0x26;
        return Task.FromResult(new OperationStatus(optimized, $"Win32PrioritySeparation = 0x{val:X}"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var (old, oldError) = SafeRegistryHelper.GetValue<int>(Registry.LocalMachine, PriorityPath, "Win32PrioritySeparation");
        
        var (success, error) = SafeRegistryHelper.SetValue(
            Registry.LocalMachine, 
            PriorityPath, 
            "Win32PrioritySeparation", 
            0x26, 
            RegistryValueKind.DWord);
        
        if (!success)
        {
            return new OperationInvokeResult(false, false, error ?? "Failed to set value");
        }
        
        // NMR5-07: 写后存 — 注册表写入成功后才保存快照
        await ctx.SaveSnapshotAsync(Id, new Snapshot(old), ct);
        return new OperationInvokeResult(true, true, "已优化进程优先级分配");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        var snap = await ctx.LoadSnapshotAsync<Snapshot>(Id, ct);
        
        // 【P0-002 修复】：使用 SafeRegistryHelper 安全恢复
        if (snap?.CsrssBasePriority.HasValue == true)
        {
            var (success, error) = SafeRegistryHelper.SetValue(
                Registry.LocalMachine, 
                PriorityPath, 
                "Win32PrioritySeparation", 
                snap.CsrssBasePriority.Value, 
                RegistryValueKind.DWord);
            
            if (!success)
            {
                return new OperationInvokeResult(false, false, error ?? "Restore failed");
            }
        }
        else
        {
            // FIX-DWM-SAFE: 无快照时写入已知安全默认值，严禁删除 Win32PrioritySeparation
            // Why: 此值控制前台/后台进程优先级分配，删除会导致系统调度异常
            var (success, error) = SafeRegistryHelper.SetValue(
                Registry.LocalMachine, 
                PriorityPath, 
                "Win32PrioritySeparation", 
                2, // Windows 默认=2（短量程、可变、前台优先）
                RegistryValueKind.DWord);
            
            if (!success)
            {
                return new OperationInvokeResult(false, false, error ?? "Restore failed");
            }
        }
        
        return new OperationInvokeResult(true, true, "已恢复默认优先级");
    }
}



