using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Repair.RepairOperations;

namespace GameOptimizer.Tests.Changes;

public sealed class NetworkRepairHealthProbeTests
{
    [Fact]
    public async Task RepairChecksUseFaultEvidenceInsteadOfOwnerAvailability()
    {
        var probe = new NetworkRepairHealthProbe(new FakePlatform(
            hasNetworkPath: true,
            canCreateSocket: true,
            canResolveDns: true));
        var items = NetworkRepairs.GetAll(healthProbe: probe).ToDictionary(item => item.Id);

        Assert.True(await items["repair.tcp_ip"].CheckHealthAsync());
        Assert.True(await items["repair.winsock"].CheckHealthAsync());
        Assert.True(await items["repair.dns"].CheckHealthAsync());
        Assert.True(await items["repair.network_reset"].CheckHealthAsync());
    }

    [Fact]
    public async Task FullResetIsOfferedOnlyWhenTheRelevantFaultIsObserved()
    {
        var probe = new NetworkRepairHealthProbe(new FakePlatform(
            hasNetworkPath: true,
            canCreateSocket: true,
            canResolveDns: false));
        var items = NetworkRepairs.GetAll(healthProbe: probe).ToDictionary(item => item.Id);

        Assert.True(await items["repair.tcp_ip"].CheckHealthAsync());
        Assert.True(await items["repair.winsock"].CheckHealthAsync());
        Assert.False(await items["repair.dns"].CheckHealthAsync());
        Assert.False(await items["repair.network_reset"].CheckHealthAsync());
    }

    private sealed class FakePlatform(
        bool hasNetworkPath,
        bool canCreateSocket,
        bool canResolveDns) : INetworkRepairHealthPlatform
    {
        public bool HasUsableNetworkPath() => hasNetworkPath;
        public bool CanCreateWinsockSocket() => canCreateSocket;
        public Task<bool> CanResolveConnectivityHostAsync(CancellationToken cancellationToken) =>
            Task.FromResult(canResolveDns);
    }
}
