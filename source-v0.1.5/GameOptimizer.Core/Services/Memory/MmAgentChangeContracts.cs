namespace GameOptimizer.Core.Services.Memory;

public sealed record MmAgentStateSnapshot(bool MemoryCompressionEnabled);

public sealed record MmAgentPlatformAssessment(
    bool HasRequiredPermission,
    string ReasonCode,
    string Details);

public sealed record MmAgentMutationResult(
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message);

public interface IMmAgentAccessor
{
    Task<MmAgentPlatformAssessment> ProbeAsync(CancellationToken cancellationToken);

    Task<MmAgentStateSnapshot> ReadAsync(CancellationToken cancellationToken);

    Task<MmAgentMutationResult> ApplyAsync(bool memoryCompressionEnabled, CancellationToken cancellationToken);

    Task<MmAgentMutationResult> RestoreAsync(MmAgentStateSnapshot original, CancellationToken cancellationToken);
}
