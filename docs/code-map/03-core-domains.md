# 03 — Core 业务域地图

> 本文档回答一个问题：**当你想改某个业务功能时，代码逻辑在哪里、谁拥有状态、它会牵动谁。**
>
> 覆盖九个业务域：内存清理、系统清理、网络、电源、游戏模式、硬件信息、还原点、安全、诊断。
>
> 每个域固定给出六项：**入口 → 契约接口 → 状态所有者 → 依赖方向 → 共享可变状态 → 跨域耦合 + 共享工具使用点**。

---

## 0. 阅读约定与证据强度

### 0.1 证据标记

| 标记 | 含义 |
|---|---|
| 无标记 | 已直接读取源码行确认（含行号） |
| **未验证** | 仅由 grep 命中数、命名或测试断言推断，未逐行确认 |
| **不确定** | 有相互矛盾或覆盖不全的迹象，需人工复核 |

### 0.2 分层与依赖总则

```
GameOptimizer.ModuleContracts / .Abstractions / .Enums / .Models   （纯契约）
        ▲
GameOptimizer.Core                    （契约 + Owner + 领域逻辑，无 UI）
        ▲
GameOptimizer.Infrastructure.Windows  （Windows 平台适配：注册表/P/Invoke/WMI）
GameOptimizer.Modules / .PerformanceHealth
        ▲
GameOptimizer.Shell                   （WPF UI + 组合根 + 基线门实现 + 隔离写入进程宿主）
```

**已确认的结构性结论（九域一致）：Core 层不存在对 Shell 的类型上向依赖。**
全仓库 `GameOptimizer.Core` 内检索 `GameOptimizer.Shell` 仅 1 处命中，且是字符串字面量——`GameOptimizer.Core/Services/Memory/ProcessCleanerService.cs:64`（进程名自保护白名单），非编译依赖。

因此任何"Core 里 `new` 了 Shell 类型"或"Core 引用 WPF"都是新引入的架构违规。

### 0.3 组合根与注册顺序（九域共同前提）

所有域的实际装配发生在 `GameOptimizer.Shell/App.xaml.cs`：

| 行 | 内容 |
|---|---|
| L226 | `AddGameOptimizerWindowsInfrastructure()` |
| L227 | `AddMemoryCleaningWindowsInfrastructure()` |
| L228 | `AddSystemRestorePointWindowsInfrastructure(...)` |
| L232 | `AddPowerManagementWindowsInfrastructure(powerRecoveryDirectory)` |
| L233 | `AddManagedAutostartWindowsInfrastructure()` |
| **L239** | **`AddGameOptimizerCore()`** |
| L240-249 | `IHomeTelemetrySource` → `ISystemInsightService as IHomeTelemetrySource`；验收模式替换为 `AcceptanceUnavailableHomeTelemetrySource`（L707-714） |
| L251 | `IChangeConfirmationPrompt` → `WpfChangeConfirmationPrompt`（覆盖 Core 的 `DenyChangeConfirmationPrompt`） |
| L254 | `IUserPrivilegeService` → `UserPrivilegeService`（提权状态的 UI 门面；唯一权威判定见 §11.2）；`:568-573` 启动时读它决定是否打"以非管理员运行"告警 |
| L262-293 | 基线门层 + 清理/隔离/修复门 |
| L301 | `AddNetworkResetComposition(() => Current?.MainWindow)` |
| L309-312 | `DiagnosticLogService` 覆盖（自定义日志目录）+ `DiagnosticPackageService` |
| L325-395 | 页面/ViewModel 单例 |
| L399 | `RegisterModuleExtensions()` |
| L423-424 | `IDiagnosticLogService` 解析 + `LogRetentionService.ApplyRetentionPolicy()` |
| L849 | `ServiceCollectionExtensions.InitializePowerOperationDiagnostics(_host.Services)` |

**顺序是关键**：Infrastructure 先注册，Core 用 `TryAddSingleton`（`GameOptimizer.Core/Extensions/ServiceCollectionExtensions.cs:48-55`）作为兜底，Shell 再覆盖部分注册。**把 Infrastructure 挪到 `AddGameOptimizerCore()` 之后，会静默退化为 Core 的空实现**（拒绝式 store / `Deny*` 门），表现为"功能全部被阻断但不报错"。

**同名重复注册的覆盖方向**（易踩）：
- `ISystemCleanupService`：Core 注册（`Core/Extensions/ServiceCollectionExtensions.cs:116`）→ **Shell 于 `App.xaml.cs:275` 再注册一次同名实现**，`GetRequiredService` 取后注册者。两处实现类型相同（`SystemCleanupService`），差别在构造：DI 走 `SystemCleanupService.cs:24-26` 的 `(ICleanupAuthorizationGate, ICleanupQuarantine)` ctor，因此拿到的是 Shell 在 `:272`/`:273` 注册的 `BaselineCleanupAuthorizationGate` + `FileSystemCleanupQuarantine`；`SystemCleanupService.cs:16` 的无参 ctor（`DenyCleanupAuthorizationGate` + `FileSystemCleanupQuarantine`）只被手工 `new` 使用。
- `IDiagnosticLogService`：Core 注册（`:72`）→ Shell 覆盖（`App.xaml.cs:309-311`），带自定义 `LogBackupDirectory`。
- `AutoRepairService` / `RepairItemRegistry`：Shell 用 `services.Replace(...)` 覆盖（`Shell/Extensions/NetworkResetServiceCollectionExtensions.cs:48-59`）。

---

## 1. 统一写入地基（读懂九域的前置）

九个域里凡"会改机器状态"的功能，**最终都必须经过同一套中央编排器**。不理解这一层，会误判"这个功能为什么不可用"。

### 1.1 核心接口

| 类型 | 位置 | 作用 |
|---|---|---|
| `IChangeCapability` | `GameOptimizer.Core/Contracts/Changes/IChangeCapability.cs` | 能力协议：`Descriptor` / `ProbeAsync` / `ReadAsync` / `PrepareAsync` / `ExecuteAsync` / `RestoreAsync` |
| `IChangeOrchestrator` | `GameOptimizer.Core/Contracts/Changes/IChangeOrchestrator.cs` | 中央编排：Observe / Prepare / Execute / Restore |
| `IChangeCompatibilityPolicy` | 同上目录 | 兼容性裁决 |
| `IChangeExecutionGate` | 同上目录 | **强制**门；缺失即抛 |
| `IChangeConfirmationPrompt` / `IChangeConfirmationAuthority` | `IChangeCapability.cs:121-135` | 确认请求与令牌 |
| `IChangeResourceLockProvider` | 同上目录 | 跨能力资源锁 |
| `IChangeJournal` / `IChangeAuditStore` / `IChangePlanStore` | 同上目录 | 计划/回执/审计持久化 |
| `IRecoveryRepository` / `IRecoveryClaimStore` | 同上目录 | 恢复材料与 claim |

### 1.2 实现与关键常量

`GameOptimizer.Core/Services/Changes/ChangeOrchestrator.cs`（1251 行）：

- `PlanLifetime = 5 分钟`
- `ResourceLockTimeout = 15 秒`
- 构造器强制：`executionGate` 为 null 时抛 `"A change execution gate is mandatory for centralized writes."`
- 阻断码：`capability.unknown` / `scope.unsupported` / `permission.denied` / `prepare.contract-mismatch`

### 1.3 确认链

| 环节 | Core 默认 | Shell 覆盖 |
|---|---|---|
| `IChangeConfirmationPrompt` | `DenyChangeConfirmationPrompt`（恒 false，`Core/Extensions/ServiceCollectionExtensions.cs:38`） | `WpfChangeConfirmationPrompt`（`App.xaml.cs:251`） |
| `IChangeConfirmationAuthority` | `ChangeConfirmationAuthority`（Core DI `:39`） | 域内自建：网络在组合工厂内 `new ChangeConfirmationAuthority(...)`（`Shell/Extensions/NetworkResetServiceCollectionExtensions.cs:35-37`）；电源/还原点各有独立 authority 类型（`PowerConfirmationAuthority.cs:7` / `SystemRestorePointConfirmationAuthority.cs:7`） |

`ChangeConfirmationAuthority`（`Core/Services/Changes/ChangeConfirmationAuthority.cs:6-96`）：`_issued` 为 `ConcurrentDictionary<Guid, IssuedConfirmation>`，**进程内**；`:87` `TryRemove` 保证单次消费；重启后所有未消费令牌失效（durable plan 仍在）。

### 1.4 五个基线门（Shell 实现 Core 端口）

| Core 端口 | Shell 实现 | 注册 |
|---|---|---|
| `IPowerBaselineGate` | `BaselinePowerGate`（`Shell/Services/PowerManagement/BaselinePowerGate.cs:8-13`） | `App.xaml.cs:264` |
| `IManagedAutostartGate` | `BaselineManagedAutostartGate` | `App.xaml.cs:266`（`:267` 为其 owner `ManagedAutostartOwner`） |
| `ISnapshotRestoreAuthorizationGate` | `BaselineSnapshotRestoreAuthorizationGate`（`BaselineSnapshotRestoreAuthorizationGate.cs:9`） | `App.xaml.cs:271` |
| `ICleanupAuthorizationGate` | `BaselineCleanupAuthorizationGate`（`BaselineCleanupAuthorizationGate.cs:9-30`） | `App.xaml.cs:272` |
| `IRepairExecutionGate` | `BaselineRepairExecutionGate` | `App.xaml.cs:291`（`:292` `IChangeExecutionGate` → `BaselineChangeExecutionGate`、`:293` `IRepairChangeExecutor`） |

均通过 `IBaselineGateService.EnsureCanOptimizeAsync(...)` 落地。**这是跨域耦合的第一大来源**：任何"需要基线授权"的域，实际都会回到基线域。

### 1.5 "两套写入面"模式（九域普遍存在）

每个域几乎都有：
- **一套活跃的 typed 写入路径**（经 `IChangeOrchestrator`）；
- **一套遗留写入器**，被降级为"语法可达、行为熔断"的墓碑（`fail-closed` 门面、`#if false`、或 `Unavailable*Owner.Instance`）。

改动时**必须确认自己改的是哪一套**。墓碑清单见 §11。

---

## 2. 内存清理（Memory Cleaning）

### 2.1 入口

| 层 | 位置 |
|---|---|
| Shell 组合根 | `App.xaml.cs:227` `AddMemoryCleaningWindowsInfrastructure()`；`:322` `MemoryModuleExtension`；`:326` `MemoryOptimizationPage` |
| Windows DI | `Infrastructure.Windows/Extensions/MemoryCleaningServiceCollectionExtensions.cs:15-29`（平台 L19、两个 capability L20-25、owner L26-28） |
| Windows DI（mmagent） | `Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs:141-148` |
| Core DI | `Core/Extensions/ServiceCollectionExtensions.cs:119-120` |
| Shell 页面 | `Shell/Views/MemoryOptimizationPage.xaml.cs:14-20`（空壳，仅 `InitializeComponent`） |
| Shell 装配 | `Shell/Extensions/MemoryModuleExtension.cs:23-31`、`:63-69` |
| Shell UI | `Shell/Views/MemoryControlPanel.xaml.cs:42-55`、`:316-330` |
| Shell 对话框 | `Shell/Views/MemoryCleanDialog.xaml.cs:138-155`、`:449`、`:459` |
| Core 写边界 | `Core/Services/Memory/MemoryCleaningOwner.cs:10-21` |

### 2.2 契约接口

| 接口 | 位置 | 关键成员 |
|---|---|---|
| `IMemoryCleanerService` | `Core/Services/Memory/MemoryCleanerService.cs:7-14` | `GetCurrentStatus` / `SmartBoostAsync` / `ClearStandbyListAsync` / `FlushWorkingSetsAsync` / `IsAdministrator` |
| `IProcessCleanerService` | `Core/Services/Memory/ProcessCleanerService.cs:33-43` | `GetProcessGroups` / `CleanProcessGroup` / `LastMutationBlockReason` / `AddToWhitelist` / `RemoveFromWhitelist` / `GetWhitelist` / `SaveWhitelist` / `LoadWhitelist`（`RecommendKeepProcesses` 是 `:57` 的 **private static 字段**，非接口成员） |
| `IMemoryCleaningOwner` | `MemoryCleaningContracts.cs:132-163` | `CaptureProcessTargetAsync` / `CaptureWorkingSetTargetAsync` / `ReadStatusAsync` / `PrepareAsync` / `RunInteractiveAsync` / `ExecuteAsync` / `RestoreAsync` |
| `IMemoryCleaningPlatform` | 同上 `:170-192` |
| `IMmAgentAccessor` | `MmAgentChangeContracts.cs:16-24`（资源 `mmagent:memory-compression`） |
| `IChangeCapability` 实现 | `MemoryCleaningCapability.cs:10`、`MmAgentChangeCapability.cs:15`（mmagent 能力定义 `MmAgentChangeCapabilityDefinition` `MmAgentChangeCapability.cs:8-13`，能力 ID `memory.disable_compression`，注册 `Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs:141-148`） |

能力 ID：`MemoryCleaningContracts.cs:194-207` — `memory.working-set.trim` / `memory.process.terminate` / `memory.standby-list.purge`。

> **已确认缺口**：`memory.process.terminate` **未注册为任何 `IChangeCapability`**（`MemoryCleaningServiceCollectionExtensions.cs:20-25` 只注册两项），Shell 侧也无 `CaptureProcessTargetAsync(ProcessTermination, …)` 调用点。该能力目前不可达。

### 2.3 状态所有者

| 类 | 可变状态 |
|---|---|
| `WindowsMemoryCleaningPlatform`（`Infrastructure.Windows/Memory/WindowsMemoryCleaningPlatform.cs:18`） | **static** `ConcurrentDictionary<string,long> TrimBaselines`（`:25`）、**static** `ConcurrentDictionary<int,MemoryProcessIdentity> TerminatedProcesses`（`:26`）——被两个 capability 实例共享 |
| `ProcessCleanerService`（`:46`） | `HashSet<string> _whitelist`（`:51`）、`_whitelistPath`（`:52`，`AppPaths.ConfigDir` + `process_whitelist.json`，`:82-85`；`LoadWhitelist` `:199`） |
| `MemoryCleanerService`（`:22`） | 无可变实例字段；所有变更方法返回 `BlockedAsync()`（`:68-75`） |
| `MemoryCleaningOwner` | 无自有持久状态；状态落在 `IChangeOrchestrator` 的计划/回执存储 |
| `MemoryCleanDialog` | `_cleanCts`（`:135`）、`_isCleaning`（`:136`）、`_beforeStatus`（`:133`），UI 生命周期内 |

**私有平台原因码**：`WindowsMemoryCleaningPlatform.cs:326` — `memory.not-reversible` / `memory.recovery-unavailable`。

### 2.4 依赖方向

- 构造注入（DI/工厂），无服务定位器。
- `MemoryCleaningOwner` ← `IChangeOrchestrator` + `IChangeConfirmationAuthority`。
- `MemoryCleaningCapability` ← `(MemoryCleaningCapabilityKind, IMemoryCleaningPlatform)`（`MemoryCleaningCapability.cs:16-44`）。
- Core 内存文件 `using` 仅 `Core.Contracts.Changes` + `Core.Constants`（`ProcessTerminationSafety.cs` 另含 `System.Diagnostics`）。
- 消费者：Shell `MemoryModuleExtension` / `MemoryControlPanel` / `MemoryCleanDialog`；测试 `GameOptimizer.Tests/Changes/MemoryCleaningBoundaryTests.cs`、`MmAgentChangeCapabilityTests.cs`、`Architecture/EvidenceIsolationArchitectureTests.cs:166-167`（禁止 `new MemoryCleanerService()` / `new ProcessCleanerService()`）。

### 2.5 共享可变状态

- 进程级：`TrimBaselines`、`TerminatedProcesses`（跨 capability 实例共享，进程内单例语义）。
- 文件：`%AppDataRoot%\Config\process_whitelist.json`——**运行期生成**（`ProcessCleanerService` ctor `:82-85` 在 `AppPaths.ConfigDir` 下创建并 `LoadWhitelist()`；工作树中不含此文件），可被外部修改的输入；`LoadWhitelist`（`:199`）在文件缺失时静默返回。
- 内存域不独占任何隔离区或日志目录。

### 2.6 跨域耦合

- 与网络/电源/安全/还原点/游戏模式：**无源码级直接引用**；唯一间接耦合是共享 `IChangeOrchestrator` / `IChangeConfirmationAuthority`。
- 自保护耦合：`ProcessTerminationSafety.cs:12-19` 静态 `SystemCriticalProcessNames`；`TryValidate`（`:21-49`；`:37-44` 以 `Process.GetCurrentProcess()` 的 PID/进程名自保护，**无 Shell 字面量**）；`GameOptimizer.Shell` 字面量在 `ProcessCleanerService.cs:64` 的 `RecommendKeepProcesses`（`:57-65`），`CategorizeProcess`（`:150-164`）在 `:157` 复用同一系统关键名单，并在 `:152-154` 以当前进程名自保护。
- mmagent capability 通过 `IChangeCapability` 聚合进中央编排器，与电源/网络能力同处一个集合。

### 2.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | **Core 内存域零使用**（grep 无命中）。Shell 侧：`MemoryControlPanel.xaml.cs:60-62` 订阅 `LanguageChanged`；`MemoryCleanDialog.xaml.cs:203-224` `UpdateLocalizedTexts()`，键 `MemClean.*` |
| 日志 | `PageDiagnosticService.Log("MemoryClean", …)` @ `MemoryCleanDialog.xaml.cs:454, 463, 469`；`Debug.WriteLine` @ `MemoryModuleExtension.cs:58`（Shell）+ `Core/Services/Memory/ProcessCleanerService.cs:106, 111, 121, 190, 195, 215, 222`（Core，7 处）。**无 `ILogger`** |
| 错误分析 | `OperationErrorAnalyzer` / `ErrorMessageHelper` / `UserFriendlyErrorMapper` / `ErrorDiagnosticEngine` / `IDiagnosticLogService` 在 Core 内存域**零命中**——结果以 `MemoryCleaningResultKind` + `ReasonCode` 自描述返回 |

> **文档腐坏实例**：`MemoryCleaningServiceCollectionExtensions.cs:8-12` 的 XML 注释称"应用启动尚未调用该扩展"——**已过时**，`App.xaml.cs:227` 已调用。

---

## 3. 系统清理（System Cleanup）

### 3.1 入口

| 层 | 位置 |
|---|---|
| Shell DI | `App.xaml.cs:272-276`（授权门 / 隔离 / 隔离管理器 / 服务 / VM）；`:333` `SystemCleanupPage` |
| Shell DI（AppX 子域） | `App.xaml.cs:277-290`（`DangerousOperationDialog.Show` 注入 `:281-287`） |
| Core DI | `Core/Extensions/ServiceCollectionExtensions.cs:116` `ISystemCleanupService`（**Shell 于 `App.xaml.cs:275` 再注册同名实现**；生效的是 Shell 那次，DI 构造注入 `ICleanupAuthorizationGate` + `ICleanupQuarantine`） |
| Core DI（注入优化目录） | 同上 `:58-67` |
| Core 目录装配 | `BuiltInOptimizationOperationProvider.cs:1557-1621` — `BuildCleanupModule`；6 项迁入 `OrchestratedFileCleanupOperation`（`:1569-1574`）；`cleanup.windows_logs` / `cleanup.recycle_bin` 永久 fail-closed（`:1588-1589`）；`cleanup.hosts_backup` 走 `OrchestratedHostsBackupOperation`（`:1577-1585`） |
| Shell VM | `Shell/ViewModels/SystemCleanupViewModel.cs:18`、`:135-157`（`:144` `?? new CleanupQuarantineManager()`；`:156` 空目录回退 `new SystemCleanupService()`） |
| Shell VM（AppX） | `Shell/ViewModels/AppxCleanupViewModel.cs:58-70` |
| Core 兼容面 | `Core/Optimization/Operations/DirectoryCleanupOperation.cs:10-35`（直删被禁用，`:27-34` 委托 `UnavailableFileShellSafetyOwner.Instance`） |

