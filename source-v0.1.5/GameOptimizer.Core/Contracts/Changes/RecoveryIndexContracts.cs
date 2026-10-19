namespace GameOptimizer.Core.Contracts.Changes;

public enum RecoveryIndexStatus
{
    Recoverable,
    Restoring,
    ManualRecovery
}

public sealed record RecoveryIndexEntry(
    Guid? ReceiptId,
    Guid RecoveryId,
    string CapabilityId,
    string ResourceKey,
    RecoveryIndexStatus Status,
    string TerminalReasonCode,
    DateTimeOffset UpdatedAtUtc);

public interface IRecoveryIndexQuery
{
    Task<IReadOnlyList<RecoveryIndexEntry>> ListAsync(CancellationToken cancellationToken);
}

public enum RecoveryClaimReadStatus
{
    Available,
    Restoring,
    Restored,
    Missing,
    IntegrityMismatch
}

public sealed record RecoveryClaimStateSnapshot(
    RecoveryClaimReadStatus Status,
    DateTimeOffset? StateUpdatedAtUtc);

public interface IRecoveryClaimStateReader
{
    Task<RecoveryClaimStateSnapshot> ReadStateAsync(
        Guid recoveryId,
        string integrityHash,
        CancellationToken cancellationToken);
}
