using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.NetworkReset;

public enum NetworkResetProfile
{
    DnsCache,
    Winsock,
    TcpIp,
    Full
}

public enum NetworkResetCommand
{
    ShowDnsCache,
    ShowWinsockCatalog,
    ShowIpv4Configuration,
    ShowIpv6Configuration,
    ShowWinHttpProxy,
    FlushDnsCache,
    ResetWinsock,
    ResetIpv4,
    ResetIpv6
}

public enum NetworkResetOutcome
{
    InProgress,
    SucceededVerified,
    AppliedRequiresRestartManualRecovery,
    Blocked,
    Denied,
    Conflict,
    CancelledBeforeWrite,
    RecoveryRequired,
    FailedNoChange,
    FailedPartial
}

public enum NetworkRecoveryDecision
{
    NotRequired,
    ManualRecoveryOnly,
    RecoveryRequired
}

public enum NetworkResetReceiptState
{
    Executing,
    Completed,
    ManualInterventionRequired,
    Unknown
}

public enum NetworkResetReceiptPhase
{
    Prepared,
    BaselineAuthorized,
    Claimed,
    LeaseAcquired,
    PreWriteVerified,
    StepExecuting,
    StepCompleted,
    Completed
}

public sealed record NetworkResetCommandResult(
    NetworkResetCommand Command,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool ProcessStarted,
    bool CancellationObservedAfterStart = false);

public sealed record NetworkResetPlatformAssessment(
    bool IsSupported,
    bool IsAdministrator,
    string ReasonCode,
    string Message);

/// <summary>
/// Read-only assessment of conditions that make a network-stack reset unsafe to enter.
/// DNS cache flush intentionally does not consume this assessment because it does not change
/// adapters, routes, Winsock providers, or address configuration.
/// </summary>
public sealed record NetworkResetSafetyFinding(string ReasonCode, string Message);

public sealed record NetworkResetSafetyAssessment(
    bool IsExecutionAllowed,
    string ReasonCode,
    string Message,
    IReadOnlyList<NetworkResetSafetyFinding> Findings);

public sealed record NetworkResetReadOnlyStatus(
    NetworkResetProfile Profile,
    bool IsSupported,
    bool IsAdministrator,
    bool SnapshotAvailable,
    string ReasonCode,
    string Message,
    NetworkResetSnapshot? Snapshot,
    DateTimeOffset ObservedAtUtc,
    NetworkResetSafetyAssessment? SafetyAssessment = null);

public sealed record NetworkResetSnapshotEntry(
    NetworkResetCommand Command,
    int ExitCode,
    string OutputHash);

public sealed record NetworkResetSnapshot(
    string IdentityHash,
    IReadOnlyList<NetworkResetSnapshotEntry> Entries,
    bool IsMachineRestorable,
    string RecoveryBoundary);

public sealed record NetworkResetPlan(
    PreparedChangePlan ChangePlan,
    NetworkResetProfile Profile,
    NetworkResetSnapshot Snapshot,
    IReadOnlyList<NetworkResetCommand> Steps,
    IReadOnlyList<string> Risks,
    bool RebootRequired,
    NetworkRecoveryDecision RecoveryDecision);

public sealed record NetworkResetStepReceipt(
    int StepNumber,
    NetworkResetCommand Command,
    int ExitCode,
    bool MutationMayHaveOccurred,
    bool ReadBackCompleted,
    bool EffectVerified,
    string ReasonCode,
    string Message,
    string OutputHash,
    string ErrorHash,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record NetworkResetReceipt(
    Guid ReceiptId,
    Guid PlanId,
    string PlanHash,
    NetworkResetProfile Profile,
    NetworkResetReceiptState State,
    NetworkResetReceiptPhase Phase,
    NetworkResetOutcome Outcome,
    string ReasonCode,
    string Message,
    bool MutationOccurred,
    bool ManualInterventionRequired,
    bool RebootRequired,
    NetworkRecoveryDecision RecoveryDecision,
    string RecoveryInstructions,
    IReadOnlyList<NetworkResetStepReceipt> Steps,
    NetworkResetCommand? CurrentStep,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record NetworkResetBaselineDecision(
    bool Authorized,
    string ReasonCode,
    string Message,
    string? EvidenceHash);

public interface INetworkResetPlatform
{
    Task<NetworkResetPlatformAssessment> ProbeAsync(CancellationToken cancellationToken);
    Task<NetworkResetSafetyAssessment> AssessResetSafetyAsync(
        NetworkResetProfile profile,
        CancellationToken cancellationToken);
    Task<NetworkResetCommandResult> RunReadOnlyAsync(NetworkResetCommand command, TimeSpan timeout, CancellationToken cancellationToken);
    Task<NetworkResetCommandResult> RunMutationAsync(NetworkResetCommand command, TimeSpan timeout, CancellationToken cancellationToken);
    Task<bool> VerifyAsync(NetworkResetCommand command, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface INetworkResetBaselineGate
{
    Task<NetworkResetBaselineDecision> AuthorizeAsync(NetworkResetPlan plan, CancellationToken cancellationToken);
}

public interface INetworkResetPlanStore
{
    Task RegisterAsync(NetworkResetPlan plan, CancellationToken cancellationToken);
    Task<NetworkResetPlan?> LoadAsync(Guid planId, CancellationToken cancellationToken);
    Task<PlanClaimStatus> TryClaimAsync(Guid planId, string planHash, CancellationToken cancellationToken);
}

public interface INetworkResetReceiptStore
{
    Task SaveAsync(NetworkResetReceipt receipt, CancellationToken cancellationToken);
    Task<IReadOnlyList<NetworkResetReceipt>> LoadPendingAsync(CancellationToken cancellationToken);
}

public sealed class NetworkResetReceiptPersistenceException : IOException
{
    public NetworkResetReceiptPersistenceException(
        Guid receiptId,
        Guid planId,
        string evidenceIdentity,
        Exception innerException)
        : base($"网络重置收据持久化失败：{evidenceIdentity}", innerException)
    {
        ReceiptId = receiptId;
        PlanId = planId;
        EvidenceIdentity = evidenceIdentity;
    }

    public Guid ReceiptId { get; }
    public Guid PlanId { get; }
    public string EvidenceIdentity { get; }
}

public interface INetworkResetOwner
{
    Task<NetworkResetReadOnlyStatus> ReadStatusAsync(NetworkResetProfile profile, CancellationToken cancellationToken = default);
    Task<NetworkResetPlan> PrepareAsync(NetworkResetProfile profile, CancellationToken cancellationToken = default);
    Task<NetworkResetReceipt> ExecuteAsync(NetworkResetPlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken = default);
    Task<NetworkResetReceipt> RunInteractiveAsync(NetworkResetProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetworkResetReceipt>> GetPendingRecoveryReceiptsAsync(CancellationToken cancellationToken = default);
}
