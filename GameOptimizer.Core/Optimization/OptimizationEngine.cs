using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Core.Security;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Optimization;

public sealed class OptimizationEngine : IOptimizationEngine
{
    private readonly IReadOnlyDictionary<string, OptimizationModuleDescriptor> _modules;
    private readonly IOperationRestoreStore _restoreStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OptimizationEngine> _logger;
    private readonly TransactionLogService _transactionLog;
    private readonly IOptimizationCompatibilityPolicy? _compatibilityPolicy;
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMilliseconds(ProcessTimeouts.LongCommand);
    private static readonly TimeSpan DefaultStatusCheckTimeout = TimeSpan.FromMilliseconds(ProcessTimeouts.StandardCommand);
    private readonly TimeSpan _operationTimeout;
    private readonly TimeSpan _statusCheckTimeout;
    private static readonly TimeSpan CancellationObservationGracePeriod = TimeSpan.FromMilliseconds(100);
    // P2-011: 根据CPU核心数动态调整并行度
    private static readonly int MaxParallelOperations = Math.Max(2, Math.Min(Environment.ProcessorCount / 2, 8));
    // P2-012: 增加锁超时时间
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    private sealed class OperationExecutionLifecycle
    {
        private int _operationStarted;

        public OperationExecutionLifecycle(string transactionId)
        {
            TransactionId = transactionId;
        }

        public string TransactionId { get; }

        public bool OperationStarted => Volatile.Read(ref _operationStarted) != 0;

        public void MarkOperationStarted() => Interlocked.Exchange(ref _operationStarted, 1);
    }

    private sealed record OperationExecutionAttempt(
        OperationExecutionSummary Summary,
        Task<OperationExecutionSummary>? CompletionTask = null,
        string? TransactionId = null);

    private sealed record PendingOperationCompletion(
        IOptimizationOperation Operation,
        string TransactionId,
        Task<OperationExecutionSummary> CompletionTask);

    private sealed record OperationTransaction(
        IOptimizationOperation Operation,
        string TransactionId);

    private sealed record StatusReadResult(
        OperationStatus Status,
        bool IsReliable,
        string? Error = null);

