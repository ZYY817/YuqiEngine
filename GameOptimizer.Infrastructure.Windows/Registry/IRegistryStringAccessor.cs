namespace GameOptimizer.Infrastructure.Windows.Registry;

public sealed record RegistryStringState(
    bool KeyExists,
    bool ValueExists,
    string? ValueKind,
    string? Value);

public interface IRegistryStringAccessor
{
    Task<RegistryStringState> ReadCurrentUserAsync(
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken);

    Task WriteCurrentUserAsync(
        string subKeyPath,
        string valueName,
        string value,
        CancellationToken cancellationToken);

    Task RestoreCurrentUserAsync(
        string subKeyPath,
        string valueName,
        RegistryStringState original,
        CancellationToken cancellationToken);

    Task<RegistryStringState> ReadLocalMachineAsync(
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken);

    Task WriteLocalMachineAsync(
        string subKeyPath,
        string valueName,
        string value,
        CancellationToken cancellationToken);

    Task RestoreLocalMachineAsync(
        string subKeyPath,
        string valueName,
        RegistryStringState original,
        CancellationToken cancellationToken);
}
