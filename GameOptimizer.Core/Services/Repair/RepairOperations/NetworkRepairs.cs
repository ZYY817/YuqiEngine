using System.Net.NetworkInformation;
using GameOptimizer.Core.Services.NetworkReset;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class NetworkRepairs
{
    public static IEnumerable<RepairItem> GetAll(
        INetworkResetOwner? networkResetOwner = null,
        NetworkRepairHealthProbe? healthProbe = null)
    {
        healthProbe ??= new NetworkRepairHealthProbe();
        yield return new RepairItem
        {
            Id = "repair.tcp_ip",
            Name = "重置 TCP/IP 协议栈",
            NameKey = "Repair.TcpIp.Name",
            Description = "修复上不了网、网络协议损坏问题",
            DescriptionKey = "Repair.TcpIp.Desc",
            Icon = "🌐",
            Category = RepairCategory.Network,
            CheckHealthAsync = healthProbe.IsTcpIpHealthyAsync,
            RepairAsync = progress => RunOwnedResetAsync(networkResetOwner, NetworkResetProfile.TcpIp, progress)
        };

        yield return new RepairItem
        {
            Id = "repair.winsock",
            Name = "重置 Winsock",
            NameKey = "Repair.Winsock.Name",
            Description = "修复网络套接字损坏、无法连接问题",
            DescriptionKey = "Repair.Winsock.Desc",
            Icon = "🔌",
            Category = RepairCategory.Network,
            CheckHealthAsync = healthProbe.IsWinsockHealthyAsync,
            RepairAsync = progress => RunOwnedResetAsync(networkResetOwner, NetworkResetProfile.Winsock, progress)
        };

        yield return new RepairItem
        {
            Id = "repair.dns",
            Name = "刷新 DNS 缓存",
            NameKey = "Repair.Dns.Name",
            Description = "修复 DNS 解析失败、网站打不开问题",
            DescriptionKey = "Repair.Dns.Desc",
            Icon = "📡",
            Category = RepairCategory.Network,
            CheckHealthAsync = healthProbe.IsDnsHealthyAsync,
            RepairAsync = progress => RunOwnedResetAsync(networkResetOwner, NetworkResetProfile.DnsCache, progress)
        };

        yield return new RepairItem
        {
            Id = "repair.network_adapter",
            Name = "重置网络适配器",
            NameKey = "Repair.NetworkAdapter.Name",
            Description = "修复网卡不工作、无法获取 IP 问题",
            DescriptionKey = "Repair.NetworkAdapter.Desc",
            Icon = "📶",
            Category = RepairCategory.Network,
            CheckHealthAsync = CheckNetworkAdapterHealthAsync,
            RepairAsync = _ => Task.FromResult(RepairResult.Failed(
                "网络适配器高级属性重置不等同于完整网络重置；专属 typed snapshot/readback/restore 证据不足，已安全阻断。"))
        };

        yield return new RepairItem
        {
            Id = "repair.dhcp",
            Name = "修复 DHCP 服务",
            NameKey = "Repair.Dhcp.Name",
            Description = "修复无法自动获取 IP 地址问题",
            DescriptionKey = "Repair.Dhcp.Desc",
            Icon = "🔄",
            Category = RepairCategory.Network,
            CheckHealthAsync = CheckDhcpHealthAsync,
            RepairAsync = _ => Task.FromResult(RepairResult.Failed(
                "DHCP 服务与租约修复不属于现有 Network Reset Owner 的固定能力；专属 typed snapshot/readback/restore 证据不足，已安全阻断。"))
        };

        yield return new RepairItem
        {
            Id = "repair.network_reset",
            Name = "完全重置网络",
            NameKey = "Repair.NetworkReset.Name",
            Description = "重置所有网络设置到默认值（需要重启）",
            DescriptionKey = "Repair.NetworkReset.Desc",
            Icon = "🔧",
            Category = RepairCategory.Network,
            IsRisky = true,
            CheckHealthAsync = healthProbe.IsFullNetworkHealthyAsync,
            RepairAsync = progress => RunOwnedResetAsync(networkResetOwner, NetworkResetProfile.Full, progress)
        };
    }

    private static async Task<bool> CheckNetworkAdapterHealthAsync()
    {
        try
        {
            // Why: GetAllNetworkInterfaces 是同步阻塞调用，在某些驱动异常时可能卡住数秒
            var adapters = await Task.Run(NetworkInterface.GetAllNetworkInterfaces).ConfigureAwait(false);
            return adapters.Any(a => 
                a.OperationalStatus == OperationalStatus.Up && 
                a.NetworkInterfaceType != NetworkInterfaceType.Loopback);
        }
        catch { return false; }
    }

    private static async Task<bool> CheckDhcpHealthAsync()
    {
        try
        {
            var result = await RepairHelper.RunCommandAsync("sc", "query Dhcp", 5000);
            return result.Contains("RUNNING");
        }
        catch { return false; }
    }

    private static async Task<RepairResult> RunOwnedResetAsync(
        INetworkResetOwner? owner,
        NetworkResetProfile profile,
        IProgress<RepairProgress>? progress)
    {
        if (owner is null)
            return RepairResult.Failed("Network Reset Owner 未接入，已安全阻断；不会回退旧命令直写。");

        progress?.Report(new RepairProgress { Message = "正在准备网络快照与安全计划…", PercentComplete = 10 });
        var receipt = await owner.RunInteractiveAsync(profile).ConfigureAwait(false);
        progress?.Report(new RepairProgress { Message = receipt.Message, PercentComplete = 100 });
        return receipt.Outcome is NetworkResetOutcome.SucceededVerified
            or NetworkResetOutcome.AppliedRequiresRestartManualRecovery
            ? RepairResult.Succeeded(receipt.Message, receipt.RebootRequired)
            : RepairResult.Failed(receipt.Message, detail: receipt.ReasonCode);
    }
}
