using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Optimization;

public sealed class OperationExecutionContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public OperationExecutionContext(IOperationRestoreStore restoreStore, ILogger logger)
    {
        RestoreStore = restoreStore;
        Logger = logger;
    }

    public IOperationRestoreStore RestoreStore { get; }
    public ILogger Logger { get; }

    public Task SaveSnapshotAsync<T>(string operationId, T snapshot, CancellationToken cancellationToken) =>
        RestoreStore.SaveSnapshotAsync(operationId, JsonSerializer.Serialize(snapshot, SerializerOptions), cancellationToken);

    public async Task<T?> LoadSnapshotAsync<T>(string operationId, CancellationToken cancellationToken)
    {
        var payload = await RestoreStore.LoadSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
        return payload is null
            ? default
            : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
    }

    public async Task TryDeleteSnapshotAsync(string operationId, CancellationToken cancellationToken)
    {
        try
        {
            await RestoreStore.DeleteSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to delete stale snapshot for operation {OperationId}.", operationId);
        }
    }

    /// <summary>
    /// P0-1: 获取注册表路径锁，防止并发写入冲突
    /// </summary>
    public Task<OperationLock> AcquireRegistryLockAsync(string registryPath, CancellationToken cancellationToken = default)
    {
        var lockId = $"registry:{registryPath.ToLowerInvariant()}";
        return OperationLock.AcquireAsync(lockId, cancellationToken);
    }

    /// <summary>
    /// P0-1: 尝试获取注册表路径锁（带超时）
    /// </summary>
    public Task<OperationLock?> TryAcquireRegistryLockAsync(string registryPath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var lockId = $"registry:{registryPath.ToLowerInvariant()}";
        return OperationLock.TryAcquireAsync(lockId, timeout, cancellationToken);
    }

    /// <summary>
    /// P1-4: 获取配置文件锁，防止并发读写冲突
    /// </summary>
    public Task<OperationLock> AcquireConfigFileLockAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var lockId = $"config:{filePath.ToLowerInvariant()}";
        return OperationLock.AcquireAsync(lockId, cancellationToken);
    }
}
