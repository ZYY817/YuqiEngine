using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.PowerManagement;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class PowerDependencyInjectionTests
{
    [Fact]
    public void InfrastructureMappingsAreSingletonAndShareOneStoreAndPersistentSecret()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GameOptimizer-PowerDiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var services = new ServiceCollection();
            services.AddPowerManagementWindowsInfrastructure(directory, _ => new XorProtector());
            services.AddSingleton<IPowerBaselineGate, AllowBaselineGate>();
            services.AddSingleton<IPowerManagementOwner, PowerManagementOwner>();

            AssertSingleSingletonRegistration<IPowerProcessFactory>(services);
            AssertSingleSingletonRegistration<IPowerCfgProcessRunner>(services);
            AssertSingleSingletonRegistration<IPowerPlatform>(services);
            AssertSingleSingletonRegistration<IManagedPowerPlanPlatform>(services);
            AssertSingleSingletonRegistration<FileManagedPowerPlanStore>(services);
            AssertSingleSingletonRegistration<IManagedPowerPlanStore>(services);
            AssertSingleSingletonRegistration<FilePowerWorkflowStore>(services);
            AssertSingleSingletonRegistration<IPowerWorkflowStore>(services);
            AssertSingleSingletonRegistration<IPowerConfirmationSecretProvider>(services);
            AssertSingleSingletonRegistration<IPowerConfirmationAuthority>(services);
            AssertSingleSingletonRegistration<IPowerBaselineGate>(services);
            AssertSingleSingletonRegistration<IPowerManagementOwner>(services);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

            Assert.Same(provider.GetRequiredService<IPowerProcessFactory>(), provider.GetRequiredService<IPowerProcessFactory>());
            Assert.Same(provider.GetRequiredService<IPowerCfgProcessRunner>(), provider.GetRequiredService<IPowerCfgProcessRunner>());
            Assert.Same(provider.GetRequiredService<IPowerPlatform>(), provider.GetRequiredService<IPowerPlatform>());
            Assert.Same(provider.GetRequiredService<IManagedPowerPlanPlatform>(), provider.GetRequiredService<IManagedPowerPlanPlatform>());
            Assert.Same(provider.GetRequiredService<IManagedPowerPlanStore>(), provider.GetRequiredService<FileManagedPowerPlanStore>());
            Assert.Same(provider.GetRequiredService<IPowerWorkflowStore>(), provider.GetRequiredService<IPowerConfirmationSecretProvider>());
            Assert.Same(provider.GetRequiredService<IPowerBaselineGate>(), provider.GetRequiredService<IPowerBaselineGate>());
            Assert.Same(provider.GetRequiredService<IPowerManagementOwner>(), provider.GetRequiredService<IPowerManagementOwner>());
            Assert.Same(provider.GetRequiredService<IPowerConfirmationAuthority>(), provider.GetRequiredService<IPowerConfirmationAuthority>());

            var secret = provider.GetRequiredService<IPowerConfirmationSecretProvider>().GetConfirmationSecret();
            using var restartProvider = new ServiceCollection()
                .AddPowerManagementWindowsInfrastructure(directory, _ => new XorProtector())
                .BuildServiceProvider();
            Assert.Equal(secret, restartProvider.GetRequiredService<IPowerConfirmationSecretProvider>().GetConfirmationSecret());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static void AssertSingleSingletonRegistration<TService>(IServiceCollection services)
    {
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    private sealed class XorProtector : IPowerDataProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Select(value => (byte)(value ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] protectedData) => Protect(protectedData);
    }

    private sealed class AllowBaselineGate : IPowerBaselineGate
    {
        public Task<PowerBaselineDecision> EnsureBaselineAsync(PowerPreparedPlan plan, CancellationToken cancellationToken) =>
            Task.FromResult(new PowerBaselineDecision(true, "baseline", null));
    }
}
