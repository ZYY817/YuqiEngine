// File: GameOptimizer.Shell/Models/BatchResult.cs
using System.Collections.Generic;
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// Represents the result of a batch optimization operation.
/// Contains success/failure counts, detailed errors and authoritative engine summaries.
/// </summary>
public sealed class BatchResult
{
    /// <summary>
    /// Gets the number of operations that reached the requested target state.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Gets the number of operations that did not reach the requested target state.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Gets the list of errors that occurred during the batch operation.
    /// Empty list if no errors occurred.
    /// </summary>
    public IReadOnlyList<OperationError> Errors { get; init; } = new List<OperationError>();

    /// <summary>
    /// Gets authoritative per-operation execution summaries returned by the engine.
    /// </summary>
    public IReadOnlyList<OperationExecutionSummary> OperationSummaries { get; init; } = new List<OperationExecutionSummary>();
}