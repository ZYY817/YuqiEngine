namespace GameOptimizer.Shell.Services.Baseline;

public sealed record BaselineGateDecision(
	BaselineGateDecisionKind Kind,
	string Reason,
	BaselineSafetyState State,
	string? AuthorizationEvidenceId = null)
{
	public bool CanProceed => Kind is BaselineGateDecisionKind.Allowed
		or BaselineGateDecisionKind.AllowedWithRiskAcknowledgement;

	public bool HasVerifiedRecoveryBaseline => Kind == BaselineGateDecisionKind.Allowed;
}
