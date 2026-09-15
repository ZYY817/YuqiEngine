using System;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Forces core parking min/max processor states to 100% on both AC/DC to disable parking entirely.
/// </summary>
public sealed class CoreParkingDisableOperation : IOptimizationOperation
{
    private static readonly Guid SubGroup = new("54533251-82BE-4824-96C1-47B60B740D00");
    private static readonly Guid SettingMin = new("0CC5B647-C1DF-4637-891A-DEC35C318583");
    private static readonly Guid SettingMax = new("EA062031-0E34-4FF1-9B6D-EB1059334028");
    private static readonly PowerSettingCapabilityGuard CapabilityGuard = new();

    private sealed class Snapshot
    {
        public Guid? Scheme { get; init; }
        public int? MinAc { get; init; }
        public int? MinDc { get; init; }
        public int? MaxAc { get; init; }
        public int? MaxDc { get; init; }
    }

    public CoreParkingDisableOperation(string id, string title, string description, string category)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.PowerPlan;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var state = await ReadSnapshotValuesAsync(cancellationToken).ConfigureAwait(false);
        if (state.Scheme is null)
        {
            return new OperationStatus(false, "无法读取电源计划（可能需要管理员权限或系统电源服务未运行）");
        }

        var expected = BuildTargetSnapshot(state.Scheme.Value);
        var compliant = MatchesExpected(state.MinAc, state.MinDc, expected.MinAc, expected.MinDc)
            && MatchesExpected(state.MaxAc, state.MaxDc, expected.MaxAc, expected.MaxDc);
        var details = $"Min AC/DC={FormatValue(state.MinAc)}/{FormatValue(state.MinDc)} · Max AC/DC={FormatValue(state.MaxAc)}/{FormatValue(state.MaxDc)}";
        return new OperationStatus(compliant, details);
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var scheme = await ResolveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (scheme is null)
        {
            return new OperationInvokeResult(false, false, BuildSchemeErrorMessage(
                await Services.PowerServiceDiagnostics.DiagnoseAsync(cancellationToken).ConfigureAwait(false)));
        }

        var minGuard = await CapabilityGuard.EvaluateAsync(scheme.Value, SubGroup, SettingMin, 100, 100, Id, Title, cancellationToken).ConfigureAwait(false);
        if (!minGuard.IsAllowed)
        {
            return new OperationInvokeResult(false, false, minGuard.Reason);
        }

        var maxGuard = await CapabilityGuard.EvaluateAsync(scheme.Value, SubGroup, SettingMax, 100, 100, Id, Title, cancellationToken).ConfigureAwait(false);
        if (!maxGuard.IsAllowed)
        {
            return new OperationInvokeResult(false, false, maxGuard.Reason);
        }

        var snapshot = await ReadSnapshotValuesAsync(scheme.Value, cancellationToken).ConfigureAwait(false);
        if (!HasRecoverablePair(snapshot.MinAc, snapshot.MinDc) || !HasRecoverablePair(snapshot.MaxAc, snapshot.MaxDc))
        {
            return new OperationInvokeResult(false, false, "无法读取 Core Parking 的 AC/DC 原始值，无法安全写入或恢复");
        }

        var target = BuildTargetSnapshot(scheme.Value);
        if (MatchesSnapshot(snapshot, target))
        {
            return new OperationInvokeResult(true, false, "Core Parking 已锁定为 100%");
        }

        await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

