using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// TCP Supplemental参数操作 - 用于设置congestionprovider等需要supplemental子命令的参数
/// </summary>
public sealed class NetshTcpSupplementalOperation : IOptimizationOperation
{
    private static readonly IReadOnlyDictionary<string, string[]> ParameterAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["congestionprovider"] = new[]
            {
                "congestionprovider",
                "Congestion Provider",
                "Add-On Congestion Control Provider",
                "附加拥塞控制提供程序",
                "拥塞控制提供程序"
            }
        };

    private static readonly SemaphoreSlim QueryCacheLock = new(1, 1);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan VerificationRetryDelay = TimeSpan.FromMilliseconds(500);
    private const int VerificationRetryCount = 3;

    private static (DateTime Timestamp, Dictionary<string, string> Values, string? Error)? _cachedSupplemental;

    private readonly string _canonicalKey;
    private readonly string _argumentName;
    private readonly string _desiredValue;
    private readonly string _desiredDisplay;
    private readonly Func<string, string> _normalize;
    private readonly Func<string, string> _valueToCommand;

    private record TcpSnapshot(string PreviousValue);

    public NetshTcpSupplementalOperation(
        string id,
        string title,
        string description,
        string category,
        string argumentName,
        string desiredValue,
        string? desiredDisplay = null,
        Func<string, string>? normalize = null,
        Func<string, string>? valueToCommand = null)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _argumentName = argumentName;
        _canonicalKey = NormalizeParameterKey(argumentName);
        _desiredValue = desiredValue;
        _desiredDisplay = desiredDisplay ?? desiredValue;
        _normalize = normalize ?? DefaultNormalize;
        _valueToCommand = valueToCommand ?? (value => value.Trim().ToLowerInvariant());
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public OperationType OperationType => OperationType.Command;
    public string? ManualPath => "cmd.exe";
    public string? TargetName => $"TCP Supplemental: {_argumentName}";
    public string? TargetValue => _desiredDisplay;
    public string? ManualSteps => $"1. 按 Win+X 选择“终端(管理员)”\n2. 输入命令：netsh int tcp set supplemental {_argumentName}={_valueToCommand(_desiredValue)}\n3. 按回车执行\n4. 查看当前设置：netsh int tcp show supplemental\n5. 部分设置可能需要重启网络适配器生效";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var (current, error) = await ReadCurrentValueWithRetryAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new OperationStatus(false, error ?? ElevationGate.DescribeWriteDenial("读取 TCP Supplemental 参数"));
        }

        var compliant = string.Equals(_normalize(current), _normalize(_desiredValue), StringComparison.OrdinalIgnoreCase);
        var details = $"当前={current} / 目标={_desiredDisplay}";
        if (!compliant)
        {
            details += " (部分网络设置可能需要重启网络适配器后完全稳定)";
        }

        return new OperationStatus(compliant, details);
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var (current, error) = await ReadCurrentValueWithRetryAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new OperationInvokeResult(false, false, error ?? "无法捕获当前 TCP Supplemental 参数，已阻止写入以避免不可恢复的变更。");
        }

        if (string.Equals(_normalize(current), _normalize(_desiredValue), StringComparison.OrdinalIgnoreCase))
        {
            return new OperationInvokeResult(true, false, "参数已符合要求。");
        }

        var snapshot = new TcpSnapshot(current);
        await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

        var applyResult = await SetValueAsync(_desiredValue, cancellationToken).ConfigureAwait(false);
        if (applyResult.Success == false)
        {
            return await FailApplyAndTryRollbackAsync(context, snapshot, applyResult.Message, cancellationToken).ConfigureAwait(false);
        }

        var verifyResult = await VerifyCurrentValueAsync(_desiredValue, cancellationToken).ConfigureAwait(false);
        if (verifyResult.Success)
        {
            return new OperationInvokeResult(true, true, $"已设置为 {_desiredDisplay}");
        }

        return await FailApplyAndTryRollbackAsync(
            context,
            snapshot,
            $"TCP Supplemental 参数写入后校验失败，原因：{verifyResult.Message}",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<TcpSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.PreviousValue))
        {
            return new OperationInvokeResult(false, false, "TCP Supplemental 参数恢复快照缺失或损坏。");
        }

        var restoreResult = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return restoreResult.Success
            ? new OperationInvokeResult(true, true, $"已恢复为 {snapshot.PreviousValue}")
            : new OperationInvokeResult(false, false, restoreResult.Message);
    }

    private async Task<(bool Success, string Message)> RestoreSnapshotInternalAsync(TcpSnapshot snapshot, CancellationToken cancellationToken)
    {
        var setResult = await SetValueAsync(snapshot.PreviousValue, cancellationToken).ConfigureAwait(false);
        if (setResult.Success == false)
        {
            return setResult;
        }

        var verifyResult = await VerifyCurrentValueAsync(snapshot.PreviousValue, cancellationToken).ConfigureAwait(false);
        if (verifyResult.Success)
        {
            return (true, $"已恢复为 {snapshot.PreviousValue}");
        }

        return (false, $"TCP Supplemental 参数恢复后校验失败：{verifyResult.Message}");
    }

    private async Task<OperationInvokeResult> FailApplyAndTryRollbackAsync(
        OperationExecutionContext context,
        TcpSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalValueAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(false, false, $"{failureMessage} {rollback.Message}");
    }

    private async Task<(bool Success, string Message)> TryRestoreOriginalValueAsync(
        OperationExecutionContext context,
        TcpSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var originalState = await VerifyCurrentValueAsync(snapshot.PreviousValue, cancellationToken).ConfigureAwait(false);
        if (originalState.Success)
        {
            await context.TryDeleteSnapshotAsync(Id, cancellationToken).ConfigureAwait(false);
            return (true, $"Original TCP supplemental value remained {snapshot.PreviousValue}; stale snapshot removed.");
        }

        var rollbackResult = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!rollbackResult.Success)
        {
            return (false, $"Automatic rollback failed. {rollbackResult.Message}");
        }

        await context.TryDeleteSnapshotAsync(Id, cancellationToken).ConfigureAwait(false);
        return (true, $"Original TCP supplemental value restored to {snapshot.PreviousValue}.");
    }
    private async Task<(bool Success, string Message)> SetValueAsync(string value, CancellationToken cancellationToken)
    {
        var argument = _valueToCommand(value);
        var result = await NetshUtility.RunAsync($"int tcp set supplemental {_argumentName}={argument}", cancellationToken).ConfigureAwait(false);
        InvalidateQueryCache();
        if (result.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            return (false, message.Trim());
        }

        return (true, string.Empty);
    }

    private async Task<(bool Success, string Message)> VerifyCurrentValueAsync(string expectedValue, CancellationToken cancellationToken)
    {
        var (current, error) = await ReadCurrentValueWithRetryAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return (false, error ?? "无法重新读取 TCP Supplemental 参数。\n请确认 netsh 输出正常。");
        }

        if (string.Equals(_normalize(current), _normalize(expectedValue), StringComparison.OrdinalIgnoreCase))
        {
            return (true, string.Empty);
        }

        return (false, $"当前值为 {current}，期望值为 {expectedValue}");
    }

    private async Task<(string? Value, string? Error)> ReadCurrentValueWithRetryAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        string? lastError = null;
        for (var attempt = 0; attempt < VerificationRetryCount; attempt++)
        {
            var (values, error) = await QuerySupplementalValuesAsync(cancellationToken, forceRefresh || attempt > 0).ConfigureAwait(false);
            if (values.TryGetValue(_canonicalKey, out var current) && current is not null)
            {
                return (current, null);
            }

            lastError = error ?? $"参数 {_argumentName} 未出现在 netsh supplemental 输出中。";
            if (attempt < VerificationRetryCount - 1)
            {
                await Task.Delay(VerificationRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return (null, lastError);
    }

    private static void InvalidateQueryCache()
    {
        _cachedSupplemental = null;
    }

    private static string DefaultNormalize(string value) => value.Trim().ToLowerInvariant();

    private static async Task<(Dictionary<string, string> Values, string? Error)> QuerySupplementalValuesAsync(CancellationToken cancellationToken, bool forceRefresh)
    {
        var cached = _cachedSupplemental;
        if (!forceRefresh && cached.HasValue && DateTime.UtcNow - cached.Value.Timestamp < CacheTtl)
        {
            return (cached.Value.Values, cached.Value.Error);
        }

        if (!await QueryCacheLock.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false))
        {
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "Query lock timeout");
        }

        try
        {
            cached = _cachedSupplemental;
            if (!forceRefresh && cached.HasValue && DateTime.UtcNow - cached.Value.Timestamp < CacheTtl)
            {
                return (cached.Value.Values, cached.Value.Error);
            }

            var result = await NetshUtility.RunAsync("int tcp show supplemental", cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
                var emptyResult = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _cachedSupplemental = (DateTime.UtcNow, emptyResult, error.Trim());
                return (emptyResult, error.Trim());
            }

            var values = ParseSupplementalOutput(result.StdOut);
            _cachedSupplemental = (DateTime.UtcNow, values, null);
            return (values, null);
        }
        finally
        {
            QueryCacheLock.Release();
        }
    }

    private static Dictionary<string, string> ParseSupplementalOutput(string stdout)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(stdout);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("----", StringComparison.Ordinal))
            {
                continue;
            }

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex < 0)
            {
                continue;
            }

            var key = trimmed[..colonIndex].Trim();
            var canonical = MapToCanonicalKey(key);
            var value = trimmed[(colonIndex + 1)..].Trim();
            values[canonical] = string.IsNullOrEmpty(value) ? "default" : value;
        }

        return values;
    }

    private static string MapToCanonicalKey(string key)
    {
        var normalized = NormalizeParameterKey(key);
        foreach (var pair in ParameterAliases)
        {
            if (pair.Value.Any(alias => NormalizeParameterKey(alias) == normalized))
            {
                return pair.Key;
            }
        }

        return normalized;
    }

    private static string NormalizeParameterKey(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
