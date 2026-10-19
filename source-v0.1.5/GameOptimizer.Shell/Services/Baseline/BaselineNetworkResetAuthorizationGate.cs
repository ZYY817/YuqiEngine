using System.Security.Cryptography;
using System.Text;
using GameOptimizer.Core.Services.NetworkReset;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineNetworkResetAuthorizationGate : INetworkResetBaselineGate
{
    private readonly IBaselineGateService _baselineGate;

    public BaselineNetworkResetAuthorizationGate(IBaselineGateService baselineGate) =>
        _baselineGate = baselineGate ?? throw new ArgumentNullException(nameof(baselineGate));

    public async Task<NetworkResetBaselineDecision> AuthorizeAsync(
        NetworkResetPlan plan,
        CancellationToken cancellationToken)
    {
        var decision = await _baselineGate.EnsureCanOptimizeAsync(
            $"network-reset:{plan.ChangePlan.PlanId:N}:{plan.ChangePlan.PlanHash}",
            cancellationToken).ConfigureAwait(false);
        if (!decision.CanProceed || string.IsNullOrWhiteSpace(decision.AuthorizationEvidenceId))
        {
            return new NetworkResetBaselineDecision(
                false,
                "baseline.network-reset-risk-not-acknowledged",
                decision.Reason,
                null);
        }

        var evidence = string.Join('\u001F',
            decision.Kind,
            decision.AuthorizationEvidenceId,
            plan.ChangePlan.PlanHash);
        return new NetworkResetBaselineDecision(
            true,
            decision.HasVerifiedRecoveryBaseline
                ? "baseline.network-reset-verified"
                : "baseline.network-reset-risk-acknowledged",
            decision.Reason,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))));
    }
}
