// File: GameOptimizer.Core/Optimization/Operations/OrchestratedHostsBackupOperation.cs
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Hosts;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Typed owner for the "cleanup.hosts_backup" catalog entry. It delegates to the single central
/// <see cref="IHostsBackupOwner"/> for atomic hosts backup (hash-verified) and byte-exact restore.
/// It never edits hosts content, never appends block lists and never fetches anything from the network.
/// </summary>
public sealed class OrchestratedHostsBackupOperation : OrchestratedChangeOperation
{
    private readonly IHostsBackupOwner? _owner;

    public OrchestratedHostsBackupOperation(
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        IHostsBackupOwner? owner)
        : base(
            NullOrchestrator.Instance,
            id,
            title,
            description,
            category,
            OperationType.Other,
            riskLevel,
            benefit,
            drawback,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        _owner = owner;
    }

    public override bool RequiresSnapshotForRestore => false;

    public override async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_owner is null)
        {
            return new OperationStatus(
                false,
                "中央 hosts 备份 Owner 未接入，该入口保持阻断。",
                SupportState: OperationSupportState.Unknown,
                SupportReasonCode: "hosts.owner-unavailable");
        }

        try
        {
            var assessment = await _owner.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (!assessment.HasRequiredPermission)
            {
                return new OperationStatus(
                    false,
                    assessment.Details,
                    SupportState: assessment.ReasonCode.StartsWith("permission.", StringComparison.OrdinalIgnoreCase)
                        ? OperationSupportState.PermissionRequired
                        : OperationSupportState.Unsupported,
                    SupportReasonCode: assessment.ReasonCode);
            }

            var hosts = await _owner.ReadHostsAsync(cancellationToken).ConfigureAwait(false);
            var backup = await _owner.ReadBackupAsync(cancellationToken).ConfigureAwait(false);
            return backup.BackupExists switch
            {
                false => new OperationStatus(false, "尚无 hosts 备份副本。"),
                _ when !string.Equals(backup.ContentSha256, hosts.ContentSha256, StringComparison.Ordinal)
                    => new OperationStatus(false, "hosts 已发生变化，备份副本已过期，可刷新备份或恢复到备份点。"),
                _ => new OperationStatus(true, "备份副本存在且与当前 hosts 一致。")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperationStatus(
                false,
                $"hosts 备份状态探测失败：{ex.Message}",
                SupportState: OperationSupportState.Unknown,
                SupportReasonCode: "hosts.probe-failed");
        }
    }

    public override async Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (_owner is null)
        {
            return new OperationInvokeResult(false, false, "中央 hosts 备份 Owner 未接入，未执行任何文件操作。");
        }

        var assessment = await _owner.ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (!assessment.HasRequiredPermission)
        {
            return new OperationInvokeResult(false, false, $"{assessment.ReasonCode}: {assessment.Details}");
        }

        var mutation = await _owner.BackupAsync(cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(mutation.Success, mutation.MutationOccurred, mutation.Message);
    }

    public override async Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (_owner is null)
        {
            return new OperationInvokeResult(false, false, "中央 hosts 备份 Owner 未接入，拒绝恢复。");
        }

        var mutation = await _owner.RestoreAsync(cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(mutation.Success, mutation.MutationOccurred, mutation.Message);
    }

    private sealed class NullOrchestrator : IChangeOrchestrator
    {
        public static readonly NullOrchestrator Instance = new();

        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<StateObservation?>(null);

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken, bool registerPlan = true) =>
            Task.FromResult(new PreparationResult(ChangePreparationStatus.Blocked, null, "hosts-backup.not-orchestrated", "hosts 备份不经过变更协调器，直接委托中央 Owner。", null));

        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken) =>
            throw new NotSupportedException("hosts 备份操作不经过协调器执行。");

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("hosts 备份操作不经过协调器恢复。");
    }
}
