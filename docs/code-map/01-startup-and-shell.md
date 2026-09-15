# 01 · 启动链路与应用外壳地图（YuqiEngine）

> 目标：把「双击 exe → 看到 MainWindow 首屏」这条链路上的每个环节落到**具体文件 + 具体类型/方法**，并显式标出**早期启动失败会被静默吞掉**的位置。
> 所有路径均为仓库相对路径（仓库根 = `YHRJ - 副本`）。

---

## 0. 一页速查

| 环节 | 文件 | 关键类型/方法 |
| --- | --- | --- |
| 真正的进程入口 | `GameOptimizer.Shell/Program.cs` | `GameOptimizer.Shell.Program.Main(string[])` |
| WPF 应用对象 | `GameOptimizer.Shell/App.xaml` + `App.xaml.cs` | `App.OnStartup(StartupEventArgs)` / `App.OnExit` |
| 启动编排（无 DI 依赖） | `GameOptimizer.Shell/Startup/StartupOrchestrator.cs` | `AcquireMutex()` / `RunStage()` / `TransitionToMainWindowAsync()` |
| DI 组合根 | `GameOptimizer.Shell/App.xaml.cs`（`OnStartup` 内联） | `Host.CreateDefaultBuilder().ConfigureServices(...)` |
| 提权状态（唯一权威判定） | `GameOptimizer.Core/Services/WindowsElevation.cs` | `Probe()` / `Current` / `IsElevated` / `IsAdministratorAccount` / `DescribeMachineScopeUnavailable()` |
| 提权状态的 UI 门面 | `GameOptimizer.Shell/Services/UserPrivilegeService.cs` | `Refresh()`（构造时一次） / `INotifyPropertyChanged` |
| 核心服务注册 | `GameOptimizer.Core/Extensions/ServiceCollectionExtensions.cs` | `AddGameOptimizerCore()` |
| Windows 平台注册 | `GameOptimizer.Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs` | `AddGameOptimizerWindowsInfrastructure()` 等 |
| 外部模块装载 | `GameOptimizer.Modules/ModuleLoader.cs` | `ModuleLoader.RegisterConfiguredModules(IServiceCollection)` |
| 导航目录 | `GameOptimizer.Shell/Navigation/NavigationCatalog.cs` | `NavigationCatalog.CreateRoutes()` |
| 页面工厂 | `GameOptimizer.Shell/Navigation/PageFactory.cs` | `PageFactory.GetOrCreate(NavigationRoute)` |
| 路由状态机 | `GameOptimizer.Shell/Navigation/NavigationRouter.cs` | `NavigationRouter.Navigate(string)` / `Navigated` |
| 外壳窗口 | `GameOptimizer.Shell/MainWindow.xaml` + `MainWindow.xaml.cs` | `MainWindow.OnLoaded` / `CreateHostMap()` / `NavigateToPage(string)` |
| 诊断日志（页面级） | `GameOptimizer.Shell/Services/PageDiagnosticService.cs` | `PageDiagnosticService.Log/LogError/LogLoaded` |

---

## 1. 进程入口：`Program.Main`

### 1.1 入口点为什么不是 WPF 默认 Main

`GameOptimizer.Shell/GameOptimizer.Shell.csproj` 中：

- `<OutputType>WinExe</OutputType>`、`<UseWPF>true</UseWPF>`
- `<AssemblyName>YuqiEngine</AssemblyName>`（最终产物 `YuqiEngine.exe`）
- `<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>`
- **`<StartupObject>GameOptimizer.Shell.Program</StartupObject>`** —— 显式指定自定义入口，`App.g.cs` 里 WPF 自动生成的 `Main` 被禁用。

因此所有命令行开关都必须在 `GameOptimizer.Shell/Program.cs` 里解析，**而不是** `App.OnStartup` 的 `e.Args`（后者只在 `app.Run()` 之后才可用）。

### 1.2 `Program.Main` 的执行顺序

`GameOptimizer.Shell/Program.cs`：

```csharp
public static int Main(string[] args)
```

1. **`ConfigureDataRootForProcess(args)`** —— 在**任何 `AppPaths` 静态成员被触碰之前**设定数据根目录，用于把验收/回放实例隔离到独立目录（`Program.cs:130–152`）。
   - `--acceptance-ui` 时：取 `--acceptance-session=` **第一个 `=` 之后**的子串（`Split('=', 2)[1]`，所以 `--acceptance-session=a=b` 得到 `a=b` 而非 `a`），再过滤为 `[A-Za-z0-9_-]`（保留 `-`/`_`，**不含 `.`**）、截断到 32 字符（空则 `default`），写入 `YUQIENGINE_DATA_ROOT = <PreferredAppDataRoot>/Acceptance/<session>`；`Program.cs` **只做数据根隔离，不设 `YUQIENGINE_ACCEPTANCE_MODE`**（该变量由 `App.OnStartup` 设置）。
   - 否则 `--recovery-dev` 时：`YUQIENGINE_DATA_ROOT = <PreferredAppDataRoot>/RecoveryDevelopment`。
2. **`--system-restore-writer` 旁路**（`Program.cs:20–26`，**在写任何诊断日志之前**）：仅当**四个条件同时成立**才短路 ——
   `args.Length == 5`、`args[0] == "--system-restore-writer"`、`args[1] == "--request"`、`args[3] == "--response"`（均为 `StringComparison.Ordinal`），
   此时**不启动 WPF**，直接 `return SystemRestorePointWriterEntryPoint.RunFiles(args[2], args[4])`（请求文件、响应文件路径）。
   实现见 `GameOptimizer.Shell/Services/SystemRestorePointWriterEntryPoint.cs`，该入口以子进程方式被调度，用于在独立进程里写系统还原点。
   这是唯一一条不进入 `App` 的启动分支；参数不满足上述形状时会**继续走正常启动流程**，而不是报错。
3. **`RunEnvironmentPreCheck()`** —— 进程级预检，失败即返回非 0 退出码（`Program.cs:69–128`）：
   - OS 版本 ≥ `10.0.19041`（不满足 → 原生弹窗 + `return false`）；
   - `AppPaths.AppDataRoot` **可写性测试**（`Directory.CreateDirectory` + 写 `.startup_test_<guid>.tmp` 再 `File.Delete`，失败 → 原生弹窗 + `return false`）；
   - 关键托管程序集 `YuqiEngine.dll` 是否存在于 exe 目录；若同目录存在 `coreclr.dll`（判定为 self-contained）则额外要求 `coreclr.dll` / `clrjit.dll` / `hostpolicy.dll`（`Program.cs:104–125`）。
   - 全部通过 → `WriteDiagnostic("Critical DLLs present")` 且返回 `true`。
4. **`LaunchWpfApp()`** —— `new App()` → `app.InitializeComponent()`（加载 `App.xaml` 资源字典）→ `app.Run()`（`Program.cs:55–64`）。
5. **最外层 `catch (Exception ex)`**（`Program.cs:44–52`）—— `WriteDiagnostic($"FATAL: {ex}")` + `ShowNativeMessageBox(...)` 后 `return 1`。这是整个进程的最后一道防线。

### 1.3 启动失败被吞掉的位置（Program 层）

