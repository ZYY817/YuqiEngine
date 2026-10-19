using System;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRecoverabilityCheckResult
{
	public bool Success { get; init; }

	public string PackageId { get; init; } = string.Empty;

	public DateTime CheckedUtc { get; init; } = DateTime.UtcNow;

	public int DomainCheckedCount { get; init; }

	public int DomainFailedCount { get; init; }

	public int DomainManualRequiredCount { get; init; }

	public string Message { get; init; } = string.Empty;

	public string ReportFilePath { get; init; } = string.Empty;
}
