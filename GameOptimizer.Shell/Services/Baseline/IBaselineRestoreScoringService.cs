namespace GameOptimizer.Shell.Services.Baseline;

public interface IBaselineRestoreScoringService
{
	int CalculateScore(BaselineRestoreExecutionReport report);

	string ResolveLevelText(int score);
}
