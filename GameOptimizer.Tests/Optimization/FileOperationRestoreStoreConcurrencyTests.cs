using System.Collections;
using System.Reflection;
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Tests.Optimization;

public sealed class FileOperationRestoreStoreConcurrencyTests
{
    [Fact]
    public async Task CancelledSaveReleasesPerOperationFileLock()
    {
        var operationId = $"test-cancelled-save:{Guid.NewGuid():N}";
        var store = new FileOperationRestoreStore(enableIntegrityCheck: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveSnapshotAsync(operationId, "payload", cancellation.Token));

        var locks = (IDictionary)typeof(FileOperationRestoreStore)
            .GetField("_fileLocks", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(store)!;
        Assert.False(locks.Contains(operationId));
    }
}
