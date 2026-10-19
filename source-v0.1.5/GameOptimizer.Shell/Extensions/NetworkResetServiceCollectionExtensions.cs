using System.Windows;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.ApplicationRepair;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.DeviceRepair;
using GameOptimizer.Core.Services.MaintenanceDiagnostics;
using GameOptimizer.Core.Services.NetworkReset;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.StorageSafety;
using GameOptimizer.Shell.Services.Baseline;
using GameOptimizer.Shell.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Extensions;

public static class NetworkResetServiceCollectionExtensions
{
    public static IServiceCollection AddNetworkResetComposition(
        this IServiceCollection services,
        Func<Window?> ownerProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ownerProvider);
        RejectDuplicate<INetworkResetBaselineGate>(services);
        RejectDuplicate<NetworkResetConfirmationPrompt>(services);
        RejectDuplicate<INetworkResetOwner>(services);

        services.AddSingleton<INetworkResetBaselineGate, BaselineNetworkResetAuthorizationGate>();
        services.AddSingleton(new NetworkResetConfirmationPrompt(ownerProvider));
        services.AddSingleton<INetworkResetOwner>(provider =>
        {
            var confirmation = new ChangeConfirmationAuthority(
                provider.GetRequiredService<NetworkResetConfirmationPrompt>(),
                provider.GetRequiredService<TimeProvider>());
            return new NetworkResetOwner(
                provider.GetRequiredService<INetworkResetPlatform>(),
                provider.GetRequiredService<INetworkResetBaselineGate>(),
                confirmation,
                provider.GetRequiredService<INetworkResetPlanStore>(),
                provider.GetRequiredService<INetworkResetReceiptStore>(),
                provider.GetRequiredService<IChangeResourceLockProvider>(),
                provider.GetRequiredService<TimeProvider>());
        });

        services.Replace(ServiceDescriptor.Singleton<AutoRepairService>(provider =>
            new AutoRepairService(
                provider.GetRequiredService<ILogger<AutoRepairService>>(),
                provider.GetRequiredService<INetworkResetOwner>())));
        services.Replace(ServiceDescriptor.Singleton<RepairItemRegistry>(provider =>
            new RepairItemRegistry(
                provider.GetService<IOptimizationRevertService>(),
                provider.GetRequiredService<INetworkResetOwner>(),
                provider.GetRequiredService<IApplicationPackageRepairOwner>(),
                provider.GetRequiredService<IDeviceRepairOwner>(),
                provider.GetRequiredService<IMaintenanceDiagnosticOwner>(),
                provider.GetRequiredService<IStorageFeatureRepairOwner>())));
        return services;
    }

    private static void RejectDuplicate<TService>(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TService)))
        {
            throw new InvalidOperationException(
                $"{typeof(TService).Name} 已注册，拒绝创建第二个 Network Reset composition。");
        }
    }
}
