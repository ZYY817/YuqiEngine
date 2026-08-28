using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Repair;

public enum OptimizationRevertAvailability
{
    Available,
    Restored,
    RestoringInterrupted,
    Unrecoverable
}

public sealed record OptimizationRevertStatus(
    string OperationId,
    OptimizationRevertAvailability Availability,
    string ReasonCode,
    string Message,
    DateTimeOffset ObservedAtUtc);

public sealed record OptimizationRevertPlan(
    Guid PlanId,
    string OperationId,
    Guid ReceiptId,
    Guid RecoveryId,
    string CapabilityId,
    string DefinitionVersion,
    string ResourceKey,
    TargetScope Scope,
    string BeforeFingerprint,
    string TargetFingerprint,
    string RecoveryIntegrityHash,
    ChangeRisk Risk,
    DateTimeOffset PreparedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string PlanHash);

public sealed record OptimizationRevertPreparation(
    OptimizationRevertStatus Status,
    OptimizationRevertPlan? Plan);

/// <summary>
/// Durable pointer stored next to the legacy optimization snapshot. The recovery material and audit
/// receipt remain the authorities; duplicated fields bind the pointer to the exact prepared owner.
/// Schema 1 (ReceiptId + RecoveryId only) remains readable, but is usable only when all fields can be
/// reconstructed and validated from the durable audit/recovery stores.
/// </summary>
public sealed record OptimizationRecoveryPointer
{
    public int SchemaVersion { get; init; } = 1;
    public string? OperationId { get; init; }
    public Guid ReceiptId { get; init; }
    public Guid? RecoveryId { get; init; }
    public string? CapabilityId { get; init; }
    public string? DefinitionVersion { get; init; }
    public string? ResourceKey { get; init; }
    public TargetScope? Scope { get; init; }
    public string? BeforeFingerprint { get; init; }
    public string? TargetFingerprint { get; init; }
}

internal sealed record OptimizationRevertDurableStatus
{
    public int SchemaVersion { get; init; } = 1;
    public required string OperationId { get; init; }
    public required OptimizationRevertAvailability Availability { get; init; }
    public required Guid RecoveryId { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
