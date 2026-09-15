using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Shell;

public sealed class ConfigurationExportServiceTests
{
    [Fact]
    public async Task ApplyConfiguration_UsesCatalogModuleOwnershipInsteadOfOperationPrefix()
    {
        var operation = new FakeOperation("gaming.mode.enable");
        var engine = new FakeEngine(new OptimizationModuleDescriptor(
            "performance",
            "Performance",
            "test",
            new[] { operation }));
        var service = CreateService(engine);

        var result = await service.ApplyConfigurationAsync(new ExportedConfiguration
        {
            AppliedOptimizations = new List<string> { operation.Id }
        });

        Assert.True(result.IsSuccess);
        var call = Assert.Single(engine.ApplyCalls);
        Assert.Equal("performance", call.ModuleKey);
        Assert.Equal(new[] { operation.Id }, call.OperationIds);
    }

    [Fact]
    public async Task ApplyConfiguration_UnknownOperationBlocksWholeProfileBeforeWrites()
    {
        var operation = new FakeOperation("gaming.mode.enable");
        var engine = new FakeEngine(new OptimizationModuleDescriptor(
            "performance",
            "Performance",
            "test",
            new[] { operation }));
        var service = CreateService(engine);

        var result = await service.ApplyConfigurationAsync(new ExportedConfiguration
        {
            AppliedOptimizations = new List<string> { operation.Id, "missing.operation" }
        });

        Assert.True(result.IsFailed);
        Assert.Empty(engine.ApplyCalls);
        Assert.Contains("missing.operation", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyConfiguration_WrongScopeBlocksBeforeWrites()
    {
        var operation = new FakeOperation("gaming.mode.enable");
        var engine = new FakeEngine(new OptimizationModuleDescriptor(
            "performance",
            "Performance",
            "test",
            new[] { operation }));
        var service = CreateService(engine);

        var result = await service.ApplyConfigurationAsync(new ExportedConfiguration
        {
            Scope = "diagnostic-log",
            AppliedOptimizations = new List<string> { operation.Id }
        });

        Assert.True(result.IsFailed);
        Assert.Empty(engine.ApplyCalls);
    }

    private static ConfigurationExportService CreateService(IOptimizationEngine engine) =>
        new(NullLogger<ConfigurationExportService>.Instance, engine);

    private sealed class FakeEngine(params OptimizationModuleDescriptor[] modules) : IOptimizationEngine
    {
        public IReadOnlyList<OptimizationModuleDescriptor> Modules { get; } = modules;
        public List<(string ModuleKey, string[] OperationIds)> ApplyCalls { get; } = new();

        public Task<OptimizationBatchResult> ApplyAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            ApplyAsync(moduleKey, Modules.Single(module => module.Key == moduleKey).Operations.Select(operation => operation.Id), cancellationToken);

        public Task<OptimizationBatchResult> ApplyAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            CancellationToken cancellationToken = default) =>
            ApplyAsync(moduleKey, operationIds, progress: null, cancellationToken);

        public Task<OptimizationBatchResult> ApplyAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            IOperationProgress? progress,
            CancellationToken cancellationToken = default)
        {
            var ids = operationIds.ToArray();
            ApplyCalls.Add((moduleKey, ids));
            IReadOnlyList<OperationExecutionSummary> summaries = ids.Select(id => new OperationExecutionSummary(
                id,
                id,
                Success: true,
                Changed: true,
                "applied",
                new OperationStatus(false, "before"),
                new OperationStatus(true, "after"))).ToArray();
            return Task.FromResult(new OptimizationBatchResult(moduleKey, summaries));
        }

        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<OperationStatus>> GetStatusesAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeOperation(string id) : IOptimizationOperation
    {
        public string Id { get; } = id;
        public string Title => Id;
        public string Description => "test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin => false;

        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationStatus(false, "test"));
        public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
