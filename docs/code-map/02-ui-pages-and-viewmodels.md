# 02 · UI 页面 / ViewModel / 服务对应关系地图（YuqiEngine）

> 目标：给出「导航路由 → 宿主 ContentControl → 页面 → ViewModel → 服务」的完整对应表，并标出**页面级失败被静默吞掉**的位置。
> 仓库根 = `YHRJ - 副本`，所有路径为仓库相对路径。

---

## 1. 导航模型（四个角色）

| 角色 | 文件 | 职责 |
| --- | --- | --- |
| `NavigationRoute` | `GameOptimizer.Shell/Navigation/NavigationRoute.cs` | 不可变路由元数据：`Key` / `HostName` / `PageType` / `IsVisible` / `GroupKey` / `GroupTitleKey` / `TitleKey` / `SubtitleKey` / `IconData` / `ModuleKey` / `ModuleTitle` / `ModuleSubtitle` / `Aliases`；`CreateSection(Func<string,string> localize)`（第 68–83 行）把可见路由渲染成 `NavigationSectionModel`（第 7 个参数是 `targetModuleKey`，取路由的 `ModuleKey`），元数据不完整时抛 `InvalidOperationException($"导航路由 {Key} 没有完整的可见导航元数据。")`（第 72 行） |
| `NavigationCatalog` | `GameOptimizer.Shell/Navigation/NavigationCatalog.cs` | `INavigationCatalog` 实现；`CreateRoutes()`（**78–132**，其中路由字面量 **87–114**）是**唯一的路由声明处**；`BuildLookup()`（55–68）同时登记 `Key` 与 `Aliases`（`StringComparer.OrdinalIgnoreCase`），重复键抛 `InvalidOperationException($"导航键重复：{key}")`（70–76）；`Resolve(string)`（31–39）`Trim()` 后查表、空白/null 返回 `null`；`VisibleSections`（29）由 `RebuildVisibleSections()`（46–53）生成，并订阅 `LocalizationService.Instance.LanguageChanged`（22）→ 切语言时整体重建（**新对象**，见 §2.4 的选中项同步坑） |
| `NavigationRouter` | `GameOptimizer.Shell/Navigation/NavigationRouter.cs` | `Navigate(string)`（23–41）：`Resolve` → `_pageFactory.GetOrCreate(route)`（32，**先建页面、后改状态**，所以建页抛异常时 `CurrentKey` 保持原值）→ 更新 `CurrentKey`（33）与 `LastVisibleKey`（仅可见路由，34–37，初始均为 `"overview"`）→ `Navigated(route, previousRoute)`（39）；未知键返回 `false`（26–29）。**全程无 try/catch** |
| `PageFactory` | `GameOptimizer.Shell/Navigation/PageFactory.cs` | 按 `route.Key` 缓存页面实例（`_pages` 字典，13/24/49，**永不淘汰**）；用 `_services.GetServices(route.PageType)` 解析，**要求恰好 1 个注册**（29–46，0 个 / 多个 / 非 `FrameworkElement` 各抛 `InvalidOperationException`）；`ConfigurePage`（53–61）给 `MasterModuleTemplate` 注入 `ModuleKey/ModuleTitle/ModuleSubtitle`；`InitializeInjectedPage`（63–73）对 `CoreAcceleratorPage`/`CpuCoreManagementPage` **火忘式**调用其 VM 的 `InitializeAsync`（75–85 catch 后只写 `PageDiagnosticService`） |

`NavigationChangedEventArgs` 与接口 `INavigationCatalog` / `INavigationRouter` / `IPageFactory` 同目录。

---

## 2. 路由总表（24 可见 + 3 内部 = 27）

来源：`NavigationCatalog.CreateRoutes()`（第 78–132 行）。分组常量：`monitor` / `modules` / `tools` / `settings`，组标题键由 `GroupTitleKey(groupKey)` 映射为 `NavGroup.Monitor|Modules|Tools|Settings`，未知组抛 `ArgumentOutOfRangeException`。

### 2.1 可见路由（`Visible(...)` → `IsVisible = true`）

| # | Key | 宿主（ContentControl） | PageType | 分组 | 标题键 | ModuleKey | 别名 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `overview` | `OverviewPageHost` | `OverviewPage` | monitor | `Nav.Overview` | — | — |
| 2 | `memory-optimization` | `MemoryOptimizationPageHost` | `MemoryOptimizationPage` | modules | `Nav.MemoryOptimization` | — | `memory` |
| 3 | `audio-optimization` | `AudioOptimizationPageHost` | `AudioOptimizationPage` | modules | `Nav.AudioOptimization` | — | `audio` |
| 4 | `performance-boost` | `PerformanceBoostPageHost` | `PerformanceBoostPage` | modules | `Nav.PerformanceBoost` | — | `performance` |
| 5 | `cpu-nav` | `CpuModulePageHost` | `CpuOptimizationPage` | modules | `Nav.CpuOptimization` | — | `cpu` |
| 6 | `graphics-nav` | `GraphicsModulePageHost` | `GraphicsOptimizationPage` | modules | `Nav.GraphicsOptimization` | — | `graphics` |
| 7 | `network-nav` | `NetworkModulePageHost` | `NetworkOptimizationPage` | modules | `Nav.NetworkOptimization` | — | `network` |
| 8 | `privacy-nav` | `PrivacyModulePageHost` | `PrivacyShieldPage` | modules | `Nav.PrivacyShield` | — | `privacy` |
| 9 | `system-nav` | `SystemModulePageHost` | **`MasterModuleTemplate`** | modules | `Nav.SystemTuning` | `system` | `tuning` |
| 10 | `explorer-nav` | `ExplorerModulePageHost` | **`MasterModuleTemplate`** | modules | `Nav.DesktopExperience` | `explorer` | `explorer` |
| 11 | `tasks-nav` | `TasksModulePageHost` | **`MasterModuleTemplate`** | modules | `Nav.TaskScheduler` | `tasks` | `scheduler` |
| 12 | `services-nav` | `ServicesModulePageHost` | **`MasterModuleTemplate`** | modules | `Nav.SystemServices` | `services` | `services` |
| 13 | `system-cleanup` | `SystemCleanupPageHost` | `SystemCleanupPage` | modules | `Nav.SystemCleanup` | — | `cleanup` |
| 14 | `appx-cleanup` | `AppxCleanupPageHost` | `AppxCleanupPage` | modules | `Nav.AppxCleanup` | — | `appx`, `uninstall` |
| 15 | `peripheral-nav` | `PeripheralModulePageHost` | `PeripheralOptimizationPage` | modules | `Nav.PeripheralOptimization` | — | `peripheral` |
| 16 | `esports-mode` | `EsportsModePageHost` | `EsportsModePage` | modules | `Nav.EsportsMode` | — | `game-mode-hub` |
| 17 | `hardware-info` | `HardwareInfoPageHost` | `HardwareInfoPage` | tools | `Nav.HardwareInfo` | — | `hardware` |
| 18 | `network-test` | `NetworkTestPageHost` | `NetworkTestPage` | tools | `Nav.NetworkTest` | — | `nettest` |
| 19 | `restore-point` | `RestorePointPageHost` | `RestorePointPage` | tools | `Nav.RestorePoint` | — | `restore` |
| 20 | `repair-center` | `RepairCenterPageHost` | `RepairCenterPage` | tools | `Nav.RepairCenter` | — | `repair` |
| 21 | `backup-manager` | `BackupManagerPageHost` | `BackupManagerPage` | tools | `Nav.BackupManager` | — | `backup` |
| 22 | `cpu-core-management` | `CpuCoreManagementPageHost` | `CpuCoreManagementPage` | tools | `Nav.CpuCoreManagement` | — | — |
| 23 | `power-plan-editor` | `PowerPlanEditorPageHost` | `PowerPlanEditorPage` | tools | `Nav.PowerOptimization` | — | — |
| 24 | `settings` | `SettingsPageHost` | `SettingsPage` | settings | `Nav.Settings` | — | — |

> 每条可见路由的 `SubtitleKey` 由 `Visible(...)` 自动生成：`$"{titleKey}.Subtitle"`（`NavigationCatalog.cs:118`）。

### 2.2 内部路由（`Internal(...)` → `IsVisible = false`，不出现在侧栏）

| Key | 宿主 | PageType | 当前代码里是否有人导航过去 |
| --- | --- | --- | --- |
| `cpu-affinity` | `CpuAffinityPageHost` | `CpuAffinityPage` | **没有**。全仓库对字符串 `"cpu-affinity"` 的**代码**命中只有四类：本行声明（`NavigationCatalog.cs:112`）、测试断言（`NavigationCatalogTests.cs:48/51`）、修复目录里的资源键（`Core/Services/Repair/CpuSchedulingRepairCatalog.cs:26–27` 的 `"repair.cpu-affinity.clear-all"` / `"cpu-affinity:all-processes"`，与导航无关）、以及该资源键的边界测试（`Tests/Changes/CpuSchedulingRepairBoundaryTests.cs:20/69/70`，同样与导航无关；`.md` 文档里的出现不计）。没有任何 `Navigate("cpu-affinity")` / `NavigateToPage("cpu-affinity")` / 别名调用 → **该路由当前不可达**（宿主与页面都在，只是没人去） |
| `core-accelerator` | `CoreAcceleratorPageHost` | `CoreAcceleratorPage` | **没有导航调用点**。`"core-accelerator"` 精确命中只有本行声明（`:113`）、`Data/GuideDefinitions.cs:110` 的 `PageKey`（新手引导序列，`:109` 的 `Id = "core-accelerator-guide"` 是另一串）与 `Services/UserGuideService.cs:42` 的 `GuidePageHostNames` 映射；另外 `Tests/Navigation/NavigationCatalogTests.cs:49` 是可见性断言。`Services/Baseline/BaselineDomainCollector.cs:265` 与 `BaselineOwnedFileDomainRestoreOwner.cs:166` 里的 `"payload/core-accelerator"` 是备份域路径，与导航无关。`UserGuideService` 只在**页面已经显示**时按宿主名去找锚点元素（`StartGuideAsync(pageKey, rootElement)`，`UserGuideService.cs:63`），该文件内搜 `Navigat` **零命中** → 它**不负责导航**，引导永远等不到这个页面被打开 |
| `module-detail` | `ModulePageHost` | `ModulePage` | **是，唯一有活的内部路由**：`DashboardViewModel.SelectedOptimizationModule` 变化 → `MainWindow.OnViewModelPropertyChanged`（683–700）→ `Navigate("module-detail")`（694）；取消选中且当前是 `module-detail` 时回到 `NavigationRouter.LastVisibleKey`（696–698） |

