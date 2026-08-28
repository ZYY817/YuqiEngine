using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Memory;
using GameOptimizer.Infrastructure.Windows.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Infrastructure.Windows.Extensions;

/// <summary>
/// Memory-only composition seam. The application startup does not call this
/// extension yet; wiring it is a separate shared App/DI change after the Shell
/// entry is migrated to IMemoryCleaningOwner.
/// </summary>
public static class MemoryCleaningServiceCollectionExtensions
{
    public static IServiceCollection AddMemoryCleaningWindowsInfrastructure(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IMemoryCleaningPlatform, WindowsMemoryCleaningPlatform>();
        services.AddSingleton<IChangeCapability>(provider => new MemoryCleaningCapability(
            MemoryCleaningCapabilityKind.WorkingSetTrim,
            provider.GetRequiredService<IMemoryCleaningPlatform>()));
        services.AddSingleton<IChangeCapability>(provider => new MemoryCleaningCapability(
            MemoryCleaningCapabilityKind.StandbyListPurge,
            provider.GetRequiredService<IMemoryCleaningPlatform>()));
        services.AddSingleton<MemoryCleaningOwner>();
        services.AddSingleton<IMemoryCleaningOwner>(provider =>
            provider.GetRequiredService<MemoryCleaningOwner>());
        return services;
    }
}
