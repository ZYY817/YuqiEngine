using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public static class PerInterfaceRegistryCapabilityDefinitions
{
    // Network adapter advanced properties live under the adapter device-class root. Each
    // numbered sub-key (0000, 0001, …) is one physical/virtual network adapter instance, so
    // enumerating the class root is deterministic and does not depend on the runtime
    // NetworkInterface enumeration the legacy operation used. Each capability claims a
    // distinct resource key so opposite targets never collide on the same lease.
    private const string NetworkAdapterClassRoot =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    // Disable adapter power saving: PnPCapabilities=0x18 (no PME/power management) + *EEE=0.
    public static PerInterfaceRegistryCapabilityDefinition DisableAdapterPowerSaving { get; } = new(
        "network.disable_adapter_power_saving",
        "1.0.0",
        NetworkAdapterClassRoot,
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.DWord("PnPCapabilities", 0x18),
            PerInterfaceRegistryTargetValue.String("*EEE", "0")
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: false,
        ResourceKey: "registry:HKLM/network-adapter/power-saving");

    // Disable Ethernet flow control: *FlowControl=0 and FlowControl=0.
    public static PerInterfaceRegistryCapabilityDefinition DisableFlowControl { get; } = new(
        "network.disable_flow_control",
        "1.0.0",
        NetworkAdapterClassRoot,
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.String("*FlowControl", "0"),
            PerInterfaceRegistryTargetValue.String("FlowControl", "0")
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: false,
        ResourceKey: "registry:HKLM/network-adapter/flow-control");

    // Disable interrupt moderation: *InterruptModeration=0 and *InterruptModerationMode=0.
    public static PerInterfaceRegistryCapabilityDefinition DisableInterruptModeration { get; } = new(
        "network.disable_interrupt_moderation",
        "1.0.0",
        NetworkAdapterClassRoot,
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.String("*InterruptModeration", "0"),
            PerInterfaceRegistryTargetValue.String("*InterruptModerationMode", "0")
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: false,
        ResourceKey: "registry:HKLM/network-adapter/interrupt-moderation");

    // Enable large send offload (LSO) for IPv4/IPv6.
    public static PerInterfaceRegistryCapabilityDefinition EnableLso { get; } = new(
        "network.enable_lso",
        "1.0.0",
        NetworkAdapterClassRoot,
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.String("*LsoV1IPv4", "1"),
            PerInterfaceRegistryTargetValue.String("*LsoV2IPv4", "1"),
            PerInterfaceRegistryTargetValue.String("*LsoV2IPv6", "1")
        ]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        RequiresRestart: false,
        ResourceKey: "registry:HKLM/network-adapter/lso");

    // Enlarge receive/transmit buffers to 512.
    public static PerInterfaceRegistryCapabilityDefinition OptimizeAdapterBuffers { get; } = new(
        "network.optimize_adapter_buffers",
        "1.0.0",
        NetworkAdapterClassRoot,
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.String("*ReceiveBuffers", "512"),
            PerInterfaceRegistryTargetValue.String("*TransmitBuffers", "512")
        ]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        RequiresRestart: false,
        ResourceKey: "registry:HKLM/network-adapter/buffers");

    // Enable checksum offload for IPv4/IPv6.
    public static PerInterfaceRegistryCapabilityDefinition OptimizeChecksumOffload { get; } = new(
        "network.optimize_checksum_offload",
        "1.0.0",
        NetworkAdapterClassRoot,
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.String("*IPChecksumOffloadIPv4", "3"),
            PerInterfaceRegistryTargetValue.String("*TCPChecksumOffloadIPv4", "3"),
            PerInterfaceRegistryTargetValue.String("*UDPChecksumOffloadIPv4", "3"),
            PerInterfaceRegistryTargetValue.String("*TCPChecksumOffloadIPv6", "3"),
            PerInterfaceRegistryTargetValue.String("*UDPChecksumOffloadIPv6", "3")
        ]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        RequiresRestart: false,
        ResourceKey: "registry:HKLM/network-adapter/checksum-offload");

    // AMD Radeon 低延迟：仅对 GPU 显示类根下 ProviderName 含 AMD/ATI 的子键写入
    // EnableUlps=0 + PP_SclkDeepSleepDisable=1。厂商过滤器保证绝不触碰 NVIDIA/Intel/虚拟显卡。
    public static PerInterfaceRegistryCapabilityDefinition AmdRadeonAntiLag { get; } = new(
        "amd.radeon_anti_lag",
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}",
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.DWord("EnableUlps", 0),
            PerInterfaceRegistryTargetValue.DWord("PP_SclkDeepSleepDisable", 1)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: false,
        ResourceKey: "registry:HKLM/gpu-adapter/radeon-anti-lag",
        VendorSelectorValueName: "ProviderName",
        VendorSelectorContains: Array.AsReadOnly(["AMD", "ATI"]));

    // NetBIOS over TCP/IP: set NetbiosOptions=2 (disable) on every NetBT interface sub-key.
    // Root: SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces
    public static PerInterfaceRegistryCapabilityDefinition DisableNetBios { get; } = new(
        "network.disable_netbios",
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces",
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.DWord("NetbiosOptions", 2)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: true);

    // Nagle algorithm: set TcpAckFrequency=1 and TCPNoDelay=1 on every active TCP/IP interface.
    // Root: SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces
    public static PerInterfaceRegistryCapabilityDefinition DisableNagle { get; } = new(
        "network.disable_nagle",
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces",
        Array.AsReadOnly(
        [
            PerInterfaceRegistryTargetValue.DWord("TcpAckFrequency", 1),
            PerInterfaceRegistryTargetValue.DWord("TCPNoDelay", 1)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: true);
}
