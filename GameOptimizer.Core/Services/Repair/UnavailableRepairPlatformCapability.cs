using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Repair;

/// <summary>
/// Reserves the single capability/resource identity for a Repair item whose real platform
/// adapter has not yet proved snapshot and restore semantics. It deliberately fails closed
/// during Probe so the legacy delegate can no longer perform an uncoordinated write.
/// </summary>
public sealed class UnavailableRepairPlatformCapability : IChangeCapability
{
    public UnavailableRepairPlatformCapability(string capabilityId, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        Descriptor = new CapabilityDescriptor(
            capabilityId,
            "1.0.0",
            ChangeRisk.HighSystem,
            [TargetScopeKind.Machine],
            RequiresAdministrator: true,
            RequiresExplicitConfirmation: true,
            RequiresRecoveryMaterial: true,
            RequiresRestart: false,
            resourceKey);
    }

    public CapabilityDescriptor Descriptor { get; }

    public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CapabilityAssessment(
            CapabilityApplicability.Unsupported,
            false,
            "platform.adapter-not-verified",
            "正式平台适配器尚未通过快照、回读和恢复验证，已安全阻断。"));
    }

    public Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Unavailable platform capability cannot read mutable state.");

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Unavailable platform capability cannot prepare a write.");

    public Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Unavailable platform capability cannot execute a write.");

    public Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Unavailable platform capability has no recovery material.");
}
