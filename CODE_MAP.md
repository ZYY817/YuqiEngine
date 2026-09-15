# 宇奇引擎 · 全仓代码地图（主索引）

> 用途：编程改功能、排障定位时，**先查本文件夹职责表**，再按需下钻到 `docs/code-map/` 的专题深潜文档。
> 主源码根目录：`D:\优化软件\YHRJ - 副本`（活跃开发树）。所有相对路径均以该目录为根，除非另有说明。
> 生成方式：对工作树做全量目录扫描 + 关键入口阅读（2026-09）；细粒度行号锚点见各专题文档。

---

## 0. 30 秒导航：我现在要干什么 → 去哪

| 你想做的事 | 第一站 | 下钻 |
| --- | --- | --- |
| 改某个页面 / 按钮 / 对话框 | `GameOptimizer.Shell/Views/`（29 个 `*Page` + 41 个 `*Dialog`） | `02-ui-pages-and-viewmodels.md` 路由总表 |
| 改页面背后的逻辑 | **先分两类**：① 有独立 VM 的页 → `Shell/ViewModels/XxxViewModel`；② 大多数优化页 → 共用 `DashboardViewModel` + `MasterModuleTemplate`（见 §6.3） | 同上 |
| 改「会不会写入系统」的门禁 / 能力探测 | `GameOptimizer.Core/Contracts/Changes/` + `Core/Services/Changes/ChangeOrchestrator.cs` | `03-core-domains.md` §1、`04-platform-and-privilege.md` |
| 改注册表 / 电源 / 网络等 Windows 平台实现 | `GameOptimizer.Infrastructure.Windows/` | `03-core-domains.md` |
| 改优化开关的业务操作（Orchestrate*） | `GameOptimizer.Core/Optimization/Operations/`（约 60 个） | `03-core-domains.md` |
| 查「为什么显示需要管理员」 | `Core/Services/WindowsElevation.cs` → `Shell/Services/UserPrivilegeService.cs` | `04-platform-and-privilege.md`、`91-admin-gating-inventory.md` |
| 查「点了没反应 / 启动失败」 | `Shell/Program.cs` → `App.xaml.cs` → `Startup/StartupOrchestrator.cs` | `01-startup-and-shell.md`、`90-symptom-index.md` |
| 改导航路由 / 新增页面 | `Shell/Navigation/NavigationCatalog.cs` + `MainWindow.xaml` 宿主表 | `02-ui-pages-and-viewmodels.md` §1–2 |
| 改主题 / 配色 / 控件样式 | `Shell/Themes/`、`Shell/Controls/`、`Shell/Theming/` | — |
| 改中英文文案 | `Core/Localization/`（核心侧）+ `Shell/Localization/LocalizeExtension` | — |
| 改首跑基线 / 备份授权门 | `Shell/Services/Baseline/`（66 文件） | `03-core-domains.md` |
| 改测试 | `GameOptimizer.Tests/<对应域>/` | `05-engineering-pipeline.md` §6 |
| 构建 / 发布 / 安装包 | `eng/Publish-Release.ps1`、`installer/YuqiEngine.iss` | `05-engineering-pipeline.md` |

---

## 1. 工作区全景（`D:\优化软件`）

| 路径 | 性质 | 说明 |
| --- | --- | --- |
| **`YHRJ - 副本/`** | **主源码（活跃）** | 6 个正式工程 + tests + docs/code-map + eng + installer；本地图全部相对此根 |
| `YuqiEngine-GitHub-Upload/source-v0.1.3-preview/` | 冻结导出快照 | 与主树近同构（cs 约 949 vs 951）；**不要在此改代码**，发布对照可用 |
| `YuqiEngine-private-archive-20260829/` | 私有归档 | 已移出公开的开发工具/探针，**非当前源码** |
| `_build/`、`_build-check/`、`_obj/`、`_构建验证/` | 构建产物 | 忽略 |
| `_安全快照/`、`_视觉修复快照/`、`_恢复盘点/` | 验收/快照证据 | 忽略；`Directory.Build.targets` 会把 `_evidence*` 等从编译中剔除 |
| 根下 `*.log`、`_baseline-tests.log` 等 | 日志残留 | 忽略 |

