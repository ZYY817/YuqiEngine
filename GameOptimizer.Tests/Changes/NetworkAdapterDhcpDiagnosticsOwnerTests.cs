using GameOptimizer.Core.Services.NetworkDiagnostics;

namespace GameOptimizer.Tests.Changes;

public sealed class NetworkAdapterDhcpDiagnosticsOwnerTests
{
    [Fact]
    public async Task Read_uses_only_the_typed_read_port_and_freezes_all_identity_evidence()
    {
        var platform = new FakePlatform();
        var owner = new NetworkAdapterDhcpDiagnosticsOwner(platform, new FixedTimeProvider());

        var result = await owner.ReadAsync();

        var entry = Assert.Single(result.Snapshot!.Adapters);
        Assert.True(result.Probe.ReadAvailable);
        Assert.Equal(AdapterGuid, entry.Identity.InterfaceGuid);
        Assert.Equal((uint)12, entry.Identity.InterfaceIndex);
        Assert.Equal(0x1020304050607080UL, entry.Identity.InterfaceLuid);
        Assert.Equal("test-driver", entry.Identity.DriverDescription);
        Assert.Equal(0x2021222324252627UL, entry.Identity.LogonSessionLuid);
        Assert.True(entry.Dhcp.IsDhcpEnabled);
        Assert.Equal("192.0.2.1", entry.Dhcp.DhcpServerAddress);
        Assert.NotEmpty(result.Snapshot.EvidenceHash);
        Assert.Equal(1, platform.ProbeCalls);
        Assert.Equal(1, platform.ReadCalls);
    }

    [Fact]
    public async Task Unsupported_or_unavailable_probe_never_reads_the_platform()
    {
        var platform = new FakePlatform
        {
            Probe = new NetworkAdapterDhcpPlatformProbe(false, false, false, "platform.unsupported", "unsupported")
        };
        var owner = new NetworkAdapterDhcpDiagnosticsOwner(platform);

        var result = await owner.ReadAsync();

        Assert.Null(result.Snapshot);
        Assert.Equal(1, platform.ProbeCalls);
        Assert.Equal(0, platform.ReadCalls);
    }

    [Fact]
    public async Task Non_administrator_can_read_but_prepared_adapter_and_dhcp_plans_are_permanently_fail_closed()
    {
        var platform = new FakePlatform
        {
            Probe = new NetworkAdapterDhcpPlatformProbe(true, false, true, "read.ready", "read only")
        };
        var owner = new NetworkAdapterDhcpDiagnosticsOwner(platform, new FixedTimeProvider());

        var adapter = await owner.PrepareAsync(NetworkAdapterDhcpRepairIntent.Adapter, AdapterGuid);
        var dhcp = await owner.PrepareAsync(NetworkAdapterDhcpRepairIntent.Dhcp, AdapterGuid);

        Assert.Equal("network-adapter:11111111111111111111111111111111:1020304050607080", adapter.ResourceKey);
        Assert.Equal("network-dhcp:11111111111111111111111111111111:1020304050607080", dhcp.ResourceKey);
        Assert.NotEqual(adapter.PlanHash, dhcp.PlanHash);
        Assert.Equal(NetworkAdapterDhcpExecutionAvailability.FailClosedRecoveryEvidenceMissing, adapter.ExecutionAvailability);
        Assert.Equal("network.adapter-dhcp.mutation-fail-closed-recovery-evidence-missing", adapter.ReasonCode);
        Assert.Contains("没有 RecoveryId", adapter.RecoveryBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(INetworkAdapterDhcpOwner).GetMethods(), method =>
            method.Name.Contains("Execute", StringComparison.OrdinalIgnoreCase)
            || method.Name.Contains("Mutat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_session_or_duplicate_identity_rejects_plan_before_any_execution_contract_exists()
    {
        var missingSession = new FakePlatform
        {
            Snapshot = Snapshot(sessionLuid: 0)
        };
        var owner = new NetworkAdapterDhcpDiagnosticsOwner(missingSession);

        var read = await owner.ReadAsync();
        Assert.Null(read.Snapshot);
        Assert.Equal("network.adapter-dhcp.identity-incomplete-or-duplicate", read.Probe.ReasonCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            owner.PrepareAsync(NetworkAdapterDhcpRepairIntent.Adapter, AdapterGuid));
    }

    [Fact]
    public async Task Identity_or_lease_drift_changes_the_evidence_hash_and_unknown_target_is_rejected()
    {
        var platform = new FakePlatform();
        var owner = new NetworkAdapterDhcpDiagnosticsOwner(platform);
        var first = await owner.ReadAsync();
        platform.Snapshot = Snapshot(leaseServer: "192.0.2.9");
        var drifted = await owner.ReadAsync();

        Assert.NotEqual(first.Snapshot!.EvidenceHash, drifted.Snapshot!.EvidenceHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            owner.PrepareAsync(NetworkAdapterDhcpRepairIntent.Dhcp, Guid.Parse("22222222-2222-2222-2222-222222222222")));
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_reading_or_planning()
    {
        var platform = new FakePlatform();
        var owner = new NetworkAdapterDhcpDiagnosticsOwner(platform);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.ReadAsync(cancellation.Token));
        Assert.Equal(1, platform.ProbeCalls);
        Assert.Equal(0, platform.ReadCalls);
    }

    private static readonly Guid AdapterGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static NetworkAdapterDhcpSnapshot Snapshot(
        ulong sessionLuid = 0x2021222324252627UL,
        string leaseServer = "192.0.2.1") =>
        new(
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            [new NetworkAdapterDhcpSnapshotEntry(
                new NetworkAdapterIdentity(AdapterGuid, 12, 0x1020304050607080UL, "test-driver", sessionLuid),
                "Ethernet Test",
                "Ethernet",
                "Up",
                new DhcpLeaseEvidence(
                    true,
                    leaseServer,
                    new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero)))],
            string.Empty);

    private sealed class FakePlatform : INetworkAdapterDhcpPlatform
    {
        public NetworkAdapterDhcpPlatformProbe Probe { get; set; } =
            new(true, true, true, "read.ready", "ready");
        public NetworkAdapterDhcpSnapshot Snapshot { get; set; } = Snapshot();
        public int ProbeCalls { get; private set; }
        public int ReadCalls { get; private set; }

        public Task<NetworkAdapterDhcpPlatformProbe> ProbeAsync(CancellationToken cancellationToken)
        {
            ProbeCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Probe);
        }

        public Task<NetworkAdapterDhcpSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
        {
            ReadCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 8, 13, 1, 2, 3, TimeSpan.Zero);
    }
}
