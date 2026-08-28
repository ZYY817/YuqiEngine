// File: GameOptimizer.Shell/Services/IOptimizationExecutor.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// Interface for executing optimization operations.
/// Allows testing with stub implementations.
/// </summary>
public interface IOptimizationExecutor
{
    Task<OptimizationBatchResult> ApplyOperationsAsync(
        string moduleKey,
        IEnumerable<string> operationIds,
        IOperationProgress? progress);

    // FIX-CT-PASS: CancellationToken 重载，允许批量操作服务传递取消令牌
    Task<OptimizationBatchResult> ApplyOperationsAsync(
        string moduleKey,
        IEnumerable<string> operationIds,
        IOperationProgress? progress,
        CancellationToken cancellationToken);

    Task<OptimizationBatchResult> RestoreOperationsAsync(
        string moduleKey,
        IEnumerable<string> operationIds);

    // FIX-CT-PASS: CancellationToken 重载
    Task<OptimizationBatchResult> RestoreOperationsAsync(
        string moduleKey,
        IEnumerable<string> operationIds,
        CancellationToken cancellationToken);
}