### 3.2 契约接口

| 接口 | 位置 |
|---|---|
| `ISystemCleanupService` | `Core/Services/SystemCleanup/ISystemCleanupService.cs:3-16` |
| `ICleanupAuthorizationGate` | `CleanupModels.cs:263-266` |
| `ICleanupQuarantine` | `CleanupSafety.cs:115-128` |
| `CleanupPathPolicy`（静态、无状态） | `CleanupSafety.cs:18-113`：`TryValidateRoot`（`:20-64`，拒绝空路径 `cleanup.path-empty` `:26`、卷根 `cleanup.root-forbidden` `:36`、祖先 reparse point `:43`）、`TryValidateCandidate`（`:66-110`，含 `FileAttributes.ReparsePoint` 阻断 `:89`）、`IsReparsePoint`（`:111-112`）——**隔离/恢复全路径的包含性契约** |
| `ICleanupQuarantineManager` | `CleanupQuarantineManagement.cs:135-142` |
| `IAppxUserRemovalService` | `Core/Services/Appx/AppxUserRemovalService.cs:36-42` |
| `IAppxRemovalOwner` | `Core/Services/Appx/AppxRemovalContracts.cs:50` |
| 数据契约 | `CleanupModels.cs:152-266`（`CleanupPreviewEntry.ContentSha256` `:157` / `ResourceKey` `:160`；`CreateResourceKey` `:167`；`ComputeFingerprint` `:225-237`；`CleanupPreviewConfirmation.Matches`） |

### 3.3 状态所有者

| 类 | 可变状态 |
|---|---|
| `SystemCleanupService`（`SystemCleanupService.cs:7`） | `_definitions` 不可变 allowlist（`:12`）；`ConcurrentDictionary<Guid,string> _issuedPreviews`（`:14`，`:134-139` 消费即移除）；`TimeProvider`（`:13`） |
| `CleanupQuarantineManager`（`CleanupQuarantineManagement.cs:144`） | `_root`（`:148`）、`_retentionRoot = root\RetentionHold`（`:165`）、`ConcurrentDictionary<Guid,string> _issued`（`:152`） |
| `CleanupQuarantineManifestStore`（静态工具） | 磁盘 manifest：`CurrentVersion = 2`（`CleanupQuarantineManagement.cs:610`）、`DefaultRoot = AppDataRoot\CleanupQuarantine`（`:612`）、`MaximumManifestBytes = 128*1024`（`:613`）；`MoveIntoQuarantineAsync`（`:617-660`，`File.Move` `:633` + 哈希回读 `:643-647`） |
| `SystemCleanupViewModel`（Shell） | `_previews` 字典（`:25`，须与 Core `_issuedPreviews` 同步失效）、`_restoreConfirmation`（`:24`） |
| `FileSystemCleanupQuarantine`（`CleanupSafety.cs:145`） | `_root`（`:147`，默认 `AppDataRoot\CleanupQuarantine` `:151`） |

> **实例重复风险**：`SystemCleanupViewModel` ctor `:144` / `:156` 在 DI 未注入时会各自 `new`。被 `new` 出的 `SystemCleanupService` 拥有**独立的 `_issuedPreviews`**，与 DI 单例不是同一实例——预览登记会错位。

### 3.4 依赖方向

- Core 定义接口、Shell 实现并可注入（**依赖倒置**）：`BaselineCleanupAuthorizationGate`（`Shell/Services/Baseline/BaselineCleanupAuthorizationGate.cs:9-30`，`:23-25` 调 `EnsureCanOptimizeAsync($"system-cleanup:{ItemId}:{Fingerprint}")`）。
- Core 内部：`OrchestratedFileCleanupOperation`（`Core/Optimization/Operations/OrchestratedFileCleanupOperation.cs:13-57`）→ 三个可空注入，缺失即 fail-closed（`cleanup.owner-unavailable` `:73`、`cleanup.item-not-found` `:83`）；`RequiresSnapshotForRestore => false`（`:60`）。
- **安全默认**：`SystemCleanupService()` 无参 = `new DenyCleanupAuthorizationGate()` + `new FileSystemCleanupQuarantine()`（`:16`）；拒绝码 `cleanup.baseline-gate-not-connected`（`CleanupSafety.cs:13`）。
- `CleanItemAsync` 永远拒绝（`cleanup.confirmation-required`，`:102`）——单参入口无预览绑定。

### 3.5 共享可变状态

| 对象 | 位置 / 说明 |
|---|---|
| 隔离区根 | `AppDataRoot\CleanupQuarantine`；`CleanupSafety.cs` 内：批次 `root\yyyyMMdd\<operationId>`（`:195-196`）、文件名 `SHA256(ResourceKey)[:16]-<fileName>`（`:210-212`）、跨卷阻断 `cleanup.cross-volume-quarantine-blocked`（`:215`） |
| 保留区 | `root\RetentionHold`（`CleanupQuarantineManagement.cs:165`） |
| 资源租约 | `CleanupQuarantineResourceLease`（`CleanupQuarantineManagement.cs:595`）15 秒（`CleanupSafety.cs:187`、`CleanupQuarantineManagement.cs:283`）；失败码 `cleanup.resource-lease-unavailable`（`CleanupSafety.cs:191`） |
| 漂移检测 | `EnsureEntryStillMatchesAsync` 移动前后各一次（`CleanupSafety.cs:183`、`:194`）；`CleanupPreviewDriftException`（`CleanupQuarantineManagement.cs:589`）；`cleanup.quarantine-manifest-drift`（`CleanupQuarantineManagement.cs:276`） |
| **两套残留路径并存** | 旧路径 `AppPaths.CleanupBackupsDir` = `AppDataRoot\cleanup`（`AppPaths.cs:42`），被 Shell `Services/Baseline/BaselineDomainCollector.cs:381` 使用——**与隔离区不是同一目录** |
| 进程阻塞 | `definition.BlockingProcesses` 命中即拒绝（`SystemCleanupService.cs:169-177`，本地化 `Cleanup.CloseProcessFirst` `:174`） |

### 3.6 跨域耦合

| 对端 | 耦合点 |
|---|---|
| 基线/还原点 | 清理授权经 `IBaselineGateService`；`baseline-risk-not-acknowledged`（Gate `:29`） |
| 网络 | `cleanup.hosts_backup` 使用 `IHostsBackupOwner`（Core DI `:67`；Provider `:1577-1585`） |
| 安全/网络/隐私 | `BuiltInOptimizationOperationProvider` 同时接收 `ISecurityProtectionOwner`、`INetworkResetOwner`（Core DI `:62-63`、`:67`）——**目录级共享，非清理逻辑内部耦合** |
| AppX 子域 | `appx.remove_current_user`（`AppxUserRemovalService.cs:46-49`）走独立 owner，**不使用** `ISystemCleanupService`/隔离区；StoreOnly 失败如实返回 `manual-compensation-required`（注释 `:30-34`） |
| 诊断/修复 | `IMaintenanceDiagnosticOwner`（参数 `maintenanceOwner`）被修复域消费：`Repair/RepairOperations/MemoryRepairs.cs:8-10, 20-21, 85-86, 90`、`StorageRepairs.cs:13-16, 28-29, 41-42, 54-55, 128`、`PrivacyRepairs.cs:14-17, 67-69`；缺省兜底为 `MaintenanceDiagnosticOwnerFallback.Instance`（`MemoryRepairs.cs:10`、`StorageRepairs.cs:16`），**具体类型 `MaintenanceDiagnosticOwner` 不出现在这些文件中** |

### 3.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | **Core 清理层直接使用** `LocalizationService.Instance`：`CleanupModels.cs:48-53`（`CleanupItem.{Id}.Name/Description`、`Cleanup.Risk.*`、`Cleanup.Category.*`）、`:69`（`CleanupConfirm.FileCountFmt`）、`:94`（`Cleanup.State.*`）、`:98`（`Common.Unknown`）；`SystemCleanupService.cs:174, 341`。Shell：`SystemCleanupViewModel.cs:28` 的 `Loc`（`:75-96`、`:214-218`、`:248`、`:314`、`:325`、`:357-371`、`:399-471`、`:522`） |
| 日志 | `PageDiagnosticService` @ `Shell/Views/SystemCleanupPage.xaml.cs:19, 30, 34, 38, 116, 134`；`AppxCleanupPage.xaml.cs:26, 42`；`Debug.WriteLine` @ `SystemCleanupViewModel.cs:155`；`ToastService.Instance.Success/Warning/Error` @ `:371, 441, 450, 452, 471` |
| 错误分析 | Core `Services/SystemCleanup` 内 `OperationErrorAnalyzer` / `ErrorMessageHelper` / `UserFriendlyErrorMapper` / `ErrorDiagnosticEngine` / `IDiagnosticLogService` **零命中**；错误以 `result.Errors` 原因码字符串承载（`:102, 118, 124, 130, 137, 153, 165`） |
| 硬编码中文兜底 | `Shell/Views/SystemCleanupPage.xaml.cs:135` — `ToastService.Instance.Error("系统清理", "扫描未能完成，请查看日志后重试。");`，标题与正文两个独立字面量（均未走本地化） |

---

## 4. 网络（Network）

### 4.1 入口

| 类型 | 位置 | 说明 |
|---|---|---|
| Shell 路由（模块） | `NavigationCatalog.cs:93` | `network-nav` → `NetworkModulePageHost` → `NetworkOptimizationPage`，组 `modules`，别名 `["network"]` |
| Shell 路由（工具） | `NavigationCatalog.cs:104` | `network-test` → `NetworkTestPageHost` → `NetworkTestPage`，别名 `["nettest"]` |
| Shell 页面（真实逻辑） | `Shell/Views/NetworkTestPage.xaml.cs`（820 行） | `INetworkResetOwner? _networkResetOwner`（`:25`）、`INetworkSpeedTestService`（`:26`）；ctor `:33-52`；`AttachNetworkResetOwner`（`:54-60`，拒绝二次替换） |
| Shell 页面（空壳） | `Shell/Views/NetworkOptimizationPage.xaml.cs:9-12` | 13 行，仅 `InitializeComponent()`；XAML 为 `<views:MasterModuleTemplate ModuleKey="network"/>` |
| DI | `App.xaml.cs:331`、`:338-341`、`:305`、`:301` | |
| 修复中心复用 | `Core/Services/Repair/OrchestratedRepairChangeExecutor.cs:47` | `NetworkRepairCatalog.TryGet` → 复用网络重置 owner |

**Core 入口类**：`Core/Services/NetworkReset/NetworkResetOwner.cs`（1102 行）。

### 4.2 契约接口

| 接口 | 位置 |
|---|---|
| `INetworkResetOwner` | `Core/Services/NetworkReset/NetworkResetModels.cs:214-221` |
| `INetworkResetPlatform` | 同上 `:166-175` |
| `INetworkResetBaselineGate` | 同上 `:177` |
| `INetworkResetPlanStore` | 同上 `:182` |
| `INetworkResetReceiptStore` | 同上 `:189` |
| `INetworkSpeedTestService` | `Shell/Services/NetworkSpeedTestService.cs:67-77`（**Shell 层接口**） |
| `INetworkAdapterDhcpOwner` | `Core/Services/NetworkDiagnostics/NetworkAdapterDhcpModels.cs:82`（只读证据冻结） |
| `IHostsBackupOwner` | `Core/Services/Hosts/HostsBackupContracts.cs:26-38` |
| `INetworkResetConfirmationPrompt` 适配器 | 该名称在仓库中不存在；实际适配器是 `Shell/Views/NetworkResetDialog.xaml.cs:141-153` 的 `NetworkResetConfirmationPrompt : IChangeConfirmationPrompt` |
| `ILatencyTestService` | `Core/Services/Latency/ILatencyTestService.cs:8-30` — **未接线**：无 DI 注册、无页面/XAML 绑定 |

枚举：`NetworkResetProfile`（`NetworkResetModels.cs:5-11`）、`NetworkResetCommand`（`:13-24`）。

**错误表达**：`NetworkResetOwner.cs:52` 抛 `UnauthorizedAccessException("network.reset.administrator-required")`；`:48,56` 抛 `InvalidOperationException($"{status.ReasonCode}: {status.Message}")`。

### 4.3 状态所有者

| 状态 | 位置 | 备注 |
|---|---|---|
| `NetworkResetOwner`（单例） | `Shell/Extensions/NetworkResetServiceCollectionExtensions.cs:33-46` | 组合：平台 + 基线门 + `new ChangeConfirmationAuthority(...)` + 计划 store + 回执 store + 资源锁 + `TimeProvider` |
| 重复注册防护 | 同上 `:27-29` | `RejectDuplicate<T>`，消息 `"{name} 已注册，拒绝创建第二个 Network Reset composition。"` |
| 策略常量 | `Core/Services/NetworkReset/NetworkResetPolicy.cs:10-16` | 超时 `:10-14`；`ResourceKey = "network-stack:machine"` `:15`；`PolicyVersion = "network-reset.durable.v2"` `:16`；`ComputePlanHash` `:18`；`IsPlanInternallyConsistent` `:40` |
| **磁盘状态** | `Core/Services/NetworkReset/FileNetworkResetStores.cs:10, 206`（`FileNetworkResetPlanStore` / `FileNetworkResetReceiptStore`） | `%AppDataRoot%\network-reset\plans`（`:19`）、`\receipts`（`:214`）；claim 永久化防重放 `:98-100`；收据 temp + `File.Move` 原子替换 `:264-269` |
| 拒绝式兜底 | `NetworkResetOwner.cs:1079-1102` | `DenyNetworkResetPlanStore`（`:1079`）/ `DenyNetworkResetReceiptStore`（`:1094`） |
| 网络备份（静态类） | `Shell/Services/NetworkBackupService.cs:13` | `BackupFolder => AppPaths.NetworkBackupsDir` `:23`；5 个期望文件 `:15-22` |
| 延迟测试静态态 | `Core/Services/Latency/LatencyTestService.cs:28, 31, 44` | 静态 `HistoryPath` + 权重/阈值字典（未接线，但仍为进程级可变态） |
| hosts 文件（基线侧） | `Shell/Services/Baseline/BaselineDomainCollector.cs:948-951` | `%SystemDirectory%\drivers\etc\hosts` |

### 4.4 依赖方向

- `NetworkResetOwner` ctor（`:17-39`）← 全部 Core 抽象。
- Shell → Core 正常；Core 内 `provider.GetService<INetworkResetOwner>()`（`Core/Extensions/ServiceCollectionExtensions.cs:63`）在 `BuiltInOptimizationOperationProvider` 工厂（`:58-67`）内执行 —— 工厂在容器构建完成后首次解析时才运行，因此该行**不受注册顺序影响**（拿不到就为 null，退化为 Provider 内兜底拒绝）。真正对顺序敏感的是同文件 `:48-55` 的 `TryAddSingleton` 组（`IRecoveryRepository` / `IChangeJournal` / `IChangeAuditStore` / `IChangePlanStore` / `IChangeExecutionGate` / `CompatibilityChecker`）：Infrastructure 必须先注册，否则静默退化为内存/拒绝式实现（见 §0.3）。
- Infrastructure → Core：`WindowsNetworkResetPlatform : INetworkResetPlatform`（`Infrastructure.Windows/NetworkReset/WindowsNetworkResetPlatform.cs:12`）；`WindowsHostsBackupOwner : IHostsBackupOwner`（`Infrastructure.Windows/Services/WindowsHostsBackupOwner.cs:8`，注册 `Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs:158`）。

### 4.5 共享可变状态

- 持久化计划/收据 + 每 store 一个 `SemaphoreSlim _mutex = new(1,1)`（`FileNetworkResetStores.cs:17`、`:212`）——**跨进程锁仅覆盖单文件写，非全局互斥**。
- 只读证据冻结：`NetworkAdapterDhcpDiagnosticsOwner.cs:12` `FailClosedReason = "network.adapter-dhcp.mutation-fail-closed-recovery-evidence-missing"`；`PrepareAsync` 恒返回 `FailClosedRecoveryEvidenceMissing`（`:113`）。
- `NetworkSpeedTestService`：`HttpClient _httpClient`、`bool _disposed`（`:83-87`），`IDisposable`。
- `NetworkBackupService` 为静态类、无锁。
- 该域 owner 全实例化；唯一进程内静态可变字段是**未接线**的 `LatencyTestService` 静态态（`LatencyTestService.cs:28, 31, 44`），见上表。

### 4.6 跨域耦合

| 对端 | 耦合点 |
|---|---|
| 基线/还原点 | `BaselineNetworkResetAuthorizationGate.cs:7-11` → `IBaselineGateService`；`BaselineRestoreOrchestrator.cs:66, 508-512` 恢复后启停 `GameCoreGuardService` |
| 安全/修复 | `Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs:114-119`：`NetworkRepairCatalog.All.Where(!UsesNetworkResetOwner)` 注册为 `UnavailableRepairPlatformCapability`（未迁移项一律不可用） |
| 诊断 | `NetworkTestPage.xaml.cs:48, 130, 425` `PageDiagnosticService` |
| 硬件信息 | `Shell/Services/Baseline/BaselineDomainCollector.cs:135`（D18 高级网络）`BaselineRecoveryCoverageClassifier.cs:46`（D18 记为 record-only） |
| 内存/清理 | 未发现直接引用：`Core/Services/Memory` + `Core/Services/SystemCleanup` 内 `NetworkReset` / `INetworkReset` / `HostsBackup` 零命中；`IMemoryCleaning*` 在内存域文件外零命中（grep 全仓 `*.cs` 确认） |

### 4.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | `NetworkTestPage.xaml.cs:75, 86, 98-109`；`NetworkResetDialog.xaml.cs:109, 122`；`NetworkBackupService.cs:35, 45, 49, 51` |
| 日志 | `NetworkResetDialog.xaml.cs:90` `Debug.WriteLine`；`NetworkTestPage.xaml.cs:48, 130, 425` `PageDiagnosticService`。**Core 侧 `NetworkResetOwner` 不注入 `ILogger`**——结果靠 `.Receipt` / `.Outcome` 表达 |
| 错误分析 | `NetworkBackupService.cs:34, 44` `UserFriendlyErrorMapper.GetFriendlyMessage`；`Core/Optimization/Operations/NetworkAdvancedOperation.cs:315, 345` `OperationErrorAnalyzer.HandleOperationException(ex, "网络优化")` |

---

## 5. 电源（Power）

### 5.1 入口

| 类型 | 位置 |
|---|---|
| Shell 路由 | `NavigationCatalog.cs:109` `power-plan-editor` → `PowerPlanEditorPage`（组 `tools`） |
| Shell 页面 | `Shell/Views/PowerPlanEditorPage.xaml.cs:26-32` 字段（7 个）、`:34-38` ctor |
| DI | `App.xaml.cs:264`（`IPowerBaselineGate`）、`:265`（`IPowerManagementOwner`）、`:306`（`IPowerPlanEditorService`）、`:317`（`IManagedPowerPlanService`）、`:368-372`（页面） |
| 启动诊断 | `Shell/Services/StartupDiagnosticService.cs:16-24`（`:18` **`new UnifiedPowerService()`，非 DI**；`:64` `TryAutoRepairAsync`） |
| 场景对话框（**疑似死代码**） | `Shell/Views/ScenarioConfirmationDialog.xaml.cs:15`（`IPowerScenarioService _scenarioService` 字段）、`:23-24`（ctor 注入该接口）、`:126`（`PowerPresetService.GetPresetForCpu`）；实现类 `Shell/Services/PowerScenarioService.cs:16`（`:25-27` `ApplyScenarioAsync` 恒 fail-closed）。**全仓无 `new ScenarioConfirmationDialog`、无 DI 注册** |
| 高级设置对话框 | `Shell/Views/AdvancedPowerDialog.xaml.cs:18, 24`（`new PowerSettingsQueryService()`，非 DI） |

