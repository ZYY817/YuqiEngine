using System;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Utilities;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class RegistryToggleOperation : IOptimizationOperation
{
    private record RegistrySnapshot(bool? KeyExisted, bool ValueExists, string? SerializedValue, RegistryValueKind? ValueKind);

    private readonly RegistryHive _hive;
    private readonly string _subKeyPath;
    private readonly string _valueName;
    private readonly object _desiredValue;
    private readonly RegistryValueKind _valueKind;

    public RegistryToggleOperation(
        string id,
        string title,
        string description,
        string category,
        RegistryHive hive,
        string subKeyPath,
        string valueName,
        object desiredValue,
        RegistryValueKind valueKind)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _hive = hive;
        _subKeyPath = subKeyPath;
        _valueName = valueName;
        _desiredValue = desiredValue;
        _valueKind = valueKind;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public string? ImpactWarning { get; init; }
    public bool AffectsUserFeeling { get; init; }
    public OperationType OperationType => OperationType.Registry;
    public string? RegistryPath => _subKeyPath;
    public string? RegistryHiveText => _hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        _ => _hive.ToString()
    };
    public string? RegistryValueName => _valueName;
    public string? OptimizedValue => Normalize(_desiredValue, _valueKind);
    public string? DefaultValue => null;
    public string? ValueType => GetValueTypeLabel(_valueKind);
    public bool RequiresAdmin => _hive == RegistryHive.LocalMachine;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var current = ReadValueRaw();
            if (!current.Exists)
            {
                return Task.FromResult(new OperationStatus(false, "注册表值不存在。"));
            }

            var normalizedCurrent = Normalize(current.Value, current.Kind);
            var normalizedDesired = Normalize(_desiredValue, _valueKind);
            var compliant = string.Equals(normalizedCurrent, normalizedDesired, StringComparison.OrdinalIgnoreCase);
            var details = compliant
                ? string.Concat("当前值已匹配目标值: ", normalizedDesired)
                : string.Concat("当前值 ", normalizedCurrent, "，目标值 ", normalizedDesired);
            return Task.FromResult(new OperationStatus(compliant, details));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new OperationStatus(false, "权限不足，无法读取注册表。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, string.Concat("检测注册表状态失败: ", ex.Message)));
        }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var current = ReadValueRaw();
            var snapshot = new RegistrySnapshot(
                current.KeyExists,
                current.Exists,
                current.Exists ? Normalize(current.Value, current.Kind) : null,
                current.Exists ? current.Kind : null);

            if (current.Exists && string.Equals(Normalize(current.Value, current.Kind), Normalize(_desiredValue, _valueKind), StringComparison.OrdinalIgnoreCase))
            {
                return new OperationInvokeResult(true, false, "注册表项已处于目标状态。");
            }

            await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

            using var hive = RegistryKey.OpenBaseKey(_hive, RegistryView.Default);
            using var key = hive.CreateSubKey(_subKeyPath, writable: true);
            if (key is null)
            {
                await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
                return new OperationInvokeResult(false, false, string.Concat("无法打开注册表项: ", _subKeyPath));
            }

            try
            {
                key.SetValue(_valueName, ConvertForRegistry(_desiredValue, _valueKind), _valueKind);
                if (!VerifyDesiredValue(key))
                {
                    return await BuildApplyFailureResultAsync(context, snapshot, "注册表写入后校验失败。", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return await BuildApplyFailureResultAsync(context, snapshot, string.Concat("注册表写入失败: ", ex.Message), cancellationToken).ConfigureAwait(false);
            }

            return new OperationInvokeResult(true, true, string.Concat("已写入 ", _valueName, "=", Normalize(_desiredValue, _valueKind)));
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "注册表配置");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await context.LoadSnapshotAsync<RegistrySnapshot>(Id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return new OperationInvokeResult(false, false, "注册表恢复快照缺失或损坏。");
            }

            using var hive = RegistryKey.OpenBaseKey(_hive, RegistryView.Default);
            using var key = ResolveRestoreKey(hive, snapshot);
            if (key is null && ((snapshot.KeyExisted ?? true) || snapshot.ValueExists))
            {
                return new OperationInvokeResult(false, false, string.Concat("无法恢复注册表项: ", _subKeyPath));
            }

            if (key is null)
            {
                return new OperationInvokeResult(true, true, "注册表值已恢复。" );
            }

            return RestoreSnapshotInternal(key, snapshot)
                ? new OperationInvokeResult(true, true, "注册表值已恢复。")
                : new OperationInvokeResult(false, false, "注册表值恢复后校验失败。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "注册表配置");
        }
    }

    private async Task<OperationInvokeResult> BuildApplyFailureResultAsync(
        OperationExecutionContext context,
        RegistrySnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        var message = rollback.Success
            ? failureMessage + " Original registry state remained intact or was restored."
            : failureMessage + " Automatic rollback failed. " + rollback.Message;
        return new OperationInvokeResult(false, false, message);
    }

    private Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        RegistrySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return OperationRollbackHelper.TryRestoreOriginalStateAsync(
            context,
            Id,
            () => MatchesSnapshot(snapshot),
            () => RestoreSnapshot(snapshot),
            "Original registry state remained intact.",
            "Original registry state restored.",
            cancellationToken);
    }

    private bool MatchesSnapshot(RegistrySnapshot snapshot)
    {
        var current = ReadValueRaw();
        if (!snapshot.ValueExists)
        {
            return !current.Exists;
        }

        if (!current.Exists || current.Kind != (snapshot.ValueKind ?? RegistryValueKind.String))
        {
            return false;
        }

        return string.Equals(
            Normalize(current.Value, current.Kind),
            snapshot.SerializedValue ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private (bool Success, string Message) RestoreSnapshot(RegistrySnapshot snapshot)
    {
        using var hive = RegistryKey.OpenBaseKey(_hive, RegistryView.Default);
        using var key = ResolveRestoreKey(hive, snapshot);
        if (key is null && ((snapshot.KeyExisted ?? true) || snapshot.ValueExists))
        {
            return (false, string.Concat("Unable to open registry key for rollback: ", _subKeyPath));
        }

        if (key is null)
        {
            return (true, string.Empty);
        }

        return RestoreSnapshotInternal(key, snapshot)
            ? (true, string.Empty)
            : (false, "Registry rollback verification failed.");
    }

    private Task TryDeleteSnapshotAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        return context.TryDeleteSnapshotAsync(Id, cancellationToken);
    }

    private (bool KeyExists, bool Exists, object? Value, RegistryValueKind Kind) ReadValueRaw()
    {
        using var hive = RegistryKey.OpenBaseKey(_hive, RegistryView.Default);
        using var key = hive.OpenSubKey(_subKeyPath, writable: false);
        if (key is null)
        {
            return (false, false, null, RegistryValueKind.Unknown);
        }

        var value = key.GetValue(_valueName);
        if (value is null)
        {
            return (true, false, null, RegistryValueKind.Unknown);
        }

        return (true, true, value, key.GetValueKind(_valueName));
    }

    private RegistryKey? ResolveRestoreKey(RegistryKey hive, RegistrySnapshot snapshot)
    {
        if (snapshot.KeyExisted == false && !snapshot.ValueExists)
        {
            return hive.OpenSubKey(_subKeyPath, writable: true);
        }

        return hive.CreateSubKey(_subKeyPath, writable: true);
    }

    private bool VerifyDesiredValue(RegistryKey key)
    {
        var actual = key.GetValue(_valueName);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(_valueName);
        return actualKind == _valueKind
            && string.Equals(Normalize(actual, actualKind), Normalize(_desiredValue, _valueKind), StringComparison.OrdinalIgnoreCase);
    }

    private bool RestoreSnapshotInternal(RegistryKey key, RegistrySnapshot snapshot)
    {
        if (snapshot.ValueExists)
        {
            var kind = snapshot.ValueKind ?? RegistryValueKind.String;
            key.SetValue(_valueName, Parse(snapshot.SerializedValue, kind), kind);
        }
        else
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
            if (snapshot.KeyExisted == false)
            {
                TryDeleteEmptySubKey(_hive, _subKeyPath);
            }
        }

        if (!snapshot.ValueExists)
        {
            return key.GetValue(_valueName) is null;
        }

        var actual = key.GetValue(_valueName);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(_valueName);
        var expectedKind = snapshot.ValueKind ?? RegistryValueKind.String;
        return actualKind == expectedKind
            && string.Equals(Normalize(actual, actualKind), snapshot.SerializedValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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

    private static string GetValueTypeLabel(RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => "DWORD",
            RegistryValueKind.QWord => "QWORD",
            RegistryValueKind.String => "REG_SZ",
            RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
            RegistryValueKind.MultiString => "REG_MULTI_SZ",
            RegistryValueKind.Binary => "REG_BINARY",
            _ => "Unknown"
        };
    }
}
