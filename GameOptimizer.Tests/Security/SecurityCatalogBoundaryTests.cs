using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Security;
using GameOptimizer.Core.Services.Repair.RepairOperations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Security;

public sealed class SecurityCatalogBoundaryTests
{
    private static readonly string[] DeniedIds = [];

    [Fact]
    public void ProviderContainsNoFailClosedSecurityReductionEntries()
    {
        var operations = new BuiltInOptimizationOperationProvider()
            .GetModules().SelectMany(module => module.Operations).ToArray();
        Assert.Empty(operations.OfType<FailClosedSecurityOptimizationOperation>());
    }

    [Fact]
    public void MigratedSecurityReductionsAreTypedAndExplicitlyConfirmed()
    {
        var migratedIds = new[]
        {
            "basic.disable_uac",
            "basic.disable_security_notifications",
            "basic.disable_windows_update",
            "basic.disable_firewall",
            "basic.disable_security_center",
            "perf.wuauserv.disable",
            "cpu.disable_spectre_meltdown",
            "security.disable_cfg",
            "security.disable_defender_realtime",
            "security.disable_hvci",
            "security.disable_sehop",
            "security.disable_smartscreen",
            "security.disable_vbs"
        };
        var operations = new BuiltInOptimizationOperationProvider()
            .GetModules().SelectMany(module => module.Operations).ToArray();

        Assert.All(migratedIds, id =>
        {
            var operation = Assert.Single(operations, candidate => candidate.Id == id);
            Assert.IsAssignableFrom<OrchestratedChangeOperation>(operation);
            Assert.True(operation.RequiresExplicitConfirmation);
        });
    }

    [Fact]
    public async Task CatalogEntriesCannotSucceedOrWriteLegacySnapshots()
    {
        var store = new CountingRestoreStore();
        var context = new OperationExecutionContext(store, NullLogger.Instance);
        foreach (var operation in GetDeniedOperations())
        {
            Assert.False((await operation.GetStatusAsync(default)).IsCompliant);
            var apply = await operation.ApplyAsync(context, default);
            var restore = await operation.RestoreAsync(context, default);
            Assert.False(apply.Success);
            Assert.False(apply.Changed);
            Assert.Contains("PolicyDenied", apply.Message, StringComparison.Ordinal);
            Assert.False(restore.Success);
            Assert.False(restore.Changed);
        }
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public void AuthorizedSecurityEntriesContainNoLegacyDirectWriter()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            @"GameOptimizer.Core\Optimization\Providers\BuiltInOptimizationOperationProvider.cs",
            @"GameOptimizer.Core\Optimization\Operations\SecurityMitigationOperation.cs",
            @"GameOptimizer.Core\Optimization\Operations\SpectreMeltdownMitigationOperation.cs",
            @"GameOptimizer.Core\Services\WindowsFeatureHealthHelper.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\OptimizationRevertRepairs.cs",
            @"GameOptimizer.Infrastructure.Windows\Extensions\ServiceCollectionExtensions.cs"
        };
        var forbidden = new[]
        {
            "Set-MpPreference",
            "DisableRealtimeMonitoring\", 1",
            "EnableLUA\", 0",
            "SmartScreenEnabled\", \"Off",
            "DeleteValue(\"EnableSmartScreen",
            "advfirewall reset",
            "advfirewall set allprofiles state off",
            "Get-NetFirewallProfile",
            "new DisableDefenderRealtimeOperation",
            "new DisableSmartScreenOperation",
            "new SpectreMeltdownMitigationOperation"
        };

        foreach (var relative in files)
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            foreach (var token in forbidden)
                Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SmartScreenRepairEntryFailsClosedInsteadOfReportingSuccess()
    {
        var repair = OptimizationRevertRepairs.GetAll()
            .Single(item => item.RelatedOptimization == "security.disable_smartscreen");

        Assert.False(await repair.CheckHealthAsync());
        var result = await repair.RepairAsync(null);

        Assert.False(result.Success);
        Assert.Contains("typed adapter", result.Detail, StringComparison.Ordinal);
        Assert.Contains("未执行注册表写入", result.Detail, StringComparison.Ordinal);
    }

    private static IOptimizationOperation[] GetDeniedOperations() =>
        new BuiltInOptimizationOperationProvider()
            .GetModules()
            .SelectMany(module => module.Operations)
            .Where(operation => DeniedIds.Contains(operation.Id, StringComparer.Ordinal))
            .OrderBy(operation => operation.Id, StringComparer.Ordinal)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class CountingRestoreStore : IOperationRestoreStore
    {
        public int CallCount { get; private set; }
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) { CallCount++; return Task.CompletedTask; }
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) { CallCount++; return Task.FromResult<string?>(null); }
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) { CallCount++; return Task.CompletedTask; }
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) { CallCount++; return Task.FromResult(false); }
    }
}