主树根下还有：`artifacts/`（release/测试输出）、`.build-check-bin/`、`_dshbuild/`、`.yuqi-team/`——均为构建/验收残留，**不是源码**。

---

## 2. 工程分层与依赖方向

```
GameOptimizer.ModuleContracts          纯插件契约（net8.0，无工程引用）
        ▲
GameOptimizer.Core                     领域逻辑 + 契约 + 变更编排（无 UI）
        ▲
GameOptimizer.Infrastructure.Windows   Windows 平台实现（注册表/P/Invoke/文件持久化）
GameOptimizer.Modules.PerformanceHealth  示例/示例插件模块（→ Contracts + Core）
        ▲
GameOptimizer.Shell                    WPF 外壳 + DI 组合根 + Baseline 门（WinExe → YuqiEngine.exe）
        ▲
GameOptimizer.Tests                    xUnit 测试（→ Core + Infra + Shell）

GameOptimizer.Modules/                 ⚠️ 不是工程：.cs 被 Shell.csproj 以 Compile Include 链入
```

要点：

- **构建/测试统一用** `GameOptimizer.sln` 或 `GameOptimizer.Main.sln`（同 6 工程，仅 GUID/顺序不同）。
- Shell 入口：`StartupObject = GameOptimizer.Shell.Program`（自定义 `Main`，不是 WPF 默认）。
- 版本权威：`GameOptimizer.Shell/GameOptimizer.Shell.csproj`（当前 `0.1.3-preview`）。
- 技术栈：.NET 8 / WPF / CommunityToolkit.Mvvm / Microsoft.Extensions.Hosting / NAudio / System.Management。

---

## 3. 顶层目录一览（主树）

| 目录 | 角色 |
| --- | --- |
| `GameOptimizer.ModuleContracts/` | 插件接口：`IModule` / `IModuleAction` / `IModuleContext` / `IModuleWidget` + 描述符模型 |
| `GameOptimizer.Core/` | 最大领域层：服务、契约、优化引擎、安全边界、本地化、诊断 |
| `GameOptimizer.Infrastructure.Windows/` | Core 契约的 Windows 实现：注册表能力、电源平台、文件变更存储等 |
| `GameOptimizer.Modules/` | 外部模块装载器源码（链接编入 Shell）+ `modules.json` |
| `GameOptimizer.Modules.PerformanceHealth/` | 性能健康卡片示例模块（真实工程） |
| `GameOptimizer.Shell/` | WPF 应用：UI、导航、ViewModel、Baseline、电源计划编辑等 |
| `GameOptimizer.Tests/` | 按 24 个业务域镜像组织的测试 |
| `eng/` | 发布脚本 `Publish-Release.ps1`、证据隔离自检 |
| `installer/` | Inno Setup：`YuqiEngine.iss` + 中文语言包 |
| `docs/code-map/` | **本套代码地图专题文档**（01–05 + 症状索引） |
| `assets/` | README 截图等静态资源（非运行时） |

---

## 4. GameOptimizer.Core — 领域与契约

> 规模约：328 `.cs` / ~77k 行。DI 入口：`Extensions/ServiceCollectionExtensions.cs` → `AddGameOptimizerCore()`。

### 4.1 一级目录

