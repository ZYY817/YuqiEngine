// File: GameOptimizer.Core/Optimization/Operations/OrchestratedDnsCacheFlushOperation.cs
using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.NetworkReset;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Typed owner for the "network.clear_dns_cache" optimization entry. It delegates to the single
/// central <see cref="INetworkResetOwner"/> (DnsCache profile), which owns the read-only snapshot,
/// baseline gate, confirmation, global network-stack resource lock, durable receipt, read-back and
/// restart-consistency semantics. It never writes DNS state directly and never fabricates recovery:
/// DNS cache is volatile runtime data, so the DnsCache profile carries <c>NotRequired</c> recovery.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OrchestratedDnsCacheFlushOperation : OrchestratedChangeOperation
{
    private const string BenefitText = "清除过期或错误的 DNS 缓存，使解析器依赖实时权威应答。";
    private const string DrawbackText = "首次访问网站时需重新解析，可能略微增加首包延迟。";

    private readonly INetworkResetOwner? _networkResetOwner;

    public OrchestratedDnsCacheFlushOperation(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        INetworkResetOwner? networkResetOwner)
        : base(
            orchestrator,
            "network.clear_dns_cache",
            "刷新 DNS 解析器缓存",
            "通过中央网络重置 Owner 的 DnsCache 流程刷新解析器缓存，使解析器依赖实时权威应答。",
            "网络稳定",
            OperationType.SystemApi,
            OperationRiskLevel.Safe,
            BenefitText,
            DrawbackText,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        _networkResetOwner = networkResetOwner;
    }

    public override async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (_networkResetOwner is null)
        {
            return new OperationStatus(
                false,
                "中央网络重置 Owner 未接入，DNS 缓存刷新保持阻断。",
                SupportState: OperationSupportState.Unknown,
                SupportReasonCode: "network-reset.owner-unavailable");
        }

        NetworkResetReadOnlyStatus status;
        try
        {
            status = await _networkResetOwner
                .ReadStatusAsync(NetworkResetProfile.DnsCache, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperationStatus(
                false,
                $"DNS 缓存只读探测失败：{ex.GetType().Name}",
                SupportState: OperationSupportState.Unknown,
                SupportReasonCode: "network-reset.probe-failed");
        }

        if (!status.IsSupported)
        {
            return new OperationStatus(
                false,
                status.Message,
                SupportState: OperationSupportState.Unsupported,
                SupportReasonCode: status.ReasonCode);
        }

        if (!status.IsAdministrator)
        {
            return new OperationStatus(
                false,
                status.Message,
                SupportState: OperationSupportState.PermissionRequired,
                SupportReasonCode: status.ReasonCode);
        }

        if (!status.SnapshotAvailable || status.Snapshot is null)
        {
            return new OperationStatus(
                false,
                status.Message,
                SupportState: OperationSupportState.Unknown,
                SupportReasonCode: status.ReasonCode);
        }

        // DNS cache is volatile runtime data; "compliant" means the owner can produce a fresh
        // typed snapshot and is ready to execute. It does not claim the cache is already empty.
        return new OperationStatus(false, "DNS 缓存刷新流程就绪（需要管理员权限与显式确认）。");
    }

    public override async Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (_networkResetOwner is null)
        {
            return new OperationInvokeResult(false, false, "中央网络重置 Owner 未接入，未执行任何网络写入。");
        }

        NetworkResetReceipt receipt;
        try
        {
            receipt = await _networkResetOwner
                .RunInteractiveAsync(NetworkResetProfile.DnsCache, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new OperationInvokeResult(false, false, "DNS 缓存刷新已取消，未执行网络写入。");
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, $"DNS 缓存刷新失败：{ex.GetType().Name}");
        }

        return receipt.Outcome switch
        {
            NetworkResetOutcome.SucceededVerified =>
                new OperationInvokeResult(true, true, receipt.Message),
            NetworkResetOutcome.CancelledBeforeWrite or NetworkResetOutcome.Denied =>
                new OperationInvokeResult(false, false, receipt.Message),
            NetworkResetOutcome.Blocked or NetworkResetOutcome.Conflict or NetworkResetOutcome.FailedNoChange =>
                new OperationInvokeResult(false, false, receipt.Message),
            _ => new OperationInvokeResult(false, receipt.MutationOccurred, receipt.Message)
        };
    }

    public override Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        // DNS cache is volatile runtime data rebuilt by the resolver on subsequent lookups;
        // the DnsCache profile carries NotRequired recovery, so there is no durable material to
        // restore and no fabricated success.
        return Task.FromResult(new OperationInvokeResult(
            true,
            false,
            "DNS 缓存属于易失性运行时数据，系统会在后续联网过程中自动重建，无需回退。"));
    }
}
