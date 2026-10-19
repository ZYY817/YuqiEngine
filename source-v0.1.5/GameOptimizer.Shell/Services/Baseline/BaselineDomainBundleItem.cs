namespace GameOptimizer.Shell.Services.Baseline;

internal sealed class BaselineDomainBundleItem
{
	public string ItemType { get; set; } = string.Empty;

	public string SourcePath { get; set; } = string.Empty;

	public string RelativePath { get; set; } = string.Empty;

	public long SizeBytes { get; set; }

	public string Notes { get; set; } = string.Empty;
}
