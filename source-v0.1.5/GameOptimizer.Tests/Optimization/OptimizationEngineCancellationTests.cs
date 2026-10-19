using System.Collections.Concurrent;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Optimization;

public sealed class OptimizationEngineCancellationTests
{
    private const string TestOperationId = "test.cancellation";

    [Fact]
    public async Task ApplyCancellationReturnsTerminalFailureAndReleasesModuleForRetry()
    {
        var operation = new CancellationAwareOperation { WaitForApplyCancellation = true };
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();
        using var cancellation = new CancellationTokenSource();

        var applyTask = engine.ApplyAsync("test", [operation.Id], progress, cancellation.Token);
        await operation.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        var cancelled = await applyTask.WaitAsync(TimeSpan.FromSeconds(2));

        var cancelledSummary = Assert.Single(cancelled.Operations);
        Assert.False(cancelledSummary.Success);
        Assert.False(cancelledSummary.Changed);
        Assert.False(cancelledSummary.After.IsCompliant);
        Assert.Contains("verified", cancelledSummary.After.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProgressState.Cancelled, progress.Items.Last().State);
        Assert.Equal(1, operation.RestoreCalls);

        operation.WaitForApplyCancellation = false;
        var retry = await engine.ApplyAsync("test", [operation.Id]);

        var retrySummary = Assert.Single(retry.Operations);
        Assert.True(retrySummary.Success);
        Assert.True(retrySummary.Changed);
    }

    [Fact]
    public async Task RestoreCancellationReturnsUnknownTerminalStateAndReleasesModuleForRetry()
    {
        var operation = new CancellationAwareOperation { IsApplied = true, WaitForRestoreCancellation = true };
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();
        using var cancellation = new CancellationTokenSource();

        var restoreTask = engine.RestoreAsync("test", [operation.Id], progress, cancellation.Token);
        await operation.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        var cancelled = await restoreTask.WaitAsync(TimeSpan.FromSeconds(2));

        var cancelledSummary = Assert.Single(cancelled.Operations);
        Assert.False(cancelledSummary.Success);
        Assert.True(cancelledSummary.Changed);
        Assert.False(cancelledSummary.After.IsCompliant);
        Assert.Contains("verified", cancelledSummary.After.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProgressState.Cancelled, progress.Items.Last().State);

        operation.WaitForRestoreCancellation = false;
        var retry = await engine.RestoreAsync("test", [operation.Id]);

        var retrySummary = Assert.Single(retry.Operations);
        Assert.True(retrySummary.Success);
        Assert.True(retrySummary.Changed);
        Assert.False(operation.IsApplied);
    }

