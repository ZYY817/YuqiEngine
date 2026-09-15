using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Infrastructure.Windows.Registry;

/// <summary>
/// Registry value kind supported by the per-interface capability. Only DWord and String are
/// writable; anything else is observed read-only and never implicitly converted.
/// </summary>
public enum PerInterfaceScalarValueKind
{
    String,
    DWord
}

/// <summary>
/// A single value observed under one enumerated sub-key of a machine hive root. The sub-key
/// name is the enumeration key (an interface GUID, adapter instance id, etc.), not a full path.
/// </summary>
public sealed record PerInterfaceValueState(
    string SubKeyName,
    string ValueName,
    bool ValueExists,
    string? ValueKind,
    string? StringValue,
    int? DwordValue);

/// <summary>
/// A machine-scope registry accessor that enumerates the direct sub-keys of a fixed root and
/// reads/writes/restores a single named value under each. It never creates or deletes keys:
/// only values under already-existing enumerated sub-keys are touched, which keeps the
/// capability's blast radius bounded to the adapter/interface entries the OS already created.
/// </summary>
public interface IPerInterfaceRegistryAccessor
{
    Task<IReadOnlyList<string>> EnumerateSubKeysAsync(
        string rootPath,
        CancellationToken cancellationToken);

    Task<PerInterfaceValueState> ReadValueAsync(
        string rootPath,
        string subKeyName,
        string valueName,
        CancellationToken cancellationToken);

    Task WriteValueAsync(
        string rootPath,
        string subKeyName,
        string valueName,
        PerInterfaceScalarValueKind kind,
        string? stringValue,
        int? dwordValue,
        CancellationToken cancellationToken);

    Task RestoreValueAsync(
        string rootPath,
        string subKeyName,
        PerInterfaceValueState original,
        CancellationToken cancellationToken);
}
