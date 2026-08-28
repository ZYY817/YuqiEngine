using GameOptimizer.Core.Services.SystemCleanup;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// The SystemCleanup adapter to the shared baseline risk policy. Preview confirmation, quarantine,
/// capability checks and failure blocking remain owned by SystemCleanupService.
/// </summary>
public sealed class BaselineCleanupAuthorizationGate : ICleanupAuthorizationGate
{
    private readonly IBaselineGateService _baselineGate;

    public BaselineCleanupAuthorizationGate(IBaselineGateService baselineGate)
    {
        _baselineGate = baselineGate ?? throw new ArgumentNullException(nameof(baselineGate));
    }

    public async Task<CleanupAuthorizationDecision> AuthorizeAsync(
        CleanupPreview preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        BaselineGateDecision decision = await _baselineGate
            .EnsureCanOptimizeAsync($"system-cleanup:{preview.ItemId}:{preview.Fingerprint}", cancellationToken)
            .ConfigureAwait(false);

        return decision.CanProceed
            ? new(true, "cleanup.baseline-risk-authorized", decision.Reason)
            : new(false, "cleanup.baseline-risk-not-acknowledged", decision.Reason);
    }
}
