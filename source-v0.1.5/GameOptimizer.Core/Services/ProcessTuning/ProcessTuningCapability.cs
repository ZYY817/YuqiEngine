using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.ProcessTuning;

/// <summary>
/// One capability instance owns one runtime property. The generic orchestrator owns
/// confirmation, baseline authorization, leases, durable recovery and receipts.
/// </summary>
public sealed class ProcessTuningCapability : IChangeCapability
{
    private const string DefinitionVersion = "process-tuning.v1";
    private const string ResourceKey = "process-tuning:runtime";
    private readonly IProcessTuningPlatform _platform;
    private readonly ProcessTuningOperation _operation;

    public ProcessTuningCapability(ProcessTuningOperation operation, IProcessTuningPlatform platform)
    {
        _operation = operation;
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        Descriptor = new CapabilityDescriptor(
            CapabilityIdFor(operation),
            DefinitionVersion,
            ChangeRisk.MediumRebootOrExperience,
            [TargetScopeKind.Process],
            RequiresAdministrator: false,
            RequiresExplicitConfirmation: true,
            RequiresRecoveryMaterial: true,
            RequiresRestart: false,
            ResourceKey,
            ExecutionPolicyVersion: "bounded-platform.v1");
    }

    public CapabilityDescriptor Descriptor { get; }

    public static string CapabilityIdFor(ProcessTuningOperation operation) => operation switch
    {
        ProcessTuningOperation.Affinity => "process.tuning.affinity",
        ProcessTuningOperation.Priority => "process.tuning.priority",
        ProcessTuningOperation.IdealProcessor => "process.tuning.ideal-processor",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    public async Task<CapabilityAssessment> ProbeAsync(
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken)
    {
        if (!TryGetProcessId(scope, out var processId))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "process.scope-invalid",
                "进程作用域必须是大于 4 的 PID。" );
        }

        return await _platform.ProbeAsync(processId, _operation, intent, cancellationToken).ConfigureAwait(false);
    }

    public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken) =>
        ProbeAsync(scope, ChangeIntent.Prepare, cancellationToken);

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        if (!TryGetProcessId(scope, out var processId))
        {
            throw new InvalidOperationException("process.scope-invalid");
        }

        var read = await _platform.ReadAsync(processId, cancellationToken).ConfigureAwait(false);
        if (!read.Succeeded || read.State is null)
        {
            throw new InvalidOperationException($"{read.ReasonCode}:{read.Message}");
        }

        if (!read.State.Identity.IsUsable)
        {
            throw new InvalidOperationException("process.identity-incomplete");
        }

        var payload = ProcessTuningStateFingerprint.Serialize(read.State);
        return new StateObservation(
            scope,
            payload,
            ProcessTuningStateFingerprint.Hash(payload),
            DateTimeOffset.UtcNow,
            "process-tuning-platform",
            "process-identity-and-readback");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(current);
        var before = DeserializeState(current.Payload, "process.before-invalid");
        var target = DeserializeTarget(targetPayload);
        if (!TryGetProcessId(current.Scope, out var processId) || target.ProcessId != processId)
        {
            throw new InvalidOperationException("process.target-scope-mismatch");
        }

        var desired = BuildDesiredState(before, target);
        var desiredPayload = ProcessTuningStateFingerprint.Serialize(desired);
        return Task.FromResult(new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            desiredPayload,
            ProcessTuningStateFingerprint.Hash(desiredPayload),
            Descriptor.ResourceKey));
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        var before = DeserializeState(change.BeforePayload, "process.before-invalid");
        var desired = DeserializeState(change.TargetPayload, "process.target-invalid");
        return await _platform.ApplyAsync(_operation, before, desired, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        var before = DeserializeState(material.BeforePayload, "process.recovery-before-invalid");
        return await _platform.RestoreAsync(
            _operation,
            material.TargetFingerprint,
            before,
            cancellationToken).ConfigureAwait(false);
    }

    private ProcessTuningState BuildDesiredState(ProcessTuningState before, ProcessTuningTarget target)
    {
        if (target.Operation != _operation)
        {
            throw new InvalidOperationException("process.operation-mismatch");
        }

        return _operation switch
        {
            ProcessTuningOperation.Affinity when target.AffinityMask is > 0 => before with { AffinityMask = target.AffinityMask.Value },
            ProcessTuningOperation.Priority when target.Priority is not null && IsSupportedPriority(target.Priority.Value) => before with { Priority = target.Priority.Value },
            ProcessTuningOperation.IdealProcessor => throw new InvalidOperationException("ideal-processor.readback-unavailable"),
            _ => throw new InvalidOperationException("process.target-invalid")
        };
    }

    private static bool IsSupportedPriority(System.Diagnostics.ProcessPriorityClass priority) =>
        priority is System.Diagnostics.ProcessPriorityClass.Normal
            or System.Diagnostics.ProcessPriorityClass.AboveNormal
            or System.Diagnostics.ProcessPriorityClass.High;

    private static bool TryGetProcessId(TargetScope scope, out int processId)
    {
        processId = 0;
        return scope.Kind == TargetScopeKind.Process
            && int.TryParse(scope.Identity, out processId)
            && processId > 4;
    }

    private static ProcessTuningState DeserializeState(string payload, string failureCode) =>
        JsonSerializer.Deserialize<ProcessTuningState>(payload, JsonOptions)
        ?? throw new InvalidOperationException(failureCode);

    internal static string SerializeTarget(ProcessTuningTarget target) =>
        JsonSerializer.Serialize(target, JsonOptions);

    private static ProcessTuningTarget DeserializeTarget(string payload) =>
        JsonSerializer.Deserialize<ProcessTuningTarget>(payload, JsonOptions)
        ?? throw new InvalidOperationException("process.target-invalid");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
}

public static class ProcessTuningStateFingerprint
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(ProcessTuningState state) => JsonSerializer.Serialize(state, Options);

    public static string Hash(ProcessTuningState state) => Hash(Serialize(state));

    public static string Hash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
