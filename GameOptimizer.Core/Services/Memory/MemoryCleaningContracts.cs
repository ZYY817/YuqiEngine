using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Memory;

public enum MemoryCleaningCapabilityKind
{
    WorkingSetTrim,
    ProcessTermination,
    StandbyListPurge
}

public enum MemoryCleaningResultKind
{
    Ready,
    ReadOnly,
    NoChange,
    SucceededVerified,
    RestoredVerified,
    FailClosed,
    Denied,
    Cancelled,
    Conflict,
    Failed,
    RecoveryRequired
}

/// <summary>
/// Stable process identity required before any future process-scoped memory change.
/// A PID alone is never a sufficient target identity.
/// </summary>
public sealed record MemoryProcessIdentity(
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

public sealed record MemoryCleaningRequest(
    MemoryCleaningCapabilityKind Capability,
    MemoryProcessIdentity? ProcessIdentity = null,
    string? MachineIdentity = null)
{
    public TargetScope Scope => Capability is MemoryCleaningCapabilityKind.WorkingSetTrim or MemoryCleaningCapabilityKind.ProcessTermination
        ? new TargetScope(
            TargetScopeKind.Process,
            ProcessIdentity?.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
        : new TargetScope(TargetScopeKind.Machine, MachineIdentity ?? string.Empty);

    public bool IsValid => Capability switch
    {
        MemoryCleaningCapabilityKind.WorkingSetTrim => ProcessIdentity?.IsUsable == true,
        MemoryCleaningCapabilityKind.ProcessTermination => ProcessIdentity?.IsUsable == true,
        MemoryCleaningCapabilityKind.StandbyListPurge => !string.IsNullOrWhiteSpace(MachineIdentity),
        _ => false
    };

    public static MemoryCleaningRequest ForWorkingSet(MemoryProcessIdentity identity) =>
        new(MemoryCleaningCapabilityKind.WorkingSetTrim, identity, null);

    public static MemoryCleaningRequest ForProcessTermination(MemoryProcessIdentity identity) =>
        new(MemoryCleaningCapabilityKind.ProcessTermination, identity, null);

    public static MemoryCleaningRequest ForStandbyList(string machineIdentity) =>
        new(MemoryCleaningCapabilityKind.StandbyListPurge, null, machineIdentity);
}

public sealed record MemoryCleaningTargetPayload(
    MemoryCleaningCapabilityKind Capability,
    MemoryProcessIdentity? ProcessIdentity,
    string? MachineIdentity,
    string RequestedOperation = "trim");

/// <summary>
/// The platform may expose read-only state, but a mutable state is not a recovery
/// material. The operation marker is only valid for a fake/reversible platform and
/// must never be used to manufacture a RecoveryId.
/// </summary>
public sealed record MemoryCleaningObservedState(
    MemoryCleaningCapabilityKind Capability,
    TargetScope Scope,
    MemoryStatus Memory,
    MemoryProcessIdentity? ProcessIdentity,
    long WorkingSetBytes,
    string OperationState,
    bool RecoveryCapable);

public sealed record MemoryCleaningResult(
    MemoryCleaningResultKind Kind,
    string ReasonCode,
    string Message,
    PreparedChangePlan? Plan = null,
    OperationReceipt? Receipt = null,
    StateObservation? Observation = null)
{
    public bool IsTerminalSuccess => Kind is MemoryCleaningResultKind.SucceededVerified
        or MemoryCleaningResultKind.RestoredVerified
        or MemoryCleaningResultKind.NoChange;
}

/// <summary>
/// Read-only capture result used by the UI before it can request a process-scoped
/// mutation. A process id alone is never promoted to a mutable target.
/// </summary>
public sealed record MemoryProcessTargetResult(
    bool Succeeded,
    string ReasonCode,
    string Message,
    MemoryCleaningRequest? Request = null);

public interface IMemoryCleaningOwner
{
    Task<MemoryProcessTargetResult> CaptureProcessTargetAsync(
        MemoryCleaningCapabilityKind capability,
        int processId,
        CancellationToken cancellationToken = default);

    Task<MemoryProcessTargetResult> CaptureWorkingSetTargetAsync(
        int processId,
        CancellationToken cancellationToken = default);

    Task<MemoryCleaningResult> ReadStatusAsync(
        MemoryCleaningRequest request,
        CancellationToken cancellationToken = default);

    Task<MemoryCleaningResult> PrepareAsync(
        MemoryCleaningRequest request,
        CancellationToken cancellationToken = default);

    Task<MemoryCleaningResult> RunInteractiveAsync(
        MemoryCleaningRequest request,
        CancellationToken cancellationToken = default);

    Task<MemoryCleaningResult> ExecuteAsync(
        PreparedChangePlan plan,
        ConfirmationToken confirmation,
        CancellationToken cancellationToken = default);

    Task<MemoryCleaningResult> RestoreAsync(
        RestoreRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The only platform boundary permitted for a future Memory mutation. The current
/// Windows implementation intentionally implements read/probe and fail-closed
/// mutation methods because neither operation has a proven inverse.
/// </summary>
public interface IMemoryCleaningPlatform
{
    Task<CapabilityAssessment> ProbeAsync(
        MemoryCleaningCapabilityKind capability,
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken);

    Task<StateObservation> ReadAsync(
        MemoryCleaningCapabilityKind capability,
        TargetScope scope,
        CancellationToken cancellationToken);

    Task<PlatformExecutionResult> ApplyAsync(
        MemoryCleaningCapabilityKind capability,
        PreparedCapabilityChange change,
        CancellationToken cancellationToken);

    Task<PlatformExecutionResult> RestoreAsync(
        MemoryCleaningCapabilityKind capability,
        RecoveryMaterial material,
        CancellationToken cancellationToken);
}

public static class MemoryCleaningCapabilityIds
{
    public const string WorkingSetTrim = "memory.working-set.trim";
    public const string ProcessTermination = "memory.process.terminate";
    public const string StandbyListPurge = "memory.standby-list.purge";

    public static string For(MemoryCleaningCapabilityKind capability) => capability switch
    {
        MemoryCleaningCapabilityKind.WorkingSetTrim => WorkingSetTrim,
        MemoryCleaningCapabilityKind.ProcessTermination => ProcessTermination,
        MemoryCleaningCapabilityKind.StandbyListPurge => StandbyListPurge,
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };
}

public static class MemoryCleaningPayloadCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SerializeTarget(MemoryCleaningRequest request) =>
        JsonSerializer.Serialize(
            new MemoryCleaningTargetPayload(
                request.Capability,
                request.ProcessIdentity,
                request.MachineIdentity),
            Options);

    public static MemoryCleaningTargetPayload DeserializeTarget(string payload) =>
        JsonSerializer.Deserialize<MemoryCleaningTargetPayload>(payload, Options)
        ?? throw new InvalidOperationException("memory.target-invalid");

    public static string SerializeObserved(MemoryCleaningObservedState state) =>
        JsonSerializer.Serialize(state, Options);

    public static MemoryCleaningObservedState DeserializeObserved(string payload) =>
        JsonSerializer.Deserialize<MemoryCleaningObservedState>(payload, Options)
        ?? throw new InvalidOperationException("memory.observation-invalid");

    public static string Hash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public static string WorkingSetTrimFingerprint(MemoryProcessIdentity identity) =>
        Hash(string.Join(
            '|',
            "memory-working-set-trim-v1",
            identity.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.CreationTimeUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.ExecutableName,
            identity.ExecutableHash ?? string.Empty,
            identity.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.UserSecurityIdentifier,
            identity.Architecture,
            identity.TopologyFingerprint));

    public static string ProcessTerminationFingerprint(MemoryProcessIdentity identity) =>
        Hash(string.Join(
            '|',
            "memory-process-termination-v1",
            identity.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.CreationTimeUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.ExecutableName,
            identity.ExecutableHash ?? string.Empty,
            identity.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.UserSecurityIdentifier,
            identity.Architecture,
            identity.TopologyFingerprint));

    public static string ProcessTerminationObservedFingerprint(MemoryProcessIdentity identity) =>
        Hash(string.Join(
            '|',
            "memory-process-termination-observed-v1",
            identity.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.CreationTimeUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.ExecutableName,
            identity.ExecutableHash ?? string.Empty,
            identity.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.UserSecurityIdentifier,
            identity.Architecture,
            identity.TopologyFingerprint));
}
