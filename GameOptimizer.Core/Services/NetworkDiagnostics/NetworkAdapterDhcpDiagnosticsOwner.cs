using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.Services.NetworkDiagnostics;

/// <summary>
/// Freezes structured adapter/DHCP evidence into a unique resource plan. Mutation is intentionally
/// absent: a snapshot alone cannot prove read-back or recovery for adapter and DHCP changes.
/// </summary>
public sealed class NetworkAdapterDhcpDiagnosticsOwner : INetworkAdapterDhcpOwner
{
    private const string FailClosedReason = "network.adapter-dhcp.mutation-fail-closed-recovery-evidence-missing";
    private readonly INetworkAdapterDhcpPlatform _platform;
    private readonly TimeProvider _timeProvider;

    public NetworkAdapterDhcpDiagnosticsOwner(
        INetworkAdapterDhcpPlatform platform,
        TimeProvider? timeProvider = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<NetworkAdapterDhcpPlatformProbe> ProbeAsync(CancellationToken cancellationToken = default) =>
        _platform.ProbeAsync(cancellationToken);

    public async Task<NetworkAdapterDhcpReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (!probe.IsSupported || !probe.ReadAvailable)
        {
            return new NetworkAdapterDhcpReadResult(probe, null);
        }

        try
        {
            var raw = await _platform.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var validation = Validate(raw);
            if (validation is not null)
            {
                return new NetworkAdapterDhcpReadResult(
                    probe with
                    {
                        ReadAvailable = false,
                        ReasonCode = validation,
                        Message = "网卡或 DHCP 只读身份证据不完整，已拒绝生成计划。"
                    },
                    null);
            }

            return new NetworkAdapterDhcpReadResult(probe, raw with { EvidenceHash = ComputeEvidenceHash(raw) });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new NetworkAdapterDhcpReadResult(
                probe with
                {
                    ReadAvailable = false,
                    ReasonCode = "network.adapter-dhcp.read-failed",
                    Message = $"网卡或 DHCP 只读探测失败：{ex.GetType().Name}"
                },
                null);
        }
    }

    public async Task<NetworkAdapterDhcpPlan> PrepareAsync(
        NetworkAdapterDhcpRepairIntent intent,
        Guid interfaceGuid,
        CancellationToken cancellationToken = default)
    {
        if (interfaceGuid == Guid.Empty)
        {
            throw new ArgumentException("An adapter interface GUID is required.", nameof(interfaceGuid));
        }

        var read = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (read.Snapshot is null)
        {
            throw new InvalidOperationException($"{read.Probe.ReasonCode}: {read.Probe.Message}");
        }

        var target = read.Snapshot.Adapters.SingleOrDefault(entry => entry.Identity.InterfaceGuid == interfaceGuid);
        if (target is null)
        {
            throw new InvalidOperationException("network.adapter-dhcp.target-not-found");
        }

        var planId = Guid.NewGuid();
        var resourceKey = ResourceKey(intent, target.Identity);
        var preparedAt = _timeProvider.GetUtcNow();
        var planHash = Hash(string.Join('|',
            planId.ToString("N"),
            intent,
            resourceKey,
            read.Snapshot.EvidenceHash,
            target.Identity.InterfaceGuid.ToString("N"),
            target.Identity.InterfaceIndex,
            target.Identity.InterfaceLuid,
            target.Identity.LogonSessionLuid));

        return new NetworkAdapterDhcpPlan(
            planId,
            planHash,
            intent,
            resourceKey,
            target,
            read.Snapshot.EvidenceHash,
            preparedAt,
            NetworkAdapterDhcpExecutionAvailability.FailClosedRecoveryEvidenceMissing,
            FailClosedReason,
            "已冻结只读网卡/DHCP 证据；缺少完整 snapshot/readback/recovery 证明，生产写入保持阻断。",
            "未执行任何网络写入，也没有 RecoveryId。若人工更改网络，请按系统网络设置、驱动程序或 DHCP 管理流程恢复，并重新读取证据。");
    }

    public static string ResourceKey(NetworkAdapterDhcpRepairIntent intent, NetworkAdapterIdentity identity) =>
        $"network-{intent.ToString().ToLowerInvariant()}:{identity.InterfaceGuid:N}:{identity.InterfaceLuid:X16}";

    private static string? Validate(NetworkAdapterDhcpSnapshot snapshot)
    {
        if (snapshot.Adapters.Count == 0)
        {
            return "network.adapter-dhcp.no-adapters";
        }

        var identities = new HashSet<Guid>();
        foreach (var entry in snapshot.Adapters)
        {
            var identity = entry.Identity;
            if (identity.InterfaceGuid == Guid.Empty
                || identity.InterfaceIndex == 0
                || identity.InterfaceLuid == 0
                || identity.LogonSessionLuid == 0
                || string.IsNullOrWhiteSpace(identity.DriverDescription)
                || !identities.Add(identity.InterfaceGuid))
            {
                return "network.adapter-dhcp.identity-incomplete-or-duplicate";
            }
        }

        return null;
    }

    private static string ComputeEvidenceHash(NetworkAdapterDhcpSnapshot snapshot)
    {
        var payload = new StringBuilder();
        foreach (var entry in snapshot.Adapters.OrderBy(item => item.Identity.InterfaceGuid))
        {
            var identity = entry.Identity;
            payload.Append(identity.InterfaceGuid.ToString("N")).Append('|')
                .Append(identity.InterfaceIndex).Append('|')
                .Append(identity.InterfaceLuid).Append('|')
                .Append(identity.DriverDescription).Append('|')
                .Append(identity.LogonSessionLuid).Append('|')
                .Append(entry.FriendlyName).Append('|')
                .Append(entry.InterfaceType).Append('|')
                .Append(entry.OperationalStatus).Append('|')
                .Append(entry.Dhcp.IsDhcpEnabled).Append('|')
                .Append(entry.Dhcp.DhcpServerAddress).Append('|')
                .Append(entry.Dhcp.LeaseObtainedAtUtc?.UtcDateTime.Ticks).Append('|')
                .Append(entry.Dhcp.LeaseExpiresAtUtc?.UtcDateTime.Ticks).AppendLine();
        }
        return Hash(payload.ToString());
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
