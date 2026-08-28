using System;
using System.Collections.Generic;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRuntimeState
{
	public int SchemaVersion { get; set; } = 1;

	public string PackageId { get; set; } = string.Empty;

	public string CreationSource { get; set; } = "FirstRunMandatory";

	public BaselineFlowStep CurrentStep { get; set; } = BaselineFlowStep.Overview;

	public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

	public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

	public bool ManualGuideCompleted { get; set; }

	public bool HasBlockingFailure { get; set; }

	public List<BaselineDomainProgress> Domains { get; set; } = new List<BaselineDomainProgress>();
}