> 判定方法：`GameOptimizer.Shell` 内所有导航发起点的穷举——`MainWindow.xaml.cs:306`（`"overview"`）、`:548`（`"settings"`）、`:599`（`route.Key`，来自 `NavigateToPage(string)`）、`:694/698`（`module-detail` / `LastVisibleKey`）、`:709/734`（侧栏点击 `section.Key`）、`:604`（`OnNavigateToPageRequested` 转发 `pageKey`）、`App.xaml.cs:502/509`（验收探针 `--acceptance-page=` / `--acceptance-navigation-sequence=`）、`RestorePointViewModel.cs:426`、`MasterModuleTemplate.xaml.cs:1794`（均 `"backup-manager"`）、`NetworkTestPage.xaml.cs:598`（`"network-nav"`）、`OverviewPage.xaml.cs:59`（转发 `DashboardViewModel.NavigateToPageRequested` 的 target）。事件发起侧另有 `DashboardViewModel.cs:1798/1805/1812`（`NavigateToPageRequested?.Invoke(...)`）与 `:2409`（`LegacyRestoreEntryPresentation.RecoveryCenterRoute`）。这些 target 全部落在 24 条**可见**路由内（外加 `module-detail`），**没有任何一处指向 `cpu-affinity` 或 `core-accelerator`**。

导航键硬约束见 `GameOptimizer.Tests/Navigation/NavigationCatalogTests.cs:42–52`（`Internal_routes_are_resolvable_but_not_visible`：断言 `module-detail`/`cpu-affinity`/`core-accelerator` 三条 `IsVisible == false` 且前两条不在 `VisibleSections` 中）。

### 2.3 宿主映射的强校验

`MainWindow.CreateHostMap()`（`GameOptimizer.Shell/MainWindow.xaml.cs:118–164`）逐条 `nameof(XxxHost) = XxxHost`，然后：

```csharp
if (hosts.Count != _navigationCatalog.Routes.Count ||
    _navigationCatalog.Routes.Any(route => !hosts.ContainsKey(route.HostName)))
{
    throw new InvalidOperationException("导航路由与 XAML 宿主映射不完整或存在重复。");
}
```

→ **新增/删除路由必须同时改 `NavigationCatalog.CreateRoutes()` 与 `MainWindow.xaml`（648–674 行的 27 个 `ContentControl`）**，否则窗口构造即抛异常。

### 2.4 两套「导航项」集合（最容易踩的坑）

同一份侧栏在代码里存在**两个互不相通的数据源**，排查「点了没反应 / 选中项不动」时必须先分清是哪一套：

| | 侧栏实际渲染的集合 | `DashboardViewModel.NavigationSections` |
| --- | --- | --- |
| 来源 | `NavigationCatalog.VisibleSections`（由 `CreateRoutes()` 的 24 条可见路由经 `CreateSection()` 生成） | `DashboardViewModel.BuildNavigationSections()`（`DashboardViewModel.cs:2638–2675`）**手写的 22 条** `new NavigationSectionModel(...)` |
| 绑定位置 | `MainWindow.BindNavigationSource()`（`MainWindow.xaml.cs:108–116`）把它绑到 `CollectionViewSource x:Key="GroupedNavItems"`（`MainWindow.xaml:305`），再由 `MainWindow.xaml:479` 作为 `ItemsSource` | 只被 ViewModel 自身使用（`SelectedNavigationSection` 的初始值、`NavigateToModule`/`Navigate*` 的查找）；**XAML 里没有任何绑定引用它** |
| 数量差异 | **24 条** | **22 条 —— 缺 `appx-cleanup` 与 `esports-mode`**（两个都只存在于 catalog） |
| `TargetModuleKey` | 由路由的 `ModuleKey` 填充（仅 4 个 `MasterModuleTemplate` 路由非 null） | **全部为 `null`**（`BuildNavigationSections` 从未传第 7 个参数） |

由此产生**四个**已核实的静默失效：

1. `DashboardViewModel.SelectedNavigationSection`（`DashboardViewModel.cs:765–781`）与 `MainWindow.SelectedNavigationSection`（`MainWindow.xaml.cs:57–66`，侧栏 `SelectedItem` 的真实绑定目标，见 `MainWindow.xaml:480`）是**两个独立属性**。ViewModel 里所有 `NavigateRestorePoint()`（1781–1788）、`NavigateToRepairCenter()`（1816–1823）、`NavigateToSystemCleanup()`（1825–1832）、`NavigateToModule()`（1790–1814）对 `SelectedNavigationSection` 的赋值**不会改变侧栏选中项，也不会触发 `NavigationListBox_SelectionChanged`**；`MainWindow.OnViewModelPropertyChanged`（683–700）只处理 `SelectedOptimizationModule`，对 `SelectedNavigationSection` 直接 `return`。真正能驱动 UI 的是 `MainWindow.NavigateToPage(string)` / `_navigationRouter.Navigate(...)`。
2. `NavigateGameMode()`（`DashboardViewModel.cs:1772–1779`）查找 `section.Key == "game-mode-hub"` —— 这是 catalog 里的**别名**，而 `NavigationSections` 里既没有 `game-mode-hub` 也没有 `esports-mode` → 恒为 `null`，方法**什么都不做**（无日志、无提示）。
3. `NavigateToModule()` 的第三级查找（`s.TargetModuleKey == moduleKey`，第 1808 行）在这份列表上**恒不命中**（22 条的 `TargetModuleKey` 全为 null），因此实际只有「映射表命中」与「`Key` 精确命中」两级；三级全落空时静默 `return`（见 §4 U13）。
4. `NavigateRestorePoint()`（1781–1788）、`NavigateToRepairCenter()`（1816–1823）、`NavigateToSystemCleanup()`（1825–1832）**连 `NavigateToPageRequested` 都不 `Invoke`**，只赋 `SelectedNavigationSection` → 在 shell 上 100% 无效（与 `NavigateToModule` 的第一/二级不同，后者至少还会发事件）。其中 `NavigateToSystemCleanup()` 是唯一有真实调用链的（`MemoryControlPanel.CleanJunk_Click`，见 §4 U17），因此这条是**用户可复现的「点了没反应」**。

> 结论：「导航到 X」这个动作有两条实现路径 —— 走 `INavigationRouter`（有效、会被 `MainWindow.OnNavigated` 记录）与改 `DashboardViewModel.SelectedNavigationSection`（在 shell 上无效）。判断一个页面入口是否真的能跳转，要看它最终是否调用了 `MainWindow.NavigateToPage` / `_navigationRouter.Navigate`。

---

## 3. 页面 ↔ ViewModel ↔ 服务 对应表

### 3.1 有独立 ViewModel 的页面（DI 注入）

