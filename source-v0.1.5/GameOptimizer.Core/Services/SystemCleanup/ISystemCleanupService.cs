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
    // Why: 已移除 CleanAllAsync——它依赖无确认的批量路径且无任何调用者；
    // 批量清理必须走逐项 CreatePreviewAsync → 用户确认 → CleanPreviewAsync。
}
