using System.Security.Cryptography;
using System.Text;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;

namespace GameOptimizer.Core.Services.Snapshot;

public enum SnapshotRestoreKind
{
    Normal,
    DeepRegistry
}

public enum SnapshotRestoreResourceKind
{
    TypedCapability,
    UnsupportedLegacyOperation,
    UnsupportedRawRegistry,
    UnsupportedPowerPlan,
    UnsupportedService,
    UnsupportedBcd,
    UnsupportedRegistryFile
}

public enum SnapshotRestoreBatchState
{
    Available,
    Claiming,
    Restoring,
    ReleasingZeroWrite,
    CompletingVerified,
    RestoredVerified,
    CompensatedVerified,
    InterruptedManual,
    ManualResolved
}

public enum SnapshotRestoreBatchClaimState
{
    Available,
    Claiming,
    Active,
    ReleasePending,
    Released,
    CompletingVerified,
    Restored,
    Compensated,
    Conflict,
    ManualRequired,
    ManualResolved
}

public enum SnapshotRestoreManualDecision
{
    KeepBlocked,
    ReleaseVerifiedZeroWrite,
    AcknowledgeManualRecovery
}

public enum SnapshotRestoreRecoveryStatus
{
    Available,
    RestoredVerified,
    CompensatedVerified,
    ManualInterventionRequired,
    ManualResolved,
    StoreFailure
}

public sealed record SnapshotRestoreRecoveryReport(
    Guid PlanId,
    SnapshotRestoreRecoveryStatus Status,
    SnapshotRestoreBatchState? BatchState,
    SnapshotRestoreBatchClaimState? ClaimState,
    SnapshotMutationStatus MutationStatus,
    int? TypedResourceCount,
    int? RecoverableResourceCount,
    int? UnknownResourceCount,
    int? ManualResourceCount,
    int? UnclassifiedResourceCount,
    string ReasonCode,
    string Message);

public enum SnapshotMutationStatus
{
    None,
    Known,
    Unknown
}

public enum SnapshotCompensationStatus
{
    NotRequired,
    VerifiedBeforeState,
    FailedOrUnknown
}

public enum SnapshotRestoreStepState
{
    Pending,
    Executing,
    NoChange,
    RestoredVerified,
    Unsupported,
    Failed,
    Cancelled,
    Unknown,
    Compensating,
    CompensatedVerified,
    CompensationFailed
}

public sealed record SnapshotRestoreStepPlan(
    string StepId,
    SnapshotRestoreResourceKind ResourceKind,
    string? CapabilityId,
    string TargetSha256,
    bool Supported,
    string ReasonCode);

public sealed record SnapshotRestorePlan(
    int SchemaVersion,
    Guid PlanId,
    string SnapshotId,
    SnapshotRestoreKind Kind,
    string SnapshotIdentitySha256,
    IReadOnlyList<SnapshotRestoreStepPlan> Steps,
    string PlanSha256,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record SnapshotRestoreAuthorizationDecision(bool Allowed, string ReasonCode, string Message);

public interface ISnapshotRestoreAuthorizationGate
{
    Task<SnapshotRestoreAuthorizationDecision> AuthorizeAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken);
}

public sealed class DenySnapshotRestoreAuthorizationGate : ISnapshotRestoreAuthorizationGate
{
    public Task<SnapshotRestoreAuthorizationDecision> AuthorizeAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SnapshotRestoreAuthorizationDecision(
            false,
            "snapshot.baseline-gate-missing",
            "快照恢复未接入已验证的首次基线门禁，已阻断。"));
    }
}

internal sealed record SnapshotRestorePreparation(
    string IdentitySha256,
    IReadOnlyList<SnapshotRestoreStepPlan> Steps);

internal sealed record SnapshotRestoreExecutionVerdict(
    bool RestoredVerified,
    bool ExplicitZeroWrite,
    bool CompensatedVerified,
    SnapshotMutationStatus MutationStatus,
    string ReasonCode);

internal sealed class SnapshotRestoreCoordinator
{
    internal const int CurrentSchemaVersion = 2;
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(2);

