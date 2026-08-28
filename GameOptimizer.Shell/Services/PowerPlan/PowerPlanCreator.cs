namespace GameOptimizer.Shell.Services.PowerPlan;

/// <summary>
/// Legacy compatibility surface. Scheme create/rename/delete are intentionally fail-closed until
/// the typed Power owner proves immutable planning, explicit confirmation and lossless recovery.
/// </summary>
public sealed class PowerPlanCreator
{
    public Task<Guid?> CreateAsync(string planName, Guid baseGuid) => Task.FromResult<Guid?>(null);
    public Task RenameAsync(Guid planGuid, string newName) => Task.FromException(
        new InvalidOperationException("Scheme rename is blocked until a typed lifecycle recovery contract is connected."));
    public Task DeleteAsync(string planGuid) => Task.FromException(
        new InvalidOperationException("Scheme deletion is blocked until a typed lifecycle recovery contract is connected."));
}
