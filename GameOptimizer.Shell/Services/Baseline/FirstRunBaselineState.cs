namespace GameOptimizer.Shell.Services.Baseline;

public enum FirstRunBaselineState
{
	NotStarted,
	PreflightFailed,
	Running,
	ManualInterventionRequired,
	PartialFailed,
	CompletedVerified,
	Corrupted
}