| 页面（`GameOptimizer.Shell/Views/…`） | 构造签名 | ViewModel（`ViewModels/…`） | ViewModel 依赖的服务 | DataContext 设置点 |
| --- | --- | --- | --- | --- |
| `OverviewPage` | `OverviewPage()` 无参（`Views/OverviewPage.xaml.cs:11–18`） | **无自己的 VM**，直接用继承来的 `DashboardViewModel` | — | **不设 `DataContext`**：`MainWindow.xaml.cs:93` `DataContext = _viewModel`，页面在 `:624 host.Content = page` 时继承；只在不依赖 DataContext 的 XAML 里用 `x:Name="Root"` + `ElementName`（`OverviewPage.xaml:2`，绑定 `PrivilegeHint` 等走继承的 DataContext）。代码后置只订阅 `DashboardViewModel.NavigateToPageRequested`（`AttachRouter` `:36–43`、`DetachRouter` `:45–51`，`OnLoaded`/`OnDataContextChanged` 均调用） |
| `EsportsModePage` | `(DashboardViewModel, IBaselineGateService, IUserPrivilegeService, OptimizationGuardService, IHardwareDetectionService)`（`:65–82`） | `GameModeHubViewModel`（**`OnLoaded` 内 `new`，非 DI**，`:120`；VM ctor `GameModeHubViewModel.cs:26–33`） | `DashboardViewModel`, `IHardwareDetectionService` | ctor `DataContext = _viewModel`（Dashboard，`:78`）；`GuardPanel.AttachGuardService(_guardService)`（`:79`） |
| `SystemCleanupPage` | `(SystemCleanupViewModel)`（`:16–24`） | `SystemCleanupViewModel`（ctor `ViewModels/SystemCleanupViewModel.cs:135–140`，后 4 个参数可选） | `ISystemCleanupService`, `IEventBus?`, `ICleanupQuarantineManager?`（缺省 `new CleanupQuarantineManager()`）, `IActiveOperationRegistry?`, `Func<string,string,bool>?` | ctor `DataContext = _viewModel`（`:21`） |
| `AppxCleanupPage` | `(AppxCleanupViewModel)`（`:16–22`） | `AppxCleanupViewModel`（ctor `AppxCleanupViewModel.cs:66–70`，后 2 个参数可选） | `IAppxRemovalOwner`, `IAppxUserRemovalService`, `Func<AppxCleanupConfirmationRequest,bool>?`（DI 里接线到 `DangerousOperationDialog.Show`，`App.xaml.cs:278–288`）, `IActiveOperationRegistry?` | ctor `DataContext = _viewModel`（`:20`） |
| `BackupManagerPage` | `(BackupManagerViewModel)`（`:27–32`） / 另有无参重载（`:20–25`） | `BackupManagerViewModel`（无参 ctor `:295` 委托到全参 `:297–305`） | `ISnapshotService`, `IEventBus?`, `BackupDiagnosticService?`, `AutoRepairService?`, `IBaselineCatalogService?`, `IBaselineRestoreOrchestrator?`, `ISystemRestoreService?`, `IComprehensiveBackupService?` | **不设 `DataContext`**：暴露 `ViewModel` 属性 + `IsCompositionReady`，XAML 用根元素名绑定（`BackupManagerPage.xaml:1` `x:Name="Root"` → `:57/:59` `{Binding ViewModel.X, ElementName=Root}`）；**无参构造只打诊断日志、不建服务**（`:24`） |
| `RestorePointPage` | `(RestorePointViewModel)`（`:20–28`） | `RestorePointViewModel`（ctor `RestorePointViewModel.cs:117–124`，后 5 个参数可选） | `ISystemRestorePointOwner`, `ISystemRestorePointConfirmationPrompt`, `IBaselineCatalogService?`, `ISnapshotService?`, `TimeProvider?`（缺省 `TimeProvider.System`）, `ISystemRestoreManualLauncher?`, `ISystemRestorePointStartupReconciliation?` | **不设 `DataContext`**：同样 `x:Name="Root"` + `ElementName=Root`（`RestorePointPage.xaml:1,54,56`）；`OnLoaded:40` `await ViewModel.InitializeAsync()`（catch `:44–47`）；30s `DispatcherTimer` 重估能力（`OnCapabilityTimerTick` catch `:63–66`） |
| `RepairCenterPage` | `(IRepairService, ISystemScanService, IActiveOperationRegistry)`（`:17–29`） | `RepairCenterViewModel`（**`OnLoaded:40–43` 内 `new`**；VM ctor `RepairCenterViewModel.cs:114–115`） | `IRepairService`, `ISystemScanService`, `IEventBus?`, `IActiveOperationRegistry?` | `OnLoaded:44` `DataContext = _viewModel`；`OnUnloaded:56–66` `Cleanup()`/`Dispose()`/`DataContext = null`。`OnLoaded:33–36` 若 `_viewModel != null` 直接 return（**该 return 在 try 之外**） |
| `HardwareInfoPage` | `(IHardwareDetectionService)`（`:41–48`） | 无独立 VM（代码后置 555 行） | `IHardwareDetectionService` | **不设 `DataContext`**；初始化入口是 XAML 事件而非 `Loaded +=`：`HardwareInfoPage.xaml:8` `Loaded="UserControl_Loaded"` → 处理器 `HardwareInfoPage.xaml.cs:97`（catch `:107–110` 只写诊断日志） |
| `NetworkTestPage` | 三个 ctor：`()`（`:33`）、`(INetworkResetOwner?)`（`:37`）、`(INetworkResetOwner?, INetworkSpeedTestService)`（`:42`）；前两个都 `new NetworkSpeedTestService()` 后委托给第三个 | 无独立 VM | `INetworkResetOwner`, `INetworkSpeedTestService` | **不设 `DataContext`**；**DI 注册的是哪个重载决定 `INetworkResetOwner` 是否接上**（`App.xaml.cs:338`），未接上时「刷新 DNS/重置网络」只弹 `ToastService.Warning(..., "Network Reset Owner 未接入，已安全阻断。")`（`:680`） |
| `CpuAffinityPage` | `(IProcessAffinityService, IProcessTuningOwner)`（`:19–28`） | `CpuAffinityViewModel`（`InitializeViewModel` 内 `new`，`:129`；VM ctor `CpuAffinityViewModel.cs:68–73`） | `IProcessAffinityService`, `IProcessTuningOwner` | `DataContext = _viewModel`（`:130`）+ `_viewModel.Initialize()`（`:131`）；catch `:137–140` 只写诊断日志 |
| `CoreAcceleratorPage` | `(CoreAcceleratorViewModel)`（`:16–24`） | `CoreAcceleratorViewModel`（DI，ctor `CoreAcceleratorViewModel.cs:89–96`） | `CpuTopologyService`, `IProcessTuningOwner` | ctor `DataContext = viewModel`（`:22`）；`InitializeAsync`（VM `:98`）**没有其它调用点**，只由 `PageFactory.InitializeInjectedPage:65–68` 火忘触发 |
| `CpuCoreManagementPage` | `(CpuCoreManagementViewModel)`（`:21–30`） | `CpuCoreManagementViewModel`（DI；partial：`.cs` / `.CoreMatrix.cs` / `.Profiles.cs` / `.Acceleration.cs`；ctor `.cs:99–111`） | `CpuTopologyService`, `IProcessAffinityService`, `IProcessTuningOwner` | ctor `DataContext = viewModel`（`:28`）；`InitializeAsync`（`.cs:119`）同样只由 `PageFactory.InitializeInjectedPage:69–72` 火忘触发（其内部 catch 只 `Debug.WriteLine`，`.cs:145/183`） |
| `PowerPlanEditorPage` | `(IPowerPlanEditorService, IPowerManagementOwner, IManagedPowerPlanService)`（`:34–46`） | 无独立 VM | 同左三个服务 | **不设 `DataContext`**（纯 `x:Name` 代码后置）；`RefreshPageAsync`（`:62–87`）catch `:75–81` **无任何日志/Toast**，只把异常文本写进 `TxtPlansEmpty.Text`/`TxtPowerStatus.Text` |
| `SettingsPage` | `(IAppSettingsStore, IConfigurationExportService, DiagnosticPackageService, Func<IStartupEntryOwner>)`（`:40–61`） | 无独立 VM（代码后置 1143 行） | 同上；`_startupOwnerFactory` 故意延迟解析（注释 `:53–57`：避免构造期触碰注册表/确认密钥） | **不设 `DataContext`**；初始化入口 `Loaded += SettingsPage_Loaded`（`:59`） |
| `StartupManagerPage` | `(IStartupService, IStartupEntryOwner)`（`:12–14`） | `StartupManagerViewModel`（`ctor:19` 内 `new`；VM ctor `StartupManagerViewModel.cs:37–43`） | `IStartupService`, `IStartupEntryOwner?`, `Func<ManagedAutostartPlan,bool>?`（接线到 `ConfirmDialog.Show`，`:22–27`） | `DataContext = viewModel`（`:28`）+ `viewModel.Initialize()`（`:29`）—— **两者都在 `try` 内**，catch `:31–34` 只 `Debug.WriteLine` → 失败时 `DataContext` 保持 `null`，页面全空白。**且该页当前不可达**（见 §3.3） |
| `ModulePage` | `ModulePage()` 无参（`:13–19`） | 无（由继承来的 `DashboardViewModel` 驱动） | — | **不设 `DataContext`**（继承自 `MainWindow.xaml.cs:93`）；绑定 `HasModuleSelected` / `SelectedOptimizationModule`（`ModulePage.xaml:7,18,22,38,48`），`HasModuleSelected => SelectedOptimizationModule != null`（`DashboardViewModel.cs:602`） |

### 3.2 纯 XAML 壳页面（无 ViewModel，托管 `MasterModuleTemplate`）

**全仓库共 12 个页面 XAML 直接内嵌 `<views:MasterModuleTemplate ModuleKey="…"/>`**，它们的代码后置都只有 `InitializeComponent()`（`MemoryOptimizationPage.xaml.cs:6–13` 是一段 8 行的 `///` 注释块，实质仍是空壳；`GraphicsOptimizationPage.xaml.cs` 另有 `OnLoaded`/`OnUnloaded` 两个只有注释的空处理器，见下）：

| 页面 | XAML 中的模板 | ModuleKey | 是否有路由指向该页面类型 | DI 注册 |
| --- | --- | --- | --- | --- |
| `MemoryOptimizationPage` | `MemoryOptimizationPage.xaml:7` | `memory` | ✅ `memory-optimization` | DI singleton |
| `AudioOptimizationPage` | `AudioOptimizationPage.xaml:6` | `audio` | ✅ `audio-optimization` | DI singleton |
| `PerformanceBoostPage` | `PerformanceBoostPage.xaml:11` | `performance` | ✅ `performance-boost` | DI singleton |
| `CpuOptimizationPage` | `CpuOptimizationPage.xaml:6` | `cpu` | ✅ `cpu-nav` | DI singleton |
| `GraphicsOptimizationPage` | `GraphicsOptimizationPage.xaml:6` | `graphics` | ✅ `graphics-nav` | DI singleton |
| `NetworkOptimizationPage` | `NetworkOptimizationPage.xaml:6` | `network` | ✅ `network-nav` | DI singleton |
| `PrivacyShieldPage` | `PrivacyShieldPage.xaml:6` | `privacy` | ✅ `privacy-nav` | DI singleton |
| `PeripheralOptimizationPage` | `PeripheralOptimizationPage.xaml:6` | `peripheral` | ✅ `peripheral-nav` | DI singleton |
| `SystemTuningPage` | `SystemTuningPage.xaml:6` | `system` | ❌ **孤儿** | **未注册** |
| `ExplorerOptimizationPage` | `ExplorerOptimizationPage.xaml:6` | `explorer` | ❌ **孤儿** | **未注册** |
| `TaskSchedulerPage` | `TaskSchedulerPage.xaml:6` | `tasks` | ❌ **孤儿** | **未注册** |
| `ServicesOptimizationPage` | `ServicesOptimizationPage.xaml:6` | `services` | ❌ **孤儿** | **未注册** |

> 后 4 个（`system`/`explorer`/`tasks`/`services`）之所以是孤儿：对应路由（`system-nav` / `explorer-nav` / `tasks-nav` / `services-nav`）的 `PageType` **直接写成 `typeof(MasterModuleTemplate)`**，并把 `moduleKey`/`moduleTitle`/`moduleSubtitle` 作为 `Visible(...)` 的命名参数传给路由（`NavigationCatalog.cs:95–98`），由 `PageFactory.ConfigurePage`（`PageFactory.cs:53–61`）注入。**这 4 个页面文件与 `MasterModuleTemplate` 的四个路由项是两套并存的写法，改任一处的 `ModuleKey` 都不会影响另一处。**

