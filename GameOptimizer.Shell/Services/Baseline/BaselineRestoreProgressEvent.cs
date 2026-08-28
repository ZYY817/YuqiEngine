namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRestoreProgressEvent
{
	public BaselineRestoreStep Step { get; init; } = BaselineRestoreStep.Precheck;

	public string Message { get; init; } = string.Empty;

	public string? DomainId { get; init; }

	public BaselineDomainStatus? DomainStatus { get; init; }

	public int CompletedDomains { get; init; }

	public int TotalDomains { get; init; }
}
