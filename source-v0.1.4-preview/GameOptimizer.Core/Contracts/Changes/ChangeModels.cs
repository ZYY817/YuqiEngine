using System;
using System.Collections.Generic;

namespace GameOptimizer.Core.Contracts.Changes;

public enum ChangeRisk
{
    ReadOnly,
    LowReversible,
    MediumRebootOrExperience,
    HighSystem,
    Experimental
}

public enum TargetScopeKind
{
    CurrentUser,
    SpecificUser,
    AllUsers,
    Machine,
    Device,
    Process
}

public enum CapabilityApplicability
{
    Applicable,
    NotApplicable,
    Unsupported,
    ManagedByPolicy,
    Conflict,
    Unknown
}

public enum ChangeIntent
{
    Prepare,
    Execute,
    Restore
}

public enum ChangePreparationStatus
{
    Ready,
    NoChange,
    Blocked
}

public enum ChangeOutcome
{
    NoChange,
    SucceededVerified,
    NotApplicable,
    Blocked,
    Conflict,
    Denied,
    CancelledBeforeWrite,
    CancelledAfterPartialWrite,
    FailedNoChange,
    FailedPartial,
    AppliedButUnverified,
    RestoredVerified,
    RestoreFailed,
    RecoveryRequired
}

public sealed record TargetScope(TargetScopeKind Kind, string Identity)
{
    public static TargetScope CurrentUser(string securityIdentifier) =>
        new(TargetScopeKind.CurrentUser, securityIdentifier);

    public static TargetScope Machine(string machineName) =>
        new(TargetScopeKind.Machine, machineName);
}

public sealed record CapabilityDescriptor(
    string CapabilityId,
    string DefinitionVersion,
    ChangeRisk Risk,
    IReadOnlyList<TargetScopeKind> SupportedScopes,
    bool RequiresAdministrator,
    bool RequiresExplicitConfirmation,
    bool RequiresRecoveryMaterial,
    bool RequiresRestart,
    string ResourceKey,
    string ExecutionPolicyVersion = "bounded-platform.v1",
    // Why: ResourceKey 同时承担"能力身份+恢复材料匹配"职责，不能按锁粒度随意改动；
    // LockKey 单独表达并发互斥粒度，缺省回落 ResourceKey 保持原语义。
    string? LockKey = null);

public sealed record ChangeExecutionPolicy(
    string Version,
    TimeSpan PlatformTimeout,
    TimeSpan VerificationTimeout,
    TimeSpan PersistenceTimeout)
{
    public string Fingerprint =>
        $"{Version}:{PlatformTimeout.Ticks}:{VerificationTimeout.Ticks}:{PersistenceTimeout.Ticks}";

    public static ChangeExecutionPolicy Default { get; } = new(
        "bounded-platform.v1",
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10));
}

public sealed record CapabilityAssessment(
    CapabilityApplicability Applicability,
    bool HasRequiredPermission,
    string ReasonCode,
    string Details);

public sealed record StateObservation(
    TargetScope Scope,
    string Payload,
    string Fingerprint,
    DateTimeOffset ObservedAtUtc,
    string Source,
    string Reliability);

public sealed record ChangeRequest(
    string CapabilityId,
    TargetScope Scope,
    string TargetPayload);

public sealed record PreparedCapabilityChange(
    string CapabilityId,
    string DefinitionVersion,
    TargetScope Scope,
    string BeforePayload,
    string BeforeFingerprint,
    string TargetPayload,
    string TargetFingerprint,
    string ResourceKey,
    string? LockKey = null);

public sealed record PreparedChangePlan(
    Guid PlanId,
    PreparedCapabilityChange Change,
    string PlanHash,
    DateTimeOffset PreparedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool RequiresAdministrator,
    bool RequiresExplicitConfirmation,
    bool RequiresRecoveryMaterial,
    ChangeRisk Risk,
    string ExecutionPolicyVersion = "bounded-platform.v1",
    string ExecutionPolicyFingerprint = "");

public sealed record PreparationResult(
    ChangePreparationStatus Status,
    PreparedChangePlan? Plan,
    string ReasonCode,
    string Message,
    StateObservation? Current);

public sealed class ConfirmationToken
{
    internal ConfirmationToken(
        Guid tokenId,
        string planHash,
        TargetScope scope,
        DateTimeOffset expiresAtUtc,
        bool explicitlyConfirmed)
    {
        TokenId = tokenId;
        PlanHash = planHash;
        Scope = scope;
        ExpiresAtUtc = expiresAtUtc;
        ExplicitlyConfirmed = explicitlyConfirmed;
    }

    public Guid TokenId { get; }
    public string PlanHash { get; }
    public TargetScope Scope { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public bool ExplicitlyConfirmed { get; }

    public static ConfirmationToken ForPreparedPlan(PreparedChangePlan plan) =>
        plan.RequiresExplicitConfirmation
            ? throw new InvalidOperationException("需要显式确认的计划不能由兼容入口自行签发确认凭据。")
            : new(Guid.Empty, plan.PlanHash, plan.Change.Scope, plan.ExpiresAtUtc, explicitlyConfirmed: false);
}

public sealed record ChangeConfirmationPrompt(
    Guid PlanId,
    string CapabilityId,
    ChangeRisk Risk,
    string Title,
    string Summary,
    DateTimeOffset ExpiresAtUtc);

public sealed record PlatformExecutionResult(
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message);

public sealed record RecoveryMaterial(
    Guid RecoveryId,
    Guid PlanId,
    string CapabilityId,
    string DefinitionVersion,
    TargetScope Scope,
    string BeforePayload,
    string BeforeFingerprint,
    string TargetFingerprint,
    string ResourceKey,
    DateTimeOffset CreatedAtUtc,
    string IntegrityHash,
    Guid ReceiptId = default,
    string? LockKey = null);

public sealed record OperationReceipt(
    Guid ReceiptId,
    Guid PlanId,
    Guid? RecoveryId,
    string CapabilityId,
    TargetScope Scope,
    ChangeOutcome Outcome,
    string BeforeFingerprint,
    string TargetFingerprint,
    string? AfterFingerprint,
    string ReasonCode,
    string Message,
    bool MutationOccurred,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record BatchReceipt(OperationReceipt Operation)
{
    public ChangeOutcome Outcome => Operation.Outcome;
}

public sealed record RestoreRequest(Guid ReceiptId, Guid? RecoveryId = null);

public sealed record JournalEntry(
    Guid PlanId,
    string CapabilityId,
    string Stage,
    DateTimeOffset TimestampUtc,
    string Details);
