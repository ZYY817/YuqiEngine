namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRestoreResult
{
	public bool Success { get; init; }

	public string Message { get; init; } = string.Empty;

	public BaselineRestoreExecutionReport Report { get; init; } = new BaselineRestoreExecutionReport();
}
