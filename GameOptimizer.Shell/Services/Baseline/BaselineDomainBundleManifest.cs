using System;
using System.Collections.Generic;

namespace GameOptimizer.Shell.Services.Baseline;

internal sealed class BaselineDomainBundleManifest
{
	public int SchemaVersion { get; set; } = 2;

	public string DomainId { get; set; } = string.Empty;

	public string DomainName { get; set; } = string.Empty;

	public bool Required { get; set; } = true;

	public BaselineDomainStatus Status { get; set; } = BaselineDomainStatus.Pending;

	public string Summary { get; set; } = string.Empty;

	public DateTime CollectedAtUtc { get; set; } = DateTime.UtcNow;

	public List<BaselineDomainBundleItem> Items { get; set; } = new List<BaselineDomainBundleItem>();

	public List<BaselineDomainRestoreStep> RestoreSteps { get; set; } = new List<BaselineDomainRestoreStep>();

	public List<string> Warnings { get; set; } = new List<string>();
}
