using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public interface IBaselineManualGuideService
{
	void OpenSystemProtectionSettings();

	Task<BaselineManualGuideCheckResult> RecheckRestorePointAsync(string packageId, DateTime flowStartedUtc, CancellationToken ct = default(CancellationToken));
}