    [Fact]
    public async Task RestoreSafetyBlocksBooleanFalseWhenHistoricalSnapshotExists()
    {
        var operation = new CancellationAwareOperation { IsApplied = false };
        var engine = CreateEngine(operation);

        var result = await engine.RestoreAsync("test", [operation.Id]);

        var summary = Assert.Single(result.Operations);
        Assert.False(summary.Success);
        Assert.False(summary.Changed);
        Assert.Equal(0, operation.RestoreCalls);
        Assert.False(summary.After.IsCompliant);
        Assert.True(summary.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase) || summary.Message.Contains("未知", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreSafetyBlockReportsFailedTerminalProgress()
    {
        var operation = new CancellationAwareOperation { IsApplied = false };
        var engine = CreateEngine(operation);
        var progress = new RecordingProgress();

        var result = await engine.RestoreAsync("test", [operation.Id], progress);

        Assert.False(result.Success);
        Assert.Equal(ProgressState.Failed, progress.Items.Last().State);
        var statusMessage = progress.Items.Last().StatusMessage ?? string.Empty;
        Assert.True(statusMessage.Contains("unknown", StringComparison.OrdinalIgnoreCase) || statusMessage.Contains("未知", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreSafetyBlocksReadbackFailureEncodedAsFalseWhenHistoricalSnapshotExists()
    {
        var operation = new CancellationAwareOperation { EncodeStatusReadFailureAsFalse = true };
        var engine = CreateEngine(operation);

        var result = await engine.RestoreAsync("test", [operation.Id]);

        var summary = Assert.Single(result.Operations);
        Assert.False(summary.Success);
        Assert.False(summary.Changed);
        Assert.Equal(0, operation.RestoreCalls);
        Assert.True(summary.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase) || summary.Message.Contains("未知", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreKeepsNoRestoreNeededForBooleanFalseWithoutHistoricalSnapshot()
    {
        var operation = new CancellationAwareOperation { IsApplied = false };
        var engine = CreateEngine(operation, snapshotExists: false);

        var result = await engine.RestoreAsync("test", [operation.Id]);

        var summary = Assert.Single(result.Operations);
        Assert.True(summary.Success);
        Assert.False(summary.Changed);
        Assert.Equal(0, operation.RestoreCalls);
    }

    [Fact]
    public async Task RestoreKeepsUnknownReadbackBlockedEvenWhenHistoricalSnapshotExists()
    {
        var operation = new CancellationAwareOperation { FailStatusRead = true };
        var engine = CreateEngine(operation);

        var result = await engine.RestoreAsync("test", [operation.Id]);

        var summary = Assert.Single(result.Operations);
        Assert.False(summary.Success);
        Assert.False(summary.Changed);
        Assert.Equal(0, operation.RestoreCalls);
        Assert.True(summary.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase) || summary.Message.Contains("未知", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreSafetyBlockRetainsDurableRecoveryAcrossRetryAndEngineRestart()
    {
        const string recoveryPointer = "durable-receipt-and-recovery-pointer";
        var backing = new TestRestoreStoreBacking();
        var firstStore = new TestRestoreStore(backing);
        await firstStore.SaveSnapshotAsync(TestOperationId, recoveryPointer, CancellationToken.None);
        var firstOperation = new CancellationAwareOperation { EncodeStatusReadFailureAsFalse = true };
        var firstEngine = CreateEngine(firstOperation, firstStore);

        var first = Assert.Single((await firstEngine.RestoreAsync("test", [TestOperationId])).Operations);
        var retry = Assert.Single((await firstEngine.RestoreAsync("test", [TestOperationId])).Operations);

        Assert.False(first.Success);
        Assert.False(retry.Success);
        Assert.False(first.Changed);
        Assert.False(retry.Changed);
        Assert.Equal(0, firstOperation.RestoreCalls);
        Assert.True(await firstStore.HasSnapshotAsync(TestOperationId, CancellationToken.None));
        Assert.Equal(recoveryPointer, await firstStore.LoadSnapshotAsync(TestOperationId, CancellationToken.None));

        var restartedStore = new TestRestoreStore(backing);
        var restartedOperation = new CancellationAwareOperation { EncodeStatusReadFailureAsFalse = true };
        var restartedEngine = CreateEngine(restartedOperation, restartedStore);

        var restartedStatus = Assert.Single(await restartedEngine.GetStatusesAsync("test"));
        var afterRestart = Assert.Single((await restartedEngine.RestoreAsync("test", [TestOperationId])).Operations);

        Assert.False(restartedStatus.IsCompliant);
        Assert.Contains("failed", restartedStatus.Details, StringComparison.OrdinalIgnoreCase);
        Assert.False(afterRestart.Success);
        Assert.False(afterRestart.Changed);
        Assert.False(afterRestart.After.IsCompliant);
        Assert.Equal(0, restartedOperation.RestoreCalls);
        Assert.Equal(0, backing.DeleteCalls);
        Assert.True(await restartedStore.HasSnapshotAsync(TestOperationId, CancellationToken.None));
        Assert.Equal(recoveryPointer, await restartedStore.LoadSnapshotAsync(TestOperationId, CancellationToken.None));
    }

    private static OptimizationEngine CreateEngine(
        CancellationAwareOperation operation,
        bool snapshotExists = true) =>
        CreateEngine(operation, new TestRestoreStore(snapshotExists));

    private static OptimizationEngine CreateEngine(
        CancellationAwareOperation operation,
        IOperationRestoreStore restoreStore) =>
        new(
            [new TestProvider(operation)],
            restoreStore,
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated());

    private sealed class TestProvider(CancellationAwareOperation operation) : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() =>
            [new OptimizationModuleDescriptor("test", "Test", "Test", [operation])];
    }

    private sealed class TestRestoreStoreBacking
    {
        private int _deleteCalls;

        public ConcurrentDictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int DeleteCalls => Volatile.Read(ref _deleteCalls);

        public void RecordDelete() => Interlocked.Increment(ref _deleteCalls);
    }

    private sealed class TestRestoreStore : IOperationRestoreStore
    {
        private readonly TestRestoreStoreBacking _backing;

        public TestRestoreStore(bool snapshotExists)
        {
            _backing = new TestRestoreStoreBacking();
            if (snapshotExists)
            {
                _backing.Values[TestOperationId] = "snapshot";
            }
        }

        public TestRestoreStore(TestRestoreStoreBacking backing)
        {
            _backing = backing;
        }

        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _backing.Values[operationId] = payload;
            return Task.CompletedTask;
        }

        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_backing.Values.TryGetValue(operationId, out var payload) ? payload : null);
        }

        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _backing.RecordDelete();
            _backing.Values.TryRemove(operationId, out _);
            return Task.CompletedTask;
        }

        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_backing.Values.ContainsKey(operationId));
        }
    }

    private sealed class RecordingProgress : IOperationProgress
    {
        public ConcurrentQueue<ProgressInfo> Items { get; } = new();

        public void Report(ProgressInfo progress) => Items.Enqueue(progress);
    }

    private sealed class CancellationAwareOperation : IOptimizationOperation
    {
        public string Id => TestOperationId;
        public string Title => "Cancellation test";
        public string Description => "Cancellation test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Balanced;
        public bool RequiresAdmin => false;
        public bool IsApplied { get; set; }
        public bool FailStatusRead { get; set; }
        public bool EncodeStatusReadFailureAsFalse { get; set; }
        public bool WaitForApplyCancellation { get; set; }
        public bool WaitForRestoreCancellation { get; set; }
        public int RestoreCalls { get; private set; }
        public TaskCompletionSource ApplyStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RestoreStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            if (FailStatusRead)
            {
                throw new InvalidOperationException("status read failed");
            }

            if (EncodeStatusReadFailureAsFalse)
            {
                return Task.FromResult(new OperationStatus(false, "status read failed"));
            }

            return Task.FromResult(new OperationStatus(IsApplied, IsApplied ? "applied" : "not applied"));
        }

        public async Task<OperationInvokeResult> ApplyAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            ApplyStarted.TrySetResult();
            if (WaitForApplyCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            IsApplied = true;
            return new OperationInvokeResult(true, true, "applied");
        }

        public async Task<OperationInvokeResult> RestoreAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            RestoreCalls++;
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
