namespace GameOptimizer.Shell.Services.Baseline;

public enum BaselineRestoreStep
{
	Precheck,
	EmergencyBackup,
	DomainRestore,
	PostVerify,
	Completed,
	Failed
}
