using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.Registry;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryValueSetAccessor : IRegistryValueSetAccessor
{
    public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadCurrentUserAsync(
        string subKeyPath,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken) =>
        ReadAsync(Microsoft.Win32.Registry.CurrentUser, "HKCU", subKeyPath, valueNames, cancellationToken);

    public Task WriteCurrentUserValueAsync(
        string subKeyPath,
        RegistryTypedTargetValue target,
        CancellationToken cancellationToken) =>
        WriteAsync(Microsoft.Win32.Registry.CurrentUser, "HKCU", subKeyPath, target, cancellationToken);

    public Task RestoreCurrentUserValueAsync(
        string subKeyPath,
        RegistryValueSetEntryState original,
        CancellationToken cancellationToken) =>
        RestoreAsync(Microsoft.Win32.Registry.CurrentUser, "HKCU", subKeyPath, original, cancellationToken);

    public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadLocalMachineAsync(
        string subKeyPath,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken) =>
        ReadAsync(Microsoft.Win32.Registry.LocalMachine, "HKLM", subKeyPath, valueNames, cancellationToken);

    public Task WriteLocalMachineValueAsync(
        string subKeyPath,
        RegistryTypedTargetValue target,
        CancellationToken cancellationToken) =>
        WriteAsync(Microsoft.Win32.Registry.LocalMachine, "HKLM", subKeyPath, target, cancellationToken);

    public Task RestoreLocalMachineValueAsync(
        string subKeyPath,
        RegistryValueSetEntryState original,
        CancellationToken cancellationToken) =>
        RestoreAsync(Microsoft.Win32.Registry.LocalMachine, "HKLM", subKeyPath, original, cancellationToken);

    private static Task<IReadOnlyList<RegistryValueSetEntryState>> ReadAsync(
        RegistryKey root,
        string hiveName,
        string subKeyPath,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = root.OpenSubKey(subKeyPath, writable: false);
        if (key is null)
        {
            return Task.FromResult<IReadOnlyList<RegistryValueSetEntryState>>(
                valueNames.Select(name => new RegistryValueSetEntryState(name, false, false, null, null, null)).ToArray());
        }

        var existingNames = new HashSet<string>(key.GetValueNames(), StringComparer.OrdinalIgnoreCase);
        var states = new RegistryValueSetEntryState[valueNames.Count];
        for (var index = 0; index < valueNames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var valueName = valueNames[index];
            if (!existingNames.Contains(valueName))
            {
                states[index] = new RegistryValueSetEntryState(valueName, true, false, null, null, null);
                continue;
            }

            var kind = key.GetValueKind(valueName);
            var rawValue = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            states[index] = kind switch
            {
                RegistryValueKind.String when rawValue is string text =>
                    new RegistryValueSetEntryState(valueName, true, true, kind.ToString(), text, null),
                RegistryValueKind.DWord when rawValue is int number =>
                    new RegistryValueSetEntryState(valueName, true, true, kind.ToString(), null, number),
                _ => new RegistryValueSetEntryState(valueName, true, true, kind.ToString(), null, null)
            };
        }

        return Task.FromResult<IReadOnlyList<RegistryValueSetEntryState>>(states);
    }

    private static Task WriteAsync(
        RegistryKey root,
        string hiveName,
        string subKeyPath,
        RegistryTypedTargetValue target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTarget(target);
        using var key = root.CreateSubKey(subKeyPath, writable: true)
            ?? throw new UnauthorizedAccessException($"无法打开或创建 {hiveName}\\{subKeyPath}。");
        switch (target.Kind)
        {
            case RegistryScalarValueKind.String:
                key.SetValue(target.ValueName, target.StringValue!, RegistryValueKind.String);
                break;
            case RegistryScalarValueKind.DWord:
                key.SetValue(target.ValueName, target.DwordValue!.Value, RegistryValueKind.DWord);
                break;
            default:
                throw new InvalidDataException($"不支持的注册表目标类型：{target.Kind}。");
        }

        return Task.CompletedTask;
    }

    private static Task RestoreAsync(
        RegistryKey root,
        string hiveName,
        string subKeyPath,
        RegistryValueSetEntryState original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRestorableState(original);
        if (original.ValueExists)
        {
            using var key = root.CreateSubKey(subKeyPath, writable: true)
                ?? throw new UnauthorizedAccessException($"无法打开或创建 {hiveName}\\{subKeyPath}。");
            if (string.Equals(original.ValueKind, RegistryValueKind.String.ToString(), StringComparison.Ordinal))
            {
                key.SetValue(original.ValueName, original.StringValue!, RegistryValueKind.String);
            }
            else
            {
                key.SetValue(original.ValueName, original.DwordValue!.Value, RegistryValueKind.DWord);
            }

            return Task.CompletedTask;
        }

        using (var key = root.OpenSubKey(subKeyPath, writable: true))
        {
            key?.DeleteValue(original.ValueName, throwOnMissingValue: false);
        }

        if (!original.KeyExists)
        {
            TryDeleteEmptyLeaf(root, subKeyPath);
        }

        return Task.CompletedTask;
    }

    private static void ValidateTarget(RegistryTypedTargetValue target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ValueName);
        var valid = target.Action == RegistryTargetAction.Set && target.Kind switch
        {
            RegistryScalarValueKind.String => target.StringValue is not null && target.DwordValue is null,
            RegistryScalarValueKind.DWord => target.StringValue is null && target.DwordValue is not null,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException($"注册表目标 {target.ValueName} 的类型与载荷不一致。");
        }
    }

    private static void ValidateRestorableState(RegistryValueSetEntryState state)
    {
        if (!state.ValueExists)
        {
            if (state.ValueKind is not null || state.StringValue is not null || state.DwordValue is not null)
            {
                throw new InvalidDataException($"恢复材料声明 {state.ValueName} 不存在，但仍包含类型或内容。");
            }

            return;
        }

        var valid = string.Equals(state.ValueKind, RegistryValueKind.String.ToString(), StringComparison.Ordinal)
            ? state.StringValue is not null && state.DwordValue is null
            : string.Equals(state.ValueKind, RegistryValueKind.DWord.ToString(), StringComparison.Ordinal)
              && state.StringValue is null
              && state.DwordValue is not null;
        if (!valid)
        {
            throw new InvalidDataException($"恢复材料中的 {state.ValueName} 不是完整的 REG_SZ 或 DWORD。");
        }
    }

    private static void TryDeleteEmptyLeaf(RegistryKey root, string subKeyPath)
    {
        var separator = subKeyPath.LastIndexOf('\\');
        if (separator <= 0 || separator >= subKeyPath.Length - 1)
        {
            return;
        }

        var parentPath = subKeyPath[..separator];
        var leafName = subKeyPath[(separator + 1)..];
        using var leaf = root.OpenSubKey(subKeyPath, writable: false);
        if (leaf is not null && (leaf.SubKeyCount > 0 || leaf.ValueCount > 0))
        {
            return;
        }

        using var parent = root.OpenSubKey(parentPath, writable: true);
        parent?.DeleteSubKey(leafName, throwOnMissingSubKey: false);
    }
}
