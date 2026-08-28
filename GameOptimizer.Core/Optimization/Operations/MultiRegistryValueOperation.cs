using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Applies multiple registry values under one optimization ID and restores them atomically.
/// </summary>
public sealed class MultiRegistryValueOperation : IOptimizationOperation
{
    public readonly record struct RegistryValueDefinition(
        RegistryHive Hive,
        string SubKeyPath,
        string ValueName,
        object DesiredValue,
        RegistryValueKind ValueKind);

    private record ValueSnapshot(
        RegistryHive Hive,
        string SubKeyPath,
        string ValueName,
        bool? KeyExisted,
        bool ValueExists,
        string? SerializedValue,
        RegistryValueKind? OriginalKind);

    private record OperationSnapshot(IReadOnlyList<ValueSnapshot> Values);

    private readonly IReadOnlyList<RegistryValueDefinition> _definitions;

    public MultiRegistryValueOperation(
        string id,
        string title,
        string description,
        string category,
        IEnumerable<RegistryValueDefinition> definitions)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _definitions = definitions.ToArray();
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public OperationType OperationType => OperationType.Registry;
    public bool RequiresAdmin => _definitions.Any(static d => d.Hive == RegistryHive.LocalMachine);
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var mismatched = 0;
        foreach (var definition in _definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ReadValue(definition);
            if (!IsDesiredState(definition, current))
            {
                mismatched++;
            }
        }

        var compliant = mismatched == 0;
        var details = compliant
            ? "所有注册表策略值均已匹配。"
            : string.Concat(mismatched, "/", _definitions.Count, " 个注册表策略值未应用。");
        return Task.FromResult(new OperationStatus(compliant, details));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshots = CaptureSnapshots(cancellationToken);
        if (snapshots.Count == 0)
        {
            return new OperationInvokeResult(true, false, "没有可应用的注册表策略。");
        }

        var operationSnapshot = new OperationSnapshot(snapshots);
        var changedDefinitions = new List<RegistryValueDefinition>();

        foreach (var definition in _definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ReadValue(definition);
            if (!IsDesiredState(definition, current))
            {
                changedDefinitions.Add(definition);
            }
        }

        if (changedDefinitions.Count == 0)
        {
            return new OperationInvokeResult(true, false, "所有注册表策略已处于目标状态。");
        }

        await context.SaveSnapshotAsync(Id, operationSnapshot, cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var definition in changedDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!WriteDesiredValue(definition))
                {
                    return await BuildApplyFailureResultAsync(
                        context,
                        operationSnapshot,
                        string.Concat("写入 ", definition.ValueName, " 后校验失败。"),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            return await BuildApplyFailureResultAsync(
                context,
                operationSnapshot,
                string.Concat("写入注册表策略时发生异常: ", ex.Message),
                cancellationToken).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, string.Concat("已写入 ", changedDefinitions.Count, " 个注册表策略值。"));
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<OperationSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.Values.Count == 0)
        {
            return new OperationInvokeResult(false, false, "多值注册表恢复快照缺失或损坏。");
        }

        var restored = 0;
        var failed = new List<string>();

        foreach (var entry in snapshot.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RestoreSnapshotEntry(entry))
            {
                failed.Add(string.Concat(entry.SubKeyPath, "::", entry.ValueName));
                continue;
            }

