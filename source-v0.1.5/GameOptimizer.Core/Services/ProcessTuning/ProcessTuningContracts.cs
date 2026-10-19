using System.Diagnostics;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.ProcessTuning;

/// <summary>
/// Runtime process tuning is deliberately separate from persistent IFEO configuration.
/// A target is valid only while this exact process identity is still observable.
/// </summary>
public sealed record ProcessIdentity(
    int ProcessId,
    long CreationTimeUtcTicks,
    string ExecutableName,
    string? ExecutableHash,
    bool ExecutableHashAvailable,
    int SessionId,
    string UserSecurityIdentifier,
    string Architecture,
    string TopologyFingerprint)
{
    public bool IsUsable => ProcessId > 4
        && CreationTimeUtcTicks > 0
        && !string.IsNullOrWhiteSpace(ExecutableName)
        && HasConsistentExecutableHash()
        && SessionId >= 0
        && !string.IsNullOrWhiteSpace(UserSecurityIdentifier)
        && !string.IsNullOrWhiteSpace(Architecture)
        && !string.IsNullOrWhiteSpace(TopologyFingerprint);

    private bool HasConsistentExecutableHash() => ExecutableHashAvailable
        ? ExecutableHash is { Length: 64 } && ExecutableHash.All(char.IsAsciiHexDigit)
        : ExecutableHash is null;
}

public enum ProcessTuningOperation
{
    Affinity,
    Priority,
    IdealProcessor
}

public sealed record ProcessTuningState(
    ProcessIdentity Identity,
    long AffinityMask,
    ProcessPriorityClass Priority,
    int? IdealProcessor,
    bool IsIdealProcessorReadbackReliable);

public sealed record ProcessTuningTarget(
    int ProcessId,
    ProcessTuningOperation Operation,
    long? AffinityMask = null,
    ProcessPriorityClass? Priority = null,
    int? IdealProcessor = null);

public sealed record ProcessTuningRequest(ProcessTuningTarget Target)
{
    public static ProcessTuningRequest Affinity(int processId, long affinityMask) =>
        new(new ProcessTuningTarget(processId, ProcessTuningOperation.Affinity, AffinityMask: affinityMask));

    public static ProcessTuningRequest Priority(int processId, ProcessPriorityClass priority) =>
        new(new ProcessTuningTarget(processId, ProcessTuningOperation.Priority, Priority: priority));

    public static ProcessTuningRequest IdealProcessor(int processId, int idealProcessor) =>
        new(new ProcessTuningTarget(processId, ProcessTuningOperation.IdealProcessor, IdealProcessor: idealProcessor));
}

public sealed record ProcessTuningPlatformReadResult(
    ProcessTuningState? State,
    string ReasonCode,
    string Message)
{
    public bool Succeeded => State is not null;

    public static ProcessTuningPlatformReadResult Success(ProcessTuningState state) =>
        new(state, "process.read.ok", "进程当前状态已读取。");

    public static ProcessTuningPlatformReadResult Failure(string reasonCode, string message) =>
        new(null, reasonCode, message);
}

/// <summary>
/// The platform adapter is the only component allowed to touch Process properties.
/// It must re-read identity before every mutation and return a fail-closed result.
/// </summary>
public interface IProcessTuningPlatform
{
    Task<CapabilityAssessment> ProbeAsync(
        int processId,
        ProcessTuningOperation operation,
        ChangeIntent intent,
        CancellationToken cancellationToken);

    Task<ProcessTuningPlatformReadResult> ReadAsync(
        int processId,
        CancellationToken cancellationToken);

    Task<PlatformExecutionResult> ApplyAsync(
        ProcessTuningOperation operation,
        ProcessTuningState expectedCurrent,
        ProcessTuningState desired,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores only after the adapter independently re-reads the still-live process
    /// and proves that its complete current state hashes to the durable target fingerprint.
    /// </summary>
    Task<PlatformExecutionResult> RestoreAsync(
        ProcessTuningOperation operation,
        string expectedTargetFingerprint,
        ProcessTuningState original,
        CancellationToken cancellationToken);
}

public enum ProcessTuningResultKind
{
    Ready,
    NoChange,
    SucceededVerified,
    RestoredVerified,
    NotApplicable,
    Blocked,
    Conflict,
    Denied,
    Cancelled,
    RecoveryRequired,
    Failed
}

public sealed record ProcessTuningResult(
    ProcessTuningResultKind Kind,
    string ReasonCode,
    string Message,
    PreparedChangePlan? Plan = null,
    OperationReceipt? Receipt = null)
{
    public bool IsTerminalSuccess => Kind is ProcessTuningResultKind.SucceededVerified
        or ProcessTuningResultKind.RestoredVerified
        or ProcessTuningResultKind.NoChange;
}

public interface IProcessTuningOwner
{
    Task<ProcessTuningResult> PrepareAsync(
        ProcessTuningRequest request,
        CancellationToken cancellationToken = default);

    Task<ProcessTuningResult> RunInteractiveAsync(
        ProcessTuningRequest request,
        CancellationToken cancellationToken = default);

    Task<ProcessTuningResult> ExecuteAsync(
        PreparedChangePlan plan,
        ConfirmationToken confirmation,
        CancellationToken cancellationToken = default);

    Task<ProcessTuningResult> RestoreAsync(
        RestoreRequest request,
        CancellationToken cancellationToken = default);
}
