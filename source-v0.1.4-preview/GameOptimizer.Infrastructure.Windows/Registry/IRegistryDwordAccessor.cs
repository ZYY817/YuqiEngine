using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public sealed record RegistryDwordState(
    bool KeyExists,
    bool ValueExists,
    int? Value,
    string? ValueKind = "DWord");

public interface IRegistryDwordAccessor
{
    Task<RegistryDwordState> ReadCurrentUserAsync(
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken);

    Task WriteCurrentUserAsync(
        string subKeyPath,
        string valueName,
        int value,
        CancellationToken cancellationToken);

    Task RestoreCurrentUserAsync(
        string subKeyPath,
        string valueName,
        RegistryDwordState original,
        CancellationToken cancellationToken);

    Task<RegistryDwordState> ReadLocalMachineAsync(
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前注册表访问器不支持 HKLM。");

    Task WriteLocalMachineAsync(
        string subKeyPath,
        string valueName,
        int value,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前注册表访问器不支持 HKLM。");

    Task RestoreLocalMachineAsync(
        string subKeyPath,
        string valueName,
        RegistryDwordState original,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前注册表访问器不支持 HKLM。");
}