        var applyResult = await ApplySnapshotAsync(scheme.Value, target, cancellationToken).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            return await BuildRollbackFailureResultAsync(context, snapshot, applyResult.Message, cancellationToken).ConfigureAwait(false);
        }

        var verifyResult = await VerifySnapshotAsync(scheme.Value, target, cancellationToken).ConfigureAwait(false);
        if (!verifyResult.Success)
        {
            return await BuildRollbackFailureResultAsync(context, snapshot, verifyResult.Message, cancellationToken).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, "已将 Core Parking 锁定为 100%（切换电源计划后可能失效）");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<Snapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new OperationInvokeResult(false, false, "没有可恢复的 Core Parking 快照，无法恢复原始值");
        }

        if (!HasRecoverablePair(snapshot.MinAc, snapshot.MinDc) || !HasRecoverablePair(snapshot.MaxAc, snapshot.MaxDc))
        {
            return new OperationInvokeResult(false, false, "Core Parking 快照缺少可恢复的 AC/DC 原始值，无法安全恢复");
        }

        var scheme = snapshot.Scheme ?? await ResolveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (scheme is null)
        {
            return new OperationInvokeResult(false, false, "无法获取电源计划，恢复失败");
        }

        var restoreResult = await ApplySnapshotAsync(scheme.Value, snapshot, cancellationToken).ConfigureAwait(false);
        if (!restoreResult.Success)
        {
            return new OperationInvokeResult(false, false, restoreResult.Message);
        }

        var verifyResult = await VerifySnapshotAsync(scheme.Value, snapshot, cancellationToken).ConfigureAwait(false);
        if (!verifyResult.Success)
        {
            return new OperationInvokeResult(false, false, verifyResult.Message);
        }

        return new OperationInvokeResult(true, true, "已恢复 Core Parking 原始参数");
    }

    private async Task<OperationInvokeResult> BuildRollbackFailureResultAsync(
        OperationExecutionContext context,
        Snapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollbackResult = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        var message = rollbackResult.Success
            ? failureMessage + "\nOriginal Core Parking settings remained intact or were restored."
            : failureMessage + "\nAutomatic rollback failed: " + rollbackResult.Message;
        return new OperationInvokeResult(false, false, message);
    }

    private async Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        Snapshot snapshot,
        CancellationToken cancellationToken)
    {
        var scheme = snapshot.Scheme ?? await ResolveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (scheme is null)
        {
            return (false, "Unable to resolve the original power scheme for rollback.");
        }

        var originalState = await VerifySnapshotAsync(scheme.Value, snapshot, cancellationToken).ConfigureAwait(false);
        if (originalState.Success)
        {
            await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
            return (true, "Original Core Parking settings remained intact.");
        }

        var rollbackResult = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!rollbackResult.Success)
        {
            return rollbackResult;
        }

        await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
        return (true, "Original Core Parking settings restored.");
    }

    private async Task<(bool Success, string Message)> RestoreSnapshotInternalAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        var scheme = snapshot.Scheme ?? await ResolveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (scheme is null)
        {
            return (false, "无法解析原始电源计划以执行回滚");
        }

        var restoreResult = await ApplySnapshotAsync(scheme.Value, snapshot, cancellationToken).ConfigureAwait(false);
        if (!restoreResult.Success)
        {
            return restoreResult;
        }

        return await VerifySnapshotAsync(scheme.Value, snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static Snapshot BuildTargetSnapshot(Guid scheme)
    {
        return new Snapshot
        {
            Scheme = scheme,
            MinAc = 100,
            MinDc = 100,
            MaxAc = 100,
            MaxDc = 100
        };
    }

    private static bool MatchesSnapshot(Snapshot actual, Snapshot expected)
    {
        return MatchesExpected(actual.MinAc, actual.MinDc, expected.MinAc, expected.MinDc)
            && MatchesExpected(actual.MaxAc, actual.MaxDc, expected.MaxAc, expected.MaxDc);
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

    private static string FormatValue(int? value) => value?.ToString() ?? "?";

    private async Task<(bool Success, string Message)> ApplySnapshotAsync(Guid scheme, Snapshot snapshot, CancellationToken cancellationToken)
    {
        var minResult = await ApplySettingAsync(scheme, SettingMin, snapshot.MinAc, snapshot.MinDc, cancellationToken).ConfigureAwait(false);
        if (!minResult.Success)
        {
            return minResult;
        }

        var maxResult = await ApplySettingAsync(scheme, SettingMax, snapshot.MaxAc, snapshot.MaxDc, cancellationToken).ConfigureAwait(false);
        if (!maxResult.Success)
        {
            return maxResult;
        }

        var activateResult = await PowerCfgUtility.RunAsync($"/S {FormatGuid(scheme)}", cancellationToken).ConfigureAwait(false);
        if (activateResult.ExitCode != 0)
        {
            return (false, BuildCommandFailureMessage("激活电源计划失败", activateResult.StdErr, activateResult.StdOut));
        }

        return (true, string.Empty);
    }

    private async Task<(bool Success, string Message)> VerifySnapshotAsync(Guid expectedScheme, Snapshot expected, CancellationToken cancellationToken)
    {
        var activeScheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
        if (activeScheme is null)
        {
            return (false, "无法读取活动电源计划以验证");
        }

        if (activeScheme.Value != expectedScheme)
        {
            return (false, $"活动电源计划与预期不符 {activeScheme.Value:D}，预期 {expectedScheme:D}");
        }

        var actual = await ReadSnapshotValuesAsync(expectedScheme, cancellationToken).ConfigureAwait(false);
        if (MatchesSnapshot(actual, expected))
        {
            return (true, string.Empty);
        }

        return (false,
            $"Core Parking 验证失败：Min AC/DC={FormatValue(actual.MinAc)}/{FormatValue(actual.MinDc)}，Max AC/DC={FormatValue(actual.MaxAc)}/{FormatValue(actual.MaxDc)}；期望 Min={FormatValue(expected.MinAc)}/{FormatValue(expected.MinDc)}，Max={FormatValue(expected.MaxAc)}/{FormatValue(expected.MaxDc)}");
    }

    private static async Task<(bool Success, string Message)> ApplySettingAsync(
        Guid scheme,
        Guid setting,
        int? ac,
        int? dc,
        CancellationToken cancellationToken)
    {
        if (ac is null && dc is null)
        {
            return (false, $"设置 {setting:D} 缺少可写入的 AC/DC 值");
        }

        var schemeText = FormatGuid(scheme);
        var subgroupText = FormatGuid(SubGroup);
        var settingText = FormatGuid(setting);

        if (ac is not null)
        {
            var acResult = await PowerCfgUtility.RunAsync($"/SETACVALUEINDEX {schemeText} {subgroupText} {settingText} {ac.Value}", cancellationToken).ConfigureAwait(false);
            if (acResult.ExitCode != 0)
            {
                return (false, BuildCommandFailureMessage("写入 AC 失败", acResult.StdErr, acResult.StdOut));
            }
        }

        if (dc is not null)
        {
            var dcResult = await PowerCfgUtility.RunAsync($"/SETDCVALUEINDEX {schemeText} {subgroupText} {settingText} {dc.Value}", cancellationToken).ConfigureAwait(false);
            if (dcResult.ExitCode != 0)
            {
                return (false, BuildCommandFailureMessage("写入 DC 失败", dcResult.StdErr, dcResult.StdOut));
            }
        }

        return (true, string.Empty);
    }

    private static async Task<Snapshot> ReadSnapshotValuesAsync(CancellationToken cancellationToken)
    {
        var scheme = await ResolveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (scheme is null)
        {
            return new Snapshot();
        }

        return await ReadSnapshotValuesAsync(scheme.Value, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Snapshot> ReadSnapshotValuesAsync(Guid scheme, CancellationToken cancellationToken)
    {
        using var queryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        queryCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var (minAc, minDc) = await PowerCfgUtility.QuerySettingValueAsync(scheme, SubGroup, SettingMin, queryCts.Token).ConfigureAwait(false);
            var (maxAc, maxDc) = await PowerCfgUtility.QuerySettingValueAsync(scheme, SubGroup, SettingMax, queryCts.Token).ConfigureAwait(false);
            return new Snapshot { Scheme = scheme, MinAc = minAc, MinDc = minDc, MaxAc = maxAc, MaxDc = maxDc };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[CoreParkingDisable] Snapshot query timed out, using null defaults");
            return new Snapshot { Scheme = scheme };
        }
    }

    private static async Task<Guid?> ResolveSchemeAsync(CancellationToken cancellationToken)
    {
        var scheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
        if (scheme.HasValue)
        {
            return scheme;
        }

        var (schemeFromDiag, _) = await PowerCfgUtility.GetActiveSchemeWithDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        if (schemeFromDiag.HasValue)
        {
            return schemeFromDiag;
        }

        var (repaired, _) = await Services.PowerServiceDiagnostics.TryAutoRepairAsync(cancellationToken).ConfigureAwait(false);
        if (!repaired)
        {
            return null;
        }

        return await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildSchemeErrorMessage(Services.PowerServiceStatus diagnostics)
    {
        var errorMsg = "无法获取或创建电源计划。";
        if (!diagnostics.IsAdministrator)
        {
            errorMsg += "\n• " + Services.ElevationGate.DescribeWriteDenial("修改电源设置");
        }

        if (!diagnostics.IsPowerServiceRunning)
        {
            errorMsg += "\n• Power 服务未运行";
        }

        if (diagnostics.AvailablePlans.Count == 0)
        {
            errorMsg += "\n• 系统中没有可用的电源计划";
        }

        errorMsg += "\n\n建议操作：";
        if (!diagnostics.IsAdministrator)
        {
            errorMsg += "\n1. 以管理员身份重新运行本程序";
            errorMsg += "\n2. 检查 Power 服务是否正常运行 (services.msc)";
            errorMsg += "\n3. 运行 'powercfg /list' 检查电源计划";
        }
        else
        {
            errorMsg += "\n1. 检查 Power 服务是否正常运行 (services.msc)";
            errorMsg += "\n2. 运行 'powercfg /list' 检查电源计划";
            errorMsg += "\n3. 查看事件查看器中的电源相关错误";
        }
        return errorMsg;
    }

    private static string BuildCommandFailureMessage(string prefix, string stderr, string stdout)
    {
        var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        details = string.IsNullOrWhiteSpace(details) ? "powercfg 无输出" : details.Trim();
        return prefix + details;
    }

    private static string FormatGuid(Guid value) => value.ToString("D").ToUpperInvariant();

    private Task TryDeleteSnapshotAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        return context.TryDeleteSnapshotAsync(Id, cancellationToken);
    }
}
