using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Optimization;

public sealed class OperationLockReentrancyTests
{
    [Fact]
    public async Task CancelledWaiterDoesNotPreventLaterLockAcquire()
    {
        var resourceId = $"test:cancelled-waiter:{Guid.NewGuid():N}";
        using var owner = await OperationLock.TryAcquireAsync(
            resourceId,
            TimeSpan.FromSeconds(2));
        Assert.NotNull(owner);

        using var cancellation = new CancellationTokenSource();
        var cancelledWaiter = OperationLock.TryAcquireAsync(
            resourceId,
            TimeSpan.FromSeconds(2),
            cancellation.Token);

        await Task.Delay(50);
        cancellation.Cancel();

        Assert.Null(await cancelledWaiter.WaitAsync(TimeSpan.FromSeconds(2)));
        owner!.Dispose();

        using var nextOwner = await OperationLock.TryAcquireAsync(
            resourceId,
            TimeSpan.FromSeconds(2));
        Assert.NotNull(nextOwner);
    }

    [Fact]
    public async Task TimedOutWaiterDoesNotPreventLaterLockAcquire()
    {
        var resourceId = $"test:timed-out-waiter:{Guid.NewGuid():N}";
        using var owner = await OperationLock.TryAcquireAsync(
            resourceId,
            TimeSpan.FromSeconds(2));
        Assert.NotNull(owner);

        var timedOutWaiter = await OperationLock.TryAcquireAsync(
            resourceId,
            TimeSpan.FromMilliseconds(50));
        Assert.Null(timedOutWaiter);

        owner!.Dispose();
        using var nextOwner = await OperationLock.TryAcquireAsync(
            resourceId,
            TimeSpan.FromSeconds(2));
        Assert.NotNull(nextOwner);
    }

    [Fact]
    public async Task WithLockExceptionReleasesLeaseForRetry()
    {
        var resourceId = $"test:delegate-exception:{Guid.NewGuid():N}";

        var failed = await resourceId.WithLockAsync<string>(
            _ => throw new InvalidOperationException("synthetic delegate failure"));
        var retried = await resourceId.WithLockAsync(
            _ => Task.FromResult("retry succeeded"));

        Assert.Equal(OperationResultCode.Failed, failed.Code);
        Assert.True(retried.IsSuccess);
        Assert.Equal("retry succeeded", retried.Data);
    }

    [Fact]
    public async Task ConcurrentApplyIsSerializedAndReleasesModuleLock()
    {
        var operation = new ReentrancyTestOperation
        {
            ApplyGate = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var engine = CreateEngine(operation);

        var firstApply = engine.ApplyAsync(operation.ModuleKey);
        await operation.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondApply = engine.ApplyAsync(operation.ModuleKey);
        await Task.Delay(50);
        Assert.False(secondApply.IsCompleted);

        operation.ApplyGate.TrySetResult();
        var firstResult = await firstApply.WaitAsync(TimeSpan.FromSeconds(2));
        var secondResult = await secondApply.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(firstResult.Success);
        Assert.True(secondResult.Success);
        Assert.Equal(1, operation.ApplyCalls);
    }

    [Fact]
    public async Task ConcurrentRestoreIsSerializedAndReleasesModuleLock()
    {
        var operation = new ReentrancyTestOperation
        {
            IsApplied = true,
            RestoreGate = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var engine = CreateEngine(operation);

        var firstRestore = engine.RestoreAsync(operation.ModuleKey);
        await operation.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondRestore = engine.RestoreAsync(operation.ModuleKey);
        await Task.Delay(50);
        Assert.False(secondRestore.IsCompleted);

        operation.RestoreGate.TrySetResult();
        var firstResult = await firstRestore.WaitAsync(TimeSpan.FromSeconds(2));
        var secondResult = await secondRestore.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(firstResult.Success);
        Assert.False(secondResult.Success);
        Assert.Equal(1, operation.RestoreCalls);
        Assert.False(Assert.Single(secondResult.Operations).Changed);
        var blockedRestoreMessage = Assert.Single(secondResult.Operations).Message;
        Assert.True(blockedRestoreMessage.Contains("unknown", StringComparison.OrdinalIgnoreCase)
            || blockedRestoreMessage.Contains("未知", StringComparison.Ordinal));

        var thirdApply = await engine.ApplyAsync(operation.ModuleKey).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(thirdApply.Success);
        Assert.Equal(1, operation.ApplyCalls);
    }

    [Fact]
    public async Task ApplyExceptionReleasesModuleLockForRetry()
    {
        var operation = new ReentrancyTestOperation { ThrowApplyOnce = true };
        var engine = CreateEngine(operation);

        var failed = await engine.ApplyAsync(operation.ModuleKey);
        var retry = await engine.ApplyAsync(operation.ModuleKey);

        Assert.False(failed.Success);
        Assert.True(retry.Success);
        Assert.Equal(2, operation.ApplyCalls);
    }

    private static OptimizationEngine CreateEngine(ReentrancyTestOperation operation) =>
        new(
            [new TestProvider(operation)],
            new TestRestoreStore(),
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated());

    private sealed class TestProvider(ReentrancyTestOperation operation) : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() =>
            [new OptimizationModuleDescriptor(operation.ModuleKey, "Test", "Test", [operation])];
    }

    private sealed class TestRestoreStore : IOperationRestoreStore
    {
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class ReentrancyTestOperation : IOptimizationOperation
    {
        private int _applyCalls;
        private int _restoreCalls;
        private int _throwApplyOnce;

        public string ModuleKey { get; } = $"test-module:{Guid.NewGuid():N}";
        public string Id => $"{ModuleKey}:operation";
        public string Title => "Reentrancy test";
        public string Description => "Reentrancy test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Balanced;
        public bool RequiresAdmin => false;
        public bool IsApplied { get; set; }
        public TaskCompletionSource ApplyStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RestoreStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ApplyGate { get; init; }
        public TaskCompletionSource? RestoreGate { get; init; }
        public bool ThrowApplyOnce
        {
            get => Volatile.Read(ref _throwApplyOnce) == 1;
            init => Volatile.Write(ref _throwApplyOnce, value ? 1 : 0);
        }

        public int ApplyCalls => Volatile.Read(ref _applyCalls);
        public int RestoreCalls => Volatile.Read(ref _restoreCalls);

        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationStatus(IsApplied, IsApplied ? "applied" : "not applied"));

        public async Task<OperationInvokeResult> ApplyAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _applyCalls);
            ApplyStarted.TrySetResult();
            if (call == 1 && ApplyGate is not null)
            {
                await ApplyGate.Task.WaitAsync(cancellationToken);
            }

            if (Interlocked.Exchange(ref _throwApplyOnce, 0) == 1)
            {
                throw new InvalidOperationException("synthetic apply failure");
            }

            IsApplied = true;
            return new OperationInvokeResult(true, true, "applied");
        }

        public async Task<OperationInvokeResult> RestoreAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _restoreCalls);
            RestoreStarted.TrySetResult();
            if (call == 1 && RestoreGate is not null)
            {
                await RestoreGate.Task.WaitAsync(cancellationToken);
            }

            IsApplied = false;
            return new OperationInvokeResult(true, true, "restored");
        }
    }
}
