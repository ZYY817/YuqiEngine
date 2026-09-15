using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GameOptimizer.Core.Services.Repair;

/// <summary>
/// Read-only evidence used to decide whether a network repair is applicable.
/// It deliberately does not inspect the Network Reset Owner: administrator
/// access and snapshots are execution prerequisites, not proof of a fault.
/// </summary>
public interface INetworkRepairHealthPlatform
{
    bool HasUsableNetworkPath();
    bool CanCreateWinsockSocket();
    Task<bool> CanResolveConnectivityHostAsync(CancellationToken cancellationToken);
}

public sealed class NetworkRepairHealthProbe(INetworkRepairHealthPlatform? platform = null)
{
    private readonly INetworkRepairHealthPlatform _platform = platform ?? new WindowsNetworkRepairHealthPlatform();

    public Task<bool> IsTcpIpHealthyAsync() =>
        Task.FromResult(_platform.HasUsableNetworkPath());

    public Task<bool> IsWinsockHealthyAsync() =>
        Task.FromResult(_platform.CanCreateWinsockSocket());

    public Task<bool> IsDnsHealthyAsync() =>
        _platform.CanResolveConnectivityHostAsync(CancellationToken.None);

    public async Task<bool> IsFullNetworkHealthyAsync() =>
        await IsTcpIpHealthyAsync().ConfigureAwait(false)
        && await IsWinsockHealthyAsync().ConfigureAwait(false)
        && await IsDnsHealthyAsync().ConfigureAwait(false);
}

internal sealed class WindowsNetworkRepairHealthPlatform : INetworkRepairHealthPlatform
{
    // This hostname is the same Windows connectivity-check endpoint family
    // documented by Microsoft. Resolution only is used here; no HTTP request
    // is sent and no router or adapter setting is changed.
    private const string ConnectivityHost = "dns.msftncsi.com";

    public bool HasUsableNetworkPath()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Any(HasUsableAddressAndGateway);
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    public bool CanCreateWinsockSocket()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            return socket.Handle != IntPtr.Zero;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public async Task<bool> CanResolveConnectivityHostAsync(CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(ConnectivityHost, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(4), cancellationToken)
                .ConfigureAwait(false);
            return addresses.Any(address => !IPAddress.IsLoopback(address));
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private static bool HasUsableAddressAndGateway(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        var hasAddress = properties.UnicastAddresses.Any(address =>
            (address.Address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address.Address)
                && !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            || (address.Address.AddressFamily == AddressFamily.InterNetworkV6
                && !IPAddress.IsLoopback(address.Address)
                && !address.Address.IsIPv6LinkLocal));
        var hasGateway = properties.GatewayAddresses.Any(gateway =>
            gateway.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
        return hasAddress && hasGateway;
    }
}