| 位置 | 行为 | 后果 |
| --- | --- | --- |
| `Program.WriteDiagnostic(...)`（154–165） | `try { Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticLogPath)!); File.AppendAllText(DiagnosticLogPath, ...) } catch { }`（**空 catch**）。`DiagnosticLogPath => Path.Combine(AppPaths.LogsDir, "startup_diagnostic.log")`（第 14 行） | 预检阶段写日志失败完全无痕；若 `AppPaths.LogsDir` 不可解析/不可写，连诊断文件都不会产生 |
| `Program.ShowNativeMessageBox(...)`（171–181） | P/Invoke `MessageBoxW(IntPtr.Zero, text, caption, 0x10 /* MB_ICONERROR */)`，外围 `try { ... } catch { }`（**空 catch**） | 预检失败时如果弹窗本身失败，进程**静默退出**（返回非 0 但用户看不到任何提示） |
| `RunEnvironmentPreCheck()` 的返回值（37–38） | 只作为退出码 `1`，不写 `AppPaths.CrashLogPath`，不触发 `PageDiagnosticService` | 用户侧表现为「点了没反应」；只能靠进程退出码/`startup_diagnostic.log` 定位 |
| `--system-restore-writer` 旁路（20–26） | 短路发生在 `WriteDiagnostic("=== YuqiEngine Startup ===")` **之前**（第 30 行），且 `RunFiles` 的返回值直接作为进程退出码，**不经任何 try/catch 与日志** | 写还原点子进程失败时既无 `startup_diagnostic.log` 记录、也无弹窗；失败只能由调度方（父进程）从退出码推断 |

> 排查建议：预检类问题先看 `AppPaths.LogsDir` 下的 `startup_diagnostic.log`；若该文件根本不存在，说明失败发生在第 1 步（数据根不可写）或 `WriteDiagnostic` 自身被吞。

---

## 2. `App.OnStartup` 全链路

`GameOptimizer.Shell/App.xaml.cs` → `protected override async void OnStartup(StartupEventArgs e)`（**async void**，异常不会向外传播）。

### 2.1 阶段表（按代码执行顺序）

| # | 阶段名（代码字面量） | 关键动作 | 行号锚点（App.xaml.cs） |
| --- | --- | --- | --- |
| 0 | —（无 stage 包装） | 解析 `e.Args`：`--recovery-dev`、`--acceptance-ui`、`--autostart`、`--acceptance-page=`、`--acceptance-navigation-sequence=`、`--acceptance-dialog=`、`--acceptance-screenshot=`、`--acceptance-language=`、`--acceptance-telemetry=unavailable`、`--acceptance-action=`、`--acceptance-width/height/scroll-y`、`--acceptance-session=` | 94–126（`--acceptance-session=` 的取值由 `GetAcceptanceSessionId(string[])` 解析，`:694–705`，调用点 `:126`） |
| 0.1 | — | 验收/恢复开发实例改写环境变量：`YUQIENGINE_DATA_ROOT`（`Acceptance/<sessionId>` 或 `RecoveryDevelopment`）、`YUQIENGINE_ACCEPTANCE_MODE=1` | 127–139 |
| 0.2 | — | `RegisterGlobalExceptionHandlers()`（**必须最早**，见 §5） | 141–150 |
| 0.3 | — | `_orchestrator = new Startup.StartupOrchestrator(e.Args)`（**零 DI 依赖**，早于容器） | 153 |
| 0.4 | — | `_orchestrator.AcquireMutex()`；抢不到 → `Shutdown(0)` 并 `return` | 156–160 |
| 1 | **环境准备** | `CleanupTempFiles()`；`PresentationTraceSources.DataBindingSource.Switch.Level = Critical`；`RenderOptions.ProcessRenderMode = Default` | 168–173 |
| 2 | **目录迁移** | `AppPaths.EnsureDirectoriesExist()`；`AppSettings.Load()`；`AppPaths.TryConfigureLogsDirectory(...)`；`Directory.CreateDirectory(AppPaths.LogsDir)`；`AppPaths.ValidateDirectories()` | 176–214 |
| — | — | `base.OnStartup(e)` | 216 |
| 3 | **核心服务初始化** | `Host.CreateDefaultBuilder().ConfigureServices(...).Build()`；`RegisterModuleExtensions()`；`_host.Start()` | 219–413 |
| — | — | `if (_host is null) { Shutdown(1); return; }`（DI 失败防线） | 416–420 |
| — | — | `SafeServiceProvider.Initialize(_host.Services)`（静态服务定位器逃生舱） | 422 |
| — | — | `_diagnosticLogService = GetRequiredService<IDiagnosticLogService>()`；`LogRetentionService.ApplyRetentionPolicy()` | 423–424 |
| — | — | `GetRequiredService<MainWindow>()`；按实例类型改写标题/尺寸；`ApplyDarkTheme()`；`LocalizationService.Instance.SetLanguage(...)` | 426–449 |
| — | — | `MainWindow = mainWindow`（**赋值给 `Application.MainWindow`**）；`ISystemTrayService.Initialize(mainWindow)`；`mainWindow.Show()` | 451–459 |
| — | 就绪过渡 | `_orchestrator.TransitionToMainWindowAsync(mainWindow, activateMainWindow: !isRecoveryDevelopmentInstance)` | 479 |
| — | 可选弹窗 | `ShowReleaseNoticeIfNeededAsync`、`ShowFirstProtectionOnboardingIfNeededAsync`（**仅非验收、非恢复开发实例**） | 480–484 |
| — | 验收探针 | `acceptancePageKey`（局部变量，`:100`） / 导航序列 / `network-test` / `system-cleanup-scan` / 截图（`CaptureAcceptanceWindow` / `CaptureAcceptanceDialog`，截图后 `Environment.Exit(0)`） | 485–553 |
| — | 非阻塞诊断 | `RunSystemRestoreStartupReconciliationAsync()` | 557 |
| — | 遥测 | `_orchestrator.WriteTelemetry(_diagnosticLogService)` | 560 |
| — | 服务初始化 | `InitializeServicesAsync()`（`ModuleHost.InitializeAsync` + `OptimizationProfileService.InitializeAsync`） | 563 |
| — | 权限回读 | 回 UI 线程取 `IUserPrivilegeService.IsAdministrator`；非管理员仅 `Debug.WriteLine` + 诊断日志（**只读，不调用 `Refresh()`**） | 566–574 |
| — | 自动化测试 | `IsAutoTestMode(e.Args)` → `_ = RunAutoTestAndExitAsync()`（火忘） | 576–579 |
| — | **兜底 catch** | `Debug.WriteLine` → 写 `AppPaths.CrashLogPath`（`catch {}` 包裹）→ `_orchestrator?.ShowError(...)` → 仅当 `_orchestrator?.GetTelemetry() == null` 时再 `MessageBox.Show` + `Shutdown(1)` | 582–608 |

### 2.2 `MainWindow.Show()` 之后才发生的初始化（首屏不阻塞）

`MainWindow.OnLoaded`（`GameOptimizer.Shell/MainWindow.xaml.cs`）是首屏真正渲染的入口，顺序被刻意设计为「先可见，后重活」：

1. `Loaded -= OnLoaded`（防重入）；`PageDiagnosticService.Log("MainWindow", "=== 应用启动 ===")`
2. `_viewModel.PropertyChanged += OnViewModelPropertyChanged`（`module-detail` 内部路由的驱动源，见 §6.4）
3. `ToastService.Instance.Initialize(ToastContainer)`（**静态单例**，不走 DI）
4. `_navigationRouter.Navigate("overview")` —— **首屏页面在此刻创建并渲染**
5. `UpdateLocalizedTexts()`（`:307` 调用；管理员/普通模式文案在 `:277–281` 刷到 `AdminModeText`，`:284–291` 把 `AdminStatusDot` 染绿/橙）——**两者读的都是 `_viewModel.IsAdministrator`（`:279`/`:286`）**，即 `UserPrivilegeService` 在启动时缓存的快照（`DashboardViewModel.cs:1148`），因此侧栏的权限指示灯同样只在启动那一刻取一次值（见 §3.3 第 5 条）
6. 若**不是**验收实例：`await _viewModel.InitializeAsync()`（系统检测、游戏识别、健康扫描、模块装箱）；验收实例只打一行日志后跳过
7. `_initializationCompleted.TrySetResult()`（`Task InitializationCompleted`，供验收探针与外部等待）
8. `PageDiagnosticService.Log("MainWindow", "=== 应用启动完成 ===")`
9. 欢迎 Toast（`Toast.WelcomeBack` / `Toast.AppReady`）；不自动触发新手引导

