namespace GameOptimizer.Core.Services.NetworkDiagnostics;

/// <summary>
/// The two unsupported repair semantics which may be inspected, but never written by this contract.
/// </summary>
public enum NetworkAdapterDhcpRepairIntent
{
    Adapter,
    Dhcp
}

/// <summary>
/// Deliberately has no writable value: a typed mutation is unavailable until snapshot, read-back,
/// and recovery evidence exist as one complete, reviewed capability.
/// </summary>
public enum NetworkAdapterDhcpExecutionAvailability
{
    FailClosedRecoveryEvidenceMissing
}

public sealed record NetworkAdapterIdentity(
    Guid InterfaceGuid,
    uint InterfaceIndex,
    ulong InterfaceLuid,
    string DriverDescription,
    ulong LogonSessionLuid);

public sealed record DhcpLeaseEvidence(
    bool IsDhcpEnabled,
    string? DhcpServerAddress,
    DateTimeOffset? LeaseObtainedAtUtc,
    DateTimeOffset? LeaseExpiresAtUtc);

public sealed record NetworkAdapterDhcpSnapshotEntry(
    NetworkAdapterIdentity Identity,
    string FriendlyName,
    string InterfaceType,
    string OperationalStatus,
    DhcpLeaseEvidence Dhcp);

public sealed record NetworkAdapterDhcpSnapshot(
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<NetworkAdapterDhcpSnapshotEntry> Adapters,
    string EvidenceHash);

public sealed record NetworkAdapterDhcpPlatformProbe(
    bool IsSupported,
    bool IsAdministrator,
    bool ReadAvailable,
    string ReasonCode,
    string Message);

public sealed record NetworkAdapterDhcpReadResult(
    NetworkAdapterDhcpPlatformProbe Probe,
    NetworkAdapterDhcpSnapshot? Snapshot);

public sealed record NetworkAdapterDhcpPlan(
    Guid PlanId,
    string PlanHash,
    NetworkAdapterDhcpRepairIntent Intent,
    string ResourceKey,
    NetworkAdapterDhcpSnapshotEntry Target,
    string EvidenceHash,
    DateTimeOffset PreparedAtUtc,
    NetworkAdapterDhcpExecutionAvailability ExecutionAvailability,
    string ReasonCode,
    string Message,
    string RecoveryBoundary);

/// <summary>
/// Read-only Windows platform port. It intentionally has no mutation member.
/// </summary>
public interface INetworkAdapterDhcpPlatform
{
    Task<NetworkAdapterDhcpPlatformProbe> ProbeAsync(CancellationToken cancellationToken);
    Task<NetworkAdapterDhcpSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Sole owner for adapter/DHCP identity probing and fail-closed planning. It is not a second writer.
/// </summary>
public interface INetworkAdapterDhcpOwner
{
    Task<NetworkAdapterDhcpPlatformProbe> ProbeAsync(CancellationToken cancellationToken = default);
    Task<NetworkAdapterDhcpReadResult> ReadAsync(CancellationToken cancellationToken = default);
    Task<NetworkAdapterDhcpPlan> PrepareAsync(
        NetworkAdapterDhcpRepairIntent intent,
        Guid interfaceGuid,
        CancellationToken cancellationToken = default);
}
