using System;
using System.Collections.Generic;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRestoreExecutionReport
{
	public string RestoreId { get; set; } = string.Empty;

	public string SourcePackageId { get; set; } = string.Empty;

	public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

	public DateTime FinishedUtc { get; set; } = DateTime.UtcNow;

	public string PrecheckStatus { get; set; } = string.Empty;

	public string EmergencySnapshotId { get; set; } = string.Empty;

	public bool EmergencySnapshotCreated { get; set; }

	public int SuccessCount { get; set; }

	public int FailedCount { get; set; }

	public int SkippedCount { get; set; }

	public bool RequiresReboot { get; set; }

	public string PostVerificationSummary { get; set; } = string.Empty;

	public int RestoreConfidenceScore { get; set; }

	public List<BaselineRestoreDomainResult> DomainResults { get; set; } = new List<BaselineRestoreDomainResult>();

	public List<string> NextActions { get; set; } = new List<string>();

	public string ReportFilePath { get; set; } = string.Empty;
}
