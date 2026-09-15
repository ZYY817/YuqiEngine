using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.Registry;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryStringAccessor : IRegistryStringAccessor
{
    public Task<RegistryStringState> ReadCurrentUserAsync(
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken) =>
        ReadAsync(Microsoft.Win32.Registry.CurrentUser, subKeyPath, valueName, cancellationToken);

    public Task WriteCurrentUserAsync(
        string subKeyPath,
        string valueName,
        string value,
        CancellationToken cancellationToken) =>
        WriteAsync(Microsoft.Win32.Registry.CurrentUser, "HKCU", subKeyPath, valueName, value, cancellationToken);

    public Task RestoreCurrentUserAsync(
        string subKeyPath,
        string valueName,
        RegistryStringState original,
        CancellationToken cancellationToken) =>
        RestoreAsync(Microsoft.Win32.Registry.CurrentUser, "HKCU", subKeyPath, valueName, original, cancellationToken);

    public Task<RegistryStringState> ReadLocalMachineAsync(
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken) =>
        ReadAsync(Microsoft.Win32.Registry.LocalMachine, subKeyPath, valueName, cancellationToken);

    public Task WriteLocalMachineAsync(
        string subKeyPath,
        string valueName,
        string value,
        CancellationToken cancellationToken) =>
        WriteAsync(Microsoft.Win32.Registry.LocalMachine, "HKLM", subKeyPath, valueName, value, cancellationToken);

    public Task RestoreLocalMachineAsync(
        string subKeyPath,
        string valueName,
        RegistryStringState original,
        CancellationToken cancellationToken) =>
        RestoreAsync(Microsoft.Win32.Registry.LocalMachine, "HKLM", subKeyPath, valueName, original, cancellationToken);

    private static Task<RegistryStringState> ReadAsync(
        RegistryKey root,
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = root.OpenSubKey(subKeyPath, writable: false);
        if (key is null)
        {
            return Task.FromResult(new RegistryStringState(false, false, null, null));
        }

        var valueExists = key.GetValueNames().Contains(valueName, StringComparer.Ordinal);
        if (!valueExists)
        {
            return Task.FromResult(new RegistryStringState(true, false, null, null));
        }

        var valueKind = key.GetValueKind(valueName);
        if (valueKind != RegistryValueKind.String)
        {
            return Task.FromResult(new RegistryStringState(true, true, valueKind.ToString(), null));
        }

        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (value is null)
        {
            throw new InvalidDataException($"注册表值 {valueName} 声明为 REG_SZ，但无法读取字符串内容。");
        }

        return Task.FromResult(new RegistryStringState(true, true, RegistryValueKind.String.ToString(), value));
    }

    private static Task WriteAsync(
        RegistryKey root,
        string hiveName,
        string subKeyPath,
        string valueName,
        string value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = root.CreateSubKey(subKeyPath, writable: true)
            ?? throw new UnauthorizedAccessException($"无法打开或创建 {hiveName}\\{subKeyPath}。");
        key.SetValue(valueName, value, RegistryValueKind.String);
        return Task.CompletedTask;
    }

    private static Task RestoreAsync(
        RegistryKey root,
        string hiveName,
        string subKeyPath,
        string valueName,
        RegistryStringState original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRestorableState(original);

        if (original.ValueExists)
        {
            using var key = root.CreateSubKey(subKeyPath, writable: true)
                ?? throw new UnauthorizedAccessException($"无法打开或创建 {hiveName}\\{subKeyPath}。");
            key.SetValue(valueName, original.Value!, RegistryValueKind.String);
            return Task.CompletedTask;
        }

        using (var key = root.OpenSubKey(subKeyPath, writable: true))
        {
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }

        if (!original.KeyExists)
        {
            TryDeleteEmptyLeaf(root, subKeyPath);
        }

        return Task.CompletedTask;
    }

    private static void ValidateRestorableState(RegistryStringState state)
    {
        if (state.ValueExists
            && (!string.Equals(state.ValueKind, RegistryValueKind.String.ToString(), StringComparison.Ordinal)
                || state.Value is null))
        {
            throw new InvalidDataException("恢复材料中的原值不是完整的 REG_SZ，已阻断恢复。");
        }

        if (!state.ValueExists && (state.ValueKind is not null || state.Value is not null))
        {
            throw new InvalidDataException("恢复材料声明原值不存在，但仍包含值类型或内容。");
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
