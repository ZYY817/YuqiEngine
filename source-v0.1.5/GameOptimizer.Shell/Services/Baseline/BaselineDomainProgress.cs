using System;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineDomainProgress
{
	public string DomainId { get; set; } = string.Empty;

	public string DomainName { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public bool Required { get; set; } = true;

	public BaselineDomainStatus Status { get; set; } = BaselineDomainStatus.Pending;

	public string? ErrorCode { get; set; }

	public string? ErrorMessage { get; set; }

	public DateTime? StartedUtc { get; set; }

	public DateTime? FinishedUtc { get; set; }
}
