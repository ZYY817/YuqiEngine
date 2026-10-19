using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Contracts.Changes;

public interface IChangeCapability
{
    CapabilityDescriptor Descriptor { get; }

    Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken);
    Task<CapabilityAssessment> ProbeAsync(
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken) =>
        ProbeAsync(scope, cancellationToken);
    Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken);
    Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken);
    Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken);
    Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken);
}

/// <summary>
/// A read-only, centralized compatibility gate that runs before a capability's own probe.
/// Returning <see langword="null"/> delegates the decision to the capability-specific probe.
/// </summary>
public interface IChangeCompatibilityPolicy
{
    Task<CapabilityAssessment?> ProbeAsync(
        CapabilityDescriptor descriptor,
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken);
}

public interface IRecoveryRepository
{
    Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken);
    Task<RecoveryMaterial?> LoadAsync(Guid recoveryId, CancellationToken cancellationToken);
}

public enum RecoveryClaimStatus
{
    Claimed,
    Missing,
    IntegrityMismatch,
    AlreadyRestoring,
    AlreadyRestored
}

public sealed record RecoveryClaimResult(RecoveryClaimStatus Status, Guid? ClaimId);

public interface IRecoveryClaimStore
{
    Task RegisterAvailableAsync(
        Guid recoveryId,
        string integrityHash,
        CancellationToken cancellationToken);

    Task<RecoveryClaimResult> TryBeginRestoreAsync(
        Guid recoveryId,
        string integrityHash,
        CancellationToken cancellationToken);

    Task MarkRestoredAsync(
        Guid recoveryId,
        string integrityHash,
        Guid claimId,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        Guid recoveryId,
        string integrityHash,
        Guid claimId,
        CancellationToken cancellationToken);
}

public interface IChangeJournal
{
    Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken);
}

public interface IChangeAuditStore
{
    Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken);
    Task<OperationReceipt?> LoadAsync(Guid receiptId, CancellationToken cancellationToken);
}

public enum PlanClaimStatus
{
    Claimed,
    Missing,
    AlreadyClaimed,
    HashMismatch
}

public interface IChangePlanStore
{
    Task RegisterAsync(PreparedChangePlan plan, CancellationToken cancellationToken);
    Task<PlanClaimStatus> TryClaimAsync(Guid planId, string planHash, CancellationToken cancellationToken);
}

public interface IChangeResourceLease : IAsyncDisposable
{
}

public interface IChangeResourceLockProvider
{
    Task<IChangeResourceLease?> TryAcquireAsync(
        string resourceKey,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface IChangeConfirmationPrompt
{
    Task<bool> ConfirmAsync(ChangeConfirmationPrompt prompt, CancellationToken cancellationToken);
}

public interface IChangeConfirmationAuthority
{
    Task<ConfirmationToken?> RequestAsync(
        PreparedChangePlan plan,
        string title,
        string summary,
        CancellationToken cancellationToken);

    bool TryConsume(PreparedChangePlan plan, ConfirmationToken token);
}