> 设计意图与代价：`OverviewPage` 一定会先出来，即使 `DashboardViewModel.InitializeAsync()` 失败也不会白屏；代价是「首屏看起来正常，但模块列表/状态全空」这种半可用状态只体现在 `page_diagnostics.log` 里，UI 上没有一等公民的错误面。

---

## 3. DI 组合根：注册地图

组合根**唯一位置** = `App.xaml.cs` 中 `Host.CreateDefaultBuilder().ConfigureServices(services => { ... })` 的内联 lambda（lambda 体 224–396 行，`.Build()` 在 397 行），不存在 `Startup.cs`。

### 3.1 分组顺序（代码真实顺序）

| 序 | 调用 | 来源文件 |
| --- | --- | --- |
| 1 | `services.AddGameOptimizerWindowsInfrastructure()` | `GameOptimizer.Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs`（`AddGameOptimizerWindowsInfrastructure()`，函数体结束于第 844 行） |
| 2 | `services.AddMemoryCleaningWindowsInfrastructure()` | `GameOptimizer.Infrastructure.Windows/Extensions/MemoryCleaningServiceCollectionExtensions.cs` |
| 3 | `services.AddSystemRestorePointWindowsInfrastructure(<AppDataRoot>/Recovery/SystemRestore)` | `GameOptimizer.Infrastructure.Windows/SystemRestore/SystemRestorePointServiceCollectionExtensions.cs` |
| 4 | `services.AddPowerManagementWindowsInfrastructure(<AppDataRoot>/Recovery/Power)` | `GameOptimizer.Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs` |
| 5 | `services.AddManagedAutostartWindowsInfrastructure(<AppDataRoot>/Recovery/Autostart)` | 同上 |
| 6 | `services.AddApplicationPackageRepairWindowsInfrastructure()` | 同上 |
| 7 | `services.AddDeviceRepairWindowsInfrastructure()` | `.../DeviceRepair/WindowsDeviceDiagnosticPlatform.cs`（第 90 行起） |
| 8 | `services.AddMaintenanceDiagnosticWindowsInfrastructure()` | `.../MaintenanceDiagnostics/WindowsMaintenanceDiagnosticPlatform.cs`（第 151 行起） |
| 9 | `services.AddStorageFeatureWindowsInfrastructure()` | `.../StorageSafety/WindowsStorageFeatureDiagnosticPlatform.cs`（第 209 行起） |
| 10 | `services.AddGameOptimizerCore()` | `GameOptimizer.Core/Extensions/ServiceCollectionExtensions.cs` |
| 11 | `IHomeTelemetrySource`（验收不可用替身 `AcceptanceUnavailableHomeTelemetrySource` 或 `ISystemInsightService` 本身） | App.xaml.cs 240–249 |
| 12 | `IActiveOperationRegistry`、`IChangeConfirmationPrompt` | App.xaml.cs 250–251 |
| 13 | `services.AddModule<PerformanceHealthModule>()` + `ModuleLoader.RegisterConfiguredModules(services)` | `GameOptimizer.Core/Extensions/ServiceCollectionExtensions.cs` / `GameOptimizer.Modules/ModuleLoader.cs` |
| 14 | 权限/还原/托盘/基线/启动项/快照/修复/清理等 Shell 服务（`IUserPrivilegeService` 在 254） | App.xaml.cs 254–275 |
| 15 | 清理类 ViewModel 与 `AppxCleanupPage`（`SystemCleanupViewModel` 276、`AppxCleanupViewModel` 278、`AppxCleanupPage` 289） | App.xaml.cs 276–290 |
| 16 | 基线/修复/设置/硬件等 Shell 业务服务（`IGameLibraryScannerService` … `CpuTopologyService`、`CoreAcceleratorViewModel` 320、`CpuCoreManagementViewModel` 321） | App.xaml.cs 291–324 |
| 17 | **页面注册**（325–383）、`MasterModuleTemplate`（387）、导航三件套（388–390） | App.xaml.cs 325–390 |
| 18 | `DashboardViewModel`、`RestorePointViewModel`、`ISystemRestoreManualLauncher`、`IDashboardTelemetryService`、`MainWindow` | App.xaml.cs 391–395 |

> 注意：**ViewModel 并不集中在页面注册块里**。`SystemCleanupViewModel`（276）、`AppxCleanupViewModel`（278）、`CoreAcceleratorViewModel`（320）、`CpuCoreManagementViewModel`（321）分散在前面的服务块；`BackupManagerViewModel` 则在页面工厂旁边（349）。而 `CpuAffinityViewModel`、`RepairCenterViewModel`、`StartupManagerViewModel`、`GameModeHubViewModel` **完全没有 DI 注册**，分别由页面在 `OnLoaded`/ctor 内 `new`。

### 3.2 页面 / ViewModel 注册清单（App.xaml.cs 276–392）

**直接 `AddSingleton<TPage>()`（构造注入由容器解析，325–335 + 383）：**

`OverviewPage`、`MemoryOptimizationPage`、`AudioOptimizationPage`、`PerformanceBoostPage`、`CpuOptimizationPage`、`GraphicsOptimizationPage`、`NetworkOptimizationPage`、`PrivacyShieldPage`、`SystemCleanupPage`、`PeripheralOptimizationPage`、`EsportsModePage`、`ModulePage`。

**显式工厂 `AddSingleton<TPage>(provider => new TPage(...))`（把可选依赖固定下来）：**

| 页面 | 注册行 | 工厂里解析的依赖 |
| --- | --- | --- |
| `AppxCleanupPage` | 289 | `AppxCleanupViewModel` |
| `HardwareInfoPage` | 336 | `IHardwareDetectionService` |
| `NetworkTestPage` | 338 | `INetworkResetOwner`、`INetworkSpeedTestService` |
| `RestorePointPage` | 342 | `RestorePointViewModel` |
| `RepairCenterPage` | 344 | `IRepairService`、`ISystemScanService`、`IActiveOperationRegistry` |
| `BackupManagerPage` | 358 | `BackupManagerViewModel`（**其 `diagnosticService`/`repairService` 在 349–357 显式传 `null`**） |
| `CpuAffinityPage` | 360 | `IProcessAffinityService`、`IProcessTuningOwner` |
| `CoreAcceleratorPage` | 364 | `CoreAcceleratorViewModel` |
| `CpuCoreManagementPage` | 366 | `CpuCoreManagementViewModel` |
| `PowerPlanEditorPage` | 368 | `IPowerPlanEditorService`、`IPowerManagementOwner`、`IManagedPowerPlanService` |
| `SettingsPage` | 373 | `IAppSettingsStore`、`IConfigurationExportService`、`DiagnosticPackageService`、`() => provider.GetRequiredService<IStartupEntryOwner>()` |
| `StartupManagerPage` | 379 | `IStartupService`、`IStartupEntryOwner`（**页面无路由、无宿主 → 不可达**，见 02 文档 §3.3） |

**ViewModel 注册（共 7 个）：**`SystemCleanupViewModel`（276）、`AppxCleanupViewModel`（278）、`CoreAcceleratorViewModel`（320）、`CpuCoreManagementViewModel`（321）、`BackupManagerViewModel`（349）、`DashboardViewModel`（391）、`RestorePointViewModel`（392）。`CpuAffinityViewModel`、`RepairCenterViewModel`、`StartupManagerViewModel`、`GameModeHubViewModel`、`ProcessPriorityViewModel`、`LatencyTestViewModel` **不在容器里**。