**Core 入口类**：`Core/PowerManagement/PowerManagementOwner.cs`（405 行）。

### 5.2 契约接口

| 接口 | 位置 |
|---|---|
| `IPowerManagementOwner` | `Core/PowerManagement/PowerManagementContracts.cs:50-59` |
| `IPowerPlatform` | 同上 `:3` |
| `IPowerBaselineGate` | 同上 `:17` |
| `IPowerWorkflowStore` | 同上 `:22` |
| `IPowerConfirmationSecretProvider` | 同上 `:34` |
| `IPowerResourceLease` | 同上 `:39` |
| `IPowerConfirmationAuthority` | 同上 `:44` |
| `IManagedPowerPlanService` | `Infrastructure.Windows/PowerManagement/ManagedPowerPlanModels.cs:98` |
| `IPowerPlanEditorService` | `Shell/Services/PowerPlanEditorService.cs:14-29` |
| `IPowerSettingAccessor` / `IPowerSettingCapabilityGuard` | `Infrastructure.Windows/PowerManagement/PowerSettingChangeCapability.cs:77` / `:98`（实现类 `WindowsPowerSettingAccessor` `:128`） |
| Core `PowerSettingCapabilityGuard` | `Core/Services/PowerSettingCapabilityGuard.cs:52` |
| `IPowerScenarioService` | `Shell/Services/IPowerScenarioService.cs:11-32` |

### 5.3 状态所有者

| 状态 | 位置 | 备注 |
|---|---|---|
| `PowerManagementOwner`（单例） | `App.xaml.cs:265` | `PlanLifetime = 5 分钟`（`PowerManagementOwner.cs:9`）；最低 build 19041（`:10`） |
| 身份常量 | `Core/PowerManagement/PowerManagementModels.cs:11-19` | `OwnerId = "gameoptimizer.power-management"`、`Scope = "machine"`、`ResourceKey = "power:machine:display-timeout-and-active-scheme"` |
| 时序默认 | 同上 `:26-27` | `(15s, 45s, 10s)` |
| 确认令牌 | `Core/PowerManagement/PowerConfirmationAuthority.cs:10` | HMAC-SHA256；`ConcurrentDictionary<Guid,byte> _consumed` 防重放；密钥 ≥32 字节 |
| 计划哈希 | `PowerManagementOwner.cs:70-75, 90-91` | SHA-256 + `FixedTimeEquals`；执行时重算并拒绝被篡改的冻结计划 |
| **磁盘状态** | `App.xaml.cs:230-232` + `Infrastructure.Windows/PowerManagement/FilePowerWorkflowStore.cs:96` | `%AppDataRoot%\Recovery\Power`；该 store 同时实现 `IPowerWorkflowStore` + `IPowerConfirmationSecretProvider`（`:81`）；密钥经 `IPowerDataProtector`（DPAPI，注册于 `Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs:66-67`） |
| **静态可变（跨域风险）** | `Core/Optimization/Operations/PowerCfgUtility.cs:21` | `public static ILogger? Logger { get; set; }`，由 `Core/Extensions/ServiceCollectionExtensions.cs:155-156` 运行时写入 |
| **静态可变（跨域风险）** | `Core/Optimization/Operations/AdvancedPowerPlanOperation.cs:75, 78` | `private static PowerOperationDiagnostics? _diagnostics` + `SetDiagnostics(...)`，由 Core DI `:152` 注入。`BasePowerPlanOperation` **无独立文件**，定义在 `AdvancedPowerPlanOperation.cs:73` |
| 静态 guard 实例 | `PowerSettingIndexOperation.cs:18`、`CpuAdvancedOperation.cs:29`、`CoreParkingDisableOperation.cs:16` | 各 `static readonly PowerSettingCapabilityGuard` |
| 静态 GUID/枚举表 | `Shell/Services/PowerPlanEditorService.cs:53-108`（`PowerGuids`）；`PowerManagementModels.cs:5` | |
| Shell 进程内可变缓存 | `Shell/Services/PowerPlanEditorService.cs:110` | `private readonly List<PowerSettingInfo> _knownSettings` — 该服务唯一可变缓存 |

### 5.4 依赖方向

- `PowerManagementOwner` ctor（`:19-30`）← 全 Core 抽象。
- **Core 定义端口 / Shell 提供适配器**：`BaselinePowerGate.cs:8-13` 实现 `IPowerBaselineGate`，内部转 `IBaselineGateService`；`:33` 检查 `HasVerifiedRecoveryBaseline`；`:42-53` 把 `OwnerId|ResourceKey|Kind|AuthorizationEvidenceId|AuthorizedPackageId|AuthorizedManifestSha256|RecoverabilityCheckedUtc|PlanHash` 拼成 SHA-256 证据。**电源写入必须绑定到已验证的恢复基线。**
- `ManagedPowerPlanService.cs:17-18, 30-31` ← `IPowerSettingAccessor` + `IPowerSettingCapabilityGuard`。
- 未发现 Core→Shell。

**关键架构事实：两套并行写入面，非 owner 那套全部 fail-closed。**

| 位置 | 行为 |
|---|---|
| `Shell/Services/PowerPlanEditorService.cs:198-202` | `SetActivePlan` → `LastBlockReason = "Scheme-only switching has no typed prepare/confirmation/baseline/recovery contract and is blocked."`，返回 `false` |
| 同上 `:240-244` | `SetPowerSettingValue` 同步写阻断，返回 `false` |
| 同上 `:216-225` | `GetPowerSettingValue` 仅允许 `DisplaySubgroup` + `DisplayIdleTimeout`，其余 `"This setting family has not been migrated to the typed Power owner and is blocked."` |
| `Shell/Services/UnifiedPowerService.cs`（78 行） | 整体为兼容墓碑：`RetiredWorkflowMessage`/`RetiredWorkflowAction`（`:21-25`）；`GetOrCreateGameOptimizerPlanAsync` 恒失败（`:30-35`）；`ActivatePlanAsync` 恒 `false`（`:40-45`）；`GetActivePlanGuidAsync` 恒 `null`（`:57-62`）；`ClearCache` 空实现（`:50-52`） |
| `Core/PowerManagement/FailClosedPowerOptimizationOperation.cs`（74 行） | `IOptimizationOperation` 墓碑，`OperationType.PowerPlan`，从不触碰平台 |

### 5.5 共享可变状态

- 磁盘：`%AppDataRoot%\Recovery\Power`（计划、claim、收据、DPAPI 保护密钥）。
- **进程级全局日志出口**：`PowerCfgUtility.Logger`、`BasePowerPlanOperation._diagnostics`——任何域经 powercfg 的操作都写同一通道。
- 静态 `PowerSettingCapabilityGuard` 实例 3 处（`PowerSettingIndexOperation.cs:18`、`CpuAdvancedOperation.cs:29`、`CoreParkingDisableOperation.cs:16`；`Refresh`/`EvaluateAsync` 含内部缓存，**未验证**缓存字段）。
- `PowerOperationDiagnostics`（`Core/Services/PowerOperationDiagnostics.cs:14`）与 `PowerServiceDiagnostics`（`Core/Services/PowerServiceDiagnostics.cs:24`，`DiagnoseAsync` `:28`、`TryAutoRepairAsync` `:44`）均为全静态类，无实例状态。
- **注册表/PowrProf**：`WindowsPowerSettingAccessor`（`PowerSettingChangeCapability.cs:128`）；`OrchestratedPowerSettingOperation.cs:7-9` 明示**不发 SETACTIVE**，避免与 machine-global active-scheme 资源竞争。

### 5.6 跨域耦合

| 对端 | 耦合点 |
|---|---|
| 基线/还原点 | `Shell/Services/PowerManagement/BaselinePowerGate.cs:10-21, 33-40`（缺 `AuthorizedPackageId`/`AuthorizedManifestSha256`/`AuthorizedRecoverabilityCheckedUtc` 即拒；实现在 **Shell** 层，Core 侧只有端口 `IPowerBaselineGate`） |
| 内存（**测试固化边界**） | `GameOptimizer.Tests/Shell/ShellPowerAndMemoryBypassBoundaryTests.cs:25` 断言 `UnifiedPowerService.cs` 源码中**不得出现** `"PowerServiceDiagnostics"`（`:21` 读源码） |
| 硬件信息 | `Core/Services/PowerSettingCapabilityGuard.cs:36, 41, 56`（`IPowerHardwareFactsProvider`、`IPowerSettingSupportProbe`、`CpuTopologyService`） |
| 诊断 | `Core/Services/PowerOperationDiagnostics.cs:14-28`（ctor: `ILogger` + `ErrorDiagnosticEngine` + `AutoRepairService`）、`:46` `CheckPowerServiceHealth()`、`:64-67` `Diagnose` → `GetRepairOptions` → `FailedWithDiagnostic`；`PowerServiceDiagnostics.cs:24`（类）`/ :28 DiagnoseAsync / :44 TryAutoRepairAsync`；`StartupDiagnosticService.cs:64`；`AutoRepairService.cs:197` |
| 清理/游戏模式操作 | `CoreParkingDisableOperation.cs:67` `PowerServiceDiagnostics.DiagnoseAsync`、`:385` `TryAutoRepairAsync` |

### 5.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | `LocalizationService.ZhCN.cs:5683` / `LocalizationService.EnUS.cs:6739` 仍有 `UnifiedPowerService` 段落（墓碑文案占用键空间）；`PowerPlanEditorPage.xaml.cs:50-57`；`StartupDiagnosticService.cs:32, 41, 58, 79, 95` |
| 日志 | `PowerOperationDiagnostics.cs:16` `ILogger<PowerOperationDiagnostics>`；`PowerCfgUtility.Logger`（静态）；`Core/Services/PowerServiceDiagnostics.cs` 的 `Debug.WriteLine` 共 10 处：`:100, 108, 133, 137, 149, 153, 164, 168, 252, 260`；`PowerPlanEditorService.cs:167, 187, 235` |
| 错误分析 | `PowerSettingIndexOperation.cs:144` `OperationErrorAnalyzer.DiagnoseWithDetails`；`PowerOperationDiagnostics.cs:64-67` 完整诊断链 |
| `IDiagnosticLogService` | 电源域**无直接使用点**（经 `ILogger` + `DiagnosticLogLoggerProvider` 间接落盘） |
| **盲点（已确认）** | `Core/PowerManagement/` 目录内 `ILogger` / `LocalizationService` / `OperationErrorAnalyzer` / `Debug.WriteLine` **全部零命中**——owner 自身不产生任何日志或本地化输出 |

---

## 6. 游戏模式（Esports / Game Mode / 进程调优）

### 6.1 入口

| 类型 | 位置 |
|---|---|
| Shell 路由（主） | `NavigationCatalog.cs:102` `esports-mode` → `EsportsModePage`，别名 `["game-mode-hub"]` |
| Shell 页面 | `Shell/Views/EsportsModePage.xaml.cs`（2145 行）：ctor `:65-82`（注入 `DashboardViewModel`、`IBaselineGateService`、`IUserPrivilegeService`、`OptimizationGuardService`、`IHardwareDetectionService`）；字段 `:30-63`（`_presetPlanner = new()` `:33`、`NotificationThrottleSeconds = 300` `:62`、`CurrentPresetPolicyVersion = 1` `:63`）；`OnLoaded` `:86`；**`:113` `new EsportsModeStateService(...)`、`:120` `new GameModeHubViewModel(...)`（非 DI）**；`OnUnloaded` `:213`（`:218` 退订 `LanguageChanged`） |
| Shell VM | `Shell/ViewModels/GameModeHubViewModel.cs`（494 行）：ctor `:26-33`；分类集合 `:35-41`；`InitializeOptimizationItems` `:54`；**安全项强制不可用 `:118-122`**；`_dashboardViewModel.OptimizationModules` `:476` |
| Shell 状态服务 | `Shell/Services/EsportsModeStateService.cs`（553 行）：ctor `:22-34`（`IOptimizationEngine` + 可选 `IBaselineGateService` + `Func<bool> isAdministrator` + `EsportsWorkflowOptions?`）；`CreatePlan` `:91`（管理员前置校验 `:111`）；`SafeError` `:533` |
| Shell 预设规划器 | `Shell/Services/EsportsPresetPlanner.cs`（91 行）：`ConservativeAllowlist = {"gaming.mode.enable"}` `:27-31`；`PerformanceAllowlist = {"gaming.mode.enable","gaming.dvr.disable"}` `:33-38` |
| 路由（核心管理） | `NavigationCatalog.cs:108` `cpu-core-management`；`:112-114` `cpu-affinity` / `core-accelerator` / `module-detail` |
| DI | `App.xaml.cs:316`（`CpuTopologyService`）、`:318`（`CoreAffinityService`）、`:319-321`、`:335`、`:360-367` |

**Core 入口类**：
- `Core/Services/ProcessTuning/ProcessTuningOwner.cs`（223 行）——进程亲和性/优先级/理想处理器的**唯一活跃写入者**。
- `Core/Services/GameCoreGuardService.cs`（428 行）——游戏守护。
- `Core/Services/CoreAffinityService.cs`（90 行）——fail-closed 兼容门面。
- `Core/Services/CpuAffinity/ProcessAffinityService.cs`（512 行）——配置 CRUD + 进程枚举。

### 6.2 契约接口

| 接口 | 位置 |
|---|---|
| `IProcessTuningOwner` | `Core/Services/ProcessTuning/ProcessTuningContracts.cs:142-160` |
| `IProcessTuningPlatform` | 同上 `:86-113` |
| `IProcessAffinityService` | `Core/Services/CpuAffinity/ProcessAffinityService.cs:13-26` |
| `ICpuAffinityService` | `Core/Services/CpuAffinity/ICpuAffinityService.cs:6-18` |
| `IRegistryPriorityService` | `Core/Services/ProcessPriority/IRegistryPriorityService.cs` |
| `ProcessTuningCapability` | `Core/Services/ProcessTuning/ProcessTuningCapability.cs:12`：`ResourceKey = "process-tuning:runtime"`（`:15`）、`DefinitionVersion = "process-tuning.v1"`（`:14`）、`CapabilityIdFor`（`:38-44`）→ `process.tuning.affinity` / `.priority` / `.ideal-processor`；`Descriptor`（`:23-33`）`ChangeRisk.MediumRebootOrExperience`、`TargetScopeKind.Process`、`RequiresExplicitConfirmation: true`、`RequiresRecoveryMaterial: true`、`RequiresAdministrator: false`；`ProbeAsync`（`:46`）PID>4 校验（`:51-58`） |
| `IGameProcessMonitorService` | `Shell/Services/GameProcessMonitorService.cs:14-22`；实现类 `GameProcessMonitorService.cs:40` — **未在任何 DI 注册** |
| `IGameProcessPriorityService` | `Shell/Services/GameProcessPriorityService.cs:7-13`；实现类 `GameProcessPriorityService.cs:20` — **未注册**；全 fail-closed 门面（`:20-83`） |
| `IGameAutoOptimizationService` | `Shell/Services/IGameAutoOptimizationService.cs:5` — **无实现类** |

### 6.3 状态所有者

| 状态 | 位置 | 备注 |
|---|---|---|
| `ProcessTuningOwner`（单例） | `Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs:100-101` | `ConcurrentDictionary<int,SemaphoreSlim> _processLocks`、`_activeReceiptsByPid`、`_activePidsByReceipt`（`ProcessTuningOwner.cs:11-13`）；per-PID 冲突门 `:75-76`；确认请求 `:86` |
| `RegistryPriorityService.HasAdminPrivileges()` | `Core/Services/ProcessPriority/RegistryPriorityService.cs:26` | `=> WindowsElevation.IsElevated`（唯一权威判定的一行转发；该服务注入 `ILogger<RegistryPriorityService>` `:19-24`） |
| 三个 `IChangeCapability` | 同上 `:91-99` | Affinity / Priority / IdealProcessor 各一实例 |
| `GameCoreGuardService`（单例） | `App.xaml.cs:319` | `IDisposable`；依赖 `CpuTopologyService` + `CoreAffinityService`（`:16-17`）；`Timer _guardTimer` / `bool _isRunning` / `object _lock`（`:23-25`）；事件 `:27-29`；`StartGuard` `:53`、`StopGuard` `:64`、`ScanAndOptimize` `:101` |
| 磁盘（守护） | `GameCoreGuardService.cs:46-47` | `AppPaths.CoreAcceleratorSettingsFilePath`（`%AppDataRoot%\CoreAccelerator\core_accelerator_settings.json`，`AppPaths.cs:51`）、`AppPaths.GameCoreProfilesFilePath`（`:52`） |
| `ProcessAffinityService`（单例） | `Core/Extensions/ServiceCollectionExtensions.cs:81` | `_cachedCpuInfo`（`:84`）；`ProfilesDir`/`ProfilesFile` 静态只读（`:74-75`）→ `AppPaths.AffinityProfilesDir`/`AffinityProfilesFilePath` |
| `CpuAffinityService` | `App.xaml.cs:318`（具体类型） | `_cachedCpuInfo`（`:13`） |
| `CorePresetService` | **未 DI**；`new` 于 `CpuCoreManagementViewModel.cs:107`、`CoreAcceleratorViewModel.cs:93` | **`AppPaths.ConfigDir` 下**（不是 `CoreAccelerator\`）：`core_presets.json`（`:35`）/ `last_core_config.json`（`:36`），`_configDir = AppPaths.ConfigDir` `:34` |
| **注册表（已熔断）** | `Core/Services/ProcessPriority/RegistryPriorityService.cs:13` | 常量 `IFEO_BASE_PATH` `:15` / `PERF_OPTIONS_KEY` `:16` / `CPU_PRIORITY_VALUE` `:17`；写操作 `SetProcessPriority` `:28` 与 `RemoveProcessPriority` `:98` 恒失败（`:32` / `:101` 返回 `"IFEO persistent priority is blocked: ..."`），真实代码在 `#if false` 区间 `:33-95` / `:102-167`；只读 `IsConfigured` `:170` / `GetCurrentPriority` `:201` 仍走 `SafeRegistryHelper.OpenKey`（`:182` / `:213`） |
| **注册表（未熔断）** | `Core/Services/CpuAffinity/CpuAffinityService.cs:99-121` | `SetDeviceAffinityAsync` → `Registry.LocalMachine.CreateSubKey` `:113`、`DevicePolicy = 4` `:117`、`AssignmentSetOverride = affinityMask` `:121`；`ResetDeviceAffinityAsync` `:203`。**经 `ICpuAffinityService` 暴露，全仓无 Shell 消费者（未验证是否有 Module 消费者）** |
| 领域模型 | `Core/Models/GameCoreProfile.cs` | `AcceleratorMode`（`:8-13`）；`CoreStrategy`（`:15-24`）；`GameCoreProfile` + `[JsonIgnore]` 运行态（`:45-51`）；`CoreAcceleratorSettings`（`:73-81`，`GuardIntervalMs = 3000`） |
| Shell 静态单例 | `Shell/Models/GameProfile.cs:81-94` | `GameProfileManager.Instance`（双重检查锁），被 `GameProcessMonitorService.cs:59` 使用 |

### 6.4 依赖方向

- `ProcessTuningOwner` ctor（`:17-22`）← `IChangeOrchestrator` + `IChangeConfirmationAuthority`。
- `GameCoreGuardService` ← `CpuTopologyService` + `CoreAffinityService`。
  > **已确认的功能性死路**：`CoreAffinityService` 是全 fail-closed 门面（`:15-16` 的 `FailClosedMessage` 明确指向 `ProcessTuningOwner`；所有方法返回 failed/false/empty），因此 `GameCoreGuardService.ApplyAffinity`（`:125`）**必然失败**——守护路径功能上已死，但 `Timer` 仍在运行。
