using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using GameOptimizer.Core.Services.NetworkDiagnostics;

namespace GameOptimizer.Infrastructure.Windows.NetworkDiagnostics;

/// <summary>
/// Windows structured, read-only evidence adapter. GetAdaptersAddresses and token statistics are
/// official Windows APIs; this class does not start a process or expose a network mutation method.
/// </summary>
public sealed class WindowsNetworkAdapterDhcpPlatform : INetworkAdapterDhcpPlatform
{
    private const uint AfUnspec = 0;
    private const uint GaaFlagIncludePrefix = 0x0010;
    private const uint ErrorSuccess = 0;
    private const uint ErrorBufferOverflow = 111;
    private const uint IpAdapterDhcpEnabled = 0x0004;
    private const ushort AfInet = 2;

    public Task<NetworkAdapterDhcpPlatformProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new NetworkAdapterDhcpPlatformProbe(
                false, false, false, "platform.unsupported", "仅支持 Windows。"));
        }

        var isAdministrator = GameOptimizer.Core.Services.WindowsElevation.IsElevated;
        return Task.FromResult(new NetworkAdapterDhcpPlatformProbe(
            true,
            isAdministrator,
            true,
            isAdministrator ? "network.adapter-dhcp.read-ready" : "network.adapter-dhcp.read-ready-non-admin",
            isAdministrator
                ? "Windows 结构化只读网卡/DHCP 探测可用。"
                : "Windows 结构化只读网卡/DHCP 探测可用；不会尝试任何写入。"));
    }

    public Task<NetworkAdapterDhcpSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows adapter diagnostics are unavailable.");
        }

        var sessionLuid = GetLogonSessionLuid();
        var managed = ManagedInterfaces();
        var entries = ReadNativeAdapters(sessionLuid, managed, cancellationToken);
        return Task.FromResult(new NetworkAdapterDhcpSnapshot(
            DateTimeOffset.UtcNow,
            entries,
            string.Empty));
    }

    private static IReadOnlyDictionary<Guid, NetworkInterface> ManagedInterfaces()
    {
        var result = new Dictionary<Guid, NetworkInterface>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (Guid.TryParse(adapter.Id, out var interfaceGuid))
            {
                result.TryAdd(interfaceGuid, adapter);
            }
        }
        return result;
    }

    private static IReadOnlyList<NetworkAdapterDhcpSnapshotEntry> ReadNativeAdapters(
        ulong sessionLuid,
        IReadOnlyDictionary<Guid, NetworkInterface> managed,
        CancellationToken cancellationToken)
    {
        uint size = 0;
        var first = GetAdaptersAddresses(AfUnspec, GaaFlagIncludePrefix, IntPtr.Zero, IntPtr.Zero, ref size);
        if (first != ErrorBufferOverflow || size == 0)
        {
            throw new InvalidOperationException($"GetAdaptersAddresses.size:{first}");
        }

        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            var result = GetAdaptersAddresses(AfUnspec, GaaFlagIncludePrefix, IntPtr.Zero, buffer, ref size);
            if (result != ErrorSuccess)
            {
                throw new InvalidOperationException($"GetAdaptersAddresses.read:{result}");
            }

            var entries = new List<NetworkAdapterDhcpSnapshotEntry>();
            for (var current = buffer; current != IntPtr.Zero;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var native = Marshal.PtrToStructure<IpAdapterAddresses>(current);
                current = native.Next;
                if (!Guid.TryParse(Marshal.PtrToStringAnsi(native.AdapterName), out var interfaceGuid))
                {
                    continue;
                }

                managed.TryGetValue(interfaceGuid, out var managedAdapter);
                var ipv4 = TryGetIpv4Properties(managedAdapter);
                var interfaceIndex = native.InterfaceIndex != 0
                    ? native.InterfaceIndex
                    : (uint)Math.Max(ipv4?.Index ?? 0, 0);
                var dhcpEnabled = ipv4?.IsDhcpEnabled ?? (native.Flags & IpAdapterDhcpEnabled) != 0;
                var driverDescription = Marshal.PtrToStringUni(native.Description)
                    ?? managedAdapter?.Description
                    ?? string.Empty;
                var friendlyName = Marshal.PtrToStringUni(native.FriendlyName)
                    ?? managedAdapter?.Name
                    ?? string.Empty;

                entries.Add(new NetworkAdapterDhcpSnapshotEntry(
                    new NetworkAdapterIdentity(
                        interfaceGuid,
                        interfaceIndex,
                        native.Luid,
                        driverDescription,
                        sessionLuid),
                    friendlyName,
                    managedAdapter?.NetworkInterfaceType.ToString() ?? native.InterfaceType.ToString(),
                    managedAdapter?.OperationalStatus.ToString() ?? native.OperStatus.ToString(),
                    new DhcpLeaseEvidence(
                        dhcpEnabled,
                        ReadIpv4Address(native.Dhcpv4Server),
                        ToUtcFileTime(native.LeaseObtained),
                        ToUtcFileTime(native.LeaseExpires))));
            }
            return entries;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IPv4InterfaceProperties? TryGetIpv4Properties(NetworkInterface? adapter)
    {
        try
        {
            return adapter?.GetIPProperties().GetIPv4Properties();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static string? ReadIpv4Address(SocketAddress address)
    {
        if (address.Address == IntPtr.Zero || address.Length < 8 || (ushort)Marshal.ReadInt16(address.Address) != AfInet)
        {
            return null;
        }

        var bytes = new byte[4];
        Marshal.Copy(IntPtr.Add(address.Address, 4), bytes, 0, bytes.Length);
        return new IPAddress(bytes).ToString();
    }

    private static DateTimeOffset? ToUtcFileTime(long fileTime)
    {
        if (fileTime <= 0)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static ulong GetLogonSessionLuid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!GetTokenInformation(
                identity.AccessToken.DangerousGetHandle(),
                TokenInformationClass.TokenStatistics,
                out var statistics,
                Marshal.SizeOf<TokenStatistics>(),
                out _))
        {
            throw new InvalidOperationException($"GetTokenInformation:{Marshal.GetLastWin32Error()}");
        }

        return statistics.AuthenticationId.ToUInt64();
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetAdaptersAddresses(
        uint family,
        uint flags,
        IntPtr reserved,
        IntPtr adapterAddresses,
        ref uint sizePointer);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        out TokenStatistics tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    private enum TokenInformationClass
    {
        TokenStatistics = 10
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;

        public readonly ulong ToUInt64() => ((ulong)(uint)HighPart << 32) | LowPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenStatistics
    {
        public Luid TokenId;
        public Luid AuthenticationId;
        public long ExpirationTime;
        public uint TokenType;
        public uint ImpersonationLevel;
        public uint DynamicCharged;
        public uint DynamicAvailable;
        public uint GroupCount;
        public uint PrivilegeCount;
        public Luid ModifiedId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SocketAddress
    {
        public IntPtr Address;
        public int Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IpAdapterAddresses
    {
        public uint Length;
        public uint InterfaceIndex;
        public IntPtr Next;
        public IntPtr AdapterName;
        public IntPtr FirstUnicastAddress;
        public IntPtr FirstAnycastAddress;
        public IntPtr FirstMulticastAddress;
        public IntPtr FirstDnsServerAddress;
        public IntPtr DnsSuffix;
        public IntPtr Description;
        public IntPtr FriendlyName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] PhysicalAddress;
        public uint PhysicalAddressLength;
        public uint Flags;
        public uint Mtu;
        public uint InterfaceType;
        public uint OperStatus;
        public uint Ipv6InterfaceIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public uint[] ZoneIndices;
        public IntPtr FirstPrefix;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public IntPtr FirstWinsServerAddress;
        public IntPtr FirstGatewayAddress;
        public uint Ipv4Metric;
        public uint Ipv6Metric;
        public ulong Luid;
        public SocketAddress Dhcpv4Server;
        public uint CompartmentId;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public uint TunnelType;
        public SocketAddress Dhcpv6Server;
        public uint Dhcpv6ClientDuidLength;
        public IntPtr Dhcpv6ClientDuid;
        public uint Dhcpv6Iaid;
        public IntPtr FirstDnsSuffix;
        public long LeaseObtained;
        public long LeaseExpires;
    }
}
