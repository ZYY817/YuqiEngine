using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.FileShellSafety;

public sealed class FileShellRepairRoutingTests
{
    [Fact]
    public void Catalog_HasFifteenUniqueEntriesWithExactMutationSemantics()
    {
        Assert.Equal(15, FileShellRepairCatalog.All.Count);
        Assert.Equal(15, FileShellRepairCatalog.All
            .Select(item => item.RepairItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());

        Assert.Equal(2, Count(FileShellMutationKind.ExplorerRestart));
        Assert.Equal(7, Count(FileShellMutationKind.ShellStateReset));
        Assert.Equal(4, Count(FileShellMutationKind.QuarantineFiles));
        Assert.Equal(2, Count(FileShellMutationKind.PermanentDelete));
        Assert.DoesNotContain(FileShellRepairCatalog.All, item => item.RepairItemId == "repair.iso_mount");
        Assert.DoesNotContain(FileShellRepairCatalog.All, item => item.RepairItemId == "repair.standby_list");
        Assert.DoesNotContain(FileShellRepairCatalog.All, item => item.RepairItemId == "repair.working_set");

        foreach (var definition in FileShellRepairCatalog.All)
        {
            Assert.True(UnavailableRepairChangeExecutor.Instance.IsManaged(definition.RepairItemId));
        }
    }

    [Fact]
    public async Task Executor_FailsClosedWithoutInvokingLegacyDelegateOrGenericOrchestrator()
    {
        var executor = CreateExecutor();
        foreach (var definition in FileShellRepairCatalog.All)
        {
            var legacyInvoked = false;
            var item = Item(definition.RepairItemId, () => legacyInvoked = true);

            var result = await executor.ExecuteAsync(item, null, CancellationToken.None);

            Assert.True(executor.IsManaged(definition.RepairItemId));
            Assert.False(result.Success);
            Assert.False(legacyInvoked);
            Assert.Contains(
                definition.MutationKind == FileShellMutationKind.QuarantineFiles
                    ? nameof(FileShellSafetyStatus.QuarantineOwnerUnavailable)
                    : nameof(FileShellSafetyStatus.PolicyDenied),
                result.Detail,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RepairService_RoutesEveryCatalogEntryThroughManagedExecutor()
    {
        var executor = new RecordingRepairExecutor();
        var registry = new RepairItemRegistry();
        var service = new RepairService(registry, new AllowGate(), executor);

        foreach (var definition in FileShellRepairCatalog.All)
        {
            var item = Assert.Single(service.GetAllRepairItems(), candidate =>
                string.Equals(candidate.Id, definition.RepairItemId, StringComparison.OrdinalIgnoreCase));
            var result = await service.RepairAsync(item);

            Assert.False(result.Success);
        }

        Assert.Equal(
            FileShellRepairCatalog.All.Select(item => item.RepairItemId).Order(StringComparer.OrdinalIgnoreCase),
            executor.ExecutedIds.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancellation_IsReportedWithoutFallingBackToLegacyDelegate()
    {
        var legacyInvoked = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await CreateExecutor().ExecuteAsync(
            Item("repair.temp_cleanup", () => legacyInvoked = true),
            null,
            cancellation.Token);

        Assert.False(result.Success);
        Assert.Contains(nameof(FileShellSafetyStatus.Cancelled), result.Detail, StringComparison.Ordinal);
        Assert.False(legacyInvoked);
    }

    [Fact]
    public async Task GenericRepairReceipt_CannotBeUsedAsFileShellRestoreAuthority()
    {
        var repairId = "repair.recycle_bin";
        var receipt = new RepairChangeReceipt(
            Guid.NewGuid(), repairId, [], false, DateTimeOffset.UtcNow);

        var result = await CreateExecutor().RestoreAsync(
            new RepairRecoveryRequest(repairId, receipt),
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(nameof(FileShellSafetyStatus.RestoreUnproven), result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("REPAIR.TEMP_CLEANUP")]
    [InlineData("repair.temp_cleanup")]
    public void Catalog_IsCaseInsensitiveAndHasOneOwnerClassification(string repairItemId)
    {
        Assert.True(FileShellRepairCatalog.TryGet(repairItemId, out var definition));
        Assert.Equal("quarantine-owner-unavailable", definition.OwnerClassification);
    }

    private static int Count(FileShellMutationKind kind) =>
        FileShellRepairCatalog.All.Count(item => item.MutationKind == kind);

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

        public bool IsManaged(string repairItemId) => FileShellRepairCatalog.Contains(repairItemId);

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
