using GameOptimizer.Core.Services;

namespace GameOptimizer.Tests.Changes;

public sealed class TransactionLogStateMachineTests
{
    [Fact]
    public void OutOfOrderWritesCannotLowerTheEffectiveTransactionState()
    {
        var logPath = CreateLogPath();
        var log = new TransactionLogService(logPath);
        var transactionId = "tx:out-of-order";

        try
        {
            log.FinalizePending("operation", success: true, moduleKey: "module", intent: "APPLY", transactionId: transactionId);
            log.LogPendingConfirmation("operation", "module", "APPLY", "late pending", transactionId);
            log.TryLogStart("operation", "module", "APPLY", transactionId);

            var entry = Assert.Single(log.ReadState().Entries);
            Assert.Equal(TransactionLogService.CompletedStatus, entry.Status);
            Assert.Null(log.GetPendingOperation("operation"));
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public void DuplicateTransactionEventsAreIdempotent()
    {
        var logPath = CreateLogPath();
        var log = new TransactionLogService(logPath);
        var transactionId = "tx:duplicate";

        try
        {
            log.TryLogStart("operation", "module", "APPLY", transactionId);
            log.TryLogStart("operation", "module", "APPLY", transactionId);
            log.LogPendingConfirmation("operation", "module", "APPLY", "pending", transactionId);
            log.LogPendingConfirmation("operation", "module", "APPLY", "pending", transactionId);

            var entries = log.ReadState().Entries;
            Assert.Single(entries);
            Assert.Equal(TransactionLogService.PendingConfirmationStatus, entries[0].Status);
            Assert.Equal(transactionId, entries[0].EffectiveTransactionId);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public void LateCompletionCannotOverwriteRecoveryRequired()
    {
        var logPath = CreateLogPath();
        var log = new TransactionLogService(logPath);
        var transactionId = "tx:recovery";

        try
        {
            log.TryLogStart("operation", "module", "APPLY", transactionId);
            log.LogPendingConfirmation("operation", "module", "APPLY", "pending", transactionId);
            log.MarkRecoveryRequired("operation", "state read failed", "module", "APPLY", transactionId: transactionId);
            log.FinalizePending("operation", success: true, error: "late completion", moduleKey: "module", intent: "APPLY", transactionId: transactionId);
            log.LogRollback("operation", "module", "APPLY", transactionId);

            var entry = Assert.Single(log.ReadState().Entries);
            Assert.Equal(TransactionLogService.RecoveryRequiredStatus, entry.Status);
            Assert.NotNull(log.GetPendingOperation("operation"));
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task ConcurrentLateCompletionAndRecoveryKeepTheRecoveryState()
    {
        var logPath = CreateLogPath();
        var log = new TransactionLogService(logPath);
        var transactionId = "tx:concurrent-late";
        log.TryLogStart("operation", "module", "APPLY", transactionId);
        log.LogPendingConfirmation("operation", "module", "APPLY", "pending", transactionId);

        try
        {
            await Task.WhenAll(
                Task.Run(() => log.FinalizePending(
                    "operation",
                    success: true,
                    error: "late completion",
                    moduleKey: "module",
                    intent: "APPLY",
                    transactionId: transactionId)),
                Task.Run(() => log.MarkRecoveryRequired(
                    "operation",
                    "concurrent status read failed",
                    "module",
                    "APPLY",
                    transactionId: transactionId)),
                Task.Run(() => log.LogRollback("operation", "module", "APPLY", transactionId)));

            var entry = Assert.Single(log.ReadState().Entries);
            Assert.Equal(TransactionLogService.RecoveryRequiredStatus, entry.Status);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    [Fact]
    public async Task ConcurrentRestartReadsKeepRecoveryRequiredAndDoNotReopenTransaction()
    {
        var logPath = CreateLogPath();
        var log = new TransactionLogService(logPath);
        log.TryLogStart("operation", "module", "APPLY", "tx:concurrent");
        log.MarkRecoveryRequired("operation", "recovery required", "module", "APPLY", "tx:concurrent");

        try
        {
            var reads = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(() => new TransactionLogService(logPath).ReadState())));

            Assert.All(reads, result =>
            {
                var entry = Assert.Single(result.Entries);
                Assert.Equal(TransactionLogService.RecoveryRequiredStatus, entry.Status);
                Assert.Empty(result.Issues);
            });
            Assert.Equal(
                TransactionLogService.RecoveryRequiredStatus,
                log.GetPendingOperation("operation")?.Status);
        }
        finally
        {
            DeleteLog(logPath);
        }
    }

    private static string CreateLogPath() =>
        Path.Combine(Path.GetTempPath(), $"yuqi-engine-state-{Guid.NewGuid():N}.log");

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
}
