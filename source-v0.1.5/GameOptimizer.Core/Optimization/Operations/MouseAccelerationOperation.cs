using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Utilities;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class MouseAccelerationOperation : IOptimizationOperation
{
    private const string MouseKeyPath = @"Control Panel\Mouse";

    private static readonly IReadOnlyDictionary<string, string> DefaultTargetValues = new Dictionary<string, string>
    {
        ["MouseSpeed"] = "0",
        ["MouseThreshold1"] = "0",
        ["MouseThreshold2"] = "0"
    };

    private record Snapshot(Dictionary<string, string> Values, HashSet<string> Missing);

    public MouseAccelerationOperation(string id, string title, string description, string category)
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
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;
    public bool AffectsUserFeeling => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var key = hive.OpenSubKey(MouseKeyPath, writable: false);
            if (key is null)
            {
                return Task.FromResult(new OperationStatus(false, "未检测到鼠标配置项。"));
            }

            var targetValues = GetTargetValues();
            var mismatches = new List<string>();
            foreach (var entry in targetValues)
            {
                var currentValue = InputRegistryValueHelper.ReadStringValue(key, entry.Key);
                if (!string.Equals(currentValue, entry.Value, StringComparison.Ordinal))
                {
                    mismatches.Add($"{entry.Key}={currentValue ?? "<missing>"}, expected {entry.Value}");
                }
            }

            if (mismatches.Count == 0)
            {
                return Task.FromResult(new OperationStatus(true, "鼠标加速度参数已匹配目标配置。"));
            }

            return Task.FromResult(new OperationStatus(false, string.Join("; ", mismatches)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, $"检测鼠标参数失败: {ex.Message}"));
        }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var key = hive.CreateSubKey(MouseKeyPath, writable: true);
            if (key is null)
            {
                return new OperationInvokeResult(false, false, $"无法打开注册表项: {MouseKeyPath}");
            }

            var targetValues = GetTargetValues();
            if (MatchesTargetValues(key, targetValues))
            {
                return new OperationInvokeResult(true, false, "鼠标参数已经处于目标状态。");
            }

            var snapshot = ReadSnapshot(key);
            await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

            bool IsOriginalStateIntact()
            {
                foreach (var valueName in DefaultTargetValues.Keys)
                {
                    if (snapshot.Missing.Contains(valueName))
                    {
                        if (key.GetValue(valueName) is not null)
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!snapshot.Values.TryGetValue(valueName, out var originalValue))
                    {
                        return false;
                    }

                    if (!InputRegistryValueHelper.MatchesStringValue(key, valueName, originalValue))
                    {
                        return false;
                    }
                }

                return true;
            }

            (bool Success, string Message) RestoreOriginalState()
            {
                return RestoreSnapshotValues(key, snapshot)
                    ? (true, string.Empty)
                    : (false, "恢复鼠标原始参数后校验失败。");
            }

            try
            {
                foreach (var entry in targetValues)
                {
                    if (!InputRegistryValueHelper.WriteStringValue(key, entry.Key, entry.Value))
                    {
                        return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                            context,
                            Id,
                            $"写入 {entry.Key} 失败。",
                            IsOriginalStateIntact,
                            RestoreOriginalState,
                            "原始状态未变更。",
                            "已回滚。",
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                if (!MatchesTargetValues(key, targetValues))
                {
                    return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                        context,
                        Id,
                        "鼠标参数写入后校验失败。",
                        IsOriginalStateIntact,
                        RestoreOriginalState,
                        "原始状态未变更。",
                        "已回滚。",
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return await OperationRollbackHelper.BuildApplyFailureResultAsync(
                    context,
                    Id,
                    string.Concat("写入鼠标参数时发生异常: ", ex.Message, "。"),
                    IsOriginalStateIntact,
                    RestoreOriginalState,
                    "原始状态未变更。",
                    "已回滚。",
                    cancellationToken).ConfigureAwait(false);
            }

            var message = PeripheralSettingsProvider.GetSettings() is null
                ? "已关闭增强型指针精度。"
                : "已应用自定义鼠标参数。";
            return new OperationInvokeResult(true, true, message);
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "外设优化");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await context.LoadSnapshotAsync<Snapshot>(Id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return new OperationInvokeResult(false, false, "鼠标参数恢复快照缺失或损坏。");
            }

            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var key = hive.CreateSubKey(MouseKeyPath, writable: true);
            if (key is null)
            {
                return new OperationInvokeResult(false, false, $"无法打开注册表项: {MouseKeyPath}");
            }

            if (!RestoreSnapshotValues(key, snapshot))
            {
                return new OperationInvokeResult(false, false, "恢复鼠标参数后校验失败。");
            }

            return new OperationInvokeResult(true, true, "已恢复鼠标指针设置。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "外设优化");
        }
    }

    private static IReadOnlyDictionary<string, string> GetTargetValues()
    {
        var settings = PeripheralSettingsProvider.GetSettings();
        if (settings is null)
        {
            return DefaultTargetValues;
        }

        return new Dictionary<string, string>
        {
            ["MouseSpeed"] = settings.MouseSpeed.ToString(),
            ["MouseThreshold1"] = settings.MouseThreshold1.ToString(),
            ["MouseThreshold2"] = settings.MouseThreshold2.ToString()
        };
    }

    private static Snapshot ReadSnapshot(RegistryKey key)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var valueName in DefaultTargetValues.Keys)
        {
            var currentValue = InputRegistryValueHelper.ReadStringValue(key, valueName);
            if (currentValue is null)
            {
                missing.Add(valueName);
            }
            else
            {
                values[valueName] = currentValue;
            }
        }

        return new Snapshot(values, missing);
    }

    private static bool MatchesTargetValues(RegistryKey key, IReadOnlyDictionary<string, string> targetValues)
    {
        return targetValues.All(entry => string.Equals(InputRegistryValueHelper.ReadStringValue(key, entry.Key), entry.Value, StringComparison.Ordinal));
    }

    private static bool RestoreSnapshotValues(RegistryKey key, Snapshot snapshot)
    {
        foreach (var valueName in DefaultTargetValues.Keys)
        {
            if (snapshot.Missing.Contains(valueName))
            {
                if (!InputRegistryValueHelper.RestoreStringValue(key, valueName, null))
                {
                    return false;
                }

                continue;
            }

            if (!snapshot.Values.TryGetValue(valueName, out var originalValue))
            {
                return false;
            }

            if (!InputRegistryValueHelper.RestoreStringValue(key, valueName, originalValue))
            {
                return false;
            }
        }

        return true;
    }
}