namespace GameOptimizer.Core.Services.Repair;

public interface IOptimizationRevertService
{
    IReadOnlyList<RepairItem> GetAllRevertItems();
    bool IsDynamicRevertItem(string repairItemId);
    Task<OptimizationRevertStatus> GetStatusAsync(string operationId, CancellationToken cancellationToken = default);
    Task<OptimizationRevertPreparation> PrepareAsync(string operationId, CancellationToken cancellationToken = default);
    Task<RepairResult> ExecuteAsync(
        OptimizationRevertPlan plan,
        IProgress<RepairProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<RepairResult> RevertOperationAsync(string operationId, IProgress<RepairProgress>? progress = null, CancellationToken cancellationToken = default);
}