`GameOptimizer.Tests/Visual/ShellVisualContractTests.cs:696–702` 把这条架构写成契约测试：`PerformanceBoostPage.xaml` 必须包含 `MasterModuleTemplate`，且 `PerformanceBoostPage.xaml.cs` 不得出现 `DataContext`。

`MasterModuleTemplate`（`GameOptimizer.Shell/Views/MasterModuleTemplate.xaml.cs`，2662 行）要点：

- 三个 DependencyProperty：`ModuleKeyProperty` / `ModuleTitleProperty` / `ModuleSubtitleProperty`（`OnModuleKeyChanged` 触发重载）；
- `OnDataContextChanged` 取 `DataContext as DashboardViewModel`，再按 `ModuleKey` 从 `_viewModel.OptimizationModules` 解析出 `OptimizationModuleDefinition? _module`；
- 监听 `DashboardViewModel.OptimizationModules` 变化重新解析模块（`e.PropertyName == nameof(DashboardViewModel.OptimizationModules)`）；
- `_initLock`（`SemaphoreSlim`）、`ThreadSafeBatchOperationService`、`CentralizedStateManager`、`CommandExecutionGate` 承担批量应用/回滚；
- `LoadThemeColors()` + `OnLoaded`/`OnUnloaded`/`OnVisibilityChanged` 管理生命周期；
- **模块扩展槽**：`ModuleExtensionRegistry.Instance.GetExtension(ModuleKey)`（第 2591–2605 行），命中则渲染扩展控件，未命中**静默跳过**；
- 唯一实际接入的扩展是内存模块：`Extensions/MemoryModuleExtension.cs:63` → `Views/MemoryControlPanel.xaml`（`MemoryControlPanel` 不在路由表里，只能通过这条扩展槽进入；其内部 catch 一律只 `Debug.WriteLine`，例如 `MemoryControlPanel.xaml.cs:201/533`）。

**覆盖核对（`CreateRoutes()` 的 27 条路由逐一对应，可机械复核）：**下表把 §2 的路由表与 §3.1/§3.2 的页面行、以及 `App.xaml.cs` 的注册行对齐；**27 行无缺漏、无多出**。

| 路由 Key（`NavigationCatalog.cs` 行号） | `PageType` | 页面/VM 对应行 | DI 注册行（`App.xaml.cs`） |
| --- | --- | --- | --- |
| `overview`（87） | `OverviewPage` | §3.1 第 1 行（继承 `DashboardViewModel`） | 325 |
| `memory-optimization`（88） | `MemoryOptimizationPage` | §3.2 `memory` | 326 |
| `audio-optimization`（89） | `AudioOptimizationPage` | §3.2 `audio` | 327 |
| `performance-boost`（90） | `PerformanceBoostPage` | §3.2 `performance` | 328 |
| `cpu-nav`（91） | `CpuOptimizationPage` | §3.2 `cpu` | 329 |
| `graphics-nav`（92） | `GraphicsOptimizationPage` | §3.2 `graphics` | 330 |
| `network-nav`（93） | `NetworkOptimizationPage` | §3.2 `network` | 331 |
| `privacy-nav`（94） | `PrivacyShieldPage` | §3.2 `privacy` | 332 |
| `system-nav`（95） | `MasterModuleTemplate` | §3.2 末段（路由自带 `system` 参数） | 387（**transient**） |
| `explorer-nav`（96） | `MasterModuleTemplate` | 同上（`explorer`） | 387 |
| `tasks-nav`（97） | `MasterModuleTemplate` | 同上（`tasks`） | 387 |
| `services-nav`（98） | `MasterModuleTemplate` | 同上（`services`） | 387 |
| `system-cleanup`（99） | `SystemCleanupPage` | §3.1（`SystemCleanupViewModel`） | 333 |
| `appx-cleanup`（100） | `AppxCleanupPage` | §3.1（`AppxCleanupViewModel`） | 289 |
| `peripheral-nav`（101） | `PeripheralOptimizationPage` | §3.2 `peripheral` | 334 |
| `esports-mode`（102） | `EsportsModePage` | §3.1（`GameModeHubViewModel` 由页面 `new`） | 335 |
| `hardware-info`（103） | `HardwareInfoPage` | §3.1（无 VM） | 336 |
| `network-test`（104） | `NetworkTestPage` | §3.1（无 VM） | 338 |
| `restore-point`（105） | `RestorePointPage` | §3.1（`RestorePointViewModel`） | 342 |
| `repair-center`（106） | `RepairCenterPage` | §3.1（`RepairCenterViewModel` 由页面 `new`） | 344 |
| `backup-manager`（107） | `BackupManagerPage` | §3.1（`BackupManagerViewModel`） | 358（VM 349） |
| `cpu-core-management`（108） | `CpuCoreManagementPage` | §3.1（DI VM） | 366 |
| `power-plan-editor`（109） | `PowerPlanEditorPage` | §3.1（无 VM） | 368 |
| `settings`（110） | `SettingsPage` | §3.1（无 VM） | 373 |
| `cpu-affinity`（112，内部） | `CpuAffinityPage` | §3.1（`CpuAffinityViewModel` 由页面 `new`） | 360 |
| `core-accelerator`（113，内部） | `CoreAcceleratorPage` | §3.1（DI VM） | 364 |
| `module-detail`（114，内部） | `ModulePage` | §3.1（继承 `DashboardViewModel`） | 383 |

> 反向核对：`App.xaml.cs` 里注册了页面类型但**不在上表**的只有 `StartupManagerPage`（379），它就是 §3.3 的头号孤儿；反过来上表每一行的 `PageType` 在容器里都**恰好 1 个注册**（这是 `PageFactory.GetOrCreate:29–46` 的硬要求，多注册一个也会在导航时抛异常）。

### 3.3 与路由脱钩的「孤儿」页面 / ViewModel（重要）

| 文件 | 状况 |
| --- | --- |
| `Views/SystemTuningPage.xaml(.cs)` | ModuleKey=`system`，但**没有任何路由引用它**：`system-nav` 路由的 `PageType` 直接是 `typeof(MasterModuleTemplate)`（`NavigationCatalog.cs:95`），也未在 DI 注册、无 XAML 引用 |
| `Views/ExplorerOptimizationPage.xaml(.cs)` | ModuleKey=`explorer`，同上（`explorer-nav` → `MasterModuleTemplate`，`NavigationCatalog.cs:96`） |
| `Views/TaskSchedulerPage.xaml(.cs)` | ModuleKey=`tasks`，同上（`tasks-nav` → `MasterModuleTemplate`，`NavigationCatalog.cs:97`） |
| `Views/ServicesOptimizationPage.xaml(.cs)` | ModuleKey=`services`，同上（`services-nav` → `MasterModuleTemplate`，`NavigationCatalog.cs:98`） |
| `Views/UniversalModuleTemplate.xaml(.cs)` | 完整的 `UserControl`（`.xaml.cs:8`），全仓库（产品代码 + 测试 + XAML）**零引用** |
| `Views/StartupManagerPage.xaml(.cs)` | **在 DI 中注册**（`App.xaml.cs:379–382`），但既无路由、也无宿主、XAML 中无人引用 → 当前版本不可达（与 `cpu-affinity`/`core-accelerator` 不同：那两条至少还有路由条目，这条连路由都没有） |
| `ViewModels/LatencyTestViewModel.cs` | 有 `LatencyTestViewModel()` 与 `LatencyTestViewModel(ILatencyTestService)`，但**没有 `LatencyTestPage`**（`Views/*Page.xaml.cs` 中不存在；只在 `obj/Release` 的旧构建产物里有同名残留）→ 无 UI 宿主 |
| `Views/ProcessPriorityPage.xaml(.cs)` | 有独立 ctor `(IRegistryPriorityService)`（`:23`）并 `DataContext = _viewModel`（`:31`），但**未在 DI 注册、无路由、无 XAML 引用**（唯一的文件外引用是读源码文本的契约测试 `GameOptimizer.Tests/Shell/ProcessPriorityToggleReentrancyContractTests.cs:8`）→ 不可达；页面内自带 `IFEO` 零写入阻断（`IsIfeoBlocked => true`，`:17`），且 `CheckAdminPrivileges()`（`:104`）**名为检查实际不查任何权限**（详见 01 文档 §3.3） |

> 结论：`DashboardViewModel` 一共声明 **13 个模块键**（`ModuleMetadata`，`DashboardViewModel.cs:246–261`，`new ModuleVisual[13]`；另有顺序数组 `OverviewModuleOrder`（`:325–329`）同为这 13 个键）：`performance`/`cpu`/`graphics`/`network`/`privacy`/`system`/`explorer`/`cleanup`/`peripheral`/`tasks`/`services`/`memory`/`audio`。其中 `cleanup` 有专属页面 `SystemCleanupPage`（`system-cleanup` 路由），**其余 12 个的渲染入口都是 `MasterModuleTemplate`**：8 个走无参壳页面，4 个（`system`/`explorer`/`tasks`/`services`）由路由把参数写在 `NavigationRoute.ModuleKey/ModuleTitle/ModuleSubtitle` 上、交给 `PageFactory.ConfigurePage` 注入。两套写法并存是历史遗留。

模块键 → 导航键的映射（`DashboardViewModel.ModuleKeyToNavKeyMap`，`DashboardViewModel.cs:301–323`，由 `NavigateToModule(string?)` 使用，1790–1814）：

| moduleKey | navKey |
| --- | --- |
| `memory` | `memory-optimization` |
| `performance` | `performance-boost` |
| `cpu` | `cpu-nav` |
| `graphics` | `graphics-nav` |
| `network` | `network-nav` |
| `audio` | `audio-optimization` |
| `privacy` | `privacy-nav` |
| `system` / `tuning` | `system-nav` |
| `explorer` | `explorer-nav` |
| `tasks` / `scheduler` | `tasks-nav` |
| `services` | `services-nav` |
| `cleanup` | `system-cleanup` |
| `peripheral` | `peripheral-nav` |
| `hardware` | `hardware-info` |
| `nettest` | `network-test` |
| `restore` | `restore-point` |
| `repair` | `repair-center` |
| `backup` | `backup-manager` |

