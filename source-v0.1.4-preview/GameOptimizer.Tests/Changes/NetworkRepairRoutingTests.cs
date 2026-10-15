using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.NetworkReset;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Repair.RepairOperations;
using GameOptimizer.Infrastructure.Windows.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class NetworkRepairRoutingTests
{
    [Fact]
    public void Catalog_HasSixUniqueMutuallyExclusiveBindings()
    {
        Assert.Equal(6, NetworkRepairCatalog.All.Count);
        Assert.Equal(6, NetworkRepairCatalog.All.Select(item => item.RepairItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(4, NetworkRepairCatalog.All.Count(item => item.UsesNetworkResetOwner));
        Assert.Equal(2, NetworkRepairCatalog.All.Count(item => !item.UsesNetworkResetOwner));

        Assert.Equal(NetworkResetProfile.TcpIp, Find("repair.tcp_ip").Profile);
        Assert.Equal(NetworkResetProfile.Winsock, Find("repair.winsock").Profile);
        Assert.Equal(NetworkResetProfile.DnsCache, Find("repair.dns").Profile);
        Assert.Equal(NetworkResetProfile.Full, Find("repair.network_reset").Profile);
        Assert.False(Find("repair.network_adapter").UsesNetworkResetOwner);
        Assert.False(Find("repair.dhcp").UsesNetworkResetOwner);
        Assert.All(NetworkRepairCatalog.All, item =>
            Assert.True(UnavailableRepairChangeExecutor.Instance.IsManaged(item.RepairItemId)));
    }

    [Fact]
    public void Infrastructure_RegistersExactlyOneFailClosedCapabilityForEachUnsupportedSemantic()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();

        Assert.All(NetworkRepairCatalog.All.Where(item => !item.UsesNetworkResetOwner), item =>
        {
            var capability = Assert.Single(capabilities, candidate =>
                string.Equals(candidate.Descriptor.CapabilityId, item.CapabilityId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ChangeRisk.HighSystem, capability.Descriptor.Risk);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
        });
    }

    [Theory]
    [InlineData("repair.tcp_ip", NetworkResetProfile.TcpIp)]
    [InlineData("repair.winsock", NetworkResetProfile.Winsock)]
    [InlineData("repair.dns", NetworkResetProfile.DnsCache)]
    [InlineData("repair.network_reset", NetworkResetProfile.Full)]
    public async Task Executor_RoutesExactProfilesToTheSingleNetworkOwner(
        string repairItemId,
        NetworkResetProfile expectedProfile)
    {
        var owner = new RecordingNetworkOwner();
        var executor = new OrchestratedRepairChangeExecutor(
            new ThrowingOrchestrator(), new ThrowingConfirmationAuthority(), TimeProvider.System, owner);
        using var cancellation = new CancellationTokenSource();

        var result = await executor.ExecuteAsync(Item(repairItemId), null, cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(expectedProfile, owner.Profile);
        Assert.Equal(cancellation.Token, owner.CancellationToken);
        Assert.Equal(1, owner.CallCount);
    }

    [Theory]
    [InlineData("repair.network_adapter")]
    [InlineData("repair.dhcp")]
    public async Task UnsupportedSemantics_FailClosedWithoutCallingNetworkOwner(string repairItemId)
    {
        var owner = new RecordingNetworkOwner();
        var executor = new OrchestratedRepairChangeExecutor(
            new ThrowingOrchestrator(), new ThrowingConfirmationAuthority(), TimeProvider.System, owner);

        var result = await executor.ExecuteAsync(Item(repairItemId), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("阻断", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, owner.CallCount);
    }

    [Fact]
    public async Task UnsupportedLegacyDelegates_CannotFallBackToFullResetOrServiceCommands()
    {
        var owner = new RecordingNetworkOwner();
        var registry = new RepairItemRegistry(networkResetOwner: owner);
        registry.Initialize();

        foreach (var id in new[] { "repair.network_adapter", "repair.dhcp" })
        {
            var item = Assert.Single(registry.GetAll(), candidate => candidate.Id == id);
            var result = await item.RepairAsync(null);
            Assert.False(result.Success);
        }

        Assert.Equal(0, owner.CallCount);
    }

    [Theory]
    [InlineData("repair.tcp_ip", NetworkResetProfile.TcpIp)]
    [InlineData("repair.winsock", NetworkResetProfile.Winsock)]
    [InlineData("repair.dns", NetworkResetProfile.DnsCache)]
    [InlineData("repair.network_reset", NetworkResetProfile.Full)]
    public async Task Owned_health_checks_use_fault_evidence_not_owner_execution_prerequisites(
        string repairItemId,
        NetworkResetProfile expectedProfile)
    {
        var owner = new RecordingNetworkOwner();
        var health = new NetworkRepairHealthProbe(new HealthyNetworkPlatform());
        var item = Assert.Single(
            NetworkRepairs.GetAll(owner, health),
            candidate => candidate.Id == repairItemId);

        Assert.True(await item.CheckHealthAsync());
        Assert.Equal(0, owner.StatusCallCount);
        Assert.Equal(0, owner.CallCount);
        Assert.Equal(expectedProfile, Find(repairItemId).Profile);
    }

    private static NetworkRepairDefinition Find(string repairItemId) =>
        Assert.Single(NetworkRepairCatalog.All, item => item.RepairItemId == repairItemId);

    private static RepairItem Item(string id) => new()
    {
        Id = id,
        Name = id,
        Description = id,
        CheckHealthAsync = () => Task.FromResult(false),
        RepairAsync = _ => throw new InvalidOperationException("Legacy delegate must not run.")
    };

    private sealed class RecordingNetworkOwner : INetworkResetOwner
    {
        public int CallCount { get; private set; }
        public NetworkResetProfile? Profile { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public int StatusCallCount { get; private set; }
        public NetworkResetProfile? StatusProfile { get; private set; }

        public Task<NetworkResetReadOnlyStatus> ReadStatusAsync(
            NetworkResetProfile profile,
            CancellationToken cancellationToken = default)
        {
            StatusCallCount++;
            StatusProfile = profile;
            return Task.FromResult(new NetworkResetReadOnlyStatus(
                profile,
                true,
                true,
                true,
                "test.ready",
                "ready",
                new NetworkResetSnapshot(new string('A', 64), [], false, "test"),
                DateTimeOffset.UtcNow));
        }

        public Task<NetworkResetPlan> PrepareAsync(NetworkResetProfile profile, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Interactive entry expected.");

        public Task<NetworkResetReceipt> ExecuteAsync(
            NetworkResetPlan plan,
            ConfirmationToken confirmation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Interactive entry expected.");

        public Task<NetworkResetReceipt> RunInteractiveAsync(
            NetworkResetProfile profile,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Profile = profile;
            CancellationToken = cancellationToken;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new NetworkResetReceipt(
                Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), profile,
                NetworkResetReceiptState.Completed, NetworkResetReceiptPhase.Completed,
                NetworkResetOutcome.Blocked, "test.blocked", "blocked",
                false, false, false, NetworkRecoveryDecision.NotRequired,
                "no recovery", [], null, now, now));
        }

        public Task<IReadOnlyList<NetworkResetReceipt>> GetPendingRecoveryReceiptsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NetworkResetReceipt>>([]);
    }

    private sealed class HealthyNetworkPlatform : INetworkRepairHealthPlatform
    {
        public bool HasUsableNetworkPath() => true;
        public bool CanCreateWinsockSocket() => true;
        public Task<bool> CanResolveConnectivityHostAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class ThrowingOrchestrator : IChangeOrchestrator
    {
        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Generic orchestrator must not run.");
        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken, bool registerPlan = true) =>
            throw new InvalidOperationException("Generic orchestrator must not run.");
        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken) =>
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
}