- `ProcessAffinityService.ApplyProfileAsync`（`:167-170`）/ `ApplyAllProfilesAsync`（`:174-179`）均 fail-closed（仅 `Debug.WriteLine`）。
- **活跃路径**：`CpuCoreManagementViewModel.Profiles.cs:286-318` `ApplyProfileThroughOwnerAsync` → `:303-304` `_processTuningOwner.RunInteractiveAsync(ProcessTuningRequest.Affinity(pid, mask))`；`:288-292` 拒绝已有未恢复回执；`:296-301` 要求 PID 唯一确定；恢复 `:320-338` → `:329` `RestoreAsync(new RestoreRequest(receiptId))`。
- Shell → Core：`CpuCoreManagementViewModel.cs:99-110`、`CpuAffinityViewModel.cs:68`、`CoreAcceleratorViewModel.cs:89, 93`、`BaselineRestoreOrchestrator.cs:22, 92, 98`（直接依赖 Core 具体类型 `GameCoreGuardService`；`:66` `StartGuard()`、`:508-512` `StopGuard()`）、`ProcessPriorityPage.xaml.cs:20, 23, 29`。
- Infrastructure → Core：`Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs:90`（平台）、`:91-99`（三 capability）。

### 6.5 共享可变状态

| 对象 | 风险 |
|---|---|
| `ProcessTuningOwner` 三个字典 + per-PID `SemaphoreSlim` | 进程内、非跨进程；PID 复用靠 `:75-76` 冲突门 + 回执身份指纹约束。**进程重启后内存锁状态丢失**，靠持久化回执恢复 |
| `GameCoreGuardService` `_guardTimer`/`_isRunning`/`_lock` + 两个配置文件 | `ScanAndOptimize`（`:101`）直接改 `profile.IsRunning`/`IsOptimized`/`ProcessId`/`LastUsed`（`:128-131`）——**这些是 `[JsonIgnore]` 运行态，不会落盘** |
| `ProcessAffinityService._cachedCpuInfo` + `CpuAffinityService._cachedCpuInfo` | **两处独立 CPU 缓存，无失效机制** |
| `GameProfileManager.Instance` | Shell 静态单例；其 `GameProcessMonitorService` 另有 `_trackedProcessNames` + `_lock`（`:44-45`）、WMI `Win32_ProcessStartTrace`/`StopTrace`（`:70-79`）、3 秒轮询回退（`:87`） |
| 页面级 | `EsportsModePage` `_allRows`/`_selectedItems`/`_appliedItems`（`:30-63` 区间）；`CpuCoreManagementViewModel._dispatcher`（`:108` 捕获构造线程 Dispatcher） |

### 6.6 跨域耦合

| 对端 | 耦合点 |
|---|---|
| 内存（反向自保护） | `Core/Services/Memory/ProcessCleanerService.cs:57-65` 白名单硬编码 `"GameOptimizer.Shell"`（`:64`） |
| 安全 | `BuiltInOptimizationOperationProvider.cs:47, 62, 70` 可选 `ISecurityProtectionOwner`；`GameModeHubViewModel.cs:118-122` 安全项强制不可用；`Core/Security/FailClosedSecurityOptimizationOperation.cs:7-11` |
| 还原点/基线 | `BaselineRestoreOrchestrator.cs:22, 66, 92, 98, 508-512`；`EsportsModeStateService` ctor 可选 `IBaselineGateService`（`:22-34`），`:394` "无法取得无基线风险确认，未执行系统写入" |
| 硬件信息 | `EsportsModePage.xaml.cs:65-82, 125-133`（硬件检测失败即"写入操作将保持阻断"）；`GameModeHubViewModel` ctor `:26-33`、`UpdateHardwareHints` `:396, 401`；`CpuTopologyService`；`GameCoreProfile.cs:93-95` 依 X3D/混合架构 |
| 电源 | `CoreParkingDisableOperation.cs`（`cpu.disable_core_parking`，被 `GameModeHubViewModel.cs:87` 列为"输入延迟"项） |
| 网络 | `GameModeHubViewModel.cs:96-103`（`network.disable_nagle` 等）、`:107` `network.disable_lmhosts`；共享 Provider 的 `network.*` 操作表 |
| 清理 | 间接：`GameModeHubViewModel` 引用 `_dashboardViewModel.OptimizationModules`（`:476`）；`EsportsModePage.xaml.cs:976` 遍历。**Core 层直接引用未做反向扫描（不确定）** |
| 诊断 | `Shell/Services/EsportsModeStartupLogger.cs:12` 静态类，在 `EsportsModePage.xaml.cs` 内 **69 次调用（68 行；`:197` 一行两次）**（`:97, 102, 105, 108, 112, …, 1857`）；`PageDiagnosticService` 在该页亦有调用点 |

### 6.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | `GameModeHubViewModel.cs:56` + `:59-146`（`GameMode.Category.*` / `GameMode.Effect.*` / `GameMode.Intel*`，`:391-393`）；`Shell/Models/GameModeOptimizationItem.cs:36, 81`；`CpuCoreManagementViewModel.cs:33`（`:109, 136-137` 使用 `_loc.Get`）；`ProcessPriorityViewModel.cs:82` |
| 日志 | `ILogger`：`GameProcessMonitorService.cs:42`、`GameProcessPriorityService.cs:22`（`:42-43, 61-62, 69-70`），另有 `GameProcessPriorityService.cs:54` `_logger.LogInformation("[PriorityService] Fail-closed priority monitor stopped")`；`PageDiagnosticService`：`CpuCoreManagementPage.xaml.cs:23, 54, 72`、`CpuAffinityPage.xaml.cs:23, 53, 128, 132, 139`、`CoreAcceleratorPage.xaml.cs:18, 38, 55`；`EsportsModeStartupLogger`（Shell 静态，69 次调用 / 68 行）；`Debug.WriteLine`（Core）：`GameCoreGuardService.cs:147`、`ProcessAffinityService.cs:169, 179`、`CpuAffinityService.cs:51, 463` |
| 错误分析 | `UserFriendlyErrorMapper.GetFriendlyMessage` @ `Core/Services/CorePresetService.cs:43`。三域自有服务**不直接调用** `OperationErrorAnalyzer`——游戏模式项经 Provider 的通用操作类间接受益 |

**未接线清单（本域）**：`IGameProcessMonitorService`、`IGameProcessPriorityService`、`IGameAutoOptimizationService` 均未注册/无实现。

---

## 7. 硬件信息（Hardware Information）

### 7.1 入口

| 层 | 位置 |
|---|---|
| Shell View | `Shell/Views/HardwareInfoPage.xaml.cs:25, 41`（ctor `IHardwareDetectionService`）；`:44` `PageDiagnosticService.Log`；`:137` `InvalidateCache()`；`:139` `GetOrDetectAsync()` |
| Shell DI | `App.xaml.cs:304`（`IHardwareDetectionService`）、`:336-337`（页面工厂）；`:314`（`SystemMonitorService`）、`:316`（`CpuTopologyService`）、`:394`（`IDashboardTelemetryService`） |
| 导航 | `NavigationCatalog.cs:103` `hardware-info` → `HardwareInfoPageHost`（组 `tools`，键 `Nav.HardwareInfo`）；host 注册 `MainWindow.xaml.cs:131` |
| Shell 其他消费者 | `EsportsModePage.xaml.cs:37, 70, 256`；`GameModeHubViewModel.cs:22, 26-33, 201`；实例化于 `EsportsModePage.xaml.cs:120` |
| 首页遥测 | `App.xaml.cs:240-249`（`:242` 验收替换 `:707-714`）；`DashboardViewModel` ctor `:1117-1131`；`ReadHomeTelemetrySnapshot()` `:2859-2872` |
| Core DI | `Core/Extensions/ServiceCollectionExtensions.cs:34`（`IGpuInventoryService`）、`:35`（`ISystemInsightService`） |
| 模块层 | `GameOptimizer.Modules.PerformanceHealth/PerformanceHealthModule.cs:36, 56-58`；`ViewModels/PerformanceHealthViewModel.cs:17, 50` |

### 7.2 契约接口

| 接口 | 位置 | 成员 |
|---|---|---|
| `IGpuInventoryService` | `Core/Services/GpuInventoryService.cs:9-13` | `GetSnapshot(bool forceRefresh=false)`、`InvalidateCache()` |
| `ISystemInsightService` | `Core/Services/ISystemInsightService.cs:6-13` | `GetCurrentSnapshot()`、`GetActiveGame()`、`GetRecommendations()`；实现类 `Core/Services/LiveSystemInsightService.cs:58` |
| `IHomeTelemetrySource` | `Core/Services/HomeTelemetryContracts.cs:83-86` | `GetCurrentSnapshotWithStatus()`；实现同为 `LiveSystemInsightService.cs:58` |
| `IHardwareDetectionService` | `Shell/Services/IHardwareDetectionService.cs:6-9` | **Shell 层接口**，返回 Shell 模型 `HardwareInfo`；唯一实现 `Shell/Services/HardwareDetectionService.cs:9`（ctor `:17` 注入 `IGpuInventoryService`） |
| `SystemMonitorService` | `Shell/Services/SystemMonitorService.cs:38` | **无接口**，public class |
| `IDashboardTelemetryService` | `Shell/Services/DashboardTelemetryService.cs:14-27` | `: IDisposable`，实现 `:29` |
| 遥测模型 | `HomeTelemetryContracts.cs:7-14`（`TelemetryAvailability`）、`:16-37`（`TelemetryFieldStatus`）、`:39-81`（`HomeTelemetrySnapshot`，`GpuLoad` 缺省 `Unavailable("未采样","尚未采样")` `:50`）、`:88-120`（`TelemetryTrendBuffer`，capacity 48 缺省 `:93`，`TryAdd` `:109-119`） |
| GPU 模型 | `Core/Models/GpuInventoryModels.cs:7`（`GpuAdapterVendor`）、`:18`（`GpuAdapterKind`）、`:30-40`（`GpuAdapterInfo`）、`:42-44`（`GpuInventorySnapshot.Empty`）、`:57-72`（`GpuLoadAttribution`，`Unavailable` `:67-68` / `Unmapped` `:70-71`） |

### 7.3 状态所有者

| 状态 | 所有者 | 证据 |
|---|---|---|
| GPU 清单缓存 5 min | `GpuInventoryService._snapshot` / `CacheDuration` / `_gate` | `GpuInventoryService.cs:22-24`；WMI `Win32_VideoController` `:52-53`，5s 超时 `:54`；DXGI LUID 合并 `:88-98`；`DetectVendor` VEN_10DE/1002/8086 `:136-143`；`Classify` `:152-165`；异常仅 `Debug.WriteLine` `:85` |
| DXGI 枚举（无状态） | `DxgiGpuAdapterDiscovery`（internal static） | `DxgiGpuAdapterDiscovery.cs:16, 83-84, 55-64`（吞掉 DllNotFound/EntryPoint/COM） |
| CPU 拓扑缓存 | `CpuTopologyService._cachedTopology` | `CpuTopologyService.cs:14, 16-23, 25-29`；WMI `Win32_Processor` 5s `:59-60`；`CalculateAffinityMask` 64 核上限 `:393-402` |
| HardwareInfo 缓存 5 min（Shell） | `HardwareDetectionService._cachedInfo` + `SemaphoreSlim _lock` | `HardwareDetectionService.cs:11, 13, 15`；`GetOrDetectAsync():22-41`（`:33` `Task.Run(() => HardwareInfo.Detect(_gpuInventory))`）；`InvalidateCache():43-48` **级联** `_gpuInventory.InvalidateCache()` `:47` |
| HardwareInfo 静态工厂 | `HardwareInfo.Detect(IGpuInventoryService?)` | `Shell/Models/HardwareInfo.cs:218`；WMI 读点 `:225, 261, 307, 320, 410, 537, 580`；**Shell 自有 `GpuVendor:13-19` / `DeviceType:24-31` 与 Core 枚举重复定义**，转换点 `ToShellVendor`（`:644-649`） |
| 实时遥测缓存 | `LiveSystemInsightService` | `:24` `_snapshotGate`、`:34` `_lastGpuAttribution`、`:41` `_gpuCounterCache`、`:48` 网络延迟 5s、`:52` 温度 2s、`:56` DPC 3s、`:250` GPU 引擎探测；`Dispose` 清缓存 `:610-612` |
| 系统监视（**注册但无消费者**） | `SystemMonitorService`（singleton） | `:38, 59` `LatestData`；`:53-54` 事件；`:56-57` 阈值 85/95；`:47, 51` 1s/2s 缓存；`:83-103` WMI `Win32_Battery` 3s |
| Dashboard 趋势 + 定时器 | `DashboardTelemetryService` | 趋势队列 cap 48 `:38`；画布 400x140 `:39-40`；`DispatcherTimer` 1s `:57-70`；`CollectSnapshotAsync` `:89` |
| 首页趋势缓冲（VM 内） | `DashboardViewModel._cpuTrendBuffer` / `_gpuTrendBuffer` | `DashboardViewModel.cs:211, 213` |

### 7.4 依赖方向

- **无 Core→Shell 上向依赖**（硬件路径全部 Core 实现不 `using GameOptimizer.Shell`）。
- Shell → Core 正常：`HardwareInfo`（Shell）消费 Core `IGpuInventoryService` / `GpuAdapterKind` / `GpuAdapterVendor`（`HardwareInfo.cs:36-45, 171, 598-650`）。
- **三处绕过 DI 的实例化隐患**：
  1. `LiveSystemInsightService` ctor 为可选注入 `IGpuInventoryService? gpuInventory = null`，缺省时 `new GpuInventoryService()`（`:58-70`）→ 与 DI 实例并存，**双缓存**。
  2. `CompatibilityChecker.cs:631` `new CpuTopologyService().GetTopology()`。
  3. `PowerSettingCapabilityGuard.cs:56` 字段 `new CpuTopologyService()`。
- `CpuTopologyService`（具体类，非接口）消费者：`CoreAffinityService.cs:18`、`GameCoreGuardService.cs:35`、`CoreAcceleratorViewModel.cs:29, 89`、`CpuCoreManagementViewModel.cs:28, 100`、`PowerProfilePreparationService.cs:66`、`ManagedPowerPlanService.cs:34`、`CompatibilityChecker.cs:631`、`PowerSettingCapabilityGuard.cs:56`。
- `ISystemInsightService` 消费者：`DashboardViewModel.cs:1118`、`PerformanceHealthViewModel.cs:17, 50`、`PerformanceHealthModule.cs:36, 56-58`。

### 7.5 共享可变状态

- 进程级单例缓存：`GpuInventoryService._snapshot` / `CpuTopologyService._cachedTopology` / `HardwareDetectionService._cachedInfo` —— TTL 内不刷新；**`HardwareDetectionService.InvalidateCache()` 是唯一反向联动点**（级联清 GPU 缓存）。
- `LiveSystemInsightService` 全部计数器 + 三类时间戳缓存，被 `IHomeTelemetrySource` / `ISystemInsightService` / `IDashboardTelemetryService` **三方共享同一实例**。
- `SystemMonitorService` 持 `PerformanceCounter` + 事件（`:321-332` Dispose 清事件），**注册但无消费者**。
- `DashboardViewModel.HardwareModuleFilter`（`:48-67`）：`ShouldShowModule` **恒返回 true**（`:59-66`），且 `:2591-2595` 固定为 `ShowAll` —— 硬件过滤实际是 no-op（保留的形状）。
- `TelemetryTrendBuffer` 环形缓冲（48 容量），VM 与 `DashboardTelemetryService` 各持一份。
- `GpuInventorySnapshot.Empty` 静态只读共享实例（`GpuInventoryModels.cs:44`）。

### 7.6 跨域耦合

| 对端 | 耦合点 |
|---|---|
| 内存优化 | `Shell/Services/MemoryOptimizationKnowledgeBase.cs:288, 312, 371, 393` 以 `HardwareInfo` 为规则输入 |
| 电源 | `OptimizationKnowledgeBase.cs:953, 981, 1020, 1116, 1172, 1214, 1233, 1248, 1302, 1338, 1413, 1442, 1465, 1593, 1647`；`PowerConfigService.cs:21, 36, 79`；`PowerScenarioService.cs:34`；`PowerSettingValidator.cs:165, 211` |
| 游戏模式 | `GameModeHubViewModel` 持 `IHardwareDetectionService`；`GameModeOptimizationItem.HardwareHint`（`Models/GameModeOptimizationItem.cs:21`）；`DashboardViewModel.cs:1659-1662, 1689-1691, 1932-1938` 按 `IsHardwareIncompatible` 跳过并计数 |
| 还原点/备份 | `SnapshotService.cs:159, 1354, 1440`、`BackupManagerViewModel.cs:975, 1048`、`FirstRunBaselineOrchestrator.cs:445` 调 `SystemResourceChecker.CheckDiskSpace` |
| 兼容性策略 | `CompatibilityChecker.cs:623-647`（`new CpuTopologyService()` + `ReadGpuNames()` `:713-720`） |
| 遥测/诊断 | `DashboardTelemetryService` 消费 Core `ISystemInsightService`；`DashboardViewModel` 消费 `IHomeTelemetrySource` + `IDiagnosticLogService`（`:183, 1124`） |
| 安全 | **未发现耦合**（**未验证**：仅能证明硬件域文件内无 security 引用，属否定断言，无法穷尽证明） |

### 7.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | `HardwareInfoPage.xaml.cs:67, 184, 222, 227, 309, 326, 360, 420`；`HardwareInfo.cs:281, 434-435, 446, 454, 501, 519`；`DashboardViewModel.cs:502-506`。键 `Hardware.*` / `HardwareExport.*`（`LocalizationService.ZhCN.cs:1394+`、`LocalizationService.EnUS.cs:1417+`，首个键 `Hardware.Title` 在 `LocalizationService.EnUS.cs:1418` / `LocalizationService.ZhCN.cs:1395`）；另有跨域共用的 `Resource.*` 键，**不在硬件域文件内**，而在 `Core/Services/SystemResourceChecker.cs:18, 23, 27, 34, 42, 48, 64, 79, 82, 89, 92, 100`（被 §8 备份/还原点共用） |
| 日志 | 硬件域 **0 处 `ILogger<T>`**。`Debug.WriteLine`：`GpuInventoryService.cs:85`（1 处）；`CpuTopologyService.cs:48, 82, 89, 257`（4 处）；`LiveSystemInsightService.cs:156, 262, 361, 458, 492, 534`（6 处）；`SystemMonitorService.cs:68, 76, 100, 121, 175, 195, 222, 283`（8 处）；`HardwareInfo.cs:346, 423, 567, 589, 635, 640`（6 处）；`DashboardViewModel.cs` **33 处**（`:1488, 2216, 2254, 2263, 2287, 2299, 2321, 2350-2362, 2389, 2598, 2601, 2607, 2613, 2632, 2634, 2870, 2949, 2961-2993, 3032, 3144, 3263-3433`）；`DashboardTelemetryService.cs:103`（1 处）。`PageDiagnosticService`：`HardwareInfoPage.xaml.cs:44` |
| 错误分析 | `OperationErrorAnalyzer` / `ErrorDiagnosticEngine` / `UserFriendlyErrorMapper` / `ErrorMessageHelper` **0 命中**；`IDiagnosticService` 无消费者 |

---

## 8. 还原点（System Restore Points / Snapshot / Comprehensive Backup）

**域内是两个并行子系统**，共用 Shell 入口但状态所有者不同：

- **A. Windows 系统还原点**：`Core/SystemRestore/` + `Infrastructure.Windows/SystemRestore/` + Shell 隔离写入进程宿主。
- **B. 应用级快照 / 完整备份**：`Core/Services/Snapshot/`，由 `BackupManagerViewModel` 驱动。