**模板页注册（注释明确要求 transient）：**

```csharp
// system/explorer/tasks/services 四个路由共用此模板页；
// 必须 transient：PageFactory 按路由缓存独立实例并设置各自 ModuleKey，
// 单例会导致四个模块互相覆盖 ModuleKey。
services.AddTransient<MasterModuleTemplate>();   // 387
```

**导航三件套（388–390）：**`INavigationCatalog → NavigationCatalog`、`IPageFactory → PageFactory`、`INavigationRouter → NavigationRouter`（均为 singleton）。

> 注册生命周期的一个直接后果：**除 `MasterModuleTemplate` 外的页面都是 singleton**，`PageFactory` 又按 `route.Key` 缓存实例（`PageFactory.cs`），所以「离开页面 → 再回来」拿到的是**同一个对象**，构造函数只跑一次，`OnLoaded`/`OnUnloaded` 会反复成对触发。因此任何放在构造函数里的初始化（如 `StartupManagerPage` 里 `DataContext = viewModel`）在整个进程生命周期内只执行一次；一旦那次失败，后续每次进入该页面都是同一个坏状态。只有 4 个共用 `MasterModuleTemplate` 的路由因为注册成 transient 才各自拿到独立实例（`App.xaml.cs:384–387` 的注释正是为此）。

### 3.3 值得注意的组合根事实（与「管理员状态不一致」类问题直接相关）

1. **权限有多个真值源，但只有一个是权威实现**：
   - **权威判定**：`GameOptimizer.Core/Services/WindowsElevation.cs` —— `Probe()`（126–202）读进程令牌的 `TokenElevation`（`IsElevated`）与 `TokenGroups`（是否属于内置 Administrators SID，`IsAdministratorAccount`），并暴露 `ElevationLevel`（`Unknown`/`StandardUser`/`AdministratorNotElevated`/`ElevatedAdministrator`）、`ElevationDetail`、`ElevationReasonCode`（`ReasonElevated`/`ReasonAdministratorNotElevated`/`ReasonStandardUser`/`ReasonNonWindows`/`ReasonProbeFailed`，53–57）以及区分「策略/ACL 拒绝」与「缺管理员」的 `DescribeMachineScopeUnavailable()`（207–218）。`WindowsPrincipal.IsInRole(Administrator)` 现在只是探测失败时的**回退路径**（约 151 行），不再是主判据。测试可用 `OverrideProbeForTesting()`（223–230）覆写。
   - **UI 门面**：`GameOptimizer.Shell/Services/UserPrivilegeService.cs`（`IUserPrivilegeService`，DI 单例，注册于 `App.xaml.cs:254`）：构造时 `:16 → Refresh()`（`:90–98`，直接调 `WindowsElevation.Probe()`，**不是**读 `Current` 缓存），把快照的**五个**字段缓存为自己的只读属性 `IsAdministrator`/`IsAdministratorAccount`/`ElevationLevel`/`ElevationDetail`/`ElevationReasonCode`，并在每次赋值时按需 `Raise(...)`（实现 `INotifyPropertyChanged`）。接口面见 `IUserPrivilegeService.cs:12–26`。
   - `GameOptimizer.Core/Services/PermissionChecker.cs`：`IsRunningAsAdministrator() => WindowsElevation.IsElevated`（第 17 行）—— 只是权威实现的一层静态转发。
   - 各平台能力类与 Shell/Core 侧的就地检查也各自转发到同一个权威实现（`GameOptimizer.Core/Services/PermissionChecker.cs:17`、`SystemGuard.cs:36/39/42/45`、`OperationErrorAnalyzer.cs:26/44/58`、`SystemCompatibilityChecker.cs:314/343`、`MemoryCleanerService.cs:45`、`RegistryPriorityService.cs:26`、`PowerServiceDiagnostics.cs:82`、`Shell/Services/SystemRestorePointWriterEntryPoint.cs:187`，以及 `GameOptimizer.Infrastructure.Windows` 下的 17 个文件 / 18 处 `WindowsElevation.IsElevated`——注意该目录下 13 个 `*Capability*.cs` 里**只有 6 个**引用它，能力类并非全部收口），因此**它们之间不会互相矛盾；矛盾只会出现在 `UserPrivilegeService` 的缓存快照上**。
   - 页面/VM 里的就地检查同样是转发，注意名字会骗人：
     - `Views/ProcessPriorityPage.xaml.cs:104 CheckAdminPrivileges()` —— **方法名在撒谎：它根本不做任何提权检查**，方法体只是无条件 `ToastService.Instance.Warning(...)` 弹 IFEO 阻断提示（配合第 17 行 `IsIfeoBlocked => true` 的硬阻断）；该页面也无 DI 注册、无路由（不可达）。
     - `ViewModels/RepairCenterViewModel.cs:157 CheckIsAdmin() => WindowsElevation.IsElevated`（ctor `:121` 调用一次）。
     - `ViewModels/CpuCoreManagementViewModel.cs:113–117 CheckAdminPrivilege()` → `IsAdmin = WindowsElevation.IsElevated`（`:116`；ctor `:110` 调用一次）。
     - `ViewModels/DashboardViewModel.cs:1148 IsAdministrator = privilegeService.IsAdministrator`（ctor）。
   → **关键事实（已核对当前代码）**：`UserPrivilegeService` 只在**构造时**刷一次（构造函数 `UserPrivilegeService.cs:14–17` 内 `:16 → Refresh()`），而在整个 `GameOptimizer.Shell` 中**没有任何外部调用点调用 `IUserPrivilegeService.Refresh()`**（全仓库命中仅三处：接口声明 `IUserPrivilegeService.cs:26`、实现体 `UserPrivilegeService.cs:90–98`、以及上面那次构造函数自调用 `UserPrivilegeService.cs:16`），因此**运行中权限状态永不重查**；`DashboardViewModel` 虽然在 `1151–1155` 订阅了它的 `INotifyPropertyChanged`、并在 `OnPrivilegePropertyChanged`（1173–1190）里同步 `IsAdministrator` 与各 toggle 的 `RefreshPrivilege(...)`，但没有任何代码会触发该事件。这正是「已提权但选项仍灰」类症状的代码级根因：**UI 显示的是进程启动瞬间的令牌快照**。
