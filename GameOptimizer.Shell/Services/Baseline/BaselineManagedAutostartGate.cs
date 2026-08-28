using GameOptimizer.Core.Services.Startup;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>Adapts the shared baseline risk acknowledgement policy to the typed autostart plan.</summary>
public sealed class BaselineManagedAutostartGate : IManagedAutostartGate
{
    private readonly IBaselineGateService _baseline;
    public BaselineManagedAutostartGate(IBaselineGateService baseline) => _baseline = baseline;

    public async Task<bool> AuthorizeBaselineAsync(ManagedAutostartPlan plan, CancellationToken cancellationToken)
    {
        var decision = await _baseline.EnsureCanOptimizeAsync(
            $"autostart:{plan.PlanId:N}:{plan.PlanHash}", cancellationToken).ConfigureAwait(false);
        return decision.CanProceed;
    }
}