| 子目录 | 职责 | 代表类型 |
| --- | --- | --- |
| `Constants/` | 路径、电源 GUID、注册表路径、超时 | `AppPaths`（日志/数据根/环境变量 `YUQIENGINE_DATA_ROOT`）、`PowerGuids`、`RegistryPaths` |
| `Contracts/Changes/` | **统一变更写入契约** | `IChangeCapability`、`IChangeOrchestrator`、`IChangeExecutionGate`、`ChangeModels`、`RecoveryIndexContracts` |
| `Diagnostics/` | 诊断包、错误引擎、用户可读错误映射 | `DiagnosticLogService`、`ErrorDiagnosticEngine`、`UserFriendlyErrorMapper` |
| `Events/` | 进程内事件总线 | `IEventBus`、`InMemoryEventBus` |
| `Extensions/` | Core DI 装配 | `ServiceCollectionExtensions.AddGameOptimizerCore` |
| `FileShellSafety/` | 文件/Shell 操作 Fail-Closed 边界 | `FileShellSafetyOwner` |
| `GraphicsSafety/` | 显卡相关 Fail-Closed 边界 | `GraphicsSafetyBoundary`、`FailClosedGraphicsOptimizationOperation` |
| `Localization/` | 中英本地化与键表 | `LocalizationService`、`Strings.zh-CN.json` / `Strings.en-US.json` |
| `Models/` | 公共 DTO | CPU 拓扑、游戏档案、GPU 清单 |
| `Modules/` | 模块宿主 | `ModuleHost`、`ModuleContext` |
| `Optimization/` | **优化引擎** | `OptimizationEngine`、`Operations/`（~60 个 Orchestrate*）、`Providers/BuiltInOptimizationOperationProvider`、`Profiles/` |
| `PowerManagement/` | 电源 Owner / 确认授权抽象 | `PowerManagementOwner`、`FailClosedPowerOptimizationOperation` |
| `Security/` | 安全缓解 Owner | `SecurityProtectionOwner` |
| `SystemRestore/` | 还原点 Owner 与对账契约 | `SystemRestorePointOwner` |
| `Theming/` | 主题服务与调色板 | `ThemeService`、`ThemePalette` |
| `Utilities/` | 安全文件操作、错误分析 | `SafeFileOperations`、`OperationErrorAnalyzer` |

### 4.2 `Services/` — 最大业务块

**根文件（安全原语 + 横切服务）**  
`WindowsElevation`（提权唯一权威）、`SystemGuard`、`PermissionChecker`、`CompatibilityChecker`、`SafeRegistryHelper` / `SafeFileWriter` / `SafeProcessHelper` / `SafeServiceHelper` / `SafeWmiHelper`、`ProcessRunner`、`CommandSanitizer`、`OperationLock`、`RegistryTransaction`、`PowerPlanService`、`CpuTopologyService`、`GpuInventoryService`、`SystemHealthService` 等。

**子目录速查**

| 子目录 | 职责 | 对应 Windows 实现（Infra） |
| --- | --- | --- |
| `Changes/` | **变更编排实现**（`ChangeOrchestrator` ~1251 行、`DenyChangeExecutionGate`、内存/文件恢复仓库） | `Infra/Changes/FileChangeStateStore` |
| `Memory/` | 内存清理、进程终止、MmAgent | `WindowsMemoryCleaningPlatform` |
| `SystemCleanup/` | 系统清理 + 隔离区 + 路径策略 | —（授权门在 Shell Baseline） |
| `NetworkReset/` / `NetworkDiagnostics/` | 网络重置 / DHCP 诊断 | `WindowsNetworkResetPlatform` / `WindowsNetworkAdapterDhcpPlatform` |
| `Repair/`（+`RepairOperations/`） | 修复中心目录与编排执行 | 经 Changes 编排器 |
| `Snapshot/` | 全量备份 / 快照 / 恢复安全 | — |
| `Startup/` | 托管自启契约 | `WindowsManagedAutostartInfrastructure` |
| `Appx/`、`ScheduledTasks/`、`SystemServices/`、`Bcd/` | Appx / 计划任务 / 服务 / BCD 变更能力 | `Services/Windows*Accessor` 等 |
| `CpuAffinity/`、`ProcessPriority/`、`ProcessTuning/` | CPU 亲和 / 优先级 / 进程调优 | `WindowsProcessTuningPlatform` 等 |
| `Audio/`、`Latency/`、`AdvancedTuning/` | 音频、输入延迟、高级调优门面 | — |
| `Hosts/`、`DeviceRepair/`、`ApplicationRepair/`、`MaintenanceDiagnostics/`、`StorageSafety/` | 各 Owner 契约 | 各 `Windows*Platform/Owner` |

### 4.3 `Optimization/Operations/`

约 60 个操作类（`*Operation.cs`），命名多为「场景 + Operation」：注册表多值、服务组合、netsh、GPU 厂商（Amd/Intel）、设备开关、清理、DNS、BCD 等。  
**约定**：真正改系统时优先经 `IChangeOrchestrator`（能力 Probe→Prepare→Execute→Restore），而不是直接散落写注册表。

---

## 5. GameOptimizer.Infrastructure.Windows — 平台实现

