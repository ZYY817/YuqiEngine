using System;
using System.Collections.Generic;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselinePackageManifest
{
	public int SchemaVersion { get; set; } = 3;

	public string PackageId { get; set; } = string.Empty;

	public string PackageType { get; set; } = "BaselineFirstRun";

	public string CreationSource { get; set; } = "FirstRunMandatory";

	public string LifecycleState { get; set; } = "Creating";

	public string AppVersion { get; set; } = string.Empty;

	public string OsVersion { get; set; } = string.Empty;

	public BaselineEnvironmentBinding? EnvironmentBinding { get; set; }

	public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

	public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

	public FirstRunBaselineState BaselineState { get; set; } = FirstRunBaselineState.NotStarted;

	public bool RequiresReboot { get; set; }

	public string IntegrityState { get; set; } = "Unknown";

	public string MirrorDirectory { get; set; } = string.Empty;

	public string MirrorSyncState { get; set; } = "NotConfigured";

	public string MirrorSyncMessage { get; set; } = string.Empty;

	public DateTime? MirrorSyncedUtc { get; set; }

	public DateTime? LastRecoverabilityCheckUtc { get; set; }

	public string RecoverabilityCheckState { get; set; } = "NotChecked";

	public string RecoverabilityCheckMessage { get; set; } = string.Empty;

	public List<BaselineDomainArtifact> Domains { get; set; } = new List<BaselineDomainArtifact>();
}
