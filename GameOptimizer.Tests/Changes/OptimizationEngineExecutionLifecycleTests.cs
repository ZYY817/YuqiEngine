using System.Collections.Concurrent;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class OptimizationEngineExecutionLifecycleTests
{
    [Fact]
    public async Task EmptySelectionReturnsFailedBatchAndDoesNotInvokeOperation()
    {
        var operation = new TestOperation();
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();

        var result = await engine.ApplyAsync(operation.ModuleKey, ["missing.operation"], progress);

        Assert.False(result.Success);
        var summary = Assert.Single(result.Operations);
        Assert.False(summary.Success);
        Assert.False(summary.Changed);
        Assert.Equal(0, operation.ApplyCalls);
        Assert.Equal(ProgressState.Failed, progress.Items.Last().State);
    }

    [Fact]
    public async Task ApplyCancellationReturnsTerminalFailureAndAllowsRetry()
    {
        var operation = new TestOperation { BlockApplyUntilCancelled = true };
        var engine = CreateEngine(operation);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress();

        var cancelledTask = engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress, cancellation.Token);
        await operation.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var cancelled = await cancelledTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(cancelled.Success);
        Assert.False(Assert.Single(cancelled.Operations).Success);
        Assert.Equal(ProgressState.Cancelled, progress.Items.Last().State);

        operation.BlockApplyUntilCancelled = false;
        var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(retry.Success);
        Assert.True(Assert.Single(retry.Operations).Success);
        Assert.Equal(ProgressState.Completed, progress.Items.Last().State);
        Assert.Equal(2, operation.ApplyCalls);
    }

    [Fact]
    public async Task OperationCancellationWithoutCallerCancellationIsReportedAsTimeoutAndAllowsRetry()
    {
        var operation = new TestOperation { CancelApplyOnce = true };
        var engine = CreateEngine(operation);

        var timedOut = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);

        Assert.False(timedOut.Success);
        var timeoutMessage = Assert.Single(timedOut.Operations).Message;
        Assert.True(
            timeoutMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || timeoutMessage.Contains("超时", StringComparison.Ordinal),
            $"Unexpected timeout message: {timeoutMessage}");

        var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);

        Assert.True(retry.Success);
        Assert.Equal(2, operation.ApplyCalls);
    }

    [Fact]
    public async Task CancelledApplyWithFailedRollbackReportsRecoveryFailure()
    {
        var operation = new TestOperation
        {
            BlockApplyUntilCancelled = true,
            ThrowRestoreOnce = true
        };
        var engine = CreateEngine(operation);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress();

        var applyTask = engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress, cancellation.Token);
        await operation.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var result = await applyTask.WaitAsync(TimeSpan.FromSeconds(2));
        var summary = Assert.Single(result.Operations);

        Assert.False(result.Success);
        Assert.False(summary.Success);
        Assert.True(summary.Changed);
        Assert.True(
            summary.Message.Contains("rollback", StringComparison.OrdinalIgnoreCase)
                || summary.Message.Contains("回滚", StringComparison.Ordinal),
            $"Unexpected recovery message: {summary.Message}");
        Assert.Equal(ProgressState.Failed, progress.Items.Last().State);
    }

    [Fact]
    public async Task ApplyAndRestoreCompetitionIsSerializedAndBothLocksCanBeRetried()
    {
        var operation = new TestOperation
        {
            IsApplied = true,
            BlockRestoreUntilReleased = true
        };
        var engine = CreateEngine(operation);

        var restoreTask = engine.RestoreAsync(operation.ModuleKey, [operation.Id]);
        await operation.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var applyTask = engine.ApplyAsync(operation.ModuleKey, [operation.Id]);

        await Task.Delay(50);
        Assert.False(applyTask.IsCompleted);
        Assert.Equal(1, operation.RestoreCalls);
        Assert.Equal(0, operation.ApplyCalls);

        operation.ReleaseRestore();
        var restored = await restoreTask.WaitAsync(TimeSpan.FromSeconds(2));
        var applied = await applyTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(restored.Success);
        Assert.True(applied.Success);
        Assert.Equal(1, operation.RestoreCalls);
        Assert.Equal(1, operation.ApplyCalls);

        var restoreRetry = await engine.RestoreAsync(operation.ModuleKey, [operation.Id]);
        Assert.True(restoreRetry.Success);
        Assert.Equal(2, operation.RestoreCalls);
    }

    [Fact]
    public async Task CancellationAfterVerifiedWorkDoesNotProduceContradictoryTerminalState()
    {
        var operation = new TestOperation();
        var engine = CreateEngine(operation);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress
        {
            OnReport = item =>
            {
                if (item.State == ProgressState.Running)
                {
                    cancellation.Cancel();
                }
            }
        };

        var result = await engine.ApplyAsync(
            operation.ModuleKey,
            [operation.Id],
            progress,
            cancellation.Token);

        Assert.True(result.Success);
        Assert.True(Assert.Single(result.Operations).Success);
        Assert.Equal(ProgressState.Completed, progress.Items.Last().State);
    }

    [Fact]
    public async Task CancelledLockWaiterDoesNotPoisonLaterAcquire()
    {
        var resourceId = $"test:engine-lifecycle:{Guid.NewGuid():N}";
        using var owner = await OperationLock.TryAcquireAsync(resourceId, TimeSpan.FromSeconds(2));
        Assert.NotNull(owner);

        using var cancellation = new CancellationTokenSource();
        var waiter = OperationLock.TryAcquireAsync(resourceId, TimeSpan.FromSeconds(2), cancellation.Token);
        cancellation.Cancel();

        Assert.Null(await waiter.WaitAsync(TimeSpan.FromSeconds(2)));
        owner!.Dispose();

        using var retry = await OperationLock.TryAcquireAsync(resourceId, TimeSpan.FromSeconds(2));
        Assert.NotNull(retry);
    }

    private static OptimizationEngine CreateEngine(TestOperation operation) =>
        new(
            [new TestProvider(operation)],
            new TestRestoreStore(),
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated());

    private sealed class TestProvider(TestOperation operation) : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() =>
            [new(operation.ModuleKey, "Engine lifecycle", "Engine lifecycle", [operation])];
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

    private sealed class RecordingProgress : IOperationProgress
    {
        public ConcurrentQueue<ProgressInfo> Items { get; } = new();
        public Action<ProgressInfo>? OnReport { get; init; }

        public void Report(ProgressInfo progress)
        {
            Items.Enqueue(progress);
            OnReport?.Invoke(progress);
        }
    }

    private sealed class TestOperation : IOptimizationOperation
    {
        private int _applyCalls;
        private int _restoreCalls;
        private int _cancelApplyOnce;
        private int _throwRestoreOnce;
        private readonly TaskCompletionSource _restoreRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ModuleKey { get; } = $"test-engine-lifecycle:{Guid.NewGuid():N}";
        public string Id => $"{ModuleKey}:operation";
        public string Title => "Engine lifecycle test";
        public string Description => "Engine lifecycle test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin => false;
        public bool IsApplied { get; set; }
        public bool BlockApplyUntilCancelled { get; set; }
        public bool BlockRestoreUntilReleased { get; set; }
        public bool CancelApplyOnce
        {
            get => Volatile.Read(ref _cancelApplyOnce) == 1;
            init => Volatile.Write(ref _cancelApplyOnce, value ? 1 : 0);
        }

        public bool ThrowRestoreOnce
        {
            get => Volatile.Read(ref _throwRestoreOnce) == 1;
            init => Volatile.Write(ref _throwRestoreOnce, value ? 1 : 0);
        }

        public int ApplyCalls => Volatile.Read(ref _applyCalls);
        public int RestoreCalls => Volatile.Read(ref _restoreCalls);
        public TaskCompletionSource ApplyStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RestoreStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationStatus(IsApplied, IsApplied ? "applied" : "not applied"));

        public async Task<OperationInvokeResult> ApplyAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _applyCalls);
            ApplyStarted.TrySetResult();
            if (BlockApplyUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (Interlocked.Exchange(ref _cancelApplyOnce, 0) == 1)
            {
                throw new OperationCanceledException();
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
            if (Interlocked.Exchange(ref _throwRestoreOnce, 0) == 1)
            {
                throw new InvalidOperationException("synthetic rollback failure");
            }

            if (BlockRestoreUntilReleased)
            {
                await _restoreRelease.Task.WaitAsync(cancellationToken);
            }

            IsApplied = false;
            return new OperationInvokeResult(true, true, "restored");
        }

        public void ReleaseRestore() => _restoreRelease.TrySetResult();
    }
}
