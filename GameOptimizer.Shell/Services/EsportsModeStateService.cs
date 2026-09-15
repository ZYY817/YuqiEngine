// File: GameOptimizer.Shell/Services/EsportsModeStateService.cs
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 电竞模式唯一工作流入口：检测、计划、无基线风险确认、逐项执行和写后回读。
/// </summary>
public sealed class EsportsModeStateService
{
    private const string MissingModuleKey = "unknown";

    private readonly IOptimizationEngine _engine;
    private readonly IBaselineGateService? _baselineGate;
    private readonly Func<bool> _isAdministrator;
    private readonly EsportsWorkflowOptions _options;
    private readonly Dictionary<string, OperationRegistration> _operationIndex;
    private readonly HashSet<string> _ambiguousOperationIds;

    public EsportsModeStateService(
        IOptimizationEngine engine,
        IBaselineGateService? baselineGate,
        Func<bool> isAdministrator,
        EsportsWorkflowOptions? options = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _baselineGate = baselineGate;
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        _options = options ?? EsportsWorkflowOptions.Default;
        _options.Validate();
        (_operationIndex, _ambiguousOperationIds) = BuildOperationIndex(engine.Modules);
    }

    public async Task<EsportsDetectionReport> DetectAllStatesAsync(
        IEnumerable<string> operationIds,
        IProgress<EsportsWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(operationIds);
        var results = new List<EsportsProbeResult>(ids.Count);

        for (var index = 0; index < ids.Count; index++)
        {
            var operationId = ids[index];
            if (cancellationToken.IsCancellationRequested)
            {
                AddCancelledProbeResults(ids, index, results);
                break;
            }

            progress?.Report(new EsportsWorkflowProgress(index + 1, ids.Count, operationId, ResolveTitle(operationId)));

            if (!TryResolveOperation(operationId, out var registration, out var resolutionError))
            {
                results.Add(new EsportsProbeResult(operationId, EsportsProbeOutcome.MissingOperation, null, resolutionError));
                continue;
            }

            try
            {
                var status = await RunWithTimeoutAsync(
                    token => registration.Operation.GetStatusAsync(token),
                    _options.DetectionTimeout,
                    cancellationToken).ConfigureAwait(false);
                results.Add(new EsportsProbeResult(
                    operationId,
                    EsportsProbeOutcome.Detected,
                    status.IsCompliant,
                    status.Details));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AddCancelledProbeResults(ids, index, results);
                break;
            }
            catch (TimeoutException ex)
            {
                results.Add(new EsportsProbeResult(operationId, EsportsProbeOutcome.TimedOut, null, ex.Message));
            }
            catch (Exception ex)
            {
                results.Add(new EsportsProbeResult(operationId, EsportsProbeOutcome.Failed, null, SafeError(ex)));
            }
        }

        return new EsportsDetectionReport(results);
    }

    public EsportsOperationPlan CreatePlan(
        IEnumerable<string> operationIds,
        EsportsWorkflowAction action)
    {
        var requestedIds = NormalizeIds(operationIds);
        var items = new List<EsportsOperationPlanItem>(requestedIds.Count);
        var rejected = new List<EsportsOperationResult>();
        bool isAdministrator;
        try
        {
            isAdministrator = _isAdministrator();
        }
        catch (Exception ex)
        {
            rejected.AddRange(requestedIds.Select(operationId => CreateResult(
                operationId,
                MissingModuleKey,
                ResolveTitle(operationId),
                EsportsExecutionOutcome.Blocked,
                EsportsFailureKind.PermissionDenied,
                $"无法确认管理员权限，整个计划已在写入前阻断：{SafeError(ex)}")));
            return new EsportsOperationPlan(action, requestedIds, items, rejected);
        }

        foreach (var operationId in requestedIds)
        {
            if (!TryResolveOperation(operationId, out var registration, out var resolutionError))
            {
                rejected.Add(CreateResult(
                    operationId,
                    MissingModuleKey,
                    operationId,
                    EsportsExecutionOutcome.Blocked,
                    EsportsFailureKind.InvalidPlan,
                    resolutionError));
                continue;
            }

            if (registration.Operation.RequiresAdmin && !isAdministrator)
            {
                rejected.Add(CreateResult(
                    operationId,
                    registration.ModuleKey,
                    registration.Operation.Title,
                    EsportsExecutionOutcome.Blocked,
                    EsportsFailureKind.PermissionDenied,
                    ElevationGate.DescribeWriteDenial(registration.Operation.Title)));
                continue;
            }

            items.Add(new EsportsOperationPlanItem(
                operationId,
                registration.ModuleKey,
                registration.Operation.Title,
                registration.Operation.RequiresAdmin));
        }

        return new EsportsOperationPlan(action, requestedIds, items, rejected);
    }

