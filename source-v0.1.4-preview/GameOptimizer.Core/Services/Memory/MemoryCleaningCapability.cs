using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Memory;

/// <summary>
/// Typed Memory capability used by the centralized ChangeOrchestrator. The
/// platform decides applicability; the current Windows platform refuses mutation
/// because it cannot provide a truthful inverse and recovery readback.
/// </summary>
public sealed class MemoryCleaningCapability : IChangeCapability
{
    private const string DefinitionVersion = "memory-cleaning.v1";
    private readonly IMemoryCleaningPlatform _platform;
    private readonly MemoryCleaningCapabilityKind _capability;

    public MemoryCleaningCapability(
        MemoryCleaningCapabilityKind capability,
        IMemoryCleaningPlatform platform)
    {
        _capability = capability;
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        Descriptor = new CapabilityDescriptor(
            MemoryCleaningCapabilityIds.For(capability),
            DefinitionVersion,
            ChangeRisk.HighSystem,
            capability is MemoryCleaningCapabilityKind.WorkingSetTrim or MemoryCleaningCapabilityKind.ProcessTermination
                ? [TargetScopeKind.Process]
                : [TargetScopeKind.Machine],
            RequiresAdministrator: capability == MemoryCleaningCapabilityKind.StandbyListPurge,
            RequiresExplicitConfirmation: true,
            // Process-scoped working-set changes and process termination have no
            // truthful inverse. They are therefore explicitly confirmed and never
            // receive a fabricated RecoveryId.
            // Standby List purge remains fail-closed because it is machine scoped.
            RequiresRecoveryMaterial: capability == MemoryCleaningCapabilityKind.StandbyListPurge,
            RequiresRestart: false,
            ResourceKey: capability switch
            {
                MemoryCleaningCapabilityKind.WorkingSetTrim => "memory:process-working-set",
                MemoryCleaningCapabilityKind.ProcessTermination => "memory:process-termination",
                _ => "memory:machine-standby-list"
            },
            ExecutionPolicyVersion: "bounded-platform.v1");
    }

    public CapabilityDescriptor Descriptor { get; }

    public Task<CapabilityAssessment> ProbeAsync(
        TargetScope scope,
        CancellationToken cancellationToken) =>
        ProbeAsync(scope, ChangeIntent.Prepare, cancellationToken);

    public Task<CapabilityAssessment> ProbeAsync(
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken)
    {
        if (!Descriptor.SupportedScopes.Contains(scope.Kind))
        {
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "memory.scope-invalid",
                "Memory capability 不支持当前作用域。"));
        }

        return _platform.ProbeAsync(_capability, scope, intent, cancellationToken);
    }

    public Task<StateObservation> ReadAsync(
        TargetScope scope,
        CancellationToken cancellationToken) =>
        _platform.ReadAsync(_capability, scope, cancellationToken);

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(current);

        var target = MemoryCleaningPayloadCodec.DeserializeTarget(targetPayload);
        if (target.Capability != _capability
            || !Descriptor.SupportedScopes.Contains(current.Scope.Kind))
        {
            throw new InvalidOperationException("memory.target-scope-mismatch");
        }

        ValidateTarget(target, current.Scope);
        _ = MemoryCleaningPayloadCodec.DeserializeObserved(current.Payload);

        var targetFingerprint = _capability switch
        {
            MemoryCleaningCapabilityKind.WorkingSetTrim => MemoryCleaningPayloadCodec.WorkingSetTrimFingerprint(target.ProcessIdentity!),
            MemoryCleaningCapabilityKind.ProcessTermination => MemoryCleaningPayloadCodec.ProcessTerminationFingerprint(target.ProcessIdentity!),
            _ => MemoryCleaningPayloadCodec.Hash(targetPayload)
        };

        return Task.FromResult(new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            targetPayload,
            targetFingerprint,
            Descriptor.ResourceKey));
    }

    public Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken) =>
        _platform.ApplyAsync(_capability, change, cancellationToken);

    public Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken) =>
        _platform.RestoreAsync(_capability, material, cancellationToken);

    private void ValidateTarget(
        MemoryCleaningTargetPayload target,
        TargetScope scope)
    {
        if (_capability is MemoryCleaningCapabilityKind.WorkingSetTrim or MemoryCleaningCapabilityKind.ProcessTermination)
        {
            if (scope.Kind != TargetScopeKind.Process
                || target.ProcessIdentity is null
                || !target.ProcessIdentity.IsUsable
                || !string.Equals(
                    scope.Identity,
                    target.ProcessIdentity.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("memory.process-identity-invalid");
            }

            return;
        }

        if (scope.Kind != TargetScopeKind.Machine
            || string.IsNullOrWhiteSpace(target.MachineIdentity))
        {
            throw new InvalidOperationException("memory.machine-identity-invalid");
        }
    }
}