2. **清单里 `highestAvailable`**：`GameOptimizer.Shell/app.manifest:19` 的 `<requestedExecutionLevel level="highestAvailable" uiAccess="false"/>`（注释在 `:15`：明确写了「优先获取管理员令牌」的意图）。按 Windows 语义，`highestAvailable` = 「以当前用户能拿到的最高权限启动」：**管理员账户**双击时会弹 UAC 同意框（同意 → `ElevatedAdministrator`）；**标准用户账户**双击时不会提权（进程拿标准令牌 → `Probe()` 得 `StandardUser`，`UserPrivilegeService.IsAdministrator == false` 且 `IsAdministratorAccount == false`）；管理员账户若运行在**过滤令牌**上（组策略/关闭 UAC 等）则得 `AdministratorNotElevated`（`ElevationReasonCode == ReasonAdministratorNotElevated`）——即「账户是管理员、但进程未提权」。因此 UI 上必须区分三种状态；`DescribeMachineScopeUnavailable()`（`WindowsElevation.cs:207–218`）与 `PermissionChecker.GetAdminRequiredMessage()`（`PermissionChecker.cs:41–56`）已经给出区分后的用户措辞：`AdministratorNotElevated` 才建议「以管理员身份运行」重启，`StandardUser` 建议换管理员账户。**注意**：本清单只决定「双击」的默认行为，不阻止通过 `runas`（右键「以管理员身份运行」）另行提权。
3. **静态单例绕过 DI**：`LocalizationService.Instance`、`ToastService.Instance`、`ModuleExtensionRegistry.Instance`（`GameOptimizer.Shell/Services/ModuleExtensionRegistry.cs`）、`PageDiagnosticService`（纯静态）、`SafeServiceProvider.Initialize(_host.Services)`（`GameOptimizer.Core/Services/SafeServiceProvider.cs`）。页面拿到服务的路径因此有两条（构造注入 / 静态定位），排查「同一状态两处不一致」时必须同时看这两条。
4. **模块扩展注册被 try/catch 包住**：`App.RegisterModuleExtensions()`（App.xaml.cs 865–880）失败只写 `Debug.WriteLine` + 诊断日志 Warning，注册表照旧为空 → `MasterModuleTemplate` 里 `ModuleExtensionRegistry.Instance.GetExtension(ModuleKey)` 返回 `null`（`Views/MasterModuleTemplate.xaml.cs:2591` 取注册表、`:2594` `HasExtension`、`:2601` `GetExtension`），页面表现为「扩展区不显示」，**不会**有任何用户可见提示。
5. **没有「权限变化 → 重新探测并刷新 UI」的通路**：UI 侧唯一入口 `IUserPrivilegeService.Refresh()` 只在 `UserPrivilegeService` 构造时自调用一次（`:16`），`GameOptimizer.Shell` 内**没有任何外部调用点**（`App.xaml.cs:566–574` 的权限回读只读 `IsAdministrator`，不调 `Refresh()`）；`IUserPrivilegeService` 也没有暴露任何周期性/事件驱动的重查机制。注意 `WindowsElevation` 自身并非「只探测一次」：`Current` 在缓存未命中时会调 `Probe()`（`WindowsElevation.cs:92`），判定失败（`Unknown`）时**不写缓存**（`:93–96`）故下次仍会重试，`WindowsElevation.Refresh()`（`WindowsElevation.cs:115–121`）的调用点只有 `WindowsElevation.cs:228`/`:245`（测试用 `OverrideProbeForTesting` 的进入/退出）与 `SystemGuard.RefreshPrivilegeStatus()`（`SystemGuard.cs:47`，**该方法自身无调用点**）。因此**运行中提权（或 UAC 状态变化）不会反映到 UI**——`UserPrivilegeService` 缓存的是启动瞬间的快照，「重启应用」是当前唯一能让权限状态生效的方式，这是「已提权但仍显示需要管理员」最可能的代码级解释。

---

## 4. 启动编排、单实例与 Splash

`GameOptimizer.Shell/Startup/StartupOrchestrator.cs`（404 行，**零 DI 依赖**，刻意早于容器创建）。

| 成员 | 作用 |
| --- | --- |
| `MutexName` / `RecoveryDevelopmentMutexName` / `AcceptanceUiMutexName` | 单实例互斥名；进程内按 `AcceptanceSessionId` 拼出 `YuqiEngine_AcceptanceUi_SingleInstance_{session}` |
| `StageWarningMs = 15_000` | 单个阶段超过 15s，Splash 显示「初始化时间较长」提示 |
| `TotalTimeoutMs = 30_000` | `CheckTotalTimeout()` 超过总预算直接放行（`ShowWarning("启动时间超过预期，正在强制进入主界面...")`） |
| `AcquireMutex()` | 拿到 → `true`；已有实例 → `ActivateExistingWindow()` + `ReleaseMutexSafe()` + `false`；`AbandonedMutexException` → 接管；**其它异常 → 直接 `true`（降级为允许多实例）** |
| `ActivateExistingWindow()` | `FindWindow(null, "宇奇引擎")` → `ShowWindow(SW_RESTORE=9)` → `SetForegroundWindow`，**按窗口标题硬编码匹配** |
| `ShowSplash()` / `ShouldShowSplash` | 只有显式 `--show-splash`/`/show-splash` 且非 autotest 才显示；`App` 目前**不调用** `ShowSplash()`（注释：默认后台启动，不显示 Splash） |
| `RunStage(name, Action)` / `RunStageAsync(name, Func<Task>)` | 阶段包装：Splash 状态 + `StartupTelemetry` + 15s 警告定时器；`OperationCanceledException` 吞掉，其它异常 `EndStage(false)` 后 **rethrow** |
| `ShowError(text, onRetry, onExit)` | 只委托给 `_splash?.ShowError(...)` —— **Splash 为 null 时是空操作**（DI 失败路径的主要风险点，见 §5） |
| `TransitionToMainWindowAsync(window, activate)` | 等 `MainWindow.Loaded`（**5s 超时后强制继续**）→ `FadeOutAsync(300)` → 可选 `Activate/Focus` |
| `WriteTelemetry(IDiagnosticLogService?)` | 遥测摘要写诊断日志；自身 try/catch 只 `Debug.WriteLine` |
| `Dispose()` | `ReleaseMutexSafe()` + 取消/释放 CTS + 关 Splash（全部 try/catch 静默） |

兄弟文件：`GameOptimizer.Shell/Startup/StartupTelemetry.cs`（阶段耗时统计）、`GameOptimizer.Shell/Startup/SplashWindow.xaml(.cs)`（仅在显式开关下使用）。

---

## 5. 早期启动失败会被静默吞掉的位置（重点）

> 这一节直接服务于团队目标「打开/执行失败时完全没有错误信息」。以下每条都给出文件 + 成员 + 表现。

### A. 进程/预检层

| # | 位置 | 静默方式 | 用户可见表现 |
| --- | --- | --- | --- |
| A1 | `GameOptimizer.Shell/Program.cs` → `WriteDiagnostic(...)` | `catch { }` 空捕获 | 预检失败无日志 |
| A2 | `GameOptimizer.Shell/Program.cs` → `ShowNativeMessageBox(...)` | `catch { }` 空捕获 | 预检失败无弹窗，进程直接退出 |
| A3 | `GameOptimizer.Shell/Program.cs` → `RunEnvironmentPreCheck()` 调用点 | 只转成退出码，不写 `crash.log` | 「双击没反应」 |

### B. 异常处理注册层

| # | 位置 | 静默方式 | 后果 |
| --- | --- | --- | --- |
| B1 | `App.OnStartup` 141–150 | `RegisterGlobalExceptionHandlers()` 失败时**仅**退化为 `args.Handled = true` 的处理器 + `Debug.WriteLine` | 全局异常全部被吞且不落盘 |
| B2 | `App.OnDispatcherUnhandledException`（1121–1152） | `_diagnosticLogService?.Append(...)`（1123，可能为 `null`——早于第 423 行赋值）；崩溃日志写入失败只 `Debug.WriteLine`（1139–1143）；**`e.Handled = true` 无条件吞掉**（1151） | UI 线程异常后界面继续运行但状态可能已损坏；用户看不到任何提示（弹窗代码被注释掉，1145–1149） |
| B3 | `App.OnDomainUnhandledException`（1154–1160） | 只写诊断日志；`_diagnosticLogService` 为 null 时**完全无痕**，且不阻止进程终止 | 进程无声消失 |
| B4 | `App.OnTaskException`（1162–1166） | 写诊断日志 + `e.SetObserved()` | 后台任务异常被标记已观察，用户无感 |
| B5 | `App.CrashLogPathSafe`（1109–1119） | `catch { _crashLogPathCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"); }`（1116） | 落盘位置被静默改变到 exe 目录，排查时找不到文件 |

### C. DI / 编排层

