using System;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Services;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class PowerSettingIndexOperation : IOptimizationOperation
{
    private readonly Guid _subGroup;
    private readonly Guid _setting;
    private readonly int _targetAc;
    private readonly int _targetDc;

    private record SettingSnapshot(Guid? Scheme, int? Ac, int? Dc);
    private static readonly PowerSettingCapabilityGuard CapabilityGuard = new();

    public PowerSettingIndexOperation(
        string id,
        string title,
        string description,
        string category,
        Guid subGroup,
        Guid setting,
        int targetValueAc,
        int targetValueDc)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _subGroup = subGroup;
        _setting = setting;
        _targetAc = targetValueAc;
        _targetDc = targetValueDc;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public OperationType OperationType => OperationType.PowerPlan;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var state = await ReadCurrentValuesAsync(cancellationToken).ConfigureAwait(false);
        if (state.Scheme is null)
        {
            return new OperationStatus(false, "无法读取电源计划（可能需要管理员权限或系统电源服务未运行）。");
        }

        var compliant = IsExpectedState(_targetAc, _targetDc, state.Ac, state.Dc);
        var details = $"AC={state.Ac?.ToString() ?? "?"} (目标:{_targetAc}) / DC={state.Dc?.ToString() ?? "?"} (目标:{_targetDc})";
        return new OperationStatus(compliant, details);
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var state = await ReadCurrentValuesAsync(cancellationToken).ConfigureAwait(false);
            if (state.Scheme is null)
            {
                var fallbackScheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
                if (fallbackScheme is null)
                {
                    var powerServiceRunning = await CheckPowerServiceAsync(cancellationToken).ConfigureAwait(false);
                    if (!powerServiceRunning)
                    {
                        return new OperationInvokeResult(false, false,
                            "电源服务(Power)未运行。解决方案：\n1. 打开服务管理器(services.msc)\n2. 找到\"Power\"服务\n3. 启动该服务并设置为自动启动");
                    }

                    if (!SystemGuard.IsAdministrator)
                    {
                        return new OperationInvokeResult(false, false,
                            Services.ElevationGate.DescribeWriteDenial("访问电源计划"));
                    }

                    return new OperationInvokeResult(false, false,
                        "无法获取或激活电源计划。可能原因：\n1. 电源服务异常\n2. 电源计划数据损坏\n3. 系统策略限制\n\n建议：\n1. 运行 powercfg /restoredefaultschemes 恢复默认电源计划\n2. 检查事件查看器中的电源相关错误");
                }

                state = (fallbackScheme, state.Ac, state.Dc);
            }

            if (state.Ac is null && state.Dc is null)
            {
                return new OperationInvokeResult(false, false, "无法准确读取当前 AC/DC 电源参数，已阻止写入以避免不可恢复的电源污染。");
            }

            var guardResult = await CapabilityGuard.EvaluateAsync(
                state.Scheme.Value,
                _subGroup,
                _setting,
                _targetAc,
                _targetDc,
                Id,
                Title,
                cancellationToken).ConfigureAwait(false);
            if (!guardResult.IsAllowed)
            {
                return new OperationInvokeResult(false, false, guardResult.Reason);
            }

            var snapshot = new SettingSnapshot(state.Scheme, state.Ac, state.Dc);
            if (IsExpectedState(_targetAc, _targetDc, state.Ac, state.Dc))
            {
                return new OperationInvokeResult(true, false, "参数已符合目标值。");
            }

            await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

            var applyResult = await ApplySettingValuesAsync(state.Scheme.Value, _targetAc, _targetDc, cancellationToken).ConfigureAwait(false);
            if (!applyResult.Success)
            {
                return await BuildApplyFailureResultAsync(context, snapshot, applyResult.Message, cancellationToken).ConfigureAwait(false);
            }

            var verifyResult = await VerifyPowerSettingAsync(state.Scheme.Value, _targetAc, _targetDc, cancellationToken).ConfigureAwait(false);
            if (verifyResult.Success)
            {
                return new OperationInvokeResult(true, true, $"AC/DC 已设置为 {_targetAc}/{_targetDc}");
            }

            return await BuildApplyFailureResultAsync(context, snapshot, verifyResult.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var diagnosticContext = new DiagnosticContext
            {
                OperationType = "Power settings",
                TargetResource = Title,
                IsAdminMode = SystemGuard.IsAdministrator
            };

            var result = Utilities.OperationErrorAnalyzer.DiagnoseWithDetails(ex, diagnosticContext.OperationType, diagnosticContext.TargetResource);
            return new OperationInvokeResult(false, false, result.UserMessage);
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<SettingSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new OperationInvokeResult(false, false, "电源参数恢复快照缺失或损坏。");
        }

        if (snapshot.Ac is null && snapshot.Dc is null)
        {
            return new OperationInvokeResult(false, false, "快照中没有可恢复的电源参数值。");
        }

        var restoreResult = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return restoreResult.Success
            ? new OperationInvokeResult(true, true, "已恢复电源参数")
            : new OperationInvokeResult(false, false, restoreResult.Message);
    }

    private async Task<(bool Success, string Message)> RestoreSnapshotInternalAsync(SettingSnapshot snapshot, CancellationToken cancellationToken)
    {
        var scheme = await ResolveSnapshotSchemeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (scheme is null)
        {
            return (false, "无法获取电源计划，恢复失败。");
        }

        var applyResult = await ApplySettingValuesAsync(scheme.Value, snapshot.Ac, snapshot.Dc, cancellationToken).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            return applyResult;
        }

        var verifyResult = await VerifyPowerSettingAsync(scheme.Value, snapshot.Ac, snapshot.Dc, cancellationToken).ConfigureAwait(false);
        return verifyResult.Success
            ? (true, "已恢复电源参数。")
            : (false, verifyResult.Message);
    }

    private async Task<Guid?> ResolveSnapshotSchemeAsync(SettingSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Scheme.HasValue)
        {
            return snapshot.Scheme.Value;
        }

        return await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Success, string Message)> ApplySettingValuesAsync(Guid schemeValue, int? acValue, int? dcValue, CancellationToken cancellationToken)
    {
        var scheme = FormatGuid(schemeValue);
        var subgroup = FormatGuid(_subGroup);
        var setting = FormatGuid(_setting);

        if (acValue is not null)
        {
            var acResult = await PowerCfgUtility.RunAsync($"/SETACVALUEINDEX {scheme} {subgroup} {setting} {acValue.Value}", cancellationToken).ConfigureAwait(false);
            if (acResult.ExitCode != 0)
            {
                return (false, BuildCommandFailureMessage("设置 AC 值失败。", acResult.StdErr, acResult.StdOut));
            }
        }

        if (dcValue is not null)
        {
            var dcResult = await PowerCfgUtility.RunAsync($"/SETDCVALUEINDEX {scheme} {subgroup} {setting} {dcValue.Value}", cancellationToken).ConfigureAwait(false);
            if (dcResult.ExitCode != 0)
            {
                return (false, BuildCommandFailureMessage("设置 DC 值失败。", dcResult.StdErr, dcResult.StdOut));
            }
        }

        var activateResult = await PowerCfgUtility.RunAsync($"/S {scheme}", cancellationToken).ConfigureAwait(false);
        if (activateResult.ExitCode != 0)
        {
            return (false, BuildCommandFailureMessage("激活电源计划失败。", activateResult.StdErr, activateResult.StdOut));
        }

        return (true, string.Empty);
    }

    private async Task<(bool Success, string Message)> VerifyPowerSettingAsync(Guid expectedScheme, int? expectedAc, int? expectedDc, CancellationToken cancellationToken)
    {
        var activeScheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
        if (activeScheme is null)
        {
            return (false, "无法重新读取当前电源计划，校验失败。");
        }

        if (activeScheme.Value != expectedScheme)
        {
            return (false, $"电源计划激活验证失败：当前激活的是 {activeScheme.Value:D}，期望 {expectedScheme:D}");
        }

        var (actualAc, actualDc) = await ReadValuesForSchemeAsync(expectedScheme, cancellationToken).ConfigureAwait(false);
        if (IsExpectedState(expectedAc, expectedDc, actualAc, actualDc))
        {
            return (true, string.Empty);
        }

        return (false, $"电源参数校验失败：当前 AC={actualAc?.ToString() ?? "?"} / DC={actualDc?.ToString() ?? "?"}，期望 AC={expectedAc?.ToString() ?? "?"} / DC={expectedDc?.ToString() ?? "?"}");
    }

    private bool IsExpectedState(int? expectedAc, int? expectedDc, int? actualAc, int? actualDc)
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

        if (_subGroup == Constants.PowerGuids.ProcessorSubgroup)
        {
            return actualAc == expectedAc && actualDc == expectedDc;
        }

        return actualAc == expectedAc || actualDc == expectedDc;
    }

    private async Task<OperationInvokeResult> BuildApplyFailureResultAsync(
        OperationExecutionContext context,
        SettingSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollbackResult = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        var message = rollbackResult.Success
            ? failureMessage + "\nOriginal power settings remained intact or were restored."
            : failureMessage + "\nAutomatic rollback failed: " + rollbackResult.Message;
        return new OperationInvokeResult(false, false, message);
    }

    private async Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        SettingSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var scheme = await ResolveSnapshotSchemeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (scheme is null)
        {
            return (false, "Unable to resolve the original power scheme for rollback.");
        }

        var originalState = await VerifyPowerSettingAsync(scheme.Value, snapshot.Ac, snapshot.Dc, cancellationToken).ConfigureAwait(false);
        if (originalState.Success)
        {
            await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
            return (true, "Original power settings remained intact.");
        }

        var rollbackResult = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!rollbackResult.Success)
        {
            return rollbackResult;
        }

        await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
        return (true, "Original power settings restored.");
    }

    private Task TryDeleteSnapshotAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        return context.TryDeleteSnapshotAsync(Id, cancellationToken);
    }

    private async Task<(Guid? Scheme, int? Ac, int? Dc)> ReadCurrentValuesAsync(CancellationToken cancellationToken)
    {
        var scheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
        if (scheme is null)
        {
            return (null, null, null);
        }

        var (ac, dc) = await ReadValuesForSchemeAsync(scheme.Value, cancellationToken).ConfigureAwait(false);
        return (scheme, ac, dc);
    }


    private async Task<(int? Ac, int? Dc)> ReadValuesForSchemeAsync(Guid scheme, CancellationToken cancellationToken)
    {
        var (ac, dc) = await PowerCfgUtility.QuerySettingValueAsync(scheme, _subGroup, _setting, cancellationToken).ConfigureAwait(false);
        if (ac is null && dc is null)
        {
            var path = $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{FormatGuid(scheme)}\{FormatGuid(_subGroup)}\{FormatGuid(_setting)}";
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = hive.OpenSubKey(path, writable: false);
            if (key is not null)
            {
                ac = ReadIndex(key, "ACSettingIndex");
                dc = ReadIndex(key, "DCSettingIndex");
            }
        }

        return (ac, dc);
    }

    private static string BuildCommandFailureMessage(string prefix, string stderr, string stdout)
    {
        var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        details = string.IsNullOrWhiteSpace(details) ? "powercfg 未返回详细错误。" : details.Trim();
        return prefix + details;
    }

    private static string FormatGuid(Guid value) => value.ToString("D").ToUpperInvariant();

    private static int? ReadIndex(RegistryKey key, string name)
    {
        var value = key.GetValue(name);
        return value switch
        {
            int i => i,
            byte[] buffer when buffer.Length >= 4 => BitConverter.ToInt32(buffer, 0),
            _ => null
        };
    }

    private static async Task<bool> CheckPowerServiceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = "query Power",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Constants.ProcessTimeouts.StandardCommand);

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
