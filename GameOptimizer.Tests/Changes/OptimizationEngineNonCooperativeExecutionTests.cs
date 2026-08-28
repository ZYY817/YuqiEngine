using System.Collections.Concurrent;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class OptimizationEngineNonCooperativeExecutionTests
{
    [Fact]
    public async Task TimeoutKeepsModuleLockedUntilLateApplyCompletesAndReadbackVerifies()
    {
        var operation = new NonCooperativeOperation { ApplyDelay = TimeSpan.FromSeconds(1) };
        var engine = CreateEngine(operation, TimeSpan.FromMilliseconds(50));
        var progress = new RecordingProgress();

        var timedOut = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress);

        Assert.False(timedOut.Success);
        Assert.True(Assert.Single(timedOut.Operations).Message.Contains("still running", StringComparison.OrdinalIgnoreCase)
            || Assert.Single(timedOut.Operations).Message.Contains("仍在运行", StringComparison.Ordinal));
        Assert.Contains(progress.Items, item => item.State == ProgressState.Running
            && item.StatusMessage?.Contains("pending confirmation", StringComparison.OrdinalIgnoreCase) == true);
        Assert.True(OperationLock.IsLocked($"module:{operation.ModuleKey}"));
        Assert.Equal(1, operation.ApplyCalls);

        using var waiterCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var waiter = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], cancellationToken: waiterCancellation.Token);
        Assert.False(waiter.Success);
        Assert.Equal(1, operation.ApplyCalls);

        await operation.ApplyCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !OperationLock.IsLocked($"module:{operation.ModuleKey}"));
        Assert.Contains(progress.Items, item => item.State == ProgressState.Completed);

        var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);
        Assert.True(retry.Success);
        Assert.Equal(1, operation.ApplyCalls);
    }

    [Fact]
    public async Task CancellationKeepsModuleLockedUntilLateApplyCompletesAndRestoreCannotCompete()
    {
        var operation = new NonCooperativeOperation { ApplyDelay = TimeSpan.FromSeconds(1) };
        var engine = CreateEngine(operation, TimeSpan.FromSeconds(1));
        var progress = new RecordingProgress();
        using var cancellation = new CancellationTokenSource();

        var cancelledTask = engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress, cancellation.Token);
        await operation.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var cancelled = await cancelledTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(cancelled.Success);
        Assert.True(Assert.Single(cancelled.Operations).Message.Contains("still running", StringComparison.OrdinalIgnoreCase)
            || Assert.Single(cancelled.Operations).Message.Contains("仍在运行", StringComparison.Ordinal));
        Assert.True(OperationLock.IsLocked($"module:{operation.ModuleKey}"));

        using var restoreCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var competingRestore = await engine.RestoreAsync(
            operation.ModuleKey,
            [operation.Id],
            cancellationToken: restoreCancellation.Token);
        Assert.False(competingRestore.Success);
        Assert.Equal(0, operation.RestoreCalls);

        await operation.ApplyCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !OperationLock.IsLocked($"module:{operation.ModuleKey}"));
        Assert.Contains(progress.Items, item => item.State == ProgressState.Completed);

        var restoreRetry = await engine.RestoreAsync(operation.ModuleKey, [operation.Id]);
        Assert.True(restoreRetry.Success);
        Assert.Equal(1, operation.RestoreCalls);
        Assert.False(operation.IsApplied);
    }

    [Fact]
    public async Task TimeoutKeepsModuleLockedUntilLateRestoreCompletesAndApplyCannotCompete()
    {
        var operation = new NonCooperativeOperation
        {
            IsApplied = true,
            RestoreDelay = TimeSpan.FromSeconds(1)
        };
        var engine = CreateEngine(operation, TimeSpan.FromMilliseconds(50));
        var progress = new RecordingProgress();

        var timedOut = await engine.RestoreAsync(operation.ModuleKey, [operation.Id], progress);

        Assert.False(timedOut.Success);
        Assert.True(Assert.Single(timedOut.Operations).Message.Contains("still running", StringComparison.OrdinalIgnoreCase)
            || Assert.Single(timedOut.Operations).Message.Contains("仍在运行", StringComparison.Ordinal));
        Assert.True(OperationLock.IsLocked($"module:{operation.ModuleKey}"));
        Assert.Equal(1, operation.RestoreCalls);

        using var waiterCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var competingApply = await engine.ApplyAsync(
            operation.ModuleKey,
            [operation.Id],
            cancellationToken: waiterCancellation.Token);
        Assert.False(competingApply.Success);
        Assert.Equal(0, operation.ApplyCalls);

        await operation.RestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !OperationLock.IsLocked($"module:{operation.ModuleKey}"));
        Assert.Contains(progress.Items, item => item.State == ProgressState.Completed);

        var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);
        Assert.True(retry.Success);
        Assert.Equal(1, operation.ApplyCalls);
    }

    [Fact]
    public async Task LateApplyCompletionUsesFinalReadbackAndDoesNotReportSuccessWhenStateRemainsNonCompliant()
    {
        var operation = new NonCooperativeOperation
        {
            ApplyDelay = TimeSpan.FromSeconds(1),
            ForceStatusNonCompliant = true
        };
        var engine = CreateEngine(operation, TimeSpan.FromMilliseconds(50));
        var progress = new RecordingProgress();

        var timedOut = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress);

        Assert.False(timedOut.Success);
        Assert.True(OperationLock.IsLocked($"module:{operation.ModuleKey}"));

        await operation.ApplyCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !OperationLock.IsLocked($"module:{operation.ModuleKey}"));

        Assert.True(operation.StatusReads >= 2);
        Assert.Contains(progress.Items, item => item.State == ProgressState.Failed);
        Assert.DoesNotContain(progress.Items, item => item.State == ProgressState.Completed);
    }

    private static OptimizationEngine CreateEngine(
        NonCooperativeOperation operation,
        TimeSpan operationTimeout) =>
        new(
            [new TestProvider(operation)],
            new TestRestoreStore(),
            NullLoggerFactory.Instance,
            operationTimeout,
            TimeSpan.FromMilliseconds(50),
            TestTransactionLogFactory.CreateIsolated());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached before the test deadline.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class TestProvider(NonCooperativeOperation operation) : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() =>
            [new(operation.ModuleKey, "Non-cooperative execution", "Non-cooperative execution", [operation])];
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

        public void Report(ProgressInfo progress) => Items.Enqueue(progress);
    }

    private sealed class NonCooperativeOperation : IOptimizationOperation
    {
        private int _applyCalls;
        private int _restoreCalls;
        private int _statusReads;

        public string ModuleKey { get; } = $"test-non-cooperative:{Guid.NewGuid():N}";
        public string Id => $"{ModuleKey}:operation";
        public string Title => "Non-cooperative execution test";
        public string Description => "Non-cooperative execution test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin => false;
        public bool IsApplied { get; set; }
        public bool ForceStatusNonCompliant { get; init; }
        public TimeSpan ApplyDelay { get; init; }
        public TimeSpan RestoreDelay { get; init; }
        public int ApplyCalls => Volatile.Read(ref _applyCalls);
        public int RestoreCalls => Volatile.Read(ref _restoreCalls);
        public int StatusReads => Volatile.Read(ref _statusReads);
        public TaskCompletionSource ApplyStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ApplyCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RestoreCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _statusReads);
            var isApplied = !ForceStatusNonCompliant && IsApplied;
            return Task.FromResult(new OperationStatus(isApplied, isApplied ? "applied" : "not applied"));
        }

        public async Task<OperationInvokeResult> ApplyAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _applyCalls);
            ApplyStarted.TrySetResult();
            await Task.Delay(ApplyDelay);
            IsApplied = true;
            ApplyCompleted.TrySetResult();
            return new OperationInvokeResult(true, true, "applied late");
        }

        public async Task<OperationInvokeResult> RestoreAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _restoreCalls);
            await Task.Delay(RestoreDelay);
            IsApplied = false;
            RestoreCompleted.TrySetResult();
            return new OperationInvokeResult(true, true, "restored late");
        }
    }
}