    public Task<EsportsWorkflowReport> ExecuteApplyPlanAsync(
        EsportsOperationPlan plan,
        IProgress<EsportsWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExecutePlanAsync(plan, EsportsWorkflowAction.Apply, progress, cancellationToken);
    }

    public Task<EsportsWorkflowReport> ExecuteRestorePlanAsync(
        EsportsOperationPlan plan,
        IProgress<EsportsWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExecutePlanAsync(plan, EsportsWorkflowAction.Restore, progress, cancellationToken);
    }

    public static string GetCategoryIcon(string category)
    {
        return category switch
        {
            "FPS" => "\uE7FC",
            "Input" => "\uE7C4",
            "Network" => "\uE968",
            "Storage" => "\uEDA2",
            "Windows" => "\uE782",
            "Security" => "\uE72E",
            "CPU" => "\uE950",
            "GPU" => "\uE7F8",
            _ => "\uE946"
        };
    }

    private async Task<EsportsWorkflowReport> ExecutePlanAsync(
        EsportsOperationPlan plan,
        EsportsWorkflowAction expectedAction,
        IProgress<EsportsWorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Action != expectedAction)
        {
            throw new ArgumentException("电竞模式计划动作与执行入口不匹配。", nameof(plan));
        }

        if (!plan.CanExecute)
        {
            var invalidPlanResults = BuildInvalidPlanResults(plan);
            return new EsportsWorkflowReport(
                plan.Action,
                plan.RequestedOperationIds,
                invalidPlanResults,
                EsportsBackupGateResult.Blocked(EsportsFailureKind.InvalidPlan, "计划验证失败，未执行任何系统写入。"));
        }

        var backupGate = await EnsureBaselineRiskGateAsync(plan.Action, cancellationToken).ConfigureAwait(false);
        if (!backupGate.Allowed)
        {
            var blocked = plan.Items
                .Select(item => CreateResult(
                    item.OperationId,
                    item.ModuleKey,
                    item.Title,
                    backupGate.FailureKind == EsportsFailureKind.Cancelled
                        ? EsportsExecutionOutcome.Cancelled
                        : backupGate.FailureKind is EsportsFailureKind.BackupTimedOut
                            or EsportsFailureKind.SafetyConfirmationTimedOut
                            ? EsportsExecutionOutcome.TimedOut
                            : EsportsExecutionOutcome.Blocked,
                    backupGate.FailureKind,
                    backupGate.Message))
                .ToList();
            return new EsportsWorkflowReport(plan.Action, plan.RequestedOperationIds, blocked, backupGate);
        }

        var results = new List<EsportsOperationResult>(plan.Items.Count);
        for (var index = 0; index < plan.Items.Count; index++)
        {
            var item = plan.Items[index];
            if (cancellationToken.IsCancellationRequested)
            {
                AddUnstartedResults(plan.Items, index, results, EsportsExecutionOutcome.Cancelled, EsportsFailureKind.Cancelled,
                    "用户已取消，未开始的操作未执行。");
                break;
            }

            progress?.Report(new EsportsWorkflowProgress(index + 1, plan.Items.Count, item.OperationId, item.Title));
            var result = await ExecuteOneAsync(item, plan.Action, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!result.Success)
            {
                var cancelled = result.Outcome == EsportsExecutionOutcome.Cancelled;
                AddUnstartedResults(
                    plan.Items,
                    index + 1,
                    results,
                    cancelled ? EsportsExecutionOutcome.Cancelled : EsportsExecutionOutcome.Blocked,
                    cancelled ? EsportsFailureKind.Cancelled : EsportsFailureKind.BlockedByPriorFailure,
                    cancelled
                        ? "用户已取消，未开始的操作未执行。"
                        : $"前一项 {item.OperationId} 未通过，后续写入已阻断。");
                break;
            }
        }

