using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.NetworkReset;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// Verifies the canonical RepairService entry keeps adapter/DHCP semantics fail-closed.
/// No network owner or platform command is allowed to run in this suite.
/// </summary>
public sealed class NetworkAdapterDhcpRepairEntryCompatibilityTests
{
    [Theory]
    [InlineData("repair.network_adapter")]
    [InlineData("repair.dhcp")]
    public async Task RepairService_RealEntry_FailsClosedWithoutCallingFullResetOwner(string repairItemId)
    {
        var owner = new RecordingNetworkResetOwner();
        var service = CreateService(owner, new FixedRepairGate(RepairExecutionGateDecision.Allowed()));
        var item = Assert.Single(service.GetAllRepairItems(), candidate => candidate.Id == repairItemId);

        var result = await service.RepairAsync(item);

        Assert.False(result.Success);
        Assert.Contains("阻断", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, owner.RunInteractiveCount);
        Assert.Equal(0, owner.ExecuteCount);
    }

    [Theory]
    [InlineData(RepairExecutionGateDecisionKind.ReadonlyOnly, "permission")]
    [InlineData(RepairExecutionGateDecisionKind.BlockedNeedBaseline, "policy")]
    public async Task RepairService_OuterPermissionOrPolicyGate_BlocksBeforeNetworkOwner(
        RepairExecutionGateDecisionKind decisionKind,
        string marker)
    {
        var owner = new RecordingNetworkResetOwner();
        var service = CreateService(
            owner,
            new FixedRepairGate(new RepairExecutionGateDecision(decisionKind, marker)));
        var item = Assert.Single(service.GetAllRepairItems(), candidate => candidate.Id == "repair.dhcp");

        var result = await service.RepairAsync(item);

        Assert.False(result.Success);
        Assert.Equal(marker, result.Message);
        Assert.Equal(0, owner.RunInteractiveCount);
        Assert.Equal(0, owner.ExecuteCount);
    }

    [Theory]
    [InlineData("repair.network_adapter")]
    [InlineData("repair.dhcp")]
    public async Task RepairService_CancellationBeforeEntry_IsZeroNetworkWrite(string repairItemId)
    {
        var owner = new RecordingNetworkResetOwner();
        var service = CreateService(owner, new FixedRepairGate(RepairExecutionGateDecision.Allowed()));
        var item = Assert.Single(service.GetAllRepairItems(), candidate => candidate.Id == repairItemId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.RepairAsync(item, cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(0, owner.RunInteractiveCount);
        Assert.Equal(0, owner.ExecuteCount);
    }

    [Theory]
    [InlineData("repair.network_adapter")]
    [InlineData("repair.dhcp")]
    public async Task RepairService_RestoreEntry_RejectsUnsupportedRecoveryWithoutNetworkWrite(string repairItemId)
    {
        var owner = new RecordingNetworkResetOwner();
        var service = CreateService(owner, new FixedRepairGate(RepairExecutionGateDecision.Allowed()));
        var receipt = new RepairChangeReceipt(
            Guid.NewGuid(),
            repairItemId,
            [],
            CompensationAttempted: false,
            DateTimeOffset.UtcNow);

        var result = await service.RestoreAsync(new RepairRecoveryRequest(repairItemId, receipt));

        Assert.False(result.Success);
        Assert.Contains("Network Reset Owner", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, owner.RunInteractiveCount);
        Assert.Equal(0, owner.ExecuteCount);
    }

    private static RepairService CreateService(
        RecordingNetworkResetOwner owner,
        IRepairExecutionGate gate) =>
        new(
            new RepairItemRegistry(networkResetOwner: owner),
            gate,
            new OrchestratedRepairChangeExecutor(
                new ThrowingOrchestrator(),
                new ThrowingConfirmationAuthority(),
                TimeProvider.System,
                owner));

    private sealed class FixedRepairGate(RepairExecutionGateDecision decision) : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(decision);
        }
    }

    private sealed class RecordingNetworkResetOwner : INetworkResetOwner
    {
        public int RunInteractiveCount { get; private set; }
        public int ExecuteCount { get; private set; }

        public Task<NetworkResetReadOnlyStatus> ReadStatusAsync(
            NetworkResetProfile profile,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Network adapter/DHCP entry must not probe Full reset owner.");

        public Task<NetworkResetPlan> PrepareAsync(
            NetworkResetProfile profile,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Network adapter/DHCP entry must not prepare Full reset.");

        public Task<NetworkResetReceipt> ExecuteAsync(
            NetworkResetPlan plan,
            ConfirmationToken confirmation,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            throw new InvalidOperationException("Network adapter/DHCP entry must not execute Full reset.");
        }

        public Task<NetworkResetReceipt> RunInteractiveAsync(
            NetworkResetProfile profile,
            CancellationToken cancellationToken = default)
        {
            RunInteractiveCount++;
            throw new InvalidOperationException("Network adapter/DHCP entry must not run Full reset.");
        }

        public Task<IReadOnlyList<NetworkResetReceipt>> GetPendingRecoveryReceiptsAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Network adapter/DHCP entry must not inspect Full reset recovery.");
    }

    private sealed class ThrowingOrchestrator : IChangeOrchestrator
    {
        public Task<StateObservation?> ObserveAsync(
            string capabilityId,
            TargetScope scope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic change orchestrator must not run.");

        public Task<PreparationResult> PrepareAsync(
            ChangeRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic change orchestrator must not run.");

        public Task<BatchReceipt> ExecuteAsync(
            PreparedChangePlan plan,
            ConfirmationToken confirmation,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic change orchestrator must not run.");

        public Task<BatchReceipt> RestoreAsync(
            RestoreRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic change orchestrator must not run.");
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
}
