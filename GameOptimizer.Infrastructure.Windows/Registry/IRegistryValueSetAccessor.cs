namespace GameOptimizer.Infrastructure.Windows.Registry;

public enum RegistryScalarValueKind
{
    String,
    DWord
}

public enum RegistryTargetAction
{
    Set,
    Delete
}

public sealed record RegistryTypedTargetValue(
    string ValueName,
    RegistryScalarValueKind Kind,
    string? StringValue,
    int? DwordValue,
    RegistryTargetAction Action = RegistryTargetAction.Set)
{
    public static RegistryTypedTargetValue String(string valueName, string value) =>
        new(valueName, RegistryScalarValueKind.String, value, null);

    public static RegistryTypedTargetValue DWord(string valueName, int value) =>
        new(valueName, RegistryScalarValueKind.DWord, null, value);

    public static RegistryTypedTargetValue Delete(string valueName, RegistryScalarValueKind declaredKind) =>
        new(valueName, declaredKind, null, null, RegistryTargetAction.Delete);
}

public sealed record RegistryValueSetEntryState(
    string ValueName,
    bool KeyExists,
    bool ValueExists,
    string? ValueKind,
    string? StringValue,
    int? DwordValue);

public sealed record RegistryValueSetPayload(IReadOnlyList<RegistryValueSetEntryState> Values);

public sealed record RegistryValueSetTargetRequest(IReadOnlyList<RegistryTypedTargetValue> Values);

public sealed record RegistryTypedTargetConstraint(
    string ValueName,
    RegistryScalarValueKind Kind,
    int? MinimumNumericValue = null,
    int? MaximumNumericValue = null);

public interface IRegistryValueSetAccessor
{
    Task<IReadOnlyList<RegistryValueSetEntryState>> ReadCurrentUserAsync(
        string subKeyPath,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken);

    Task WriteCurrentUserValueAsync(
        string subKeyPath,
        RegistryTypedTargetValue target,
        CancellationToken cancellationToken);

    Task RestoreCurrentUserValueAsync(
        string subKeyPath,
        RegistryValueSetEntryState original,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistryValueSetEntryState>> ReadLocalMachineAsync(
        string subKeyPath,
        IReadOnlyList<string> valueNames,
        CancellationToken cancellationToken);

    Task WriteLocalMachineValueAsync(
        string subKeyPath,
        RegistryTypedTargetValue target,
        CancellationToken cancellationToken);

    Task RestoreLocalMachineValueAsync(
        string subKeyPath,
        RegistryValueSetEntryState original,
        CancellationToken cancellationToken);
}
