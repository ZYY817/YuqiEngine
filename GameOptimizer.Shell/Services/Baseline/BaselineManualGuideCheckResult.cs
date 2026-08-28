using System;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineManualGuideCheckResult
{
	public bool Success { get; init; }

	public string Message { get; init; } = string.Empty;

	public DateTime CheckedUtc { get; init; } = DateTime.UtcNow;
}
