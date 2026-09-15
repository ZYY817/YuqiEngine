namespace GameOptimizer.Core.Services.SystemCleanup;

public interface ISystemCleanupService
{
    IReadOnlyList<CleanupItem> GetCleanupItems();
    Task<CleanupPreview> CreatePreviewAsync(CleanupItem item, CancellationToken ct = default);
    Task<CleanupPreview> CreatePreviewAsync(
        CleanupItem item,
        IProgress<CleanupScanProgress>? progress,
        CancellationToken ct = default) => CreatePreviewAsync(item, ct);
    void InvalidatePreview(CleanupPreview preview);
    Task<long> ScanItemAsync(CleanupItem item, CancellationToken ct = default);
    Task<CleanupResult> CleanPreviewAsync(CleanupItem item, CleanupPreview preview, CleanupPreviewConfirmation confirmation, CancellationToken ct = default);
    Task<CleanupResult> CleanItemAsync(CleanupItem item, CancellationToken ct = default);
    Task<CleanupResult> CleanAllAsync(IEnumerable<CleanupItem> items, IProgress<(int current, int total, string name)>? progress = null, CancellationToken ct = default);
}
