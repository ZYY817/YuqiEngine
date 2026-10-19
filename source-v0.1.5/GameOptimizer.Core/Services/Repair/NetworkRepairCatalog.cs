using GameOptimizer.Core.Services.NetworkReset;

namespace GameOptimizer.Core.Services.Repair;

public sealed record NetworkRepairDefinition(
    string RepairItemId,
    NetworkResetProfile? Profile,
    string? CapabilityId,
    string? ResourceKey,
    string BlockReason)
{
    public bool UsesNetworkResetOwner => Profile is not null;
}

public static class NetworkRepairCatalog
{
    private const string AdapterBlockReason =
        "网络适配器高级属性重置不等同于完整网络重置；专属 typed snapshot/readback/restore 证据不足，已安全阻断。";
    private const string DhcpBlockReason =
        "DHCP 服务与租约修复不属于现有 Network Reset Owner 的固定能力；专属 typed snapshot/readback/restore 证据不足，已安全阻断。";

    public static IReadOnlyList<NetworkRepairDefinition> All { get; } =
    [
        Owned("repair.tcp_ip", NetworkResetProfile.TcpIp),
        Owned("repair.winsock", NetworkResetProfile.Winsock),
        Owned("repair.dns", NetworkResetProfile.DnsCache),
        Blocked("repair.network_adapter", "repair.network.network-adapter", "network-repair:adapter", AdapterBlockReason),
        Blocked("repair.dhcp", "repair.network.dhcp", "network-repair:dhcp", DhcpBlockReason),
        Owned("repair.network_reset", NetworkResetProfile.Full)
    ];

    public static bool Contains(string repairItemId) =>
        All.Any(item => string.Equals(item.RepairItemId, repairItemId, StringComparison.OrdinalIgnoreCase));

    public static bool TryGet(string repairItemId, out NetworkRepairDefinition definition)
    {
        definition = All.FirstOrDefault(item =>
            string.Equals(item.RepairItemId, repairItemId, StringComparison.OrdinalIgnoreCase))!;
        return definition is not null;
    }

    private static NetworkRepairDefinition Owned(string repairItemId, NetworkResetProfile profile) =>
        new(repairItemId, profile, null, null, string.Empty);

    private static NetworkRepairDefinition Blocked(
        string repairItemId,
        string capabilityId,
        string resourceKey,
        string blockReason) =>
        new(repairItemId, null, capabilityId, resourceKey, blockReason);
}
