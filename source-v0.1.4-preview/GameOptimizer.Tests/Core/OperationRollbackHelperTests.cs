using System;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Core;

public sealed class OperationRollbackHelperTests
{
    [Fact]
    public async Task TryRestoreOriginalStateAsync_DoesNotRestore_WhenOriginalStateIsIntact()
    {
        var store = new InMemoryRestoreStore();
        var context = new OperationExecutionContext(store, NullLogger.Instance);
        var restoreCalled = false;

        var result = await OperationRollbackHelper.TryRestoreOriginalStateAsync(
            context,
            "op.test",
            () => true,
            () =>
            {
                restoreCalled = true;
                return (true, string.Empty);
            },
            "intact",
            "restored",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("intact", result.Message);
        Assert.False(restoreCalled);
        Assert.Equal(1, store.DeleteCount);
    }

    [Fact]
    public async Task TryRestoreOriginalStateAsync_DeletesSnapshot_WhenRestoreSucceeds()
    {
        var store = new InMemoryRestoreStore();
        var context = new OperationExecutionContext(store, NullLogger.Instance);
        var restoreCalled = false;

        var result = await OperationRollbackHelper.TryRestoreOriginalStateAsync(
            context,
            "op.test",
            () => false,
            () =>
            {
                restoreCalled = true;
                return (true, string.Empty);
            },
            "intact",
            "restored",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("restored", result.Message);
        Assert.True(restoreCalled);
        Assert.Equal(1, store.DeleteCount);
    }

    [Fact]
    public async Task BuildApplyFailureResultAsync_PreservesFailureContext_WhenRollbackFails()
    {
        var store = new InMemoryRestoreStore();
        var context = new OperationExecutionContext(store, NullLogger.Instance);

        var result = await OperationRollbackHelper.BuildApplyFailureResultAsync(
            context,
            "op.test",
            "write failed.",
            () => false,
            () => (false, "rollback failed."),
            "intact",
            "restored",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Equal("write failed. rollback failed.", result.Message);
        Assert.Equal(0, store.DeleteCount);
    }

    private sealed class InMemoryRestoreStore : IOperationRestoreStore
    {
        public int DeleteCount { get; private set; }

        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }

        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}