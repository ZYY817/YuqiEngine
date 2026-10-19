using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Events;
using GameOptimizer.Core.Modules;
using System.Runtime.Versioning;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Optimization.Profiles;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.CpuAffinity;
using GameOptimizer.Core.Services.Memory;
using GameOptimizer.Core.Services.ProcessPriority;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Snapshot;
using GameOptimizer.Core.Services.SystemCleanup;
using GameOptimizer.Core.Security;
using GameOptimizer.Core.Theming;
using GameOptimizer.ModuleContracts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Extensions;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddGameOptimizerCore(this IServiceCollection services)
    {
        // 核心基础服务
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IGpuInventoryService, GpuInventoryService>();
        services.AddSingleton<ISystemInsightService, LiveSystemInsightService>();
        services.AddSingleton<IOperationRestoreStore, FileOperationRestoreStore>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IChangeConfirmationPrompt, DenyChangeConfirmationPrompt>();
        services.AddSingleton<IChangeConfirmationAuthority, ChangeConfirmationAuthority>();

        // Centralized change state stores. The Shell composes the durable Windows store
        // (FileChangeStateStore) before AddGameOptimizerCore, so these TryAdd registrations
        // defer to it; in Core-only compositions they provide safe in-memory fallbacks so the
        // orchestrator can still be constructed without a second durable recovery chain.
        // IRecoveryClaimStore and IChangeResourceLockProvider are intentionally left to the
        // orchestrator's own durable/in-process defaults (FileRecoveryClaimStore and
        // InProcessChangeResourceLockProvider) so recovery claims survive restart.
        services.TryAddSingleton<IRecoveryRepository, InMemoryChangeStateStore>();
        services.TryAddSingleton<IChangeJournal, InMemoryChangeStateStore>();
        services.TryAddSingleton<IChangeAuditStore, InMemoryChangeStateStore>();
        services.TryAddSingleton<IChangePlanStore, InMemoryChangePlanStore>();
        services.TryAddSingleton<IChangeExecutionGate, DenyChangeExecutionGate>();
        services.TryAddSingleton<CompatibilityChecker>();
        services.TryAddSingleton<IChangeCompatibilityPolicy>(provider => provider.GetRequiredService<CompatibilityChecker>());
        services.TryAddSingleton<IOptimizationCompatibilityPolicy>(provider => provider.GetRequiredService<CompatibilityChecker>());

        services.AddSingleton<IChangeOrchestrator, ChangeOrchestrator>();
        services.AddSingleton<BuiltInOptimizationOperationProvider>(provider =>
            new BuiltInOptimizationOperationProvider(
                provider.GetRequiredService<IChangeOrchestrator>(),
                provider.GetRequiredService<IChangeConfirmationAuthority>(),
                provider.GetService<ISecurityProtectionOwner>(),
                provider.GetService<GameOptimizer.Core.Services.NetworkReset.INetworkResetOwner>(),
                provider.GetService<ISystemCleanupService>(),
                provider.GetService<ICleanupQuarantineManager>(),
                provider.GetService<ICleanupAuthorizationGate>(),
                provider.GetService<GameOptimizer.Core.Services.Hosts.IHostsBackupOwner>()));
        services.AddSingleton<IOptimizationOperationProvider>(provider =>
            provider.GetRequiredService<BuiltInOptimizationOperationProvider>());
        services.AddSingleton<IOptimizationEngine, OptimizationEngine>();
        services.AddSingleton<OptimizationProfileService>();
        services.AddSingleton<IDiagnosticLogService, DiagnosticLogService>();
        services.AddSingleton<ILoggerProvider, DiagnosticLogLoggerProvider>();
        services.AddSingleton<IOperationHistoryService, OperationHistoryService>();
        services.AddSingleton<ISystemHealthService, SystemHealthService>();
        services.AddSingleton<IOperationDependencyService, OperationDependencyService>();
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<ModuleHost>();
        
        // CPU 亲和性服务
        services.AddSingleton<IProcessAffinityService, ProcessAffinityService>();
        
        // 快照和备份服务
        services.AddSingleton<SnapshotService>(provider =>
        {
            var authorizationGate = provider.GetService<ISnapshotRestoreAuthorizationGate>();
            if (authorizationGate is null)
            {
                return new SnapshotService(
                    new DenySnapshotRestoreAuthorizationGate(),
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            return new SnapshotService(
                authorizationGate,
                provider.GetRequiredService<IChangeOrchestrator>(),
                provider.GetRequiredService<IChangeConfirmationAuthority>(),
                provider.GetRequiredService<IOptimizationOperationProvider>(),
                provider.GetService<IRecoveryClaimStore>(),
                provider.GetService<TimeProvider>());
        });
        services.AddSingleton<ISnapshotService>(provider => provider.GetRequiredService<SnapshotService>());
        services.AddSingleton<IUserRegistryExportBackend, RegExeUserRegistryExportBackend>();
        services.AddSingleton<IComprehensiveBackupService>(provider =>
            new ComprehensiveBackupService(
                provider.GetRequiredService<ISnapshotService>(),
                provider.GetRequiredService<IUserRegistryExportBackend>(),
                operationProvider: provider.GetRequiredService<IOptimizationOperationProvider>(),
                operationRestoreStore: provider.GetRequiredService<IOperationRestoreStore>()));
        
        // 系统清理服务
        services.AddSingleton<ISystemCleanupService, SystemCleanupService>();
        
        // 内存管理服务
        services.AddSingleton<IMemoryCleanerService, MemoryCleanerService>();
        services.AddSingleton<IProcessCleanerService, ProcessCleanerService>();
        
        // 修复和扫描服务
        services.AddSingleton<IRepairService, RepairService>();
        services.AddSingleton<ISystemScanService, SystemScanService>();
        services.AddSingleton<IOptimizationRevertService, OptimizationRevertService>();
        
        // 终极错误处理架构 - 诊断和修复服务
        services.AddSingleton<ErrorDiagnosticEngine>();
        services.AddSingleton<SolutionProvider>();
        services.AddSingleton<AutoRepairService>();
        services.AddSingleton<PowerOperationDiagnostics>();
        services.AddSingleton<BackupDiagnosticService>();
        services.AddSingleton<SystemCompatibilityChecker>();
        
        // 优化守护服务 - 防止系统自动恢复
        services.AddSingleton<OptimizationGuardService>();
        
        return services;
    }
    
    /// <summary>
    /// 初始化电源操作诊断服务（在应用启动后调用）
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void InitializePowerOperationDiagnostics(IServiceProvider serviceProvider)
    {
        var diagnostics = serviceProvider.GetRequiredService<PowerOperationDiagnostics>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("GameOptimizer.PowerOperation");
        
        // 设置静态服务到 BasePowerPlanOperation
        GameOptimizer.Core.Optimization.Operations.BasePowerPlanOperation.SetDiagnostics(diagnostics, logger);
        
        // 设置日志记录器到 PowerCfgUtility（使用符合过滤规则的类别名）
        GameOptimizer.Core.Optimization.Operations.PowerCfgUtility.Logger = 
            loggerFactory.CreateLogger("GameOptimizer.PowerCfg");
    }

    public static IServiceCollection AddModule<TModule>(this IServiceCollection services)
        where TModule : class, IModule, new()
    {
        var module = new TModule();
        module.ConfigureServices(services);
        services.AddSingleton<IModule, TModule>();
        return services;
    }
}
