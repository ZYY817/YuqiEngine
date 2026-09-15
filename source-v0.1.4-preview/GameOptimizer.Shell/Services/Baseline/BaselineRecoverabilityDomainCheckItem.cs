namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRecoverabilityDomainCheckItem
{
	public string DomainId { get; set; } = string.Empty;

	public string DomainName { get; set; } = string.Empty;

	public BaselineDomainStatus Status { get; set; } = BaselineDomainStatus.Pending;

	public string Message { get; set; } = string.Empty;
}
