namespace GameOptimizer.Shell.Services.Baseline;

internal sealed class BaselineDomainRestoreStep
{
	public string StepType { get; set; } = string.Empty;

	public string SourceRelativePath { get; set; } = string.Empty;

	public string TargetPath { get; set; } = string.Empty;

	public string Command { get; set; } = string.Empty;

	public string Arguments { get; set; } = string.Empty;

	public bool Optional { get; set; }

	public bool Overwrite { get; set; } = true;

	public string Description { get; set; } = string.Empty;
}
