using System;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineSafetyState
{
	public int SchemaVersion { get; set; } = 1;

	public FirstRunBaselineState FirstRunState { get; set; } = FirstRunBaselineState.NotStarted;

	public bool IsReadonlyMode { get; set; }

	public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

	public DateTime? CompletedUtc { get; set; }

	public string? LastEntryPoint { get; set; }

	public string? LastError { get; set; }

	public string? AuthorizedPackageId { get; set; }

	public string? AuthorizedManifestSha256 { get; set; }

	public DateTime? AuthorizedRecoverabilityCheckedUtc { get; set; }
}
