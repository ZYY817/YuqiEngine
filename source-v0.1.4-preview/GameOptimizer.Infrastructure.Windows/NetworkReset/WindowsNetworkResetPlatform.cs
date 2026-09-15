using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using GameOptimizer.Core.Services.NetworkReset;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.NetworkReset;

public sealed class WindowsNetworkResetPlatform : INetworkResetPlatform
{
    public Task<NetworkResetPlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new NetworkResetPlatformAssessment(
                false,
                false,
                "platform.unsupported",
                "仅支持 Windows。"));
        }

        var administrator = GameOptimizer.Core.Services.WindowsElevation.IsElevated;
        return Task.FromResult(new NetworkResetPlatformAssessment(
            true,
            administrator,
            administrator ? "platform.ready" : "permission.administrator-required",
            administrator
                ? "Windows 网络命令可用。"
                : GameOptimizer.Core.Services.WindowsElevation.DescribeMachineScopeUnavailable()));
    }

    public Task<NetworkResetSafetyAssessment> AssessResetSafetyAsync(
        NetworkResetProfile profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (profile == NetworkResetProfile.DnsCache)
        {
            return Task.FromResult(new NetworkResetSafetyAssessment(
                true,
                "safety.dns-not-applicable",
                "DNS 缓存刷新不会改动网卡、地址、路由或 Winsock 目录。",
                Array.Empty<NetworkResetSafetyFinding>()));
        }

        try
        {
            var findings = CollectSafetyFindings();
            return Task.FromResult(findings.Count == 0
                ? new NetworkResetSafetyAssessment(
                    true,
                    "safety.clear",
                    "未发现 VPN、代理、静态地址或默认网关风险；仍需确认、基线与人工恢复准备。",
                    findings)
                : new NetworkResetSafetyAssessment(
                    false,
                    "safety.high-impact-network-configuration",
                    "检测到可能在重置后丢失或无法自动恢复的网络配置，Winsock/TCP-IP/完整重置已零写入阻断。",
                    findings));
        }
        catch (Exception ex) when (ex is NetworkInformationException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Task.FromResult(new NetworkResetSafetyAssessment(
                false,
                "safety.probe-unavailable",
                $"无法可靠完成网络配置安全预检：{ex.GetType().Name}。为避免误重置，已零写入阻断。",
                [new NetworkResetSafetyFinding("safety.probe-unavailable", "网络环境无法可靠读取；未执行任何网络写入。")]));
        }
    }

    private static List<NetworkResetSafetyFinding> CollectSafetyFindings()
    {
        var findings = new List<NetworkResetSafetyFinding>();
        var activeAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

        if (activeAdapters.Length == 0)
        {
            findings.Add(new NetworkResetSafetyFinding(
                "network.no-active-adapter",
                "未检测到可用网络适配器，无法建立可靠恢复边界。"));
            return findings;
        }

        var hasDefaultGateway = false;
        foreach (var adapter in activeAdapters)
        {
            var properties = adapter.GetIPProperties();
            if (IsVpnOrTunnel(adapter))
            {
                findings.Add(new NetworkResetSafetyFinding(
                    "network.vpn-or-tunnel-active",
                    $"检测到活动 VPN/隧道适配器“{adapter.Name}”；重置可能中断企业或远程连接。"));
            }

            hasDefaultGateway |= properties.GatewayAddresses.Any(
                gateway => gateway.Address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                    or System.Net.Sockets.AddressFamily.InterNetworkV6);

            var ipv4 = properties.GetIPv4Properties();
            var hasUsableIpv4 = properties.UnicastAddresses.Any(address =>
                address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address.Address)
                && !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
            if (hasUsableIpv4 && !ipv4.IsDhcpEnabled)
            {
                findings.Add(new NetworkResetSafetyFinding(
                    "network.static-ipv4-detected",
                    $"适配器“{adapter.Name}”使用非 DHCP IPv4 地址；TCP/IP 重置后不能自动还原。"));
            }

            foreach (var address in properties.UnicastAddresses.Where(address =>
                address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                && !address.Address.IsIPv6LinkLocal))
            {
                if (address.PrefixOrigin == PrefixOrigin.Manual)
                {
                    findings.Add(new NetworkResetSafetyFinding(
                        "network.static-ipv6-detected",
                        $"适配器“{adapter.Name}”使用手动 IPv6 地址；TCP/IP 重置后不能自动还原。"));
                }
                else if (address.PrefixOrigin == PrefixOrigin.Other)
                {
                    findings.Add(new NetworkResetSafetyFinding(
                        "network.ipv6-origin-undetermined",
                        $"无法可靠判断适配器“{adapter.Name}”的 IPv6 地址来源；为避免误清静态配置已阻断。"));
                }
            }
        }

        if (!hasDefaultGateway)
        {
            findings.Add(new NetworkResetSafetyFinding(
                "network.default-gateway-missing",
                "活动适配器未报告默认网关；无法确认重置后联网恢复路径。"));
        }

        AddProxyFindings(findings);
        return findings;
    }

    private static bool IsVpnOrTunnel(NetworkInterface adapter)
    {
        if (adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
        {
            return true;
        }

        var identity = $"{adapter.Name} {adapter.Description}";
        return new[] { "vpn", "wireguard", "openvpn", "tailscale", "zerotier", "wintun", "tap" }
            .Any(marker => identity.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddProxyFindings(List<NetworkResetSafetyFinding> findings)
    {
        using var settings = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings",
            writable: false);
        if (settings is null)
        {
            findings.Add(new NetworkResetSafetyFinding(
                "network.proxy-state-undetermined",
                "无法读取当前用户系统代理设置；为避免误影响代理环境已阻断。"));
            return;
        }

        var proxyEnabled = settings.GetValue("ProxyEnable") is int enabled && enabled != 0;
        var proxyServer = settings.GetValue("ProxyServer") as string;
        var autoConfigUrl = settings.GetValue("AutoConfigURL") as string;
        if (proxyEnabled || !string.IsNullOrWhiteSpace(proxyServer) || !string.IsNullOrWhiteSpace(autoConfigUrl))
        {
            findings.Add(new NetworkResetSafetyFinding(
                "network.system-proxy-detected",
                "检测到系统代理或自动代理脚本；网络重置可能影响代理连接。"));
        }
    }

    public Task<NetworkResetCommandResult> RunReadOnlyAsync(
        NetworkResetCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!IsReadOnly(command))
        {
            throw new InvalidOperationException($"Command {command} is not read-only.");
        }
        return RunAsync(command, timeout, cancellationToken);
    }

    public Task<NetworkResetCommandResult> RunMutationAsync(
        NetworkResetCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (IsReadOnly(command))
        {
            throw new InvalidOperationException($"Command {command} is not a mutation command.");
        }
        return RunAsync(command, timeout, cancellationToken);
    }

    public async Task<bool> VerifyAsync(
        NetworkResetCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var probe = command switch
        {
            NetworkResetCommand.FlushDnsCache =>
                NetworkResetCommand.ShowDnsCache,
            NetworkResetCommand.ResetWinsock => NetworkResetCommand.ShowWinsockCatalog,
            NetworkResetCommand.ResetIpv4 => NetworkResetCommand.ShowIpv4Configuration,
            NetworkResetCommand.ResetIpv6 => NetworkResetCommand.ShowIpv6Configuration,
            _ => throw new InvalidOperationException($"No read-back probe for {command}.")
        };
        var result = await RunAsync(probe, timeout, cancellationToken).ConfigureAwait(false);
        return result.ProcessStarted && !result.TimedOut && result.ExitCode == 0;
    }

    private static async Task<NetworkResetCommandResult> RunAsync(
        NetworkResetCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var (fileName, arguments) = CommandLine(command);
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new NetworkResetCommandResult(
                    command,
                    -1,
                    string.Empty,
                    "Process start returned false.",
                    false,
                    false);
            }
        }
        catch (Exception ex)
        {
            return new NetworkResetCommandResult(
                command,
                -1,
                string.Empty,
                ex.Message,
                false,
                false);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return new NetworkResetCommandResult(
                command,
                process.ExitCode,
                await stdout.ConfigureAwait(false),
                await stderr.ConfigureAwait(false),
                false,
                true);
        }
        catch (OperationCanceledException)
        {
            Exception? terminationError = null;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                terminationError = ex;
            }

            // Never release the global network-stack lease while a started writer may still run.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var timedOut = timeoutCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested;
            return new NetworkResetCommandResult(
                command,
                -1,
                string.Empty,
                terminationError is null
                    ? timedOut ? "Command timed out." : "Command cancelled after start."
                    : $"Command termination required waiting for natural exit: {terminationError.GetType().Name}",
                timedOut,
                true,
                !timedOut);
        }
    }

    private static bool IsReadOnly(NetworkResetCommand command) => command is
        NetworkResetCommand.ShowDnsCache or
        NetworkResetCommand.ShowWinsockCatalog or
        NetworkResetCommand.ShowIpv4Configuration or
        NetworkResetCommand.ShowIpv6Configuration or
        NetworkResetCommand.ShowWinHttpProxy;

    private static (string FileName, string[] Arguments) CommandLine(
        NetworkResetCommand command) => command switch
    {
        NetworkResetCommand.ShowDnsCache => ("ipconfig.exe", ["/displaydns"]),
        NetworkResetCommand.ShowWinsockCatalog => ("netsh.exe", ["winsock", "show", "catalog"]),
        NetworkResetCommand.ShowIpv4Configuration => ("netsh.exe", ["interface", "ipv4", "show", "config"]),
        NetworkResetCommand.ShowIpv6Configuration => ("netsh.exe", ["interface", "ipv6", "show", "interface"]),
        NetworkResetCommand.ShowWinHttpProxy => ("netsh.exe", ["winhttp", "show", "proxy"]),
        NetworkResetCommand.FlushDnsCache => ("ipconfig.exe", ["/flushdns"]),
        NetworkResetCommand.ResetWinsock => ("netsh.exe", ["winsock", "reset"]),
        NetworkResetCommand.ResetIpv4 => ("netsh.exe", ["int", "ipv4", "reset"]),
        NetworkResetCommand.ResetIpv6 => ("netsh.exe", ["int", "ipv6", "reset"]),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };
}
