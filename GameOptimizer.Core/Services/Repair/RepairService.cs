using System.Collections.Concurrent;
using System.Diagnostics;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.Repair;

public class RepairService : IRepairService
{
    private readonly RepairItemRegistry _registry;
    private readonly IRepairExecutionGate _executionGate;
    private readonly IRepairChangeExecutor _changeExecutor;
    private readonly IOptimizationRevertService? _optimizationRevertService;
    private readonly ConcurrentBag<RepairLogEntry> _history = [];
    private readonly object _historyLock = new();
    private readonly SemaphoreSlim _batchGate = new(1, 1);

    public RepairService(
        RepairItemRegistry registry,
        IRepairExecutionGate executionGate,
        IRepairChangeExecutor? changeExecutor = null,
        IOptimizationRevertService? optimizationRevertService = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executionGate = executionGate ?? throw new ArgumentNullException(nameof(executionGate));
        _changeExecutor = changeExecutor ?? UnavailableRepairChangeExecutor.Instance;
        _optimizationRevertService = optimizationRevertService;
        _registry.Initialize();
    }

    public IReadOnlyList<RepairItem> GetAllRepairItems() => _registry.GetAll();

    public IEnumerable<RepairItem> GetByCategory(RepairCategory category) =>
        _registry.GetByCategory(category);

    public IEnumerable<RepairItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAllRepairItems();
        
        var lowerQuery = query.ToLowerInvariant();
        return GetAllRepairItems().Where(item =>
            item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ScanResult> ScanForIssuesAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var allItems = GetAllRepairItems();
        var issues = new List<RepairItem>();
        var scanned = 0;

        foreach (var item in allItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            progress?.Report(new ScanProgress
            {
                CurrentItem = item.Name,
                ScannedCount = scanned,
                TotalCount = allItems.Count,
                IssuesFound = issues.Count
            });

            try
            {
                var isHealthy = await item.CheckHealthAsync();
                if (!isHealthy) issues.Add(item);
            }
            catch
            {
                issues.Add(item);
            }
            
            scanned++;
        }

        sw.Stop();
        return new ScanResult
        {
            Issues = issues,
            TotalScanned = scanned,
            Duration = sw.Elapsed
        };
    }

    public async Task<RepairResult> RepairAsync(
        RepairItem item,
        IProgress<RepairProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var sw = Stopwatch.StartNew();
        RepairResult result;

        var loc = LocalizationService.Instance;
        RepairItem? registeredItem = _registry.GetById(item.Id);
        if (registeredItem is null)
        {
            result = RepairResult.NotApplicable(
                $"Repair item is not registered: {item.Id}",
                "repair.item-not-registered") with { Duration = sw.Elapsed };
            LogRepair(item, result);
            return result;
        }

        item = registeredItem;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new RepairProgress { Message = loc.Get("Repair.Repairing", item.Name), PercentComplete = 0 });

