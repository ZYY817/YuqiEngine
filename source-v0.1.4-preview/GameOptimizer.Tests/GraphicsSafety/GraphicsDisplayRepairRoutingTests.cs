using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.GraphicsSafety;

public sealed class GraphicsDisplayRepairRoutingTests
{
    [Fact]
    public void Catalog_HasEightUniqueFailClosedEntriesAndNoTypedReuse()
    {
        Assert.Equal(8, GraphicsDisplayRepairCatalog.All.Count);
        Assert.Equal(8, GraphicsDisplayRepairCatalog.All
            .Select(item => item.RepairItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
        Assert.Equal(2, Count(GraphicsMutationKind.VendorPrivateSetting));
        Assert.Equal(3, Count(GraphicsMutationKind.DriverLifecycle));
        Assert.Equal(1, Count(GraphicsMutationKind.DisplayTopology));
        Assert.Equal(1, Count(GraphicsMutationKind.DwmLifecycle));
        Assert.Equal(1, Count(GraphicsMutationKind.ExistingTypedRegistryCapability));

        var fullscreen = Assert.Single(GraphicsDisplayRepairCatalog.All,
            item => item.RepairItemId == "repair.fullscreen_optimization");
        Assert.Equal("graphics-semantic-mismatch-policy-denied", fullscreen.OwnerClassification);

        foreach (var definition in GraphicsDisplayRepairCatalog.All)
        {
            Assert.True(UnavailableRepairChangeExecutor.Instance.IsManaged(definition.RepairItemId));
        }
    }

    [Fact]
    public async Task Executor_RoutesAllEightToGraphicsBoundaryWithoutLegacyOrGenericWriter()
    {
        var executor = CreateExecutor();
        foreach (var definition in GraphicsDisplayRepairCatalog.All)
        {
            var legacyInvoked = false;
            var result = await executor.ExecuteAsync(
                Item(definition.RepairItemId, () => legacyInvoked = true),
                null,
                CancellationToken.None);

            Assert.True(executor.IsManaged(definition.RepairItemId));
            Assert.False(result.Success);
            Assert.False(legacyInvoked);
            Assert.Equal(GraphicsSafetyStatus.PolicyDenied.ToString(), result.Detail);
        }
    }

    [Fact]
    public async Task RepairService_RoutesEveryCatalogEntryThroughManagedExecutor()
    {
        var executor = new RecordingRepairExecutor();
        var service = new RepairService(new RepairItemRegistry(), new AllowGate(), executor);

        foreach (var definition in GraphicsDisplayRepairCatalog.All)
        {
            var item = Assert.Single(service.GetAllRepairItems(), candidate =>
                string.Equals(candidate.Id, definition.RepairItemId, StringComparison.OrdinalIgnoreCase));
            var result = await service.RepairAsync(item);
            Assert.False(result.Success);
        }

        Assert.Equal(
            GraphicsDisplayRepairCatalog.All.Select(item => item.RepairItemId).Order(StringComparer.OrdinalIgnoreCase),
            executor.ExecutedIds.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CancellationAndGenericReceiptRestoreRemainFailClosed()
    {
        var legacyInvoked = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await CreateExecutor().ExecuteAsync(
            Item("repair.display_driver", () => legacyInvoked = true),
            null,
            cancellation.Token);

        Assert.False(cancelled.Success);
        Assert.Equal(GraphicsSafetyStatus.Cancelled.ToString(), cancelled.Detail);
        Assert.False(legacyInvoked);

        var receipt = new RepairChangeReceipt(
            Guid.NewGuid(), "repair.display_driver", [], false, DateTimeOffset.UtcNow);
        var restored = await CreateExecutor().RestoreAsync(
            new RepairRecoveryRequest("repair.display_driver", receipt),
            null,
            CancellationToken.None);

        Assert.False(restored.Success);
        Assert.Equal(GraphicsSafetyStatus.PolicyDenied.ToString(), restored.Detail);
    }

    private static int Count(GraphicsMutationKind kind) =>
        GraphicsDisplayRepairCatalog.All.Count(item => item.MutationKind == kind);

    private static OrchestratedRepairChangeExecutor CreateExecutor() =>
        new(new ThrowingOrchestrator(), new ThrowingConfirmationAuthority(), TimeProvider.System);

    private static RepairItem Item(string id, Action onLegacy) => new()
    {
        Id = id,
        Name = id,
        Description = id,
        CheckHealthAsync = () => Task.FromResult(false),
        RepairAsync = _ =>
        {
            onLegacy();
            return Task.FromResult(RepairResult.Succeeded("legacy"));
        }
    };

    private sealed class ThrowingOrchestrator : IChangeOrchestrator
    {
        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic orchestrator must not run.");

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic orchestrator must not run.");

        public Task<BatchReceipt> ExecuteAsync(
            PreparedChangePlan plan,
            ConfirmationToken confirmation,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic orchestrator must not run.");

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic orchestrator must not run.");
    }

    private sealed class ThrowingConfirmationAuthority : IChangeConfirmationAuthority
    {
        public Task<ConfirmationToken?> RequestAsync(
            PreparedChangePlan plan,
            string title,
            string summary,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic confirmation must not run.");

        public bool TryConsume(PreparedChangePlan plan, ConfirmationToken token) =>
            throw new InvalidOperationException("Generic confirmation must not run.");
    }

    private sealed class AllowGate : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RepairExecutionGateDecision.Allowed());
    }

    private sealed class RecordingRepairExecutor : IRepairChangeExecutor
    {
        public List<string> ExecutedIds { get; } = [];

        public bool IsManaged(string repairItemId) => GraphicsDisplayRepairCatalog.Contains(repairItemId);

        public Task<RepairResult> ExecuteAsync(
            RepairItem item,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken)
        {
            ExecutedIds.Add(item.Id);
            return Task.FromResult(RepairResult.Failed("managed fail-closed"));
        }

        public Task<RepairResult> RestoreAsync(
            RepairRecoveryRequest request,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Restore is not part of this routing assertion.");
    }
}
