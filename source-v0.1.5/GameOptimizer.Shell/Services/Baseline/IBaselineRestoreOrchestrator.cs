using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public interface IBaselineRestoreOrchestrator
{
	Task<BaselineRestoreResult> RestoreAsync(string packageId, IProgress<BaselineRestoreProgressEvent>? progress = null, CancellationToken ct = default(CancellationToken), BaselineRestoreMode mode = BaselineRestoreMode.Full);

	Task<BaselineRecoverabilityCheckResult> RunRecoverabilitySelfCheckAsync(string packageId, string trigger, CancellationToken ct = default(CancellationToken));
}
