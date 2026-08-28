using System;
using System.Collections.Generic;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRecoverabilityCheckReport
{
	public string CheckId { get; set; } = string.Empty;

	public string PackageId { get; set; } = string.Empty;

	public string Trigger { get; set; } = string.Empty;

	public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

	public DateTime FinishedUtc { get; set; } = DateTime.UtcNow;

	public bool VerifyPassed { get; set; }

	public int DomainCheckedCount { get; set; }

	public int DomainFailedCount { get; set; }

	/// <summary>
	/// 域资产可读取但尚未具备受控自动恢复链的数量。
	/// 该状态不能被包装完整性校验掩盖，也不能用于解锁优化入口。
	/// </summary>
	public int DomainManualRequiredCount { get; set; }

	public string Summary { get; set; } = string.Empty;

	public List<BaselineRecoverabilityDomainCheckItem> DomainChecks { get; set; } = new List<BaselineRecoverabilityDomainCheckItem>();
}
