using System.Security.Cryptography;
using System.Text;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Shell.Services.PowerManagement;

public sealed class BaselinePowerGate : IPowerBaselineGate
{
    private readonly IBaselineGateService _baselineGate;

    public BaselinePowerGate(IBaselineGateService baselineGate) =>
        _baselineGate = baselineGate ?? throw new ArgumentNullException(nameof(baselineGate));

    public async Task<PowerBaselineDecision> EnsureBaselineAsync(
        PowerPreparedPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var decision = await _baselineGate.EnsureCanOptimizeAsync(
            $"power:{plan.PlanId:N}:{plan.PlanHash}", cancellationToken).ConfigureAwait(false);
        if (!decision.CanProceed || string.IsNullOrWhiteSpace(decision.AuthorizationEvidenceId))
        {
            return new PowerBaselineDecision(false, null,
                string.IsNullOrWhiteSpace(decision.Reason)
                    ? "Risk acknowledgement was not obtained."
                    : decision.Reason);
        }

        // A normal "Allowed" decision represents the mandatory safety baseline.
        // Do not downgrade it to a bare opaque token: power writes must remain
        // bound to the package, its verified manifest and the recoverability check.
        if (decision.HasVerifiedRecoveryBaseline &&
            (string.IsNullOrWhiteSpace(decision.State.AuthorizedPackageId)
             || string.IsNullOrWhiteSpace(decision.State.AuthorizedManifestSha256)
             || decision.State.AuthorizedRecoverabilityCheckedUtc is null))
        {
            return new PowerBaselineDecision(false, null,
                "Verified baseline recovery evidence is incomplete.");
        }

        var evidence = string.Join('\u001F',
            PowerManagementIdentity.OwnerId,
            plan.ResourceKey,
            decision.Kind,
            decision.AuthorizationEvidenceId,
            decision.State.AuthorizedPackageId ?? string.Empty,
            decision.State.AuthorizedManifestSha256 ?? string.Empty,
            decision.State.AuthorizedRecoverabilityCheckedUtc?.ToUniversalTime().ToString("O") ?? string.Empty,
            plan.PlanHash);
        return new PowerBaselineDecision(
            true,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))),
            decision.Reason);
    }
}
