using System.Text.Json;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class TransactionLogCorruptionTests
{
    [Fact]
    public async Task LegacyRecordWithoutSchemaVersionRemainsReadable()
    {
        var operation = new CorruptionOperation();
        var logPath = CreateLogPath();
        var legacyRecord = new
        {
            OperationId = operation.Id,
            Status = TransactionLogService.CompletedStatus,
            Timestamp = DateTime.UtcNow,
            Error = (string?)null,
            ModuleKey = operation.ModuleKey,
            Intent = "APPLY"
        };
        File.WriteAllText(logPath, JsonSerializer.Serialize(legacyRecord) + Environment.NewLine);

        try
        {
            var transactionLog = new TransactionLogService(logPath);
            var engine = CreateEngine([operation], transactionLog);
            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));

            Assert.False(status.IsPending);
            Assert.True(status.IsKnown);
            Assert.Empty(transactionLog.ReadState().Issues);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task TruncatedJsonIsRecoveryRequiredAndBlocksReentry()
    {
        var operation = new CorruptionOperation();
        var logPath = CreateLogPath();
        File.WriteAllText(
            logPath,
            $"{{\"OperationId\":\"{operation.Id}\",\"ModuleKey\":\"{operation.ModuleKey}\",\"Status\":\"{TransactionLogService.PendingConfirmationStatus}\"");

        try
        {
            var transactionLog = new TransactionLogService(logPath);
            var engine = CreateEngine([operation], transactionLog);

            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));
            Assert.False(status.IsCompliant);
            Assert.True(status.IsPending);
            Assert.True(status.RequiresRecovery);
            Assert.False(status.IsKnown);
            Assert.Contains("could not be verified", status.Details, StringComparison.OrdinalIgnoreCase);

            var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);
            Assert.False(retry.Success);
            Assert.Equal(0, operation.ApplyCalls);
            Assert.Single(transactionLog.ReadState().Issues);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task UnknownSchemaIsRecoveryRequiredAfterRestartAndRetryRemainsBlocked()
    {
        var operation = new CorruptionOperation();
        var logPath = CreateLogPath();
        var record = new
        {
            OperationId = operation.Id,
            ModuleKey = operation.ModuleKey,
            Intent = "APPLY",
            Status = TransactionLogService.PendingConfirmationStatus,
            Timestamp = DateTime.UtcNow,
            SchemaVersion = TransactionLogService.CurrentSchemaVersion + 1
        };
        File.WriteAllText(logPath, JsonSerializer.Serialize(record) + Environment.NewLine);

        try
        {
            var transactionLog = new TransactionLogService(logPath);
            var firstEngine = CreateEngine([operation], transactionLog);
            var firstStatus = Assert.Single(await firstEngine.GetStatusesAsync(operation.ModuleKey));
            Assert.True(firstStatus.IsPending);
            Assert.True(firstStatus.RequiresRecovery);

            var restartedEngine = CreateEngine([operation], new TransactionLogService(logPath));
            var restartedStatus = Assert.Single(await restartedEngine.GetStatusesAsync(operation.ModuleKey));
            Assert.True(restartedStatus.IsPending);
            Assert.True(restartedStatus.RequiresRecovery);
            var retry = await restartedEngine.RestoreAsync(operation.ModuleKey, [operation.Id]);
            Assert.False(retry.Success);
            Assert.Equal(0, operation.RestoreCalls);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task ModuleScopedCorruptionBlocksOnlyAffectedModule()
    {
        var healthy = new CorruptionOperation("healthy");
        var affected = new CorruptionOperation("affected");
        var logPath = CreateLogPath();
        File.WriteAllText(
            logPath,
            $"{{\"OperationId\":\"{affected.Id}\",\"ModuleKey\":\"{affected.ModuleKey}\",\"Status\":\"{TransactionLogService.PendingConfirmationStatus}\"\n");

        try
        {
            var transactionLog = new TransactionLogService(logPath);
            var engine = CreateEngine([healthy, affected], transactionLog);

            var healthyStatus = Assert.Single(await engine.GetStatusesAsync(healthy.ModuleKey));
            Assert.False(healthyStatus.IsPending);
            Assert.True(healthyStatus.IsKnown);

            var healthyApply = await engine.ApplyAsync(healthy.ModuleKey, [healthy.Id]);
            Assert.True(healthyApply.Success);
            Assert.Equal(1, healthy.ApplyCalls);
            var healthyAfterApply = Assert.Single(await engine.GetStatusesAsync(healthy.ModuleKey));
            Assert.False(healthyAfterApply.IsPending);
            Assert.True(healthyAfterApply.IsCompliant);

            var affectedStatus = Assert.Single(await engine.GetStatusesAsync(affected.ModuleKey));
            Assert.True(affectedStatus.IsPending);
            Assert.True(affectedStatus.RequiresRecovery);
            var affectedApply = await engine.ApplyAsync(affected.ModuleKey, [affected.Id]);
            Assert.False(affectedApply.Success);
            Assert.Equal(0, affected.ApplyCalls);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task AtomicWriteFailureDoesNotStartOperationOrLeavePartialRecord()
    {
        var operation = new CorruptionOperation();
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(
            logPath,
            writeFailureFactory: _ => new IOException("synthetic atomic write failure"));

        try
        {
            var engine = CreateEngine([operation], transactionLog);
            var result = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);

            Assert.False(result.Success);
            Assert.Equal(0, operation.ApplyCalls);
            Assert.False(File.Exists(logPath));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(logPath)!, $"{Path.GetFileName(logPath)}.*.tmp"));
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task ReadExceptionFailsClosedForAffectedModuleAndRetryDoesNotClearIt()
    {
        var operation = new CorruptionOperation();
        var logPath = CreateLogPath();
        var transactionLog = new TransactionLogService(
            logPath,
            readFailureFactory: () => new IOException("synthetic transaction log read failure"));

        try
        {
            var engine = CreateEngine([operation], transactionLog);
            var status = Assert.Single(await engine.GetStatusesAsync(operation.ModuleKey));
            Assert.True(status.IsPending);
            Assert.True(status.RequiresRecovery);
            Assert.False(status.IsKnown);

            var retry = await engine.ApplyAsync(operation.ModuleKey, [operation.Id]);
            Assert.False(retry.Success);
            Assert.Equal(0, operation.ApplyCalls);
            Assert.False(transactionLog.ReadState().IsReliable);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    private static OptimizationEngine CreateEngine(
        IReadOnlyList<CorruptionOperation> operations,
        TransactionLogService transactionLog) =>
        new(
            [new CorruptionProvider(operations)],
            new CorruptionRestoreStore(),
            NullLoggerFactory.Instance,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            transactionLog);

    private static string CreateLogPath() =>
        Path.Combine(Path.GetTempPath(), $"yuqi-engine-corrupt-{Guid.NewGuid():N}.log");

    private static void DeleteLog(string logPath)
    {
        try
        {
            if (File.Exists(logPath))
                File.Delete(logPath);

            foreach (var temporaryPath in Directory.GetFiles(
                         Path.GetDirectoryName(logPath)!,
                         $"{Path.GetFileName(logPath)}.*.tmp"))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
            // The test only cleans its isolated temporary evidence files.
        }
    }

    private sealed class CorruptionProvider(IReadOnlyList<CorruptionOperation> operations) : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() =>
            operations
                .GroupBy(operation => operation.ModuleKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => new OptimizationModuleDescriptor(
                    group.Key,
                    group.Key,
                    group.Key,
                    group.ToArray()));
    }

    private sealed class CorruptionRestoreStore : IOperationRestoreStore
    {
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class CorruptionOperation(string moduleSuffix = "corruption") : IOptimizationOperation
    {
        private int _applyCalls;
        private int _restoreCalls;

        public string ModuleKey { get; } = $"test-log-{moduleSuffix}:{Guid.NewGuid():N}";
        public string Id => $"{ModuleKey}:operation";
        public string Title => "Transaction log corruption test";
        public string Description => "Transaction log corruption test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin => false;
        public bool IsApplied { get; private set; }
        public int ApplyCalls => Volatile.Read(ref _applyCalls);
        public int RestoreCalls => Volatile.Read(ref _restoreCalls);

        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationStatus(IsApplied, IsApplied ? "applied" : "original"));

        public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _applyCalls);
            IsApplied = true;
            return Task.FromResult(new OperationInvokeResult(true, true, "applied"));
        }

        public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _restoreCalls);
            IsApplied = false;
            return Task.FromResult(new OperationInvokeResult(true, true, "restored"));
        }
    }
}
