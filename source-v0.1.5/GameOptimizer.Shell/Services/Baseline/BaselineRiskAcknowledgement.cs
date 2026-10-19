using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed record BaselineRiskAcknowledgementRequest(
	string EntryPoint,
	FirstRunBaselineState BaselineState,
	string RiskSummary,
	string RecoveryCapabilityDescription);

public interface IBaselineRiskAcknowledgementPrompt
{
	Task<bool> ConfirmAsync(
		BaselineRiskAcknowledgementRequest request,
		CancellationToken cancellationToken = default);
}
