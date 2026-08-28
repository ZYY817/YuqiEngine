using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.Registry;

[SupportedOSPlatform("windows")]
public sealed class WindowsPerInterfaceRegistryAccessor : IPerInterfaceRegistryAccessor
{
    public Task<IReadOnlyList<string>> EnumerateSubKeysAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(rootPath, writable: false);
        if (root is null)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        return Task.FromResult<IReadOnlyList<string>>(
            root.GetSubKeyNames()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public Task<PerInterfaceValueState> ReadValueAsync(
        string rootPath,
        string subKeyName,
        string valueName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            $@"{rootPath}\{subKeyName}", writable: false);
        if (key is null)
        {
            return Task.FromResult(new PerInterfaceValueState(subKeyName, valueName, false, null, null, null));
        }

        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
        {
            return Task.FromResult(new PerInterfaceValueState(subKeyName, valueName, false, null, null, null));
        }

        var kind = key.GetValueKind(valueName);
        return Task.FromResult(kind switch
        {
            RegistryValueKind.String when value is string text =>
                new PerInterfaceValueState(subKeyName, valueName, true, kind.ToString(), text, null),
            RegistryValueKind.DWord when value is int number =>
                new PerInterfaceValueState(subKeyName, valueName, true, kind.ToString(), null, number),
            _ => new PerInterfaceValueState(subKeyName, valueName, true, kind.ToString(), null, null)
        });
    }

    public Task WriteValueAsync(
        string rootPath,
        string subKeyName,
        string valueName,
        PerInterfaceScalarValueKind kind,
        string? stringValue,
        int? dwordValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            $@"{rootPath}\{subKeyName}", writable: true)
            ?? throw new UnauthorizedAccessException($"无法打开或写入 HKLM\\{rootPath}\\{subKeyName}。");

        switch (kind)
        {
            case PerInterfaceScalarValueKind.String:
                key.SetValue(valueName, stringValue!, RegistryValueKind.String);
                break;
            case PerInterfaceScalarValueKind.DWord:
                key.SetValue(valueName, dwordValue!.Value, RegistryValueKind.DWord);
                break;
            default:
                throw new InvalidDataException($"不支持的每接口注册表目标类型：{kind}。");
        }

        return Task.CompletedTask;
    }

    public Task RestoreValueAsync(
        string rootPath,
        string subKeyName,
        PerInterfaceValueState original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            $@"{rootPath}\{subKeyName}", writable: true);
        if (key is null)
        {
            throw new UnauthorizedAccessException($"无法打开 HKLM\\{rootPath}\\{subKeyName} 以恢复值。");
        }

        if (!original.ValueExists)
        {
            key.DeleteValue(original.ValueName, throwOnMissingValue: false);
            return Task.CompletedTask;
        }

        if (string.Equals(original.ValueKind, RegistryValueKind.String.ToString(), StringComparison.Ordinal))
        {
            key.SetValue(original.ValueName, original.StringValue!, RegistryValueKind.String);
        }
        else if (string.Equals(original.ValueKind, RegistryValueKind.DWord.ToString(), StringComparison.Ordinal))
        {
            key.SetValue(original.ValueName, original.DwordValue!.Value, RegistryValueKind.DWord);
        }
        else
        {
            throw new InvalidDataException($"恢复材料中的 {original.ValueName} 不是可恢复的受支持类型。");
        }

        return Task.CompletedTask;
    }
}