        return new EsportsWorkflowReport(plan.Action, plan.RequestedOperationIds, results, backupGate);
    }

    private async Task<EsportsOperationResult> ExecuteOneAsync(
        EsportsOperationPlanItem item,
        EsportsWorkflowAction action,
        CancellationToken cancellationToken)
    {
        if (!TryResolveOperation(item.OperationId, out var registration, out var resolutionError))
        {
            return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Blocked,
                EsportsFailureKind.InvalidPlan, resolutionError);
        }

        try
        {
            var batch = await RunWithTimeoutAsync(
                token => action == EsportsWorkflowAction.Apply
                    ? _engine.ApplyAsync(item.ModuleKey, new[] { item.OperationId }, token)
                    : _engine.RestoreAsync(item.ModuleKey, new[] { item.OperationId }, token),
                _options.OperationTimeout,
                cancellationToken).ConfigureAwait(false);

            var summary = batch.Operations.FirstOrDefault(operation =>
                operation.OperationId.Equals(item.OperationId, StringComparison.OrdinalIgnoreCase));
            if (summary == null)
            {
                return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Failed,
                    EsportsFailureKind.ExecutionFailed, "执行引擎未返回该操作的逐项结果。");
            }

            if (!summary.Success)
            {
                return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Failed,
                    EsportsFailureKind.ExecutionFailed, string.IsNullOrWhiteSpace(summary.Message) ? "执行失败。" : summary.Message);
            }

            return await VerifyReadbackAsync(item, registration.Operation, action, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Cancelled,
                EsportsFailureKind.Cancelled, "用户已取消当前操作。");
        }
        catch (TimeoutException ex)
        {
            return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.TimedOut,
                EsportsFailureKind.ExecutionTimedOut, ex.Message);
        }
        catch (Exception ex)
        {
            return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Failed,
                EsportsFailureKind.ExecutionFailed, SafeError(ex));
        }
    }

    private async Task<EsportsOperationResult> VerifyReadbackAsync(
        EsportsOperationPlanItem item,
        IOptimizationOperation operation,
        EsportsWorkflowAction action,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await RunWithTimeoutAsync(
                token => operation.GetStatusAsync(token),
                _options.ReadbackTimeout,
                cancellationToken).ConfigureAwait(false);
            var verified = action == EsportsWorkflowAction.Apply
                ? status.IsCompliant
                : !status.IsCompliant || operation.RestoreMayLeaveStatusCompliant;

            return verified
                ? CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Succeeded,
                    EsportsFailureKind.None, "执行成功且写后回读已验证。", status)
                : CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Failed,
                    EsportsFailureKind.ReadbackFailed, "执行引擎报告成功，但写后回读未达到目标状态。", status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Cancelled,
                EsportsFailureKind.Cancelled, "用户在写后回读阶段取消；当前状态未知，后续写入已阻断。");
        }
        catch (TimeoutException ex)
        {
            return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.TimedOut,
                EsportsFailureKind.ReadbackTimedOut, $"写后回读超时：{ex.Message}");
        }
        catch (Exception ex)
        {
            return CreateResult(item.OperationId, item.ModuleKey, item.Title, EsportsExecutionOutcome.Failed,
                EsportsFailureKind.ReadbackFailed, $"写后回读失败：{SafeError(ex)}");
        }
    }

    private async Task<EsportsBackupGateResult> EnsureBaselineRiskGateAsync(
        EsportsWorkflowAction action,
        CancellationToken cancellationToken)
    {
        if (action == EsportsWorkflowAction.Restore)
        {
            return EsportsBackupGateResult.Passed(
                "恢复操作不受首次安全基线 Apply 策略阻断；恢复自身安全检查保持生效。");
        }

        if (_baselineGate == null)
        {
            return EsportsBackupGateResult.Blocked(
                EsportsFailureKind.SafetyConfirmationUnavailable,
                "无基线风险确认服务未接入，无法取得本次明确确认，未执行系统写入。");
        }

        try
        {
            var decision = await RunWithTimeoutAsync(
                token => _baselineGate.EnsureCanOptimizeAsync("esports:apply-plan", token),
                _options.BackupTimeout,
                cancellationToken).ConfigureAwait(false);
            return decision.CanProceed
                ? EsportsBackupGateResult.Passed(decision.Reason)
                : EsportsBackupGateResult.Blocked(
                    EsportsFailureKind.SafetyConfirmationDeclined,
                    decision.Reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return EsportsBackupGateResult.Blocked(EsportsFailureKind.Cancelled, "用户在风险确认阶段取消，未执行系统写入。");
        }
        catch (TimeoutException ex)
        {
            return EsportsBackupGateResult.Blocked(
                EsportsFailureKind.SafetyConfirmationTimedOut,
                $"风险确认超时，未执行系统优化写入：{ex.Message}");
        }
        catch (Exception ex)
        {
            return EsportsBackupGateResult.Blocked(
                EsportsFailureKind.SafetyConfirmationUnavailable,
                $"无法取得无基线风险确认，未执行系统写入：{SafeError(ex)}");
        }
    }

    private bool TryResolveOperation(
        string operationId,
        out OperationRegistration registration,
        out string error)
    {
        if (_ambiguousOperationIds.Contains(operationId))
        {
            registration = default!;
            error = $"操作 ID {operationId} 在多个模块中重复，计划已阻断。";
            return false;
        }

        if (_operationIndex.TryGetValue(operationId, out registration!))
        {
            error = string.Empty;
            return true;
        }

        error = $"未找到操作 ID {operationId}，计划已阻断。";
        return false;
    }

    private static (Dictionary<string, OperationRegistration>, HashSet<string>) BuildOperationIndex(
        IReadOnlyList<OptimizationModuleDescriptor> modules)
    {
        var index = new Dictionary<string, OperationRegistration>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            foreach (var operation in module.Operations)
            {
                if (string.IsNullOrWhiteSpace(operation.Id) || ambiguous.Contains(operation.Id))
                {
                    continue;
                }

                if (!index.TryAdd(operation.Id, new OperationRegistration(module.Key, operation)))
                {
                    index.Remove(operation.Id);
                    ambiguous.Add(operation.Id);
                }
            }
        }

        return (index, ambiguous);
    }

    private static async Task<T> RunWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var task = action(timeoutSource.Token);
            return await task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"超过 {timeout.TotalSeconds:0.##} 秒限制。");
        }
    }

    private static IReadOnlyList<EsportsOperationResult> BuildInvalidPlanResults(EsportsOperationPlan plan)
    {
        var results = plan.RejectedItems.ToList();
        results.AddRange(plan.Items.Select(item => CreateResult(
            item.OperationId,
            item.ModuleKey,
            item.Title,
            EsportsExecutionOutcome.Blocked,
            EsportsFailureKind.InvalidPlan,
            "同一计划中存在无效或未授权操作，整批已在写入前阻断。")));
        return OrderByPlan(plan.RequestedOperationIds, results);
    }

    private static IReadOnlyList<EsportsOperationResult> OrderByPlan(
        IReadOnlyList<string> plannedIds,
        IReadOnlyList<EsportsOperationResult> results)
    {
        var index = plannedIds
            .Select((id, order) => (id, order))
            .ToDictionary(static pair => pair.id, static pair => pair.order, StringComparer.OrdinalIgnoreCase);
        return results.OrderBy(result => index.GetValueOrDefault(result.OperationId, int.MaxValue)).ToList();
    }

    private static void AddCancelledProbeResults(
        IReadOnlyList<string> ids,
        int startIndex,
        ICollection<EsportsProbeResult> results)
    {
        for (var index = startIndex; index < ids.Count; index++)
        {
            results.Add(new EsportsProbeResult(
                ids[index],
                EsportsProbeOutcome.Cancelled,
                null,
                "检测已取消，状态未知。"));
        }
    }

    private static void AddUnstartedResults(
        IReadOnlyList<EsportsOperationPlanItem> items,
        int startIndex,
        ICollection<EsportsOperationResult> results,
        EsportsExecutionOutcome outcome,
        EsportsFailureKind failureKind,
        string message)
    {
        for (var index = startIndex; index < items.Count; index++)
        {
            var item = items[index];
            results.Add(CreateResult(item.OperationId, item.ModuleKey, item.Title, outcome, failureKind, message));
        }
    }

    private static List<string> NormalizeIds(IEnumerable<string> operationIds)
    {
        ArgumentNullException.ThrowIfNull(operationIds);
        return operationIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolveTitle(string operationId)
    {
        return _operationIndex.TryGetValue(operationId, out var registration)
            ? registration.Operation.Title
            : operationId;
    }

    private static string SafeError(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
    }

    private static EsportsOperationResult CreateResult(
        string operationId,
        string moduleKey,
        string title,
        EsportsExecutionOutcome outcome,
        EsportsFailureKind failureKind,
        string message,
        GameOptimizer.Core.Optimization.OperationStatus? readbackStatus = null)
    {
        return new EsportsOperationResult(operationId, moduleKey, title, outcome, failureKind, message, readbackStatus);
    }

    private sealed record OperationRegistration(string ModuleKey, IOptimizationOperation Operation);
}
