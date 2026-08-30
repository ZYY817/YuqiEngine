using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Events;
using GameOptimizer.Core.Extensions;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Modules;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Profiles;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Core.Services.Startup;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Appx;
using GameOptimizer.Core.Services.CpuAffinity;
using GameOptimizer.Core.Services.ProcessTuning;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Snapshot;
using GameOptimizer.Core.Services.SystemCleanup;
using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Core.Theming;
using GameOptimizer.Core.Utilities;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.DeviceRepair;
using GameOptimizer.Infrastructure.Windows.MaintenanceDiagnostics;
using GameOptimizer.Infrastructure.Windows.PowerManagement;
using GameOptimizer.Infrastructure.Windows.StorageSafety;
using GameOptimizer.Infrastructure.Windows.SystemRestore;
using GameOptimizer.Modules;
using GameOptimizer.Modules.PerformanceHealth;
using GameOptimizer.ModuleContracts.Abstractions;
using GameOptimizer.Shell.ViewModels;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Services.Baseline;
using GameOptimizer.Shell.Services.PowerManagement;
using GameOptimizer.Shell.Extensions;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Navigation;
using GameOptimizer.Shell.Views;
using GameOptimizer.Core.Services.NetworkReset;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell;

public partial class App : Application
{
    private IHost? _host;
    private IDiagnosticLogService? _diagnosticLogService;
    private Startup.StartupOrchestrator? _orchestrator;
    
    /// <summary>
    /// 获取服务提供者
    /// </summary>
    public IServiceProvider? Services => _host?.Services;

    /// <summary>
    /// 应用固定的深色资源。应用不再提供浅色或跟随系统主题。
    /// </summary>
    public void ApplyDarkTheme()
    {
        var newTheme = new ResourceDictionary { Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative) };
        
        // 移除旧主题（第一个是主题字典）
        if (Resources.MergedDictionaries.Count > 0)
        {
            var first = Resources.MergedDictionaries[0];
            if (first.Source?.OriginalString.Contains("Theme") == true)
            {
                Resources.MergedDictionaries.RemoveAt(0);
            }
        }
        
        // 插入新主题到最前面
        Resources.MergedDictionaries.Insert(0, newTheme);
        
