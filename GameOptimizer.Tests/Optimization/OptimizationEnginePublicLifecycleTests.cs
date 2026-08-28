using System.Collections.Concurrent;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Optimization;

public sealed class OptimizationEnginePublicLifecycleTests
{
    [Fact]
    public async Task ApplyExceptionReportsFailedAndRetryReportsCompleted()
    {
        var operation = new LifecycleOperation { ThrowApplyOnce = true };
        var engine = CreateEngine(operation);
        var firstProgress = new RecordingProgress();

        var failed = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], firstProgress);

        Assert.False(failed.Success);
        Assert.False(Assert.Single(failed.Operations).Success);
        Assert.Equal(ProgressState.Failed, firstProgress.Items.Last().State);

        var retryProgress = new RecordingProgress();
        var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], retryProgress);

        Assert.True(retry.Success);
        Assert.True(Assert.Single(retry.Operations).Success);
        Assert.Equal(ProgressState.Completed, retryProgress.Items.Last().State);
        Assert.Equal(2, operation.ApplyCalls);
    }

    [Fact]
    public async Task UnknownRestoreReportsFailedAndRetryReportsCompleted()
    {
        var store = new TestRestoreStore { SnapshotExists = true };
        var operation = new LifecycleOperation { IsApplied = false };
        var engine = CreateEngine(operation, store);
        var blockedProgress = new RecordingProgress();

        var blocked = await engine.RestoreAsync(operation.ModuleKey, [operation.Id], blockedProgress);

        var blockedSummary = Assert.Single(blocked.Operations);
        Assert.False(blocked.Success);
        Assert.False(blockedSummary.Success);
        Assert.False(blockedSummary.Changed);
        Assert.True(blockedSummary.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase) || blockedSummary.Message.Contains("未知", StringComparison.Ordinal));
        Assert.Equal(ProgressState.Failed, blockedProgress.Items.Last().State);
        Assert.Equal(0, operation.RestoreCalls);

        operation.IsApplied = true;
        var retryProgress = new RecordingProgress();
        var retry = await engine.RestoreAsync(operation.ModuleKey, [operation.Id], retryProgress);

        Assert.True(retry.Success);
        Assert.True(Assert.Single(retry.Operations).Success);
        Assert.Equal(ProgressState.Completed, retryProgress.Items.Last().State);
        Assert.Equal(1, operation.RestoreCalls);
        Assert.False(operation.IsApplied);
    }

    [Fact]
    public async Task CancelledApplyAndCancelledWaiterReleaseModuleForRetry()
    {
        var operation = new LifecycleOperation
        {
            WaitForApplyCancellation = true,
            WaitForRestoreGate = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var engine = CreateEngine(operation);
        using var firstCancellation = new CancellationTokenSource();
        using var waiterCancellation = new CancellationTokenSource();
        var firstProgress = new RecordingProgress();
        var waiterProgress = new RecordingProgress();

        var firstApply = engine.ApplyAsync(
            operation.ModuleKey,
            [operation.Id],
            firstProgress,
            firstCancellation.Token);
        await operation.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var waitingApply = engine.ApplyAsync(
            operation.ModuleKey,
            [operation.Id],
            waiterProgress,
            waiterCancellation.Token);
        await Task.Delay(50);

        firstCancellation.Cancel();
        await operation.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(waitingApply.IsCompleted);

        waiterCancellation.Cancel();
        var waitingResult = await waitingApply.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(waitingResult.Success);
        Assert.Equal(ProgressState.Cancelled, waiterProgress.Items.Last().State);

        operation.WaitForApplyCancellation = false;
        operation.WaitForRestoreGate.TrySetResult();

        var firstResult = await firstApply.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(firstResult.Success);
        Assert.Equal(ProgressState.Cancelled, firstProgress.Items.Last().State);
        Assert.Equal(1, operation.ApplyCalls);
        Assert.Equal(1, operation.RestoreCalls);

        var retryProgress = new RecordingProgress();
        var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], retryProgress)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(retry.Success);
        Assert.Equal(ProgressState.Completed, retryProgress.Items.Last().State);
        Assert.Equal(2, operation.ApplyCalls);
    }

    private static OptimizationEngine CreateEngine(
        LifecycleOperation operation,
        TestRestoreStore? store = null) =>
        new(
            [new TestProvider(operation)],
            store ?? new TestRestoreStore(),
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated());

    private sealed class TestProvider(LifecycleOperation operation) : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() =>
            [new OptimizationModuleDescriptor(operation.ModuleKey, "Lifecycle test", "Lifecycle test", [operation])];
    }

    private sealed class TestRestoreStore : IOperationRestoreStore
    {
        public bool SnapshotExists { get; set; }

        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            SnapshotExists = false;
            return Task.CompletedTask;
        }

        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(SnapshotExists);
    }

    private sealed class RecordingProgress : IOperationProgress
    {
        public ConcurrentQueue<ProgressInfo> Items { get; } = new();

        public void Report(ProgressInfo progress) => Items.Enqueue(progress);
    }

    private sealed class LifecycleOperation : IOptimizationOperation
    {
        private int _applyCalls;
        private int _restoreCalls;
        private int _throwApplyOnce;

        public string ModuleKey { get; } = $"test-public-lifecycle:{Guid.NewGuid():N}";
        public string Id => $"{ModuleKey}:operation";
        public string Title => "Public lifecycle test";
        public string Description => "Public lifecycle test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Balanced;
        public bool RequiresAdmin => false;
        public bool IsApplied { get; set; }
        public bool WaitForApplyCancellation { get; set; }
        public TaskCompletionSource? WaitForRestoreGate { get; init; }
        public TaskCompletionSource ApplyStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RestoreStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            Interlocked.Increment(ref _applyCalls);
            ApplyStarted.TrySetResult();
            if (WaitForApplyCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
            Interlocked.Increment(ref _restoreCalls);
            RestoreStarted.TrySetResult();
            if (WaitForRestoreGate is not null)
            {
                await WaitForRestoreGate.Task;
            }

            IsApplied = false;
            return new OperationInvokeResult(true, true, "restored");
        }
    }
}
