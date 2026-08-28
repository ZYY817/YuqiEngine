using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

public sealed class ResidualRepairOwnerRoutingTests
{
    private static readonly string[] ExpectedIds =
    [
        "repair.activity_history", "repair.iso_mount", "repair.standby_list", "repair.working_set"
    ];

    [Fact]
    public void Catalog_FreezesFourUniqueMutuallyExclusiveSemantics()
    {
        Assert.Equal(4, ResidualRepairCatalog.All.Count);
        Assert.Equal(
            ExpectedIds.Order(StringComparer.OrdinalIgnoreCase),
            ResidualRepairCatalog.All.Select(item => item.RepairItemId).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(4, ResidualRepairCatalog.All
            .Select(item => item.ResourceKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());

        var activityHistory = Find("repair.activity_history");
        Assert.Equal(ResidualRepairSemantic.ActivityHistoryDataDeletion, activityHistory.IntendedSemantic);
        Assert.Equal(
            [ResidualRepairSemantic.FutureActivityUploadPolicy],
            activityHistory.ExcludedSemantics);
        Assert.Equal(ResidualRepairSemantic.IsoImageMountLifecycle, Find("repair.iso_mount").IntendedSemantic);
        Assert.Equal(ResidualRepairSemantic.StandbyListPurge, Find("repair.standby_list").IntendedSemantic);
        Assert.Equal(ResidualRepairSemantic.ProcessWorkingSetTrim, Find("repair.working_set").IntendedSemantic);

        foreach (var definition in ResidualRepairCatalog.All)
        {
            Assert.True(UnavailableRepairChangeExecutor.Instance.IsManaged(definition.RepairItemId));
            Assert.False(RepairSpecializedOwnerCatalog.Contains(definition.RepairItemId));
            Assert.False(FileShellRepairCatalog.Contains(definition.RepairItemId));
            Assert.False(GraphicsDisplayRepairCatalog.Contains(definition.RepairItemId));
            Assert.False(NetworkRepairCatalog.Contains(definition.RepairItemId));
            Assert.False(BootMemoryRepairCatalog.Contains(definition.RepairItemId));
        }
    }

    [Fact]
    public async Task RepairService_RoutesAllFourThroughSingleResidualOwner()
    {
        var owner = new RecordingResidualOwner();
        var executor = CreateExecutor(owner);
        var service = new RepairService(new RepairItemRegistry(), new AllowGate(), executor);

        foreach (var id in ExpectedIds)
        {
            var item = Assert.Single(service.GetAllRepairItems(), candidate => candidate.Id == id);
            var result = await service.RepairAsync(item);

            Assert.False(result.Success);
            Assert.Null(result.ChangeReceipt);
        }

        Assert.Equal(ExpectedIds, owner.ExecutedIds);
        Assert.Empty(owner.RestoredIds);
    }

    [Fact]
    public async Task FixedOwner_FailsClosedWithoutReceiptForExecuteAndRestore()
    {
        var executor = CreateExecutor();
        foreach (var id in ExpectedIds)
        {
            var legacyInvoked = false;
            var executed = await executor.ExecuteAsync(
                Item(id, () => legacyInvoked = true),
                null,
                CancellationToken.None);

            Assert.False(executed.Success);
            Assert.Contains(nameof(ResidualRepairOwnerStatus.PolicyDenied), executed.Detail, StringComparison.Ordinal);
            Assert.Null(executed.ChangeReceipt);
            Assert.False(legacyInvoked);

            var fakeReceipt = new RepairChangeReceipt(Guid.NewGuid(), id, [], false, DateTimeOffset.UtcNow);
            var restored = await executor.RestoreAsync(
                new RepairRecoveryRequest(id, fakeReceipt),
                null,
                CancellationToken.None);

            Assert.False(restored.Success);
            Assert.Contains(nameof(ResidualRepairOwnerStatus.RestoreUnproven), restored.Detail, StringComparison.Ordinal);
            Assert.Null(restored.ChangeReceipt);
        }
    }

    [Fact]
    public async Task Cancellation_IsNamedAndNeverFallsBackToWriter()
    {
        var legacyInvoked = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await CreateExecutor().ExecuteAsync(
            Item("repair.working_set", () => legacyInvoked = true),
            null,
            cancellation.Token);

        Assert.False(result.Success);
        Assert.Contains(nameof(ResidualRepairOwnerStatus.Cancelled), result.Detail, StringComparison.Ordinal);
        Assert.Null(result.ChangeReceipt);
        Assert.False(legacyInvoked);
    }

    private static ResidualRepairDefinition Find(string repairItemId) =>
        Assert.Single(ResidualRepairCatalog.All, item => item.RepairItemId == repairItemId);

    private static OrchestratedRepairChangeExecutor CreateExecutor(IResidualRepairOwner? owner = null) =>
        new(
            new ThrowingOrchestrator(),
            new ThrowingConfirmationAuthority(),
            TimeProvider.System,
            networkResetOwner: null,
            residualRepairOwner: owner);

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

    private sealed class RecordingResidualOwner : IResidualRepairOwner
    {
        public List<string> ExecutedIds { get; } = [];
        public List<string> RestoredIds { get; } = [];

        public Task<RepairResult> ExecuteAsync(
            ResidualRepairDefinition definition,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken)
        {
            ExecutedIds.Add(definition.RepairItemId);
            return Task.FromResult(RepairResult.Failed("fake owner fail-closed"));
        }

        public Task<RepairResult> RestoreAsync(
            ResidualRepairDefinition definition,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken)
        {
            RestoredIds.Add(definition.RepairItemId);
            return Task.FromResult(RepairResult.Failed("fake owner restore fail-closed"));
        }
    }

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
}
