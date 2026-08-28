using System;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRestoreDomainResult
{
	public string DomainId { get; set; } = string.Empty;

	public string DomainName { get; set; } = string.Empty;

	public BaselineDomainStatus Status { get; set; } = BaselineDomainStatus.Pending;

	public string? ErrorCode { get; set; }

	public string Message { get; set; } = string.Empty;

	public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

	public DateTime FinishedUtc { get; set; } = DateTime.UtcNow;
}
