namespace GameOptimizer.Shell.Services.PowerPlan;

/// <summary>
/// Dynamic setting writes cannot satisfy the typed Power recovery contract and are blocked.
/// Every requested key is returned as not applied; no command or registry fallback exists.
/// </summary>
public sealed class PowerSettingApplicator
{
    public Task<List<string>> ApplyAllAsync(Guid planGuid, Dictionary<string, int> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Task.FromResult(settings.Keys.ToList());
    }
}
