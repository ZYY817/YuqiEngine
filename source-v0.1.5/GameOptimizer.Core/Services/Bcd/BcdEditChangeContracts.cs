namespace GameOptimizer.Core.Services.Bcd;

public sealed record BcdStateSnapshot(
    bool ReadSucceeded,
    string? ElementValue);

public sealed record BcdPlatformAssessment(
    bool HasRequiredPermission,
    bool SecureBootEnabled,
    bool OsSupported,
    string ReasonCode,
    string Details);

public sealed record BcdMutationResult(
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message);

public interface IBcdEditAccessor
{
    Task<BcdPlatformAssessment> ProbeAsync(CancellationToken cancellationToken);

    Task<BcdStateSnapshot> ReadAsync(string elementName, CancellationToken cancellationToken);

    Task<BcdMutationResult> ApplyAsync(string elementName, string? targetValue, CancellationToken cancellationToken);

    Task<BcdMutationResult> RestoreAsync(string elementName, BcdStateSnapshot original, CancellationToken cancellationToken);
}
