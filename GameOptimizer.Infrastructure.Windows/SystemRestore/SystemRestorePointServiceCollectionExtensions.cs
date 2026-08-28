using System.Security.Cryptography;
using GameOptimizer.Core.SystemRestore;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Infrastructure.Windows.SystemRestore;

public static class SystemRestorePointServiceCollectionExtensions
{
    public static IServiceCollection AddSystemRestorePointWindowsInfrastructure(
        this IServiceCollection services,
        string workflowDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(workflowDirectory))
        {
            throw new ArgumentException("A durable System Restore workflow directory is required.", nameof(workflowDirectory));
        }

        services.AddSingleton<ISystemRestorePointWriterProcessRunner>(
            _ => new SystemRestorePointWriterProcessRunner(
                Path.Combine(AppContext.BaseDirectory, "YuqiEngine.exe")));
        services.AddSingleton<ISystemRestorePointPlatform, WindowsSystemRestorePointPlatform>();
        services.AddSingleton<ISystemRestorePointWorkflowStore>(
            _ => new FileSystemRestorePointWorkflowStore(workflowDirectory));
        services.AddSingleton<ISystemRestorePointConfirmationAuthority>(
            _ => new SystemRestorePointConfirmationAuthority(RandomNumberGenerator.GetBytes(32)));
        services.AddSingleton<ISystemRestorePointOwner>(provider => new SystemRestorePointOwner(
            provider.GetRequiredService<ISystemRestorePointPlatform>(),
            provider.GetRequiredService<ISystemRestorePointWorkflowStore>(),
            provider.GetRequiredService<ISystemRestorePointConfirmationAuthority>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<ISystemRestorePointStartupReconciliation>(provider =>
            new SystemRestorePointStartupReconciliation(
                provider.GetRequiredService<ISystemRestorePointOwner>()));
        return services;
    }
}