> 约 62 `.cs` / ~14k 行。DI 入口：`Extensions/ServiceCollectionExtensions.cs`（`AddGameOptimizerWindowsInfrastructure` 等）。

| 子目录 | 职责 |
| --- | --- |
| **`Changes/`** | 持久化变更状态：`FileChangeStateStore`、`DurableChangeDocument`、`FileChangeResourceLockProvider`（替换 Core 内存回退） |
| `Registry/` | **注册表能力中枢**（~20 文件）：Dword/String/ValueSet Accessor + Capability + Codec + Definitions |
| `Services/` | Win32 Accessor：服务、BCD、计划任务、Appx、Hosts、MmAgent、设备状态 |
| `PowerManagement/` | `WindowsPowerPlatform`、托管电源计划存储（DPAPI）、`PowerCfgProcessRunner`、电源变更 Capability |
| `Memory/`、`NetworkDiagnostics/`、`NetworkReset/`、`ProcessTuning/`、`Startup/`、`StorageSafety/`、`SystemRestore/`、`ApplicationRepair/`、`DeviceRepair/`、`MaintenanceDiagnostics/` | 与 Core `Services/*` 一一镜像的平台实现 |
| `Extensions/` | 全部 Windows 侧 DI 扩展 |

**依赖铁律**：Infrastructure 依赖 Core；Core **不得**引用 Shell/WPF。新增系统写入能力时，通常需要 Core 契约 + Infra 实现 +（可选）Optimization Operation 三处齐改。

---

## 6. GameOptimizer.Shell — 外壳与 UI

> 约 353 `.cs` + 109 `.xaml` / ~81k 行。产物：`YuqiEngine.exe`。

### 6.1 入口三件套

| 文件 | 角色 |
| --- | --- |
| `Program.cs` | 自定义 Main：数据根隔离、`--system-restore-writer` 子进程旁路、OS/DLL/可写预检、`startup_diagnostic.log` |
| `App.xaml.cs` | **DI 组合根**（`ConfigureServices` 内联）、全局异常、主题、MainWindow 创建 |
| `MainWindow.xaml(.cs)` | 主壳：导航宿主 ContentControl 表、托盘、关闭确认、Admin 状态显示 |

### 6.2 一级目录

| 目录 | 职责 |
| --- | --- |
| `Navigation/` | `NavigationCatalog`（路由表）、`PageFactory`、`NavigationRouter` |
| `Views/` | 页面与对话框（~74 cs + 73 xaml） |
| `ViewModels/` | 17 个根 VM（Dashboard、CpuCoreManagement partial×4、GameModeHub…） |
| `Services/` | ~70 个应用服务 + **`Baseline/` 66 文件** + `PowerPlan/` |
| `Controls/` | 自定义控件（AuroraCard、GlowButton、ModernToggle…） |
| `Themes/` | DarkTheme 等 7 个资源字典（App.xaml 合并） |
| `Models/` | UI 模型（设置、进度、确认、电源、游戏档案…） |
| `Startup/` | `StartupOrchestrator`（单实例 Mutex、Splash、阶段） |
| `Localization/` | XAML `LocalizeExtension` |
| `Constants/`、`Converters/`、`Helpers/`、`Diagnostics/`、`Storage/`、`Assets/`、`Resources/PowerPlans/` | 图标字体、转换器、活动日志、内置 `.pow` 等 |
| `Controls/WizardV3`、`Models/WizardV3|V4`、`ViewModels|Views/GuidedOptimize|WizardV3|V4` | **空占位目录**（当前无向导 UI 实现） |

### 6.3 导航与页面命名规律（含「有没有独立 ViewModel」）

- 路由硬编码于 `NavigationCatalog.CreateRoutes()`（约 24 可见 + 3 内部）。
- 页面类名 ≈ 导航标题：`OverviewPage`、`MemoryOptimizationPage`、`EsportsModePage`、`RepairCenterPage`…
- 多模块复用壳：`MasterModuleTemplate` / `UniversalModuleTemplate`（靠 `ModuleKey` 区分 system/explorer/tasks/services）。
- 新增路由必须同步改 `MainWindow.xaml` 宿主表，否则构造期抛异常。

