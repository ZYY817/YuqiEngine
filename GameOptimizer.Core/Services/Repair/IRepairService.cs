namespace GameOptimizer.Core.Services.Repair;

public interface IRepairService
{
    IReadOnlyList<RepairItem> GetAllRepairItems();
    
    IEnumerable<RepairItem> GetByCategory(RepairCategory category);
    
    IEnumerable<RepairItem> Search(string query);
    
    Task<ScanResult> ScanForIssuesAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
    
    Task<RepairResult> RepairAsync(RepairItem item, IProgress<RepairProgress>? progress = null, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<RepairResult>> RepairAllAsync(IEnumerable<RepairItem> items, IProgress<RepairProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<RepairResult> RestoreAsync(RepairRecoveryRequest request, IProgress<RepairProgress>? progress = null, CancellationToken cancellationToken = default);
    
    IReadOnlyList<RepairLogEntry> GetRepairHistory(int count = 50);
    
    void ClearHistory();
}