`NavigateToModule` 的后备逻辑：先在映射表里找 → 再按 `NavigationSections.Key` 精确匹配 → 再按 `TargetModuleKey` 匹配；**三者都未命中时直接静默返回**（`DashboardViewModel.cs:1790–1814`，无日志、无提示）——这是又一处「点击无反应」的候选根因。

数量核对（三处集合**互不相等**，改代码时勿假定同步）：

| 集合 | 位置 | 条目数 | 内容 |
| --- | --- | --- | --- |
| `ModuleMetadata`（模块图标） | `DashboardViewModel.cs:246–261` | **13** | `performance`/`cpu`/`graphics`/`network`/`privacy`/`system`/`explorer`/`cleanup`/`peripheral`/`tasks`/`services`/`memory`/`audio` |
| `OverviewModuleOrder`（概览页顺序） | `DashboardViewModel.cs:325–329` | **13** | 同上 13 个键，仅顺序不同 |
| `ModuleKeyToNavKeyMap`（键→路由键） | `DashboardViewModel.cs:301–323` | **20** | 上述 13 个 + 7 个**纯别名**：`tuning`、`scheduler`、`hardware`、`nettest`、`restore`、`repair`、`backup`（对外部/旧深链兼容，`ModuleMetadata` 里没有对应图标，故在概览页不可见） |

`ModuleKeyToNavKeyMap` 的 7 个额外键恰好对应 7 条 `hardware-info` / `network-test` / `restore-point` / `repair-center` / `backup-manager` 路由别名 —— 即模块键可以指向一个**不是 `MasterModuleTemplate` 的独立页面**。

---

## 4. 页面级「失败静默」位置

> 与团队目标「打开/执行失败时完全没有错误信息」直接相关。以下都给出可导航锚点。

| # | 位置 | 静默方式 | 用户可见表现 |
| --- | --- | --- | --- |
| U1 | `PageFactory.GetOrCreate`（`Navigation/PageFactory.cs:29–46`） | 注册数 0（`:30–34`）/ >1（`:36–41`）/ 不是 `FrameworkElement`（`:43–46`）→ 抛 `InvalidOperationException`；`NavigationRouter.Navigate`（`NavigationRouter.cs:23–41`）与 `MainWindow.NavigateToPage`（`:577–600`）**都不 catch** | 异常冒泡到 Dispatcher → 被 `App.OnDispatcherUnhandledException` 以 `e.Handled = true` 吞掉（`App.xaml.cs:1121–1152`）→ **点导航行毫无反应、无任何提示** |
| U2 | `MainWindow.OnNavigated`（`:609–612`） | 宿主缺失只 `PageDiagnosticService.Log("MainWindow", $"路由宿主不存在: {e.Route.HostName}")`（`:611`）后 `return` | 同上 |
| U3 | `MainWindow.NavigateToPage`（`:579–584`） | 未知键只 `PageDiagnosticService.Log("MainWindow", $"忽略未知导航键: {pageKey}")`（`:582`）后 `return` | 同上 |
| U4 | `PageFactory.InitializeInjectedPage`（`PageFactory.cs:63–85`） | `_ = InitializeAsync(...)`（`:67/:71`）火忘；catch（`:81–84`）→ `PageDiagnosticService.LogError(pageName, "注入 ViewModel 初始化失败", ex)` | `CoreAcceleratorPage` / `CpuCoreManagementPage` 打开后长时间空白，无提示 |
| U5 | `MainWindow.OnLoaded`（`:309–331`） | `await _viewModel.InitializeAsync()`（`:320`）异常只 `PageDiagnosticService.LogError("MainWindow", "ViewModel 初始化失败", ex)`（`:330`），然后**照常** `_initializationCompleted.TrySetResult()`（`:332`） | 首屏可见但模块/状态缺失 |
| U6 | `RepairCenterPage.OnLoaded` catch（`:49–53`） | `PageDiagnosticService.LogError("RepairCenterPage", "Init error", ex)`（`:51`）+ `ToastService.Instance.Error("Repair Center", "Initialization failed. Please try again.")`（`:52`）；**这条文案是英文硬编码**，其余页面走 `LocalizationService` | 有 Toast（全仓库罕见的「有提示」页面） |
| U7 | `StartupManagerPage` ctor try/catch（`StartupManagerPage.xaml.cs:17–34`） | `DataContext = viewModel`（`:28`）与 `viewModel.Initialize()`（`:29`）都在 `try` 内；catch（`:31–34`）只 `Debug.WriteLine` | `DataContext` 保持 `null` → 页面全空白（且该页当前不可达，见 §3.3） |
| U8 | `BackupManagerPage`（`:20–25` / `:41–47`） | 无参构造只 `PageDiagnosticService.Log(..., "Constructor called without ViewModel")`（`:24`）；`OnLoaded` 在 `!IsCompositionReady` 时只 `LogLoaded(..., false, ...)`（`:45`）后 `return`；UI 显示 `CompositionStatusText`（`:17–18`） | 有中文文案，但**只写诊断日志、不弹错**。生产路径走 DI 的 VM 构造（`App.xaml.cs:358–359`），无参构造仅在有人手工 `new` 时才会出现 |
| U9 | `OverviewPage.OnNavigateToPageRequested`（`:53–66`） | `if (Window.GetWindow(this) is MainWindow mainWindow)`（`:55`）**不成立时直接跳过**（无日志）；成立时 `mainWindow.NavigateToPage(target)`（`:59`）包 try/catch → `PageDiagnosticService.LogError`（`:63`） | 概览页快捷入口点击无效 |
| U10 | `MainWindow.AboutButton_Click`（`:472–483`）/ `SupportAuthorButton_Click`（`:485–496`） | catch → `Debug.WriteLine`（`:481` / `:494`） | 点了没反应 |
| U11 | `MainWindow.OpenOfficialGitHubPage`（`:557–575`） | 成功才静默（`:568–572`）；失败**有** `InfoDialog.Show(this, "打开失败", ...)`（`:574`） | 这是少数「打开失败有错误信息」的正例，可作为补齐其他入口的模板 |
| U12 | `PageDiagnosticService`（`Services/PageDiagnosticService.cs`，全 61 行） | `Log` 的写文件失败只 `Debug.WriteLine`（`:35–38`）；超 5MB 时 `try { File.Delete(LogPath); } catch { }`（`:26`）是**完全空的 catch** | 诊断日志本身可能丢失/被截断，用户侧一切「无信息」 |
| U13 | `DashboardViewModel.NavigateToModule(string?)`（`:1790–1814`） | `ModuleKeyToNavKeyMap`（`:1796`）/ `NavigationSections.Key`（`:1801`）/ `TargetModuleKey`（`:1808`）三级匹配全部未命中时**直接落空**，无日志无提示 | 概览/模块卡片点击跳转无反应（且第三级在当前 `NavigationSections` 上恒不命中，见 §2.4） |

### 4.1 页面自身初始化/刷新失败（按页面逐个核对，全部有代码锚点）

