namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineDomainArtifact
{
	public string DomainId { get; set; } = string.Empty;

	public string DomainName { get; set; } = string.Empty;

	public bool Required { get; set; } = true;

	public BaselineDomainStatus Status { get; set; } = BaselineDomainStatus.Pending;

	public string RelativePath { get; set; } = string.Empty;

	public long SizeBytes { get; set; }

	public string Sha256 { get; set; } = string.Empty;

	public string? ErrorCode { get; set; }

	public string? ErrorMessage { get; set; }
}
