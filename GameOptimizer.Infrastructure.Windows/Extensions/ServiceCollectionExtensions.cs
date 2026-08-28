using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.NetworkReset;
using GameOptimizer.Core.Services.ProcessTuning;
using GameOptimizer.Core.Services.SystemServices;
 using GameOptimizer.Core.Services.Bcd;
 using GameOptimizer.Core.Services.DeviceRepair;
using GameOptimizer.Core.Services.Hosts;
using GameOptimizer.Core.Services.ScheduledTasks;
using GameOptimizer.Core.Services.Memory;
using GameOptimizer.Core.Services.Startup;
using GameOptimizer.Core.Services.ApplicationRepair;
using GameOptimizer.Infrastructure.Windows.ApplicationRepair;
using GameOptimizer.Infrastructure.Windows.Changes;
using GameOptimizer.Infrastructure.Windows.NetworkReset;
using GameOptimizer.Infrastructure.Windows.PowerManagement;
using GameOptimizer.Infrastructure.Windows.ProcessTuning;
using GameOptimizer.Infrastructure.Windows.Registry;
using GameOptimizer.Infrastructure.Windows.Services;
using GameOptimizer.Infrastructure.Windows.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Infrastructure.Windows.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationPackageRepairWindowsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IApplicationPackageDiagnosticPlatform, WindowsApplicationPackageDiagnosticPlatform>();
        services.AddSingleton<IApplicationPackageRepairOwner, ApplicationPackageRepairOwner>();
        return services;
    }

    public static IServiceCollection AddManagedAutostartWindowsInfrastructure(this IServiceCollection services, string recoveryDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(recoveryDirectory))
            throw new ArgumentException("A durable autostart recovery directory is required.", nameof(recoveryDirectory));
        services.AddSingleton<IManagedAutostartPlatform, WindowsManagedAutostartPlatform>();
        services.AddSingleton(provider => new FileManagedAutostartStore(
            recoveryDirectory, provider.GetRequiredService<IPowerDataProtector>()));
        services.AddSingleton<IManagedAutostartStore>(provider => provider.GetRequiredService<FileManagedAutostartStore>());
        services.AddSingleton<IManagedAutostartConfirmationSecretProvider>(provider => provider.GetRequiredService<FileManagedAutostartStore>());
        return services;
    }
    public static IServiceCollection AddPowerManagementWindowsInfrastructure(
        this IServiceCollection services,
        string recoveryDirectory,
        Func<IServiceProvider, IPowerDataProtector>? protectorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(recoveryDirectory))
            throw new ArgumentException("A durable Power recovery directory is required.", nameof(recoveryDirectory));

        services.AddSingleton<IPowerProcessFactory, WindowsPowerProcessFactory>();
        services.AddSingleton<IPowerCfgProcessRunner, PowerCfgProcessRunner>();
        services.AddSingleton<IPowerPlatform, WindowsPowerPlatform>();
        services.AddSingleton<IManagedPowerPlanPlatform, WindowsManagedPowerPlanPlatform>();
        services.AddSingleton(provider => new FileManagedPowerPlanStore(
            recoveryDirectory,
            provider.GetRequiredService<IPowerDataProtector>()));
        services.AddSingleton<IManagedPowerPlanStore>(provider => provider.GetRequiredService<FileManagedPowerPlanStore>());
        services.AddSingleton<IPowerDataProtector>(provider =>
            protectorFactory?.Invoke(provider) ?? new DpapiPowerDataProtector());
        services.AddSingleton(provider => new FilePowerWorkflowStore(
            recoveryDirectory,
            provider.GetRequiredService<IPowerDataProtector>()));
        services.AddSingleton<IPowerWorkflowStore>(provider => provider.GetRequiredService<FilePowerWorkflowStore>());
        services.AddSingleton<IPowerConfirmationSecretProvider>(provider => provider.GetRequiredService<FilePowerWorkflowStore>());
        services.AddSingleton<IPowerConfirmationAuthority>(provider => new PowerConfirmationAuthority(
            provider.GetRequiredService<IPowerConfirmationSecretProvider>().GetConfirmationSecret()));
        return services;
    }

    public static IServiceCollection AddGameOptimizerWindowsInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<INetworkResetPlatform, WindowsNetworkResetPlatform>();
        services.AddSingleton<FileNetworkResetPlanStore>();
        services.AddSingleton<INetworkResetPlanStore>(provider =>
            provider.GetRequiredService<FileNetworkResetPlanStore>());
        services.AddSingleton<INetworkResetReceiptStore, FileNetworkResetReceiptStore>();
        services.AddSingleton<IRegistryDwordAccessor, WindowsRegistryDwordAccessor>();
        services.AddSingleton<IRegistryStringAccessor, WindowsRegistryStringAccessor>();
        services.AddSingleton<IRegistryValueSetAccessor, WindowsRegistryValueSetAccessor>();
        services.AddSingleton<IPerInterfaceRegistryAccessor, WindowsPerInterfaceRegistryAccessor>();
        services.AddSingleton<IWindowsServiceAccessor, WindowsServiceAccessor>();
        services.AddSingleton<IProcessTuningPlatform, WindowsProcessTuningPlatform>();
        services.AddSingleton<IChangeCapability>(provider => new ProcessTuningCapability(
            ProcessTuningOperation.Affinity,
            provider.GetRequiredService<IProcessTuningPlatform>()));
        services.AddSingleton<IChangeCapability>(provider => new ProcessTuningCapability(
            ProcessTuningOperation.Priority,
            provider.GetRequiredService<IProcessTuningPlatform>()));
        services.AddSingleton<IChangeCapability>(provider => new ProcessTuningCapability(
            ProcessTuningOperation.IdealProcessor,
            provider.GetRequiredService<IProcessTuningPlatform>()));
        services.AddSingleton<ProcessTuningOwner>();
        services.AddSingleton<IProcessTuningOwner>(provider => provider.GetRequiredService<ProcessTuningOwner>());
        foreach (var item in SystemMaintenanceRepairCatalog.All)
        {
            services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
                item.CapabilityId,
                item.ResourceKey));
        }
        foreach (var item in BootMemoryRepairCatalog.All)
        {
            services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
                item.CapabilityId,
                item.ResourceKey));
        }
        foreach (var item in NetworkRepairCatalog.All.Where(item => !item.UsesNetworkResetOwner))
        {
            services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
                item.CapabilityId!,
                item.ResourceKey!));
        }
        foreach (var item in CpuSchedulingRepairCatalog.All)
        {
            services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
                item.CapabilityId,
                item.ResourceKey));
        }
        foreach (var definition in ServiceCapabilityDefinitions.Optimization)
        {
            services.AddSingleton<IChangeCapability>(provider => new ServiceChangeCapability(
                definition,
                provider.GetRequiredService<IWindowsServiceAccessor>(),
                provider.GetService<TimeProvider>()));
        }
        services.AddSingleton<IScheduledTaskAccessor, WindowsScheduledTaskAccessor>();
        foreach (var definition in ScheduledTaskCapabilityDefinitions.All)
        {
            services.AddSingleton<IChangeCapability>(provider => new ScheduledTaskChangeCapability(
                definition,
                provider.GetRequiredService<IScheduledTaskAccessor>(),
                provider.GetService<TimeProvider>()));
        }
        services.AddSingleton<IMmAgentAccessor, WindowsMmAgentAccessor>();
        services.AddSingleton<IChangeCapability>(provider => new MmAgentChangeCapability(
            new MmAgentChangeCapabilityDefinition(
                "memory.disable_compression",
                "1.0.0",
                TargetMemoryCompressionEnabled: false),
            provider.GetRequiredService<IMmAgentAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IBcdEditAccessor, WindowsBcdEditAccessor>();
        foreach (var definition in BcdEditCapabilityDefinitions.All)
        {
            services.AddSingleton<IChangeCapability>(provider => new BcdEditChangeCapability(
                definition,
                provider.GetRequiredService<IBcdEditAccessor>(),
                provider.GetService<TimeProvider>()));
        }
        services.AddSingleton<IDeviceStateAccessor, WindowsDeviceStateAccessor>();
        services.AddSingleton<IHostsBackupOwner, WindowsHostsBackupOwner>();
        services.AddSingleton<GameOptimizer.Core.Services.Appx.IAppxRemovalOwner, WindowsAppxRemovalOwner>();
        foreach (var definition in DeviceStateCapabilityDefinitions.All)
        {
            services.AddSingleton<IChangeCapability>(provider => new DeviceStateChangeCapability(
                definition,
                provider.GetRequiredService<IDeviceStateAccessor>(),
                provider.GetService<TimeProvider>()));
        }
        services.AddSingleton<IChangeCapability, ShowFileExtensionsCapability>();
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "explorer.hidden_files",
                "1.0.0",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Hidden",
                1,
                ChangeRisk.LowReversible,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "privacy.tailored_experiences",
                "1.0.0",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy",
                "TailoredExperiencesWithDiagnosticDataEnabled",
                0,
                ChangeRisk.LowReversible,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "network.disable_delivery_optimization",
                "1.0.0",
                @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                "DODownloadMode",
                0,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "network.ipv6_prefer_ipv4",
                "1.0.0",
                @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters",
                "DisabledComponents",
                32,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine,
                RequiresRestart: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "system.long_paths",
                "1.0.0",
                @"SYSTEM\CurrentControlSet\Control\FileSystem",
                "LongPathsEnabled",
                1,
                ChangeRisk.LowReversible,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "system.disable_wpbt",
                "1.0.0",
                @"SYSTEM\CurrentControlSet\Control\Session Manager",
                "DisableWpbtExecution",
                1,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "explorer.end_task",
                "1.0.0",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings",
                "TaskbarEndTask",
                1,
                ChangeRisk.LowReversible,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "registry.disable_thumbnail_delay",
                "1.0.0",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "ExtendedUIHoverTime",
                1,
                ChangeRisk.LowReversible,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "gaming.mode.enable",
                "1.0.0",
                @"SOFTWARE\Microsoft\GameBar",
                "AllowAutoGameMode",
                1,
                ChangeRisk.LowReversible,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            RepairRegistryCapabilityDefinitions.GameModeAutoEnable,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            RepairRegistryCapabilityDefinitions.HardwareAccelerationReset,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            RepairRegistryCapabilityDefinitions.FeedbackReset,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            RepairRegistryCapabilityDefinitions.GameDvrEnabled,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            RepairRegistryCapabilityDefinitions.GameDvrAppCapture,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            RepairRegistryCapabilityDefinitions.HagsEnable,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            RepairRegistryCapabilityDefinitions.DiagnosticDataReset,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            RepairRegistryCapabilityDefinitions.FastStartupEnable,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.TelemetryServiceReset,
            "service:DiagTrack"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.MemoryCompressionEnable,
            "memory:compression"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.HibernationEnable,
            "power:hibernation"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.PowerPlansReset,
            "power:machine:schemes"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.HighPerformancePlanRestore,
            "power:machine:schemes"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.UltimatePerformancePlanRestore,
            "power:machine:schemes"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.SleepPolicyReset,
            "power:machine:sleep-policy"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.CoreParkingReset,
            "power:machine:processor-policy"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.CpuFrequencyReset,
            "power:machine:processor-policy"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.BatteryReportGenerate,
            "power:machine:battery-report"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.PowerTroubleshooterLaunch,
            "power:machine:troubleshooter"));
        foreach (var definition in ServiceCapabilityDefinitions.Repair)
        {
            services.AddSingleton<IChangeCapability>(provider => new ServiceChangeCapability(
                definition,
                provider.GetRequiredService<IWindowsServiceAccessor>(),
                provider.GetService<TimeProvider>()));
        }
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.ServicesReset,
            "service:critical-batch"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.BitsEnsureRunning,
            "service:bits"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.CryptSvcEnsureRunning,
            "service:cryptsvc"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.ShellHardwareDetectionEnsureRunning,
            "service:shellhwdetection"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.PlugPlayEnsureRunning,
            "service:plugplay"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.NetworkListEnsureRunning,
            "service:netprofm"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.NetworkLocationEnsureRunning,
            "service:nlasvc"));
        services.AddSingleton<IChangeCapability>(_ => new UnavailableRepairPlatformCapability(
            RepairChangeCapabilityIds.RpcSsEnsureRunning,
            "service:rpcss"));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueDeleteCapability(
            RepairRegistryCapabilityDefinitions.DpiReset,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueDeleteCapability(
            RepairRegistryCapabilityDefinitions.VsyncReset,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        foreach (var definition in OptimizationRegistryDwordCapabilityDefinitions.CurrentBatch)
        {
            services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
                definition,
                provider.GetRequiredService<IRegistryDwordAccessor>(),
                provider.GetService<TimeProvider>()));
        }
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableClipboardHistory,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableFocusAssist,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableAudioEnhancements,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableSpatialSound,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableCommDucking,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableCeipSqmClient,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableCeipSqmPolicy,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableFeedbackSiufPeriod,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableFeedbackNotificationPolicy,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.OptimizeMouseQueueSize,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.OptimizeKeyboardQueueSize,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableGameModeOptimizations,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableUac,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableSecurityNotifications,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableWindowsUpdate,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableVbs,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableHvci,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableCfg,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableDefenderRealtime,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableSehop,
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "edge.disable_game_assist",
                "1.0.0",
                @"Software\Microsoft\Edge\GameAssist",
                "Enabled",
                0,
                ChangeRisk.LowReversible,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "basic.disable_game_bar",
                "1.0.0",
                @"SOFTWARE\Microsoft\GameBar",
                "ShowStartupPanel",
                0,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "basic.disable_uwp_background",
                "1.0.0",
                @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
                "GlobalUserDisabled",
                1,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "privacy.hide_most_used_apps",
                "1.0.0",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Start_TrackProgs",
                0,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "privacy.disable_website_access_language_list",
                "1.0.0",
                @"Control Panel\International\User Profile",
                "HttpAcceptLanguageOptOut",
                1,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "peripheral.anti_mistouch",
                "1.0.0",
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer",
                "NoWinKeys",
                1,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "basic.disable_edge_startup",
                "1.0.0",
                @"SOFTWARE\Policies\Microsoft\Edge",
                "StartupBoostEnabled",
                0,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "basic.disable_map_auto_update",
                "1.0.0",
                @"SOFTWARE\Policies\Microsoft\Windows\Maps",
                "AutoDownloadAndUpdateMapData",
                0,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "registry.disable_startup_sound",
                "1.0.0",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                "DisableStartupSound",
                1,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "edge.disable_game_assist_policy",
                "1.0.0",
                @"SOFTWARE\Policies\Microsoft\Edge",
                "GameAssistEnabled",
                0,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider =>
            InputDeviceApplicabilityCapabilities.ForPen(
                new RegistryDwordCapability(
                    new RegistryDwordCapabilityDefinition(
                        "input.disable_pen_feedback",
                        "1.0.0",
                        @"Software\Microsoft\TabletTip\1.7",
                        "PenVisualization",
                        0,
                        ChangeRisk.MediumRebootOrExperience,
                        RequiresExplicitConfirmation: true),
                    provider.GetRequiredService<IRegistryDwordAccessor>(),
                    provider.GetService<TimeProvider>())));
        services.AddSingleton<IChangeCapability>(provider => new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "registry.disable_low_disk_check",
                "1.0.0",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer",
                "NoLowDiskSpaceChecks",
                1,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true),
            provider.GetRequiredService<IRegistryDwordAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DisablePointerTrails,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DisableSnapToDefaultButton,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppCalendarAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppContactsAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppTasksAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppNotificationsAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.LocationConsent,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.WebcamConsent,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.MicrophoneConsent,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppRadioAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppMessagingAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppEmailAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppAccountInfoAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppDiagnosticInfoAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppPhoneCallsAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppCallHistoryAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppTrustedDevicesAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppMotionAccess,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.PrioritizeGames,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.OptimizeAudioBuffer,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.SetCongestionCtcp,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.ForceHighPerformanceGpu,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.SetSfioPriority,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DisableSmartScreen,
            provider.GetRequiredService<IRegistryStringAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.KeyboardResponse,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.MousePrecision,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider =>
            InputDeviceApplicabilityCapabilities.ForTouch(
                new RegistryValueSetCapability(
                    RegistryValueSetCapabilityDefinitions.DisableTouchFeedback,
                    provider.GetRequiredService<IRegistryValueSetAccessor>(),
                    provider.GetService<TimeProvider>())));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableHandwritingCollection,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableAdvertisingId,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider =>
            AccessibilityChangeCapabilities.CreateStickyKeys(provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider =>
            AccessibilityChangeCapabilities.CreateFilterKeys(provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider =>
            AccessibilityChangeCapabilities.CreateToggleKeys(provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableWindowsTips,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableSuggestedApps,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableFullscreenOptimizations,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.OptimizeDns,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableCloudContent,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableAppSync,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableTimeline,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.OptimizeAfdParameters,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableNetworkThrottling,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.OptimizeSampleRate,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableDwmAnimations,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableSpectreMeltdown,
            provider.GetRequiredService<IRegistryValueSetAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.DisableNetBios,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.DisableNagle,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.DisableAdapterPowerSaving,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.DisableFlowControl,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.DisableInterruptModeration,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.EnableLso,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.OptimizeAdapterBuffers,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.OptimizeChecksumOffload,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IChangeCapability>(provider => new PerInterfaceRegistryCapability(
            PerInterfaceRegistryCapabilityDefinitions.AmdRadeonAntiLag,
            provider.GetRequiredService<IPerInterfaceRegistryAccessor>(),
            provider.GetService<TimeProvider>()));

        // Typed power-setting + scheme-switch capabilities (fail-closed power migration).
        // Scheme switching and the editor owner intentionally share the same machine-global
        // active-scheme resource key, so the orchestrator serializes both entry points.
        services.AddSingleton<IPowerSettingAccessor, WindowsPowerSettingAccessor>();
        services.AddSingleton<IPowerSettingCapabilityGuard, WindowsPowerSettingCapabilityGuard>();
        foreach (var definition in PowerSettingCapabilityDefinitions.All)
        {
            services.AddSingleton<IChangeCapability>(provider => new PowerSettingChangeCapability(
                definition,
                provider.GetRequiredService<IPowerSettingAccessor>(),
                provider.GetRequiredService<IPowerSettingCapabilityGuard>(),
                provider.GetService<TimeProvider>()));
        }
        services.AddSingleton<IPowerSchemeAccessor, WindowsPowerSchemeAccessor>();
        services.AddSingleton<WindowsPowerSchemeEnvironmentProvider>();
        services.AddSingleton<IPowerPlatformFactsProvider>(provider => provider.GetRequiredService<WindowsPowerSchemeEnvironmentProvider>());
        services.AddSingleton<IPowerSchemeCatalog, WindowsPowerSchemeCatalog>();
        // Read/prepare-only profile coordination.  It intentionally exposes no profile execute
        // entry point: any future batch write must keep explicit confirmation and per-item receipts.
        services.AddSingleton<PowerProfilePlanner>();
        services.AddSingleton<IPowerProfilePreparationService, PowerProfilePreparationService>();
        services.AddSingleton<IChangeCapability>(provider => new PowerSchemeChangeCapability(
            PowerSettingCapabilityDefinitions.EnableHighPerformanceMode,
            provider.GetRequiredService<IPowerSchemeAccessor>(),
            provider.GetService<TimeProvider>(),
            factsProvider: provider.GetRequiredService<IPowerPlatformFactsProvider>(),
            catalog: provider.GetRequiredService<IPowerSchemeCatalog>()));
        services.AddSingleton<FileChangeStateStore>();
        services.AddSingleton<IRecoveryRepository>(provider => provider.GetRequiredService<FileChangeStateStore>());
        services.AddSingleton<IChangeJournal>(provider => provider.GetRequiredService<FileChangeStateStore>());
        services.AddSingleton<IChangeAuditStore>(provider => provider.GetRequiredService<FileChangeStateStore>());
        services.AddSingleton<IChangePlanStore>(provider => provider.GetRequiredService<FileChangeStateStore>());
        services.AddSingleton<IChangeResourceLockProvider, FileChangeResourceLockProvider>();
        return services;
    }
}