| 页面 | 位置 | 静默方式 | 用户可见表现 |
| --- | --- | --- | --- |
| `EsportsModePage` | `OnLoaded` catch（`:203–210`） | `EsportsModeStartupLogger.LogError("FATAL", "启动失败", ex)`（`:207`）—— 写的是**专用 logger**（`Services/EsportsModeStartupLogger.cs`，自己的日志文件），**不进 `page_diagnostics.log`**；同时 `SetDetectionStatus(..., isError: true)`（`:208`） | 有界面文案（少数正例），但排查时容易只看 `page_diagnostics.log` 而漏掉 |
| `HardwareInfoPage` | `UserControl_Loaded` catch（`:107–110`） | 只 `PageDiagnosticService.LogError("HardwareInfoPage", "UserControl_Loaded failed", ex)` | 硬件卡片区域空白 |
| `CpuAffinityPage` | `InitializeViewModel` catch（`:137–140`） | 只 `PageDiagnosticService.LogError("CpuAffinityPage", "Init error", ex)` | 页面空白、无提示 |
| `SystemCleanupPage` | `OnLoaded` catch（`:36–39`） | 只 `PageDiagnosticService.LogError(..., "Failed to create ViewModel", ex)` —— **文案与实际行为不符**：VM 是 ctor 注入的，此处并不创建 VM | 清理列表空白 |
| `SystemCleanupPage` | 详情弹窗 catch（`:114–118`）/ 扫描 catch（`:132–135`） | 诊断日志 + Toast；扫描那条的标题是**硬编码中文**「系统清理」（`:135`），未走 `LocalizationService` | 有提示（英文/中文混用） |
| `AppxCleanupPage` | `OnLoaded` → `await _viewModel.InitializeAsync()` catch（`:40–43`） | 只 `PageDiagnosticService.LogError("AppxCleanupPage", "初始化枚举失败", ex)`（`:42`） | 应用列表空白、无提示 |
| `RestorePointPage` | `OnLoaded` catch（`:44–46`）/ 能力重估 catch（`:63–66`） | 只 `PageDiagnosticService.LogError` | 还原点列表/能力状态不更新 |
| `CpuCoreManagementViewModel` | `InitializeAsync` 外层 catch（`.cs:181–185`）；CPU 检测子 catch（`:143–150`） | 子 catch 与总 catch **都只 `Debug.WriteLine`**（`:145` / `:183`）；部分可恢复分支有 Toast（`:135–137` 检测超时、`:171–173` 权限提示） | 核心拓扑矩阵空、无失败提示 |
| `PowerPlanEditorPage` | `RefreshPageAsync` catch（`:75–81`） | **一条日志/Toast 都没有**，只把异常文本写进 `TxtPlansEmpty.Text`（`:79`）/ `TxtPowerStatus.Text`（`:77`） | 页面有红字文案，但日志里查不到任何记录 |
| `MemoryControlPanel` | 只读刷新 catch（`:199–203`）/ 跳转清理页 catch（`:530–534`） | 都只 `Debug.WriteLine`（`:201` / `:533`）；前者另有 `ShowUnavailableState()` 兜底 | 内存面板显示不可用，「清理」按钮点了没反应 |
| `OverviewPage` | `OnNavigateToPageRequested` catch（`:61–64`）/ `SupportAuthorButton_Click` catch（`:78–81`） | 都只 `PageDiagnosticService.LogError("OverviewPage", ...)`（`:63` 导航转发失败 / `:80` 作者信息弹窗失败） | 失败只落 `page_diagnostics.log`；若 `Window.GetWindow(this) is MainWindow` 不成立（`:55`）则连日志都没有（见 §4 U9） |
| `CoreAcceleratorPage` | `OnLoaded` catch（`:36–39`）/ `OnUnloaded` catch（`:53–56`） | 只 `PageDiagnosticService.LogError("CoreAcceleratorPage", ...)`（`:38` `"OnLoaded failed"` / `:55` `"Receipt restore on unload failed"`） | `OnLoaded` 里 `:34` 主动 `throw new InvalidOperationException("CoreAcceleratorPage requires DI construction.")`，又被自己的 catch 吞掉 → 非 DI 构造时页面静默空转 |
| `CpuCoreManagementPage` | `OnLoaded` catch（`:52–55`）/ `OnUnloaded` catch（`:70–73`） | 只 `PageDiagnosticService.LogError("CpuCoreManagementPage", ...)`（`:54` `"OnLoaded failed"` / `:72` `"Receipt restore on unload failed"`）；`OnLoaded` 内 `:50` 同样有 `requires DI construction` 的 `throw` 被吞 | 核心矩阵空白；离开页面时收据还原失败无提示 |
| `NetworkTestPage` | `OnUnloaded` catch（`:73`）/ 连接元数据 catch（`:185–198`）/ DNS 刷新 catch（`:713–720`） | `:73` 是**完全空的 `catch { }`**（取消/释放 CTS 失败，无日志无 UI）；`:185` 只 `Debug.WriteLine`（`:187`）+ 改状态文案与若干 `"--"` 文本（`:188–197`）；`:713` **不写任何日志文件**（`SetDnsActionStatus`（`:715–718`）改的是页面状态文本，另外 `:719` **有**用户可见的 `ToastService.Instance.Error(...)`） | 卸载清理失败完全无痕；连接元数据 / DNS 刷新失败在 `page_diagnostics.log` 里查不到（DNS 那条至少有 Toast，属于少见的「有提示但无日志」组合） |
| `HardwareInfoPage` | 除 `UserControl_Loaded` 外还有 `RefreshButton_Click` catch（`:119–122`）、检测 catch（`:167–172`，`:163` 是取消分支只 `Log`）、系统运行时长 catch（`:317–320`）、`GetDiskInfo` catch（`:345–348`）、`OpenSystemInfoButton_Click` catch（`:402–406`）、导出写权限探测裸 `catch`（`:453`，探测文件 `.test_{guid}.tmp` 见 `:449`）、导出 catch（`:519–522`） | `:317` 是**裸 `catch { return "--"; }`**（`GetSystemUptime` 读失败 → 界面显示 `--`，无日志无提示）；`:345` 只 `Debug.WriteLine`；`:453` 是**裸 `catch`**（但会弹「导出路径无权限」`InfoDialog`，`:455`）；其它走 `LogError`（`:171` 另置 `HardwarePageState.Error`、`:405` Toast、`:521` `InfoDialog`） | 系统运行时长 / `GetDiskInfo` 失败时磁盘卡片**静默少几项或显示 `--`**；导出路径探测失败时会留下 `.test_*.tmp`（探测文件在 `:451` 删，异常路径下不删）且 `page_diagnostics.log` 无记录 |
| `SettingsPage` | 共 21 处 `catch`（`:197/:295/:466/:473/:523/:542/:593/:620/:720/:756/:783/:803/:825/:854/:935/:1018/:1058/:1063/:1098/:1115/:1133`） | **完全无日志**（连 `Debug` 都没有）的只有两条：`:542`（空 `catch { return string.Empty; }`）、`:620`（空 `catch { }`，删探测文件 `.yuqi_write_probe_*.tmp`（`:609`）失败）；只写 `Debug.WriteLine`、不进 `page_diagnostics.log` 的还有 `:803`（`Debug.WriteLine`，`:805` 作者信息按钮）、`:1115`（`Debug.WriteLine`，`:1117` 关于按钮）；另有 `:523`（只改 `StartupToggle.ToolTip`）、`:854`（`Debug` + 状态文本）、`:1133`（`Debug` + Toast）也不进 `page_diagnostics.log` | 设置页的目录探测与若干按钮失败只有 `Debug` 输出，`page_diagnostics.log` 里可能完全没有记录；`:620` 吞掉的遗留 `.tmp` 也不会被 `App.CleanupTempFiles()` 收走（它只扫 `.write_test_*.tmp` 命名，见 01 文档 §5 C13/C13a） |
| `MasterModuleTemplate`（12 个壳页共用） | 共 16 处 `catch`（`:314/:625/:644/:681/:718/:724/:1449/:1915/:1924/:1932/:1944/:2319/:2332/:2349/:2503/:2629`）；**完全无任何输出的只有一处**：`:2503`（空 `catch (ObjectDisposedException) { }`，`:2505` 仅注释）；另有一处**只写 `Debug.WriteLine`、不进任何日志文件**：`:314`（读主题色失败，`:316` `Debug.WriteLine`）；还有一处**只吞掉取消、无任何记录**：`:681` `catch (OperationCanceledException) { /* 被新请求取消，正常行为 */ }` | `:625/:644/:718/:724/:1449/:1924/:1932/:1944/:2332/:2349/:2629` 写 `PageDiagnosticService.Log/LogError`（其中 `:1944`/`:2349` 还弹 `InfoDialog`）；**`:1915` 与 `:2319` 两个取消分支不写任何日志**，只做 `CompleteRestoreFailure(...)`（`:1919`）或 `CompleteApplyFailure` + `InfoDialog.Show(...FromException(ex)...)`（`:1921` / `:2329`）→ 用户看得到弹窗，但日志里查不到 | 模板页失败**多数**有日志（与多数页面的「只 Debug」相反），但异常发生在「UI 文案 + 无日志」的分支时，只能靠用户描述复现；`:681` 的静默取消与真正的失败在日志上不可区分 |
| `EsportsModePage` | 除 `OnLoaded` 外另有 13 处 `catch`（`:260/:409/:414/:685/:810/:903/:1385/:1410/:1493/:1512/:1636/:1820/:1855`） | 其中 **11 处**写 `EsportsModeStartupLogger`（专用日志文件，`:260/:414/:685/:810/:1385/:1410/:1493/:1512/:1636/:1820/:1855`）；**例外两处**：`:409`（`catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`）只清 `_lastDetectionReport` + `SetDetectionStatus(...)`（`:411–412`，无日志），`:903`（恢复系统状态失败）只 `ShowError("恢复失败", ex.Message)`（`:905`，**两个日志文件都不写**）。`:1385`（加载配置）/`:1410`（保存配置）/`:1493`（导航）/`:1512`（滚动）/`:1636`（恢复守护服务状态）**只写该文件**、无 UI 提示；`:685`（`UpdateStrategyButtons`）记录后 **rethrow** | 电竞模式的问题只在 `EsportsMode_Startup_*.log` 里可见，`page_diagnostics.log` 完全没有记录；而 `:903` 的恢复失败**连专用日志都没有**（只有界面文案） |

### 4.2 静默「什么都不发生」的入口（无异常、无日志）

| # | 位置 | 静默方式 | 用户可见表现 |
| --- | --- | --- | --- |
| U14 | `DashboardViewModel.NavigateGameMode()`（`:1772–1779`） | 查找 `NavigationSections` 里 `section.Key == "game-mode-hub"`；该键**不在**这 22 条里（`game-mode-hub` 只是 catalog 的 alias）→ 恒 `null`，方法体什么都不做 | 「电竞模式」入口点了完全没反应，日志也没有 |
| U15 | `UserGuideService.StartGuideAsync`（`Services/UserGuideService.cs:63–88`） | 序列不存在 / 步数为 0（`:71–74`）或该引导已完成（`:76–79`）时**直接 `return`**，无任何记录 | 新手引导不出现，无从判断是「没有引导」还是「已完成」 |
| U16 | `ProcessPriorityPage.CheckAdminPrivileges()`（`:104–107`，调用点 `:41`） | 方法名是「检查管理员权限」，实际只弹 `ToastService.Warning(..., IfeoBlockedMessage)`，**不查任何权限令牌**；页面另有 `IsIfeoBlocked => true`（`:17`）硬常量阻断所有 IFEO 写入 | 所有持久优先级写入被静默降级为「阻断提示」（该页也不可达，见 §3.3） |
| U17 | `MemoryControlPanel.CleanJunk_Click`（`Views/MemoryControlPanel.xaml.cs:530–534`）→ `DashboardViewModel.NavigateToSystemCleanupCommand`（`:1067`）→ `NavigateToSystemCleanup()`（`:1825–1832`） | 该命令的**唯一**调用点就是这里；而 `NavigateToSystemCleanup()` 只给 `NavigationSections` 里的 `system-cleanup` 项赋 `SelectedNavigationSection`（`:1830`），**从不 `Invoke` `NavigateToPageRequested`**，而该属性在 shell 上没有任何绑定（§2.4）→ 整条链路等于空转；`try/catch`（`:532–533`）只在抛异常时写 `Debug`，正常空转**一条日志都没有** | 内存面板点「清理垃圾」完全没反应，日志里也查不到 |
| U18 | `NavigateGameModeCommand`（`:1051`）/ `NavigateRestorePointCommand`（`:1055`）/ `NavigateToRepairCenterCommand`（`:1063`） | 三个命令**没有任何调用点**（全仓库搜命令名只在 `DashboardViewModel.cs` 自身出现；XAML 未绑定），其方法体（`:1772–1779` / `:1781–1788` / `:1816–1823`）同样只改 `SelectedNavigationSection` | 「快速入口」类功能点了没反应（若日后被绑定） |