            RepairExecutionGateDecision gateDecision = await _executionGate
                .EnsureCanExecuteAsync(item.Id, item.ExecutionIntent, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var readOnlyDiagnosticMayRun = gateDecision.Kind == RepairExecutionGateDecisionKind.ReadonlyOnly
                && item.Availability == RepairExecutionAvailability.ReadOnly;
            if (!gateDecision.IsAllowed && !readOnlyDiagnosticMayRun)
            {
                result = GateFailure(item, gateDecision) with { Duration = sw.Elapsed };
                LogRepair(item, result);
                return result;
            }
            
            if (item.Category == RepairCategory.OptimizationRevert)
            {
                if (_optimizationRevertService is null
                    || !_optimizationRevertService.IsDynamicRevertItem(item.Id)
                    || string.IsNullOrWhiteSpace(item.RelatedOptimization))
                {
                    result = RepairResult.Failed("durable optimization revert 服务未接入或恢复项绑定无效，已安全阻断。");
                }
                else
                {
                    result = await _optimizationRevertService.RevertOperationAsync(
                        item.RelatedOptimization,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                result = await ExecuteStaticRepairAsync(item, progress, cancellationToken).ConfigureAwait(false);
            }
            result = NormalizeResult(item, result) with { Duration = sw.Elapsed };
        }
        catch (OperationCanceledException ex)
        {
            result = RepairResult.Cancelled(
                loc.Get("Repair.RepairCancelled"),
                "cancelled.repair",
                ex.Message,
                item.Availability) with { Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            result = RepairResult.FromException(
                loc.Get("Repair.FailedSimple"),
                ex,
                item.Availability) with { Duration = sw.Elapsed };
            result = NormalizeResult(item, result);
        }

        LogRepair(item, result);
        return result;
    }

    private async Task<RepairResult> ExecuteStaticRepairAsync(
        RepairItem item,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_changeExecutor.IsManaged(item.Id))
        {
            return await _changeExecutor.ExecuteAsync(item, progress, cancellationToken).ConfigureAwait(false);
        }

        if (RepairSpecializedOwnerCatalog.Contains(item.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await item.RepairAsync(progress).ConfigureAwait(false);
        }

        return RepairResult.Failed(
            $"{item.Id} 尚未接入唯一 typed Owner/Executor，静态 legacy 执行入口已关闭。该修复项当前不可用且不会执行系统写入。");
    }

    public async Task<IReadOnlyList<RepairResult>> RepairAllAsync(
        IEnumerable<RepairItem> items,
        IProgress<RepairProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var itemList = items.ToList();

        if (itemList.Count == 0)
        {
            return [];
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return itemList
                .Select(item => BatchCancelled(item, "批次在启动前已取消；未执行任何修复项。"))
                .ToArray();
        }

        if (!_batchGate.Wait(0))
        {
            return itemList
                .Select(item => RepairResult.Blocked(
                    "另一个 Repair 批次正在执行，当前批次未启动。",
                    "repair.batch-busy",
                    availability: item.Availability))
                .ToArray();
        }

        try
        {
            var results = new List<RepairResult>(itemList.Count);
            var completed = 0;

            foreach (var item in itemList)
            {
                RepairResult result;
                try
                {
                    var itemProgress = new Progress<RepairProgress>(p =>
                    {
                        progress?.Report(new RepairProgress
                        {
                            Message = p.Message,
                            PercentComplete = (completed * 100 + p.PercentComplete) / itemList.Count,
                            CurrentStep = $"({completed + 1}/{itemList.Count}) {item.Name}"
                        });
                    });

                    result = await RepairAsync(item, itemProgress, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    result = RepairResult.Cancelled(
                        "批次中的修复项已取消；后续项未执行。",
                        "cancelled.batch-item",
                        ex.Message,
                        item.Availability);
                    LogRepair(item, result);
                }
                catch (Exception ex)
                {
                    result = RepairResult.FromException(
                        "批次中的修复项执行异常；后续项未执行。",
                        ex,
                        item.Availability);
                    LogRepair(item, result);
                }

                results.Add(result);
                completed++;
                if (!ShouldStopBatch(result))
                {
                    continue;
                }

                for (var index = completed; index < itemList.Count; index++)
                {
                    results.Add(BatchAborted(itemList[index], result));
                }

                break;
            }

            return results;
        }
        finally
        {
            _batchGate.Release();
        }
    }

    public async Task<RepairResult> RestoreAsync(
        RepairRecoveryRequest request,
        IProgress<RepairProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = _registry.GetById(request.RepairItemId);
        if (item is null || !_changeExecutor.IsManaged(item.Id))
        {
            return RepairResult.NotApplicable(
                "恢复请求对应的统一修复项不存在。",
                "repair.restore-item-not-found") with { ChangeReceipt = request.Receipt };
        }

        try
        {
            var gate = await _executionGate
                .EnsureCanExecuteAsync(item.Id, RepairExecutionIntent.Restore, cancellationToken)
                .ConfigureAwait(false);
            if (!gate.IsAllowed)
            {
                return GateFailure(item, gate) with { ChangeReceipt = request.Receipt };
            }

            var result = await _changeExecutor.RestoreAsync(request, progress, cancellationToken)
                .ConfigureAwait(false);
            return NormalizeRestoreResult(item, result, request.Receipt);
        }
        catch (OperationCanceledException ex)
        {
            return RepairResult.Cancelled(
                "恢复已取消；未继续执行恢复写入。",
                "cancelled.restore",
                ex.Message,
                item.Availability) with { ChangeReceipt = request.Receipt };
        }
        catch (Exception ex)
        {
            var failure = RepairResult.FromException(
                "恢复失败；未将异常当作恢复成功。",
                ex,
                item.Availability);
            if (failure.Code == RepairResultCode.Failed)
            {
                failure = RepairResult.RecoveryRequired(
                    "恢复协调器异常；恢复状态未知，保留原 durable 回执供后续核查。",
                    "restore.exception",
                    ex.Message,
                    item.Availability) with { Exception = ex };
            }

            return failure with { ChangeReceipt = request.Receipt };
        }
    }

    public IReadOnlyList<RepairLogEntry> GetRepairHistory(int count = 50)
    {
        lock (_historyLock)
        {
            return _history
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _history.Clear();
        }
    }

    private void LogRepair(RepairItem item, RepairResult result)
    {
        var entry = new RepairLogEntry
        {
            RepairItemId = item.Id,
            RepairItemName = item.Name,
            Category = item.Category,
            Success = result.Success,
            Code = result.Code,
            Availability = result.Availability,
            ReasonCode = result.ReasonCode,
            Message = result.Message,
            ErrorDetail = result.Detail,
            Duration = result.Duration
        };

        lock (_historyLock)
        {
            _history.Add(entry);
        }
    }

    private static RepairResult GateFailure(
        RepairItem item,
        RepairExecutionGateDecision decision)
    {
        var reasonCode = decision.ReasonCode ?? decision.Kind switch
        {
            RepairExecutionGateDecisionKind.ReadonlyOnly => "execution-gate.readonly",
            RepairExecutionGateDecisionKind.BlockedNeedBaseline => "execution-gate.policy-denied",
            _ => "execution-gate.blocked"
        };

        var classified = decision.ReasonCode is null
            ? null
            : RepairResult.FromReasonCode(
                decision.Reason,
                reasonCode,
                availability: item.Availability);
        if (classified?.Code is
            RepairResultCode.PermissionDenied or
            RepairResultCode.VersionUnsupported or
            RepairResultCode.NotApplicable or
            RepairResultCode.PolicyDenied or
            RepairResultCode.Cancelled)
        {
            return classified;
        }

        if (decision.Kind == RepairExecutionGateDecisionKind.ReadonlyOnly
            && item.Availability == RepairExecutionAvailability.WorkingBidirectional)
        {
            return RepairResult.ReadOnly(decision.Reason, reasonCode: reasonCode);
        }

        return decision.Kind == RepairExecutionGateDecisionKind.BlockedNeedBaseline
            ? RepairResult.PolicyDenied(
                decision.Reason,
                reasonCode,
                availability: item.Availability)
            : RepairResult.Blocked(
                decision.Reason,
                reasonCode,
                availability: item.Availability);
    }

    private static RepairResult NormalizeResult(RepairItem item, RepairResult result)
    {
        if (result.Success || result.Code != RepairResultCode.Failed)
        {
            return result;
        }

        if (item.Availability == RepairExecutionAvailability.WorkingBidirectional)
        {
            return result;
        }

        var classified = RepairResult.FromReasonCode(
            result.Message,
            result.ReasonCode,
            result.Detail,
            item.Availability);
        if (classified.Code != RepairResultCode.Blocked)
        {
            return classified with
            {
                Exception = result.Exception,
                Duration = result.Duration,
                RequiresReboot = result.RequiresReboot,
                ChangeReceipt = result.ChangeReceipt
            };
        }

        if (item.Availability == RepairExecutionAvailability.ReadOnly)
        {
            return RepairResult.ReadOnly(
                result.Message,
                result.Detail,
                result.ReasonCode ?? "repair.read-only") with
            {
                Exception = result.Exception,
                Duration = result.Duration,
                ChangeReceipt = result.ChangeReceipt
            };
        }

        return RepairResult.Blocked(
            result.Message,
            result.ReasonCode ?? "repair.blocked",
            result.Detail,
            item.Availability) with
        {
            Exception = result.Exception,
            Duration = result.Duration,
            ChangeReceipt = result.ChangeReceipt
        };
    }

    private static RepairResult NormalizeRestoreResult(
        RepairItem item,
        RepairResult result,
        RepairChangeReceipt sourceReceipt)
    {
        var receipt = result.ChangeReceipt ?? sourceReceipt;
        if (result.Code == RepairResultCode.Succeeded)
        {
            if (HasVerifiedRestoreReceipt(item, sourceReceipt, result.ChangeReceipt))
            {
                return result with { ChangeReceipt = receipt };
            }

            return RepairResult.RecoveryRequired(
                "恢复入口返回成功，但缺少同源且逐项 RestoredVerified 的 durable 回执；最终状态未知。",
                "restore.success-receipt-missing",
                result.Message,
                item.Availability) with
            {
                Exception = result.Exception,
                Duration = result.Duration,
                RequiresReboot = result.RequiresReboot,
                ChangeReceipt = receipt
            };
        }

        if (result.Code == RepairResultCode.NoChange)
        {
            return RepairResult.RecoveryRequired(
                "恢复未返回实际 RestoredVerified 结果；最终状态未知。",
                "restore.no-op",
                result.Message,
                item.Availability) with
            {
                Exception = result.Exception,
                Duration = result.Duration,
                ChangeReceipt = receipt
            };
        }

        if (result.Code is
            RepairResultCode.ReadOnly or
            RepairResultCode.PermissionDenied or
            RepairResultCode.VersionUnsupported or
            RepairResultCode.NotApplicable or
            RepairResultCode.PolicyDenied or
            RepairResultCode.Cancelled or
            RepairResultCode.RecoveryRequired)
        {
            return result with { ChangeReceipt = receipt };
        }

        if (result.ReasonCode?.StartsWith("restore.request-", StringComparison.OrdinalIgnoreCase) == true)
        {
            return result with { ChangeReceipt = receipt };
        }

        if (result.Code == RepairResultCode.Blocked
            && result.Availability is RepairExecutionAvailability.FailClosed or RepairExecutionAvailability.SafetyBlocked)
        {
            return result with { ChangeReceipt = receipt };
        }

        var classified = result.Exception is not null
            ? RepairResult.FromException(
                result.Message,
                result.Exception,
                item.Availability)
            : RepairResult.FromReasonCode(
                result.Message,
                result.ReasonCode,
                result.Detail,
                item.Availability);
        if (classified.Code is
            RepairResultCode.PermissionDenied or
            RepairResultCode.VersionUnsupported or
            RepairResultCode.NotApplicable or
            RepairResultCode.PolicyDenied or
            RepairResultCode.Cancelled or
            RepairResultCode.ReadOnly or
            RepairResultCode.RecoveryRequired)
        {
            return classified with
            {
                Exception = result.Exception,
                Duration = result.Duration,
                RequiresReboot = result.RequiresReboot,
                ChangeReceipt = receipt
            };
        }

        return RepairResult.RecoveryRequired(
            "恢复未返回可区分的失败终态；最终状态未知。",
            result.ReasonCode ?? "restore.unknown-result",
            result.Detail ?? result.Message,
            item.Availability) with
        {
            Exception = result.Exception,
            Duration = result.Duration,
            RequiresReboot = result.RequiresReboot,
            ChangeReceipt = receipt
        };
    }

    private static bool HasVerifiedRestoreReceipt(
        RepairItem item,
        RepairChangeReceipt sourceReceipt,
        RepairChangeReceipt? resultReceipt)
    {
        if (resultReceipt is null
            || sourceReceipt.ReceiptId == Guid.Empty
            || resultReceipt.ReceiptId != sourceReceipt.ReceiptId
            || !string.Equals(resultReceipt.RepairItemId, item.Id, StringComparison.OrdinalIgnoreCase)
            || sourceReceipt.Operations is null
            || resultReceipt.Operations is null)
        {
            return false;
        }

        var expected = sourceReceipt.Operations
            .Where(operation => operation.RecoveryId is Guid)
            .Select(operation => (operation.ReceiptId, RecoveryId: operation.RecoveryId!.Value))
            .ToHashSet();
        var actual = resultReceipt.Operations
            .Where(operation => operation.RecoveryId is Guid)
            .Select(operation => (operation.ReceiptId, RecoveryId: operation.RecoveryId!.Value))
            .ToArray();
        return expected.Count > 0
            && resultReceipt.Operations.Count == sourceReceipt.Operations.Count
            && actual.Length == expected.Count
            && actual.Distinct().Count() == expected.Count
            && actual.All(expected.Contains)
            && resultReceipt.Operations.All(operation => sourceReceipt.Operations.Any(source =>
                source.ReceiptId == operation.ReceiptId
                && source.RecoveryId == operation.RecoveryId
                && string.Equals(source.CapabilityId, operation.CapabilityId, StringComparison.OrdinalIgnoreCase)))
            && resultReceipt.Operations
                .Where(operation => operation.RecoveryId is Guid)
                .All(operation => operation.Outcome == ChangeOutcome.RestoredVerified);
    }

    private static bool ShouldStopBatch(RepairResult result)
    {
        if (result.ChangeReceipt?.CompensationAttempted == true
            || (!result.Success
                && result.ChangeReceipt?.Operations.Any(operation =>
                    operation.MutationOccurred && operation.RecoveryId is not null) == true))
        {
            return true;
        }

        return result.Code is
            RepairResultCode.Blocked or
            RepairResultCode.PermissionDenied or
            RepairResultCode.PolicyDenied or
            RepairResultCode.VersionUnsupported or
            RepairResultCode.Cancelled or
            RepairResultCode.RecoveryRequired or
            RepairResultCode.Failed;
    }

    private static RepairResult BatchCancelled(RepairItem item, string message) =>
        RepairResult.Cancelled(
            message,
            "cancelled.batch-before-start",
            availability: item.Availability);

    private static RepairResult BatchAborted(RepairItem item, RepairResult cause)
    {
        if (cause.Code == RepairResultCode.Cancelled)
        {
            return RepairResult.Cancelled(
                "当前批次已取消；该修复项未执行。",
                "cancelled.batch-aborted",
                cause.ReasonCode,
                item.Availability);
        }

        if (cause.Code == RepairResultCode.RecoveryRequired)
        {
            return RepairResult.RecoveryRequired(
                "当前批次因前一项需要恢复核查而停止；该修复项未执行。",
                "restore.batch-aborted",
                $"前一项状态：{cause.Code}；原因：{cause.ReasonCode ?? "unknown"}",
                item.Availability);
        }

        return RepairResult.Blocked(
            "当前批次因前一项未完成而停止；该修复项未执行。",
            "repair.batch-aborted",
            $"前一项状态：{cause.Code}；原因：{cause.ReasonCode ?? "unknown"}",
            item.Availability);
    }
}