域内**无 PowerShell**（`Checkpoint-Computer` / `Get-ComputerRestorePoint` 全仓 0 命中）；读取走 WMI `root\default:SystemRestore`，创建走 `srclient.dll!SRSetRestorePointW` 独立子进程。

### 8.1 入口

| 层 | 位置 |
|---|---|
| Shell View | `Shell/Views/RestorePointPage.xaml.cs:20`（ctor 注入 VM）；`:13-16` 30s `DispatcherTimer` 重估能力；`:23, 42, 46, 65` `PageDiagnosticService` |
| Shell View | `Shell/Views/BackupManagerPage.xaml.cs:15, 17, 20-27`（无参 ctor 为 fail-safe）；`:51` `RefreshSnapshots()`、`:56` `Initialize()`、`:62` 卸载不 Dispose VM |
| Shell VM | `Shell/ViewModels/RestorePointViewModel.cs:13, 26, 117-132`；依赖 `ISystemRestorePointOwner` / `ISystemRestorePointConfirmationPrompt` / `IBaselineCatalogService?` / `ISnapshotService?` / `TimeProvider?` / `ISystemRestoreManualLauncher?` / `ISystemRestorePointStartupReconciliation?`；`_operationGate`（`:42`） |
| Shell VM | `Shell/ViewModels/BackupManagerViewModel.cs:79, 81-88, 297-326`；`:295` 无参 ctor 自建 `new SnapshotService()` |
| 导航 | `NavigationCatalog.cs:105` `restore-point`；`:107` `backup-manager` |
| DI | `App.xaml.cs:228-229`（Infra）、`:255`（确认）、`:256`（`ISystemRestoreService`）、`:271`（授权门）、`:342-343`、`:349-359`、`:392-393` |
| Core DI | `Core/Extensions/ServiceCollectionExtensions.cs:84-106`（`SnapshotService` 工厂 `:86-95`，无 gate 时降级 `DenySnapshotRestoreAuthorizationGate`）、`:106`、`:107`、`:108-113` |
| 启动协调 | `App.xaml.cs:557` → `RunSystemRestoreStartupReconciliationAsync()` `:801-836`（`:823, 830` `_diagnosticLogService.Append`，类别 `"App.SystemRestore.Reconciliation"`） |
| **独立写入进程** | `Shell/Program.cs:20-26`（`--system-restore-writer --request X --response Y`）→ `Services/SystemRestorePointWriterEntryPoint.cs:9, 19, 47, 215, 239`；**native P/Invoke 只在 Shell 程序集**（`SRSetRestorePointW` 声明 `:237-241`，调用 `:215`） |
| 兼容门面 | `Shell/Services/SystemRestoreService.cs:91-110`；`:156-160` 删除永久 fail-closed；`:162-170` `IsSystemRestoreEnabled()` 同步阻塞读 |

### 8.2 契约接口

| 接口 | 位置 |
|---|---|
| `ISystemRestorePointPlatform` | `Core/SystemRestore/SystemRestorePointContracts.cs:3-10` — **刻意无 restore/execute 成员**（测试断言 `SystemRestorePointOwnerTests.cs:413-416, 585-590`） |
| `ISystemRestorePointWorkflowStore` | 同上 `:12-38` |
| `ISystemRestorePointResourceLease` | `:41-44` |
| `ISystemRestorePointConfirmationAuthority` | `:46-67` |
| `ISystemRestorePointOwner` | `:69-106` |
| `ISystemRestorePointStartupReconciliation` | `:108-113` |
| `ISystemRestorePointWriterProcessRunner` | `Infrastructure.Windows/SystemRestore/SystemRestorePointWriterProcessRunner.cs:8` |
| `ISnapshotService` | `Core/Services/Snapshot/ISnapshotService.cs:7-41`（`:33-36` 注册表恢复永久 fail-closed） |
| `IComprehensiveBackupService` | `Core/Services/Snapshot/IComprehensiveBackupService.cs:8-25` |
| `ISnapshotRestoreAuthorizationGate` | `Core/Services/Snapshot/SnapshotRestoreSafety.cs:134-137`；拒绝实现 `DenySnapshotRestoreAuthorizationGate:139`（reason `"snapshot.baseline-gate-missing"`） |
| `ISnapshotRestoreBatchStore` | `Core/Services/Snapshot/SnapshotRestoreBatchStore.cs:37`（`InMemory…:61` / `File…:385`） |
| `IUserRegistryExportBackend` | `Core/Services/Snapshot/ComprehensiveBackupModels.cs:92`；实现 `RegExeUserRegistryExportBackend.cs:31-41`（仅 HKCU；`:10-14` 永不 import） |
| Shell 侧 | `Shell/Services/SystemRestoreService.cs:6-19`（`ISystemRestoreService`）；`SystemRestorePointConfirmationPrompt.cs:7`；`SystemRestoreManualLauncher.cs:8, 14-31` |

### 8.3 状态所有者

| 状态 | 所有者 | 证据 |
|---|---|---|
| 系统还原点创建/回读唯一权威 | `SystemRestorePointOwner`（Core, singleton） | `Core/SystemRestore/SystemRestorePointOwner.cs:7`；注册 `Infrastructure.Windows/SystemRestore/SystemRestorePointServiceCollectionExtensions.cs:27` |
| 描述上限 255 | `SystemRestorePointOwner.cs:9`、`NormalizeDescription:916` | |
| 计划哈希 / 防篡改 | `:614`（create）/ `:638`（execution）；`FixedEquals:932` 用 `CryptographicOperations.FixedTimeEquals` | |
| 确认防重放 | `SystemRestorePointConfirmationAuthority.cs:10, 15-18, 76-96, 98-108` | **进程内**：重启后旧 token 失效，durable plan 仍在 |
| 持久工作流 | `FileSystemRestorePointWorkflowStore`（Infra, singleton） | `:19-25` `SchemaVersion=3` / `LegacySchemaVersion=2` / `MaximumPayloadBytes=1MB` / `MaximumDocumentBytes=2MB`；`:26-31` `UnmappedMemberHandling.Disallow` |
| 磁盘布局 | `<AppDataRoot>\Recovery\SystemRestore\` 下 `create-plan-{id:D}.json` / `create-plan-{id:D}.claim.json` / `restore-plan-{id:D}.json` / `receipt-{id:D}.json` | `App.xaml.cs:228-229`；`FileSystemRestorePointWorkflowStore.cs:658-661`（`CreatePlanPath` `:658`、`ClaimPath` `:659`、`RestorePlanPath` `:660`、`ReceiptPath` `:661`） |
| 密钥环 | `KeyRingFileName = ".system-restore-auth-keyring.v2"`（旧 `".system-restore-auth-key.v1"`）；DPAPI `CryptProtectData`/`CryptUnprotectData`（`FileSystemRestorePointWorkflowStore.cs:706, 717` 声明，调用 `:483-484`）；`:458` `FileShare.None`+`WriteThrough`；轮换 `FileSystemRestorePointWorkflowStore.Maintenance.cs:9`（跨进程锁 `authentication-keyring.rotation.lock` `:20`）；迁移 `FileSystemRestorePointWorkflowStore.Maintenance.cs:47`；`ComputeKeyId` = SHA256[..32]（`FileSystemRestorePointWorkflowStore.Maintenance.cs:282`） | `FileSystemRestorePointWorkflowStore.cs:17` 与 `FileSystemRestorePointWorkflowStore.Maintenance.cs:7` 是**同一个 partial 类**的两个分部（文档早期版本写作 `Maintenance.cs`，特指后者） |
| 跨进程资源租约 | `system-volume.lock` | `FileSystemRestorePointWorkflowStore.cs:294, 301, 698` |
| 启动协调一次性状态 | `SystemRestorePointStartupReconciliation.cs:15, 32, 22`（`Interlocked.Exchange(ref _runStarted,1)`，15s 超时） | 状态枚举 `SystemRestorePointModels.cs:186-193` |
| 快照目录 | `SnapshotService.cs:62-64`；`:78-99` `ValidateSnapshotDirectory`；`:1454` `GetSnapshotDirectory()` | 批存 `restore-batches` |
| 快照文件侧车锁 | `{file}.tmp` / `{file}.lock` + 重试 | `SnapshotFileWriter.cs:32-33, 42, 186, 213`（静态 `JsonOptions:17`） |
| 完整备份包 | `AppPaths.ComprehensiveBackupsDir` = `<root>\Backups\Comprehensive` | `ComprehensiveBackupService.cs:47`；`AppPaths.cs:32, 34, 80` |
| 恢复 claim（**跨域共享**） | `FileRecoveryClaimStore` 默认根 `<root>\Changes\recovery-claims` | `FileRecoveryClaimStore.cs:24-27`；被 `SnapshotRestoreSafety.cs:206` 与 `ChangeOrchestrator.cs:55` 共用 |
| 写入进程协议 | `<TempDir>\SystemRestoreWriter\<guid>\` request/response JSON；协议版本 1 | `SystemRestorePointWriterProcessRunner.cs:46-52, 62-66`；`SystemRestorePointModels.cs:295` |
| 读取的系统状态（只读） | 注册表 `...\SystemRestore`（`DisableSR`、`SystemRestorePointCreationFrequency`）、`...Policies\...SystemRestore`、`RebootPending`/`RebootRequired`/`PendingFileRenameOperations` | `WindowsSystemRestorePointPlatform.cs:13-14, 139-152, 177, 186-207` |
| 冷却默认 | `DefaultCreationCooldownMinutes = 1_440` | `WindowsSystemRestorePointPlatform.cs:15` |
| 时序预算 | plan 5min / confirmation 2min / lease 15s / execution 60s / read-back 15s | `SystemRestorePointModels.cs:389-395` |
| 快照 schema | `OptimizationSnapshot.Version = 5`；批 schema = 2；plan lifetime 2min | `SnapshotModels.cs:7-50`；`SnapshotRestoreSafety.cs:164-165` |
| 快照上限/包含校验 | `MaximumSnapshotBytes = 50MB`；`EnsureContained` 防根逃逸 + symlink | `SnapshotFilePolicy.cs:9, 69-92, 94` |
| 完整备份身份 | `UserSidSha256` / `MachineIdentitySha256` | `ComprehensiveBackupService.cs:112-113`；恢复校验 `:234` |

### 8.4 依赖方向

- `GameOptimizer.Core.csproj:21` 唯一 ProjectReference 为 `..\GameOptimizer.ModuleContracts\`；**无 Core→Shell 上向依赖**。
- 方向：Core（契约 + Owner）← Infrastructure.Windows（平台/存储/进程运行器）← Shell（UI + 隔离写入进程宿主 + 授权门实现）。
- **镜像特例**：native `SRSetRestorePointW` 声明在 **Shell 程序集**（`SystemRestorePointWriterEntryPoint.cs:237-241`：`[DllImport("srclient.dll", ...)]` `:237`、`[return: MarshalAs(UnmanagedType.Bool)]` `:238`、声明 `:239-241`；调用点 `:215`），经 `YuqiEngine.exe` 子进程调用（`Program.cs:20-26`）；Core/Infra 只持有 `ISystemRestorePointWriterProcessRunner` 抽象。这是"**写边界在 Shell、状态在 Core**"，不是上向依赖。
- `SystemRestorePointOwnerTests.cs:396-417` 断言生产组合中 Platform/Owner/Reconciliation/WriterRunner 各恰一个且为单例。

### 8.5 共享可变状态

| 对象 | 风险 |
|---|---|
| `ISystemRestorePointOwner` / `ISystemRestorePointWorkflowStore` / `ISystemRestorePointConfirmationAuthority` 均 singleton | `RestorePointViewModel`、`SystemRestoreService`、`DashboardViewModel`（`:189`）、`BaselineManualGuideService`（`:12`）、`FirstRunBaselineOrchestrator`（`:71`）、`StartupDiagnosticService`（`:19`）**共享同一创建权威** |
| `FileSystemRestorePointWorkflowStore._cachedKeyRing`（`:425` 起，`_keyGate` 保护） | 单实例内存缓存 + 跨进程轮换锁 |
| `SystemRestorePointStartupReconciliation._runStarted` / `LastResult` | `RestorePointViewModel.cs:571-579` 直接读 `LastResult` 生成 `RecoveryAttentionText` |
| `SystemRestorePointConfirmationAuthority._consumed` + 内存密钥 | 重启后 token 失效；durable plan 保留 |
| `SnapshotService` 静态 `JsonOptions`、`_batchStore`、`_changeOrchestrator`/`_confirmationAuthority` | 被 `BackupManagerViewModel`、`ComprehensiveBackupService`、`BaselineDomainCollector`（`:146`）、`BaselineRestoreOrchestrator`（`:86, 98`）同时使用 |
| `FileSnapshotRestoreBatchStore` 根 `Snapshots\restore-batches`；`FileRecoveryClaimStore` 根 `Changes\recovery-claims` | **与 Changes 域共享 claim 根** |
| `BackupManagerViewModel` singleton | `:343-374` 订阅 `OptimizationCompletedEvent`，累计 ≥10 变更置 `NeedsBackup` |
| `SystemRestoreService._lastProbe` / `_lastPoint`（Shell） | `:164-169` 缓存探测且 `IsSystemRestoreEnabled()` 同步阻塞；`:204` 返回缓存 |
| **`AppPaths` 运行期重定向** | `_configuredLogsDirectory` / `RootResolution` Lazy；`AppDataRootOverrideVariable="YUQIENGINE_DATA_ROOT"`（`AppPaths.cs:11-13, 21`）；`Program.cs:130-152` 按 `--acceptance-ui`/`--recovery-dev` 覆盖 → **验收/开发运行使用不同的恢复数据根** |

### 8.6 跨域耦合

| 对端 | 耦合点 |
|---|---|
| 基线 | `BaselineManualGuideService.cs:48-79`（要求"新且描述精确相等且时间 ≥ -1min"的唯一回读，否则首次基线保持阻断）；`FirstRunBaselineOrchestrator.cs:451` `CanCreateRestorePoint()` 作为硬门禁；`BaselineSnapshotRestoreAuthorizationGate` 是快照恢复授权门 |
| 优化执行 | `DashboardViewModel.cs:1703-1750` `EnsureApplyBackupGateAsync`：仅对 `RequiresRestorePoint==true` 的操作强制系统还原门禁（未启用即阻断 `:1719-1724`，创建失败即阻断 `:1729-1735`），15 分钟复用窗口 `:1713-1717`；声明方 `CpuAdvancedOperation.cs:382`、`BcdSettingOperation.cs:52` |
| 变更编排 | `SnapshotService.cs:37-38, 459, 476, 496, 739, 755-757, 1062` 经 `IChangeOrchestrator` 做 typed 注册表恢复与逆序补偿；`IRecoveryClaimStore` 与 Changes 域共享同一文件存储 |
| 事件总线 | `BackupManagerViewModel.cs:1241, 1367, 1900` 发布 `SnapshotRestoredEvent`；`:352` 订阅 `OptimizationCompletedEvent` |
| 优化目录 | `ComprehensiveBackupService.cs:285-286` 遍历 `IOptimizationOperationProvider.GetModules()`；`:346-376` `IOperationRestoreStore.LoadSnapshotAsync(operationId)` |
| 存储/硬件 | `SnapshotService.cs:159, 1440` `SystemResourceChecker.CheckDiskSpace`；`BackupManagerViewModel.cs:975, 1048`（`RefreshStorageVolumeSummary()` `:596` 是另一回事，非磁盘空间校验） |
| 诊断 | `StartupDiagnosticService.cs:87, 101`（必要时 `EnableSystemRestoreAsync()`）；`BackupManagerViewModel.cs:2022` 写 `Logs\backup_errors.log`；`:2079` `DiagnosticPackageService` |
| 网络/清理/电源/游戏模式 | **未发现直接耦合**；仅共享 `IChangeOrchestrator` 与 claim 存储 |
| 安全/权限 | `WindowsSystemRestorePointPlatform.cs:238` `IsAdministrator => WindowsElevation.IsElevated`（一行转发，见 §11.2）；写入进程自检 `SystemRestorePointWriterEntryPoint.cs:186-187`（`WindowsSystemRestoreNativeApi.IsWindows` / `IsAdministrator`，同样转发 `WindowsElevation`）；接口 `ISystemRestoreNativeApi` `:175-182`、实现 `WindowsSystemRestoreNativeApi` `:184`；native 声明 `:237-241` |

### 8.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | Core 直接引用：`SnapshotService.cs:105, 415, 1337, 1386`；`SnapshotModels.cs:66, 69-73`（`Backup.*`）。Shell：`SystemRestoreService.cs:36-37, 50-62, 72-80`；`RestorePointViewModel.cs:211, 258-308, 356, 391, 449, 522-523`。段首注释 `// RestorePointPage UI` 在 `LocalizationService.ZhCN.cs:1525` / `LocalizationService.EnUS.cs:1548`，`// BackupManagerPage UI` 在 `LocalizationService.ZhCN.cs:1703` / `LocalizationService.EnUS.cs:1723` |
| `Debug.WriteLine` | `SnapshotFileWriter.cs:48, 96, 106, 117, 132, 148, 153, 186, 191, 213, 223`；`SnapshotService.cs:86, 89, 95, 99, 162, 181, 185, 196, 219, 441, 444, 1224, 1233, 1242, 1296, 1324, 1329, 1365, 1369, 1375, 1393, 1427`；`RegistryBackupHelper.cs:39, 134`；`BackupManagerViewModel.cs:2036, 2040` |
| `PageDiagnosticService` | `RestorePointPage.xaml.cs:23, 42, 46, 65`；`BackupManagerPage.xaml.cs:32, 45, 57` |
| `IDiagnosticLogService` | 仅 `App.xaml.cs:823, 830`；**未注入** RestorePoint/Snapshot 服务 |
| `ILogger<T>` | 仅 Shell 基线服务：`BaselineManualGuideService.cs:31, 89`；`BaselineDomainCollector.cs:148`；`BaselineRestoreOrchestrator.cs:98`；`FirstRunBaselineOrchestrator.cs:77`；`FirstRunBackupService.cs:21` |
| 错误分析 | `Core/SystemRestore/`、`Infrastructure.Windows/SystemRestore/`、`Shell/Services/SystemRestoreService.cs` 中 `OperationErrorAnalyzer` / `ErrorDiagnosticEngine` / `UserFriendlyErrorMapper` / `ErrorMessageHelper` **0 命中**。快照域改用 `Core/Diagnostics/BackupDiagnosticService.cs:10`，由 `BackupManagerViewModel.cs:83, 309, 1008-1014, 1081-1087` 调用 |
| 安全文件工具 | `SafeProcessHelper`/`SafeDirectoryOperations`：`SystemRestoreManualLauncher.cs:31`；`BackupManagerViewModel.cs:2052, 2055`；`SnapshotService.cs:84, 177` |
| **盲点（已确认）** | `Core/SystemRestore/` 目录内 `ILogger` / `LocalizationService` / `OperationErrorAnalyzer` / `Debug.WriteLine` **全部零命中**——所有失败路径以英文 `SystemRestorePointWorkflowResult` 原样返回 |

---

## 9. 安全（Security Protections / Mitigations）

> **本域最重要的事实：存在两套互不连通的机制。**
>
> 1. **fail-closed 边界**：`SecurityProtectionOwner` + `FailClosedSecurityOptimizationOperation` —— 设计上无条件拒绝任何削弱保护的操作。**生产组合中未接线**（见 §9.3）。
> 2. **目录层真实可执行操作**：`security.disable_*` / `basic.disable_*` 实际是 typed `OrchestratedChangeOperation`，**会真实写注册表**，仅由确认权威 + 编排器约束。**这才是运行时真实行为。**
>
> 阅读安全相关代码时若只看第 1 套，会得出"安全项全部被拒绝"的错误结论。

### 9.1 入口

