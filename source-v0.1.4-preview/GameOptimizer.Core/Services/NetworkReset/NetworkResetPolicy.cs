using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.NetworkReset;

internal static class NetworkResetPolicy
{
    internal static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan PersistenceTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan DefaultResourceLockTimeout = TimeSpan.FromSeconds(15);
    internal const string ResourceKey = "network-stack:machine";
    internal const string PolicyVersion = "network-reset.durable.v2";

    internal static string ComputePlanHash(NetworkResetPlan plan)
    {
        var material = new PlanHashMaterial(
            plan.ChangePlan.PlanId,
            plan.ChangePlan.Change,
            plan.ChangePlan.PreparedAtUtc,
            plan.ChangePlan.ExpiresAtUtc,
            plan.ChangePlan.RequiresAdministrator,
            plan.ChangePlan.RequiresExplicitConfirmation,
            plan.ChangePlan.RequiresRecoveryMaterial,
            plan.ChangePlan.Risk,
            plan.ChangePlan.ExecutionPolicyVersion,
            plan.ChangePlan.ExecutionPolicyFingerprint,
            plan.Profile,
            plan.Snapshot,
            plan.Steps,
            plan.Risks,
            plan.RebootRequired,
            plan.RecoveryDecision);
        return Hash(Serialize(material));
    }

