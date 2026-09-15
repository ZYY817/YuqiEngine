// File: GameOptimizer.Shell/Services/ThreadSafeBatchOperationService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// Thread-safe batch executor that reports progress and returns authoritative per-operation summaries.
/// </summary>
public sealed class ThreadSafeBatchOperationService
{
    public async Task<BatchResult> ExecuteApplyAsync(
        IOptimizationExecutor executor,
        string moduleKey,
        IReadOnlyList<OptimizationToggleModel> items,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(items);

        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            throw new ArgumentException("Module key cannot be null or empty", nameof(moduleKey));
        }

        if (items.Count == 0)
        {
            return new BatchResult();
        }

        var errors = new List<OperationError>();
        var summaries = new List<OperationExecutionSummary>();

        progress?.Report(new OperationProgress
        {
            CurrentIndex = 0,
            TotalCount = items.Count,
            CurrentItemName = string.Empty,
            Status = BatchOperationStatus.Starting
        });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationIds = ValidateAndGetOperationIds(items);
            var result = await executor.ApplyOperationsAsync(moduleKey, operationIds, null, cancellationToken)
                .ConfigureAwait(false);
            return BuildResult(items, result, progress, isApply: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            foreach (var item in items)
            {
                errors.Add(BuildOperationError(item, ex));
            }

            System.Diagnostics.Debug.WriteLine($"[ThreadSafeBatchService] Apply batch failed: {ex.Message}");
            return new BatchResult
            {
                SuccessCount = 0,
                FailedCount = items.Count,
                Errors = errors,
                OperationSummaries = summaries
            };
        }
    }

    public async Task<BatchResult> ExecuteRestoreAsync(
        IOptimizationExecutor executor,
        string moduleKey,
        IReadOnlyList<OptimizationToggleModel> items,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(items);

        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            throw new ArgumentException("Module key cannot be null or empty", nameof(moduleKey));
        }

        if (items.Count == 0)
        {
            return new BatchResult();
        }

        var errors = new List<OperationError>();
        var summaries = new List<OperationExecutionSummary>();

        progress?.Report(new OperationProgress
        {
            CurrentIndex = 0,
            TotalCount = items.Count,
            CurrentItemName = string.Empty,
            Status = BatchOperationStatus.Starting
        });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationIds = ValidateAndGetOperationIds(items);
            var result = await executor.RestoreOperationsAsync(moduleKey, operationIds, cancellationToken)
                .ConfigureAwait(false);
            return BuildResult(items, result, progress, isApply: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            foreach (var item in items)
            {
                errors.Add(BuildOperationError(item, ex));
            }

            System.Diagnostics.Debug.WriteLine($"[ThreadSafeBatchService] Restore batch failed: {ex.Message}");
            return new BatchResult
            {
                SuccessCount = 0,
                FailedCount = items.Count,
                Errors = errors,
                OperationSummaries = summaries
            };
        }
    }

    private static IReadOnlyList<string> ValidateAndGetOperationIds(IReadOnlyList<OptimizationToggleModel> items)
    {
        var operationIds = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.OperationId))
            {
                throw new InvalidOperationException($"Item '{item.Title}' has no OperationId");
            }

            operationIds.Add(item.OperationId);
        }

        return operationIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static BatchResult BuildResult(
        IReadOnlyList<OptimizationToggleModel> items,
        OptimizationBatchResult batchResult,
        IProgress<OperationProgress>? progress,
        bool isApply)
    {
        var summariesByOperation = batchResult.Operations
            .GroupBy(summary => summary.OperationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var errors = new List<OperationError>();
        var summaries = new List<OperationExecutionSummary>(items.Count);
        var successCount = 0;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!string.IsNullOrWhiteSpace(item.OperationId)
                && summariesByOperation.TryGetValue(item.OperationId, out var summary))
            {
                summaries.Add(summary);
                var presentation = CommandResultPresentationMapper.FromOptimization(summary);
                var targetReached = isApply
                    ? OptimizationExecutionStateEvaluator.DidApplyReachTarget(summary)
                    : OptimizationExecutionStateEvaluator.DidRestoreReachTarget(summary);
                if (presentation.IsSuccess && targetReached)
                {
                    successCount++;
                    ReportProgress(progress, index, items.Count, item.Title, BatchOperationStatus.Completed);
                    continue;
                }

                errors.Add(BuildOperationError(item, summary));
                ReportProgress(progress, index, items.Count, item.Title, BatchOperationStatus.Failed);
                continue;
            }

            errors.Add(new OperationError
            {
                ItemName = item.Title,
                ErrorMessage = "执行器未返回该操作的逐项回执。"
            });
            ReportProgress(progress, index, items.Count, item.Title, BatchOperationStatus.Failed);
        }

        return new BatchResult
        {
            SuccessCount = successCount,
            FailedCount = items.Count - successCount,
            Errors = errors,
            OperationSummaries = summaries
        };
    }

    private static void ReportProgress(
        IProgress<OperationProgress>? progress,
        int index,
        int totalCount,
        string itemName,
        BatchOperationStatus status)
    {
        progress?.Report(new OperationProgress
        {
            CurrentIndex = index + 1,
            TotalCount = totalCount,
            CurrentItemName = itemName,
            Status = status
        });
    }

    private static OperationError BuildOperationError(OptimizationToggleModel item, OperationExecutionSummary summary)
    {
        var presentation = CommandResultPresentationMapper.FromOptimization(summary);
        var message = presentation.DisplayText;

        return new OperationError
        {
            ItemName = item.Title,
            ErrorMessage = message,
            Exception = summary.Exception
        };
    }

    private static OperationError BuildOperationError(OptimizationToggleModel item, Exception exception)
    {
        var presentation = CommandResultPresentationMapper.FromException(exception);
        return new OperationError
        {
            ItemName = item.Title,
            ErrorMessage = presentation.DisplayText,
            Exception = exception
        };
    }
}