| # | 位置 | 静默方式 | 后果 |
| --- | --- | --- | --- |
| C1 | `App.OnStartup` 219–413（`核心服务初始化` stage 的 lambda，`catch (Exception hostEx)` 在 402–412） | 调 `_orchestrator.ShowError(...)`（406–409，onExit:`Shutdown(1)`），但 **Splash 默认为 null（`ShowSplash()` 从未被调用）→ `ShowError` 是空操作**，随后 `return`（411）；紧接着 416–420 的 `_host is null` 防线直接 `Shutdown(1)`（**也没有任何错误面**） | DI 装配失败时**主窗口永不出现、也没有任何错误 UI**，只有 `Debug.WriteLine` |
| C1b | `App.OnStartup` 415–420（`if (_host is null) { Shutdown(1); return; }`） | 无条件静默退出，不写 `crash.log`、不弹窗（唯一痕迹是 C1 catch 里的 `Debug.WriteLine`） | 进程出现又立刻消失，用户侧「双击没反应」 |
| C2 | `App.OnStartup` 582–608（最外层 catch） | 写 `crash.log`（`catch {}` 包裹）；`_orchestrator.ShowError` 同样可能空操作；`MessageBox` 仅在 `_orchestrator?.GetTelemetry() == null` 时兜底 | 兜底弹窗条件依赖遥测对象是否为 null，**不是**依赖「错误是否已呈现给用户」 |
| C3 | `StartupOrchestrator.ActivateExistingWindow()`（387+） | 全部 try/catch，失败只 `Debug.WriteLine` | 二次启动「无反应」，用户不知道已有实例 |
| C4 | `StartupOrchestrator.AcquireMutex()` 泛化 catch（113–118） | 返回 `true` 允许多实例 | 单实例保护失效且无提示 |
| C5 | `StartupOrchestrator.ShowError`（230–233） | `_splash?.ShowError(...)` | Splash 为 null 时是**静默 no-op**（见 C1） |
| C6 | `StartupOrchestrator.TransitionToMainWindowAsync`（287–294） | `catch` 里三件事**全部无用户可见输出**：`_telemetry.EndStage(success: false)`（`:289`）、`Debug.WriteLine`（`:290`）、`_splash?.Dispatcher.Invoke(() => { _splash?.Close(); })`（`:292–293`，内层在 `SplashWindow.Close()` 外再套一个空 `catch { }`）；`finally` 里 `_telemetry.Stop()`（`:295–298`） | 过渡失败时用户只看到窗口「卡住」，且 Splash 万一还在会被静默关掉 |
| C7 | `App.InitializeServicesAsync()`（838–862） | `catch` 写诊断日志后**不重抛** | `ModuleHost` / `OptimizationProfileService` 初始化失败 → 页面模块/健康数据为空，无用户提示 |
| C8 | `App.RunSystemRestoreStartupReconciliationAsync()`（801–836） | `catch` 里先 `Debug.WriteLine`（`:829`），再写 Warning 诊断日志（`:830–834`；注释：UI 已可见，必须保持可用） | 还原点对账失败无提示 |
| C9 | `ModuleHost.InitializeAsync`（`GameOptimizer.Core/Modules/ModuleHost.cs`） | 单模块 30s 超时/异常只 `_logger.LogError` 并继续 | 单个模块静默缺失 |
| C10 | `ModuleLoader.RegisterModule`（`GameOptimizer.Modules/ModuleLoader.cs`） | `AssemblyPath is null` / 文件不存在 / 类型找不到 / 反射失败 → 只 `Debug.WriteLine` | `modules.json` 里的模块静默不注册 |
| C11 | `ModuleManifestProvider.Load()`（`GameOptimizer.Modules/ModuleManifestProvider.cs`） | 读失败/反序列化失败 → 返回空 `ModuleManifest` + `Debug.WriteLine` | **`modules.json` 壞掉 = 所有外部模块静默消失** |
| C12 | `App.RegisterModuleExtensions()`（865–880） | catch → 诊断日志 Warning | 扩展区空白（见 §3.3 第 4 条） |
| C13 | `App.CleanupTempFiles()`（1061–1097，`App.OnExit` 第 1029 行调用） | 两层 `catch`：内层删除失败 `catch { }`（1081–1084），外层 `catch (Exception ex)` 只 `Debug.WriteLine`（1092–1096）；且匹配串**写死成 `.write_test_*.tmp`（1072）**，只覆盖一类探测文件 | 未被该 glob 覆盖的探测文件（见下）在 `AppDataRoot` 下**静默堆积**，且删除失败一律无痕 | 
| C13a | 探测文件的实际命名（与 `:1072` 的 glob 逐一对齐） | **会被收走**：`.write_test_{guid:N}.tmp` —— `AppPaths.ValidateDirectories()`（`GameOptimizer.Core/Constants/AppPaths.cs:219`，随 `App.xaml.cs:189` 每次启动执行）、`Core/Utilities/SafeDirectoryOperations.cs:98`、`Core/Services/Snapshot/SnapshotService.cs:92`；**不会被收走**：`Program.cs:89` 的 `.startup_test_{guid:N}.tmp`（预检成功后自删，但预检中途失败/进程被强杀时残留）、`AppPaths.cs:193` 的 `.root_probe_{guid:N}.tmp`（由 `TryProbeWritableDirectory` 创建，调用点 `AppPaths.cs:122`（`TryConfigureLogsDirectory`，`App.xaml.cs:184`）与 `:171/:175`（`ResolveAppDataRoot`）——即**进程启动早期就会产生**）、`SettingsPage.xaml.cs:609` 的 `.yuqi_write_probe_{guid:N}.tmp`（其删除失败被 `SettingsPage.xaml.cs:620` 空 catch 吞掉）、`HardwareInfoPage.xaml.cs:449` 的 `.test_{guid}.tmp`（探测文件在 `:451` 删，异常路径下不删） | 排查时在 `AppDataRoot` 下看到的 `.tmp` 需要按前缀反查来源：`.write_test_` 会在退出时被清、`.root_probe_` / `.yuqi_write_probe_` / `.test_` / `.startup_test_` 都不会 |
| C14 | `GameOptimizer.Shell/Services/ApplyOperationTracer.cs` | 静态构造 `catch { }`（`:29`，初始化 `apply-operation-trace.log` 失败即静默）与 `Log(...)` 的 `catch { }`（`:54`，**每次写日志失败都无痕**） | 该类**全仓库没有任何调用点**（全部命中仅 `:1` 文件头、`:10` 类声明、`:17` 静态构造三处自引用，见 §10 第 7 条）——即整条「应用操作追踪」通路当前事实上不存在，一旦被启用，其失败仍不会被任何日志记录 |

### D. 首屏渲染层

| # | 位置 | 静默方式 | 后果 |
| --- | --- | --- | --- |
| D1 | `MainWindow.OnLoaded`（294–340；`try` 309–327，`catch` 328–331） | `await _viewModel.InitializeAsync()`（`:320`）的异常只 `PageDiagnosticService.LogError("MainWindow", "ViewModel 初始化失败", ex)`（`:330`），随后照常 `TrySetResult()`（`:332`） | **「首屏正常但全空」**——与本目标描述高度吻合 |
| D2 | `PageFactory.InitializeInjectedPage`（`Navigation/PageFactory.cs:63–73`） | `_ = InitializeAsync(...)` 火忘（`:67` `CoreAcceleratorPage` / `:71` `CpuCoreManagementPage`）+ `InitializeAsync` catch（`:81–84`）→ `PageDiagnosticService.LogError(pageName, "注入 ViewModel 初始化失败", ex)`（`:83`） | 注入页面的 VM 初始化失败静默 |
| D3 | `PageFactory.GetOrCreate`（`Navigation/PageFactory.cs:20–51`） | 注册数为 0（`:30–34`）或 >1（`:36–41`）、或实例不是 `FrameworkElement`（`:43–46`）→ 抛 `InvalidOperationException("...")` | 抛异常本身会被 B2 吞掉：页面宿主留空、界面无提示 |
| D4 | `MainWindow.OnNavigated`（607–681；宿主缺失分支 609–612） | 宿主不存在时只 `PageDiagnosticService.Log("MainWindow", $"路由宿主不存在: {e.Route.HostName}")`（`:611`）后 `return`（`:612`） | 点导航无反应、无提示 |
| D5 | `MainWindow.NavigateToPage`（577–600；未知键分支 579–584） | 未知导航键只 `PageDiagnosticService.Log("MainWindow", $"忽略未知导航键: {pageKey}")`（`:582`）后 `return` | 同上 |
| D6 | `MainWindow.CreateHostMap`（118–164；校验 151–154） | `hosts.Count != _navigationCatalog.Routes.Count \|\| Routes.Any(route => !hosts.ContainsKey(route.HostName))` → 抛 `InvalidOperationException("导航路由与 XAML 宿主映射不完整或存在重复。")`（`:154`，构造期，会被 B2 吞/或导致窗口构造失败） | 外壳整体不可用且无提示 |