| 层 | 位置 | 说明 |
|---|---|---|
| 无独立安全页面/VM | — | 全仓无 `SecurityPage` / `SecurityViewModel`；`Nav.*Security*` 无命中 |
| 主入口（模块页） | `Shell/Views/PerformanceBoostPage.xaml:11` `<views:MasterModuleTemplate ModuleKey="performance"/>`；nav `NavigationCatalog.cs:90` | 安全项位于 `BuiltInOptimizationOperationProvider.cs:208-214`（`"performance"` 模块）+ `:403-493` |
| 游戏模式入口 | `Shell/ViewModels/GameModeHubViewModel.cs:110-122` | 固定 7 项 `security.disable_*`；`:118-122` **全部强制 `IsCompatible=false`**，`IncompatibleReason = "产品安全策略禁止为了性能关闭系统保护；该入口固定 PolicyDenied。"`；UI `EsportsModePage.xaml.cs:484, 1128, 2000, 2055` |
| 隐私模块（相邻） | `Shell/Views/PrivacyShieldPage.xaml:6` `ModuleKey="privacy"` | `DisableCeipCompositeOperation` @ `Core/Optimization/Operations/PrivacyTelemetryCompositeOperation.cs:17` |
| Core 安全边界 | `Core/Security/`（4 文件：Contracts / Models / Owner / FailClosedOperation） | **无任何 Shell 引用** |
| Provider 注入点 | `BuiltInOptimizationOperationProvider.cs:47, 61, 70, 121, 127, 211, 1828` | `ISecurityProtectionOwner` 仅透传，方法体内未使用（见 §9.3） |

### 9.2 契约接口

| 接口/类型 | 位置 |
|---|---|
| `ISecurityProtectionInspector` | `Core/Security/SecurityProtectionContracts.cs:3-8` |
| `ISecurityProtectionOwner` | 同上 `:10-15` |
| `IOptimizationOperation` 实现 | `FailClosedSecurityOptimizationOperation.cs:5`；`SecurityMitigationOperation.cs:9-96`（抽象 + 7 子类）；`SpectreMeltdownMitigationOperation.cs:5`；`PolicyDeniedOptimizationOperation.cs:3` |
| 身份常量 | `SecurityProtectionModels.cs:3-8`：`OwnerId = "gameoptimizer.security-protection"`、`ResourceKey = "security:machine:protection-policy"`、`MinimumWindowsBuild = 19041` |
| 区域枚举 | `:10-25`（13 个 `SecurityProtectionArea`） |
| 意图 | `:27-31` `SecurityProtectionIntent { WeakenProtection, RestoreProtection }` |
| 状态 | `:33-47`（12 个，含 `PolicyDenied` / `RecoveryUnproven` / `TamperProtected` / `ThirdPartySecurityProvider` / `ManagedByPolicy` / `DriftBlocked` / `PartialState` / `PlatformFailure` / `TimedOut` / `Cancelled` / `PermissionDenied` / `Unsupported`） |
| 时序 | `:76-79` `SecurityProtectionTimingOptions.Default = 8s` |
| 目录级确认 | `Core/Contracts/Changes/IChangeCapability.cs:121-124`（`IChangeConfirmationPrompt`）、`:126-135`（`IChangeConfirmationAuthority`） |

### 9.3 状态所有者

| 状态 | 所有者 | 证据 |
|---|---|---|
| fail-closed 决策 | `SecurityProtectionOwner` | `SecurityProtectionOwner.cs:3`；ctor `:9-19`；`EvaluateAsync:21-74`：`:29-33` **无条件**拒绝 `WeakenProtection`；`:36-37` 超时 linked CTS；`:50-54` catch→`PlatformFailure`；`:56-70` 七重门（Windows 版本 / 管理员 / 第三方安全产品 / 策略托管 / 篡改保护 / 状态完整性 / 指纹漂移）；`:72-73` **恒以 `RecoveryUnproven` 结束** |
| 无状态兜底 | `UnavailableSecurityProtectionOwner.Instance` | `SecurityProtectionOwner.cs:83-101`：`WeakenProtection`→`PolicyDenied`，其余→`RecoveryUnproven`；均 `Allowed=false` |
| **注册现状（关键）** | `ISecurityProtectionOwner` **无任何 DI 注册**；`ISecurityProtectionInspector` **无生产实现** | `Core/Extensions/ServiceCollectionExtensions.cs:62` 是 `provider.GetService<ISecurityProtectionOwner>()`（可为 null）→ `BuiltInOptimizationOperationProvider.cs:61` 退化为 `UnavailableSecurityProtectionOwner.Instance`。全仓 `SecurityProtectionOwner` 26 命中（生产 18 / 测试 8）：除定义/契约文件外，生产侧为 `Core/Extensions/ServiceCollectionExtensions.cs:62`、`SecurityMitigationOperation.cs:11, 20`、`FailClosedSecurityOptimizationOperation.cs:7, 11` 与 `BuiltInOptimizationOperationProvider.cs` 的 8 处参数透传；测试侧 `GameOptimizer.Tests/Security/SecurityProtectionOwnerTests.cs`（`:5, 11, 43, 56, 61, 69, 82, 93`）；`ISecurityProtectionInspector` 唯一实现是同一测试文件的 `:102` `FakeInspector` |
| **Provider 内未使用** | `BlockedSecurity(...)` 定义后**全仓无调用点** | `BuiltInOptimizationOperationProvider.cs:2293-2301` 定义；`securityProtectionOwner` 仅出现在 `:47, 61, 70, 121, 127, 211, 1828`（声明与透传），`BuildPerformanceModule` / `BuildCpuModule` 方法体内未引用。`GameOptimizer.Tests/Security/SecurityCatalogBoundaryTests.cs:19` 断言目录中不存在任何 `FailClosedSecurityOptimizationOperation` 实例 |
| **目录层真实可执行项** | typed `OrchestratedChangeOperation` | `security.disable_vbs` → `SYSTEM\CurrentControlSet\Control\DeviceGuard\EnableVirtualizationBasedSecurity=0`（`:405-418`）；`hvci` → `...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled=0`（`:419-432`）；`cfg` → `SYSTEM\...\Session Manager\kernel\MitigationOptions=0`（`:433-446`）；`defender_realtime` → `SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection\DisableRealtimeMonitoring=1`（`:447-460`）；`smartscreen` → `...\Explorer\SmartScreenEnabled="Off"`（`:461-476`，string op）；`sehop` → `...\kernel\DisableExceptionChainValidation=1`（`:479-492`）；`dep` → `PolicyDenied(...)`（`:477-478`，不写入）；`cpu.disable_spectre_meltdown` → `...\Memory Management\FeatureSettingsOverride;...Mask="3;3"`（`:1876-1891`）。全部 `RiskLevel=Extreme`（smartscreen 为 `Balanced`）、`requiresExplicitConfirmation:true`、`RequiresRestorePoint=false` |
| 相邻可执行项 | `basic.*` | `basic.disable_uac` → `...\Policies\System\EnableLUA=0`（`OptimizationRegistryDwordCapabilityDefinitions.cs:526-534`）；`basic.disable_security_notifications` → `...\PushNotifications\ToastEnabled=0`（`:536-545`）；`basic.disable_windows_update` → `...\WindowsUpdate\AU\NoAutoUpdate=1`（`:547-555`）；`basic.disable_security_center` / `disable_firewall` 见 `BuiltInOptimizationOperationProvider.cs:219-254` |
| 授权（真实门禁） | `ChangeConfirmationAuthority` | `Core/Services/Changes/ChangeConfirmationAuthority.cs:6-96`：`_issued` 进程内；`:20-65` `RequestAsync`；`:67-89` `TryConsume` 校验 PlanId/PlanHash/Scope/Expiry 且单次消费（`:87`） |
| 遗留兼容类型 | `SecurityMitigationCompatibilityOperation` | `SecurityMitigationOperation.cs:11, 20-28`：硬编码 `UnavailableSecurityProtectionOwner.Instance`，**不经 DI**；`:34` `RiskLevel=Extreme`；`:40` `RequiresRestorePoint=false`；`ApplyAsync`/`RestoreAsync` 恒 `false`。**7 个子类均不在当前目录中**（`SecurityCatalogBoundaryTests.cs:94-96` 列为禁止出现 token） |

### 9.4 依赖方向

- `Core/Security/*` **无** `using GameOptimizer.Shell`；**无** `ILogger` / 本地化 / `Debug` 引用（grep 0 命中）。
- `FailClosedSecurityOptimizationOperation` ← `SecurityMitigationCompatibilityOperation`（`SecurityMitigationOperation.cs:11, 20-28`）——该路径**硬编码 `UnavailableSecurityProtectionOwner.Instance`，绕过 DI**。
- Shell → Core：`GameModeHubViewModel.cs:110-122` 只用 `OperationRiskLevel` + 硬编码 id 字符串，**不引用** Core 安全契约。
- **无 Core→Shell 上向依赖。**
- Provider 侧：`ISecurityProtectionOwner` 由 `Core/Extensions/ServiceCollectionExtensions.cs:62` 以 `GetService`（可选）注入，`BuiltInOptimizationOperationProvider.cs:61` 兜底——**契约依赖方向正确，但链路断开**。

### 9.5 共享可变状态

| 对象 | 风险 |
|---|---|
| `ChangeConfirmationAuthority._issued` | 进程内单次消费表；重启后未消费 token 失效 |
| `BuiltInOptimizationOperationProvider` 的 4 个 `private static` 可变字段 | `:27-30` `_cleanupService` / `_cleanupQuarantineManager` / `_cleanupAuthorizationGate` / `_hostsBackupOwner` —— **进程级可变静态**，与安全域同处一个 provider。另有兼容 ctor `:34-42` 回退 `UnavailableChangeOrchestrator.Instance`（`:2411`） |
| `SecurityProtectionIdentity.ResourceKey` / `OwnerId` | 声明的资源锁与所有者身份常量；**当前无锁实现消费它**（`ISecurityProtectionOwner` 无注册） |
| 注册表键（读+写） | §9.3 表中全部 HKLM 路径 |
| 无自有磁盘文件 | 安全域自身不写文件；恢复材料由 Changes 域 `IOperationRestoreStore` 统一管理（`FileOperationRestoreStore`，`Core/Extensions/ServiceCollectionExtensions.cs:36`） |

### 9.6 跨域耦合

| 对端 | 耦合点 |
|---|---|
| 变更编排 | 所有安全项经 `IChangeOrchestrator` + `IChangeConfirmationAuthority`（`Core/Extensions/ServiceCollectionExtensions.cs:38-39, 57`）；`OptimizationEngine.cs:1773-1779` `IsFailClosedCatalogOperation` 显式识别 `FailClosedSecurityOptimizationOperation`；`OptimizationProviderRuntimeManifest.cs:56` 归类 `ManagedFailClosed` |
| 兼容性策略 | `WindowsCompatibilityPolicyTests.cs:74-112` 断言 `basic.disable_security_center`/`uac`/`firewall`/`security_notifications`/`security.disable_*`（除 dep）"显示真实效果而非 PolicyDenied 文案"；`:107-111` 断言 `security.disable_dep` 是 `PolicyDeniedOptimizationOperation` |
| 修复域 | `Core/Services/Repair/RepairOperations/OptimizationRevertRepairs.cs:216` `security.disable_smartscreen` 修复项；`SecurityCatalogBoundaryTests.cs:108-119` 断言其 `CheckHealthAsync()==false`、`RepairAsync` 失败且 Detail 含 `"typed adapter"` + `"未执行注册表写入"` |
| 游戏模式 | `GameModeHubViewModel.cs:110-122`；`EsportsPresetPlanner.cs:48, 71` 仅选 `IsCompatible` 项 → **安全项永不被预设选中**；`EsportsAppliedIncompatibleScenarioTests.cs:71, 84-87` 断言 |
| 优化目录迁移 | `OptimizationCatalogMigrationTests.cs:134-139` 断言 6 个 `security.disable_*` 均已迁移为 typed 且显式确认；`SecurityCatalogBoundaryTests.cs:22-50` |
| 快照/还原 | `SecurityMitigationOperation.cs:40` `RequiresRestorePoint=false`；typed 安全项目标值由 Changes 域恢复存储 + 快照 V5 typed 采集覆盖（`SnapshotService.cs:143-150`） |
| 内存/清理/网络/电源/诊断 | **未发现直接引用** |

### 9.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | `Core/Localization/OptimizationTranslations.cs:142-165`（`security.disable_*` 标题/描述）、`:1007, 1321-1344`；`LocalizationService.ZhCN.cs:1215, 3035, 3548, 5643-5644, 5153`；`LocalizationService.EnUS.cs:1239, 5034, 6392, 6699-6700`。**三个键仅存在于资源文件、全仓 C# 引用 0 命中**：`Confirm.HighRisk.SecurityMitigation`、`Module.Security`、`Step5.Module.SecurityMitigation` → 疑似遗留文案。`GameModeHubViewModel.cs:110` 是 `GameMode.Category.SecurityMitigation` 的唯一消费者 |
| 日志 | `Core/Security/` 内 **0 命中**（无 `ILogger`、无 `Debug.WriteLine`、无 `PageDiagnosticService`）。安全项 UI 日志仅经通用 `MasterModuleTemplate.xaml.cs:81, 426, …`（`PageDiagnosticService`）与 `Shell/Services/DebugLogger.cs`（写 `<LogsDir>\ui-freeze-debug.log`，使用点 `MasterModuleTemplate.xaml.cs:1815-2358`） |
| 错误分析 | 安全域 **0 命中**。相邻隐私域使用：`PrivacyTelemetryCompositeOperation.cs:132, 156, 280, 304` `OperationErrorAnalyzer.HandleOperationException(ex,"隐私防护")` |
| 危险确认 UI | `Shell/Helpers/OperationHelper.cs:79` `DangerousOperationDialog.Show`；`Shell/Services/WpfBaselineRiskAcknowledgementPrompt.cs:37`；`Shell/Services/WpfChangeConfirmationPrompt.cs:8`（`:47` 特判 `ProcessTermination` id） |

---

## 10. 诊断（Diagnostics）

### 10.1 入口

| 层 | 位置 |
|---|---|
| Core DI | `Core/Extensions/ServiceCollectionExtensions.cs:72-73`（`IDiagnosticLogService` + `ILoggerProvider`）；`:77, 128-129`（`IDiagnosticService`、`ErrorDiagnosticEngine`、`SolutionProvider`）；`:131-132`（`PowerOperationDiagnostics`、`BackupDiagnosticService`） |
| Core DI（**静态注入**） | `:148-157` `InitializePowerOperationDiagnostics` → `BasePowerPlanOperation.SetDiagnostics(...)` `:152`、`PowerCfgUtility.Logger` `:155-156` |
| Shell 覆盖 | `App.xaml.cs:309-311` `new DiagnosticLogService(provider.GetRequiredService<IAppSettingsStore>().Current.LogBackupDirectory)`；`:312` `DiagnosticPackageService`；`:257` `LogRetentionService`；`:373-378` `SettingsPage` 工厂 |
| Shell 启动 | `App.xaml.cs:423-424` `LogRetentionService.ApplyRetentionPolicy()` |
| Shell UI | `Shell/Views/SettingsPage.xaml.cs:35, 51, 831-905`（`:845`/`:876` `GetPreview()`，`:905` `CreateDiagnosticPackageAsync`，`:913-915` 结果只本地展示、不上传） |
| Shell 其他消费者 | `App.xaml.cs:199-211`（`UserFriendlyErrorMapper`）；`ViewModels/DashboardViewModel.cs:183, 1124, 1905`；`ViewModels/BackupManagerViewModel.cs:83, 300-309, 2079`；`Views/FirstRunBaselineDialog.xaml.cs:67, 86`；`Startup/StartupOrchestrator.cs:304` |
| 维护诊断（**无 Shell 页面**） | Core `Services/MaintenanceDiagnostics/MaintenanceDiagnosticOwner.cs:114-178`；Windows `MaintenanceDiagnostics/WindowsMaintenanceDiagnosticPlatform.cs:151-161`（重复注册抛 `"IMaintenanceDiagnosticOwner 已注册，拒绝创建第二个维护诊断边界。"` `:156`）；Shell 唯一引用 `Extensions/NetworkResetServiceCollectionExtensions.cs:58` |

### 10.2 契约接口

| 接口/类型 | 位置 |
|---|---|
| `IDiagnosticLogService` | `Core/Diagnostics/IDiagnosticLogService.cs:9-19` |
| `IDiagnosticService` | `Core/Diagnostics/IDiagnosticService.cs:9-15`（接口**无** `SaveReportToFileAsync`，实现类 `DiagnosticService` `:42-45` 有——注意 `:38-42` 是 `ErrorReportGenerator.cs:38` 的同名方法） |
| 维护诊断契约 | `Core/Services/MaintenanceDiagnostics/MaintenanceDiagnosticOwner.cs:43-58`（`IMaintenanceDiagnosticPlatform` `:43-48`、`IMaintenanceDiagnosticOwner` `:50-58`） |
| 日志条目 | `Core/Diagnostics/DiagnosticLogEntry.cs:6` |
| 错误模型 | `Core/Diagnostics/ErrorModels.cs:7-176`（`ErrorCategory` `:7-19`、`ErrorSeverity` `:21-27`、`ErrorCode` `:29-69`（`GO-{CAT}-{SEV}-{Number:D4}` `:67`）、`DiagnosticResult` `:79-92`、`ErrorReport.ToText()` `:110`） |
| 诊断包（**无接口**） | `Core/Diagnostics/DiagnosticPackageService.cs:22`：`GetPreview()` `:79-87`、`CreateDiagnosticPackageAsync(...)` `:89-144`；静态 `GetLogDirectory()` `:555`、`GetAvailableLogFiles()` `:557-596` |
| 维护诊断目录 | `Core/Services/MaintenanceDiagnostics/MaintenanceDiagnosticOwner.cs:60-112` — `repair.chkdsk` / `repair.location_service`(lfsvc) / `repair.memory_service`(SysMain) / `repair.search_index`(WSearch) / `repair.storage_service`(StorSvc) / `repair.superfetch`(SysMain) |

### 10.3 状态所有者

| 类 | 可变状态 |
|---|---|
| `DiagnosticLogService`（`:12`） | 单例；`ConcurrentQueue<DiagnosticLogEntry> _entries`（`:16`，上限 `MaxEntries = 120` `:14`）、`SemaphoreSlim _writeLock`（`:17`）、`_logFilePath`（`:18`，`optimizer-yyyyMMdd.log` `:35`）、`_disposed`（`:19`）；`MaxLogFileSizeBytes = 10MB`（`:15`），轮转 `:175-186`。**无 static 可变字段** |
| `DiagnosticPackageService`（`:22`） | 无可变实例状态；静态只读白名单 `:24-34`（`MaximumLogFileCount=64`、`MaximumSingleLogBytes=10MB`、`MaximumTotalLogBytes=40MB`、`AllowedLogExtensions`、`AllowedLogNamePrefixes`）与可注入 `Func<>` 接缝 `:42-46` |
| `MaintenanceDiagnosticOwner`（`:114`） | 无写入状态；`RepairAsync` 只诊断并返回 `RepairResult.Failed`（`:174-176`，fail-closed）；静态兜底 `MaintenanceDiagnosticOwnerFallback.Instance`（`:194-198`）、`UnavailableMaintenanceDiagnosticPlatform`（`:180-192`） |
| `MaintenanceDiagnosticSnapshot`（`:28`） | 只读 record，无持久化 |
| `DiagnosticService`（`:9-46`） | `_diagnosticEngine` / `_solutionProvider` / `_reportGenerator`，**均在 ctor `:15-20` 内 `new` 构造（不经 DI）** |
| 静态共享状态（跨域） | Core DI `:148-157` 向 `BasePowerPlanOperation` 与 `PowerCfgUtility` 的静态成员写入诊断依赖——**进程级共享** |

### 10.4 依赖方向

