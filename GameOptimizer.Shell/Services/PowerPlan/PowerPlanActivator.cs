namespace GameOptimizer.Shell.Services.PowerPlan;

/// <summary>Legacy scheme-only activation is blocked; use the typed Power owner workflow.</summary>
public sealed class PowerPlanActivator
{
    public Task<bool> ActivateAsync(Guid planGuid) => Task.FromResult(false);
}
