using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Optimization;

public interface IOptimizationEngine
{
    IReadOnlyList<OptimizationModuleDescriptor> Modules { get; }

    Task<OptimizationBatchResult> ApplyAsync(string moduleKey, CancellationToken cancellationToken = default);
    Task<OptimizationBatchResult> ApplyAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default);
    Task<OptimizationBatchResult> ApplyAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default);
    Task<OptimizationBatchResult> RestoreAsync(string moduleKey, CancellationToken cancellationToken = default);
    Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default);
    Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationStatus>> GetStatusesAsync(string moduleKey, CancellationToken cancellationToken = default);
}
