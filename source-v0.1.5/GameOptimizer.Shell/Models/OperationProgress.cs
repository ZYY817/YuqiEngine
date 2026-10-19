namespace GameOptimizer.Shell.Models;

public sealed class OperationProgress
{
	public int CurrentIndex { get; init; }

	public int TotalCount { get; init; }

	public string CurrentItemName { get; init; } = string.Empty;

	public BatchOperationStatus Status { get; init; }
}
