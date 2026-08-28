using System.Collections.Concurrent;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Optimization;

public sealed class OptimizationEngineTerminalStateTests
{
    [Fact]
    public async Task ApplySuccessReportsCompletedAndDuplicateApplyCanRun()
    {
        var operation = new TerminalTestOperation();
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();

        var first = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress);
        var duplicate = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress);

        Assert.True(first.Success);
        Assert.True(duplicate.Success);
        Assert.False(duplicate.ChangedCount > 0);
        Assert.Equal(1, operation.ApplyCalls);
        Assert.Equal(ProgressState.Completed, progress.Items.Last().State);
    }

    [Fact]
    public async Task RestoreSuccessReportsCompletedAndDuplicateRestoreCanRun()
    {
        var operation = new TerminalTestOperation { IsApplied = true };
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();

        var first = await engine.RestoreAsync(operation.ModuleKey, [operation.Id], progress);
        var duplicate = await engine.RestoreAsync(operation.ModuleKey, [operation.Id], progress);

        Assert.True(first.Success);
        Assert.True(duplicate.Success);
        Assert.Equal(1, operation.RestoreCalls);
        Assert.Equal(ProgressState.Completed, progress.Items.Last().State);
    }

    [Fact]
    public async Task FailedApplyStillReachesTerminalProgressAndRetrySucceeds()
    {
        var operation = new TerminalTestOperation { ThrowApplyOnce = true };
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();

        var failed = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress);
        Assert.Equal(ProgressState.Failed, progress.Items.Last().State);

        var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress);

        Assert.False(failed.Success);
        Assert.True(retry.Success);
        Assert.Equal(2, operation.ApplyCalls);
        Assert.Equal(ProgressState.Completed, progress.Items.Last().State);
    }

    [Fact]
    public async Task ApplyCancellationThroughPublicEntryCanBeRetried()
    {
        var operation = new TerminalTestOperation { WaitForApplyCancellation = true };
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();
        using var cancellation = new CancellationTokenSource();

        var cancelledTask = engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress, cancellation.Token);
        await operation.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var cancelled = await cancelledTask.WaitAsync(TimeSpan.FromSeconds(2));
        var cancelledSummary = Assert.Single(cancelled.Operations);
        Assert.False(cancelledSummary.Success);
        Assert.Equal(ProgressState.Cancelled, progress.Items.Last().State);

        operation.WaitForApplyCancellation = false;
        var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id], progress);

        Assert.True(retry.Success);
        Assert.Equal(ProgressState.Completed, progress.Items.Last().State);
    }

    [Fact]
    public async Task RestoreCancellationThroughPublicEntryCanBeRetried()
    {
        var operation = new TerminalTestOperation
        {
            IsApplied = true,
            WaitForRestoreCancellation = true
        };
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();
        using var cancellation = new CancellationTokenSource();

        var cancelledTask = engine.RestoreAsync(operation.ModuleKey, [operation.Id], progress, cancellation.Token);
        await operation.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var cancelled = await cancelledTask.WaitAsync(TimeSpan.FromSeconds(2));
        var cancelledSummary = Assert.Single(cancelled.Operations);
        Assert.False(cancelledSummary.Success);
        Assert.True(cancelledSummary.Changed);
        Assert.Equal(ProgressState.Cancelled, progress.Items.Last().State);

        operation.WaitForRestoreCancellation = false;
        var retry = await engine.RestoreAsync(operation.ModuleKey, [operation.Id], progress);

        Assert.True(retry.Success);
        Assert.False(operation.IsApplied);
        Assert.Equal(ProgressState.Completed, progress.Items.Last().State);
    }

    private static OptimizationEngine CreateEngine(TerminalTestOperation operation) =>
        new(
            [new TestProvider(operation)],
            new TestRestoreStore(),
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated());

    private sealed class TestProvider(TerminalTestOperation operation) : IOptimizationOperationProvider
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

    private sealed class RecordingProgress : IOperationProgress
    {
        public ConcurrentQueue<ProgressInfo> Items { get; } = new();

        public void Report(ProgressInfo progress) => Items.Enqueue(progress);
    }

    private sealed class TerminalTestOperation : IOptimizationOperation
    {
        private int _applyCalls;
        private int _restoreCalls;
        private int _throwApplyOnce;

        public string ModuleKey { get; } = $"test-terminal:{Guid.NewGuid():N}";
        public string Id => $"{ModuleKey}:operation";
        public string Title => "Terminal state test";
        public string Description => "Terminal state test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Balanced;
        public bool RequiresAdmin => false;
        public bool IsApplied { get; set; }
        public bool WaitForApplyCancellation { get; set; }
        public bool WaitForRestoreCancellation { get; set; }
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
            if (WaitForRestoreCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            IsApplied = false;
            return new OperationInvokeResult(true, true, "restored");
        }
    }
}