    private readonly ISnapshotRestoreAuthorizationGate _authorizationGate;
    private readonly IRecoveryClaimStore _claimStore;
    private readonly ISnapshotRestoreBatchStore _batchStore;
    private readonly Func<string, SnapshotRestoreKind, CancellationToken, Task<SnapshotRestorePreparation>> _preparationReader;
    private readonly Func<SnapshotRestorePlan, CancellationToken, Task<bool>> _confirmation;
    private readonly TimeProvider _timeProvider;

    internal SnapshotRestoreCoordinator(
        ISnapshotRestoreAuthorizationGate authorizationGate,
        Func<string, SnapshotRestoreKind, CancellationToken, Task<string>> identityReader,
        IRecoveryClaimStore? claimStore = null,
        TimeProvider? timeProvider = null)
        : this(
            authorizationGate,
            async (snapshotId, kind, cancellationToken) => new SnapshotRestorePreparation(
                await identityReader(snapshotId, kind, cancellationToken).ConfigureAwait(false),
                Array.Empty<SnapshotRestoreStepPlan>()),
            static (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(true);
            },
            claimStore,
            new InMemorySnapshotRestoreBatchStore(),
            timeProvider)
    {
    }

    internal SnapshotRestoreCoordinator(
        ISnapshotRestoreAuthorizationGate authorizationGate,
        Func<string, SnapshotRestoreKind, CancellationToken, Task<SnapshotRestorePreparation>> preparationReader,
        Func<SnapshotRestorePlan, CancellationToken, Task<bool>> confirmation,
        IRecoveryClaimStore? claimStore = null,
        ISnapshotRestoreBatchStore? batchStore = null,
        TimeProvider? timeProvider = null)
    {
        _authorizationGate = authorizationGate ?? throw new ArgumentNullException(nameof(authorizationGate));
        _preparationReader = preparationReader ?? throw new ArgumentNullException(nameof(preparationReader));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _claimStore = claimStore ?? new FileRecoveryClaimStore();
        _batchStore = batchStore ?? new FileSnapshotRestoreBatchStore();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal async Task<SnapshotRestorePlan> PrepareAsync(
        string snapshotId,
        SnapshotRestoreKind kind,
        CancellationToken cancellationToken)
    {
        ValidateSnapshotId(snapshotId);
        var preparation = await _preparationReader(snapshotId, kind, cancellationToken).ConfigureAwait(false);
        ValidatePreparation(preparation);
        var now = _timeProvider.GetUtcNow();
        var planId = Guid.NewGuid();
        var expires = now + PlanLifetime;
        var planHash = ComputePlanHash(
            CurrentSchemaVersion,
            planId,
            snapshotId,
            kind,
            preparation.IdentitySha256,
            preparation.Steps,
            now,
            expires);
        var plan = new SnapshotRestorePlan(
            CurrentSchemaVersion,
            planId,
            snapshotId,
            kind,
            preparation.IdentitySha256,
            preparation.Steps,
            planHash,
            now,
            expires);

        await _batchStore.RegisterAsync(plan, cancellationToken).ConfigureAwait(false);
        await _claimStore.RegisterAvailableAsync(plan.PlanId, plan.PlanSha256, cancellationToken).ConfigureAwait(false);
        return plan;
    }

    internal Task<T> ExecuteAsync<T>(
        SnapshotRestorePlan plan,
        SnapshotRestoreKind expectedKind,
        Func<CancellationToken, Task<T>> execute,
        Func<string, T> blocked,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            plan,
            expectedKind,
            execute,
            blocked,
            EvaluateCompatibilityResult,
            cancellationToken);

    internal async Task<T> ExecuteAsync<T>(
        SnapshotRestorePlan plan,
        SnapshotRestoreKind expectedKind,
        Func<CancellationToken, Task<T>> execute,
        Func<string, T> blocked,
        Func<T, SnapshotRestoreExecutionVerdict> evaluate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ValidatePlan(plan, expectedKind, _timeProvider.GetUtcNow(), out var validationError))
        {
            return blocked(validationError);
        }

        SnapshotRestoreAuthorizationDecision authorization;
        try
        {
            authorization = await _authorizationGate.AuthorizeAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return blocked("快照恢复授权检查失败，已阻断：" + ex.Message);
        }

        if (!authorization.Allowed)
        {
            return blocked(authorization.Message);
        }

        bool confirmed;
        try
        {
            confirmed = await _confirmation(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return blocked("快照恢复显式确认失败，已阻断：" + ex.Message);
        }

        if (!confirmed)
        {
            return blocked("用户未显式确认当前快照恢复计划，已阻断。");
        }

        var current = await _preparationReader(plan.SnapshotId, plan.Kind, cancellationToken).ConfigureAwait(false);
        ValidatePreparation(current);
        if (!FixedEquals(plan.SnapshotIdentitySha256, current.IdentitySha256)
            || !StepsEqual(plan.Steps, current.Steps))
        {
            return blocked("快照内容在确认后发生变化，原恢复计划已失效。");
        }

        try
        {
            await _batchStore.SetClaimingAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return blocked("Snapshot restore batch is not durably available for claim: " + ex.Message);
        }
        RecoveryClaimResult claim;
        try
        {
            claim = await _claimStore.TryBeginRestoreAsync(
                plan.PlanId,
                plan.PlanSha256,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await InterruptAsync(plan, SnapshotMutationStatus.None, "snapshot.claim-cancelled").ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await InterruptAsync(plan, SnapshotMutationStatus.None, "snapshot.claim-store-failed").ConfigureAwait(false);
            return blocked("Snapshot restore claim cannot be read or persisted reliably; manual review is required: " + ex.Message);
        }
        if (claim.Status != RecoveryClaimStatus.Claimed || claim.ClaimId is null)
        {
            await InterruptAsync(
                plan,
                SnapshotMutationStatus.None,
                "snapshot.claim-" + claim.Status.ToString().ToLowerInvariant()).ConfigureAwait(false);
            return blocked("恢复计划不存在、已领取、已执行或完整性校验失败。");
        }

        try
        {
            await _batchStore.BeginAsync(plan, claim.ClaimId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await _claimStore.ReleaseAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    claim.ClaimId.Value,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The durable Claiming state remains fail-closed for restart reconciliation.
            }
            throw;
        }
        var invoked = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            invoked = true;
            var result = await execute(cancellationToken).ConfigureAwait(false);
            var verdict = evaluate(result);

            if (verdict.RestoredVerified)
            {
                await _batchStore.SetStateAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    SnapshotRestoreBatchState.CompletingVerified,
                    verdict.MutationStatus,
                    verdict.ReasonCode,
                    CancellationToken.None).ConfigureAwait(false);
                var completing = await _batchStore.LoadAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    CancellationToken.None).ConfigureAwait(false);
                if (completing is null || !HasCompleteVerifiedEvidence(completing))
                {
                    await InterruptAsync(
                        plan,
                        SnapshotMutationStatus.Unknown,
                        "snapshot.completion-evidence-incomplete").ConfigureAwait(false);
                    ApplyBatchState(result, SnapshotRestoreBatchState.InterruptedManual, SnapshotMutationStatus.Unknown);
                    InvalidateVerifiedResult(result);
                    return result;
                }
                await _claimStore.MarkRestoredAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    claim.ClaimId.Value,
                    CancellationToken.None).ConfigureAwait(false);
                await _batchStore.SetStateAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    SnapshotRestoreBatchState.RestoredVerified,
                    verdict.MutationStatus,
                    verdict.ReasonCode,
                    CancellationToken.None).ConfigureAwait(false);
                ApplyBatchState(result, SnapshotRestoreBatchState.RestoredVerified, verdict.MutationStatus);
                return result;
            }

            if (verdict.ExplicitZeroWrite)
            {
                await ReleaseZeroWriteAsync(plan, claim.ClaimId.Value, verdict.ReasonCode).ConfigureAwait(false);
                ApplyBatchState(result, SnapshotRestoreBatchState.Available, SnapshotMutationStatus.None);
                return result;
            }

            if (verdict.CompensatedVerified)
            {
                await _batchStore.SetStateAsync(
                    plan.PlanId,
                    plan.PlanSha256,
                    SnapshotRestoreBatchState.CompensatedVerified,
                    verdict.MutationStatus,
                    verdict.ReasonCode,
                    CancellationToken.None).ConfigureAwait(false);
                ApplyBatchState(result, SnapshotRestoreBatchState.CompensatedVerified, verdict.MutationStatus);
                return result;
            }

            await InterruptAsync(plan, verdict.MutationStatus, verdict.ReasonCode).ConfigureAwait(false);
            ApplyBatchState(result, SnapshotRestoreBatchState.InterruptedManual, verdict.MutationStatus);
            return result;
        }
        catch
        {
            if (!invoked)
            {
                await ReleaseZeroWriteAsync(
                    plan,
                    claim.ClaimId.Value,
                    "snapshot.cancelled-before-execute").ConfigureAwait(false);
            }
            else
            {
                var batch = await _batchStore.LoadAsync(plan.PlanId, plan.PlanSha256, CancellationToken.None).ConfigureAwait(false);
                var mutation = batch?.State == SnapshotRestoreBatchState.ReleasingZeroWrite
                    ? SnapshotMutationStatus.None
                    : batch?.MutationStatus == SnapshotMutationStatus.None
                    ? SnapshotMutationStatus.Unknown
                    : batch?.MutationStatus ?? SnapshotMutationStatus.Unknown;
                await InterruptAsync(plan, mutation, "snapshot.execution-exception").ConfigureAwait(false);
            }
            throw;
        }
    }

    internal async Task<SnapshotRestoreRecoveryReport> ReconcileAsync(
        Guid planId,
        string planSha256,
        CancellationToken cancellationToken)
    {
        SnapshotRestoreBatchDocument? batch;
        try
        {
            batch = await _batchStore.LoadAsync(planId, planSha256, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StoreFailure(planId, "snapshot.batch-store-corrupt", ex);
        }

        if (batch is null)
        {
            return StoreFailure(planId, "snapshot.batch-missing", null);
        }

        switch (batch.State)
        {
            case SnapshotRestoreBatchState.Claiming:
                await _batchStore.SetStateAsync(
                    planId,
                    planSha256,
                    SnapshotRestoreBatchState.InterruptedManual,
                    SnapshotMutationStatus.None,
                    "snapshot.restart-claim-unknown",
                    CancellationToken.None).ConfigureAwait(false);
                break;

            case SnapshotRestoreBatchState.Restoring:
                await _batchStore.SetStateAsync(
                    planId,
                    planSha256,
                    SnapshotRestoreBatchState.InterruptedManual,
                    SnapshotMutationStatus.Unknown,
                    "snapshot.restart-write-outcome-unknown",
                    CancellationToken.None).ConfigureAwait(false);
                break;

            case SnapshotRestoreBatchState.ReleasingZeroWrite:
                if (batch.ClaimId is null)
                {
                    return StoreFailure(planId, "snapshot.release-claim-missing", null);
                }
                try
                {
                    await _claimStore.ReleaseAsync(
                        planId,
                        planSha256,
                        batch.ClaimId.Value,
                        CancellationToken.None).ConfigureAwait(false);
                    await _batchStore.SetStateAsync(
                        planId,
                        planSha256,
                        SnapshotRestoreBatchState.Available,
                        SnapshotMutationStatus.None,
                        "snapshot.restart-release-completed",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await _batchStore.SetStateAsync(
                        planId,
                        planSha256,
                        SnapshotRestoreBatchState.InterruptedManual,
                        SnapshotMutationStatus.None,
                        "snapshot.restart-release-failed",
                        CancellationToken.None).ConfigureAwait(false);
                    batch = await _batchStore.LoadAsync(planId, planSha256, CancellationToken.None).ConfigureAwait(false);
                    return batch is null
                        ? StoreFailure(planId, "snapshot.batch-missing-after-release-failure", ex)
                        : Report(batch, "snapshot.restart-release-failed", ex.Message);
                }
                break;

            case SnapshotRestoreBatchState.CompletingVerified:
                if (batch.ClaimId is null || !HasCompleteVerifiedEvidence(batch))
                {
                    await _batchStore.SetStateAsync(
                        planId,
                        planSha256,
                        SnapshotRestoreBatchState.InterruptedManual,
                        SnapshotMutationStatus.Unknown,
                        "snapshot.restart-completion-evidence-incomplete",
                        CancellationToken.None).ConfigureAwait(false);
                    break;
                }
                // Durable receipts prove what was observed before the process stopped,
                // not what the resource contains after restart. Without a post-restart
                // readback source this batch must remain Unknown/manual; do not close
                // the claim or project historical evidence as a current terminal state.
                await _batchStore.SetStateAsync(
                    planId,
                    planSha256,
                    SnapshotRestoreBatchState.InterruptedManual,
                    SnapshotMutationStatus.Unknown,
                    "snapshot.restart-current-readback-required",
                    CancellationToken.None).ConfigureAwait(false);
                break;
        }

        batch = await _batchStore.LoadAsync(planId, planSha256, CancellationToken.None).ConfigureAwait(false);
        return batch is null
            ? StoreFailure(planId, "snapshot.batch-missing-after-reconcile", null)
            : Report(batch, batch.ReasonCode, "Snapshot restore batch reconciliation completed without executing a resource writer.");
    }

    internal async Task<SnapshotRestoreRecoveryReport> ResolveManualAsync(
        Guid planId,
        string planSha256,
        SnapshotRestoreManualDecision decision,
        CancellationToken cancellationToken)
    {
        SnapshotRestoreBatchDocument? batch;
        try
        {
            batch = await _batchStore.LoadAsync(planId, planSha256, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StoreFailure(planId, "snapshot.batch-store-corrupt", ex);
        }

        if (batch is null)
        {
            return StoreFailure(planId, "snapshot.batch-missing", null);
        }
        if (batch.State != SnapshotRestoreBatchState.InterruptedManual)
        {
            return Report(batch, "snapshot.manual-decision-not-applicable", "Only an interrupted manual batch can be adjudicated.");
        }
        if (decision == SnapshotRestoreManualDecision.KeepBlocked)
        {
            return Report(batch, "snapshot.manual-kept-blocked", "The batch remains blocked for manual recovery.");
        }
        if (decision == SnapshotRestoreManualDecision.AcknowledgeManualRecovery)
        {
            await _batchStore.SetStateAsync(
                planId,
                planSha256,
                SnapshotRestoreBatchState.ManualResolved,
                batch.MutationStatus,
                "snapshot.manual-recovery-acknowledged",
                CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            if (batch.ClaimId is null || !HasVerifiedZeroWriteEvidence(batch))
            {
                return Report(
                    batch,
                    "snapshot.manual-zero-write-evidence-insufficient",
                    "The durable receipts do not prove a zero-write outcome; the claim remains blocked.");
            }
            await _batchStore.SetStateAsync(
                planId,
                planSha256,
                SnapshotRestoreBatchState.ReleasingZeroWrite,
                SnapshotMutationStatus.None,
                "snapshot.manual-zero-write-release-pending",
                CancellationToken.None).ConfigureAwait(false);
            try
            {
                await _claimStore.ReleaseAsync(
                    planId,
                    planSha256,
                    batch.ClaimId.Value,
                    CancellationToken.None).ConfigureAwait(false);
                await _batchStore.SetStateAsync(
                    planId,
                    planSha256,
                    SnapshotRestoreBatchState.Available,
                    SnapshotMutationStatus.None,
                    "snapshot.manual-zero-write-released",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await _batchStore.SetStateAsync(
                    planId,
                    planSha256,
                    SnapshotRestoreBatchState.InterruptedManual,
                    SnapshotMutationStatus.None,
                    "snapshot.manual-release-failed",
                    CancellationToken.None).ConfigureAwait(false);
                batch = await _batchStore.LoadAsync(planId, planSha256, CancellationToken.None).ConfigureAwait(false);
                return batch is null
                    ? StoreFailure(planId, "snapshot.batch-missing-after-manual-release", ex)
                    : Report(batch, "snapshot.manual-release-failed", ex.Message);
            }
        }

        batch = await _batchStore.LoadAsync(planId, planSha256, CancellationToken.None).ConfigureAwait(false);
        return batch is null
            ? StoreFailure(planId, "snapshot.batch-missing-after-manual-decision", null)
            : Report(batch, batch.ReasonCode, "Manual adjudication was persisted; no resource writer was invoked.");
    }

    internal static void ValidateSnapshotId(string snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId)
            || snapshotId.Length > 64
            || snapshotId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidDataException("Snapshot identity is missing or contains invalid characters.");
        }
    }

    private async Task InterruptAsync(
        SnapshotRestorePlan plan,
        SnapshotMutationStatus mutationStatus,
        string reasonCode)
    {
        await _batchStore.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.InterruptedManual,
            mutationStatus,
            reasonCode,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReleaseZeroWriteAsync(
        SnapshotRestorePlan plan,
        Guid claimId,
        string reasonCode)
    {
        await _batchStore.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.ReleasingZeroWrite,
            SnapshotMutationStatus.None,
            "snapshot.claim-release-pending",
            CancellationToken.None).ConfigureAwait(false);
        await _claimStore.ReleaseAsync(
            plan.PlanId,
            plan.PlanSha256,
            claimId,
            CancellationToken.None).ConfigureAwait(false);
        await _batchStore.SetStateAsync(
            plan.PlanId,
            plan.PlanSha256,
            SnapshotRestoreBatchState.Available,
            SnapshotMutationStatus.None,
            reasonCode,
            CancellationToken.None).ConfigureAwait(false);
    }

    private static bool HasCompleteVerifiedEvidence(SnapshotRestoreBatchDocument batch)
    {
        if (batch.Steps.Count == 0 || batch.Steps.Any(step => !step.Supported))
        {
            return false;
        }

        var latest = LatestReceipts(batch);
        foreach (var step in batch.Steps)
        {
            if (!latest.TryGetValue(step.StepId, out var receipt)
                || receipt.State is not SnapshotRestoreStepState.NoChange
                    and not SnapshotRestoreStepState.RestoredVerified
                || receipt.State == SnapshotRestoreStepState.RestoredVerified
                    && (!receipt.RecoveryId.HasValue
                        || receipt.ReceiptId == Guid.Empty
                        || receipt.MutationStatus != SnapshotMutationStatus.Known))
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasVerifiedZeroWriteEvidence(SnapshotRestoreBatchDocument batch)
    {
        if (batch.MutationStatus != SnapshotMutationStatus.None
            || batch.Receipts.Any(receipt => receipt.MutationStatus != SnapshotMutationStatus.None))
        {
            return false;
        }

        var latest = LatestReceipts(batch);
        return latest.Count > 0 && latest.Values.All(receipt => receipt.State is
            SnapshotRestoreStepState.NoChange or
            SnapshotRestoreStepState.Unsupported or
            SnapshotRestoreStepState.Failed or
            SnapshotRestoreStepState.Cancelled);
    }

    private static IReadOnlyDictionary<string, SnapshotRestoreStepReceipt> LatestReceipts(
        SnapshotRestoreBatchDocument batch) => batch.Receipts
        .GroupBy(receipt => receipt.StepId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Sequence).Last(), StringComparer.Ordinal);

    private static SnapshotRestoreRecoveryReport Report(
        SnapshotRestoreBatchDocument batch,
        string reasonCode,
        string message)
    {
        var typedSteps = batch.Steps
            .Where(step => step.Supported && step.ResourceKind == SnapshotRestoreResourceKind.TypedCapability)
            .ToArray();
        var typedIds = typedSteps.Select(step => step.StepId).ToHashSet(StringComparer.Ordinal);
        var latest = LatestReceipts(batch);
        var recoverable = batch.Receipts
            .Where(receipt => typedIds.Contains(receipt.StepId) && receipt.RecoveryId.HasValue)
            .Select(receipt => receipt.StepId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var restartReadBackUnknown = batch.State == SnapshotRestoreBatchState.InterruptedManual
            && batch.MutationStatus == SnapshotMutationStatus.Unknown;
        var unknownStepIds = latest.Values
            .Where(receipt => typedIds.Contains(receipt.StepId)
                && (receipt.MutationStatus == SnapshotMutationStatus.Unknown
                    || receipt.State is SnapshotRestoreStepState.Executing
                        or SnapshotRestoreStepState.Unknown
                        or SnapshotRestoreStepState.Compensating))
            .Select(receipt => receipt.StepId)
            .ToHashSet(StringComparer.Ordinal);
        var manualStepIds = latest.Values
            .Where(receipt => typedIds.Contains(receipt.StepId)
                && (receipt.State is SnapshotRestoreStepState.Executing
                        or SnapshotRestoreStepState.Unknown
                        or SnapshotRestoreStepState.Compensating
                        or SnapshotRestoreStepState.CompensationFailed
                    || receipt.MutationStatus != SnapshotMutationStatus.None
                        && receipt.State is SnapshotRestoreStepState.Failed or SnapshotRestoreStepState.Cancelled))
            .Select(receipt => receipt.StepId)
            .ToHashSet(StringComparer.Ordinal);
        if (restartReadBackUnknown)
        {
            unknownStepIds.UnionWith(typedIds);
            manualStepIds.UnionWith(typedIds);
        }
        var unknown = unknownStepIds.Count;
        var manual = manualStepIds.Count;
        var unclassified = typedSteps.Count(step => !latest.ContainsKey(step.StepId));

        return new SnapshotRestoreRecoveryReport(
            batch.PlanId,
            RecoveryStatus(batch.State),
            batch.State,
            batch.ClaimState,
            batch.MutationStatus,
            typedSteps.Length,
            recoverable,
            unknown,
            manual,
            unclassified,
            reasonCode,
            message);
    }

    private static SnapshotRestoreRecoveryStatus RecoveryStatus(SnapshotRestoreBatchState state) => state switch
    {
        SnapshotRestoreBatchState.Available => SnapshotRestoreRecoveryStatus.Available,
        SnapshotRestoreBatchState.RestoredVerified => SnapshotRestoreRecoveryStatus.RestoredVerified,
        SnapshotRestoreBatchState.CompensatedVerified => SnapshotRestoreRecoveryStatus.CompensatedVerified,
        SnapshotRestoreBatchState.ManualResolved => SnapshotRestoreRecoveryStatus.ManualResolved,
        _ => SnapshotRestoreRecoveryStatus.ManualInterventionRequired
    };

    private static SnapshotRestoreRecoveryReport StoreFailure(
        Guid planId,
        string reasonCode,
        Exception? exception) => new(
        planId,
        SnapshotRestoreRecoveryStatus.StoreFailure,
        null,
        null,
        SnapshotMutationStatus.Unknown,
        null,
        null,
        null,
        null,
        null,
        reasonCode,
        exception is null
            ? "The durable snapshot batch is missing; resource counts are unknown."
            : $"The durable snapshot batch cannot be trusted: {exception.GetType().Name}. Resource counts are unknown.");

    private static SnapshotRestoreExecutionVerdict EvaluateCompatibilityResult<T>(T result) => result switch
    {
        SnapshotRestoreResult snapshot => new(
            snapshot.Success
                && snapshot.FailedCount == 0
                && snapshot.Warnings.Count == 0
                && snapshot.UnsupportedCount == 0,
            !snapshot.Success
                && snapshot.MutationStatus == SnapshotMutationStatus.None
                && snapshot.CompensationStatus == SnapshotCompensationStatus.NotRequired,
            !snapshot.Success
                && snapshot.MutationStatus == SnapshotMutationStatus.Known
                && snapshot.CompensationStatus == SnapshotCompensationStatus.VerifiedBeforeState,
            snapshot.MutationStatus,
            snapshot.Success
                ? "snapshot.restored-verified"
                : snapshot.CompensationStatus == SnapshotCompensationStatus.VerifiedBeforeState
                    ? "snapshot.failed-no-change"
                    : snapshot.OutcomeReasonCode),
        RegistryBackupRestoreResult registry => new(
            false,
            registry.MutationStatus == SnapshotMutationStatus.None,
            false,
            registry.MutationStatus,
            "snapshot.deep-registry-unsupported"),
        _ => new(false, false, false, SnapshotMutationStatus.Unknown, "snapshot.unsupported-result-type")
    };

    private static void ApplyBatchState<T>(
        T result,
        SnapshotRestoreBatchState batchState,
        SnapshotMutationStatus mutationStatus)
    {
        if (result is SnapshotRestoreResult snapshot)
        {
            snapshot.BatchState = batchState;
            snapshot.MutationStatus = mutationStatus;
        }
        else if (result is RegistryBackupRestoreResult registry)
        {
            registry.BatchState = batchState;
            registry.MutationStatus = mutationStatus;
        }
    }

    private static void InvalidateVerifiedResult<T>(T result)
    {
        if (result is SnapshotRestoreResult snapshot)
        {
            snapshot.Success = false;
            snapshot.OutcomeReasonCode = "snapshot.completion-evidence-incomplete";
            snapshot.Message = "Durable per-resource receipts are incomplete; the batch requires manual review.";
            snapshot.Errors.Add("snapshot.completion-evidence-incomplete");
        }
        else if (result is RegistryBackupRestoreResult registry)
        {
            registry.Success = false;
        }
    }

    private static bool ValidatePlan(
        SnapshotRestorePlan plan,
        SnapshotRestoreKind expectedKind,
        DateTimeOffset now,
        out string error)
    {
        error = string.Empty;
        try
        {
            ValidateSnapshotId(plan.SnapshotId);
            EnsureSha256(plan.SnapshotIdentitySha256, nameof(plan.SnapshotIdentitySha256));
            EnsureSha256(plan.PlanSha256, nameof(plan.PlanSha256));
            ValidateSteps(plan.Steps);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (plan.SchemaVersion != CurrentSchemaVersion
            || plan.PlanId == Guid.Empty
            || plan.Kind != expectedKind
            || plan.CreatedAtUtc > now
            || now > plan.ExpiresAtUtc
            || plan.ExpiresAtUtc - plan.CreatedAtUtc != PlanLifetime)
        {
            error = "恢复计划版本、类型、身份或有效期无效。";
            return false;
        }

        var expected = ComputePlanHash(
            plan.SchemaVersion,
            plan.PlanId,
            plan.SnapshotId,
            plan.Kind,
            plan.SnapshotIdentitySha256,
            plan.Steps,
            plan.CreatedAtUtc,
            plan.ExpiresAtUtc);
        if (!FixedEquals(expected, plan.PlanSha256))
        {
            error = "恢复计划哈希与内容不匹配。";
            return false;
        }
        return true;
    }

    private static string ComputePlanHash(
        int schema,
        Guid id,
        string snapshotId,
        SnapshotRestoreKind kind,
        string identity,
        IReadOnlyList<SnapshotRestoreStepPlan> steps,
        DateTimeOffset created,
        DateTimeOffset expires)
    {
        var input = new StringBuilder()
            .Append(schema).Append('\u001F')
            .Append(id.ToString("N")).Append('\u001F')
            .Append(snapshotId).Append('\u001F')
            .Append(kind).Append('\u001F')
            .Append(identity).Append('\u001F')
            .Append(created.ToUnixTimeMilliseconds()).Append('\u001F')
            .Append(expires.ToUnixTimeMilliseconds());
        foreach (var step in steps)
        {
            input.Append('\u001E')
                .Append(step.StepId).Append('\u001F')
                .Append(step.ResourceKind).Append('\u001F')
                .Append(step.CapabilityId ?? string.Empty).Append('\u001F')
                .Append(step.TargetSha256).Append('\u001F')
                .Append(step.Supported).Append('\u001F')
                .Append(step.ReasonCode);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }

    private static void ValidatePreparation(SnapshotRestorePreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        EnsureSha256(preparation.IdentitySha256, nameof(preparation.IdentitySha256));
        ValidateSteps(preparation.Steps);
    }

    private static void ValidateSteps(IReadOnlyList<SnapshotRestoreStepPlan> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != steps.Count)
        {
            throw new InvalidDataException("Snapshot restore step identifiers must be unique.");
        }

        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepId)
                || string.IsNullOrWhiteSpace(step.ReasonCode)
                || !Enum.IsDefined(step.ResourceKind)
                || step.Supported != (step.ResourceKind == SnapshotRestoreResourceKind.TypedCapability)
                || (step.Supported && string.IsNullOrWhiteSpace(step.CapabilityId)))
            {
                throw new InvalidDataException("Snapshot restore step is incomplete or inconsistent.");
            }
            EnsureSha256(step.TargetSha256, nameof(step.TargetSha256));
        }
    }

    private static bool StepsEqual(
        IReadOnlyList<SnapshotRestoreStepPlan> left,
        IReadOnlyList<SnapshotRestoreStepPlan> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    private static void EnsureSha256(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException($"{name} must be a SHA-256 hexadecimal value.");
        }
    }

    private static bool FixedEquals(string left, string right)
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
}
