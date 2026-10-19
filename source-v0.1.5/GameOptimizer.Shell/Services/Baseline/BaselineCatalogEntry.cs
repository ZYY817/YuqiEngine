using System;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineCatalogEntry
{
	public string PackageId { get; set; } = string.Empty;

	public string PackageType { get; set; } = "BaselineFirstRun";

	public string CreationSource { get; set; } = "Unknown";

	public string LifecycleState { get; set; } = "Active";

	public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

	public string AppVersion { get; set; } = string.Empty;

	public string IntegrityState { get; set; } = "Unknown";

	public FirstRunBaselineState BaselineState { get; set; } = FirstRunBaselineState.NotStarted;

	public int DomainSucceededCount { get; set; }

	public int DomainFailedCount { get; set; }

	public bool RequiresReboot { get; set; }

	public string MirrorDirectory { get; set; } = string.Empty;

	public string MirrorSyncState { get; set; } = "NotConfigured";

	public string MirrorSyncMessage { get; set; } = string.Empty;

	public DateTime? MirrorSyncedUtc { get; set; }

	public DateTime? LastRecoverabilityCheckUtc { get; set; }

	public string RecoverabilityCheckState { get; set; } = "NotChecked";

	public string RecoverabilityCheckMessage { get; set; } = string.Empty;
}
