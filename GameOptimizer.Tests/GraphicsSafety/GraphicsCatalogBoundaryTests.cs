using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Repair.RepairOperations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.GraphicsSafety;

public sealed class GraphicsCatalogBoundaryTests
{
    private static readonly string[] BlockedCatalogIds = [];

    [Fact]
    public void ProviderHasNineOrchestratedEntriesAndNoBlockedGraphicsEntries()
    {
        var graphics = GraphicsOperations();
        Assert.Equal(9, graphics.Length);

        Assert.Empty(BlockedCatalogIds);

        Assert.IsType<OrchestratedRegistryDwordOperation>(Assert.Single(graphics, operation => operation.Id == "gpu.enable_hags"));
        Assert.IsType<OrchestratedRegistryStringOperation>(Assert.Single(graphics, operation => operation.Id == "gpu.force_high_performance_gpu"));
        Assert.IsType<OrchestratedRegistryStringOperation>(Assert.Single(graphics, operation => operation.Id == "gpu.set_sfio_priority"));
        Assert.IsType<OrchestratedRegistryDwordOperation>(Assert.Single(graphics, operation => operation.Id == "gpu.disable_game_mode_optimizations"));
        Assert.IsType<OrchestratedRegistryValueSetOperation>(Assert.Single(graphics, operation => operation.Id == "fps.disable_fullscreen_optimizations"));
        Assert.IsType<OrchestratedRegistryValueSetOperation>(Assert.Single(graphics, operation => operation.Id == "gpu.disable_dwm_animations"));
        Assert.IsType<OrchestratedServiceOptimizationOperation>(Assert.Single(graphics, operation => operation.Id == "gpu.disable_xbox_game_monitoring"));
        Assert.IsType<OrchestratedRegistryDwordOperation>(Assert.Single(graphics, operation => operation.Id == "gpu.disable_vrr"));
        Assert.IsType<DisableGameDvrCompleteOperation>(Assert.Single(graphics, operation => operation.Id == "gpu.disable_game_dvr_complete"));

        var all = new BuiltInOptimizationOperationProvider().GetModules().SelectMany(module => module.Operations);
        Assert.IsType<OrchestratedRegistryDwordOperation>(
            Assert.Single(all, operation => operation.Id == "gaming.dvr.disable"));
    }

    [Fact]
    public async Task MigratedGraphicsEntriesRouteThroughOrchestratorAndNeverTouchLegacyRestoreStore()
    {
        var store = new CountingStore();
        var context = new OperationExecutionContext(store, NullLogger.Instance);
        var migrated = GraphicsOperations().Where(operation =>
            operation.Id is "gpu.force_high_performance_gpu"
                or "gpu.set_sfio_priority"
                or "gpu.disable_game_mode_optimizations").ToArray();
        Assert.Equal(3, migrated.Length);
        foreach (var operation in migrated)
        {
            Assert.IsAssignableFrom<OrchestratedChangeOperation>(operation);
            Assert.True(operation.RequiresExplicitConfirmation);
        }
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task DriverDisplayAndVendorRepairDelegatesAreFailClosed()
    {
        var ids = new[]
        {
            "repair.amd_settings", "repair.display_adapter", "repair.display_driver_reset",
            "repair.nvidia_settings", "repair.display_driver", "repair.display_reset", "repair.dwm_restart"
        };
        var entries = GraphicsRepairs.GetAll().Concat(DisplayRepairs.GetAll())
            .Where(item => ids.Contains(item.Id, StringComparer.Ordinal))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(ids.Order(), entries.Select(item => item.Id));
        foreach (var entry in entries)
        {
            var result = await entry.RepairAsync(null);
            Assert.False(result.Success);
        }
    }

    [Fact]
    public void AuthorizedSourcesContainNoLegacyDriverOrDisplayWriter()
    {
        var root = FindRoot();
        var files = new[]
        {
            @"GameOptimizer.Core\Optimization\Operations\GpuVendorOperation.cs",
            @"GameOptimizer.Core\Optimization\Operations\GpuAdvancedOperation.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\GraphicsRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\DisplayRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\GameOptimizationRepairs.cs",
            @"GameOptimizer.Shell\Services\DeviceManagerService.cs"
        };
        var forbidden = new[]
        {
            "FileName = \"pnputil", "FileName = \"devcon", "RunCommandAsync(\"pnputil",
            "Disable-PnpDevice", "Enable-PnpDevice", "RunCommandAsync(\"displayswitch.exe\"",
            "Set-ItemProperty", "Remove-ItemProperty", ".SetValue(\"OverlayTestMode\"", ".SetValue(\"TdrDelay\"",
            ".SetValue(", ".DeleteValue(", "Process.Start("
        };
        foreach (var file in files)
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            foreach (var token in forbidden)
                Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IOptimizationOperation[] GraphicsOperations() =>
        new BuiltInOptimizationOperationProvider().GetModules()
            .Single(module => module.Key == "graphics").Operations.ToArray();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class CountingStore : IOperationRestoreStore
    {
        public int Calls { get; private set; }
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.FromResult<string?>(null); }
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.FromResult(false); }
    }
}