        // 同步 Core 调色板，避免 WPF 资源与服务层状态分离。
        var themeService = Services?.GetService<IThemeService>();
        themeService?.ApplyDarkPalette();
    }

    // File: GameOptimizer.Shell/App.xaml.cs
    protected override async void OnStartup(StartupEventArgs e)
    {
        var isRecoveryDevelopmentInstance = e.Args.Any(argument =>
            argument.Equals("--recovery-dev", StringComparison.OrdinalIgnoreCase));
        var isAcceptanceUiInstance = e.Args.Any(argument =>
            argument.Equals("--acceptance-ui", StringComparison.OrdinalIgnoreCase));
        var isAutostartInstance = e.Args.Any(argument =>
            argument.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        var acceptancePageKey = e.Args
            .FirstOrDefault(argument => argument.StartsWith("--acceptance-page=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        var acceptanceNavigationSequence = e.Args
            .FirstOrDefault(argument => argument.StartsWith("--acceptance-navigation-sequence=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var acceptanceDialogKey = e.Args
            .FirstOrDefault(argument => argument.StartsWith("--acceptance-dialog=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        var acceptanceScreenshotPath = e.Args
            .FirstOrDefault(argument => argument.StartsWith("--acceptance-screenshot=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        var acceptanceLanguage = e.Args
            .FirstOrDefault(argument => argument.StartsWith("--acceptance-language=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        var acceptanceTelemetryUnavailable = isAcceptanceUiInstance && e.Args.Any(argument =>
            argument.Equals("--acceptance-telemetry=unavailable", StringComparison.OrdinalIgnoreCase));
        var acceptanceAction = e.Args
            .FirstOrDefault(argument => argument.StartsWith("--acceptance-action=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        var acceptanceWidth = GetAcceptanceDimension(e.Args, "--acceptance-width=", 1200, 3840);
        var acceptanceHeight = GetAcceptanceDimension(e.Args, "--acceptance-height=", 700, 2160);
        // This opt-in probe is deliberately limited to the named tool-page content scroller.
        // It exists only to capture lower visual regions; it cannot invoke page commands or change state.
        var acceptanceScrollOffset = GetAcceptanceDimension(e.Args, "--acceptance-scroll-y=", 0, 100000);
        var acceptanceSessionId = GetAcceptanceSessionId(e.Args);
        if (isAcceptanceUiInstance)
        {
            Environment.SetEnvironmentVariable(
                "YUQIENGINE_DATA_ROOT",
                Path.Combine(AppPaths.PreferredAppDataRoot, "Acceptance", acceptanceSessionId));
            Environment.SetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_MODE", "1");
        }
        else if (isRecoveryDevelopmentInstance)
        {
            Environment.SetEnvironmentVariable(
                "YUQIENGINE_DATA_ROOT",
                Path.Combine(AppPaths.PreferredAppDataRoot, "RecoveryDevelopment"));
        }

        // BUG-006 + D6: 全局异常处理必须在最早期注册
        try
        {
            RegisterGlobalExceptionHandlers();
        }
        catch (Exception regEx)
        {
            DispatcherUnhandledException += (s, args) => { args.Handled = true; };
            Debug.WriteLine($"[App] RegisterGlobalExceptionHandlers failed: {regEx.Message}");
        }

        // 创建启动编排器（在 DI 之前，零依赖）
        _orchestrator = new Startup.StartupOrchestrator(e.Args);

        // 单实例检测
        if (!_orchestrator.AcquireMutex())
        {
            Shutdown(0);
            return;
        }

        // 用户要求：默认后台启动，不显示启动小弹窗（Splash）
        // 保留 StartupOrchestrator 其余能力（阶段执行/遥测/互斥锁）。

        try
        {
            // Stage: 环境准备
            _orchestrator.RunStage("环境准备", () =>
            {
                CleanupTempFiles();
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            });

            // Stage: 目录迁移
            _orchestrator.RunStage("目录迁移", () =>
            {
                try
                {
                    // Acceptance UI must not import production files or touch legacy registry keys.
                    // Historical C-drive data remains in place until the user explicitly approves migration.
                    AppPaths.EnsureDirectoriesExist();
                    var startupSettings = AppSettings.Load();
                    if (!AppPaths.TryConfigureLogsDirectory(startupSettings.LogBackupDirectory, out var logDirectoryError))
                    {
                        Debug.WriteLine($"[App] Configured log directory unavailable; using default: {logDirectoryError}");
                    }
                    Directory.CreateDirectory(AppPaths.LogsDir);
                    var (success, errors) = AppPaths.ValidateDirectories();
                    if (!success)
                    {
                        Debug.WriteLine($"[App] Directory validation warnings: {string.Join("; ", errors)}");
                        _orchestrator.ShowError(string.Join("\n", errors));
                    }
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    Debug.WriteLine($"[App] Unauthorized access during directory initialization: {uaEx.Message}");
                    var friendlyError = UserFriendlyErrorMapper.GetFriendlyMessage(uaEx, LocalizationService.Instance.Get("App.Startup.InitDirContext"));
                    _orchestrator.ShowError(friendlyError);
                }
                catch (IOException ioEx)
                {
                    Debug.WriteLine($"[App] IO error during directory initialization: {ioEx.Message}");
                    var friendlyError = UserFriendlyErrorMapper.GetFriendlyMessage(ioEx, LocalizationService.Instance.Get("App.Startup.InitDirContext"));
                    _orchestrator.ShowError(friendlyError);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Failed to initialize directories: {ex.Message}");
                    var friendlyError = UserFriendlyErrorMapper.GetFriendlyMessage(ex, LocalizationService.Instance.Get("App.Startup.InitDirContext"));
                    _orchestrator.ShowError(friendlyError);
                }
            });

            base.OnStartup(e);

            // Stage: 核心服务初始化（DI 容器 + Host.Start）
            _orchestrator.RunStage("核心服务初始化", () =>
            {
                try
                {
                    _host = Host.CreateDefaultBuilder()
                        .ConfigureServices(services =>
                        {
                            services.AddGameOptimizerWindowsInfrastructure();
                            services.AddMemoryCleaningWindowsInfrastructure();
                            services.AddSystemRestorePointWindowsInfrastructure(Path.Combine(
                                AppPaths.AppDataRoot, "Recovery", "SystemRestore"));
                            var powerRecoveryDirectory = Path.Combine(
                                AppPaths.AppDataRoot, "Recovery", "Power");
                            services.AddPowerManagementWindowsInfrastructure(powerRecoveryDirectory);
                            services.AddManagedAutostartWindowsInfrastructure(Path.Combine(
                                AppPaths.AppDataRoot, "Recovery", "Autostart"));
                            services.AddApplicationPackageRepairWindowsInfrastructure();
                            services.AddDeviceRepairWindowsInfrastructure();
                            services.AddMaintenanceDiagnosticWindowsInfrastructure();
                            services.AddStorageFeatureWindowsInfrastructure();
                            services.AddGameOptimizerCore();
                            if (acceptanceTelemetryUnavailable)
                            {
                                services.AddSingleton<IHomeTelemetrySource, AcceptanceUnavailableHomeTelemetrySource>();
                            }
                            else
                            {
                                services.AddSingleton<IHomeTelemetrySource>(provider =>
                                    provider.GetRequiredService<ISystemInsightService>() as IHomeTelemetrySource
                                    ?? throw new InvalidOperationException("The configured system insight service must also provide home telemetry."));
                            }
                            services.AddSingleton<IActiveOperationRegistry, ActiveOperationRegistry>();
                            services.AddSingleton<IChangeConfirmationPrompt, WpfChangeConfirmationPrompt>();
                            services.AddModule<PerformanceHealthModule>();
                            ModuleLoader.RegisterConfiguredModules(services);
                            services.AddSingleton<IUserPrivilegeService, UserPrivilegeService>();
                            services.AddSingleton<ISystemRestorePointConfirmationPrompt, WpfSystemRestorePointConfirmationPrompt>();
                            services.AddSingleton<ISystemRestoreService, SystemRestoreService>();
                            services.AddSingleton<LogRetentionService>();
                            services.AddSingleton<ISystemTrayService, SystemTrayService>();
                            services.AddSingleton<BaselineSafetyStateStore>();
                            services.AddSingleton<BaselineRuntimeStateStore>();
                            services.AddSingleton<FirstProtectionPreferenceStore>();
                            services.AddSingleton<IBaselineRiskAcknowledgementPrompt, WpfBaselineRiskAcknowledgementPrompt>();
                            services.AddSingleton<IBaselineGateService, BaselineGateService>();
                            services.AddSingleton<IPowerBaselineGate, BaselinePowerGate>();
                            services.AddSingleton<IPowerManagementOwner, PowerManagementOwner>();
                            services.AddSingleton<IManagedAutostartGate, BaselineManagedAutostartGate>();
                            services.AddSingleton<ManagedAutostartOwner>();
                            services.AddSingleton<IStartupEntryOwner>(provider => provider.GetRequiredService<ManagedAutostartOwner>());
                            services.AddSingleton<IScheduledTaskOwner>(provider => provider.GetRequiredService<ManagedAutostartOwner>());
                            services.AddSingleton<IStartupService, StartupService>();
                            services.AddSingleton<ISnapshotRestoreAuthorizationGate, BaselineSnapshotRestoreAuthorizationGate>();
                            services.AddSingleton<ICleanupAuthorizationGate, BaselineCleanupAuthorizationGate>();
                            services.AddSingleton<ICleanupQuarantine, FileSystemCleanupQuarantine>();
                            services.AddSingleton<ICleanupQuarantineManager, CleanupQuarantineManager>();
                            services.AddSingleton<ISystemCleanupService, SystemCleanupService>();
                            services.AddSingleton<SystemCleanupViewModel>();
                            services.AddSingleton<IAppxUserRemovalService, AppxUserRemovalService>();
                            services.AddSingleton<AppxCleanupViewModel>(provider => new AppxCleanupViewModel(
                                provider.GetRequiredService<IAppxRemovalOwner>(),
                                provider.GetRequiredService<IAppxUserRemovalService>(),
                                request => DangerousOperationDialog.Show(
                                    Application.Current.MainWindow,
                                    request.Title,
                                    request.Message,
                                    request.Consequences,
                                    confirmationKeyword: null,
                                    countdownSeconds: 5),
                                provider.GetRequiredService<IActiveOperationRegistry>()));
                            services.AddSingleton<AppxCleanupPage>(provider =>
                                new AppxCleanupPage(provider.GetRequiredService<AppxCleanupViewModel>()));
                            services.AddSingleton<IRepairExecutionGate, BaselineRepairExecutionGate>();
                            services.AddSingleton<IChangeExecutionGate, BaselineChangeExecutionGate>();
                            services.AddSingleton<IRepairChangeExecutor, OrchestratedRepairChangeExecutor>();
                            services.AddSingleton<IBaselineDomainCollector, BaselineDomainCollector>();
                            services.AddSingleton<IBaselineIntegrityService, BaselineIntegrityService>();
                            services.AddSingleton<IBaselineManualGuideService, BaselineManualGuideService>();
                            services.AddSingleton<IBaselineCatalogService, BaselineCatalogService>();
                            services.AddSingleton<IBaselineRestoreScoringService, BaselineRestoreScoringService>();
                            services.AddSingleton<IBaselineRestoreOrchestrator, BaselineRestoreOrchestrator>();
                            services.AddSingleton<IFirstRunBaselineOrchestrator, FirstRunBaselineOrchestrator>();
                            services.AddNetworkResetComposition(() => Current?.MainWindow);
                            services.AddSingleton<IGameLibraryScannerService, GameLibraryScannerService>();
                            services.AddSingleton<ISystemCompatibilityService, SystemCompatibilityService>();
                            services.AddSingleton<IHardwareDetectionService, HardwareDetectionService>();
                            services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
                            services.AddSingleton<INetworkSpeedTestService, NetworkSpeedTestService>();
                            services.AddSingleton<IPowerPlanEditorService, PowerPlanEditorService>();
                            services.AddSingleton<IConfigurationExportService, ConfigurationExportService>();
                            services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
                            services.AddSingleton<IDiagnosticLogService>(provider =>
                                new DiagnosticLogService(
                                    provider.GetRequiredService<IAppSettingsStore>().Current.LogBackupDirectory));
                            services.AddSingleton<DiagnosticPackageService>();
                            services.AddSingleton<ISoundNotificationService, SoundNotificationService>();
                            services.AddSingleton<SystemMonitorService>();
                            services.AddSingleton<WindowVisibilityGuard>();
                            services.AddSingleton<CpuTopologyService>();
                            services.AddSingleton<IManagedPowerPlanService, ManagedPowerPlanService>();
                            services.AddSingleton<CoreAffinityService>();
                            services.AddSingleton<GameCoreGuardService>();
                            services.AddSingleton<CoreAcceleratorViewModel>();
                            services.AddSingleton<CpuCoreManagementViewModel>();
                            services.AddSingleton<Extensions.MemoryModuleExtension>();
                            services.AddSingleton<Func<IModuleWidget, object>>(provider =>
                                widget => widget.BuildContent(provider));
                            services.AddSingleton<OverviewPage>();
                            services.AddSingleton<MemoryOptimizationPage>();
                            services.AddSingleton<AudioOptimizationPage>();
                            services.AddSingleton<PerformanceBoostPage>();
                            services.AddSingleton<CpuOptimizationPage>();
                            services.AddSingleton<GraphicsOptimizationPage>();
                            services.AddSingleton<NetworkOptimizationPage>();
                            services.AddSingleton<PrivacyShieldPage>();
                            services.AddSingleton<SystemCleanupPage>();
                            services.AddSingleton<PeripheralOptimizationPage>();
                            services.AddSingleton<EsportsModePage>();
                            services.AddSingleton<HardwareInfoPage>(provider =>
                                new HardwareInfoPage(provider.GetRequiredService<IHardwareDetectionService>()));
                            services.AddSingleton<NetworkTestPage>(provider =>
                                new NetworkTestPage(
                                    provider.GetRequiredService<INetworkResetOwner>(),
                                    provider.GetRequiredService<INetworkSpeedTestService>()));
                            services.AddSingleton<RestorePointPage>(provider =>
                                new RestorePointPage(provider.GetRequiredService<RestorePointViewModel>()));
                            services.AddSingleton<RepairCenterPage>(provider =>
                                new RepairCenterPage(
                                    provider.GetRequiredService<IRepairService>(),
                                    provider.GetRequiredService<ISystemScanService>(),
                                    provider.GetRequiredService<IActiveOperationRegistry>()));
                            services.AddSingleton<BackupManagerViewModel>(provider => new BackupManagerViewModel(
                                provider.GetRequiredService<ISnapshotService>(),
                                provider.GetService<IEventBus>(),
                                diagnosticService: null,
                                repairService: null,
                                provider.GetService<IBaselineCatalogService>(),
                                provider.GetService<IBaselineRestoreOrchestrator>(),
                                provider.GetRequiredService<ISystemRestoreService>(),
                                provider.GetRequiredService<IComprehensiveBackupService>()));
                            services.AddSingleton<BackupManagerPage>(provider =>
                                new BackupManagerPage(provider.GetRequiredService<BackupManagerViewModel>()));
                            services.AddSingleton<CpuAffinityPage>(provider =>
                                new CpuAffinityPage(
                                    provider.GetRequiredService<IProcessAffinityService>(),
                                    provider.GetRequiredService<IProcessTuningOwner>()));
                            services.AddSingleton<CoreAcceleratorPage>(provider =>
                                new CoreAcceleratorPage(provider.GetRequiredService<CoreAcceleratorViewModel>()));
                            services.AddSingleton<CpuCoreManagementPage>(provider =>
                                new CpuCoreManagementPage(provider.GetRequiredService<CpuCoreManagementViewModel>()));
                            services.AddSingleton<PowerPlanEditorPage>(provider =>
                                new PowerPlanEditorPage(
                                    provider.GetRequiredService<IPowerPlanEditorService>(),
                                    provider.GetRequiredService<IPowerManagementOwner>(),
                                    provider.GetRequiredService<IManagedPowerPlanService>()));
                            services.AddSingleton<SettingsPage>(provider =>
                                new SettingsPage(
                                    provider.GetRequiredService<IAppSettingsStore>(),
                                    provider.GetRequiredService<IUpdateCheckService>(),
                                    provider.GetRequiredService<IConfigurationExportService>(),
                                    provider.GetRequiredService<DiagnosticPackageService>(),
                                    () => provider.GetRequiredService<IStartupEntryOwner>()));
                            services.AddSingleton<StartupManagerPage>(provider =>
                                new StartupManagerPage(
                                    provider.GetRequiredService<IStartupService>(),
                                    provider.GetRequiredService<IStartupEntryOwner>()));
                            services.AddSingleton<ModulePage>();
                            // system/explorer/tasks/services 四个路由共用此模板页；
                            // 必须 transient：PageFactory 按路由缓存独立实例并设置各自 ModuleKey，
                            // 单例会导致四个模块互相覆盖 ModuleKey。
                            services.AddTransient<MasterModuleTemplate>();
                            services.AddSingleton<INavigationCatalog, NavigationCatalog>();
                            services.AddSingleton<IPageFactory, PageFactory>();
                            services.AddSingleton<INavigationRouter, NavigationRouter>();
                            services.AddSingleton<DashboardViewModel>();
                            services.AddSingleton<RestorePointViewModel>();
                            services.AddSingleton<ISystemRestoreManualLauncher, WindowsSystemRestoreManualLauncher>();
                            services.AddSingleton<IDashboardTelemetryService, DashboardTelemetryService>();
                            services.AddSingleton<MainWindow>();
                        })
                        .Build();

                    RegisterModuleExtensions();
                    _host.Start();
                }
                catch (Exception hostEx)
                {
                    Debug.WriteLine($"[App] DI container build/start failed: {hostEx}");
                    var loc = LocalizationService.Instance;
                    _orchestrator.ShowError(
                        loc.Get("App.Startup.CriticalError", hostEx.Message, hostEx.GetType().Name),
                        onRetry: null,
                        onExit: () => Shutdown(1));
                    // Why: ShowError 显示了按钮，等用户点击退出；此处 return 防止后续代码执行
                    return;
                }
            });

            // DI 构建失败时 RunStage 内部已 return，但 _host 仍可能为 null
            if (_host is null)
            {
                Shutdown(1);
                return;
            }

            SafeServiceProvider.Initialize(_host.Services);
            _diagnosticLogService = _host.Services.GetRequiredService<IDiagnosticLogService>();
            _host.Services.GetRequiredService<LogRetentionService>().ApplyRetentionPolicy();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            if (isAcceptanceUiInstance)
            {
                mainWindow.Title = "宇奇引擎（视觉验收实例 · 隔离配置）";
                if (acceptanceWidth.HasValue) mainWindow.Width = acceptanceWidth.Value;
                if (acceptanceHeight.HasValue) mainWindow.Height = acceptanceHeight.Value;
            }
            else if (isRecoveryDevelopmentInstance)
                mainWindow.Title = "宇奇引擎（恢复开发版）";

            // 应用保存的主题和语言
            var settings = AppSettings.Load();
            if (isAcceptanceUiInstance)
            {
                if (!string.IsNullOrWhiteSpace(acceptanceLanguage)) settings.Language = acceptanceLanguage;
                settings.Save();
                _host.Services.GetRequiredService<IAppSettingsStore>().TryUpdate(current =>
                {
                    current.Language = settings.Language;
                });
            }
            // 深色主题是唯一主题，旧配置和验收参数均不能改变它。
            ApplyDarkTheme();
            LocalizationService.Instance.SetLanguage(isAcceptanceUiInstance ? acceptanceLanguage ?? settings.Language : settings.Language);
            // Recovery development builds start minimized so they cannot interrupt a game.
            MainWindow = mainWindow;
            if (isRecoveryDevelopmentInstance)
            {
                mainWindow.ShowInTaskbar = true;
                mainWindow.WindowState = WindowState.Minimized;
            }
            var trayService = _host.Services.GetRequiredService<ISystemTrayService>();
            trayService.Initialize(mainWindow);
            mainWindow.Show();
            if (isAutostartInstance && settings.MinimizeToTray)
            {
                trayService.MinimizeToTray();
            }
            if (isRecoveryDevelopmentInstance)
            {
                // Recovery builds must never interrupt a foreground game or full-screen app.
                // Restore this window only through an explicit taskbar interaction.
                mainWindow.ShowInTaskbar = true;
                mainWindow.Visibility = Visibility.Visible;
            }
            if (isAcceptanceUiInstance)
            {
                _diagnosticLogService?.Append(
                    LogLevel.Warning,
                    "App.AcceptanceUi",
                    "Visual acceptance instance started with isolated mutex and data root.",
                    $"Mutex={_orchestrator.MutexIdentity}; DataRoot={AppPaths.AppDataRoot}; Page={acceptancePageKey ?? "overview"}");
            }
            await _orchestrator.TransitionToMainWindowAsync(mainWindow, activateMainWindow: !isRecoveryDevelopmentInstance).ConfigureAwait(false);
            if (!isAcceptanceUiInstance && !isRecoveryDevelopmentInstance)
            {
                await ShowReleaseNoticeIfNeededAsync(mainWindow).ConfigureAwait(false);
                await ShowFirstProtectionOnboardingIfNeededAsync(mainWindow).ConfigureAwait(false);
            }
            bool isSystemCleanupScanProbe = isAcceptanceUiInstance
                && string.Equals(acceptanceAction, "system-cleanup-scan", StringComparison.OrdinalIgnoreCase);
            // A screenshot probe is bounded and isolated.  It must not wait for the
            // dashboard's unrelated telemetry/health scan before it can navigate to
            // its requested page: those probes may be slow or unavailable on a test
            // machine, while the target page can still render its own explicit state.
            // Commands that write system state are never invoked by this condition.
            bool isReadOnlyAcceptancePageProbe = isSystemCleanupScanProbe
                || (isAcceptanceUiInstance
                    && !string.IsNullOrWhiteSpace(acceptanceScreenshotPath));
            if (isAcceptanceUiInstance && !string.IsNullOrWhiteSpace(acceptancePageKey))
            {
                // Do not make an isolated visual capture wait indefinitely on unrelated
                // dashboard initialization. The normal application path still waits for
                // its own startup work; this only changes opt-in acceptance screenshots.
                if (!isReadOnlyAcceptancePageProbe)
                    await mainWindow.InitializationCompleted.ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() => mainWindow.NavigateToPage(acceptancePageKey));
            }
            if (isAcceptanceUiInstance && acceptanceNavigationSequence is { Length: > 0 })
            {
                await mainWindow.InitializationCompleted.ConfigureAwait(false);
                foreach (var routeKey in acceptanceNavigationSequence)
                {
                    await Dispatcher.InvokeAsync(() => mainWindow.NavigateToPage(routeKey));
                    await Task.Delay(40).ConfigureAwait(false);
                }
            }
            if (isAcceptanceUiInstance
                && string.Equals(acceptanceAction, "network-test", StringComparison.OrdinalIgnoreCase))
            {
                await Dispatcher.InvokeAsync(() =>
                    _host.Services.GetRequiredService<NetworkTestPage>().BeginAcceptanceNetworkTest());
            }
            if (isSystemCleanupScanProbe)
            {
                // Acceptance-only read probe: it invokes the same page view-model scan command,
                // but never opens confirmation, cleanup, quarantine, or restore flows.
                Task scanTask = await Dispatcher.InvokeAsync(() =>
                    _host.Services.GetRequiredService<SystemCleanupViewModel>().RunScannerActionAsync());
                await scanTask;
            }
            if (isAcceptanceUiInstance && !string.IsNullOrWhiteSpace(acceptanceScreenshotPath))
            {
                // Capture inside the acceptance process so elevated windows can be verified even
                // while another full-screen application covers the desktop. This path is opt-in
                // and does not change normal startup, page commands, bindings, or safety gates.
                // Page entrance animations and async view-model initialization can overlap for
                // roughly two seconds on slower machines; capture only after the visual tree settles.
                await Task.Delay(3500).ConfigureAwait(false);
                if (acceptanceScrollOffset.HasValue)
                {
                    await Dispatcher.InvokeAsync(() =>
                        ScrollAcceptanceContent(mainWindow, acceptanceScrollOffset.Value));
                    await Task.Delay(80).ConfigureAwait(false);
                }
                if (!string.IsNullOrWhiteSpace(acceptanceDialogKey))
                {
                    await Dispatcher.InvokeAsync(() => CaptureAcceptanceDialog(mainWindow, acceptanceDialogKey, acceptanceScreenshotPath));
                }
                else
                {
                    await Dispatcher.InvokeAsync(() => CaptureAcceptanceWindow(mainWindow, acceptanceScreenshotPath));
                }
                // A screenshot request is a bounded, isolated probe. Normal WPF shutdown enters
                // async host cleanup and can leave the elevated acceptance window alive for an
                // unbounded period, so terminate this opt-in process only after the PNG stream closed.
                Environment.Exit(0);
            }

            // System Restore startup reconciliation is deliberately after the UI is visible.
            // It reads/classifies durable claims only; any failure is diagnostic and cannot block the UI.
            await RunSystemRestoreStartupReconciliationAsync();

            // 写入启动遥测
            _orchestrator.WriteTelemetry(_diagnosticLogService);

            // 异步初始化模块和配置服务（不阻塞UI线程）
            await InitializeServicesAsync().ConfigureAwait(false);

            // 回到UI线程执行后续初始化
            await Dispatcher.InvokeAsync(() =>
            {
                var privilegeService = _host.Services.GetRequiredService<IUserPrivilegeService>();
                if (!privilegeService.IsAdministrator)
                {
                    Debug.WriteLine("[App] Running without administrator privileges, some features may be limited.");
                    _diagnosticLogService?.Append(LogLevel.Information, "App.Startup",
                        "Running without administrator privileges", string.Empty);
                }

                // 首次运行不再弹出教学或遮罩；用户可直接使用全部功能。

                if (IsAutoTestMode(e.Args))
                {
                    _ = RunAutoTestAndExitAsync();
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Critical startup error: {ex}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.CrashLogPath)!);
                File.AppendAllText(
                    AppPaths.CrashLogPath,
                    $"[{DateTime.Now:O}] Startup failure{Environment.NewLine}{ex}{Environment.NewLine}");
            }
            catch
            {
                // 启动失败路径不能再被日志写入失败阻断。
            }
            _orchestrator?.ShowError(
                LocalizationService.Instance.Get("App.Startup.CriticalError", ex.Message, ex.GetType().Name),
                onExit: () => Shutdown(1));
            // Why: 如果 Splash 不可用（null），回退到 MessageBox
            if (_orchestrator?.GetTelemetry() == null)
            {
                MessageBox.Show(
                    LocalizationService.Instance.Get("App.Startup.CriticalError", ex.Message, ex.GetType().Name),
                    LocalizationService.Instance.Get("App.Startup.CriticalErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }

    private async Task ShowFirstProtectionOnboardingIfNeededAsync(Window owner)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var preferences = _host.Services.GetRequiredService<FirstProtectionPreferenceStore>();
            var preference = await preferences.LoadAsync().ConfigureAwait(true);
            if (!preference.ShouldOfferOnStartup)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new FirstRunBaselineDialog(
                    _host.Services.GetRequiredService<IFirstRunBaselineOrchestrator>(),
                    _host.Services.GetRequiredService<IBaselineGateService>(),
                    _host.Services.GetRequiredService<IBaselineManualGuideService>(),
                    preferences,
                    _diagnosticLogService)
                {
                    Owner = owner
                };
                dialog.Show();
            });
        }
        catch (Exception ex)
        {
            _diagnosticLogService?.Append(
                LogLevel.Warning,
                "App.FirstProtectionOnboarding",
                "首次保护引导未能显示；软件仍可继续使用，未建立基线时执行操作会单独请求风险确认。",
                ex.Message);
        }
    }

    private async Task ShowReleaseNoticeIfNeededAsync(Window owner)
    {
        try
        {
            var settings = Models.AppSettings.Load();
            if (string.Equals(
                    settings.AcknowledgedReleaseNoticeId,
                    GameOptimizer.Shell.Services.ReleaseNoticePolicy.CurrentNoticeId,
                    StringComparison.Ordinal))
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Views.ReleaseNoticeDialog { Owner = owner };
                if (dialog.ShowDialog() == true)
                {
                    settings.AcknowledgedReleaseNoticeId = GameOptimizer.Shell.Services.ReleaseNoticePolicy.CurrentNoticeId;
                    settings.Save();
                }
            });
        }
        catch (Exception ex)
        {
            _diagnosticLogService?.Append(
                LogLevel.Warning,
                "App.ReleaseNotice",
                "测试版使用范围提醒未能显示；未将其误记为已确认。",
                ex.Message);
        }
    }

    private static string GetAcceptanceSessionId(string[] args)
    {
        var raw = args.FirstOrDefault(argument =>
            argument.StartsWith("--acceptance-session=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        if (string.IsNullOrWhiteSpace(raw))
            return "default";

        var safe = new string(raw.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_').Take(32).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "default" : safe;
    }

    private sealed class AcceptanceUnavailableHomeTelemetrySource : IHomeTelemetrySource
    {
        public HomeTelemetrySnapshot GetCurrentSnapshotWithStatus() => HomeTelemetrySnapshot.Unavailable(
            DateTimeOffset.UtcNow,
            "acceptance-only unavailable telemetry",
            TelemetryAvailability.Unavailable,
            "用于验证读取失败时的只读界面，不提供模拟数值");
    }

    private static double? GetAcceptanceDimension(string[] args, string prefix, double minimum, double maximum)
    {
        var raw = args.FirstOrDefault(argument =>
            argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        return double.TryParse(raw, System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out var value)
               && value >= minimum
               && value <= maximum
            ? value
            : null;
    }

    private static void ScrollAcceptanceContent(Window window, double requestedOffset)
    {
        var scrollViewer = FindAcceptanceScrollViewer(window, "ToolPageContentScrollViewer");
        if (scrollViewer is null)
            return;

        scrollViewer.UpdateLayout();
        scrollViewer.ScrollToVerticalOffset(Math.Clamp(requestedOffset, 0, scrollViewer.ScrollableHeight));
    }

    private static ScrollViewer? FindAcceptanceScrollViewer(DependencyObject root, string name)
    {
        if (root is ScrollViewer scrollViewer && string.Equals(scrollViewer.Name, name, StringComparison.Ordinal))
            return scrollViewer;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindAcceptanceScrollViewer(VisualTreeHelper.GetChild(root, index), name);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static void CaptureAcceptanceWindow(Window window, string requestedPath)
    {
        var outputPath = Path.GetFullPath(requestedPath);
        if (!string.Equals(Path.GetExtension(outputPath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Acceptance screenshots must use the .png extension.", nameof(requestedPath));

        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        window.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Acceptance screenshot directory is unavailable."));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static void CaptureAcceptanceDialog(Window owner, string dialogKey, string requestedPath)
    {
        Window dialog = dialogKey.Trim().ToLowerInvariant() switch
        {
            "info" => new Views.InfoDialog("Information", "A representative long message verifies wrapping, contrast, hierarchy, and keyboard-accessible actions."),
            "confirm" => new Views.ConfirmDialog("Confirm system change", "Review the requested change carefully. This acceptance dialog does not execute or persist any operation."),
            "about" => new Views.AboutDialog(),
            "about-support" => new Views.AboutDialog(focusSupportSection: true),
            "peripheral-advanced" => new Views.PeripheralAdvancedDialog(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialogKey), dialogKey, "Unknown acceptance dialog key.")
        };
        dialog.Owner = owner;
        dialog.Show();
        dialog.UpdateLayout();
        // AboutDialog 将“支持作者”的定位排入 Loaded 后的调度队列；验收截图前先清空该队列，
        // 以确保截图反映用户实际看到的首屏位置，而不是尚未完成布局时的中间状态。
        if (string.Equals(dialogKey, "about-support", StringComparison.OrdinalIgnoreCase))
        {
            dialog.Dispatcher.Invoke(
                static () => { },
                System.Windows.Threading.DispatcherPriority.ContextIdle);
            dialog.UpdateLayout();
        }
        CaptureAcceptanceWindow(dialog, requestedPath);
        dialog.Close();
    }

    private async Task RunSystemRestoreStartupReconciliationAsync()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var reconciliation = _host.Services.GetService<ISystemRestorePointStartupReconciliation>();
            if (reconciliation is null)
            {
                return;
            }

            var result = await reconciliation.RunAsync();
            var level = result.Status is
                SystemRestorePointStartupReconciliationStatus.Faulted or
                SystemRestorePointStartupReconciliationStatus.TimedOut
                || result.ManualInterventionRequired
                ? LogLevel.Warning
                : LogLevel.Information;
            _diagnosticLogService?.Append(level, "App.SystemRestore.Reconciliation", result.Message,
                $"status={result.Status}; classifications={result.Results.Count}; manual={result.ManualInterventionRequired}");
        }
        catch (Exception ex)
        {
            // This is a non-blocking startup diagnostic. The UI is already visible and remains usable.
            Debug.WriteLine($"[App] System Restore startup reconciliation failed closed: {ex.Message}");
            _diagnosticLogService?.Append(
                LogLevel.Warning,
                "App.SystemRestore.Reconciliation",
                "System Restore startup reconciliation failed closed; the UI remains available.",
                ex.ToString());
        }
    }

    private async Task InitializeServicesAsync()
    {
        // D8: _host 可能为 null（DI 构建失败后不应到达此处，但防御性检查）
        if (_host is null)
        {
            Debug.WriteLine("[App] InitializeServicesAsync skipped: _host is null");
            return;
        }
        
        try
        {
            GameOptimizer.Core.Extensions.ServiceCollectionExtensions.InitializePowerOperationDiagnostics(_host.Services);
            
            var moduleHost = _host.Services.GetRequiredService<ModuleHost>();
            await moduleHost.InitializeAsync().ConfigureAwait(false);

            var profileService = _host.Services.GetRequiredService<OptimizationProfileService>();
            await profileService.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Service initialization failed: {ex.Message}");
            _diagnosticLogService?.Append(LogLevel.Error, "App.InitializeServices", ex.Message, ex.ToString());
        }
    }

    // File: GameOptimizer.Shell/App.xaml.cs
    private void RegisterModuleExtensions()
    {
        try
        {
            var host = _host ?? throw new InvalidOperationException("Service host is not initialized.");
            var registry = ModuleExtensionRegistry.Instance;
            registry.Register(host.Services.GetRequiredService<Extensions.MemoryModuleExtension>());
            Debug.WriteLine("[App] Module extensions registered successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Failed to register module extensions: {ex.Message}");
            _diagnosticLogService?.Append(LogLevel.Warning, "App.RegisterExtensions", 
                "Failed to register module extensions", ex.Message);
        }
    }

    // 保留旧实现仅供历史兼容；启动路径不再调用首次运行教学。
    private void ShowFirstRunDialogIfNeeded(Window owner)
    {
        var settingsPath = AppPaths.FirstRunFlagPath;
        var dontShowPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(settingsPath) ?? "", "welcome_dont_show.flag");

        // 如果用户选择了"不再显示"，则跳过
        if (System.IO.File.Exists(dontShowPath)) return;
        
        // 如果已经显示过首次运行标记，也跳过
        if (System.IO.File.Exists(settingsPath)) return;

        bool shouldStartGuide = false;

        try
        {
            var dir = System.IO.Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                string? dirError = null;
                SafeDirectoryOperations.TryCreateDirectory(dir, out dirError);
            }
            SafeFileWriter.TryWriteAllText(settingsPath, DateTime.Now.ToString("O"));

            var dialog = new Views.WelcomeGuideDialog { Owner = owner };
            var result = dialog.ShowDialog();
            
            if (result == true && dialog.DontShowAgain)
            {
                try
                {
                    SafeFileWriter.TryWriteAllText(dontShowPath, DateTime.Now.ToString("O"));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Failed to write dont_show flag: {ex.Message}");
                }
            }

            if (result == true)
            {
                shouldStartGuide = true;
            }
        }
        catch (Exception ex)
        {
            // APP-004: 记录文件写入失败，但仍显示对话框
            Debug.WriteLine($"[App] Failed to write first_run.flag: {ex.Message}");
            _diagnosticLogService?.Append(LogLevel.Warning, "App.FirstRun", 
                "Failed to write first run flag", ex.Message);
            
            // 即使标记文件写入失败，仍然显示欢迎对话框
            try
            {
                var dialog = new Views.WelcomeGuideDialog { Owner = owner };
                var result = dialog.ShowDialog();
                
                if (result == true && dialog.DontShowAgain)
                {
                    // P3-001: 使用 SafeFileWriter 替代裸露的 File.WriteAllText
                    GameOptimizer.Core.Services.SafeFileWriter.TryWriteAllText(dontShowPath, DateTime.Now.ToString("O"));
                }

                if (result == true)
                {
                    shouldStartGuide = true;
                }
            }
            catch (Exception dialogEx) { Debug.WriteLine($"[App] Failed to show WelcomeGuideDialog: {dialogEx.Message}"); }
        }

        // 启动首页引导
        if (shouldStartGuide)
        {
            // 使用Dispatcher延迟执行，确保主窗口完全加载
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Task.Delay(800); // 等待主窗口完全显示和动画完成
                    
                    // 获取MainWindow的实际内容根容器
                    var rootContainer = owner.FindName("RootContainer") as FrameworkElement;
                    if (rootContainer != null)
                    {
                        await GameOptimizer.Shell.Services.UserGuideService.Instance.StartGuideAsync("overview", rootContainer);
                    }
                    else
                    {
                        Debug.WriteLine("[App] Failed to find RootContainer for guide");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Failed to start user guide: {ex.Message}");
                    _diagnosticLogService?.Append(LogLevel.Warning, "App.UserGuide", 
                        "Failed to start user guide", ex.Message);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    // Why: 全自动测试模式 — 启动应用 → 跑完测试 → 保存报告 → 自动退出
    // 通过命令行 --autotest 或环境变量 GAMEOPTIMIZER_AUTOTEST=1 触发
    // 使用反射调用 Tests 程序集，避免 Shell→Tests 的循环项目引用
    private static bool IsAutoTestMode(string[]? args)
    {
        if (args != null && args.Any(a =>
            a.Equals("--autotest", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/autotest", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (Environment.GetEnvironmentVariable("GAMEOPTIMIZER_AUTOTEST") == "1")
            return true;

        return false;
    }

    private async Task RunAutoTestAndExitAsync()
    {
        try
        {
            Debug.WriteLine("[App] 🤖 自动测试模式启动，等待服务初始化...");
            await Task.Delay(3000);

            Debug.WriteLine("[App] 🚀 开始执行全链路自动化测试...");

            // Why: Shell 不能直接引用 Tests 项目（会造成循环依赖）
            // 通过反射加载 Tests 程序集，优先调用 V2 入口，V2 不存在时回退到 V1
            var testAssembly = TryLoadTestAssembly();
            if (testAssembly == null)
            {
                Debug.WriteLine("[App] ❌ 找不到测试程序集 GameOptimizer.Tests.dll");
                Dispatcher.Invoke(() => Shutdown(2));
                return;
            }

            var testPassed = await TryLaunchV2Async(testAssembly).ConfigureAwait(false)
                          ?? await TryLaunchV1Async(testAssembly).ConfigureAwait(false);

            if (testPassed is null)
            {
                Debug.WriteLine("[App] ❌ V2 和 V1 入口均不可用");
                Dispatcher.Invoke(() => Shutdown(2));
                return;
            }

            Debug.WriteLine($"[App] 🏁 自动测试完成 — 结果: {(testPassed.Value ? "PASS" : "FAIL")}");
            Dispatcher.Invoke(() => Shutdown(testPassed.Value ? 0 : 1));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ❌ 自动测试异常: {ex.Message}");
            Dispatcher.Invoke(() => Shutdown(2));
        }
    }

    // Why: 优先使用 V2 真实交互测试引擎（基于真实 DI 容器的深度验证）
    private async Task<bool?> TryLaunchV2Async(System.Reflection.Assembly testAssembly)
    {
        var v2Type = testAssembly.GetType("GameOptimizer.Tests.Automation.V2.TestLauncherV2");
        if (v2Type is null)
        {
            Debug.WriteLine("[App] ⚠️ TestLauncherV2 类型未找到，回退到 V1");
            return null;
        }

        var launchMethod = v2Type.GetMethod("LaunchAsync",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (launchMethod is null)
        {
            Debug.WriteLine("[App] ⚠️ TestLauncherV2.LaunchAsync 方法未找到，回退到 V1");
            return null;
        }

        Debug.WriteLine("[App] 🚀 使用 V2 测试引擎");
        var taskObj = launchMethod.Invoke(null, new object?[] { _host?.Services });
        if (taskObj is Task<bool> resultTask)
            return await resultTask;

        Debug.WriteLine("[App] ⚠️ TestLauncherV2.LaunchAsync 返回类型异常，回退到 V1");
        return null;
    }

    // Why: V1 作为 fallback，保证旧版测试仍可运行
    private async Task<bool?> TryLaunchV1Async(System.Reflection.Assembly testAssembly)
    {
        var v1Type = testAssembly.GetType("GameOptimizer.Tests.Automation.TestLauncher");
        if (v1Type is null)
        {
            Debug.WriteLine("[App] ❌ TestLauncher (V1) 类型也未找到");
            return null;
        }

        var launchMethod = v1Type.GetMethod("LaunchFullTestSuiteAsync",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (launchMethod is null)
        {
            Debug.WriteLine("[App] ❌ TestLauncher.LaunchFullTestSuiteAsync 方法未找到");
            return null;
        }

        Debug.WriteLine("[App] 🚀 使用 V1 测试引擎 (fallback)");
        var taskObj = launchMethod.Invoke(null, new object?[] { _host?.Services });
        if (taskObj is Task<bool> resultTask)
            return await resultTask;

        Debug.WriteLine("[App] ❌ LaunchFullTestSuiteAsync 返回类型异常");
        return null;
    }

    private static System.Reflection.Assembly? TryLoadTestAssembly()
    {
        // 优先从已加载的程序集中查找
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "GameOptimizer.Tests");
        if (loaded != null) return loaded;

        // 尝试从 exe 同目录加载
        try
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var dllPath = Path.Combine(exeDir, "GameOptimizer.Tests.dll");
            if (File.Exists(dllPath))
            {
                Debug.WriteLine($"[App] 📦 加载测试程序集: {dllPath}");
                return System.Reflection.Assembly.LoadFrom(dllPath);
            }

            Debug.WriteLine($"[App] ⚠️ 测试程序集不存在: {dllPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ⚠️ 加载测试程序集失败: {ex.Message}");
        }

        return null;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnTaskException;

        // 释放启动编排器（Mutex + Splash 清理）
        _orchestrator?.Dispose();

        // P2-003: 清理临时文件
        CleanupTempFiles();

        if (_host is not null)
        {
            // 清理托盘图标
            var trayService = _host.Services.GetService<ISystemTrayService>();
            trayService?.Dispose();

            // NMR-13: 保护 async void OnExit 中的 StopAsync，防止未观察异常导致崩溃
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Host stop failed: {ex.Message}");
            }
            _host.Dispose();
        }
        
        // 清理静态资源锁
        OperationLock.Shutdown();
        
        // P2-017: 清理 ActivityService 事件订阅
        ActivityService.Shutdown();

        base.OnExit(e);
    }
    
    /// <summary>
    /// P2-003: 清理临时文件（权限检查产生的测试文件）
    /// </summary>
    private void CleanupTempFiles()
    {
        try
        {
            var appDataDir = AppPaths.AppDataRoot;
            if (!Directory.Exists(appDataDir))
            {
                return;
            }
            
            // 清理权限检查产生的临时文件
            var tempFiles = Directory.GetFiles(appDataDir, ".write_test_*.tmp", SearchOption.AllDirectories);
            
            foreach (var file in tempFiles)
            {
                try
                {
                    File.Delete(file);
                    Debug.WriteLine($"[App] Cleaned up temp file: {file}");
                }
                catch
                {
                    // 忽略删除失败（文件可能被占用或已删除）
                }
            }
            
            if (tempFiles.Length > 0)
            {
                Debug.WriteLine($"[App] Cleaned up {tempFiles.Length} temporary files");
            }
        }
        catch (Exception ex)
        {
            // 清理失败不影响程序运行
            Debug.WriteLine($"[App] Failed to cleanup temp files: {ex.Message}");
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
    }

    private static int _exceptionCount;
    // Why: 原来是 static readonly 直接初始化，会在类加载时触发 AppPaths 静态链，
    // 如果 %LOCALAPPDATA% 异常会抛 TypeInitializationException，比异常处理注册还早，进程直接崩溃。
    private static string? _crashLogPathCache;
    private static string CrashLogPathSafe
    {
        get
        {
            if (_crashLogPathCache != null) return _crashLogPathCache;
            try { _crashLogPathCache = AppPaths.CrashLogPath; }
            catch { _crashLogPathCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"); }
            return _crashLogPathCache;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _diagnosticLogService?.Append(LogLevel.Critical, "UI.Dispatcher", e.Exception.Message, e.Exception.ToString());
        
        // 写入崩溃日志文件
        try
        {
            var crashPath = CrashLogPathSafe;
            var dir = System.IO.Path.GetDirectoryName(crashPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] #{++_exceptionCount}\n{e.Exception}\n\n";

            // Why: 追加写入。旧实现每次异常都全量 ReadAllText+整体重写，
            // 日志涨到几十 MB 后每帧异常会把 UI 线程打满（死机放大器）。
            System.IO.File.AppendAllText(crashPath, logEntry);
        }
        catch (Exception ex) 
        { 
            Debug.WriteLine($"[App] Failed to write crash log: {ex.Message}");
            // 静默失败，不影响程序运行
        }
        
        // 只弹一次窗（注释掉，避免干扰用户）
        // if (_exceptionCount == 1)
        // {
        //     MessageBox.Show($"检测到界面异常，日志已保存到:\n{CrashLogPathSafe}", "宇奇引擎", MessageBoxButton.OK, MessageBoxImage.Error);
        // }
        
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _diagnosticLogService?.Append(LogLevel.Critical, "AppDomain", ex.Message, ex.ToString());
        }
    }

    private void OnTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _diagnosticLogService?.Append(LogLevel.Error, "TaskScheduler", e.Exception.Message, e.Exception.ToString());
        e.SetObserved();
    }
}
