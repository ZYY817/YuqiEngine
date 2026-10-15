using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Extensions;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class OptimizationProviderCanonicalArchitectureTests
{
    [Fact]
    public void CanonicalCatalog_HasStableModuleCountsAndUniqueOperationIds()
    {
        var modules = new BuiltInOptimizationOperationProvider().GetModules().ToArray();
        var counts = modules.ToDictionary(module => module.Key, module => module.Operations.Count);
        var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["performance"] = 20,
            ["network"] = 29,
            ["privacy"] = 37,
            ["system"] = 23,
            ["cleanup"] = 9,
            ["explorer"] = 13,
            ["cpu"] = 14,
            ["graphics"] = 9,
            ["peripheral"] = 17,
            ["tasks"] = 16,
            ["services"] = 12,
            ["memory"] = 6,
            ["audio"] = 7
        };

        Assert.Equal(expected, counts);
        var operations = modules.SelectMany(module => module.Operations).ToArray();
        Assert.Equal(212, operations.Length);
        Assert.Equal(operations.Length, operations.Select(operation => operation.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void CanonicalCatalog_ContainsOnlyOrchestratedOwnerAdaptersOrFailClosedEntries()
    {
        var operations = new BuiltInOptimizationOperationProvider()
            .GetModules()
            .SelectMany(module => module.Operations)
            .ToArray();

        Assert.All(operations, operation => Assert.True(
            operation is OrchestratedChangeOperation
                or PolicyDeniedOptimizationOperation
                or FailClosedServiceOptimizationOperation
                or FailClosedSecurityOptimizationOperation
                or FailClosedGraphicsOptimizationOperation
                or FailClosedFileShellOptimizationOperation,
            $"{operation.Id} exposes forbidden active type {operation.GetType().FullName}."));
    }

    [Fact]
    public void CoreComposition_MapsConcreteAndContractToTheSameProviderInstance()
    {
        var services = new ServiceCollection();
        services.AddGameOptimizerCore();
        services.AddSingleton<IChangeOrchestrator, StubChangeOrchestrator>();

        using var serviceProvider = services.BuildServiceProvider();
        var concrete = serviceProvider.GetRequiredService<BuiltInOptimizationOperationProvider>();
        var contract = serviceProvider.GetRequiredService<IOptimizationOperationProvider>();

        Assert.Same(concrete, contract);
        Assert.Single(serviceProvider.GetServices<IOptimizationOperationProvider>());
    }

    [Fact]
    public void Engine_RejectsMissingOrMultipleCatalogProviders()
    {
        var provider = new BuiltInOptimizationOperationProvider();
        var store = new StubRestoreStore();

        Assert.Throws<InvalidOperationException>(() =>
            new OptimizationEngine(Array.Empty<IOptimizationOperationProvider>(), store, NullLoggerFactory.Instance));
        Assert.Throws<InvalidOperationException>(() =>
            new OptimizationEngine([provider, provider], store, NullLoggerFactory.Instance));
    }

    [Fact]
    public void ProductionConsumers_DoNotConstructFallbackBuiltInProviders()
    {
        var root = FindRepositoryRoot();
        var consumers = new[]
        {
            @"GameOptimizer.Core\Services\Snapshot\SnapshotService.cs"
        };

        foreach (var relativePath in consumers)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("new BuiltInOptimizationOperationProvider", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GameOptimizer.Core.Optimization.Providers.BuiltInOptimizationOperationProvider", source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class StubChangeOrchestrator : IChangeOrchestrator
    {
        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<StateObservation?>(null);

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken, bool registerPlan = true) =>
            Task.FromResult(new PreparationResult(ChangePreparationStatus.Blocked, null, "test.blocked", "Blocked for architecture test.", null));

        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Execution is not available in an architecture test.");

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Restore is not available in an architecture test.");
    }

    private sealed class StubRestoreStore : IOperationRestoreStore
    {
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
