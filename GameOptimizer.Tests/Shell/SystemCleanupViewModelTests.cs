using GameOptimizer.Core.Services.SystemCleanup;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Tests.Shell;

public sealed class SystemCleanupViewModelTests
{
    [Fact]
    public async Task CancelDuringPreview_ClearsBusyAndInvalidatesPartialScan()
    {
        var service = new CancellablePreviewService();
        using var vm = new SystemCleanupViewModel(service);

        var scan = vm.RunScannerActionAsync();
        await service.PreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsScanning);

        vm.CancelCommand.Execute(null);
        await scan.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(vm.IsScanning);
        Assert.False(vm.IsWorking);
        Assert.False(vm.HasScanned);
        Assert.Equal(0, vm.ScanProgress);
        Assert.Equal(CleanupState.Idle, Assert.Single(vm.CleanupItems).State);
    }

    [Fact]
    public async Task CancelDuringQuarantineRestore_ClearsBusyAndReportsRecoveryRecheck()
    {
        var manager = new CancellableQuarantineManager();
        using var vm = new SystemCleanupViewModel(
            new CancellablePreviewService(),
            quarantineManager: manager,
            restoreConfirmation: (_, _) => true);

        var restore = vm.RestoreQuarantineCommand.ExecuteAsync(null);
        await manager.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsRestoring);

        vm.CancelCommand.Execute(null);
        await restore.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(vm.IsRestoring);
        Assert.False(vm.IsWorking);
        Assert.Contains("恢复清单", vm.StatusText);
    }

    private sealed class CancellablePreviewService : ISystemCleanupService
    {
        internal TaskCompletionSource PreviewStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CleanupItem _item = new() { Id = "isolated", Name = "隔离测试", TargetPaths = [] };

        public IReadOnlyList<CleanupItem> GetCleanupItems() => [_item];

        public async Task<CleanupPreview> CreatePreviewAsync(CleanupItem item, CancellationToken ct = default)
        {
            PreviewStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }

        public void InvalidatePreview(CleanupPreview preview) { }
        public Task<long> ScanItemAsync(CleanupItem item, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CleanupResult> CleanPreviewAsync(CleanupItem item, CleanupPreview preview, CleanupPreviewConfirmation confirmation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CleanupResult> CleanItemAsync(CleanupItem item, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CleanupResult> CleanAllAsync(IEnumerable<CleanupItem> items, IProgress<(int current, int total, string name)>? progress = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CancellableQuarantineManager : ICleanupQuarantineManager
    {
        internal TaskCompletionSource RestoreStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CleanupQuarantinePreview> CreateRestorePreviewAsync(CancellationToken cancellationToken = default)
        {
            var manifest = new CleanupQuarantineManifest(
                1, "op", Guid.NewGuid(), "item", "C:\\source", "C:\\", "C:\\quarantine",
                1, DateTime.UtcNow, "hash", "resource", CleanupQuarantineState.QuarantinedVerified,
                null, DateTimeOffset.UtcNow, [], "manifest-hash");
            return Task.FromResult(new CleanupQuarantinePreview(
                "restore", [new CleanupQuarantineEntry("manifest.json", manifest)], DateTimeOffset.UtcNow));
        }

        public async Task<CleanupQuarantineResult> RestoreAsync(CleanupQuarantinePreview preview, CleanupQuarantineConfirmation confirmation, CancellationToken cancellationToken = default)
        {
            RestoreStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new CleanupQuarantineResult();
        }

        public Task<CleanupQuarantinePreview> CreateRetentionPreviewAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CleanupQuarantineResult> StageRetentionAsync(CleanupQuarantinePreview preview, CleanupQuarantineConfirmation confirmation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CleanupQuarantineReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
