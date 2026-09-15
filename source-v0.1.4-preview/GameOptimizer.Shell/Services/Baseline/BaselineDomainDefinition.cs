namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineDomainDefinition
{
	public string DomainId { get; init; } = string.Empty;

	public string DomainName { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public bool Required { get; init; } = true;

	public bool SupportsManualGuide { get; init; }
}
