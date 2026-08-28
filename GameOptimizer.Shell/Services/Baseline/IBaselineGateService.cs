using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public interface IBaselineGateService
{
	Task<BaselineGateDecision> EnsureCanOptimizeAsync(string entryPoint, CancellationToken ct = default(CancellationToken));

	Task<BaselineSafetyState> GetStateAsync(CancellationToken ct = default(CancellationToken));

	Task SetReadonlyModeAsync(bool isReadonly, string entryPoint, string? reason = null, CancellationToken ct = default(CancellationToken));

	Task MarkBaselineCompletedVerifiedAsync(string entryPoint, string packageId, CancellationToken ct = default(CancellationToken));

	Task MarkBaselineFailureAsync(string entryPoint, FirstRunBaselineState state, string? reason = null, CancellationToken ct = default(CancellationToken));
}
