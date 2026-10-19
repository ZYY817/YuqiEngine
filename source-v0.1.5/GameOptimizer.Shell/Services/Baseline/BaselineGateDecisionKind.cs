namespace GameOptimizer.Shell.Services.Baseline;

public enum BaselineGateDecisionKind
{
	Allowed,
	AllowedWithRiskAcknowledgement,
	ReadonlyOnly,
	BlockedNeedBaseline,
	BlockedRiskNotAcknowledged
}