- 日志链路：`ILoggerFactory` → `DiagnosticLogLoggerProvider`（`:45, 52-55`，**仅 category 以 `"GameOptimizer"` 开头才非 `NullLogger`**）→ `DiagnosticsLogger`（`:9-41`，`IsEnabled` 限 `>= Information` `:26-29`）→ `IDiagnosticLogService.Append` `:39`。
- 错误分析链路：`OperationErrorAnalyzer`（`Core/Utilities/OperationErrorAnalyzer.cs:16`）持有 `static Lazy<DiagnosticService>` —— **绕过 DI**；`DiagnosticService` ctor 内再 `new SolutionProvider()` / `new ErrorDiagnosticEngine(...)`（`:17-18`）⇒ **DI 中注册的 `ErrorDiagnosticEngine` / `SolutionProvider` 单例（`:128-129`）不被 `DiagnosticService` 使用**。
- **两处"已注册但无消费者"**：
  - `IDiagnosticService`——全仓 grep 仅接口定义、实现类、注册三处，**无任何消费者**。
  - `ISystemHealthService`——仅 `SystemHealthService.cs:6, 19`、接口 `ISystemHealthService.cs:44-47`、Core `:75` 注册，**无消费者**（健康自检已有实现但未接线）。
- `MaintenanceDiagnosticOwner` 由修复域消费：`Repair/RepairItemRegistry.cs:17, 26`（注入 `IMaintenanceDiagnosticOwner?`）；`Repair/RepairOperations/MemoryRepairs.cs:8-10, 20, 85`、`StorageRepairs.cs:13-16, 28, 41, 54, 128`、`PrivacyRepairs.cs:14-17` 均以 `IMaintenanceDiagnosticOwner?` 形参 + `MaintenanceDiagnosticOwnerFallback.Instance` 兜底，**不引用具体类 `MaintenanceDiagnosticOwner`**。

### 10.5 共享可变状态

| 对象 | 位置 / 说明 |
|---|---|
| 日志目录 | 默认 `AppPaths.LogsDir`（`AppPaths.cs:30`；`DiagnosticLogService.cs:89` 回退引用）；可被 `AppPaths.TryConfigureLogsDirectory`（`:90-125`）重定向（`static string? _configuredLogsDirectory` `:13` + `Volatile.Read` `:30`）；UNC（`\\`）与不可写目录被拒（`ResolveWritableLogDirectory` `:49-90`，写入探针 `:62-73`） |
| 日志文件族（`AppPaths.cs`） | `optimizer-yyyyMMdd.log`（`LogsDir`）；`page_diagnostics.log` `:68`（`PageDiagnosticService.LogPath` `:9`，5 MB 自截断 `PageDiagnosticService.cs:12, 24-27`，`static object _lock` `:11`）；**`LogsDir` 下**：`activity.json` `:56`、`operation_history.json` `:58`、`transaction.log` `:59`、`audio_debug.log` `:60`；**`AppDataRoot` 下（不在 LogsDir）**：`activity_history.json` `:57`、`crash.log` `:61`（`CrashLogPath`） |
| 诊断包临时目录 | `AppPaths.TempDir\YuqiEngine_Diag_{Guid:N}`（`DiagnosticPackageService.cs:499-500`），finally 整体删除 `:130-143` |
| 输出候选目录 | `AppPaths.LogsDir` `:423`、`ReportsExportDir` `:429`、`TempDir` `:430`（`CreateProductionOutputDirectoryCandidates` `:419-430`），按序回退 `:115-121` |
| 保留策略删除 | `LogRetentionService.ApplyRetentionPolicy` `:23-67`：`LogsDir` 下 `*.log`/`*.log.old` 且 `LastWriteTime < now - retentionDays` 经 `SafeFileHelper.DeleteFile`（`:50`）删除；`retentionDays <= 0` 直接返回（`:26-29`） |
| 维护诊断 | 快照仅内存对象（`MaintenanceDiagnosticSnapshot` `:28`/`:36`），无持久化 |

### 10.6 跨域耦合

| 对端 | 耦合点 |
|---|---|
| 备份/还原点 | `BackupDiagnosticService.DiagnoseBackupFailure`（`:12-125`，错误码 1001-1004/1099，`AppPaths.SnapshotsDir` `:28, 44, 143`、`AppDataRoot` `:158`）、`GetRepairOptions` `:127-163`；Shell `BackupManagerViewModel` `:300-309` |
| 电源 | `PowerOperationDiagnostics` 注入 `ErrorDiagnosticEngine`（`:17, 22`）；Core DI `:148-157` 的静态注入即电源域 |
| 安全/权限 | `ErrorDiagnosticEngine.Diagnose` 分支含 `UnauthorizedAccessException`/`Security`/`Win32`/`IO`（`:25-36`）；`SolutionProvider.GetPermissionSolutions` `:35+` 提供 `RunAsAdmin` `:44`、`resmon.exe` `:66`、`services.msc` `:73`；`OperationErrorAnalyzer` 三处直接读 `WindowsElevation.IsElevated`（`Core/Utilities/OperationErrorAnalyzer.cs:26, 44, 58`，**不再经 `SystemGuard`**，见 §11.2）；`ErrorMessageHelper.IsUacCancelled`（`Core/Services/ErrorMessageHelper.cs:127`，Win32 1223/5） |
| 硬件信息 | `ErrorReportGenerator.CollectSystemInfo` `:218-234`；`EnhancedOperationResult.CompatibilityInfo` `:163` / `SystemRequirement` `:194` / `SystemInfo` `:220` / `PerformanceImpact` `:251` |
| 修复域 | `ErrorReportGenerator.SaveReportToFileAsync` `:38-42`；`EnhancedOperationResult.AutoRepairOption` `:91`、`RepairRiskLevel` `:137` |
| 游戏模式 | `Core/Diagnostics/` 14 个文件内**无**游戏模式/Esports 引用（仅 `DiagnosticLogLoggerProvider.cs:52` 的 `"GameOptimizer"` category 前缀与命名空间行）——**未验证**：未做完整反向扫描（游戏模式侧是否消费 `DiagnosticPackageService` / `EnhancedOperationResult` 未穷举） |

### 10.7 共享工具使用点

| 工具 | 使用情况 |
|---|---|
| 本地化 | `LocalizationService.Instance`（`Core/Localization/LocalizationService.cs:8-24`，静态 `Lazy<LocalizationService> _instance` `:10`、`Instance` `:11`、`AvailableLanguages` `:18-22`、`LanguageChanged` `:24`、`Get` `:72` 带 en-US 回退 `:79`）：`ErrorDiagnosticEngine.cs:14`、`SolutionProvider.cs:9`、`UserFriendlyErrorMapper.cs:20, 44, 87, 119`、`BackupDiagnosticService.cs:14, 137, 152`、`EnhancedOperationResult.cs:47`；Shell `SettingsPage.xaml.cs:24, 75-130` |
| 日志 | `Debug.WriteLine` @ `DiagnosticPackageService.cs:127, 141, 190, 279, 532, 590`（6 处）、`DiagnosticLogService.cs:31, 44`、`LogRetentionService.cs:53, 59, 65`、`App.xaml.cs:404`；`PageDiagnosticService`（静态，`Shell/Services/PageDiagnosticService.cs:7-61`）被 `MainWindow.xaml.cs:297-333, 582-680`、`Navigation/PageFactory.cs:83`、`Views/BackupManagerPage.xaml.cs`、`Views/HardwareInfoPage.xaml.cs` 使用；**两份同名静态类**：`Shell/Services/DebugLogger.cs:12` 与 `Shell/Diagnostics/DebugLogger.cs:14`（后者写 `<LogsDir>\diag\freeze-diag-*.log`，前者写 `<LogsDir>\ui-freeze-debug.log`） |
| 安全文件工具 | `SafeDirectoryOperations.TryCreateDirectory`（`DiagnosticPackageService.cs:101`、`DiagnosticLogService.cs:29`）、`SafeFileWriter.TryWriteAllText` `:39`、`SafeFileHelper.DeleteFile`（`LogRetentionService.cs:50`）、`SafeProcessHelper.StartProcess`（`SettingsPage.xaml.cs:924`） |
| 错误分析 | 见 §10.4；`UserFriendlyErrorMapper`（`:13-137`）另被 `Core/Services/CorePresetService.cs:43`、`Shell/Services/NetworkBackupService.cs:34, 44`、`App.xaml.cs:199-211` 使用 |

---

## 11. 跨域共享清单

### 11.1 共享可变状态（进程级 / 跨域）

| 共享物 | 位置 | 涉及域 |
|---|---|---|
| `IChangeOrchestrator` / `IChangeConfirmationAuthority` | `Core/Contracts/Changes`；Core DI `:57`（orchestrator）/ `:39`（confirmation authority）；`:61` 是 Provider 工厂内的解析点，非注册 | 内存 + 清理 + 网络 + 电源 + 游戏模式 + 还原点 + 安全 |
| `FileRecoveryClaimStore` 根 `Changes\recovery-claims` | `FileRecoveryClaimStore.cs:24-27` | 还原点 ↔ 变更编排（`SnapshotRestoreSafety.cs:206`、`ChangeOrchestrator.cs:55` 共用） |
| `BuiltInOptimizationOperationProvider` 的 4 个 `private static` 字段 | `:27-30` | 系统清理 + 网络 + 安全 + hosts（**进程级可变静态**） |
| 电源静态日志出口 `PowerCfgUtility.Logger` / `BasePowerPlanOperation._diagnostics` | `PowerCfgUtility.cs:21`；`AdvancedPowerPlanOperation.cs:75` | 电源 ↔ 诊断（Core DI `:148-157` 注入） |
| `LocalizationService.Instance`（进程单例） | `LocalizationService.cs:10-11` | 除内存域外全部 |
| `IDiagnosticLogService` | Core DI `:72` → Shell 覆盖 `:309` | 诊断 + Shell 全域 |
| `PageDiagnosticService`（静态文件日志 + `static object _lock`） | `Shell/Services/PageDiagnosticService.cs:9, 11` | 内存 + 清理 + 网络 + 游戏模式 + 硬件 + 还原点 + 诊断 UI |
| `AppPaths`（静态 + `Volatile` 配置项 + 运行期根覆盖） | `AppPaths.cs:11-13, 21, 30` | 全部九域 |
| `WindowsElevation._cached`（**提权判定的唯一权威缓存**，见 §11.2） | `Core/Services/WindowsElevation.cs:69`（`Refresh()` `:115-121`） | 全部九域：Core / Infrastructure / Shell 的权限裁决均经它 |
| `SystemCompatibilityChecker.CurrentSystem`（`Lazy<SystemInfo>`） | `SystemCompatibilityChecker.cs:18, 23, 308-314` | 电源诊断 + 兼容性 |
| `GpuInventoryService._snapshot` / `CpuTopologyService._cachedTopology` | `GpuInventoryService.cs:22-24`；`CpuTopologyService.cs:14` | 硬件 + 电源 + 游戏模式 + 兼容性 |
| 两个独立 CPU 缓存：`ProcessAffinityService._cachedCpuInfo` / `CpuAffinityService._cachedCpuInfo` | `:84` / `:13` | 游戏模式内部（无失效机制） |
| `WindowsMemoryCleaningPlatform.TrimBaselines` / `TerminatedProcesses`（static） | `:25, 26` | 内存域内跨 capability |

### 11.2 提权判定：唯一权威 + 多层缓存（"已管理员仍提示需要管理员"的根因收敛点）

**唯一权威实现**：`Core/Services/WindowsElevation.cs`（359 行，`GameOptimizer.Core` 程序集）。

| 成员 | 位置 | 说明 |
|---|---|---|
| `ElevationLevel`（4 态） | `WindowsElevation.cs:15-28` | `Unknown=0` / `StandardUser=1` / `AdministratorNotElevated=2`（管理员组但 UAC 筛选令牌）/ `ElevatedAdministrator=3` |
| `ElevationSnapshot`（record） | `:33-46` | `Level` / `IsElevated` / `IsAdministratorAccount` / `IsWindows` / `ReasonCode` / `Detail`；`:42` `CanWriteMachineScope => IsElevated` |
| 原因码常量 | `:53-57` | `elevation.elevated-administrator` / `elevation.administrator-token-filtered` / `elevation.standard-user` / `elevation.non-windows` / `elevation.probe-failed` |
| `Current`（缓存读） | `:76-101` | 读令牌 `TOKEN_ELEVATION` + `TOKEN_GROUPS`；**令牌在进程生命周期内不变**，故结果进程级缓存（`_cached` `:69`，`Gate` `:65` 保护）；**仅 `Unknown` 不缓存**（`:93-96`），以便下次重试 |
| `Refresh()` | `:115-121` | 显式丢弃缓存（`_cached = null`），不重算 |
| `Probe()`（不读缓存） | `:126-202` | `:141-143` `WindowsIdentity.GetCurrent()` → `identity.AccessToken.DangerousGetHandle()`；`:148-161` 令牌 API 全部不可用时退回 `WindowsPrincipal.IsInRole`；`:163-196` 判定 `elevated` / `administratorAccount` 并产出对应 `ElevationLevel` |
| 面向用户的归因 | `:207-218` `DescribeMachineScopeUnavailable()` | **把"缺少管理员"与"已提权但系统拒绝"分开**：`ReasonElevated` → "已提权；若仍被拒绝，原因是策略/ACL/安全软件" |
| 测试接缝 | `:223-247` `OverrideProbeForTesting` | `internal`（`InternalsVisibleTo GameOptimizer.Tests`）注入判定结果 |

**消费方（grep 全仓确认，均为一行转发，不再各自判定）**：

| 消费方 | 位置 |
|---|---|
| `SystemGuard.IsAdministrator` / `IsAdministratorAccount` / `ElevationLevel` / `ElevationDetail` / `RefreshPrivilegeStatus()` | `Core/Services/SystemGuard.cs:36-47`（**已无 5 分钟 `AdminCacheExpiry` 静态缓存**，原文档描述已过时） |
| `PermissionChecker.IsRunningAsAdministrator()` / `IsAdministratorAccount()` / `CurrentElevationLevel` | `Core/Services/PermissionChecker.cs:17, 20, 23` |
| `OperationErrorAnalyzer`（3 处构造 `DiagnosticContext.IsAdminMode`） | `Core/Utilities/OperationErrorAnalyzer.cs:26, 44, 58` |
| `SystemCompatibilityChecker` → `SystemInfo.IsAdminMode` | `SystemCompatibilityChecker.cs:314`，私有 `IsAdministrator()` `:343` |
| `MemoryCleanerService.IsAdministrator` | `Core/Services/Memory/MemoryCleanerService.cs:45` |
| `RegistryPriorityService.HasAdminPrivileges()` | `Core/Services/ProcessPriority/RegistryPriorityService.cs:26` |
| `PowerServiceDiagnostics.IsAdministrator()` | `Core/Services/PowerServiceDiagnostics.cs:82` |
| `IUserPrivilegeService`（Shell UI 门面） | `Shell/Services/UserPrivilegeService.cs:90-98` `Refresh()` → `WindowsElevation.Probe()`；接口 `IUserPrivilegeService.cs:9-26`（新增 `IsAdministratorAccount` / `ElevationLevel` / `ElevationDetail` / `ElevationReasonCode`）；注册 `App.xaml.cs:254` |
| Infrastructure.Windows 各适配器私有 `IsAdministrator()` | 17 个文件 / 18 个引用点：`WindowsSystemRestorePointPlatform.cs:238`、`NetworkReset/WindowsNetworkResetPlatform.cs:26`（另 `:33` 用 `DescribeMachineScopeUnavailable()`）、`NetworkDiagnostics/WindowsNetworkAdapterDhcpPlatform.cs:31`、`PowerManagement/PowerSchemeChangeCapability.cs:67`、`PowerManagement/PowerSettingChangeCapability.cs:69`、`PowerManagement/WindowsPowerPlatform.cs:30`、`Services/WindowsBcdEditAccessor.cs:147`、`WindowsDeviceStateAccessor.cs:103`、`WindowsMmAgentAccessor.cs:74`、`WindowsHostsBackupOwner.cs:163`、`WindowsScheduledTaskAccessor.cs:142`、`WindowsServiceAccessor.cs:314`、`WindowsAppxRemovalOwner.cs:323`、`Registry/RegistryDwordCapability.cs:83, 324`、`Registry/RegistryStringCapability.cs:623`、`Registry/RegistryValueSetCapability.cs:364`、`Registry/PerInterfaceRegistryCapability.cs:401`（其中 `WindowsAppxRemovalOwner.cs:323` 方法名为 `IsCurrentUserAdministrator`，语义相同） |
| 独立写入进程 | `Shell/Services/SystemRestorePointWriterEntryPoint.cs:187`（`WindowsSystemRestoreNativeApi.IsAdministrator`，经接口 `:175-182` 抽象） |

**仍然存在的多层缓存（不一致窗口已收窄到"缓存陈旧"而非"判定不同"）**：

| 缓存层 | 位置 | 失效方式 |
|---|---|---|
| `WindowsElevation._cached` | `WindowsElevation.cs:69` | `Refresh()`；`Unknown` 不缓存。**进程令牌不变 ⇒ 该缓存永不陈旧** |
| `SystemCompatibilityChecker` 的 `Lazy<SystemInfo>` | `SystemCompatibilityChecker.cs:18, 23` | 无失效入口；首次读取后固定（`:308-314` 取值那一瞬的 `WindowsElevation` 结果） |
| `UserPrivilegeService` 的 5 个字段 | `UserPrivilegeService.cs:19-23` | `Refresh()`（`:90-98`），ctor 内先刷新（`:16`）；`INotifyPropertyChanged` 广播 |

> **结论修正**：本文档早前版本称"三条权限真相并存、无同步机制"。当前工作树已收敛为**单一判定源**（`WindowsElevation`），上面三层的差异仅来自**取值时刻不同**（`Lazy<SystemInfo>` 是唯一无刷新入口的一层）。因此"已是管理员却提示需要管理员"的残留可能来自：(a) `SystemCompatibilityChecker` 的永久 `Lazy` 快照；(b) 某个 UI 仍在读自己算出的旧标志；(c) `OperationErrorAnalyzer` 之外的调用点直接读 `PermissionChecker`（见 §15.3）。**这属于待验证推论，不是本文档已证实的结论。**

**已不复存在的旧机制**：`SystemGuard.AdminCacheExpiry`（5 分钟静态缓存）与 `SystemGuard` 内自建的 `IsInRole` 判定在当前工作树中均无命中（grep 全仓 `*.cs`：`AdminCacheExpiry` 0 命中；`WindowsPrincipal` / `IsInRole` 仅命中 `WindowsElevation.cs:151, 159-160` 与 `SystemGuard.cs:33` 的注释文本）。

### 11.3 共享工具使用矩阵

| 工具 | 内存 | 清理 | 网络 | 电源 | 游戏模式 | 硬件 | 还原点 | 安全 | 诊断 |
|---|---|---|---|---|---|---|---|---|---|
| `LocalizationService.Instance` | ✗ Core | ✓ | ✓ Shell | ✓ Shell | ✓ | ✓ | ✓ | 仅资源文件 | ✓ |
| `ILocalizationService`（接口） | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗（全仓无消费者） |
| `ILogger<T>` | ✗ | ✗ | ✗ Core | 仅诊断服务 | 部分 | ✗ | 仅 Shell 基线 | ✗ | ✓ |
| `Debug.WriteLine` | ✓ Core（`Core/Services/Memory/ProcessCleanerService.cs` 7 处：`:106, 111, 121, 190, 195, 215, 222`） | ✓ | ✓ | 密集 | ✓ | 密集 | 密集 | ✗ | ✓ |
| `PageDiagnosticService` | ✓ | ✓ | ✓ | ✗ | ✓ | ✓ | ✓ | 仅通用模板 | ✓（自身） |
| `OperationErrorAnalyzer` 族 | ✗ | ✗ | ✓（目录操作） | ✓ | 间接 | ✗ | ✗ | ✗ | 定义处 |
| `IDiagnosticLogService` | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | 仅启动协调 | ✗ | ✓ |
| `ToastService` | ✗ | ✓ | ✗ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ |

**已确认的五个共享工具盲点**（改动/排障时必须知道）：

1. **`Core/PowerManagement/` 整个目录**：`ILogger` / `LocalizationService` / `OperationErrorAnalyzer` / `Debug.WriteLine` **全部零命中**——`PowerManagementOwner` 失败时不产生任何日志或本地化消息。
2. **`Core/SystemRestore/` 整个目录**：同上四项**全部零命中**——失败路径原样返回英文 `SystemRestorePointWorkflowResult`。
3. **`Core/Security/` 整个目录**：同上四项**全部零命中**。
4. **`Core/Services/SystemCleanup/`**：仅 `LocalizationService` + `Debug.WriteLine`，无 `ILogger`、无错误分析器。
5. **`Core/Services/Memory/`**：Core 侧**零本地化**，无 `ILogger`，无错误分析器——结果靠 `ReasonCode` 自描述；`Debug.WriteLine` **仅 `ProcessCleanerService.cs` 7 处**（`:106, 111, 121, 190, 195, 215, 222`）。

### 11.4 日志可见性链路（"失败时没有错误信息"的结构性原因）

```
业务代码
  ├─ ILogger<T>  → DiagnosticLogLoggerProvider
  │                   ├─ category 不以 "GameOptimizer" 开头 → NullLogger（丢弃）
  │                   └─ 否则 → DiagnosticsLogger
  │                              ├─ level < Information → 丢弃
  │                              └─ 否则 → DiagnosticLogService.Append
  │                                          → optimizer-yyyyMMdd.log
  ├─ Debug.WriteLine → 仅 DebugView / 附加调试器可见，不落盘
  └─ PageDiagnosticService.Log → page_diagnostics.log（仅 UI 层显式调用）
```

两个过滤条件叠加的结果：**只要 category 不是 `GameOptimizer*`，或级别低于 Information，`ILogger` 的输出就完全消失**。而 Power/SystemRestore/Security/Cleanup/Memory 五个 Core 域**根本不注入 `ILogger`**，因此它们的失败只能通过返回值/原因码传播；若 UI 层未把原因码展示出来，用户看到的就是"没有错误信息"。

同时 `DiagnosticLogService` 的 `_entries` 上限 120 条、单文件 10 MB——高频失败会覆盖早期证据。

---

## 12. 已确认的"注册但无消费者 / 定义但无调用"清单

| # | 对象 | 位置 | 状态 |
|---|---|---|---|
| 1 | `IDiagnosticService` | `Core/Diagnostics/IDiagnosticService.cs:9-15`；注册 `Core/Extensions/ServiceCollectionExtensions.cs:77` | 无任何消费者 |
| 2 | `ISystemHealthService` | `ISystemHealthService.cs:44-47`；注册 `:75` | 无任何消费者 |
| 3 | `ISecurityProtectionOwner` | `Core/Extensions/ServiceCollectionExtensions.cs:62`（`GetService`，可为 null） | **无 DI 注册**；`SecurityProtectionOwner` 在生产中永不被构造 |
| 4 | `ISecurityProtectionInspector` | `SecurityProtectionContracts.cs:3-8` | **无生产实现**（唯一实现是测试 `FakeInspector`） |
| 5 | `BlockedSecurity(...)` | `BuiltInOptimizationOperationProvider.cs:2293-2301` | 定义后无调用点；`securityProtectionOwner` 参数在方法体内未使用 |
| 6 | `SystemMonitorService` | `Shell/Services/SystemMonitorService.cs:38`；注册 `App.xaml.cs:314` | 全仓无消费者 |
| 7 | `memory.process.terminate` 能力 | `MemoryCleaningContracts.cs` 常量 | 未注册为 `IChangeCapability`，无 Shell 调用点 |
| 8 | `ILatencyTestService` / `LatencyTestViewModel` | `Core/Services/Latency/ILatencyTestService.cs:8-30`；`Shell/ViewModels/LatencyTestViewModel.cs:12` | 无 DI 注册、无页面/XAML 绑定 |
| 9 | `IGameProcessMonitorService` / `IGameProcessPriorityService` / `IGameAutoOptimizationService` | `Shell/Services/…`；前两者的实现类为 `GameProcessMonitorService.cs:40` / `GameProcessPriorityService.cs:20`；`IGameAutoOptimizationService`（`IGameAutoOptimizationService.cs:5`）只有 `BaselineRestoreOrchestrator.cs:20, 32, 90, 98` 的可空参数引用 | 均**未注册**；`IGameAutoOptimizationService` **无实现类**（`BaselineRestoreOrchestrator` 恒收到 null） |
| 10 | `DemoSystemInsightService` | `Core/Services/DemoSystemInsightService.cs:7` | 仅定义，无注册/消费者 |
| 11 | `ErrorMessageHelper` | `Core/Services/ErrorMessageHelper.cs:11` | 全仓仅定义，无调用点 |
| 12 | `ILocalizationService`（接口） | `Core/Localization/ILocalizationService.cs:3-13` | 仅接口 + 实现，无 DI 注册与消费者 |
| 13 | `IPowerScenarioService` / `PowerScenarioService` / `ScenarioConfirmationDialog` | 接口 `Shell/Services/IPowerScenarioService.cs:11`；实现 `Shell/Services/PowerScenarioService.cs:16`；对话框 `Shell/Views/ScenarioConfirmationDialog.xaml.cs:15, 23-24` | **疑似死代码**：实现类与对话框均已定义，但全仓无 `new ScenarioConfirmationDialog(...)`、无 `IPowerScenarioService` DI 注册，故对话永不弹出 |
| 14 | `IFirstRunBackupService` | `Shell/Services/FirstRunBackupService.cs:11` | **未验证**是否经反射注册 |
| 15 | `Confirm.HighRisk.SecurityMitigation` / `Module.Security` / `Step5.Module.SecurityMitigation` | 仅资源文件 | 全仓 C# 引用 0 命中 |
| 16 | `NetworkOptimizationPage` | `Shell/Views/NetworkOptimizationPage.xaml.cs:9-12` | 13 行空壳（真实逻辑在 `NetworkTestPage`） |
| 17 | `LiveSystemInsightService` 缺省注入路径（早期版本误写为 `LightSystemInsightService`，该类型名**全仓不存在**） | `LiveSystemInsightService.cs:58-60` | 未注册 `IGpuInventoryService` 时 `new GpuInventoryService()`（`:60`），与 DI 实例并存 |

---

## 13. 未验证 / 待人工复核项

1. **未运行构建或测试**：所有"通过/失败"结论来自测试文件断言语句与源码文本，非实际运行结果。
2. `ISecurityProtectionOwner` / `ISecurityProtectionInspector` 是否经 `ModuleLoader.RegisterConfiguredModules`、`Program.cs` 或反射动态注册——未逐处穷举（结论基于全仓 `*.cs` 文本 grep）。
3. `SystemMonitorService` "无消费者" 结论基于全仓文本 grep，未验证容器泛型扫描/反射激活。
4. `MemoryCleaningCapability.cs:26, 125` 对 `WorkingSetTrim` / `ProcessTermination` 的分支语义未逐行确认；`StandbyListPurge` 是否在 descriptor 阶段即被拒，未验证。
5. `CleanupQuarantineManagement.cs:190-271`、`:285-584`、`:585-923` 未逐段阅读。
6. `SystemCleanupService.cs:206-428` 未逐行阅读。
7. `DiagnosticLogService.cs:97-172`（`Append`/`GetRecentEntries`/`FlushAsync` 主体）与 `:173-186` 轮转未逐行阅读。
8. `DiagnosticPackageService.cs:148-554`（脱敏、容量、manifest、白名单枚举主体）未逐行阅读。
9. `SolutionProvider.cs:81-367`、`ErrorReportGenerator.cs:43-217` 未阅读。
10. `SystemRestorePointOwner.cs` L83-926、`SnapshotService.cs` L206-450 / L500-735 / L796-1050、`SnapshotRestoreSafety.cs` L270-1039、`BackupManagerViewModel.cs` L645-1075 / L1158-2045、`BuiltInOptimizationOperationProvider.cs` L215-400 / L500-1820 仅按签名与调用点核对。
11. `MasterModuleTemplate.xaml.cs`（约 2400 行）未读全文；安全项在 UI 上的呈现与危险确认触发路径未逐行核对。
12. `RestorePointPage.xaml` / `BackupManagerPage.xaml` 未读；绑定正确性仅由 `GameOptimizer.Tests/Visual/*` 断言间接佐证。
13. `PowerSettingCapabilityGuard.Refresh` / `EvaluateAsync` 内部是否含可变缓存字段——仅读到签名（`:70` / `:76`）。
14. 游戏模式域对 `ISystemCleanupService` 的 Core 层直接引用——未做完整反向扫描。
15. 两份同名 `DebugLogger`（`Shell/Services/DebugLogger.cs:12` 与 `Shell/Diagnostics/DebugLogger.cs:14`）的取舍与实际使用范围未逐行核对（安全项 UI 日志确认经前者，写 `<LogsDir>\ui-freeze-debug.log`）。
16. `IPowerScenarioService` 链路已核实**类型齐全但无装配**（`PowerScenarioService` `:16` 实现接口、`ScenarioConfirmationDialog.xaml.cs:23-24` 有注入该接口的 ctor，但无 `new ScenarioConfirmationDialog`、无 DI 注册）→ §12 第 13 项由**不确定**升格为**疑似死代码**；`PowerScenarioService` 内部 `PowerPlanEditorService.CreateFailClosedCompatibility()`（`:18`）说明即使被装配也为 fail-closed。
17. WMI 查询字符串与 `srclient.dll` 导出名核对基于源码文本，未在真实 Windows 上验证。
18. §11.3 矩阵中"密集/部分/仅 Shell 基线"等定性档位来自各域 `Debug.WriteLine` / `ILogger` 命中计数（本轮已逐域计数复核），未逐条判断每条日志在失败路径上是否可达。
19. **本轮（九域逐域复核）对 §12 死代码清单的复核结论**：17 项全部重新 grep 确认，其中第 17 项的类型名 `LightSystemInsightService` **全仓 0 命中**，实际类型为 `LiveSystemInsightService`，已就地修正；其余 16 项命中数与清单一致。同轮还修正了三处与实际装配不符的表述：
    - §0.2 / §3.1 原称 `ISystemCleanupService`"Shell 未覆盖"——实为 Shell 在 `App.xaml.cs:275` 再注册同名实现（后注册者生效），已修正并补上 DI 构造函数（`SystemCleanupService.cs:24-26`）取用 `ICleanupAuthorizationGate` / `ICleanupQuarantine` 的事实。
    - §1.3 的 `IChangeConfirmationAuthority`、§11.1 的 `IChangeOrchestrator` / `IChangeConfirmationAuthority` 原把 `Core/Extensions/ServiceCollectionExtensions.cs:61` 当作注册点——`:61` 实为 `BuiltInOptimizationOperationProvider` 工厂内的解析点，注册点是 `:39`，已在文中区分。
    - §13 第 14 项、§12 第 3/6/8/13 项的"无消费者/未接线"结论本轮以全仓 `*.cs` 文本 grep 重新计数核对，结论未变，但性质仍是**文本级否定断言**（见第 2、3 项），不排除反射/泛型激活。

---

## 14. 变更导航速查（按"我想改什么"索引）

| 我想改… | 先看 | 别碰 / 注意 |
|---|---|---|
| 内存清理的可用能力范围 | `Infrastructure.Windows/Extensions/MemoryCleaningServiceCollectionExtensions.cs:20-28`；`Core/Services/Memory/MemoryCleaningContracts.cs:194-207` | `memory.process.terminate` 未注册，注册它需要同时提供 Shell 侧 `CaptureProcessTargetAsync(ProcessTermination, …)` 调用点 |
| 清理的授权/基线策略 | `Shell/Services/Baseline/BaselineCleanupAuthorizationGate.cs:9-30` | 改门会同时影响所有走 `system-cleanup:*` 的项 |
| 清理隔离区位置/格式 | `Core/Services/SystemCleanup/CleanupSafety.cs:145-152`；`CleanupQuarantineManagement.cs:610-613` | 旧路径 `AppPaths.CleanupBackupsDir` 仍被 `BaselineDomainCollector.cs:381` 使用，两套并存 |
| 网络重置的确认文案/流程 | `Shell/Views/NetworkResetDialog.xaml.cs:141-153`；`Core/Services/NetworkReset/NetworkResetPolicy.cs:10-16` | 公开 `PolicyVersion = "network-reset.durable.v2"`，改动会与既有持久 plan 不兼容 |
| 电源可写设置族 | `Shell/Services/PowerPlanEditorService.cs:198-244`（当前全部 fail-closed） | 打开某个设置族必须同时补 `IPowerManagementOwner` 的 typed 路径 + `BaselinePowerGate` 证据哈希 |
| 游戏模式预设允许清单 | `Shell/Services/EsportsPresetPlanner.cs:27-38` | 安全项永不被选中（`:48, 71` 只选 `IsCompatible`） |
| 游戏模式"可用项"清单 | `Shell/ViewModels/GameModeHubViewModel.cs:54-146` | 安全项被硬编码 `IsCompatible=false`（`:118-122`） |
| 硬件信息检测字段 | `Shell/Models/HardwareInfo.cs:218`（`Detect`）+ `Core/Services/GpuInventoryService.cs:9-13` | `HardwareDetectionService.InvalidateCache()` 是唯一级联失效入口（`:47`） |
| 系统还原点创建流程 | `Core/SystemRestore/SystemRestorePointOwner.cs`；`ISystemRestorePointPlatform`（**无 restore 成员**，勿加） | native P/Invoke 只在 Shell 程序集，Core 侧只能走 `ISystemRestorePointWriterProcessRunner` |
| 快照恢复授权 | `Shell/Services/Baseline/BaselineSnapshotRestoreAuthorizationGate.cs:9`；`Core/Services/Snapshot/SnapshotRestoreSafety.cs:134-139` | `DenySnapshotRestoreAuthorizationGate` 是 Core 兜底，缺失 gate 即全拒 |
| 安全项真实写注册表行为 | `Core/Optimization/Providers/BuiltInOptimizationOperationProvider.cs:403-493`（typed 定义） | `Core/Security/SecurityProtectionOwner.cs` 在生产中**未被构造**，改它不会改变运行时行为 |
| 日志落盘范围/过滤条件 | `Core/Diagnostics/DiagnosticLogLoggerProvider.cs:52-55`（category 前缀）+ `DiagnosticsLogger` `:26-29`（级别） | 放宽过滤会显著增加日志量；`DiagnosticLogService` 上限 120 条 / 10 MB |
| 页面级诊断日志 | `Shell/Services/PageDiagnosticService.cs:7-61` | 写 `page_diagnostics.log`，5 MB 自截断；与 `IDiagnosticLogService` 是两条独立链路 |
| "是否管理员"判定 | §11.2：唯一权威 `WindowsElevation`（`WindowsElevation.cs:69` 进程级缓存，`Refresh()` `:115`）+ 消费方多层转发（`SystemGuard.cs:36-47`、`IUserPrivilegeService.cs:9-26`、17 个 Infrastructure 适配器 / 18 个引用点） | 判定已收敛；残留差异仅来自 `SystemCompatibilityChecker` 的永久 `Lazy<SystemInfo>`（`:18, 23`，无刷新入口）与 UI 侧缓存陈旧 |

---

## 15. 与团队任务的衔接（供后续排查引用）

本文档是 `docs/code-map/` 系列的第 3 篇，服务团队任务中的"**以管理员身份运行后仍有功能显示需要管理员且不可用**、**打开/执行失败时完全没有错误信息**"两个缺陷。地图层面已定位到以下结构性关联点（**仅记录，未做修复**）：

1. **管理员判定已收敛，残留的是"缓存陈旧 + 个别读点"**（§11.2）：判定唯一权威是 `Core/Services/WindowsElevation.cs`（`Probe()` 读 `TOKEN_ELEVATION` + `TOKEN_GROUPS`，区分「标准用户 / 管理员账户但令牌被 UAC 筛选 / 已提权管理员」三态，原因码 `elevation.administrator-token-filtered` 等）。`SystemGuard`（`SystemGuard.cs:36-47`）、`PermissionChecker`（`:17, 20, 23`）、`OperationErrorAnalyzer`（`:26, 44, 58`）、`SystemCompatibilityChecker`（`:314, 343`）、`MemoryCleanerService`（`:45`）、`RegistryPriorityService`（`:26`）、`PowerServiceDiagnostics`（`:82`）、Shell `IUserPrivilegeService`（`UserPrivilegeService.cs:90-98`）与 17 个 Infrastructure 适配器（18 个引用点）均**只做一行转发**，不再各自 `IsInRole`。
   - **已不存在的旧机制**：`SystemGuard.AdminCacheExpiry`（5 分钟静态缓存）在全仓 `*.cs` 中 0 命中。
   - **仍需人工确认的残留**：`SystemCompatibilityChecker.CurrentSystem` 的 `Lazy<SystemInfo>`（`:18, 23`）无失效入口，首次取值后永久固定——这是当前唯一"会给出过时提权答案"的缓存层。
2. **失败无错误信息的链路**（§11.3 / §11.4）：五个 Core 域（Power / SystemRestore / Security / SystemCleanup / Memory）不注入 `ILogger`；`DiagnosticLogLoggerProvider` 只放行 `GameOptimizer*` 类别且 ≥ Information；`Debug.WriteLine` 不落盘；`PageDiagnosticService` 只记录 UI 层显式调用。因此这几个域的失败仅以原因码/英文结果对象返回，UI 若不显式展示即"无信息"。
3. **`OperationErrorAnalyzer.DiagnosePermissionIssue`**（`Core/Utilities/OperationErrorAnalyzer.cs:50-86`）——**该路径本轮已修复，不再是"已管理员却提示需要管理员"的来源**：
   - `:63-69` 当 `context.IsAdminMode`（即 `WindowsElevation.IsElevated`）为真时返回 `(false, PermissionChecker.GetSystemDeniedMessage(operationContext, result.UserMessage))`，注释明确写着"已提权仍被拒绝访问时，不得再提示需要管理员权限"。
   - `:74-78` 仅在**未提权**时返回 `(true, PermissionChecker.GetAdminRequiredMessage(...))`。
   - `PermissionChecker.GetAdminRequiredMessage`（`Core/Services/PermissionChecker.cs:41-59`）会区分"账户是管理员但令牌被 UAC 筛选"与"账户不在管理员组"，并附 `WindowsElevation.Current.ReasonCode - Detail`；`GetSystemDeniedMessage`（`:61-88`）供"已提权但系统拒绝"分支使用。
   - 调用点分布在 `Optimization/Operations/*`（如 `AudioOptimizationOperation.cs:368`、`MemoryConfigOperation.cs:62/83/265/295/635/673`、`RegistryToggleOperation.cs:139/171`）——**这些调用点本身未逐个复核**，仍需确认它们确实走 `DiagnosePermissionIssue` 而非自建字符串。
4. **域名级的"能力未注册"缺口**（§12）：`memory.process.terminate`、`ISecurityProtectionOwner`、`SystemMonitorService`（**无接口类型**，`Shell/Services/SystemMonitorService.cs:38`）等缺失注册会让对应 UI 项进入 fail-closed 分支；若该分支不产出本地化失败消息，即表现为"不可用且无原因"。

以上均需在对应领域的独立排查中进一步验证，不属于本文档（文档）交付范围。

---

**文档结束。** 全部结论均标注了仓库相对路径与行号；第 13 节列出未逐行验证的区段，使用前请按标记复核。