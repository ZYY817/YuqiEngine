using System;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRestoreScoringService : IBaselineRestoreScoringService
{
	public int CalculateScore(BaselineRestoreExecutionReport report)
	{
		ArgumentNullException.ThrowIfNull(report, "report");
		int num = Math.Max(1, report.SuccessCount + report.FailedCount + report.SkippedCount);
		double num2 = (double)report.SuccessCount / (double)num;
		double num3 = (double)report.FailedCount / (double)num;
		double num4 = (double)report.SkippedCount / (double)num;
		int num5 = (int)Math.Round(num2 * 100.0 - num3 * 120.0 - num4 * 10.0);
		if (!report.EmergencySnapshotCreated)
		{
			num5 -= 20;
		}
		if (report.RequiresReboot)
		{
			num5 -= 3;
		}
		return Math.Clamp(num5, 0, 100);
	}

	public string ResolveLevelText(int score)
	{
		string result = ((score >= 95) ? "High" : ((score < 80) ? "Low" : "Medium"));
		return result;
	}
}