---

## 5. 对话窗口清单（逐个核对后的准确数量）

按当前工作树实测，`Window` 派生类的构成是：

| 类别 | 数量 | 依据 |
| --- | --- | --- |
| `Views/*Dialog.xaml` 成对的 `*Dialog : Window` | **41** | `GameOptimizer.Shell/Views/` 下 `*Dialog.xaml` 文件数（文件名与类名一一对应） |
| 纯代码 `OperationDetailCardDialog : Window` | **+1** | `Views/OperationDetailCardDialog.cs:11`（无 XAML，`private` 构造 `:15` + `static Show` `:21`，`:23` 起为方法体） |
| `Controls/ModernMessageBox : Window` | **+1** | `Controls/ModernMessageBox.xaml.cs:12`，`private` 构造 `:16` |
| `Startup/SplashWindow : Window` | **+1** | `Startup/SplashWindow.xaml.cs:17`（启动闪屏，属启动链路，见 01 文档） |
| `MainWindow : Window` | **+1** | `MainWindow.xaml.cs:24` |
| **合计 `Window` 派生类** | **45** | 上表 45 个 `: Window` 声明（`ProgressDialog` 是唯一额外实现 `INotifyPropertyChanged` 的，`ProgressDialog.xaml.cs:15`） |

其中：

- `ModernMessageBox`（`Controls/ModernMessageBox.xaml` + `.xaml.cs`，`private` 构造 `:16`）的**两个**调用点都在自己文件内（`:127`、`:138`），产品代码里**没有任何外部调用** → 事实上的死代码；
- `OperationDetailCardDialog` 的**唯一**入口是 `Services/OperationDetailCardLauncher.cs:30`（`private` 构造 + `static Show`，调用方拿不到类型）；
- `MemoryCleanDialog.xaml.cs` 一个文件里还有两个非窗口类：`ProcessGroupItem:19`、`ProcessInstanceItem:118`（按类名搜 `MemoryCleanDialog` 时要留意）。

`GameOptimizer.Shell/Views/*Dialog.xaml.cs`（按用途分组，共 41 个）：

- **通用**：`InfoDialog`、`ConfirmDialog`、`SmartErrorDialog`、`ProgressDialog`、`OperationInfoDialog`、`OperationTransparencyDialog`、`CompletionSummaryDialog`
- **危险操作/确认**：`DangerousOperationDialog`、`RepairConfirmDialog`、`CleanupConfirmDialog`、`RestorePointConfirmDialog`、`RestoreOptionsDialog`、`RestoreDetailDialog`、`ScenarioConfirmationDialog`、`PowerScenarioConfirmDialog`、`SystemAutoRevertWarningDialog`、`NetworkResetDialog`、`RevertProgressDialog`、`RevertResultDialog`
- **备份/快照**：`BackupSelectDialog`、`FirstRunBaselineDialog`、`ManagedPowerPlanCreateDialog`
- **配置编辑**：`ProcessPriorityEditDialog`、`GamePriorityEditDialog`、`GameProfileEditDialog`、`AffinityEditDialog`、`PowerPlanNameDialog`、`AdvancedPowerDialog`、`PowerOptimizeDetailDialog`、`PeripheralAdvancedDialog`、`GuardServiceSettingsDialog`、`GuardFirstTimeDialog`
- **其他**：`AboutDialog`、`ReleaseNoticeDialog`、`OptimizationResultDialog`、`GameScanResultDialog`、`RegistryInfoDialog`、`CleanupFileListDialog`、`ProcessSelectorDialog`、`MemoryCleanDialog`、`EsportsModeApplyConfirmDialog`

注册/接线要点：

- `IChangeConfirmationPrompt → WpfChangeConfirmationPrompt`、`ISystemRestorePointConfirmationPrompt → WpfSystemRestorePointConfirmationPrompt`、`IBaselineRiskAcknowledgementPrompt → WpfBaselineRiskAcknowledgementPrompt`（App.xaml.cs 251/255/262）；
- `AppxCleanupViewModel` 的确认回调直接调用 `DangerousOperationDialog.Show(Application.Current.MainWindow, ...)`（App.xaml.cs 281–287）；
- 验收截图模式可渲染的对话键：`info` / `confirm` / `about` / `about-support` / `peripheral-advanced`（`App.CaptureAcceptanceDialog`，App.xaml.cs 774–799）。

---

## 6. 管理员状态在 UI 中的传递路径（供权限问题排查）

```
WindowsElevation.Probe()                                   // Core/Services/WindowsElevation.cs:126–202
   └─ 读进程令牌 TokenElevation / TokenGroups → ElevationLevel + 原因码(ElevationReasonCode 53–57)
      └─ UserPrivilegeService.Refresh()                     // Shell/Services/UserPrivilegeService.cs:90–98（仅构造时调用一次，实现 INotifyPropertyChanged）
           └─ IUserPrivilegeService（DI singleton，App.xaml.cs:254）
                ├─ DashboardViewModel ctor: IsAdministrator = privilegeService.IsAdministrator   // DashboardViewModel.cs:1148
                │    └─ 订阅 PropertyChanged → OnPrivilegePropertyChanged → 逐 toggle RefreshPrivilege(IsAdministrator)  // 1151–1155 订阅, 1173–1190 处理
                │         └─ OptimizationToggleModel.RefreshPrivilege(bool) → EvaluateCapabilities()  // Models/OptimizationToggleModel.cs:576–664
                │              ├─ RequiresAdmin && !_isAdministrator → BlockedReason = "需要管理员权限；当前进程未获得 operation 所需权限。"（636–640）
                │              └─ ResolveCapabilityState() → ModuleCapabilityState.AdminRequired（688）
                │    └─ MainWindow.UpdateLocalizedTexts(): AdminModeText / AdminStatusDot 颜色（MainWindow.xaml.cs，见 01 文档 §3.3）
                ├─ EsportsModePage（ctor 注入，不查询 IsAdministrator 而是传 `() => _privilegeService.IsAdministrator` 给 EsportsModeStateService）  // EsportsModePage.xaml.cs:65–82, 113–117
                └─ 不在 DI 链上的独立检查（**全部只是转发**，不再各自 `IsInRole`）：
                     PermissionChecker.IsRunningAsAdministrator()   // Core/Services/PermissionChecker.cs:17（另有 :20 IsAdministratorAccount、:23 CurrentElevationLevel 两个转发）
                     ProcessPriorityPage.CheckAdminPrivileges()      // Views/ProcessPriorityPage.xaml.cs:104（名为检查、实际不查令牌）
                     RepairCenterViewModel.cs:121 / CpuCoreManagementViewModel.cs:113–117（`:116` `IsAdmin = WindowsElevation.IsElevated`，ctor `:110` 调用一次）
                     各 Infrastructure.Windows 就地点（17 个文件 / 18 处 `WindowsElevation.IsElevated`；该目录 13 个 `*Capability*.cs` 只有 6 个引用，见 01 文档 §3.3 第 1 条）
```

要点：

1. `UserPrivilegeService` **没有周期性重查**：`Refresh()`（`:90–98`，内部调 `WindowsElevation.Probe()`）只在构造时被自调用一次（`:16`），且**全 `GameOptimizer.Shell` 没有任何其它 `Refresh()` 调用点**（`App.xaml.cs:566–574` 只读 `IsAdministrator`；01 文档 §3.3 第 5 条已逐处核对）→ 运行中 `IsAdministrator` 会一直是启动那一刻的结论，唯一可靠补救是**重启应用**。注意 `WindowsElevation` 自身不是「只探测一次」：`Current` 缓存未命中时会调 `Probe()`（`WindowsElevation.cs:92`）、判定失败不缓存（`:93–96`），但**能触发重查的 `Refresh()` 没有任何生产调用点**。
2. 「多真值源」在今天已经收口：能力类、`PermissionChecker`、`SystemGuard`、各页面/VM 的检查**全部转发**到 `WindowsElevation.Probe()`（`IsInRole` 只剩探测失败时的回退路径，见 `WindowsElevation.cs:151`），因此**跨模块的判定不会互相矛盾**；仍会造成「侧栏显示管理员模式，但某个模块仍提示需要管理员」的原因是**时间差**：`UserPrivilegeService` 缓存的是启动瞬间的快照，而每次 `WindowsElevation.IsElevated` 读的是同一份进程令牌——两者都不会在运行中变化。
3. `app.manifest` 的 `highestAvailable` 意味着**双击启动时不会请求提权**：管理员账户在 UAC 打开时拿到的是过滤令牌 → `AdministratorNotElevated`（`ReasonAdministratorNotElevated`）；标准用户账户拿到标准令牌 → `StandardUser`（`ReasonStandardUser`）。只有显式右键「以管理员身份运行」（标准用户需再输入管理员凭据）才会得到 `ElevatedAdministrator`。因此 UI 上「账户是管理员但进程未提权」与「账户不是管理员」必须分开处理：前者重新提权启动即可解决，后者不能靠重启解决（原因码见 `WindowsElevation.cs:53–57`，面向用户的措辞见 `DescribeMachineScopeUnavailable()`（`:207–218`）与 `PermissionChecker.GetAdminRequiredMessage()`（`PermissionChecker.cs:41–56`））。
4. 显示层的用户可见文案来自 `LocalizationService`（静态单例 `Instance`）；`OptimizationToggleModel` 的 `BlockedReason` 既有本地化键（如 `Module.Capability.RecoveryUnavailable.*`）也有**硬编码中文字符串**（636–640、614–626），两者混用。

---

## 7. 页面级诊断日志怎么读

`PageDiagnosticService` 写 `<AppPaths.LogsDir>/page_diagnostics.log`（`AppPaths.PageDiagnosticLogPath`），格式：

```
[HH:mm:ss.fff] [页面名] 消息
```

排查特定页面时的关键前缀：

