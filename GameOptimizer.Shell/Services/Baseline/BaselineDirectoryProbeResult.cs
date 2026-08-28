namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineDirectoryProbeResult
{
	public bool IsConfigured { get; init; }

	public bool Success { get; init; }

	public string NormalizedPath { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;
}