---

## 6. MainWindow：外壳与首屏渲染链路

### 6.1 构造与依赖

`GameOptimizer.Shell/MainWindow.xaml.cs`：

```csharp
public MainWindow(
    DashboardViewModel viewModel,
    ISystemTrayService trayService,
    INavigationCatalog navigationCatalog,
    INavigationRouter navigationRouter,
    IPageFactory pageFactory,
    IAppSettingsStore settingsStore,
    IActiveOperationRegistry activeOperations)
```

构造期做的事（`MainWindow.xaml.cs:79–106`）：`BindNavigationSource()`（`:84` → `:108–116`，把 `CollectionViewSource "GroupedNavItems"` 的 `Source` 设为 `_navigationCatalog.VisibleSections`，取不到资源直接抛 `InvalidOperationException("Main navigation collection view is unavailable.")`）；`_hosts = CreateHostMap()`（`:85`）；缓存 `_appSettings = settingsStore.Current`（`:87`）；`new UiSourceInspector(this)`（`:88`，开发期 Ctrl+Alt+I 界面定位器，`Diagnostics/UiSourceInspector.cs:17`，构造里挂 `InputManager.PreProcessInput` + 窗口 `PreviewMouseLeftButtonDown`/`Closed`）；订阅 `_settingsStore.Changed`（`:89–92`，只同步 `MinimizeToTray` 一个字段，**无取消订阅**）；`DataContext = _viewModel`；挂 `Loaded/StateChanged/Closing`；订阅 `_navigationRouter.Navigated`、`_viewModel.NavigateToPageRequested`；`RestoreWindowState()`；`UpdateShellFrameState()`；订阅 `LocalizationService.Instance.LanguageChanged`（`:105`）。

### 6.2 资源与导航源绑定

- `App.xaml` 的 `Application.Resources` 合并 7 个主题字典：`DarkTheme.xaml`、`DesignTokens.xaml`、`GlobalControlStyles.xaml`、`Animations.xaml`、`ModernStyles.xaml`、`ShellCompatibilityResources.xaml`、`YuqiComponentLibrary.xaml`（注释标明它是原生控件状态的「single final authority」）。
- `MainWindow.xaml` 定义 `CollectionViewSource x:Key="GroupedNavItems"`（第 305 行），`BindNavigationSource()`（108–116）把它 `Source` 设为 `_navigationCatalog.VisibleSections`；XAML 第 479 行以此作为导航 `ItemsSource`。找不到资源直接抛 `InvalidOperationException("Main navigation collection view is unavailable.")`。

### 6.3 宿主映射（页面容器）

- XAML（648–674）声明 27 个 `ContentControl`：`OverviewPageHost`、`RestorePointPageHost`、`RepairCenterPageHost`、`BackupManagerPageHost`、`CpuAffinityPageHost`、`CoreAcceleratorPageHost`、`CpuCoreManagementPageHost`、`SystemCleanupPageHost`、`AppxCleanupPageHost`、`HardwareInfoPageHost`、`NetworkTestPageHost`、`CpuModulePageHost`、`GraphicsModulePageHost`、`NetworkModulePageHost`、`PrivacyModulePageHost`、`SystemModulePageHost`、`ExplorerModulePageHost`、`TasksModulePageHost`、`ServicesModulePageHost`、`PeripheralModulePageHost`、`AudioOptimizationPageHost`、`PerformanceBoostPageHost`、`MemoryOptimizationPageHost`、`PowerPlanEditorPageHost`、`EsportsModePageHost`、`SettingsPageHost`、`ModulePageHost`。
- `CreateHostMap()`（118–164）用 `nameof(...)` 建字典并**强校验**：`hosts.Count != _navigationCatalog.Routes.Count || _navigationCatalog.Routes.Any(route => !hosts.ContainsKey(route.HostName))`（`:151–152`）→ 抛 `InvalidOperationException("导航路由与 XAML 宿主映射不完整或存在重复。")`（`:154`）。**新增路由必须同时改 `NavigationCatalog.CreateRoutes()` 和 `MainWindow.xaml` 宿主，否则启动即失败。**

### 6.4 导航与渲染时序

```
用户点击导航行
  → NavigationListBox_SelectionChanged / PrimaryNavigationList_PreviewMouseLeftButtonUp
  → MainWindow.NavigateToPage(section.Key)                     // 577
      → _navigationCatalog.Resolve(key)   // 支持 alias；null → 只打日志 return (D5)
      → 若可见路由：清空 _viewModel.SelectedOptimizationModule（_suppressModuleNavigation 保护）
      → _navigationRouter.Navigate(route.Key)
          → PageFactory.GetOrCreate(route)                     // 缓存 / DI GetServices(route.PageType)
          → Navigated(route, previousRoute)
  → MainWindow.OnNavigated(e)                                  // 607
      → 遍历 _hosts：清除动画、Opacity=1、Visibility=Hidden、ZIndex=0
      → host.Content = page; Visibility=Visible; ZIndex=1; UpdateLayout()
      → AmbientBackdrop 仅 modules 分组可见
      → ClientAreaAnimation 开启时 160ms Opacity 0→1 淡入（FillBehavior.Stop）
      → PageDiagnosticService.Log(route/host/content/dataContext/visibility/opacity/zIndex/size)
      → 可见路由：同步选中项 + ScrollIntoView + 延迟 UpdateNavigationSelectionIndicator
```

`module-detail` 是**内部路由**（`IsVisible=false`）：`DashboardViewModel.SelectedOptimizationModule` 变化 → `OnViewModelPropertyChanged`（683） → `Navigate("module-detail")`，取消选中则回到 `_navigationRouter.LastVisibleKey`。

### 6.5 托盘 / 关闭

- `ISystemTrayService.Initialize(mainWindow)`（App.xaml.cs 457–458）；`--autostart` 且设置 `MinimizeToTray` → `MinimizeToTray()`。
- `MainWindow.OnClosing`（`MainWindow.xaml.cs:370–450`，**async void**）：`if (_closeApproved) return;`（`:372`，二次关闭直接放行，避免自己触发的 `Close()` 再走一遍确认）→ `_activeOperations.Snapshot()`（`:374`）：有活动操作时先 `e.Cancel = true`（`:377`）并置 `_closeCheckRunning` 防重入（`:378–379`）；其中有 `ClosePolicy == BlockUntilComplete` 的操作 → `InfoDialog`「操作仍在进行」后 `return`（`:383–389`）；否则 `ConfirmDialog`「关闭前取消正在进行的操作？」未确认则 `return`（`:391–395`）；确认后 `await CancelAndDrainAsync(15s)`（`:397`），`CanClose == false` 时 `InfoDialog`「暂时无法关闭」并 `return`（`:398–408`）；全部通过才 `_closeApproved = true` + `Close()`（`:410–411`）。**没有活动操作**时走另一条独立分支：`_viewModel?.IsApplyingModule == true` → `ConfirmDialog`（`Window.CloseWhileOptimizing`），未确认则 `e.Cancel = true` 后 `return`（`:421–435`）。最后 `SaveWindowState()`（`:438`）并退订 `LanguageChanged`（`:441`）、`Navigated`（`:442`）、`NavigateToPageRequested` 与 `PropertyChanged`（`:445–449`）——注意退订列表里**没有**构造期挂上的 `_settingsStore.Changed`（见 §6.1）。
- `MinimizeButton_Click`/`CloseButton_Click` 都遵循 `_appSettings.MinimizeToTray`。

