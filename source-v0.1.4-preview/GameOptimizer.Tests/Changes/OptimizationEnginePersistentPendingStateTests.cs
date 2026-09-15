using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class OptimizationEnginePersistentPendingStateTests
{
    [Fact]
    public async Task ReconstructedEngineKeepsPersistedPendingVisibleAndBlocksApplyAndRestore()
    {
        var operation = new PersistentOperation
        {
            StatusOverride = new OperationStatus(false, "state unavailable", IsKnown: false)
        };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        transactionLog.LogPendingConfirmation(operation.Id, operation.ModuleKey, "APPLY", "awaiting final confirmation");

        try
        {
            var engine = CreateEngine(operation, transactionLog);

            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));
            Assert.True(status.IsPending);
            Assert.True(status.RequiresRecovery);
            Assert.False(status.IsCompliant);
            Assert.True(status.Details.Contains("Recovery required", StringComparison.OrdinalIgnoreCase) || status.Details.Contains("需要恢复", StringComparison.Ordinal));

            var apply = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);
            var restore = await engine.RestoreAsync(operation.ModuleKey, [operation.Id]);

            Assert.False(apply.Success);
            Assert.False(restore.Success);
            Assert.Equal(0, operation.ApplyCalls);
            Assert.Equal(0, operation.RestoreCalls);
            Assert.NotNull(transactionLog.GetPendingOperation(operation.Id));
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task PendingStatusReadFailureRemainsPendingAndDoesNotPermitReentry()
    {
        var operation = new PersistentOperation { ThrowOnStatusRead = true };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        transactionLog.LogPendingConfirmation(operation.Id, operation.ModuleKey, "RESTORE", "awaiting final confirmation");

        try
        {
            var engine = CreateEngine(operation, transactionLog);

            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));
            Assert.True(status.IsPending);
            Assert.True(status.RequiresRecovery);
            Assert.True(status.Details.Contains("State read failed", StringComparison.OrdinalIgnoreCase) || status.Details.Contains("状态读取失败", StringComparison.Ordinal));

            var restore = await engine.RestoreAsync(operation.ModuleKey, [operation.Id]);

            Assert.False(restore.Success);
            Assert.Equal(0, operation.RestoreCalls);
            Assert.NotNull(transactionLog.GetPendingOperation(operation.Id));
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task LateApplyCompletionFinalizesPersistedStateForReconstructedEngine()
    {
        var operation = new PersistentOperation { ApplyDelay = TimeSpan.FromSeconds(1) };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        var engine = CreateEngine(operation, transactionLog, TimeSpan.FromMilliseconds(50));

        try
        {
            var pending = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);
            Assert.False(pending.Success);
            Assert.NotNull(transactionLog.GetPendingOperation(operation.Id));

            var reconstructed = CreateEngine(operation, transactionLog, TimeSpan.FromMilliseconds(50));
            var pendingStatus = Assert.Single(await reconstructed.GetStatusesAsync(operation.ModuleKey));
            Assert.True(pendingStatus.IsPending);
            Assert.False(pendingStatus.IsCompliant);

            await operation.ApplyCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => !OperationLock.IsLocked($"module:{operation.ModuleKey}"));

            Assert.Null(transactionLog.GetPendingOperation(operation.Id));
            var finalStatus = Assert.Single(await reconstructed.GetStatusesAsync(operation.ModuleKey));
            Assert.False(finalStatus.IsPending);
            Assert.True(finalStatus.IsCompliant);

            var restore = await reconstructed.RestoreAsync(operation.ModuleKey, [operation.Id]);
            Assert.True(restore.Success);
            Assert.Equal(1, operation.RestoreCalls);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task LateRestoreCompletionFinalizesPersistedStateBeforeApplyCanRetry()
    {
        var operation = new PersistentOperation
        {
            IsApplied = true,
            RestoreDelay = TimeSpan.FromSeconds(1)
        };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        var engine = CreateEngine(operation, transactionLog, TimeSpan.FromMilliseconds(50));

        try
        {
            var pending = await engine.RestoreAsync(operation.ModuleKey, [operation.Id]);
            Assert.False(pending.Success);
            Assert.NotNull(transactionLog.GetPendingOperation(operation.Id));

            var reconstructed = CreateEngine(operation, transactionLog, TimeSpan.FromMilliseconds(50));
            using var applyCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var blockedApply = await reconstructed.ApplyAsync(
                operation.ModuleKey,
                [operation.Id],
                applyCancellation.Token);
            Assert.False(blockedApply.Success);
            Assert.Equal(0, operation.ApplyCalls);

            await operation.RestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => !OperationLock.IsLocked($"module:{operation.ModuleKey}"));

            var finalStatus = Assert.Single(await reconstructed.GetStatusesAsync(operation.ModuleKey));
            Assert.False(finalStatus.IsPending);
            Assert.False(finalStatus.IsCompliant);

            var apply = await reconstructed.ApplyAsync(operation.ModuleKey, [operation.Id]);
            Assert.True(apply.Success);
            Assert.Equal(1, operation.ApplyCalls);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task StartupReconciliationClearsPendingWhenApplyTargetIsAlreadyEffective()
    {
        var operation = new PersistentOperation { IsApplied = true };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        transactionLog.LogPendingConfirmation(operation.Id, operation.ModuleKey, "APPLY");

        try
        {
            var engine = CreateEngine(operation, transactionLog);

            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));

            Assert.True(status.IsCompliant);
            Assert.False(status.IsPending);
            Assert.Null(transactionLog.GetPendingOperation(operation.Id));

            var apply = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);
            Assert.True(apply.Success);
            Assert.Equal(0, operation.ApplyCalls);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task StartupReconciliationClearsPendingWhenOriginalStateIsConfirmed()
    {
        var operation = new PersistentOperation { IsApplied = false };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        transactionLog.LogPendingConfirmation(operation.Id, operation.ModuleKey, "RESTORE");

        try
        {
            var engine = CreateEngine(operation, transactionLog);

            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));

            Assert.False(status.IsCompliant);
            Assert.False(status.IsPending);
            Assert.Null(transactionLog.GetPendingOperation(operation.Id));

            var restore = await engine.RestoreAsync(operation.ModuleKey, [operation.Id]);
            Assert.True(restore.Success);
            Assert.Equal(0, operation.RestoreCalls);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task StartupReconciliationRetainsRecoveryWhenResourceIsMissing()
    {
        var operation = new PersistentOperation
        {
            StatusOverride = new OperationStatus(false, "resource missing", IsKnown: false)
        };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        transactionLog.LogPendingConfirmation(operation.Id, operation.ModuleKey, "APPLY");

        try
        {
            var engine = CreateEngine(operation, transactionLog);

            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));

            Assert.True(status.IsPending);
            Assert.True(status.RequiresRecovery);
            Assert.True(status.Details.Contains("Recovery required", StringComparison.OrdinalIgnoreCase) || status.Details.Contains("需要恢复", StringComparison.Ordinal));
            Assert.Equal(TransactionLogService.RecoveryRequiredStatus, transactionLog.GetPendingOperation(operation.Id)?.Status);

            var apply = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);
            Assert.False(apply.Success);
            Assert.Equal(0, operation.ApplyCalls);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task StartupReconciliationRetainsRecoveryWhenStatusReadTimesOut()
    {
        var operation = new PersistentOperation { StatusDelay = TimeSpan.FromMilliseconds(250) };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        transactionLog.LogPendingConfirmation(operation.Id, operation.ModuleKey, "APPLY");

        try
        {
            var engine = CreateEngine(operation, transactionLog);
            var started = DateTime.UtcNow;

            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
            Assert.True(status.IsPending);
            Assert.True(status.RequiresRecovery);
            Assert.True(status.Details.Contains("timed out", StringComparison.OrdinalIgnoreCase) || status.Details.Contains("超时", StringComparison.Ordinal));
            Assert.Equal(TransactionLogService.RecoveryRequiredStatus, transactionLog.GetPendingOperation(operation.Id)?.Status);

            await Task.Delay(operation.StatusDelay + TimeSpan.FromMilliseconds(50));
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task DuplicateStartupReconciliationDoesNotReexecuteOrReopenPendingState()
    {
        var operation = new PersistentOperation { IsApplied = true };
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(logPath);
        transactionLog.LogPendingConfirmation(operation.Id, operation.ModuleKey, "APPLY");

        try
        {
            var firstEngine = CreateEngine(operation, transactionLog);
            var secondEngine = CreateEngine(operation, transactionLog);
            var statuses = await Task.WhenAll(
                firstEngine.GetStatusesAsync(operation.ModuleKey),
                secondEngine.GetStatusesAsync(operation.ModuleKey));

            Assert.All(statuses, result =>
            {
                var status = Assert.Single(result);
                Assert.True(status.IsCompliant);
                Assert.False(status.IsPending);
            });
            Assert.Null(transactionLog.GetPendingOperation(operation.Id));
            Assert.Equal(0, operation.ApplyCalls);
            Assert.Equal(0, operation.RestoreCalls);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    private static OptimizationEngine CreateEngine(
        PersistentOperation operation,
        TransactionLogService transactionLog,
        TimeSpan? operationTimeout = null) =>
        new(
            [new TestProvider(operation)],
            new TestRestoreStore(),
            NullLoggerFactory.Instance,
            operationTimeout ?? TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(50),
            transactionLog);

    private static string CreateLogPath() =>
        Path.Combine(Path.GetTempPath(), $"yuqi-engine-pending-{Guid.NewGuid():N}.log");

    private static void DeleteLog(string logPath)
    {
        try
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
        catch (IOException)
        {
            // The test only cleans up its isolated temporary evidence file.
        }
    }

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

    private sealed class TestProvider(PersistentOperation operation) : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() =>
            [new(operation.ModuleKey, "Persistent pending state", "Persistent pending state", [operation])];
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

    private sealed class PersistentOperation : IOptimizationOperation
    {
        private int _applyCalls;
        private int _restoreCalls;
        private int _statusReads;

        public string ModuleKey { get; } = $"test-persistent-pending:{Guid.NewGuid():N}";
        public string Id => $"{ModuleKey}:operation";
        public string Title => "Persistent pending state test";
        public string Description => "Persistent pending state test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin => false;
        public bool IsApplied { get; set; }
        public bool ThrowOnStatusRead { get; init; }
        public OperationStatus? StatusOverride { get; init; }
        public TimeSpan StatusDelay { get; init; }
        public TimeSpan ApplyDelay { get; init; }
        public TimeSpan RestoreDelay { get; init; }
        public int ApplyCalls => Volatile.Read(ref _applyCalls);
        public int RestoreCalls => Volatile.Read(ref _restoreCalls);
        public int StatusReads => Volatile.Read(ref _statusReads);
        public TaskCompletionSource ApplyCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RestoreCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _statusReads);
            if (ThrowOnStatusRead)
            {
                throw new InvalidOperationException("synthetic status read failure");
            }

            await Task.Delay(StatusDelay);
            return StatusOverride ?? new OperationStatus(IsApplied, IsApplied ? "applied" : "not applied");
        }

        public async Task<OperationInvokeResult> ApplyAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _applyCalls);
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