**页面逻辑落在哪里（已实测，勿假设 `XxxPage → XxxViewModel`）：**

| 类型 | 页面 | 逻辑位置 |
| --- | --- | --- |
| 有独立 VM | `AppxCleanup` / `BackupManager` / `CoreAccelerator` / `CpuAffinity` / `CpuCoreManagement` / `ProcessPriority` / `RepairCenter` / `RestorePoint` / `StartupManager` / `SystemCleanup` 等 | `Shell/ViewModels/XxxViewModel.cs`（CpuCoreManagement 为 4 个 partial） |
| 共用 Dashboard | `OverviewPage`（DataContext 即 `DashboardViewModel`）、`MasterModuleTemplate`（多模块壳，内部持有 `_viewModel as DashboardViewModel`） | `Shell/ViewModels/DashboardViewModel.cs`（约 2.6k+ 行，优化批量执行入口） |
| code-behind 驱动 | 多数 `*Dialog`；以及 `MemoryOptimizationPage` 等几乎无独立 VM 的 UserControl | `Views/*.xaml.cs` + 注入的服务/控件模板绑定 |
| 仅 DI 工厂 | `HardwareInfoPage`、`NetworkTestPage`、`PowerPlanEditorPage`、`SettingsPage` 等在 `App.xaml.cs` 用带参 ctor 注册 | 构造注入的服务，逻辑分散在 Services/ |

> 排障口诀：优化开关点不动 → 先看 `DashboardViewModel` / `MasterModuleTemplate`，不要只搜 `MemoryOptimizationViewModel`（**不存在**）。

### 6.4 `Services/` 重点

| 区域 | 代表 |
| --- | --- |
| 提权 UI 门面 | `UserPrivilegeService`（委托 `WindowsElevation`） |
| 页级日志 | `PageDiagnosticService` → `page_diagnostics.log` |
| 托盘 | `SystemTrayService` |
| 确认弹窗 | `WpfChangeConfirmationPrompt`、`WpfBaselineRiskAcknowledgementPrompt` |
| 基线/授权门 | `BaselineChangeExecutionGate`、各类 `Baseline*Gate`、`BaselineCatalog`、恢复对账 |
| 电源 | `UnifiedPowerService`、`PowerPlanEditorService`、`PowerScenarioService`… |
| 电竞/游戏 | `EsportsMode*`、`GameProcessMonitorService`、`GameTypeDetector` |

---

## 7. 模块系统

| 路径 | 说明 |
| --- | --- |
| `GameOptimizer.ModuleContracts/` | 插件 ABI：7 个源文件 |
| `GameOptimizer.Modules/ModuleLoader.cs` 等 | 读 `modules.json` → `Assembly.LoadFrom` → DI 注册；**文件被 Shell 链接编译** |
| `GameOptimizer.Modules/modules.json` | 目前仅登记 PerformanceHealth |
| `GameOptimizer.Modules.PerformanceHealth/` | `PerformanceHealthModule` + VM + 卡片 View |

空 manifest 与「没有模块」在 UI 上不可区分——发布包缺 `modules.json` 时表现为「整块模块消失」。

---

## 8. GameOptimizer.Tests

- 按业务域镜像 Core/Shell 能力：`Changes/`（最大，~92）、`Shell/`、`PowerManagement/`、`Snapshot/`、`Baseline/`、`Optimization/`、`Visual/`、`Navigation/`、`Architecture/` 等约 24 个一级目录。
- 命名：`{类型/场景}Tests.cs`，后缀 `Contract` / `Boundary` / `Regression` / `Workflow` / `Safety` / `Concurrency` 表达意图。
- 运行：先 `dotnet build GameOptimizer.sln -c Release`，再 `dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Release --no-build`。
- 详见 `05-engineering-pipeline.md`。

---

## 9. 统一变更写入地基（必读心智模型）

几乎所有「会改机器状态」的功能都走同一条链：

```
UI / ViewModel
    → Optimization Operation 或 Capability
        → IChangeOrchestrator（Plan → Prepare → Execute → Restore）
            → IChangeExecutionGate（Shell: BaselineChangeExecutionGate；Core 默认 Deny）
            → IChangeCapability（Infra 实现 Probe/Read/Prepare/Execute/Restore）
            → FileChangeStateStore（持久恢复材料）
```