| 前缀 | 来源 | 含义 |
| --- | --- | --- |
| `[MainWindow] === 应用启动 ===`（`:298`）/ `=== 应用启动完成 ===`（`:333`） | `MainWindow.OnLoaded` | 首屏渲染窗口期；两条之间就是「首屏可用但数据未就绪」的窗口 |
| `[MainWindow] 开始初始化 ViewModel...`（`:319`）/ `ViewModel 初始化完成, OptimizationModules: N`（`:321`） | `MainWindow.OnLoaded` | 初始化进度；`N` 为 0 时说明模块清单为空（见 `ModuleManifestProvider` 的静默降级，01 文档 §3.3） |
| `[MainWindow] ViewModel 初始化失败`（`:330`） | `MainWindow.OnLoaded` catch | 首屏数据缺失的主线索（接 `_initializationCompleted.TrySetResult()`，所以**不会**阻塞首屏） |
| `[MainWindow] 忽略未知导航键: X`（`:582`） | `MainWindow.NavigateToPage` | 路由键不存在（检查 alias/拼写；别名表见 §2.1） |
| `[MainWindow] 路由宿主不存在: X`（`:611`） | `MainWindow.OnNavigated` | 路由 `HostName` 与 XAML 宿主名不匹配（正常情况下被 `CreateHostMap` 的构造期校验拦住） |
| `[MainWindow] 导航宿主状态: route=…; host=…; content=…; dataContext=…`（`:653–658`） | `MainWindow.OnNavigated` | 每次成功导航的实证；`dataContext=null` 可用于识别「页面在、但绑定没到位」（如 `RepairCenterPage` 的 `OnLoaded` 失败） |
| `[MainWindow] 导航完成: X -> Y`（`:680`） | `MainWindow.OnNavigated` | 导航成功的收尾标记；**没有**这条而侧栏点击也无反应，就往 §4 U1/U2/U3 查 |
| `[BackupManagerPage] Constructor called without/with injected ViewModel`（`BackupManagerPage.xaml.cs:24/32`） | `BackupManagerPage` | 用于区分 DI 注入是否生效（生产路径应总是 `with`） |
| `[CoreAcceleratorPage]` / `[CpuCoreManagementPage] 注入 ViewModel 初始化失败`（`PageFactory.cs:83`） | `PageFactory.InitializeInjectedPage` | 火忘初始化失败 |
| `[RepairCenterPage] Init error`（`RepairCenterPage.xaml.cs:51`） | `RepairCenterPage.OnLoaded` | 同时会弹英文 Toast（§4 U6） |

> 注意两点：
> 1. `MainWindow.OnLoaded` 里 `PageDiagnosticService.ClearLog()` 被注释掉了（`MainWindow.xaml.cs:297`），所以日志是**跨启动累积**的，读的时候要按时间戳区分会话。
> 2. `EsportsModePage` 的启动/失败信息**不写这个文件**，而是走 `EsportsModeStartupLogger` 的独立日志文件（`EsportsModeStartupLogger.GetLogFilePath()`，`Services/EsportsModeStartupLogger.cs:85`）——排查电竞模式时只看 `page_diagnostics.log` 会一无所获。

---

## 8. 新增一个页面的最小改动清单（从本表推导）

1. 在 `App.xaml.cs` 的 `ConfigureServices` 里注册页面（`AddSingleton<TPage>()` 或工厂重载给可选依赖兜底），**且确保该 `PageType` 恰好只有 1 个注册**（`PageFactory.GetOrCreate:29–46` 强制要求，0 个或多于 1 个都会抛）；
2. 在 `MainWindow.xaml`（**648–674 行，当前正好 27 个** `<ContentControl x:Name="XxxPageHost"/>`）加一个宿主；
3. 在 `MainWindow.CreateHostMap()`（**118–164**）加 `[nameof(XxxPageHost)] = XxxPageHost`，并在 `:151–155` 的 `hosts.Count != _navigationCatalog.Routes.Count` 校验通过；
4. 在 `NavigationCatalog.CreateRoutes()`（**78–132**，其中 `Visible(...)` 调用位于 **87–110**、`Internal(...)` 位于 **112–114**）加 `Visible("key", "XxxPageHost", typeof(TPage), group, "Nav.TitleKey", NavigationIconData.Icon)`；
5. 在语言资源里补 `Nav.TitleKey` 与 `Nav.TitleKey.Subtitle`（`Visible(...)` 会自动派生 `SubtitleKey`，缺键时 `CreateSection` 抛 `导航路由 X 没有完整的可见导航元数据。`，`NavigationRoute.cs:72`）；
6. 若页面托管 `MasterModuleTemplate`，二选一：内嵌模板的「壳页面」写法（§3.2，需另注册页面类型）或直接把 `moduleKey/moduleTitle/moduleSubtitle` 写在路由上（§3.2 后 4 行的写法，`PageType` 直接是 `typeof(MasterModuleTemplate)`）；
7. 若该页要出现在 `DashboardViewModel` 的导航逻辑里（`NavigateToModule` 等），**还要改 `BuildNavigationSections()`（`:2638–2675`）**——否则会命中 §2.4 的「两套集合不同步」；并注意当前它只有 22 条，比 catalog 的 24 条少。

第 2/3/4 步任意一处漏改，都会命中 `CreateHostMap` 的一致性校验而在**窗口构造期抛异常**（该异常会被 `OnDispatcherUnhandledException` 吞掉 → 表现为「启动后没有任何窗口」，见 01 文档 §5 B2/D6 与 §10 第 1/6 条）。

---

## 9. 本文件的核对依据（可复现）

| 断言 | 复核命令/文件 |
| --- | --- |
| 24 可见 + 3 内部 = 27 条路由 | 读 `NavigationCatalog.CreateRoutes()`（`NavigationCatalog.cs:78–132`，路由字面量 `:87–114`）逐条点数；测试里**两个** `[Fact]` 分工：`NavigationCatalogTests.cs:9–24` 的 `Catalog_contains_24_visible_routes_and_27_unique_hosts` 断言 `:14` `VisibleRoutes.Count == 24`、`:15` `Routes.Count == 27`、`:22` `VisibleSections.Count == 24`（**24/27/24**）；`:42–52` 的 `Internal_routes_are_resolvable_but_not_visible` 只断言三条内部路由 `IsVisible == false`（`:47–49`）且不出现在 `VisibleSections`（`:50–51`），**不包含任何数字断言** |
| 27 个宿主与 XAML 一一对应 | `MainWindow.xaml:648–674` 的 `ContentControl x:Name=` 共 27 个；`MainWindow.CreateHostMap()`（`:118–149`）27 行；`CreateHostMap` 的一致性校验 `:151–155` |
| 12 个页面内嵌 `MasterModuleTemplate` | `Views/*.xaml` 搜 `<views:MasterModuleTemplate ModuleKey="…"`，命中 12 个文件 |
| 4 个壳页面是孤儿 | `CreateRoutes()` 里搜不到 `typeof(SystemTuningPage)` / `typeof(ExplorerOptimizationPage)` / `typeof(TaskSchedulerPage)` / `typeof(ServicesOptimizationPage)`；`App.xaml.cs` 里也没有它们的注册 |
| 41 个 `*Dialog.xaml` | `Get-ChildItem GameOptimizer.Shell\Views -Filter *Dialog.xaml` 计数 |
| 45 个 `: Window` 派生类 | 全 `GameOptimizer.Shell`（排除 `obj/`）搜 `class \w+ : Window` 计 45，其中 41 个 `*Dialog` + `OperationDetailCardDialog` + `ModernMessageBox` + `SplashWindow` + `MainWindow` |
| `ModernMessageBox` 无外部调用 | 全仓库搜 `ModernMessageBox`：`Controls/ModernMessageBox.xaml.cs:12/16/127/138` + `Controls/ModernMessageBox.xaml:1`（`x:Class`），**没有任何产品代码引用它** |
| `NavigationSections` 只有 22 条 | `BuildNavigationSections()`（`DashboardViewModel.cs:2638–2675`）内 `NavigationSections.Add(new NavigationSectionModel(` 计数 = 22 |
| 27 条路由 → 页面 → VM → DI 注册全覆盖 | §3.2 后的覆盖核对表逐行对 `NavigationCatalog.cs:87–114`、§3.1/§3.2、`App.xaml.cs:289/325–383` 三处点位；反向只剩 `StartupManagerPage`（379）在册未挂路由 |
| 12 个壳页代码后置只含 `InitializeComponent()` | 逐个读 `Views/{Memory,Audio,PerformanceBoost,Cpu,Graphics,Network,Peripheral,SystemTuning,Explorer,TaskScheduler,Services}OptimizationPage.xaml.cs` + `Views/PrivacyShieldPage.xaml.cs`（**12–23 行/文件**：`PerformanceBoostPage`=12、其余 9 个=13、`MemoryOptimizationPage`=20、`GraphicsOptimizationPage`=23）；其中 `PrivacyShieldPage.xaml.cs` 是**文件名与页面名不一致**的那一个（没有 `PrivacyOptimizationPage.*`）；`MemoryOptimizationPage` 多一段 8 行 `///` 注释（`:6–13`），仅 `GraphicsOptimizationPage` 多两个空 `OnLoaded`/`OnUnloaded`（`:14`/`:19`，函数体只有注释） |
| `ModuleKeyToNavKeyMap` 20 条、`ModuleMetadata` 13 条 | `DashboardViewModel.cs:301–323`（20 行映射）与 `:246–261`（`new ModuleVisual[13]`） |
| 「清理垃圾」按钮链路空转、三个命令无调用点 | 全仓库搜 `NavigateToSystemCleanupCommand` → 仅 `DashboardViewModel.cs:1067`（声明与命令包装）与 `MemoryControlPanel.xaml.cs:532`（唯一调用）；搜 `NavigateRestorePointCommand` / `NavigateToRepairCenterCommand` / `NavigateGameModeCommand` → 只有 `DashboardViewModel.cs:1051/1055/1063` 的声明，**XAML 与其它代码里零绑定零调用** |