    private static readonly SemaphoreSlim PendingReconciliationGate = new(1, 1);
    public OptimizationEngine(
        IEnumerable<IOptimizationOperationProvider> providers,
        IOperationRestoreStore restoreStore,
        ILoggerFactory loggerFactory,
        TimeSpan? operationTimeout = null,
        TimeSpan? statusCheckTimeout = null,
        TransactionLogService? transactionLog = null,
        IOptimizationCompatibilityPolicy? compatibilityPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(restoreStore);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _operationTimeout = ValidateTimeout(operationTimeout ?? DefaultOperationTimeout, nameof(operationTimeout));
        _statusCheckTimeout = ValidateTimeout(statusCheckTimeout ?? DefaultStatusCheckTimeout, nameof(statusCheckTimeout));
        var providerList = providers.ToArray();
        if (providerList.Length != 1 || providerList[0] is null)
        {
            throw new InvalidOperationException(
                $"OptimizationEngine requires exactly one non-null canonical operation provider, but found {providerList.Length} entries.");
        }

        if (providerList[0] is BuiltInOptimizationOperationProvider && compatibilityPolicy is null)
        {
            throw new InvalidOperationException(
                "Built-in optimization catalog requires the centralized compatibility policy.");
        }

        _restoreStore = restoreStore;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<OptimizationEngine>();
        _transactionLog = transactionLog ?? new TransactionLogService();
        _compatibilityPolicy = compatibilityPolicy;
        var modules = providerList[0].GetModules().ToArray();
        var duplicateModuleKeys = modules
            .GroupBy(module => module.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateModuleKeys.Length > 0)
        {
            throw new InvalidOperationException(
                $"Canonical optimization catalog contains duplicate module keys: {string.Join(", ", duplicateModuleKeys)}.");
        }

        _modules = modules.ToDictionary(module => module.Key, StringComparer.OrdinalIgnoreCase);
        
        CheckPendingOperations();
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, timeout, "Timeout must be greater than zero.");
        }

        return timeout;
    }

    private void CheckPendingOperations()
    {
        try
        {
            var pending = _transactionLog.GetPendingOperations();
            if (pending.Count > 0)
            {
                var loc = LocalizationService.Instance;
                _logger.LogWarning(loc.Get("Engine.PendingOperations", pending.Count));
                foreach (var op in pending)
                {
                    _logger.LogWarning(
                        "  - {OperationId} {Intent} started at {Timestamp}; state remains pending confirmation",
                        op.OperationId,
                        op.Intent ?? "operation",
                        op.Timestamp);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read persisted optimization execution state; future writes will fail closed");
        }
    }

    public IReadOnlyList<OptimizationModuleDescriptor> Modules => _modules.Values.ToList();

    // File: GameOptimizer.Core/Optimization/OptimizationEngine.cs
    // Why: 每个 GetStatusAsync 必须有独立超时，防止单个卡死的Operation拖住整个模块状态检测
    public async Task<IReadOnlyList<OperationStatus>> GetStatusesAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        var module = GetModule(moduleKey);
        var reconciliationObservations = await ReconcilePendingTransactionsAsync(module, cancellationToken).ConfigureAwait(false);
        if (!TryGetPendingTransactions(module, out var pendingTransactions, out var pendingStateError))
        {
            var message = BuildPendingStateUnavailableMessage(pendingStateError);
            _logger.LogError(pendingStateError, "Persisted optimization state is unavailable for module {ModuleKey}", module.Key);
            return module.Operations
                .Select(_ => CreatePendingStatus(null, null, message))
                .ToArray();
        }

        var pendingByOperationId = pendingTransactions
            .GroupBy(entry => entry.OperationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var unmatchedPending = pendingTransactions.FirstOrDefault(
            entry => !module.Operations.Any(
                operation => string.Equals(operation.Id, entry.OperationId, StringComparison.OrdinalIgnoreCase)));
        if (unmatchedPending is not null)
        {
            return module.Operations
                .Select(_ => CreatePendingStatus(
                    unmatchedPending,
                    null,
                    "The pending operation is not present in the current catalog."))
                .ToArray();
        }

        var tasks = module.Operations.Select(async op =>
        {
            pendingByOperationId.TryGetValue(op.Id, out var pendingTransaction);
            var compatibility = await ProbeCompatibilityAsync(module.Key, op, cancellationToken).ConfigureAwait(false);
            var metadata = compatibility is null
                ? await DescribeCompatibilityAsync(module.Key, op, cancellationToken).ConfigureAwait(false)
                : null;
            var read = compatibility is not null
                ? new StatusReadResult(compatibility, IsReliable: true)
                : reconciliationObservations.TryGetValue(op.Id, out var observedRead)
                    ? observedRead
                    : await ReadStatusAsync(op, cancellationToken).ConfigureAwait(false);
            if (metadata is not null)
            {
                read = read with { Status = AttachCompatibilityMetadata(read.Status, metadata) };
            }
            return pendingTransaction is null
                ? read.Status
                : CreatePendingStatus(pendingTransaction, read.Status, read.Error);
        });
        var statuses = await Task.WhenAll(tasks).ConfigureAwait(false);
        return statuses;
    }

    public async Task<OptimizationBatchResult> ApplyAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        var module = GetModule(moduleKey);
        return await ApplyAsyncInternal(module, module.Operations, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OptimizationBatchResult> ApplyAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default)
    {
        var module = GetModule(moduleKey);
        var operations = FilterOperations(module, operationIds);
        return await ApplyAsyncInternal(module, operations, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OptimizationBatchResult> ApplyAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default)
    {
        var module = GetModule(moduleKey);
        var operations = FilterOperations(module, operationIds);
        return await ApplyAsyncInternal(module, operations, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OptimizationBatchResult> RestoreAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        var module = GetModule(moduleKey);
        return await RestoreAsyncInternal(module, module.Operations, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default)
    {
        var module = GetModule(moduleKey);
        var operations = FilterOperations(module, operationIds);
        return await RestoreAsyncInternal(module, operations, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default)
    {
        var module = GetModule(moduleKey);
        var operations = FilterOperations(module, operationIds);
        return await RestoreAsyncInternal(module, operations, progress, cancellationToken).ConfigureAwait(false);
    }

    private OptimizationModuleDescriptor GetModule(string moduleKey)
    {
        Guard.NotNullOrWhiteSpace(moduleKey, nameof(moduleKey));
        if (!_modules.TryGetValue(moduleKey, out var module))
        {
            throw new InvalidOperationException($"Unknown module: {moduleKey}");
        }
        return module;
    }

    private OperationExecutionContext CreateContext(IOptimizationOperation operation)
    {
        var logger = _loggerFactory.CreateLogger(operation.GetType());
        return new OperationExecutionContext(_restoreStore, logger);
    }

    private static IReadOnlyList<IOptimizationOperation> FilterOperations(OptimizationModuleDescriptor module, IEnumerable<string> operationIds)
    {
        ArgumentNullException.ThrowIfNull(operationIds);
        var targetSet = new HashSet<string>(operationIds, StringComparer.OrdinalIgnoreCase);
        return module.Operations.Where(op => targetSet.Contains(op.Id)).ToList();
    }

    // File: GameOptimizer.Core/Optimization/OptimizationEngine.cs
    private async Task<OptimizationBatchResult> ApplyAsyncInternal(
        OptimizationModuleDescriptor module,
        IEnumerable<IOptimizationOperation> operations,
        IOperationProgress? progress,
        CancellationToken cancellationToken)
    {
        var operationList = operations.ToList();
        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCancelledBatchResult(module, operationList, progress);
        }

        if (operationList.Count == 0)
        {
            return CreateEmptySelectionBatchResult(module, progress);
        }

        // 获取模块级锁，防止同一模块并发执行
        var lockKey = $"module:{module.Key}";
        var moduleIsBusy = OperationLock.IsLocked(lockKey);
        if (!moduleIsBusy)
        {
            await ReconcilePendingTransactionsAsync(module, cancellationToken).ConfigureAwait(false);
        }

        if (!TryGetPendingTransactions(module, out var pendingTransactions, out var pendingStateError))
        {
            if (!moduleIsBusy)
            {
                return CreatePendingStateUnavailableResult(module, operationList, progress, pendingStateError);
            }
        }
        else if (!moduleIsBusy && pendingTransactions.Count > 0)
        {
            return CreatePersistedPendingResult(module, operationList, progress, pendingTransactions, "apply");
        }

        var moduleLock = await OperationLock.TryAcquireAsync(lockKey, LockTimeout, cancellationToken);
        if (moduleLock == null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateCancelledBatchResult(module, operationList, progress);
            }

            var loc = LocalizationService.Instance;
            _logger.LogWarning("Module {ModuleKey} is already being processed (apply)", module.Key);
            var conflict = new OptimizationBatchResult(module.Key, new List<OperationExecutionSummary>
            {
                new("", loc.Get("Engine.ConcurrencyConflict"), false, false, loc.Get("Engine.ModuleBusy"), new OperationStatus(false, loc.Get("Engine.Locked"), false), new OperationStatus(false, loc.Get("Engine.Locked"), false))
            });
            progress?.Report(ProgressInfo.Failed(module.Key, module.Title, loc.Get("Engine.ModuleBusy")));
            return conflict;
        }

        try
        {
            var totalCount = operationList.Count;
            _logger.LogInformation("Module {ModuleKey} applying {Count} operations", module.Key, totalCount);

            progress?.Report(ProgressInfo.Starting(module.Key, module.Title, totalCount));

            var summaries = new ConcurrentBag<OperationExecutionSummary>();
            var successfulOperations = new ConcurrentBag<OperationTransaction>();
            var pendingOperations = new ConcurrentBag<PendingOperationCompletion>();
            var completedCount = 0;

            // 分批并行执行，每批最多 MaxParallelOperations 个
            var batches = operationList
                .Select((op, idx) => (Operation: op, Index: idx))
                .GroupBy(x => x.Index / MaxParallelOperations)
                .Select(g => g.Select(x => x.Operation).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    AddCancelledSummaries(operationList, summaries);
                    break;
                }

                var tasks = batch.Select(async operation =>
                {
                    var attempt = await ExecuteApplyOperationAsync(module.Key, operation, cancellationToken).ConfigureAwait(false);
                    var summary = attempt.Summary;
                    summaries.Add(summary);

                    if (attempt.CompletionTask is not null)
                    {
                        pendingOperations.Add(new PendingOperationCompletion(
                            operation,
                            attempt.TransactionId ?? operation.Id,
                            attempt.CompletionTask));
                    }
                    else if (summary.Changed)
                    {
                        successfulOperations.Add(new OperationTransaction(
                            operation,
                            attempt.TransactionId ?? operation.Id));
                    }

                    var current = Interlocked.Increment(ref completedCount);
                    ReportProgressSafely(progress, ProgressInfo.Running(module.Key, module.Title, current, totalCount, operation.Title));
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                AddCancelledSummaries(operationList, summaries);
            }

            // Why: ConcurrentBag 不保证顺序，按原始操作列表顺序排序保证 UI 一致性
            var resultList = summaries.OrderBy(s => operationList.FindIndex(op => op.Id == s.OperationId)).ToList();
            var failures = resultList.Count(s => !s.Success);
            var changedCount = resultList.Count(s => s.Changed);

            if (pendingOperations.Count > 0)
            {
                var pending = pendingOperations.ToArray();
                ReportProgressSafely(progress, ProgressInfo.Running(
                    module.Key,
                    module.Title,
                    completedCount,
                    totalCount,
                    "An underlying operation is still running; final state is pending confirmation."));

                var pendingLock = moduleLock;
                moduleLock = null;
                _ = CompletePendingBatchAsync(
                    module,
                    operationList,
                    resultList,
                    pending,
                    successfulOperations.ToArray(),
                    pendingLock,
                    progress,
                    "APPLY");
                return new OptimizationBatchResult(module.Key, resultList);
            }

            // P0-001/P0-008: 事务回滚 - 如果有失败且有成功的操作，尝试回滚
            if (failures > 0 && successfulOperations.Count > 0)
            {
                _logger.LogWarning("Module {ModuleKey} has {FailureCount} failures, attempting rollback of {SuccessCount} successful operations",
                    module.Key, failures, successfulOperations.Count);

                progress?.Report(ProgressInfo.Running(module.Key, module.Title, completedCount, totalCount, LocalizationService.Instance.Get("Engine.RollingBack")));

                // Recovery must not inherit a caller cancellation after a partial apply. Otherwise
                // already changed operations remain applied while the caller receives no terminal batch result.
                var rollbackToken = cancellationToken.IsCancellationRequested
                    ? CancellationToken.None
                    : cancellationToken;
                var rollbackResults = await RollbackOperationsAsync(module.Key, successfulOperations.ToList(), rollbackToken).ConfigureAwait(false);

                foreach (var rollbackResult in rollbackResults)
                {
                    if (!rollbackResult.Success)
                    {
                        _logger.LogError("Rollback failed for operation {OperationId}: {Message}",
                            rollbackResult.OperationId, rollbackResult.Message);
                    }
                }

                resultList = MergeRollbackResults(resultList, rollbackResults);
            }

            if (failures > 0)
                _logger.LogWarning("Module {ModuleKey} completed with {FailureCount} failures, {ChangedCount} operations changed out of {TotalCount} selected",
                    module.Key, failures, changedCount, totalCount);
            else
                _logger.LogInformation("Module {ModuleKey} completed successfully, {ChangedCount} operations changed out of {TotalCount} selected",
                    module.Key, changedCount, totalCount);

            ReportTerminalProgress(module, resultList, progress);
            return new OptimizationBatchResult(module.Key, resultList);
        }
        finally
        {
            moduleLock?.Dispose();
        }
    }

    private async Task<List<OperationExecutionSummary>> RollbackOperationsAsync(
        string moduleKey,
        IReadOnlyList<OperationTransaction> operations,
        CancellationToken cancellationToken)
    {
        var results = new List<OperationExecutionSummary>();
        
        foreach (var transaction in operations)
        {
            var operation = transaction.Operation;
            try
            {
                var context = CreateContext(operation);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_operationTimeout);

                // Why: GetStatusAsync 也需要超时保护，防止回滚路径卡死
                using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                statusCts.CancelAfter(_statusCheckTimeout);
                
                OperationStatus before;
                try
                {
                    before = await operation.GetStatusAsync(statusCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("[OptimizationEngine][Rollback] GetStatusAsync timed out for {OperationId}, using default", operation.Id);
                    before = new OperationStatus(false, LocalizationService.Instance.Get("Engine.StatusCheckTimeout"));
                }

                var result = await operation.RestoreAsync(context, timeoutCts.Token).ConfigureAwait(false);

                OperationStatus after = before;
                if (result.Changed)
                {
                    using var afterCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    afterCts.CancelAfter(_statusCheckTimeout);
                    try
                    {
                        after = await operation.GetStatusAsync(afterCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("[OptimizationEngine][Rollback] Post-restore GetStatusAsync timed out for {OperationId}", operation.Id);
                        after = new OperationStatus(false, LocalizationService.Instance.Get("Engine.StatusCheckTimeout"));
                    }
                }
                
                _transactionLog.LogRollback(
                    operation.Id,
                    moduleKey,
                    "APPLY",
                    transaction.TransactionId);
                
                results.Add(new OperationExecutionSummary(
                    operation.Id, operation.Title, result.Success, result.Changed,
                    result.Message, before, after));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var loc = LocalizationService.Instance;
                _logger.LogWarning("[OptimizationEngine][Rollback] Operation {OperationId} timed out during rollback", operation.Id);
                results.Add(new OperationExecutionSummary(
                    operation.Id, operation.Title, Success: false, Changed: false,
                    Message: loc.Get("Engine.RestoreTimeout"),
                    Before: new OperationStatus(false, loc.Get("Engine.Unknown")),
                    After: new OperationStatus(false, loc.Get("Engine.Unknown"))));
            }
            catch (Exception ex)
            {
                var loc = LocalizationService.Instance;
                _logger.LogError(ex, "[OptimizationEngine][Rollback] Failed for {ModuleKey}/{OperationId}", moduleKey, operation.Id);
                results.Add(new OperationExecutionSummary(
                    operation.Id, operation.Title, Success: false, Changed: false,
                    Message: loc.Get("Engine.RollbackFailed", ex.Message), 
                    Before: new OperationStatus(false, loc.Get("Engine.Unknown")), 
                    After: new OperationStatus(false, loc.Get("Engine.Unknown")),
                    Exception: ex));
            }
        }
        
        return results;
    }

    private async Task<OperationExecutionAttempt> ExecuteApplyOperationAsync(
        string moduleKey, IOptimizationOperation operation, CancellationToken cancellationToken)
    {
        var lifecycle = new OperationExecutionLifecycle(CreateTransactionId());
        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionCts.CancelAfter(_operationTimeout);
        var executionToken = executionCts.Token;
        var executionTask = ExecuteApplyOperationCoreAsync(
            moduleKey,
            operation,
            cancellationToken,
            executionToken,
            lifecycle);
        var trackedTask = DisposeCancellationSourceAfterAsync(executionTask, executionCts);

        try
        {
            var summary = await trackedTask.WaitAsync(executionToken).ConfigureAwait(false);
            return new OperationExecutionAttempt(
                RecordTerminalExecution(operation, moduleKey, "APPLY", summary, lifecycle.TransactionId),
                TransactionId: lifecycle.TransactionId);
        }
        catch (OperationCanceledException)
        {
            if (!trackedTask.IsCompleted)
            {
                var completed = await Task.WhenAny(
                    trackedTask,
                    Task.Delay(CancellationObservationGracePeriod)).ConfigureAwait(false);
                if (completed != trackedTask)
                {
                    var pending = CreatePendingExecutionSummary(
                        operation,
                        lifecycle.OperationStarted,
                        cancellationToken.IsCancellationRequested);
                    _transactionLog.LogPendingConfirmation(
                        operation.Id,
                        moduleKey,
                        "APPLY",
                        pending.Message,
                        lifecycle.TransactionId);
                    return new OperationExecutionAttempt(
                        pending,
                        trackedTask,
                        lifecycle.TransactionId);
                }
            }

            var summary = await trackedTask.ConfigureAwait(false);
            return new OperationExecutionAttempt(
                RecordTerminalExecution(operation, moduleKey, "APPLY", summary, lifecycle.TransactionId),
                TransactionId: lifecycle.TransactionId);
        }
    }

    private async Task<OperationExecutionSummary> ExecuteApplyOperationCoreAsync(
        string moduleKey,
        IOptimizationOperation operation,
        CancellationToken callerCancellationToken,
        CancellationToken executionToken,
        OperationExecutionLifecycle lifecycle)
    {
        var loc = LocalizationService.Instance;
        var defaultStatus = new OperationStatus(false, loc.Get("Engine.Unknown"), false);
        OperationStatus before = defaultStatus;
        var operationStarted = false;

        var compatibility = await ProbeCompatibilityAsync(moduleKey, operation, executionToken).ConfigureAwait(false);
        if (compatibility is not null)
        {
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: compatibility.Details, Before: compatibility, After: compatibility);
        }

        if (IsFailClosedCatalogOperation(operation))
        {
            var deniedMessage = loc.Get("Engine.FailClosedApply");
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: deniedMessage, Before: defaultStatus, After: defaultStatus);
        }

        if (operation.RequiresAdmin && !SystemGuard.IsAdministrator)
        {
            _logger.LogWarning("Operation {OperationId} requires admin but running as normal user", operation.Id);
            // Why: 已提权时不得再返回「请以管理员身份运行」；未提权时给出可操作指引。
            var elevationGate = PermissionChecker.EvaluateElevationGate(
                operation.Id,
                SystemGuard.IsAdministrator,
                SystemGuard.IsAdministratorAccount);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: elevationGate.Message,
                Before: defaultStatus, After: defaultStatus);
        }
        
        if (!_transactionLog.TryLogStart(operation.Id, moduleKey, "APPLY", lifecycle.TransactionId))
        {
            var logUnavailableMessage = loc.Get("Engine.TransactionUnavailableApply");
            _logger.LogError("[OptimizationEngine][Apply] Could not persist transaction start for {OperationId}; operation blocked", operation.Id);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: logUnavailableMessage, Before: defaultStatus, After: defaultStatus);
        }
        
        try
        {
            using var beforeStatusCts = CancellationTokenSource.CreateLinkedTokenSource(executionToken);
            beforeStatusCts.CancelAfter(_statusCheckTimeout);
            try
            {
                before = await operation.GetStatusAsync(beforeStatusCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
            {
                var preReadTimeoutMessage = loc.Get("Engine.PreApplyVerificationTimeout");
                _logger.LogWarning("[OptimizationEngine][Apply] Pre-apply GetStatusAsync timed out for {OperationId}; apply blocked", operation.Id);
                return new OperationExecutionSummary(
                    operation.Id, operation.Title, Success: false, Changed: false,
                    Message: preReadTimeoutMessage, Before: defaultStatus, After: defaultStatus);
            }

            if (before.IsCompliant)
            {
                return new OperationExecutionSummary(
                    operation.Id, operation.Title, Success: true, Changed: false,
                    Message: loc.Get("Engine.AlreadyCompliant"), Before: before, After: before);
            }

            var context = CreateContext(operation);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(executionToken);
            timeoutCts.CancelAfter(_operationTimeout);
            operationStarted = true;
            lifecycle.MarkOperationStarted();
            var result = await operation.ApplyAsync(context, timeoutCts.Token).ConfigureAwait(false);

            var after = before;
            var verifiedSuccess = false;
            if (result.Success || result.Changed)
            {
                using var afterStatusCts = new CancellationTokenSource();
                afterStatusCts.CancelAfter(_statusCheckTimeout);
                try
                {
                    after = await operation.GetStatusAsync(afterStatusCts.Token).ConfigureAwait(false);
                    verifiedSuccess = result.Success && after.IsCompliant;
                }
                catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("[OptimizationEngine][Apply] Post-apply GetStatusAsync timed out for {OperationId}; result is unverified", operation.Id);
                    after = new OperationStatus(false, loc.Get("Engine.PostApplyVerificationTimeout"), false);
                }
            }

            var message = result.Message;
            if (result.Success && !verifiedSuccess)
            {
                message = string.IsNullOrWhiteSpace(message)
                    ? loc.Get("Engine.VerificationFailed")
                    : $"{message} {loc.Get("Engine.VerificationFailedSuffix")}";
            }

            return new OperationExecutionSummary(
                operation.Id, operation.Title, verifiedSuccess, result.Changed,
                message, before, after,
                Exception: null,
                Diagnostic: result.Diagnostic,
                RepairOptions: result.RepairOptions);
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Module {ModuleKey} - {OperationId} timed out; write operations are never retried automatically", moduleKey, operation.Id);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: loc.Get("Engine.Timeout"), Before: before, After: before,
                Exception: null, Diagnostic: null, RepairOptions: null);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            var message = LocalizationService.Instance.Get("Dashboard.UserCancelled");
            var unknown = new OperationStatus(false, $"{message} Final state was not verified.");
            _logger.LogWarning("Module {ModuleKey} - {OperationId} was cancelled; final state remains unverified", moduleKey, operation.Id);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: operationStarted,
                Message: $"{message} Final state was not verified.", Before: before, After: unknown,
                Exception: null, Diagnostic: null, RepairOptions: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {ModuleKey} - {OperationId} failed", moduleKey, operation.Id);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: ex.Message, Before: before, After: before, Exception: ex,
                Diagnostic: null, RepairOptions: null);
        }
    }

    // File: GameOptimizer.Core/Optimization/OptimizationEngine.cs
    private async Task<OptimizationBatchResult> RestoreAsyncInternal(
        OptimizationModuleDescriptor module,
        IEnumerable<IOptimizationOperation> operations,
        IOperationProgress? progress,
        CancellationToken cancellationToken)
    {
        var operationList = operations.ToList();
        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCancelledBatchResult(module, operationList, progress);
        }

        if (operationList.Count == 0)
        {
            return CreateEmptySelectionBatchResult(module, progress);
        }

        // 获取模块级锁，防止同一模块并发执行 (CORE-005: 增加超时时间)
        var lockKey = $"module:{module.Key}";
        var moduleIsBusy = OperationLock.IsLocked(lockKey);
        if (!moduleIsBusy)
        {
            await ReconcilePendingTransactionsAsync(module, cancellationToken).ConfigureAwait(false);
        }

        if (!TryGetPendingTransactions(module, out var pendingTransactions, out var pendingStateError))
        {
            if (!moduleIsBusy)
            {
                return CreatePendingStateUnavailableResult(module, operationList, progress, pendingStateError);
            }
        }
        else if (!moduleIsBusy && pendingTransactions.Count > 0)
        {
            return CreatePersistedPendingResult(module, operationList, progress, pendingTransactions, "restore");
        }

        // A restore request that arrived while another action was running must
        // not re-evaluate the post-action state as a successful no-op.
        var restoreStartedWhileBusy = moduleIsBusy;
        var moduleLock = await OperationLock.TryAcquireAsync(lockKey, LockTimeout, cancellationToken);
        if (moduleLock == null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateCancelledBatchResult(module, operationList, progress);
            }

            return CreateConcurrencyConflictResult(module, progress, "restore");
        }

        try
        {
            if (restoreStartedWhileBusy)
            {
                return CreateConcurrencyConflictResult(module, progress, "restore");
            }

            var totalCount = operationList.Count;
            _logger.LogInformation("Module {ModuleKey} restoring {Count} operations", module.Key, totalCount);

            progress?.Report(ProgressInfo.Starting(module.Key, module.Title, totalCount));

            var summaries = new ConcurrentBag<OperationExecutionSummary>();
            var pendingOperations = new ConcurrentBag<PendingOperationCompletion>();
            var completedCount = 0;

            // 分批并行执行
            var batches = operationList
                .Select((op, idx) => (Operation: op, Index: idx))
                .GroupBy(x => x.Index / MaxParallelOperations)
                .Select(g => g.Select(x => x.Operation).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    AddCancelledSummaries(operationList, summaries);
                    break;
                }

                var tasks = batch.Select(async operation =>
                {
                    var attempt = await ExecuteRestoreOperationAsync(module.Key, operation, cancellationToken).ConfigureAwait(false);
                    var summary = attempt.Summary;
                    summaries.Add(summary);

                    if (attempt.CompletionTask is not null)
                    {
                        pendingOperations.Add(new PendingOperationCompletion(
                            operation,
                            attempt.TransactionId ?? operation.Id,
                            attempt.CompletionTask));
                    }

                    var current = Interlocked.Increment(ref completedCount);
                    ReportProgressSafely(progress, ProgressInfo.Running(module.Key, module.Title, current, totalCount, operation.Title));
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                AddCancelledSummaries(operationList, summaries);
            }

            // Why: ConcurrentBag 不保证顺序，按原始操作列表顺序排序保证 UI 一致性
            var resultList = summaries.OrderBy(s => operationList.FindIndex(op => op.Id == s.OperationId)).ToList();
            var failures = resultList.Count(s => !s.Success);

            if (pendingOperations.Count > 0)
            {
                var pending = pendingOperations.ToArray();
                ReportProgressSafely(progress, ProgressInfo.Running(
                    module.Key,
                    module.Title,
                    completedCount,
                    totalCount,
                    "An underlying operation is still running; final state is pending confirmation."));

                var pendingLock = moduleLock;
                moduleLock = null;
                _ = CompletePendingBatchAsync(
                    module,
                    operationList,
                    resultList,
                    pending,
                    Array.Empty<OperationTransaction>(),
                    pendingLock,
                    progress,
                    "RESTORE");
                return new OptimizationBatchResult(module.Key, resultList);
            }

            if (failures > 0)
                _logger.LogWarning("Module {ModuleKey} restore completed with {FailureCount} failures", module.Key, failures);
            else
                _logger.LogInformation("Module {ModuleKey} restore completed successfully", module.Key);

            ReportTerminalProgress(module, resultList, progress);
            return new OptimizationBatchResult(module.Key, resultList);
        }
        finally
        {
            moduleLock?.Dispose();
        }
    }

    private async Task<OperationExecutionAttempt> ExecuteRestoreOperationAsync(
        string moduleKey, IOptimizationOperation operation, CancellationToken cancellationToken)
    {
        var lifecycle = new OperationExecutionLifecycle(CreateTransactionId());
        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionCts.CancelAfter(_operationTimeout);
        var executionToken = executionCts.Token;
        var executionTask = ExecuteRestoreOperationCoreAsync(
            moduleKey,
            operation,
            cancellationToken,
            executionToken,
            lifecycle);
        var trackedTask = DisposeCancellationSourceAfterAsync(executionTask, executionCts);

        try
        {
            var summary = await trackedTask.WaitAsync(executionToken).ConfigureAwait(false);
            return new OperationExecutionAttempt(
                RecordTerminalExecution(operation, moduleKey, "RESTORE", summary, lifecycle.TransactionId),
                TransactionId: lifecycle.TransactionId);
        }
        catch (OperationCanceledException)
        {
            if (!trackedTask.IsCompleted)
            {
                var completed = await Task.WhenAny(
                    trackedTask,
                    Task.Delay(CancellationObservationGracePeriod)).ConfigureAwait(false);
                if (completed != trackedTask)
                {
                    var pending = CreatePendingExecutionSummary(
                        operation,
                        lifecycle.OperationStarted,
                        cancellationToken.IsCancellationRequested);
                    _transactionLog.LogPendingConfirmation(
                        operation.Id,
                        moduleKey,
                        "RESTORE",
                        pending.Message,
                        lifecycle.TransactionId);
                    return new OperationExecutionAttempt(
                        pending,
                        trackedTask,
                        lifecycle.TransactionId);
                }
            }

            var summary = await trackedTask.ConfigureAwait(false);
            return new OperationExecutionAttempt(
                RecordTerminalExecution(operation, moduleKey, "RESTORE", summary, lifecycle.TransactionId),
                TransactionId: lifecycle.TransactionId);
        }
    }

    private async Task<OperationExecutionSummary> ExecuteRestoreOperationCoreAsync(
        string moduleKey,
        IOptimizationOperation operation,
        CancellationToken callerCancellationToken,
        CancellationToken executionToken,
        OperationExecutionLifecycle lifecycle)
    {
        var loc = LocalizationService.Instance;
        var defaultStatus = new OperationStatus(false, loc.Get("Engine.Unknown"), false);
        OperationStatus before = defaultStatus;
        var snapshotExists = false;
        var operationStarted = false;

        var compatibility = await ProbeCompatibilityAsync(moduleKey, operation, executionToken).ConfigureAwait(false);
        if (compatibility is not null)
        {
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: compatibility.Details, Before: compatibility, After: compatibility);
        }

        if (IsFailClosedCatalogOperation(operation))
        {
            var deniedMessage = loc.Get("Engine.FailClosedRestore");
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: deniedMessage, Before: defaultStatus, After: defaultStatus);
        }

        if (!_transactionLog.TryLogStart(operation.Id, moduleKey, "RESTORE", lifecycle.TransactionId))
        {
            var logUnavailableMessage = loc.Get("Engine.TransactionUnavailableRestore");
            _logger.LogError("[OptimizationEngine][Restore] Could not persist transaction start for {OperationId}; operation blocked", operation.Id);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: logUnavailableMessage, Before: defaultStatus, After: defaultStatus);
        }

        try
        {
            snapshotExists = await _restoreStore.HasSnapshotAsync(operation.Id, executionToken).ConfigureAwait(false);

            // Why: GetStatusAsync 需要超时保护，防止 netsh/NIC枚举 卡死
            var statusReliable = true;
            using var beforeStatusCts = CancellationTokenSource.CreateLinkedTokenSource(executionToken);
            beforeStatusCts.CancelAfter(_statusCheckTimeout);
            try
            {
                before = await operation.GetStatusAsync(beforeStatusCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("[OptimizationEngine][Restore] Pre-restore GetStatusAsync timed out for {OperationId}, will force restore attempt", operation.Id);
                before = defaultStatus;
                statusReliable = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[OptimizationEngine][Restore] Pre-restore GetStatusAsync failed for {OperationId}, will force restore attempt", operation.Id);
                before = defaultStatus;
                statusReliable = false;
            }

            if (!before.IsCompliant)
            {
                if (!statusReliable)
                {
                    // A durable snapshot is historical evidence, not proof of the current state.
                    // An unreadable current state must remain unknown and must not trigger a restore write.
                    var unknownRestoreMessage = loc.Get("Engine.RestoreStateUnknown");
                    _logger.LogWarning(
                        "[OptimizationEngine][Restore] Current status is unknown for {OperationId}; historical snapshot will not override it",
                        operation.Id);
                    return new OperationExecutionSummary(
                        operation.Id, operation.Title, Success: false, Changed: false,
                        Message: unknownRestoreMessage, Before: before, After: before);
                }

                if (snapshotExists)
                {
                    // OperationStatus cannot distinguish a known non-compliant state from a read failure
                    // encoded as false. Durable recovery material makes that ambiguity safety-relevant.
                    var ambiguousRestoreMessage = loc.Get("Engine.RestoreStateAmbiguous");
                    _logger.LogWarning(
                        "[OptimizationEngine][Restore] Boolean status is ambiguous for {OperationId}; durable snapshot retained and restore blocked",
                        operation.Id);
                    return new OperationExecutionSummary(
                        operation.Id, operation.Title, Success: false, Changed: false,
                        Message: ambiguousRestoreMessage, Before: before, After: before);
                }

                return new OperationExecutionSummary(
                    operation.Id, operation.Title, Success: true, Changed: false,
                    Message: loc.Get("Engine.NoRestoreNeeded"), Before: before, After: before);
            }

            if (!snapshotExists && operation.RequiresSnapshotForRestore)
            {
                _logger.LogWarning("[OptimizationEngine][Restore] Missing snapshot for {OperationId}; skipping restore to avoid overwriting user-specific settings", operation.Id);
                return new OperationExecutionSummary(
                    operation.Id, operation.Title, Success: false, Changed: false,
                    Message: BuildMissingSnapshotRestoreMessage(operation), Before: before, After: before);
            }

            var context = CreateContext(operation);
            
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(executionToken);
            timeoutCts.CancelAfter(_operationTimeout);
            operationStarted = true;
            lifecycle.MarkOperationStarted();
            var result = await operation.RestoreAsync(context, timeoutCts.Token).ConfigureAwait(false);

            OperationStatus after;
            var afterStatusReliable = !result.Changed;
            if (result.Changed)
            {
                using var afterStatusCts = new CancellationTokenSource();
                afterStatusCts.CancelAfter(_statusCheckTimeout);
                try
                {
                    after = await operation.GetStatusAsync(afterStatusCts.Token).ConfigureAwait(false);
                    afterStatusReliable = true;
                }
                catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("[OptimizationEngine][Restore] Post-restore GetStatusAsync timed out for {OperationId}", operation.Id);
                        after = new OperationStatus(false, loc.Get("Engine.StatusCheckTimeout"));
                }
            }
            else
            {
                after = before;
            }

            var restoreVerifiedSuccess = result.Success && afterStatusReliable;
            var shouldDeleteSnapshot = restoreVerifiedSuccess
                && ShouldDeleteSnapshotAfterRestore(operation, result, after);
            if (shouldDeleteSnapshot)
            {
                await _restoreStore.DeleteSnapshotAsync(operation.Id, CancellationToken.None).ConfigureAwait(false);
            }

            var message = result.Message ?? string.Empty;
            if (!afterStatusReliable)
            {
                message = string.IsNullOrWhiteSpace(message)
                    ? loc.Get("Engine.RestoreVerificationUnavailable")
                    : $"{message} {loc.Get("Engine.RestoreVerificationUnavailableSuffix")}";
            }
            else if (snapshotExists && restoreVerifiedSuccess && result.Changed && after.IsCompliant && !operation.RestoreMayLeaveStatusCompliant)
            {
                message = BuildRestoreVerificationWarningMessage(message);
                restoreVerifiedSuccess = false;
            }
            
            return new OperationExecutionSummary(
                operation.Id, operation.Title, restoreVerifiedSuccess, result.Changed,
                message, before, after);
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Module {ModuleKey} - {OperationId} restore timed out", moduleKey, operation.Id);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: loc.Get("Engine.RestoreTimeout"), Before: before, After: before);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            var message = LocalizationService.Instance.Get("Dashboard.UserCancelled");
            var unknown = new OperationStatus(false, $"{message} Final state was not verified.");
            _logger.LogWarning("Module {ModuleKey} - {OperationId} restore was cancelled; final state remains unverified", moduleKey, operation.Id);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: operationStarted,
                Message: $"{message} Final state was not verified.", Before: before, After: unknown);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {ModuleKey} - {OperationId} restore failed", moduleKey, operation.Id);
            return new OperationExecutionSummary(
                operation.Id, operation.Title, Success: false, Changed: false,
                Message: ex.Message, Before: before, After: before, Exception: ex);
        }
    }

    private async Task CompletePendingBatchAsync(
        OptimizationModuleDescriptor module,
        IReadOnlyList<IOptimizationOperation> operations,
        IReadOnlyList<OperationExecutionSummary> initialSummaries,
        IReadOnlyList<PendingOperationCompletion> pendingOperations,
        IReadOnlyList<OperationTransaction> alreadyChangedOperations,
        OperationLock moduleLock,
        IOperationProgress? progress,
        string intent)
    {
        try
        {
            var pendingSummaries = await Task.WhenAll(
                pendingOperations.Select(pending => pending.CompletionTask)).ConfigureAwait(false);
            var summaryById = initialSummaries.ToDictionary(
                summary => summary.OperationId,
                StringComparer.OrdinalIgnoreCase);
            foreach (var pending in pendingSummaries)
            {
                summaryById[pending.OperationId] = pending;
            }

            var finalSummaries = operations
                .Where(operation => summaryById.ContainsKey(operation.Id))
                .Select(operation => summaryById[operation.Id])
                .ToList();
            var changedOperations = alreadyChangedOperations
                .Concat(
                    pendingOperations
                        .Zip(pendingSummaries)
                        .Where(pair => pair.Second.Changed)
                        .Select(pair => new OperationTransaction(
                            pair.First.Operation,
                            pair.First.TransactionId)))
                .GroupBy(operation => operation.Operation.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (finalSummaries.Any(summary => !summary.Success) && changedOperations.Count > 0)
            {
                ReportProgressSafely(progress, ProgressInfo.Running(
                    module.Key,
                    module.Title,
                    finalSummaries.Count,
                    operations.Count,
                    LocalizationService.Instance.Get("Engine.RollingBack")));
                var rollbackResults = await RollbackOperationsAsync(
                    module.Key,
                    changedOperations,
                    CancellationToken.None).ConfigureAwait(false);
                finalSummaries = MergeRollbackResults(finalSummaries, rollbackResults);
            }

            foreach (var pending in pendingOperations)
            {
                var finalSummary = finalSummaries.FirstOrDefault(
                    summary => string.Equals(summary.OperationId, pending.Operation.Id, StringComparison.OrdinalIgnoreCase));
                if (finalSummary is not null)
                {
                    _transactionLog.FinalizePending(
                        pending.Operation.Id,
                        finalSummary.Success,
                        finalSummary.Message,
                        module.Key,
                        intent,
                        pending.TransactionId);
                }
            }

            ReportTerminalProgress(module, finalSummaries, progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pending optimization execution could not reach a verified terminal state for {ModuleKey}", module.Key);
            ReportProgressSafely(progress, ProgressInfo.Failed(module.Key, module.Title, ex.Message));
        }
        finally
        {
            moduleLock.Dispose();
        }
    }

    private static List<OperationExecutionSummary> MergeRollbackResults(
        IReadOnlyList<OperationExecutionSummary> summaries,
        IReadOnlyList<OperationExecutionSummary> rollbackResults)
    {
        return summaries.Select(summary =>
        {
            if (!summary.Changed)
            {
                return summary;
            }

            var rollback = rollbackResults.FirstOrDefault(result => result.OperationId == summary.OperationId);
            if (rollback is null)
            {
                return summary;
            }

            return rollback.Success
                ? summary with
                {
                    Changed = false,
                    Message = summary.Message + " " + LocalizationService.Instance.Get("Engine.RolledBack")
                }
                : summary with
                {
                    Success = false,
                    Message = summary.Message + " " + LocalizationService.Instance.Get("Engine.RollbackFailed", rollback.Message)
                };
        }).ToList();
    }

    private static async Task<T> DisposeCancellationSourceAfterAsync<T>(Task<T> task, CancellationTokenSource cancellationSource)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            cancellationSource.Dispose();
        }
    }

    private bool TryGetPendingTransactions(
        OptimizationModuleDescriptor module,
        out IReadOnlyList<TransactionLogService.TransactionEntry> pendingTransactions,
        out Exception? error)
    {
        try
        {
            var operationIds = module.Operations
                .Select(operation => operation.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var state = _transactionLog.ReadState();
            var relevantIssues = state.Issues
                .Where(issue => issue.IsGlobal
                    || string.Equals(issue.ModuleKey, module.Key, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(issue.OperationId) && operationIds.Contains(issue.OperationId)))
                .ToArray();
            if (relevantIssues.Length > 0)
            {
                pendingTransactions = Array.Empty<TransactionLogService.TransactionEntry>();
                error = new TransactionLogService.TransactionLogReadException(relevantIssues);
                return false;
            }

            pendingTransactions = state.Entries
                .Where(entry =>
                    entry.Status is TransactionLogService.StartedStatus
                        or TransactionLogService.PendingConfirmationStatus
                        or TransactionLogService.RecoveryRequiredStatus)
                .Where(entry =>
                    string.Equals(entry.ModuleKey, module.Key, StringComparison.OrdinalIgnoreCase)
                    || operationIds.Contains(entry.OperationId))
                .ToArray();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            pendingTransactions = Array.Empty<TransactionLogService.TransactionEntry>();
            error = ex;
            return false;
        }
    }

    private async Task<IReadOnlyDictionary<string, StatusReadResult>> ReconcilePendingTransactionsAsync(
        OptimizationModuleDescriptor module,
        CancellationToken cancellationToken)
    {
        var observations = new Dictionary<string, StatusReadResult>(StringComparer.OrdinalIgnoreCase);
        var lockKey = $"module:{module.Key}";
        if (OperationLock.IsLocked(lockKey))
        {
            return observations;
        }

        try
        {
            await PendingReconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return observations;
        }

        try
        {
            if (OperationLock.IsLocked(lockKey)
                || !TryGetPendingTransactions(module, out var pendingTransactions, out _))
            {
                return observations;
            }

            foreach (var pending in pendingTransactions)
            {
                if (OperationLock.IsLocked(lockKey))
                {
                    return observations;
                }

                var operation = module.Operations.FirstOrDefault(
                    candidate => string.Equals(candidate.Id, pending.OperationId, StringComparison.OrdinalIgnoreCase));
                if (operation is null)
                {
                    _transactionLog.MarkRecoveryRequired(
                        pending.OperationId,
                        "Pending operation could not be matched to the current catalog; recovery is required and the record was retained.",
                        module.Key,
                        pending.Intent,
                        pending.EffectiveTransactionId);
                    continue;
                }

                var read = await ReadStatusAsync(operation, cancellationToken).ConfigureAwait(false);
                observations[pending.OperationId] = read;
                if (!read.IsReliable || !IsStatusEvidenceReliable(read.Status))
                {
                    var message = $"Startup reconciliation could not verify the pending {pending.Intent ?? "operation"}: {read.Error ?? read.Status.Details}. Recovery is required; the pending record was retained.";
                    _logger.LogWarning(
                        "[OptimizationEngine][Reconcile] Retaining pending operation {OperationId}: {Message}",
                        pending.OperationId,
                        message);
                    _transactionLog.MarkRecoveryRequired(
                        pending.OperationId,
                        message,
                        module.Key,
                        pending.Intent,
                        pending.EffectiveTransactionId);
                    continue;
                }

                if (string.Equals(pending.Intent, "APPLY", StringComparison.OrdinalIgnoreCase)
                    && read.Status.IsCompliant)
                {
                    _transactionLog.MarkReconciled(
                        pending.OperationId,
                        targetConfirmed: true,
                        "Startup reconciliation confirmed the target state; the pending record was cleared.",
                        module.Key,
                        pending.Intent,
                        pending.EffectiveTransactionId);
                    continue;
                }

                if (string.Equals(pending.Intent, "APPLY", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pending.Intent, "RESTORE", StringComparison.OrdinalIgnoreCase))
                {
                    _transactionLog.MarkReconciled(
                        pending.OperationId,
                        targetConfirmed: false,
                        "Startup reconciliation confirmed the original state; the pending record was cleared.",
                        module.Key,
                        pending.Intent,
                        pending.EffectiveTransactionId);
                    continue;
                }

                var unknownIntentMessage = $"Startup reconciliation read a state for pending operation {pending.OperationId}, but its intent is unknown. Recovery is required; the pending record was retained.";
                _transactionLog.MarkRecoveryRequired(
                    pending.OperationId,
                    unknownIntentMessage,
                    module.Key,
                    pending.Intent,
                    pending.EffectiveTransactionId);
            }
        }
        finally
        {
            PendingReconciliationGate.Release();
        }

        return observations;
    }

    private async Task<StatusReadResult> ReadStatusAsync(
        IOptimizationOperation operation,
        CancellationToken cancellationToken)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_statusCheckTimeout);

        Task<OperationStatus> statusTask;
        try
        {
            statusTask = operation.GetStatusAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException ex)
        {
            timeoutCts.Dispose();
            return new StatusReadResult(
                new OperationStatus(false, LocalizationService.Instance.Get("Engine.StatusReadCancelled"), IsKnown: false),
                IsReliable: false,
                ex.Message);
        }
        catch (Exception ex)
        {
            timeoutCts.Dispose();
            return new StatusReadResult(
                new OperationStatus(false, LocalizationService.Instance.Get("Engine.StatusCheckFailed", ex.GetType().Name), IsKnown: false),
                IsReliable: false,
                ex.Message);
        }

        var timeoutTask = Task.Delay(_statusCheckTimeout);
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var completedTask = await Task.WhenAny(statusTask, timeoutTask, cancellationTask).ConfigureAwait(false);
        if (completedTask != statusTask)
        {
            var message = completedTask == cancellationTask
                ? LocalizationService.Instance.Get("Engine.StatusReadCancelled")
                : LocalizationService.Instance.Get("Engine.StatusReadTimeout");
            _ = ObserveStatusTaskAsync(statusTask, timeoutCts);
            return new StatusReadResult(
                new OperationStatus(false, message, IsKnown: false),
                IsReliable: false,
                message);
        }

        try
        {
            var status = await statusTask.ConfigureAwait(false);
            timeoutCts.Dispose();
            return new StatusReadResult(status, status.IsKnown);
        }
        catch (OperationCanceledException ex)
        {
            timeoutCts.Dispose();
            return new StatusReadResult(
                new OperationStatus(false, LocalizationService.Instance.Get("Engine.StatusReadCancelled"), IsKnown: false),
                IsReliable: false,
                ex.Message);
        }
        catch (Exception ex)
        {
            timeoutCts.Dispose();
            return new StatusReadResult(
                new OperationStatus(false, LocalizationService.Instance.Get("Engine.StatusCheckFailed", ex.GetType().Name), IsKnown: false),
                IsReliable: false,
                ex.Message);
        }
    }

    private async Task<OperationStatus?> ProbeCompatibilityAsync(
        string moduleKey,
        IOptimizationOperation operation,
        CancellationToken cancellationToken)
    {
        if (_compatibilityPolicy is null) return null;
        try
        {
            return await _compatibilityPolicy.ProbeAsync(moduleKey, operation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compatibility probe failed for {ModuleKey}/{OperationId}; operation blocked", moduleKey, operation.Id);
            return new OperationStatus(
                false,
                LocalizationService.Instance.Get("Compatibility.UnknownBlock"),
                IsKnown: true,
                SupportState: OperationSupportState.Unknown,
                SupportReasonCode: "compatibility.probe-failed");
        }
    }

    private async Task<OperationCompatibilityMetadata?> DescribeCompatibilityAsync(
        string moduleKey,
        IOptimizationOperation operation,
        CancellationToken cancellationToken)
    {
        if (_compatibilityPolicy is not IOptimizationCompatibilityMetadataProvider provider) return null;
        try
        {
            return await provider.DescribeAsync(moduleKey, operation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compatibility metadata unavailable for {ModuleKey}/{OperationId}; execution policy is unchanged",
                moduleKey,
                operation.Id);
            return null;
        }
    }

    private static OperationStatus AttachCompatibilityMetadata(
        OperationStatus status,
        OperationCompatibilityMetadata metadata) => status with
    {
        CompatibilityVariantId = metadata.VariantId,
        CompatibilityVariantLabel = metadata.VariantLabel,
        CompatibilityMethod = metadata.Method,
        CompatibilityInformation = metadata.Information
    };

    private static async Task ObserveStatusTaskAsync(
        Task<OperationStatus> statusTask,
        CancellationTokenSource timeoutCts)
    {
        try
        {
            await statusTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The read result has already been converted to an unknown state.
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }

    private static bool IsStatusEvidenceReliable(OperationStatus status)
    {
        if (!status.IsKnown)
        {
            return false;
        }

        var details = status.Details ?? string.Empty;
        var failureMarkers = new[]
        {
            "failed", "failure", "error", "unable", "unknown", "timeout", "timed out",
            "not found", "missing", "unavailable", "not detectable", "not detected", "no eligible",
            "失败", "错误", "无法", "未知", "超时", "不存在", "缺失", "未检测"
        };
        return !failureMarkers.Any(marker => details.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private OptimizationBatchResult CreatePersistedPendingResult(
        OptimizationModuleDescriptor module,
        IReadOnlyList<IOptimizationOperation> operations,
        IOperationProgress? progress,
        IReadOnlyList<TransactionLogService.TransactionEntry> pendingTransactions,
        string intent)
    {
        var operationIds = string.Join(", ", pendingTransactions.Select(entry => entry.OperationId).Distinct(StringComparer.OrdinalIgnoreCase));
        var message = LocalizationService.Instance.Get("Engine.PendingOperationMessage", intent, operationIds, intent);
        var status = new OperationStatus(
            false,
            message,
            IsPending: true,
            RequiresRecovery: true);
        var summaries = operations
            .Select(operation => new OperationExecutionSummary(
                operation.Id,
                operation.Title,
                Success: false,
                Changed: false,
                Message: message,
                Before: status,
                After: status))
            .ToArray();

        ReportProgressSafely(progress, new ProgressInfo
        {
            OperationId = module.Key,
            OperationName = module.Title,
            State = ProgressState.Pending,
            TotalSteps = operations.Count,
            StatusMessage = message
        });
        return new OptimizationBatchResult(module.Key, summaries);
    }

    private OptimizationBatchResult CreatePendingStateUnavailableResult(
        OptimizationModuleDescriptor module,
        IReadOnlyList<IOptimizationOperation> operations,
        IOperationProgress? progress,
        Exception? error)
    {
        var message = BuildPendingStateUnavailableMessage(error);
        _logger.LogError(error, "Persisted optimization state is unavailable for module {ModuleKey}; execution blocked", module.Key);
        var status = new OperationStatus(
            false,
            message,
            IsPending: true,
            RequiresRecovery: true);
        var summaries = operations
            .Select(operation => new OperationExecutionSummary(
                operation.Id,
                operation.Title,
                Success: false,
                Changed: false,
                Message: message,
                Before: status,
                After: status))
            .ToArray();
        ReportProgressSafely(progress, ProgressInfo.Failed(module.Key, module.Title, message));
        return new OptimizationBatchResult(module.Key, summaries);
    }

    private static string BuildPendingStateUnavailableMessage(Exception? error) =>
        error is null
            ? LocalizationService.Instance.Get("Engine.PendingStateUnavailable")
            : LocalizationService.Instance.Get("Engine.PendingStateUnavailableWithError", error.Message);

    private static OperationStatus CreatePendingStatus(
        TransactionLogService.TransactionEntry? pendingTransaction,
        OperationStatus? observedStatus,
        string? readError)
    {
        var intent = pendingTransaction?.Intent ?? "operation";
        var startedAt = pendingTransaction is null
            ? string.Empty
            : $" started at {pendingTransaction.Timestamp:O}";
        var stateLabel = pendingTransaction?.Status == TransactionLogService.RecoveryRequiredStatus
            ? LocalizationService.Instance.Get("Engine.RecoveryRequired")
            : LocalizationService.Instance.Get("Engine.PendingConfirmation");
        var details = LocalizationService.Instance.Get("Engine.PendingStatusDetails", stateLabel, intent, startedAt);
        if (observedStatus is not null)
        {
            details += $" {LocalizationService.Instance.Get("Engine.LatestReadback", observedStatus.Details)}";
        }

        if (!string.IsNullOrWhiteSpace(readError))
        {
            details += $" {LocalizationService.Instance.Get("Engine.StateReadFailed", readError)}";
        }

        return new OperationStatus(
            false,
            details,
            observedStatus?.RequiresReboot ?? false,
            observedStatus?.IsSystemDefault ?? false,
            IsPending: true,
            RequiresRecovery: true,
            IsKnown: false);
    }

    private static OperationExecutionSummary CreatePendingExecutionSummary(
        IOptimizationOperation operation,
        bool operationStarted,
        bool callerCancellationRequested)
    {
        var reason = callerCancellationRequested
            ? LocalizationService.Instance.Get("Engine.CancellationRequested")
            : LocalizationService.Instance.Get("Engine.OperationTimedOut");
        var message = LocalizationService.Instance.Get("Engine.PendingOperationRunning", reason);
        var unknown = new OperationStatus(
            false,
            message,
            IsPending: true,
            RequiresRecovery: true);
        return new OperationExecutionSummary(
            operation.Id,
            operation.Title,
            Success: false,
            Changed: operationStarted,
            Message: message,
            Before: unknown,
            After: unknown);
    }

    private OperationExecutionSummary RecordTerminalExecution(
        IOptimizationOperation operation,
        string moduleKey,
        string intent,
        OperationExecutionSummary summary,
        string transactionId)
    {
        _transactionLog.LogComplete(
            operation.Id,
            summary.Success,
            summary.Message,
            moduleKey,
            intent,
            transactionId);
        return summary;
    }

    private static string CreateTransactionId() => $"tx:{Guid.NewGuid():N}";

    private void ReportProgressSafely(IOperationProgress? progress, ProgressInfo info)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress.Report(info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Progress callback failed while reporting optimization execution state for {OperationId}", info.OperationId);
        }
    }

    private static string BuildMissingSnapshotRestoreMessage(IOptimizationOperation operation)
    {
        var loc = LocalizationService.Instance;
        if (!string.IsNullOrWhiteSpace(operation.ManualPath))
        {
            return loc.Get("Engine.MissingSnapshotWithManualPath", operation.Title, operation.ManualPath);
        }

        return loc.Get("Engine.MissingSnapshot", operation.Title);
    }

    private static string BuildRestoreVerificationWarningMessage(string? baseMessage)
    {
        var loc = LocalizationService.Instance;
        var suffix = loc.Get("Engine.RestoreStillActive");

        return string.IsNullOrWhiteSpace(baseMessage)
            ? suffix
            : $"{baseMessage} ({suffix})";
    }

    private static bool ShouldDeleteSnapshotAfterRestore(
        IOptimizationOperation operation,
        OperationInvokeResult result,
        OperationStatus after)
    {
        if (!result.Success)
        {
            return false;
        }

        if (!result.Changed)
        {
            return false;
        }

        return !after.IsCompliant || operation.RestoreMayLeaveStatusCompliant;
    }

    private static bool IsFailClosedCatalogOperation(IOptimizationOperation operation) =>
        operation is PolicyDeniedOptimizationOperation
            or FailClosedServiceOptimizationOperation
            or FailClosedSecurityOptimizationOperation
            or FailClosedGraphicsOptimizationOperation
            or FailClosedPowerOptimizationOperation
            or FailClosedFileShellOptimizationOperation;

    private static OptimizationBatchResult CreateCancelledBatchResult(
        OptimizationModuleDescriptor module,
        IReadOnlyList<IOptimizationOperation> operations,
        IOperationProgress? progress)
    {
        var message = LocalizationService.Instance.Get("Dashboard.UserCancelled");
        var unknown = new OperationStatus(false, $"{message} Final state was not verified.");
        var summaries = operations
            .Select(operation => new OperationExecutionSummary(
                operation.Id,
                operation.Title,
                Success: false,
                Changed: false,
                Message: $"{message} Operation was not started.",
                Before: unknown,
                After: unknown))
            .ToArray();

        progress?.Report(ProgressInfo.Cancelled(module.Key, module.Title, message));
        return new OptimizationBatchResult(module.Key, summaries);
    }

    private static void ReportTerminalProgress(
        OptimizationModuleDescriptor module,
        IReadOnlyList<OperationExecutionSummary> summaries,
        IOperationProgress? progress)
    {
        if (progress is null)
        {
            return;
        }

        var failures = summaries.Where(summary => !summary.Success).ToArray();
        if (failures.Length > 0)
        {
            if (failures.Any(IsRecoveryRequiredSummary))
            {
                progress.Report(ProgressInfo.Failed(
                    module.Key,
                    module.Title,
                    failures.First(IsRecoveryRequiredSummary).Message));
                return;
            }

            var hasCancellation = failures.Any(IsCancellationSummary);
            var hasNonCancellationFailure = failures.Any(summary => !IsCancellationSummary(summary));
            if (hasCancellation && !hasNonCancellationFailure)
            {
                progress.Report(ProgressInfo.Cancelled(
                    module.Key,
                    module.Title,
                    LocalizationService.Instance.Get("Dashboard.UserCancelled")));
            }
            else
            {
                progress.Report(ProgressInfo.Failed(
                    module.Key,
                    module.Title,
                    failures[0].Message));
            }
            return;
        }

        progress.Report(ProgressInfo.Completed(module.Key, module.Title));
    }

    private static OptimizationBatchResult CreateEmptySelectionBatchResult(
        OptimizationModuleDescriptor module,
        IOperationProgress? progress)
    {
        var message = LocalizationService.Instance.Get("Engine.EmptySelection");
        var unknown = new OperationStatus(false, LocalizationService.Instance.Get("Engine.Unknown"));
        var summary = new OperationExecutionSummary(
            string.Empty,
            module.Title,
            Success: false,
            Changed: false,
            Message: message,
            Before: unknown,
            After: unknown);

        progress?.Report(ProgressInfo.Failed(module.Key, module.Title, message));
        return new OptimizationBatchResult(module.Key, [summary]);
    }

    private static bool IsCancellationSummary(OperationExecutionSummary summary)
    {
        var cancellationMessage = LocalizationService.Instance.Get("Dashboard.UserCancelled");
        return summary.Message.Contains(cancellationMessage, StringComparison.OrdinalIgnoreCase)
            || summary.After.Details.Contains(cancellationMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecoveryRequiredSummary(OperationExecutionSummary summary) =>
        summary.Message.Contains("Rollback failed", StringComparison.OrdinalIgnoreCase)
            || summary.Message.Contains("回滚失败", StringComparison.Ordinal);

    private OptimizationBatchResult CreateConcurrencyConflictResult(
        OptimizationModuleDescriptor module,
        IOperationProgress? progress,
        string intent)
    {
        var loc = LocalizationService.Instance;
        _logger.LogWarning("Module {ModuleKey} is already being processed ({Intent})", module.Key, intent);
        var message = string.Equals(intent, "restore", StringComparison.OrdinalIgnoreCase)
            ? $"{loc.Get("Engine.ModuleBusy")} {loc.Get("Engine.RestoreQueuedUnknown")}"
            : loc.Get("Engine.ModuleBusy");
        var conflict = new OptimizationBatchResult(module.Key, new List<OperationExecutionSummary>
        {
            new(
                "",
                loc.Get("Engine.ConcurrencyConflict"),
                Success: false,
                Changed: false,
                Message: message,
                Before: new OperationStatus(false, loc.Get("Engine.Locked"), false),
                After: new OperationStatus(false, loc.Get("Engine.Locked"), false))
        });
        progress?.Report(ProgressInfo.Failed(module.Key, module.Title, message));
        return conflict;
    }

    private static void AddCancelledSummaries(
        IReadOnlyList<IOptimizationOperation> operations,
        ConcurrentBag<OperationExecutionSummary> summaries)
    {
        var existingIds = summaries
            .Select(summary => summary.OperationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var message = LocalizationService.Instance.Get("Dashboard.UserCancelled");
        var unknown = new OperationStatus(false, $"{message} {LocalizationService.Instance.Get("Engine.OperationNotStarted")}");
        foreach (var operation in operations)
        {
            if (!existingIds.Add(operation.Id))
            {
                continue;
            }

            summaries.Add(new OperationExecutionSummary(
                operation.Id,
                operation.Title,
                Success: false,
                Changed: false,
            Message: $"{message} {LocalizationService.Instance.Get("Engine.OperationNotStarted")}",
                Before: unknown,
                After: unknown));
        }
    }
}