    internal static bool IsPlanInternallyConsistent(NetworkResetPlan plan)
    {
        try
        {
            var captureCommands = CaptureCommands(plan.Profile);
            var expectedSteps = MutationCommands(plan.Profile);
            var expectedRisks = Risks(plan.Profile);
            var expectedRecovery = plan.Profile == NetworkResetProfile.DnsCache
                ? NetworkRecoveryDecision.NotRequired
                : NetworkRecoveryDecision.ManualRecoveryOnly;
            var expectedSnapshotHash = Hash(Serialize(plan.Snapshot.Entries));
            return plan.ChangePlan.PlanId != Guid.Empty
                && string.Equals(
                    plan.ChangePlan.Change.CapabilityId,
                    $"network.reset.{plan.Profile.ToString().ToLowerInvariant()}",
                    StringComparison.Ordinal)
                && string.Equals(plan.ChangePlan.Change.DefinitionVersion, "2.0.0", StringComparison.Ordinal)
                && plan.ChangePlan.Change.Scope.Kind == TargetScopeKind.Machine
                && string.Equals(plan.ChangePlan.Change.Scope.Identity, Environment.MachineName, StringComparison.Ordinal)
                && string.Equals(plan.ChangePlan.Change.BeforePayload, Serialize(plan.Snapshot), StringComparison.Ordinal)
                && FixedHashEquals(plan.ChangePlan.Change.BeforeFingerprint, plan.Snapshot.IdentityHash)
                && FixedHashEquals(plan.Snapshot.IdentityHash, expectedSnapshotHash)
                && plan.Snapshot.Entries.Select(item => item.Command).SequenceEqual(captureCommands)
                && !plan.Snapshot.IsMachineRestorable
                && string.Equals(plan.Snapshot.RecoveryBoundary, RecoveryBoundary(plan.Profile), StringComparison.Ordinal)
                && string.Equals(plan.ChangePlan.Change.TargetPayload, Serialize(expectedSteps), StringComparison.Ordinal)
                && FixedHashEquals(plan.ChangePlan.Change.TargetFingerprint, Hash(plan.ChangePlan.Change.TargetPayload))
                && string.Equals(plan.ChangePlan.Change.ResourceKey, ResourceKey, StringComparison.Ordinal)
                && plan.Steps.SequenceEqual(expectedSteps)
                && plan.Risks.SequenceEqual(expectedRisks, StringComparer.Ordinal)
                && plan.RebootRequired == (plan.Profile != NetworkResetProfile.DnsCache)
                && plan.RecoveryDecision == expectedRecovery
                && plan.ChangePlan.RequiresAdministrator
                && plan.ChangePlan.RequiresExplicitConfirmation
                && plan.ChangePlan.RequiresRecoveryMaterial == (plan.Profile != NetworkResetProfile.DnsCache)
                && plan.ChangePlan.Risk == ChangeRisk.HighSystem
                && plan.ChangePlan.ExpiresAtUtc - plan.ChangePlan.PreparedAtUtc == PlanLifetime
                && string.Equals(plan.ChangePlan.ExecutionPolicyVersion, PolicyVersion, StringComparison.Ordinal)
                && string.Equals(plan.ChangePlan.ExecutionPolicyFingerprint, PolicyFingerprint(), StringComparison.Ordinal)
                && FixedHashEquals(plan.ChangePlan.PlanHash, ComputePlanHash(plan));
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException or JsonException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<NetworkResetCommand> CaptureCommands(NetworkResetProfile profile) => profile switch
    {
        NetworkResetProfile.DnsCache => [NetworkResetCommand.ShowDnsCache],
        NetworkResetProfile.Winsock => [NetworkResetCommand.ShowWinsockCatalog],
        NetworkResetProfile.TcpIp =>
        [
            NetworkResetCommand.ShowIpv4Configuration,
            NetworkResetCommand.ShowIpv6Configuration,
            NetworkResetCommand.ShowWinHttpProxy
        ],
        NetworkResetProfile.Full =>
        [
            NetworkResetCommand.ShowDnsCache,
            NetworkResetCommand.ShowWinsockCatalog,
            NetworkResetCommand.ShowIpv4Configuration,
            NetworkResetCommand.ShowIpv6Configuration,
            NetworkResetCommand.ShowWinHttpProxy
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    internal static IReadOnlyList<NetworkResetCommand> MutationCommands(NetworkResetProfile profile) => profile switch
    {
        NetworkResetProfile.DnsCache =>
        [
            NetworkResetCommand.FlushDnsCache
        ],
        NetworkResetProfile.Winsock => [NetworkResetCommand.ResetWinsock],
        NetworkResetProfile.TcpIp =>
        [
            NetworkResetCommand.ResetIpv4,
            NetworkResetCommand.ResetIpv6
        ],
        NetworkResetProfile.Full =>
        [
            NetworkResetCommand.ResetWinsock,
            NetworkResetCommand.ResetIpv4,
            NetworkResetCommand.ResetIpv6,
            NetworkResetCommand.FlushDnsCache
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    internal static IReadOnlyList<string> Risks(NetworkResetProfile profile) => profile switch
    {
        NetworkResetProfile.DnsCache =>
        [
            "仅执行 DNS 缓存刷新；不会重新注册 DNS，也不包含防火墙重置。"
        ],
        NetworkResetProfile.Winsock =>
        [
            "Winsock 目录重置不能由文本快照自动逆转。",
            "执行后需要用户手动重启。"
        ],
        NetworkResetProfile.TcpIp =>
        [
            "TCP/IP 重置可能影响静态地址、VPN 与自定义路由，不能保证自动回滚。",
            "执行后需要用户手动重启。"
        ],
        NetworkResetProfile.Full =>
        [
            "组合重置不能自动逆转，可能影响 VPN、静态地址和路由。",
            "明确不包含 firewall reset。",
            "执行后需要用户手动重启。"
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    internal static string RecoveryBoundary(NetworkResetProfile profile) => profile switch
    {
        NetworkResetProfile.DnsCache =>
            "DNS 缓存是临时状态；后续只读查询可访问不等于能够证明缓存内容已清空。无需恢复材料或重启。",
        NetworkResetProfile.Winsock =>
            "Winsock reset 无法从 catalog 文本无损还原；只能手动重启，并在异常时人工重配网络。",
        NetworkResetProfile.TcpIp =>
            "TCP/IP reset 无法从只读配置快照自动还原；必须保留基线并准备人工恢复静态地址、VPN 和路由。",
        NetworkResetProfile.Full =>
            "Full 不包含 firewall reset；Winsock/TCP 写入不可自动逆转，必须手动重启并准备人工网络重配置。",
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    internal static string RecoveryInstructions(
        NetworkResetProfile profile,
        bool mutationOccurred,
        NetworkResetOutcome outcome)
    {
        if (!mutationOccurred)
        {
            return "未执行网络写入；可重新 Prepare 生成新计划。";
        }
        if (profile == NetworkResetProfile.DnsCache
            && outcome == NetworkResetOutcome.SucceededVerified)
        {
            return "DNS 刷新命令已成功退出，后续只读查询可访问；该回读不能证明缓存内容已清空。无需恢复或重启。";
        }
        if (profile == NetworkResetProfile.DnsCache)
        {
            return "不要自动重试；先检查 DNS Client 状态和名称解析，再由用户决定是否重新生成计划。";
        }
        return "不要自动重试。手动重启 Windows 后检查联网、静态 IP、DNS、VPN、代理和自定义路由；异常时依据首次基线人工重配。此流程没有自动 RecoveryId。";
    }

    internal static bool IsDnsCommand(NetworkResetCommand command) =>
        command == NetworkResetCommand.FlushDnsCache;

    internal static string Classify(NetworkResetCommandResult result)
    {
        if (!result.ProcessStarted)
        {
            return "process.start-failed";
        }
        if (result.TimedOut)
        {
            return "process.timeout-write-unknown";
        }
        if (result.CancellationObservedAfterStart)
        {
            return "process.cancelled-write-unknown";
        }
        return result.ExitCode == 0 ? "process.succeeded" : $"process.exit-{result.ExitCode}";
    }

    internal static string PolicyFingerprint() =>
        $"{CommandTimeout.Ticks}:{VerificationTimeout.Ticks}:{PersistenceTimeout.Ticks}";

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    internal static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));

    internal static bool FixedHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record PlanHashMaterial(
        Guid PlanId,
        PreparedCapabilityChange Change,
        DateTimeOffset PreparedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        bool RequiresAdministrator,
        bool RequiresExplicitConfirmation,
        bool RequiresRecoveryMaterial,
        ChangeRisk Risk,
        string ExecutionPolicyVersion,
        string ExecutionPolicyFingerprint,
        NetworkResetProfile Profile,
        NetworkResetSnapshot Snapshot,
        IReadOnlyList<NetworkResetCommand> Steps,
        IReadOnlyList<string> Risks,
        bool RebootRequired,
        NetworkRecoveryDecision RecoveryDecision);
}