---

## 7. 退出链路

`App.OnExit(ExitEventArgs e)`（1019–1056），**async void**：

1. 退订三个全局异常处理器（与 `RegisterGlobalExceptionHandlers` 对称）；
2. `_orchestrator?.Dispose()`（Mutex + Splash）；
3. `CleanupTempFiles()`（1061–1097）——删 `AppDataRoot` 下 `.write_test_*.tmp`；**内外两层 catch，删除失败一律忽略**；
4. `ISystemTrayService.Dispose()`；
5. `try { await _host.StopAsync(TimeSpan.FromSeconds(5)); } catch { Debug.WriteLine }`（**吞掉**）→ `_host.Dispose()`；
6. `OperationLock.Shutdown()`、`ActivityService.Shutdown()`（静态资源锁/事件订阅清理）；
7. `base.OnExit(e)`。

> 退出路径上没有任何用户可见的错误面：托盘图标残留、Host 停止超时、临时文件清理失败都只写在 `Debug` 输出里。

---

## 8. 数据目录与日志落点（排查用）

`GameOptimizer.Core/Constants/AppPaths.cs`：

| 成员 | 值/来源 | 备注 |
| --- | --- | --- |
| `AppDataFolderName` | `"YuqiEngine"` | |
| `PreferredAppDataRoot` | `F:\YuqiEngine`（**硬编码盘符**） | |
| `LegacyAppDataRoot` | `%LOCALAPPDATA%\YuqiEngine` | |
| `AppDataRoot` | `RootResolution.Value.Path`（`Lazy<AppDataRootResolution>`，`ResolveAppDataRoot`） | 受环境变量 **`YUQIENGINE_DATA_ROOT`** 覆盖（`AppDataRootOverrideVariable`） |
| `IsFallbackDataRoot` / `DataRootFallbackReason` | 回退标记与原因 | `F:\` 不可用时才会用到，排查「数据去哪了」先看这两项 |
| `LogsDir` | `_configuredLogsDirectory`（`TryConfigureLogsDirectory(startupSettings.LogBackupDirectory, ...)`，`App.xaml.cs:184`）否则 `<AppDataRoot>/Logs` | |
| `CrashLogPath` | `<AppDataRoot>/crash.log` | 启动期异常（App 最外层 catch / Dispatcher 未处理异常）落在这里 |
| `PageDiagnosticLogPath` | `<LogsDir>/page_diagnostics.log` | **UI 初始化/导航诊断的主战场** |
| `startup_diagnostic.log` | `<LogsDir>/startup_diagnostic.log` | 仅 `Program.WriteDiagnostic` 写入 |

`PageDiagnosticService`（`GameOptimizer.Shell/Services/PageDiagnosticService.cs`）：全静态、`lock` 保护、单文件上限 5MB（超过直接 `File.Delete`），写入失败只 `Debug.WriteLine` → **UI 诊断日志本身也可能静默丢失**。

---

## 9. 症状 → 定位入口

| 症状 | 先看 |
| --- | --- |
| 双击无反应、无进程 | `Program.RunEnvironmentPreCheck()` 返回值；`<LogsDir>/startup_diagnostic.log`；`AppPaths.IsFallbackDataRoot` |
| 第二个实例「没反应」 | `StartupOrchestrator.ActivateExistingWindow()`（窗口标题硬编码 `"宇奇引擎"`，被改标题后失配） |
| 主窗口不出现且无报错 | `App.OnStartup` 219–413 的 `hostEx` 分支 + `ShowError` 空操作（Splash 未创建） |
| 首屏出来但内容全空 | `MainWindow.OnLoaded` 的 `PageDiagnosticService.LogError("MainWindow", "ViewModel 初始化失败", ex)`；`InitializeServicesAsync` 的 `App.InitializeServices` 记录 |
| 点导航无反应 | `page_diagnostics.log` 中 `MainWindow` 的「忽略未知导航键 / 路由宿主不存在」 |
| 某个优化模块整块消失 | `ModuleManifestProvider.Load()` + `ModuleLoader.RegisterModule` 的 `Debug` 输出；`modules.json` 是否被 `Compile`/拷贝到输出目录 |
| 显示「需要管理员」但进程已提权 | `UserPrivilegeService.ElevationLevel` / `ElevationReasonCode`（**启动瞬间的令牌快照**，运行中无 `Refresh()` 调用点）；`WindowsElevation.Probe()` 的 `DescribeMachineScopeUnavailable()`（策略/ACL 拒绝 ≠ 缺管理员）；`PermissionChecker.IsRunningAsAdministrator()`；`app.manifest` 的 `highestAvailable` |
| 崩溃日志找不到 | `App.CrashLogPathSafe` 的 fallback（`<BaseDirectory>/crash.log`） |
| 某个页面「永远打不开 / 打开是空白」 | 先确认它是否真有路由：`NavigationCatalog.CreateRoutes()`（24 可见 + 3 内部）；再确认 `MainWindow.xaml` 有对应 `ContentControl` 宿主（`CreateHostMap` 会强校验）。`StartupManagerPage`、`ProcessPriorityPage` 两者都不满足 → 当前版本不可达（详见 02 文档 §3.3） |

---

## 10. 与「启动失败无错误信息」相关的改造锚点（不改代码，仅定位）

1. `StartupOrchestrator.ShowError` 是**空操作**且 `App` 从不调用 `ShowSplash()` → DI 失败路径没有错误面。若要给出用户可见错误，最小改动点是 C1（App.xaml.cs 402–412）或让 `ShowError` 在 `_splash == null` 时回退到 `MessageBox`。
2. `App.OnDispatcherUnhandledException` 里的「只弹一次」代码已被注释（1145–1149）且 `e.Handled = true` → UI 异常对用户完全不可见。
3. `MainWindow.OnLoaded` 的阶段划分使「首屏正常/数据缺失」成为默认降级形态；若要暴露，需要在 D1 的 catch 里补一个用户可见面（当前只有 `PageDiagnosticService`）。
4. `ModuleManifestProvider.Load()` 返回空 manifest 与「本来就没有模块」在 UI 上不可区分（C11）。
5. **启动成功之后的「页面级」失败面同样缺失**，其逐页清单在 `02-ui-pages-and-viewmodels.md` §4（U1–U18，含 §4.2 的「静默什么都不发生」入口）与 §4.1（逐页初始化/刷新失败）：最突出的是 `U1`（`PageFactory.GetOrCreate` 的 `throw` 被 B2 吞掉 → 点导航毫无反应）与 `U5/D1`（首屏照常渲染、数据全空）。
6. 若只想改一处让「启动失败可见」：把 `ShowError`（`StartupOrchestrator.cs:230–233`）在 `_splash == null` 时回退到 `MessageBox.Show`，可同时覆盖 C1（DI 失败）与 C1b（`_host is null` 防线）两条静默路径。
7. 两个「整体未被启用」的诊断设施：`Services/ApplyOperationTracer.cs`（74 行，`catch { }` 两处，全仓库零调用点，见 C14）与 `Controls/ModernMessageBox`（无外部调用，见 02 文档 §5）。它们既没有产生数据，也不会有失败记录——排查时不要把它们当作可用的证据来源。