            restored++;
        }

        if (failed.Count > 0)
        {
            return new OperationInvokeResult(false, restored > 0, string.Concat("已恢复 ", restored, " 个注册表策略值，失败项: ", string.Join(", ", failed.Take(5)), "。"));
        }

        return new OperationInvokeResult(true, restored > 0, string.Concat("已恢复 ", restored, " 个注册表策略值。"));
    }

    private async Task<OperationInvokeResult> BuildApplyFailureResultAsync(
        OperationExecutionContext context,
        OperationSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        var message = rollback.Success
            ? failureMessage + " Original registry policy state remained intact or was restored."
            : failureMessage + " Automatic rollback failed. " + rollback.Message;
        return new OperationInvokeResult(false, false, message);
    }

    private Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        OperationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return OperationRollbackHelper.TryRestoreOriginalStateAsync(
            context,
            Id,
            () => MatchesSnapshotSet(snapshot.Values),
            () => RestoreSnapshotState(snapshot.Values),
            "Original registry policy state remained intact.",
            "Original registry policy state restored.",
            cancellationToken);
    }

    private static (bool Success, string Message) RestoreSnapshotState(IEnumerable<ValueSnapshot> snapshots)
    {
        return RestoreSnapshotSet(snapshots)
            ? (true, string.Empty)
            : (false, "Registry policy rollback verification failed.");
    }

    private static bool MatchesSnapshotSet(IEnumerable<ValueSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (!MatchesSnapshotEntry(snapshot))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesSnapshotEntry(ValueSnapshot snapshot)
    {
        using var hive = RegistryKey.OpenBaseKey(snapshot.Hive, RegistryView.Default);
        using var key = hive.OpenSubKey(snapshot.SubKeyPath, writable: false);
        if (key is null)
        {
            return !snapshot.ValueExists;
        }

        if (!snapshot.ValueExists)
        {
            return key.GetValue(snapshot.ValueName) is null;
        }

        var actual = key.GetValue(snapshot.ValueName);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(snapshot.ValueName);
        var expectedKind = snapshot.OriginalKind ?? RegistryValueKind.String;
        return actualKind == expectedKind
            && string.Equals(Normalize(actual, actualKind), snapshot.SerializedValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private Task TryDeleteSnapshotAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        return context.TryDeleteSnapshotAsync(Id, cancellationToken);
    }

    private List<ValueSnapshot> CaptureSnapshots(CancellationToken cancellationToken)
    {
        var snapshots = new List<ValueSnapshot>(_definitions.Count);
        foreach (var definition in _definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ReadValue(definition);
            snapshots.Add(new ValueSnapshot(
                definition.Hive,
                definition.SubKeyPath,
                definition.ValueName,
                current.KeyExists,
                current.Exists,
                current.Exists ? Normalize(current.Value, current.Kind) : null,
                current.Exists ? current.Kind : null));
        }

        return snapshots;
    }

    private static (bool KeyExists, bool Exists, object? Value, RegistryValueKind Kind) ReadValue(RegistryValueDefinition definition)
    {
        using var hive = RegistryKey.OpenBaseKey(definition.Hive, RegistryView.Default);
        using var key = hive.OpenSubKey(definition.SubKeyPath, writable: false);
        if (key is null)
        {
            return (false, false, null, RegistryValueKind.Unknown);
        }

        var value = key.GetValue(definition.ValueName);
        if (value is null)
        {
            return (true, false, null, RegistryValueKind.Unknown);
        }

        return (true, true, value, key.GetValueKind(definition.ValueName));
    }

    private static bool IsDesiredState(RegistryValueDefinition definition, (bool KeyExists, bool Exists, object? Value, RegistryValueKind Kind) current)
    {
        return current.Exists
            && string.Equals(Normalize(current.Value, current.Kind), Normalize(definition.DesiredValue, definition.ValueKind), StringComparison.OrdinalIgnoreCase);
    }

    private static bool WriteDesiredValue(RegistryValueDefinition definition)
    {
        using var hive = RegistryKey.OpenBaseKey(definition.Hive, RegistryView.Default);
        using var key = hive.CreateSubKey(definition.SubKeyPath, writable: true);
        if (key is null)
        {
            return false;
        }

        key.SetValue(definition.ValueName, ConvertForRegistry(definition.DesiredValue, definition.ValueKind), definition.ValueKind);
        var actual = key.GetValue(definition.ValueName);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(definition.ValueName);
        return actualKind == definition.ValueKind
            && string.Equals(Normalize(actual, actualKind), Normalize(definition.DesiredValue, definition.ValueKind), StringComparison.OrdinalIgnoreCase);
    }

    private static bool RestoreSnapshotSet(IEnumerable<ValueSnapshot> snapshots)
    {
        var success = true;
        foreach (var snapshot in snapshots.Reverse())
        {
            if (!RestoreSnapshotEntry(snapshot))
            {
                success = false;
            }
        }

        return success;
    }

    private static bool RestoreSnapshotEntry(ValueSnapshot snapshot)
    {
        using var hive = RegistryKey.OpenBaseKey(snapshot.Hive, RegistryView.Default);
        using var key = ResolveRestoreKey(hive, snapshot);
        if (key is null && ((snapshot.KeyExisted ?? true) || snapshot.ValueExists))
        {
            return false;
        }

        if (key is null)
        {
            return true;
        }

        if (snapshot.ValueExists)
        {
            var kind = snapshot.OriginalKind ?? RegistryValueKind.String;
            key.SetValue(snapshot.ValueName, Parse(snapshot.SerializedValue, kind), kind);
        }
        else
        {
            key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
            if (snapshot.KeyExisted == false)
            {
                TryDeleteEmptySubKey(snapshot.Hive, snapshot.SubKeyPath);
            }
        }

        if (!snapshot.ValueExists)
        {
            return key.GetValue(snapshot.ValueName) is null;
        }

        var actual = key.GetValue(snapshot.ValueName);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(snapshot.ValueName);
        var expectedKind = snapshot.OriginalKind ?? RegistryValueKind.String;
        return actualKind == expectedKind
            && string.Equals(Normalize(actual, actualKind), snapshot.SerializedValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static RegistryKey? ResolveRestoreKey(RegistryKey hive, ValueSnapshot snapshot)
    {
        if (snapshot.KeyExisted == false && !snapshot.ValueExists)
        {
            return hive.OpenSubKey(snapshot.SubKeyPath, writable: true);
        }

        return hive.CreateSubKey(snapshot.SubKeyPath, writable: true);
    }

    private static void TryDeleteEmptySubKey(RegistryHive hiveKind, string subKeyPath)
    {
        var separatorIndex = subKeyPath.LastIndexOf('\\');
        if (separatorIndex <= 0 || separatorIndex >= subKeyPath.Length - 1)
        {
            return;
        }

        var parentPath = subKeyPath[..separatorIndex];
        var leafName = subKeyPath[(separatorIndex + 1)..];

        using var hive = RegistryKey.OpenBaseKey(hiveKind, RegistryView.Default);
        using var key = hive.OpenSubKey(subKeyPath, writable: false);
        if (key is not null && (key.ValueCount > 0 || key.SubKeyCount > 0))
        {
            return;
        }

        using var parent = hive.OpenSubKey(parentPath, writable: true);
        parent?.DeleteSubKey(leafName, throwOnMissingSubKey: false);
    }

    private static string Normalize(object? value, RegistryValueKind kind)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return kind switch
        {
            RegistryValueKind.DWord => NormalizeUnsigned(value, 32),
            RegistryValueKind.QWord => NormalizeUnsigned(value, 64),
            RegistryValueKind.MultiString => string.Join(",", (string[])value),
            RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string NormalizeUnsigned(object value, int bitSize)
    {
        if (bitSize == 32)
        {
            var signed = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return unchecked((uint)signed).ToString(CultureInfo.InvariantCulture);
        }

        var signed64 = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return unchecked((ulong)signed64).ToString(CultureInfo.InvariantCulture);
    }

    private static object ConvertForRegistry(object value, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            RegistryValueKind.MultiString => value is string[] array
                ? array
                : (Convert.ToString(value, CultureInfo.InvariantCulture)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>()),
            RegistryValueKind.Binary => value is byte[] bytes
                ? bytes
                : Convert.FromBase64String(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static object Parse(string? value, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => value is null ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture),
            RegistryValueKind.MultiString => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>(),
            RegistryValueKind.Binary => string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Convert.FromBase64String(value),
            _ => value ?? string.Empty
        };
    }
}
