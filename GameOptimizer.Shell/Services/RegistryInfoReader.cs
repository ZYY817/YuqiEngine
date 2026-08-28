using Microsoft.Win32;

namespace GameOptimizer.Shell.Services;

public interface IRegistryInfoReader
{
    RegistryInfoValue Read(string hive, string keyPath, string valueName);
}

public sealed record RegistryInfoValue(
    bool Exists,
    object? Value,
    RegistryInfoValueKind Kind);

public enum RegistryInfoValueKind
{
    Unknown,
    DWord,
    QWord,
    String,
    ExpandString,
    MultiString,
    Binary
}

/// <summary>
/// Read-only adapter for the registry details dialog. It deliberately exposes no writable key or
/// value APIs, so dialog presentation cannot become a registry mutation path.
/// </summary>
public sealed class WindowsRegistryInfoReader : IRegistryInfoReader
{
    public static WindowsRegistryInfoReader Instance { get; } = new();

    private WindowsRegistryInfoReader()
    {
    }

    public RegistryInfoValue Read(string hive, string keyPath, string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hive);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

        var registryHive = hive.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, "Unsupported registry hive.")
        };

        using var baseKey = RegistryKey.OpenBaseKey(registryHive, RegistryView.Default);
        using var key = baseKey.OpenSubKey(keyPath);
        if (key is null)
        {
            return new RegistryInfoValue(false, null, RegistryInfoValueKind.Unknown);
        }

        var value = key.GetValue(valueName);
        return value is null
            ? new RegistryInfoValue(false, null, RegistryInfoValueKind.Unknown)
            : new RegistryInfoValue(true, value, MapKind(key.GetValueKind(valueName)));
    }

    private static RegistryInfoValueKind MapKind(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => RegistryInfoValueKind.DWord,
        RegistryValueKind.QWord => RegistryInfoValueKind.QWord,
        RegistryValueKind.String => RegistryInfoValueKind.String,
        RegistryValueKind.ExpandString => RegistryInfoValueKind.ExpandString,
        RegistryValueKind.MultiString => RegistryInfoValueKind.MultiString,
        RegistryValueKind.Binary => RegistryInfoValueKind.Binary,
        _ => RegistryInfoValueKind.Unknown
    };
}
