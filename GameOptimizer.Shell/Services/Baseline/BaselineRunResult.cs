namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRunResult
{
	public bool Success { get; init; }

	public bool RequiresManualGuide { get; init; }

	public string Message { get; init; } = string.Empty;

	public BaselineRuntimeState RuntimeState { get; init; } = new BaselineRuntimeState();

	public BaselinePackageManifest? Manifest { get; init; }
}