关键文件：

1. `Core/Contracts/Changes/IChangeCapability.cs`
2. `Core/Services/Changes/ChangeOrchestrator.cs`
3. `Core/Services/Changes/DenyChangeExecutionGate.cs`（默认拒绝）
4. `Shell/Services/Baseline/BaselineChangeExecutionGate.cs`（应用侧真正放行）
5. `Infra/Changes/FileChangeStateStore.cs`

DI 顺序敏感：**Infrastructure 必须在 `AddGameOptimizerCore()` 之前注册**，否则 TryAdd 会落到 Core 的拒绝/内存空实现，表现为「功能全灰但不报错」。

---

## 10. 提权 / 权限（统一 Elevation 体系）

| 关注点 | 位置 |
| --- | --- |
| 令牌真相 | `Core/Services/WindowsElevation.cs` |
| **体系唯一入口** | `Core/Services/ElevationGate.cs`（Evaluate / DescribeWriteDenial / AttributeFailure / ShortLabel） |
| 转发层 | `PermissionChecker` → `ElevationGate`；`SystemGuard` → `WindowsElevation` |
| UI 门面 | `Shell/Services/UserPrivilegeService`（含 `EnsureAuthoritativeSnapshot`） |
| UI 开关 | `OptimizationToggleModel`：已提权时忽略过期 `PermissionRequired` |
| 架构守护 | `GameOptimizer.Tests/Shell/ElevationArchitectureTests.cs`（禁止生产代码硬编码「请以管理员身份运行程序」） |
| 清单 | `docs/code-map/91-admin-gating-inventory.md` |

**约定**：任何新的写入失败文案、权限门禁、失败归因，必须走 `ElevationGate`，不要在业务文件里写「请以管理员身份运行」。

---

## 11. 日志与数据落点

| 内容 | 位置（默认） |
| --- | --- |
| 数据根 | `AppPaths.AppDataRoot`（可被环境变量 `YUQIENGINE_DATA_ROOT` 覆盖） |
| 启动诊断 | `<LogsDir>/startup_diagnostic.log` |
| 页面诊断 | `<LogsDir>/page_diagnostics.log` |
| 崩溃 | `<AppDataRoot>/crash.log`（另有 BaseDirectory fallback） |
| 发布产物 | `artifacts/release/v<Version>/{portable,installer,checksums}` |

---

## 12. 工程链路（一句话）

```powershell
# 构建（推荐串行，避免 obj 并发）
dotnet build GameOptimizer.sln -c Release -m:1 -p:BuildInParallel=false

# 测试
dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Release --no-build

# 发布便携版 + 安装包
pwsh -File eng\Publish-Release.ps1 -Version 0.1.3-preview
```

Release 开启 `TreatWarningsAsErrors`；`Directory.Build.targets` 强制证据隔离 glob，勿把 `_evidence` / `*.bak` 类路径加入编译。

---

## 13. 专题深潜索引（`docs/code-map/`）

| 文档 | 覆盖 |
| --- | --- |
| [`01-startup-and-shell.md`](./docs/code-map/01-startup-and-shell.md) | 启动链路、失败被吞点、DI 阶段表 |
| [`02-ui-pages-and-viewmodels.md`](./docs/code-map/02-ui-pages-and-viewmodels.md) | 27 条路由、页面↔VM↔服务、对话框 |
| [`03-core-domains.md`](./docs/code-map/03-core-domains.md) | 九大业务域入口/契约/状态/耦合 |
| [`04-platform-and-privilege.md`](./docs/code-map/04-platform-and-privilege.md) | 提权判定、P/Invoke、门禁调用方 |
| [`05-engineering-pipeline.md`](./docs/code-map/05-engineering-pipeline.md) | 构建/发布/安装器/测试布局 |
| [`90-symptom-index.md`](./docs/code-map/90-symptom-index.md) | 症状 → 位置 → 测试 |
| [`91-admin-gating-inventory.md`](./docs/code-map/91-admin-gating-inventory.md) | 管理员门禁清单 |
| [`README.md`](./docs/code-map/README.md) | 旧版地图索引与核查状态 |

