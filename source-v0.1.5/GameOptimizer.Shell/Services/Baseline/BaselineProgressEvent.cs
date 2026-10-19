namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineProgressEvent
{
	public BaselineFlowStep Step { get; init; }

	public string Message { get; init; } = string.Empty;

	public string? DomainId { get; init; }

	public BaselineDomainStatus? DomainStatus { get; init; }

	public int CompletedDomains { get; init; }

	public int TotalDomains { get; init; }
}
