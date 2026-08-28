using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.Registry;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryDwordAccessor : IRegistryDwordAccessor
{
    public Task<RegistryDwordState> ReadCurrentUserAsync(
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken) =>
        ReadAsync(Microsoft.Win32.Registry.CurrentUser, subKeyPath, valueName, cancellationToken);

    public Task WriteCurrentUserAsync(
        string subKeyPath,
        string valueName,
        int value,
        CancellationToken cancellationToken) =>
        WriteAsync(Microsoft.Win32.Registry.CurrentUser, "HKCU", subKeyPath, valueName, value, cancellationToken);

    public Task RestoreCurrentUserAsync(
        string subKeyPath,
        string valueName,
        RegistryDwordState original,
        CancellationToken cancellationToken) =>
        RestoreAsync(Microsoft.Win32.Registry.CurrentUser, "HKCU", subKeyPath, valueName, original, cancellationToken);

    public Task<RegistryDwordState> ReadLocalMachineAsync(
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken) =>
        ReadAsync(Microsoft.Win32.Registry.LocalMachine, subKeyPath, valueName, cancellationToken);

    public Task WriteLocalMachineAsync(
        string subKeyPath,
        string valueName,
        int value,
        CancellationToken cancellationToken) =>
        WriteAsync(Microsoft.Win32.Registry.LocalMachine, "HKLM", subKeyPath, valueName, value, cancellationToken);

    public Task RestoreLocalMachineAsync(
        string subKeyPath,
        string valueName,
        RegistryDwordState original,
        CancellationToken cancellationToken) =>
        RestoreAsync(Microsoft.Win32.Registry.LocalMachine, "HKLM", subKeyPath, valueName, original, cancellationToken);

    private static Task<RegistryDwordState> ReadAsync(
        RegistryKey root,
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = root.OpenSubKey(subKeyPath, writable: false);
        if (key is null)
        {
            return Task.FromResult(new RegistryDwordState(false, false, null));
        }

        var value = key.GetValue(valueName);
        if (value is null)
        {
            return Task.FromResult(new RegistryDwordState(true, false, null));
        }

        if (key.GetValueKind(valueName) != RegistryValueKind.DWord)
        {
            throw new InvalidDataException($"注册表值 {valueName} 不是 DWORD，拒绝隐式转换。");
        }

        return Task.FromResult(new RegistryDwordState(true, true, Convert.ToInt32(value), "DWord"));
    }

    private static Task WriteAsync(
        RegistryKey root,
        string hiveName,
        string subKeyPath,
        string valueName,
        int value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = root.CreateSubKey(subKeyPath, writable: true)
            ?? throw new UnauthorizedAccessException($"无法打开或创建 {hiveName}\\{subKeyPath}。");
        key.SetValue(valueName, value, RegistryValueKind.DWord);
        return Task.CompletedTask;
    }

    private static Task RestoreAsync(
        RegistryKey root,
        string hiveName,
        string subKeyPath,
        string valueName,
        RegistryDwordState original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (original.ValueExists)
        {
            if (original.Value is null)
            {
                throw new InvalidDataException("恢复状态声明注册表值存在，但未包含 DWORD 数值。");
            }

            using var key = root.CreateSubKey(subKeyPath, writable: true)
                ?? throw new UnauthorizedAccessException($"无法打开或创建 {hiveName}\\{subKeyPath}。");
            key.SetValue(valueName, original.Value.Value, RegistryValueKind.DWord);
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