本文件（`CODE_MAP.md`）是**目录级总览与快速入口**；行号级细节以专题文档为准，专题文档行号以各自标注的工作树快照为准。

---

## 14. 维护本地图的约定

1. 改动后若目录职责变化（新增一级/二级域、空目录落地、工程增删），更新本文件对应表格。
2. 行号、具体方法签名不写在本文件，写进 `docs/code-map/01–05`。
3. 不把 `bin` / `obj` / `artifacts` / `.build-check*` / `_dshbuild` 写入源码职责表。
4. 活跃开发只在 `YHRJ - 副本`；`YuqiEngine-GitHub-Upload` 仅作对照快照。

---

## 15. 覆盖范围与核验状态（诚实声明）

### 15.1 本地图是什么 / 不是什么

| 本地图保证 | 本地图不保证 |
| --- | --- |
| 每个**工程 / 一级·二级业务目录**有职责说明与代表类型 | **不会**为 1061 个源文件逐文件建索引（会腐烂，也不利于日常定位） |
| 变更写入、提权、导航、启动、发布等**跨切主链路**可按图索骥 | 行号级精确到每一行——那在 `docs/code-map/01–05`，且会随提交漂移 |
| 关键入口文件与类型名已抽查通过 | 未对每个 `*Operation` / 每个 Dialog 的内部字段做穷举 |
| 路径引用经存在性检查 | 空占位目录（WizardV3/V4 等）无实现可定位 |

### 15.2 已执行的核验（2026-09-15）

| 检查项 | 结果 |
| --- | --- |
| 地图 + `docs/code-map` 中引用的仓库相对路径 `Test-Path` | **195 / 198 命中**；3 条“缺失”为正则把 `.csproj` 截成 `.cs` 的误报，对应 `*.csproj` **均存在** |
| 26 个关键类型文件是否存在 | **26 / 26 存在** |
| 7 个关键类名是否真在对应文件中声明 | **全部命中**（如 `WindowsElevation` L51、`ChangeOrchestrator` L12、`DashboardViewModel` L44） |
| 源文件总量（排除 bin/obj/artifacts 等） | **1061** 个 `.cs`/`.xaml` |
| ViewModels 数量 | 17 个根 VM 文件；**多数优化页没有同名 VM**（见 §6.3） |

### 15.3 源文件规模快照（定位时用的数量感）

| 工程 | `.cs`+`.xaml`（排除构建产物） |
| --- | --- |
| GameOptimizer.Shell | 462（含 109 xaml） |
| GameOptimizer.Core | 328 |
| GameOptimizer.Tests | 195 |
| GameOptimizer.Infrastructure.Windows | 62 |
| ModuleContracts + Modules + PerformanceHealth | 7 + 4 + 3 |
| **合计** | **1061** |

`Infrastructure.Windows` 仅 62 个 `.cs`，可在需要时做**文件级**对照；Shell 的 `Views`/`Services`/`Models` 体量大，应按目录 + 类名搜索定位，而不是背全文索引。

### 15.4 已知会削弱「全自动定位」的结构特性

1. **大量页面共用 `DashboardViewModel` / `MasterModuleTemplate`**——按页名搜 VM 会扑空（§6.3）。
2. **`Optimization/Operations/` ~60 个操作类**只在地图中按目录聚合，未逐类列表；要找具体开关对应操作，用 `BuiltInOptimizationOperationProvider` / operationId / `moduleKey` 反查更稳。
3. **对话框 41 个**多数无独立 VM，靠 `Views/*Dialog.xaml.cs` code-behind。
4. **`docs/code-map` 行号**以专题文档自述的工作树快照为准；大改后以类型/方法名重新定位。
5. 测试 `Changes/` 有 ~92 个文件，地图只到目录级；跑单测用 `--filter FullyQualifiedName~...`。

### 15.5 若你要「逐文件级」索引

当前地图刻意不做全文件枚举。若确有需要，更可持续的做法是：

- IDE 全局符号搜索（`Ctrl+T`）+ 本地图目录职责；
- 或后续加一份**机器生成**的 `docs/code-map/file-inventory.md`（仅文件名+路径，不写描述），改代码后用脚本重生成，避免手维护腐烂。

需要的话可以说一声，我可以补一版自动生成的文件清单。
