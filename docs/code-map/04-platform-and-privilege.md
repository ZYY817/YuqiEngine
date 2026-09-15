# 04 · 平台与权限层地图（YuqiEngine）

> 目标：把「Windows 平台调用封装」与「提权判定 / 能力门禁」两条线全部落到**具体文件 + 具体类型/方法 + 具体调用方**，
> 并显式标出**哪些判定与实际提权状态无关**（恒真/恒假/提前短路/与令牌无关）。
> 所有路径均为仓库相对路径（仓库根 = `YHRJ - 副本`）。
>
> **快照基线**：`HEAD = aa45545`（`Fix Game Modes stack-guard crash from applied-but-incompatible toggles`）。
> 写入时工作树包含**未提交**的提权收敛改动（新增未跟踪文件 `GameOptimizer.Core/Services/WindowsElevation.cs`，
> 并把散落的 `WindowsPrincipal.IsInRole` 判定改为转发）：`git status --porcelain` 共 51 行 = **45 个已跟踪文件为 `M`**
> + 6 个未跟踪项（`.yuqi-team/`、`GameOptimizer.Core/Services/WindowsElevation.cs`、`README_EN.md`、`_dshbuild/`、`docs/`、`installer/`）。
> 本文描述的是**工作树当前内容**；已提交版本（`HEAD`）的提权实现是「每个文件各自 `new WindowsPrincipal(...).IsInRole(...)`」，
> 与本文 §2.5 的对照说明一致。

---

## 0. 一页速查

| 关注点 | 文件 | 关键类型/方法 |
| --- | --- | --- |
| 提权判定的**唯一权威实现** | `GameOptimizer.Core/Services/WindowsElevation.cs` | `WindowsElevation.Current` / `Probe()` / `Refresh()` / `IsElevated` / `IsAdministratorAccount` / `Level` |
| 提权级别模型 | `GameOptimizer.Core/Services/WindowsElevation.cs` | `enum ElevationLevel`、`record ElevationSnapshot` |
| Core 侧门面（静态） | `GameOptimizer.Core/Services/SystemGuard.cs` | `SystemGuard.IsAdministrator` / `IsAdministratorAccount` / `ElevationLevel` / `ElevationDetail` |
| Core 侧门面（文案） | `GameOptimizer.Core/Services/PermissionChecker.cs` | `IsRunningAsAdministrator()` / `GetAdminRequiredMessage()` / `GetSystemDeniedMessage()` |
| Shell 侧 UI 门面 | `GameOptimizer.Shell/Services/UserPrivilegeService.cs` + `IUserPrivilegeService.cs` | `IsAdministrator` / `IsAdministratorAccount` / `ElevationLevel` / `ElevationDetail` / `ElevationReasonCode` / `Refresh()` |
| typed change 能力探测 | `GameOptimizer.Core/Services/CompatibilityChecker.cs` | `CompatibilityChecker.ProbeAsync(CapabilityDescriptor, TargetScope, ChangeIntent, CancellationToken)` |
| 能力门禁契约 | `GameOptimizer.Core/Contracts/Changes/ChangeModels.cs` | `record CapabilityDescriptor` / `record CapabilityAssessment`（`HasRequiredPermission`） |
| 写入路径门禁执行点 | `GameOptimizer.Core/Services/Changes/ChangeOrchestrator.cs` | `PrepareAsync` L88–97 / `ExecuteAsync` L233–251 / `RestoreAsync` L529–547 |
| 执行前置门控 | `GameOptimizer.Shell/Services/Baseline/BaselineChangeExecutionGate.cs` | `EnsureCanExecuteAsync` |
| 开关可用性模型 | `GameOptimizer.Shell/Models/OptimizationToggleModel.cs` | `CanToggle` / `CapabilityState` / `HasCapabilityConstraint` / `SyncOperationContract()` / `RefreshPrivilege()` |
| 开关可用性绑定 | `GameOptimizer.Shell/Views/MasterModuleTemplate.xaml.cs` | L1133 `IsEnabled ← CanToggle`、L1021 `Visibility ← HasCapabilityConstraint` |
| 提权状态注入 UI 的唯一入口 | `GameOptimizer.Shell/ViewModels/DashboardViewModel.cs` | L1148 `IsAdministrator = privilegeService.IsAdministrator`、L3021 `SyncOperationContract(statuses[i], IsAdministrator)` |
| 外壳管理员状态显示 | `GameOptimizer.Shell/MainWindow.xaml.cs` | L277–291 `AdminModeText` / `AdminStatusDot` |
| 清单提权级别 | `GameOptimizer.Shell/app.manifest` | `<requestedExecutionLevel level="highestAvailable" uiAccess="false"/>` |
| 全仓唯一 `runas` 启动 | `GameOptimizer.Shell/Views/RegistryInfoDialog.xaml.cs` | L308 `Verb = "runas"`（启动 `regedit.exe`） |
| Windows 平台封装注册入口 | `GameOptimizer.Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs` | `AddGameOptimizerWindowsInfrastructure(...)` 等（800+ 行，注册 100+ 平台服务） |
| 进程启动封装 | `GameOptimizer.Core/Services/SafeProcessHelper.cs`、`GameOptimizer.Core/Services/ProcessRunner.cs` | `StartProcess()` / `RunProcessAsync()` / `RunPowerShellAsync()` |

---

## 1. 提权判定的唯一权威实现

### 1.1 `WindowsElevation`（`GameOptimizer.Core/Services/WindowsElevation.cs`，359 行）

| 成员 | 行 | 说明 |
| --- | --- | --- |
| `enum ElevationLevel` | 15 | `Unknown=0` / `StandardUser=1` / `AdministratorNotElevated=2` / `ElevatedAdministrator=3` |
| `record ElevationSnapshot` | 33 | `(Level, IsElevated, IsAdministratorAccount, IsWindows, ReasonCode, Detail)`；派生属性 `CanWriteMachineScope => IsElevated` |
| `ElevationSnapshot.Unknown(detail, isWindows)` | 44 | `internal`，探测失败时的构造器 |
| `WindowsElevation.Current` | 76 | **带缓存**的判定入口（`lock (Gate)`） |
| `WindowsElevation.Level` | 103 | `Current.Level` |
| `WindowsElevation.IsElevated` | 105 | **全仓提权真值**（唯一语义：令牌已提权） |
| `WindowsElevation.IsAdministratorAccount` | 108 | 账户属于管理员组（即使令牌被 UAC 筛选） |
| `WindowsElevation.IsAdministratorAccountWithoutElevation` | 111 | `Level == AdministratorNotElevated`；**当前无调用方** |
| `WindowsElevation.Refresh()` | 115 | 丢弃缓存（令牌在进程生命周期内不可变，正常无需调用） |
| `WindowsElevation.Probe()` | 126 | **不读缓存**的直接探测；`UserPrivilegeService.Refresh()` 使用它 |
| `WindowsElevation.DescribeMachineScopeUnavailable()` | 207 | 按 reason code 生成面向用户的不可用原因（XML 注释 L204–206）；**当前无调用方** |
| `WindowsElevation.OverrideProbeForTesting(Func<ElevationSnapshot>)` | 223 | `internal`，测试注入（返回 `IDisposable` 作用域，`Refresh()` 一次） |
| 常量 reason code | 53–57 | `elevation.elevated-administrator` / `elevation.administrator-token-filtered` / `elevation.standard-user` / `elevation.non-windows` / `elevation.probe-failed` |
| `[DllImport("advapi32.dll")] GetTokenInformation` | 351 | 唯一原生依赖；`TokenGroupsInformationClass=2`（L59）、`TokenElevationInformationClass=20`（L60）、`SeGroupUseForDenyOnly=0x10`（L61） |

### 1.2 探测算法（`Probe()`，L126–202）

1. 非 Windows → `Unknown` + `elevation.non-windows`（L128–137）。
2. `WindowsIdentity.GetCurrent()`（L141）→ 用 `identity.AccessToken.DangerousGetHandle()`（L143；
   注释 L142 明确记录「`WindowsIdentity.Token` 返回 `nint`，只有 `AccessToken` 才是 `SafeAccessTokenHandle`」这一已修缺陷）。
3. `TryReadTokenElevation(handle, out tokenElevated)`（L145，实现 L249）读 `TOKEN_ELEVATION`。
4. `TryReadAdministratorsGroup(handle, out groupPresent, out groupDenyOnly)`（L146，实现 L274）读 `TOKEN_GROUPS`，
   在组列表里找 `WellKnownSidType.BuiltinAdministratorsSid`（L66–67），并识别 `SE_GROUP_USE_FOR_DENY_ONLY`（L331）。
5. **两者都读不到**时（L148–161）才退回 `new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)`（L151），
   这是全仓**最后一处**托管 `IsInRole` 判定，且仅作为 native 失败时的降级路径。
6. `elevated = tokenElevationKnown ? tokenElevated : (groupPresent && !groupDenyOnly)`（L163–165）。
7. 结论分支：
   - `elevated == true` → `ElevatedAdministrator`（L168–177）
   - 否则 `administratorAccount`（`elevated || groupPresent`）→ `AdministratorNotElevated`（L179–188）
   - 否则 → `StandardUser`（L190–196）
8. 异常（非 `OperationCanceledException`）→ `ElevationSnapshot.Unknown("读取进程令牌失败：...")`（L198–201）。

> 关键语义：**`IsElevated` 是「令牌已提权」，而不是「账户在管理员组」**。
> UAC 筛选令牌（管理员账户但未提权）下 `IsElevated == false`、`IsAdministratorAccount == true`；
> 这正是修复「以管理员身份运行后仍提示需要管理员」时必须在文案层区分的两种状态
> （见 `PermissionChecker.GetAdminRequiredMessage()` 的 `identity` 分支）。

### 1.3 缓存与刷新语义

| 位置 | 语义 | 风险 |
| --- | --- | --- |
| `WindowsElevation.Current`（L76–101） | 成功结果**永久缓存**；`Unknown` 不缓存（下次重试） | 令牌在进程内不可变 ⇒ 语义正确；`Refresh()`（L115）全仓只有 1 个调用方 `SystemGuard.RefreshPrivilegeStatus()`（`SystemGuard.cs:47`），而后者自身**零调用方** ⇒ 运行期不存在任何缓存失效路径 |
| `UserPrivilegeService.Refresh()`（`UserPrivilegeService.cs:90`） | 调 `WindowsElevation.Probe()`（**绕过缓存**）后写入 5 个属性并广播 `PropertyChanged` | 仅在**构造函数**（L16）被调用一次；UI 侧无任何运行期刷新触发点 |
| `SystemGuard.IsAdministrator`（`SystemGuard.cs:36`） | 纯转发 `WindowsElevation.IsElevated`（**不再有 5 分钟缓存**） | `HEAD` 版本存在 `AdminCacheExpiry = 5min` 缓存（见 §2.5） |
| `RuntimeWindowsCompatibilityEnvironmentProbe._cached`（`CompatibilityChecker.cs:616/625`） | 兼容性环境快照**永久缓存**（含 `WindowsElevation.IsElevated`，L641） | 提权值本身不会变 ⇒ 无实际风险；但**其它环境字段**（GPU/驱动/策略）同样永不失效 |
| `SystemCompatibilityChecker._cachedSystemInfo`（`SystemCompatibilityChecker.cs:18`） | `Lazy<SystemInfo>`，进程内**一次求值**，`IsAdminMode` 在此刻固化（L314） | 被 `PowerOperationDiagnostics` 的 6 处诊断上下文复用（见 §3.3） |

### 1.4 reason code 与用户可见文案

| reason code | 触发条件 | 面向用户的话术来源 |
| --- | --- | --- |
| `elevation.elevated-administrator` | 令牌已提权 | `WindowsElevation.DescribeMachineScopeUnavailable()` L212「进程已提权；若系统仍拒绝写入，原因是目标位置被策略、ACL 或安全软件保护，而不是缺少管理员权限。」 |
| `elevation.administrator-token-filtered` | 管理员组 + deny-only | L213「当前账户属于管理员组，但进程令牌未提权（UAC 筛选令牌）。请退出后用「以管理员身份运行」重新启动宇奇引擎。」 |
| `elevation.standard-user` | 不在管理员组 | L214「当前账户不在管理员组内…请使用管理员账户登录后重新启动。」 |
| `elevation.non-windows` | 非 Windows | L215 |
| `elevation.probe-failed` | 令牌读取异常 | L216「无法读取进程令牌以确认提权状态；已按最小权限处理。」 |

> ⚠ **这 5 条话术几乎不可达**：`DescribeMachineScopeUnavailable()` 全仓**只有一个调用方**
> ——`WindowsNetworkResetPlatform.cs:33`（把文本塞进 `NetworkResetPlatformAssessment` 的说明字段），
> 其余判定点都不走这个 switch；`PermissionChecker.GetAdminRequiredMessage()` 只读
> `ReasonCode`/`Detail` 两个**字段**（`PermissionChecker.cs:54`，输出成一行 "提权判定 / Elevation: …"），
> 不产生 `elevation.*` 的**分场景话术**。因此「管理员账户但令牌被 UAC 筛选」与「不在管理员组」
> 这两种状态在绝大多数界面上仍共用同一段「右键以管理员身份运行」文案（只有 `GetAdminRequiredMessage()` L43–45
> 的 `identity` 一行做了区分）。

`PermissionChecker.GetAdminRequiredMessage(operationName)`（`PermissionChecker.cs:41`）会把
`WindowsElevation.Current.ReasonCode` 与 `Detail` 直接拼进用户提示（L54），因此
**只要走这个函数，用户提示里就带有真实提权判定依据**。`GetSystemDeniedMessage(...)`（L61）用于
「已提权但系统拒绝」的分支（L63–67 明确写「管理员权限不是本次失败的原因，重复提权不会解决问题」）。

---

## 2. 全部提权判定实现点与调用方

### 2.1 汇总：当前工作树的提权判定入口（逐条本轮复核）

> 复核方式：`Select-String 'WindowsElevation\.'` 全仓（排除 `obj`/`bin`）共命中 **43 处引用 / 30 个文件**，
> 其中 2 处是 `WindowsElevation.cs` 自身（L1 文件头注释、L45 `ReasonProbeFailed`），
> 其余 41 处即下表所有转发点。下表每行的行号均为本轮实测。

| # | 文件 | 类型/成员 | 角色 | 调用方（文件:行） |
| --- | --- | --- | --- | --- |
| 1 | `GameOptimizer.Core/Services/WindowsElevation.cs` | `WindowsElevation.IsElevated`（经 `Current`） | **权威实现** | 全仓 **43 处引用 / 30 个文件**（§2.2 为完整清单） |
| 2 | `GameOptimizer.Core/Services/SystemGuard.cs:36` | `SystemGuard.IsAdministrator` | 转发 | `OptimizationEngine.cs:633`、`DiagnosticPackageService.cs:155`、`PowerSettingIndexOperation.cs:81,141`；另有 `SystemGuard.CheckPrerequisites` **L160**（同一文件内，但该 API 零调用方，见 §2.4） |
| 3 | `GameOptimizer.Core/Services/SystemGuard.cs:47` | `SystemGuard.RefreshPrivilegeStatus()` | 转发 `WindowsElevation.Refresh()` | **无调用方**（因此 `WindowsElevation.Refresh()` 也运行期不可达） |
| 4 | `GameOptimizer.Core/Services/PermissionChecker.cs:17` | `PermissionChecker.IsRunningAsAdministrator()` | 转发 | `StartupDiagnosticService.cs:35`、`OperationResultItem.cs:35`、`FirstRunBaselineOrchestrator.cs:426`、`SystemCleanupViewModel.cs:355`、`BackupManagerViewModel.cs:1922`、`RepairCenterViewModel.cs:698,948,1080` |
| 5 | `GameOptimizer.Core/Services/PermissionChecker.cs:28` | `CheckOperationPermission(string)` | 转发 + 文案 | **无调用方** |
| 6 | `GameOptimizer.Core/Services/PermissionChecker.cs:41/61` | `GetAdminRequiredMessage` / `GetSystemDeniedMessage` | 文案 | `OperationErrorAnalyzer.cs:66`（已提权分支）/ `:76`（未提权分支） |
| 7 | `GameOptimizer.Core/Services/PermissionChecker.cs:77` | `CanRestartAsAdmin()` | **伪判定**（只看 `MainModule != null`，L82） | **无调用方** |
| 8 | `GameOptimizer.Shell/Services/UserPrivilegeService.cs:90` | `UserPrivilegeService.Refresh()`（L92 用 `WindowsElevation.Probe()`，绕过缓存） | UI 门面 | 构造函数 L16 调一次；**运行期无调用方**（`IUserPrivilegeService.Refresh()` 全仓无 call site） |
| 9 | `GameOptimizer.Core/Services/CompatibilityChecker.cs:641` | `RuntimeWindowsCompatibilityEnvironmentProbe.CaptureAsync`（L623；缓存 L616/L625/L629） | 快照消费 | `CompatibilityChecker`（经 `IWindowsCompatibilityEnvironmentProbe`，`CompatibilityChecker.cs:200,259`）→ 能力探测 |
| 10 | `GameOptimizer.Core/Services/SystemCompatibilityChecker.cs:314/343` | `GetCurrentSystemInfo` → `SystemInfo.IsAdminMode`；L343 私有 `IsAdministrator()` | 快照消费（L343 已无调用方） | `PowerOperationDiagnostics.cs:61,122,171,211,271,315`（6 处全部实测） |
| 11 | `GameOptimizer.Core/Services/PowerServiceDiagnostics.cs:82` | `PowerServiceDiagnostics.IsAdministrator()` | 转发 | 同文件 **L30**（`DiagnoseAsync` 取快照）→ **L48**（`TryAutoRepairAsync` 拒绝分流） |
| 12 | `GameOptimizer.Core/Services/ProcessPriority/RegistryPriorityService.cs:26` | `RegistryPriorityService.HasAdminPrivileges()`（接口 `IRegistryPriorityService.cs:45`） | 转发 | **当前无任何可达调用方**：唯二两处引用 L39、L108 **都在 `#if false` 块内**（L33–95 = `SetProcessPriority`，L102–167 = `RemoveProcessPriority`），两个方法在 `#if` 之前 L32/L101 已直接返回 `Failed("IFEO persistent priority is blocked…")` |
| 13 | `GameOptimizer.Core/Services/Memory/MemoryCleanerService.cs:45` | `MemoryCleanerService.IsAdministrator`（接口声明在同文件 **L13**，`IMemoryCleanerService` 与实现同文件；**不存在独立的 `IMemoryCleanerService.cs`**） | 转发 | **无消费者**：`Select-String '\.IsAdministrator'` 在 `Memory*` 下 0 命中，三个 `IMemoryCleanerService` 注入点（`MemoryModuleExtension.cs:19,24`、`MemoryCleanDialog.xaml.cs:129,140`、`MemoryControlPanel.xaml.cs:23,43`）都不读该属性 |
| 14 | `GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs:157` | 静态 `CheckIsAdmin()`（**不是** `CheckAdminPrivilege`）→ `[ObservableProperty] private bool isAdmin`（字段 L57） | 转发 | 构造/刷新 **L121** 赋值；`IsAdmin` **无任何读取点**（XAML 无绑定，`Select-String 'IsAdmin'` 仅命中 L57/121/157） |
| 15 | `GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.cs:116` | `IsAdmin = WindowsElevation.IsElevated`（**无 `CheckAdminPrivilege()` 方法**） | 转发 | 同文件 **L169** `if (!IsAdmin)` → L172–173 `ToastService.Warning(_loc.Get("CoreAccel.PermissionHint"), …)`；`IsAdmin` 未绑定任何控件 |
| 16 | `GameOptimizer.Shell/Services/SystemRestorePointWriterEntryPoint.cs:187` | `WindowsSystemRestoreNativeApi.IsAdministrator`（接口成员 L178） | 转发 | 同文件 **L59** → `WriteFailure("Administrator permission is required by Windows System Restore.")` |
| 17 | `GameOptimizer.Infrastructure.Windows/SystemRestore/WindowsSystemRestorePointPlatform.cs:238` | 私有 `IsAdministrator()` | 转发 | 同文件 **L44、L72**（写入 `SystemRestorePointProbe.IsAdministrator`） |
| 18 | `GameOptimizer.Infrastructure.Windows/Services/WindowsServiceAccessor.cs:314` | 私有 `IsAdministrator()` | 转发 | 同文件 **L31** → `ServicePlatformAssessment(..., administrator ? "applicable" : "permission.administrator-required", ...)`（L38） |
| 19 | `GameOptimizer.Infrastructure.Windows/Services/WindowsScheduledTaskAccessor.cs:142` | 私有 `IsAdministrator()` | 转发 | 同文件 **L22** → `ScheduledTaskPlatformAssessment`（L23） |
| 20 | `GameOptimizer.Infrastructure.Windows/Services/WindowsMmAgentAccessor.cs:74` | 私有 `IsAdministrator()` | 转发 | 同文件 **L17** → `MmAgentPlatformAssessment`（L18） |
| 21 | `GameOptimizer.Infrastructure.Windows/Services/WindowsDeviceStateAccessor.cs:103` | 私有 `IsAdministrator()` | 转发 | 同文件 **L20** → `DevicePlatformAssessment`（L21） |
| 22 | `GameOptimizer.Infrastructure.Windows/Services/WindowsBcdEditAccessor.cs:147` | 私有 `IsAdministrator()` | 转发 | 同文件 **L19** → `BcdPlatformAssessment`（L22） |
| 23 | `GameOptimizer.Infrastructure.Windows/Services/WindowsHostsBackupOwner.cs:163` | 私有 `IsAdministrator()` | 转发 | 同文件 **L22** → `HostsPlatformAssessment`（L23） |
| 24 | `GameOptimizer.Infrastructure.Windows/Services/WindowsAppxRemovalOwner.cs:323` | `IsCurrentUserAdministrator()` | 转发 | 同文件 **L36** `if (!IsCurrentUserAdministrator())`（早退）；SID 另由 `AppxRemovalEnvironment.GetCurrentUserSid()` 提供（`Core/Services/Appx/AppxRemovalContracts.cs:118–128`） |
| 25 | `GameOptimizer.Infrastructure.Windows/Registry/RegistryValueSetCapability.cs:364` | 私有 `IsAdministrator()` | 转发 | 同文件 **L85** → 环境记录 L88，reason `applicable`/`permission.administrator-required` L89 |
| 26 | `GameOptimizer.Infrastructure.Windows/Registry/RegistryStringCapability.cs:623` | 私有 `IsAdministrator()` | 转发 | 同文件 **L277** → 环境记录 L280，reason L281 |
| 27 | `GameOptimizer.Infrastructure.Windows/Registry/RegistryDwordCapability.cs:83` **和** `:324` | 私有 `IsAdministrator()` ×2（同文件两份拷贝） | 转发 | 同文件 **L64**（环境记录构造）、**L143**（`environment.IsAdministrator` 读取）；reason 分流 L147–148 |
| 28 | `GameOptimizer.Infrastructure.Windows/Registry/PerInterfaceRegistryCapability.cs:401` | 私有 `IsAdministrator()` | 转发 | 同文件 **L74** → 环境记录 L77，reason L78 |
| 29 | `GameOptimizer.Infrastructure.Windows/NetworkReset/WindowsNetworkResetPlatform.cs:26` | 内联 `WindowsElevation.IsElevated`（`ProbeAsync` L14） | 转发 | 同文件 **L30** reason `permission.administrator-required`/`platform.ready`；**L33** 是 `DescribeMachineScopeUnavailable()` 的**唯一**调用方 |
| 30 | `GameOptimizer.Infrastructure.Windows/NetworkDiagnostics/WindowsNetworkAdapterDhcpPlatform.cs:31` | 内联 `WindowsElevation.IsElevated` | 转发 | 同文件 **L36** → reason `network.adapter-dhcp.read-ready-non-admin` / `read-ready` |
| 31 | `GameOptimizer.Infrastructure.Windows/PowerManagement/WindowsPowerPlatform.cs:30` | 内联 `WindowsElevation.IsElevated` | 转发 | 构造 `PowerEnvironment(..., IsAdministrator, ...)`（`Core/PowerManagement/PowerManagementModels.cs`）→ `PowerPlanEditorPage.xaml.cs:118` |
| 32 | `GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSettingChangeCapability.cs:69` | 私有 `IsAdministrator()` | 转发 | 环境记录 `PowerSettingEnvironment(bool IsAdministrator)`（字段 L54，构造 L67）→ 消费 **L305**、**L460** |
| 33 | `GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSchemeChangeCapability.cs:67` | 私有 `IsAdministrator()` | 转发 | 环境记录 `PowerSchemeEnvironment(..., bool IsAdministrator)`（L27/L52，构造 L65）→ 消费 **L74/L82/L323/L496**；`IPowerPlatformFactsProvider` → `PowerPlatformFacts.IsAdministrator` → `ManagedPowerPlanService.CanMutate`（`ManagedPowerPlanService.cs:548–556`） |

### 2.2 权威实现（`WindowsElevation.IsElevated`）的直接调用点（**43 处引用 / 30 个文件**）

```
GameOptimizer.Core/Services/CompatibilityChecker.cs:641
GameOptimizer.Core/Services/Memory/MemoryCleanerService.cs:45
GameOptimizer.Core/Services/PermissionChecker.cs:17,20,23,54
GameOptimizer.Core/Services/PowerServiceDiagnostics.cs:82
GameOptimizer.Core/Services/ProcessPriority/RegistryPriorityService.cs:26
GameOptimizer.Core/Services/SystemCompatibilityChecker.cs:314,343
GameOptimizer.Core/Services/SystemGuard.cs:36,39,42,45,47
GameOptimizer.Core/Services/WindowsElevation.cs:1,45                  （自身；L45 为 ReasonProbeFailed）
GameOptimizer.Core/Utilities/OperationErrorAnalyzer.cs:26,44,58
GameOptimizer.Infrastructure.Windows/NetworkDiagnostics/WindowsNetworkAdapterDhcpPlatform.cs:31
GameOptimizer.Infrastructure.Windows/NetworkReset/WindowsNetworkResetPlatform.cs:26,33
GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSchemeChangeCapability.cs:67
GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSettingChangeCapability.cs:69
GameOptimizer.Infrastructure.Windows/PowerManagement/WindowsPowerPlatform.cs:30
GameOptimizer.Infrastructure.Windows/Registry/PerInterfaceRegistryCapability.cs:401
GameOptimizer.Infrastructure.Windows/Registry/RegistryDwordCapability.cs:83,324
GameOptimizer.Infrastructure.Windows/Registry/RegistryStringCapability.cs:623
GameOptimizer.Infrastructure.Windows/Registry/RegistryValueSetCapability.cs:364
GameOptimizer.Infrastructure.Windows/Services/WindowsAppxRemovalOwner.cs:323
GameOptimizer.Infrastructure.Windows/Services/WindowsBcdEditAccessor.cs:147
GameOptimizer.Infrastructure.Windows/Services/WindowsDeviceStateAccessor.cs:103
GameOptimizer.Infrastructure.Windows/Services/WindowsHostsBackupOwner.cs:163
GameOptimizer.Infrastructure.Windows/Services/WindowsMmAgentAccessor.cs:74
GameOptimizer.Infrastructure.Windows/Services/WindowsScheduledTaskAccessor.cs:142
GameOptimizer.Infrastructure.Windows/Services/WindowsServiceAccessor.cs:314
GameOptimizer.Infrastructure.Windows/SystemRestore/WindowsSystemRestorePointPlatform.cs:238
GameOptimizer.Shell/Services/SystemRestorePointWriterEntryPoint.cs:187
GameOptimizer.Shell/Services/UserPrivilegeService.cs:92                （Probe()，非缓存）
GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.cs:116
GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs:157
```

> 计数口径：`WindowsElevation.` 字面命中（含 `WindowsElevation.Current.ReasonCode` 这类字段读取，
> 即 `PermissionChecker.cs:54`）。若只算「返回布尔提权值的调用点」，则需扣除 `PermissionChecker.cs:54`
> 与 `WindowsElevation.cs:1,45`，为 **40 处 / 29 个文件**。

### 2.3 无调用方的判定 API（已接线但没人读，或彻底闲置）

| 成员 | 位置 | 状态 |
| --- | --- | --- |
| `PermissionChecker.CanRestartAsAdmin()` | `PermissionChecker.cs:77–88` | **零调用方**；实现只检查 `Process.GetCurrentProcess().MainModule != null`，**不检测提权、也不重启任何进程**。任何「用它可以判断能否自提权」的假设都不成立 |
| `PermissionChecker.CheckOperationPermission(string)` | `PermissionChecker.cs:28` | 零调用方 |
| `WindowsElevation.IsAdministratorAccountWithoutElevation` | `WindowsElevation.cs:111` | 零调用方（专为「管理员账户但未提权」文案准备） |
| `WindowsElevation.DescribeMachineScopeUnavailable()` | `WindowsElevation.cs:207` | **有 1 个调用方**：`WindowsNetworkResetPlatform.cs:33`（唯一）——不要按「死 API」处理；但其余判定点都不用它，所以它生成的分场景话术只出现在网络重置一处 |
| `WindowsElevation.Refresh()` / `SystemGuard.RefreshPrivilegeStatus()` | `WindowsElevation.cs:115` / `SystemGuard.cs:47` | **链式零调用方**：`RefreshPrivilegeStatus()` 无任何 call site，`Refresh()` 的唯一调用方就是它 ⇒ 两者在运行期都不可达（`OverrideProbeForTesting` 内部会调 `Refresh()`，但该 internal 方法当前也无测试调用，见 §9.5） |
| `SystemGuard.IsAdministratorAccount` / `SystemGuard.ElevationLevel` / `SystemGuard.ElevationDetail` | `SystemGuard.cs:39,42,45` | 三个转发属性**全仓无消费者**（`SystemGuard.` 前缀 0 命中） |
| `SystemCompatibilityChecker.IsAdministrator()` | `SystemCompatibilityChecker.cs:343` | 零调用方（L314 已改用 `WindowsElevation.IsElevated`） |
| `MemoryCleanerService.IsAdministrator`（接口 `IMemoryCleanerService` 声明在**同一文件** `MemoryCleanerService.cs:7`，属性在 **L13**） | `MemoryCleanerService.cs:45` | 接口面暴露，**无消费者**（仓库中**不存在** `IMemoryCleanerService.cs` 这个文件） |
| `RegistryPriorityService.HasAdminPrivileges()` | `RegistryPriorityService.cs:26`（接口 `IRegistryPriorityService.cs:45`） | **两个引用点 L39 / L108 都在 `#if false` 块内** ⇒ 该方法当前无任何可达调用方（详见 §2.1 #12） |
| `RepairCenterViewModel.IsAdmin`（`[ObservableProperty] private bool isAdmin`，字段 L57） | `RepairCenterViewModel.cs:121` 赋值 | **只写不读**，XAML 无绑定 |
| `UserPrivilegeService.ElevationLevel / ElevationDetail / ElevationReasonCode / IsAdministratorAccount` | `UserPrivilegeService.cs:38–88`、`IUserPrivilegeService.cs:12–26` | 全仓**无消费者**（UI 仍只读 `IsAdministrator`，`IUserPrivilegeService.cs:12`） |

### 2.4 「看起来像提权判定、实际不是」的成员（易误判）

| 成员 | 位置 | 真实语义 |
| --- | --- | --- |
| `WindowsIdentity.GetCurrent().User?.Value` | `RegistryValueSetCapability.cs:368`、`RegistryStringCapability.cs:614`、`RegistryDwordCapability.cs:74,315`、`RegistryValueDeleteCapability.cs:224`、`WindowsProcessTuningPlatform.cs:384`、`WindowsMemoryCleaningPlatform.cs:422`、`Core/Services/Appx/AppxRemovalContracts.cs:122`、`Core/Services/Snapshot/SnapshotService.cs:325,470`、`Core/Services/Snapshot/ComprehensiveBackupService.cs:704`、`Shell/Services/Baseline/BaselineEnvironmentBinding.cs:110`、`Core/Services/Repair/OrchestratedRepairChangeExecutor.cs:822`、`Core/Optimization/Operations/AccessibilityChangeCapability.cs:379`、`Core/Optimization/Operations/OrchestratedRegistryDwordOperation.cs:224` | **拿当前用户 SID 做作用域/指纹**，与提权无关（标准用户与管理员账户的 SID 相同）。⚠ 注意 `WindowsProcessTuningPlatform.cs:383` 与 `WindowsMemoryCleaningPlatform.cs:421` 用的是 `new WindowsIdentity(token)`（**被检查进程**的令牌，不是当前进程） |
| `SystemGuard.HasSufficientDiskSpace` / `GetAvailableDiskSpace` / `FormatBytes` | `SystemGuard.cs:120,137,143`（`CheckPrerequisites` L174/L176 调用） | 磁盘空间预检 |
| `ConfigurationExportService.cs:94`、`PowerConfigService.cs:27` | Shell 服务 | 导出/配置前的磁盘空间检查（`SystemGuard.HasSufficientDiskSpace`） |
| `SystemGuard.CheckPrerequisites(..., requiresAdmin: ...)` | `SystemGuard.cs:156`（提权分支 **L160–163**） | 提权判定存在，但**该 API 零调用方**——没有任何 operation 走这条预检。⚠ 该成员在 `SystemCompatibilityChecker.cs` 中 **0 命中**，引用时勿指向该文件 |
| `SystemRestoreService.SystemRestoreStatus.IsAdministrator` | `SystemRestoreService.cs:24`（DTO），L182 由 `result.Probe?.IsAdministrator` 赋值 | **数据搬运**：值来自 `WindowsSystemRestorePointPlatform` 的探测结果 |
| `GraphicsEnvironmentSnapshot.IsAdministrator` | `GraphicsSafety/GraphicsSafetyModels.cs:55` | 环境快照字段；两处构造点**硬编码全 false**（见 §6.3） |
| `SystemInfo.IsAdminMode` | `SystemCompatibilityChecker.cs:314`（`Lazy` L18 一次求值） | 诊断快照字段，进程生命周期内冻结 |
| `DiagnosticContext.IsAdminMode` | `Diagnostics/ErrorModels.cs:75` | 默认 `false`；由 `OperationErrorAnalyzer`（3 处正确赋值：`:26,44,58`）、`PowerSettingIndexOperation.cs:141`、`PowerOperationDiagnostics.cs` **6 处**（`:61,122,171,211,271,315`）设置 |
| `EnhancedOperationResult.IsAdminMode` | `Diagnostics/EnhancedOperationResult.cs:235` | 结果对象字段 |

### 2.5 与已提交版本（`HEAD`）的差异

`HEAD` 状态下**没有** `WindowsElevation.cs`（`git cat-file -e HEAD:GameOptimizer.Core/Services/WindowsElevation.cs` → 不存在）：
每个文件各自 `new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)`，
按 `git grep -c IsInRole HEAD -- '*.cs'` 实测为 **28 处 / 27 个文件**（`RegistryDwordCapability` 有两份：`:86` 与 `:331`），
另有 `SystemGuard` 的 5 分钟缓存版本（`HEAD:GameOptimizer.Core/Services/SystemGuard.cs`：`_isAdminCached` L32、
`_lastAdminCheck` L33、`AdminCacheExpiry = TimeSpan.FromMinutes(5)` L34、判定入口 L43–49、`IsInRole` L68）。
当前工作树把这些全部改为转发到 `WindowsElevation`，并且：
- `SystemGuard.cs` / `PermissionChecker.cs` 退化为门面；
- `OperationErrorAnalyzer.cs` 新增「已提权仍被拒绝」的归因分支（`OperationErrorAnalyzer.cs:63–78`）；
- `IUserPrivilegeService` 扩容（`IUserPrivilegeService.cs:15,18,21,24,26` 新增 `IsAdministratorAccount` / `ElevationLevel` / `ElevationDetail` / `ElevationReasonCode` / `Refresh()`）。

> 结论：**提权判定已经收敛为单一实现**（`WindowsElevation`），但**消费侧的语义仍未收敛**：
> §2.2 的 43 个引用点各自决定「如何解读」这个布尔值，且 §2.3 列出的新 API 多数尚无消费者。
> 因此「同一台机器、同一个进程，不同页面给出不同权限结论」的**判定根因已消除**，
> 但**门禁与文案仍然由各自的门禁链独立决定**（§5、§6）。

---

## 3. 提权状态的消费方（谁在用这个结论）

### 3.1 能力探测层（typed change 的 `CapabilityAssessment`）

| 消费点 | 文件:行 | 用法 |
| --- | --- | --- |
| 兼容性环境快照 | `CompatibilityChecker.cs:641` | `WindowsElevation.IsElevated` 写入 `WindowsCompatibilityEnvironment.IsAdministrator` |
| 权限门禁判定 | `CompatibilityChecker.cs:362–366` | `if (requiresAdmin && !environment.IsAdministrator)` → `Blocked(..., CompatibilityLevel.RequiresAdmin, "permission.administrator-required", loc.Get("Compatibility.AdminRequired"))` |
| 评估结果映射 | `CompatibilityChecker.cs:147` | `var hasPermission = result.Level != CompatibilityLevel.RequiresAdmin;`（**只有** `RequiresAdmin` 会被判为无权限） |
| 评估结果构造 | `CompatibilityChecker.cs:151` | `new CapabilityAssessment(applicability, hasPermission, result.ReasonCode, details)` |
| operation 级映射 | `CompatibilityChecker.cs:181` | `CompatibilityLevel.RequiresAdmin => OperationSupportState.PermissionRequired` |
| 各平台能力探测 | 见 §2.1 #17–#33 | 每个平台 accessor/capability 把自己的 `IsAdministrator` 写进 *PlatformAssessment / *Environment 记录 |
| 业务 Owner 判定 | `Core/PowerManagement/PowerManagementOwner.cs:283`（"Administrator permission is required for power writes."，另 L323 用于恢复门禁）、`Core/SystemRestore/SystemRestorePointOwner.cs:710`、`Core/SystemRestore/SystemRestorePointModels.cs:112`（记录字段 L205）、`Core/Security/SecurityProtectionOwner.cs:58` | 从环境记录读 `IsAdministrator` 后决定 PermissionDenied |
| 网络重置 | `Core/Services/NetworkReset/NetworkResetOwner.cs`（**6 处**：L50、L138、L162、L175、L185、L564） | 平台评估的提权位决定命令是否可执行 |
| 命令结果归因 | `Core/Optimization/Operations/OrchestratedRegistryDwordOperation.cs:88` | `reasonCode.StartsWith("permission.")` → `OperationSupportState.PermissionRequired` |
| 启动项 | `Core/Services/Startup/StartupTaskOwners.cs:71`（接口声明）、`:153`（消费）+ `Infrastructure.Windows/Startup/WindowsManagedAutostartInfrastructure.cs:76` | `IManagedAutostartPlatform.HasRequiredPermissionAsync` |

### 3.2 业务 / UI 门禁层

| 消费点 | 文件:行 | 用法 |
| --- | --- | --- |
| 主模块开关 | `OptimizationToggleModel.SyncOperationContract(status, isAdministrator)` L544 / `RefreshPrivilege` L576 / `EvaluateCapabilities` L636 / `ResolveCapabilityState` L688 | 见 §5.1 |
| 开关数据源 | `DashboardViewModel.cs:1148,1179–1184,2619,3021` | 见 §5.1 |
| 电源受管方案 | `ManagedPowerPlanService.cs:548–556`（`CanMutate`）+ `PowerPlanEditorPage.xaml.cs:146–167` | 见 §5.2 |
| 电竞模式 | `EsportsModePage.xaml.cs:116` → `EsportsModeStateService.cs:101,129` | 见 §5.3 |
| 预装应用清理 | `AppxCleanupViewModel.cs:149–155` | 见 §5.4 |
| 系统还原点 | `SystemRestoreService.cs:182` → `RestorePointViewModel.cs:152,300–302` | 见 §5.5 |
| 系统清理 | `SystemCleanupViewModel.cs:355`（`[RelayCommand] CleanAsync` 内） | 非管理员 → `StatusText = Cleanup.AdminRequired`（`:357`）+ `InfoDialog`（`:361–362` `Common.AdminRequired` / `Cleanup.AdminRequiredMessage`），直接 return |
| 备份深度恢复 | `BackupManagerViewModel.cs:1922`（`RestoreRegistryBackupAsync`） | 同上模式（`:1924` `Backup.AdminRequired` + `:1926–1927` 弹窗键） |
| 修复中心 | `RepairCenterViewModel.cs:698`（`RepairItemInternalAsync`）、`:948`（`RunResetPowerDefaultsAsync`）、`:1080`（`RunFullRestoreAsync`） | 非管理员 → `StatusMessage = Repair.AdminRequired`（`:700,951,1083`）+ `InfoDialog`（`:704–705,955–956,1087–1088`，键 `Common.AdminRequired` / `Repair.AdminRequiredMessage`）+ return |
| 首次基线 | `FirstRunBaselineOrchestrator.cs:426`（`RunPreflightAsync`） | 非管理员 → 流程失败，`ErrorMessage = "当前未使用管理员权限启动，无法执行首次安全基线备份。"`（`:428`） |
| 核心加速 | `CpuCoreManagementViewModel.cs:116`（赋值）→ `:169` 判定 → `:172–173` `ToastService.Warning(CoreAccel.PermissionHint, CoreAccel.PermissionHintDesc)` | 非管理员 → **仅 Toast 警告**，不改变任何控件的 `IsEnabled`（`IsAdmin` 字段 L37，无 XAML 绑定） |
| 进程优先级页 | `ProcessPriorityPage.xaml.cs:41`（`OnLoaded` 调 `CheckAdminPrivileges()`）、`:104–107`（方法体） | 该方法**只弹 IFEO 阻断 Toast（L106 `ToastService.Warning(IfeoBlockedMessage)`），不做任何提权检查**（详见 §6.6） |
| 外壳状态 | `MainWindow.xaml.cs:277–291`（`AdminModeText` / `AdminStatusDot`）、`OverviewPage.xaml:119`（`PrivilegeHint`） | 见 §5.7 |
| 启动诊断 | `StartupDiagnosticService.cs:35–46` | 非管理员 → 追加 `Diagnostic.NoAdminTitle/Desc/Action`（`:41–43`）警告项。⚠ **这三个键在四个本地化目录中全部缺失**，因此实际显示的是键名本身（见 附录 A） |

### 3.3 诊断与报告层

| 消费点 | 文件:行 | 用法 |
| --- | --- | --- |
| 异常归因（核心） | `OperationErrorAnalyzer.cs:26,44,58,63–78` | 提权位决定 `IsAdminMode`；已提权时走 `GetSystemDeniedMessage`，未提权且 `ErrorCategory.Permission` 时走 `GetAdminRequiredMessage` |
| 权限方案生成 | `SolutionProvider.cs:39–48` | `if (!context.IsAdminMode)` → 第一条方案固定为 `Solution.RunAsAdmin`（`RequiresAdmin = true`） |
| 权限异常话术 | `ErrorDiagnosticEngine.cs:47,51,58–64,158,182` | `isAdmin` 决定严重级别（High/Critical）、用户消息键、可能原因集合 |
| 诊断包 | `DiagnosticPackageService.cs:155` | `["IsAdministrator"] = SystemGuard.IsAdministrator` |
| 报告导出 | `ErrorReportGenerator.cs:80` | `e.Context.IsAdminMode` |
| 电源诊断 | `PowerOperationDiagnostics.cs:61,122,171,211,271,315` | 用 `SystemCompatibilityChecker.CurrentSystem.IsAdminMode`（**进程内冻结值**） |
| 电源设置诊断 | `PowerSettingIndexOperation.cs:141`（自建 `DiagnosticContext { IsAdminMode = SystemGuard.IsAdministrator }`；同文件 `:81` 另有 `if (!SystemGuard.IsAdministrator)` 早退） |
| 备份诊断 | `BackupDiagnosticService.cs:40`（`else if (exception is UnauthorizedAccessException)`）→ `:43` `Backup.Error.NoPermission`、`:51` `Backup.Solution.RunAsAdmin`（`RequiresAdmin = true` L52）、`:55–61` ACL 步骤 | **无条件**给出 `Backup.Solution.RunAsAdmin`（不看提权位，见 §6.5） |
| 能力文案 | `SmartErrorDialog.xaml.cs:219`、`OperationTransparencyDialog.xaml.cs:341,521–523`、`OperationDetailCardDialog.cs:121` | 读 `SolutionStep.RequiresAdmin` / `toggle.RequiresAdmin`（**operation 声明位，不是进程提权位**） |
| 结果消息格式化 | `OptimizationResultMessageFormatter.cs:22–27`（由 `OperationResultItem.cs:31–35` 调用） | 命中 `administrator/管理员权限/access denied` 关键词时，用 `isAdministrator` 区分「已提权但被 Windows 拒绝」与「需要管理员」 |

### 3.4 数据 / 契约层（记录提权位但自己做判定）

`PowerSchemeEnvironment` / `PowerSettingEnvironment`（`PowerSchemeChangeCapability.cs:52`、`PowerSettingChangeCapability.cs:50`）、
`PowerEnvironment`（`PowerManagementModels.cs:83`）、`PowerPlatformFacts`（`PowerManagementModels.cs:86`）、
`ServicePlatformAssessment`（`WindowsServiceAccessor.cs:32`）、`ScheduledTaskPlatformAssessment`、`MmAgentPlatformAssessment`、
`DeviceStatePlatformAssessment`、`BcdEditPlatformAssessment`、`HostsBackupPlatformAssessment`、`AppxRemovalEnvironment`、
`NetworkAdapterDhcpPlatformProbe`（`NetworkAdapterDhcpModels.cs:48`）、`NetworkResetPlatformAssessment`（`NetworkResetModels.cs:78,98`）、
`SystemRestorePointProbe`（`SystemRestorePointModels.cs:112,205`）、`GraphicsEnvironmentSnapshot`、
`MemoryCleaningEnvironment`、`SecurityEnvironment`、`RepairEnvironment`。

---

## 4. typed change 能力门禁判定路径（`CapabilityAssessment`）

```
[写入请求]
  │
  ├─(Prepare)  ChangeOrchestrator.PrepareAsync()                       Core/Services/Changes/ChangeOrchestrator.cs:88
  │     └─ ProbeAsync(capability, scope, ChangeIntent.Prepare, ct)     :1165
  │          ├─ _compatibilityPolicy != null ?                         :1171   （DI 注入的 CompatibilityChecker）
  │          │     └─ CompatibilityChecker.ProbeAsync(descriptor, …)   CompatibilityChecker.cs:126
  │          │          ├─ CheckAsync(capabilityId, descriptor.RequiresAdministrator, descriptor.RequiresRestart, ct)  :133
  │          │          │     └─ environment.IsAdministrator ← WindowsElevation.IsElevated          :641
  │          │          │           └─ requiresAdmin && !IsAdministrator → Blocked(
  │          │          │                CompatibilityLevel.RequiresAdmin, "permission.administrator-required",
  │          │          │                loc.Get("Compatibility.AdminRequired"))                     :362–366
  │          │          ├─ CanExecute ? → return null（= 无阻断评估）                              :139–142
  │          │          └─ return new CapabilityAssessment(
  │          │                 applicability, hasPermission = Level != RequiresAdmin,
  │          │                 result.ReasonCode, details)                                          :144–151
  │          └─ 否则 capability.ProbeAsync(scope, intent, ct)                                       :1184  （各平台自评）
  │
  ├─ 门禁：applicability != Applicable → Blocked(reasonCode, details)                              :89–92
  ├─ 门禁：!HasRequiredPermission   → Blocked("permission.denied", details)                        :94–97
  │
  ├─(Execute) ExecuteAsync() → ProbeAsync(..., ChangeIntent.Execute, ct)                           :233
  │     └─ !HasRequiredPermission → 写 Denied 回执 "permission.denied"                             :249–251
  │
  └─(Restore) RestoreAsync() → ProbeAsync(..., ChangeIntent.Restore, ct)                           :529
        └─ !HasRequiredPermission → Denied 回执 "permission.denied"                                :545–547
```

门禁契约（`Core/Contracts/Changes/ChangeModels.cs`）：

```csharp
public sealed record CapabilityDescriptor(
    string CapabilityId, string DefinitionVersion, ChangeRisk Risk,
    IReadOnlyList<TargetScopeKind> SupportedScopes,
    bool RequiresAdministrator,            // ← 由 capability 自己声明（不是进程状态）
    bool RequiresExplicitConfirmation, bool RequiresRecoveryMaterial, bool RequiresRestart,
    string ResourceKey, string ExecutionPolicyVersion = "bounded-platform.v1");   // L76–86

public sealed record CapabilityAssessment(
    CapabilityApplicability Applicability,
    bool HasRequiredPermission,            // ← 门禁唯一读取的权限位
    string ReasonCode, string Details);                                            // L104–108
```

`IChangeCapability`（`Core/Contracts/Changes/IChangeCapability.cs`）：`Descriptor`（L8）、
`ProbeAsync(TargetScope, CancellationToken)`（L10）、带 `ChangeIntent` 的重载（L11–15）。

**执行前置的第二道门**：`GameOptimizer.Shell/Services/Baseline/BaselineChangeExecutionGate.cs`
`EnsureCanExecuteAsync`（L19）——Restore 意图直接放行（L27–30），其余走 `IBaselineGateService.EnsureCanOptimizeAsync($"change:{capabilityId}")`；
该门禁**与提权无关**（基线与快照策略）。

**注意**：`CompatibilityChecker.ProbeAsync` 在 `CanExecute == true` 时返回 `null`，
`ChangeOrchestrator.ProbeAsync` 因此回退到 `capability.ProbeAsync(...)`（L1184）。
也就是说**平台自评才是最终权限位来源**；兼容性策略只负责「阻断时给原因」。

---

## 5. UI 开关 / 页面可用性门禁路径

### 5.1 主模块开关（`MasterModuleTemplate` + `OptimizationToggleModel`）

```
UserPrivilegeService（DI singleton，构造时 Refresh 一次）                Shell/Services/UserPrivilegeService.cs:14–17
  └─ DashboardViewModel ctor: IsAdministrator = privilegeService.IsAdministrator
        │                                                              ViewModels/DashboardViewModel.cs:1148
        ├─ 订阅 privilegeService.PropertyChanged → OnPrivilegePropertyChanged
        │     ├─ e.PropertyName == "IsAdministrator"                    :1175
        │     ├─ IsAdministrator = _privilegeService.IsAdministrator     :1179
        │     └─ 逐 toggle：toggle.RefreshPrivilege(IsAdministrator)      :1184
        │            └─ OptimizationToggleModel.RefreshPrivilege(bool)   Models/OptimizationToggleModel.cs:576
        │                 └─ EvaluateCapabilities()                      :587
        │                      ├─ RequiresAdmin && !_isAdministrator
        │                      │     → BlockedReason = "需要管理员权限；当前进程未获得 operation 所需权限。"   :636–638
        │                      └─ CanApply = true（其余分支满足时）        :644
        ├─ 模块状态同步：SyncModuleStatusAsyncCore → _optimizationEngine.GetStatusesAsync(module.Key)
        │                                                              ViewModels/DashboardViewModel.cs:3006–3012
        └─ module.Toggles[i].SyncOperationContract(statuses[i], IsAdministrator)   :3021
                 └─ OptimizationToggleModel.SyncOperationContract(status, isAdministrator)  :544
                      ├─ _isAdministrator = isAdministrator             :547
                      ├─ SupportState = status.SupportState              :548
                      ├─ IsReadbackKnown = true                          :563
                      └─ EvaluateCapabilities()                          :565
                           └─ ResolveCapabilityState()                   :666
                                ├─ AlternativeAvailable → AlternativeAvailable   :669
                                ├─ PermissionRequired  → AdminRequired           :670
                                ├─ ManagedByPolicy     → PolicyManaged           :671
                                ├─ Conflict/NotApplicable/Unsupported/Unknown …   :672–675
                                ├─ !IsReadbackKnown → ReadbackUnavailable         :687
                                ├─ RequiresAdmin && !_isAdministrator → AdminRequired  :688
                                └─ Ready                                          :690

显示绑定（Views/MasterModuleTemplate.xaml.cs）：
  L1133  toggleBtn.IsEnabled   ← Binding(OptimizationToggleModel.CanToggle)
  L958–960 能力标签文本/颜色   ← toggle.CapabilityStateLabel / GetCapabilityColors(CapabilityState)（L1175–1180）
  L1021 原因文本 Visibility    ← Binding(OptimizationToggleModel.HasCapabilityConstraint)
```

关键属性（`OptimizationToggleModel.cs`）：

| 成员 | 行 | 语义 |
| --- | --- | --- |
| `RequiresAdmin { get; init; } = true` | 212 | **默认 `true`**：任何未显式传值的开关默认「需要管理员」 |
| `_isAdministrator` | 284 | 由 `SyncOperationContract` / `RefreshPrivilege` 注入；构造时（`isAdministrator` 参数，L119）注入 |
| `CanToggle` | 293 | `!_stateMachine.IsOperating && (CanApply \|\| CanRestore)` |
| `CapabilityState` | 299 | `ResolveCapabilityState()` |
| `HasCapabilityConstraint` | 321 | `CapabilityState != Ready` |
| `CanRetryCapabilityProbe` | 317 | `ReadbackUnavailable \|\| Unknown \|\| Conflict` |

`enum ModuleCapabilityState`（L16）：`Ready` / `AdminRequired` / `PolicyManaged` / `Unsupported` / `NotApplicable` /
`ReadOnly` / `RecoveryUnavailable` / `Executing` / `Conflict` / `Unknown` / `AlternativeAvailable`。
文案键：`Module.Capability.{State}` + `Module.Capability.{State}.Reason`
（`LocalizationService.ZhCN.cs:1269–1270`：`Module.Capability.AdminRequired = "需要管理员"`、`...Reason = "请以管理员身份重新启动后再试。"`）。

**注意一条 dead binding**：`MasterModuleTemplate.xaml.cs:1175–1180` 给 `AdminRequired or RecoveryUnavailable` 配了警告色，
但**同处 L958–960 只在初始创建时取一次颜色**；后续 `CapabilityState` 变化只刷新文本绑定（L960），
颜色不会重算 ⇒ 「状态已恢复为 Ready，但色块仍是警告色」的外观残留（不影响可点击性）。

### 5.2 电源页（受管方案）

```
PowerPlanEditorPage.RefreshPageAsync
  ├─ _powerOwner.InspectAsync(null)                         Views/PowerPlanEditorPage.xaml.cs:113
  │     └─ TxtPowerStatus.Text ← state.Environment.IsAdministrator
  │            ? "管理员权限已就绪" : "将请求管理员确认"      :118     （环境来自 WindowsPowerPlatform.cs:30）
  └─ _managedOverview = _managedPower.InspectAsync()        :128
        └─ ManagedPowerPlanService.CanMutate(facts, out status)   ManagedPowerPlanService.cs:548
              ├─ !facts.IsWindows || Build < 19041 → "需要 Windows 10 2004 或更高版本。"        :550
              ├─ !facts.IsAdministrator            → "创建或修改受管电源计划需要管理员权限。"  :551
              ├─ !facts.IsDetectionReliable        → "…已按安全策略阻止。"                     :552
              ├─ facts.IsPolicyRestricted || IsOemRestricted → "检测到组织或 OEM 电源策略接管…" :553
              ├─ facts.IsModernStandby             → "Modern Standby 设备…"                     :554
              ├─ HasBattery && !IsOnAcPower        → "请先接通交流电…"                         :555
              └─ true

ApplyCapabilityState()                                       Views/PowerPlanEditorPage.xaml.cs:146
  ├─ canEditManagedPlan = _managedOverview is { CanMutate: true } && _selectedManagedPlanId.HasValue   :148
  ├─ SliderCpuMinimum / SliderCpuMaximum / SliderCpuEpp / BoostModeSelector / HeteroPolicySelector.IsEnabled
  │     ← canEditManagedPlan && _supportedManagedSettings.Contains(...)                     :149–153
  ├─ BtnApplyAll.IsEnabled ← canEditManagedPlan                                             :154
  ├─ BtnRestoreDefault.IsEnabled ← Backups.Count > 0                                        :155
  ├─ 不可编辑时的提示：var reason = Plans.Count > 0 ? "请先选择一个本软件创建的电源方案。" : _managedOverview?.Status ?? …  :159
  └─ TxtPowerCapabilityState.Text ← 固定说明文案（与 CanMutate 原因无关）                     :165–166
```

⚠ **L159 的原因选择顺序会遮蔽权限原因**：只要 `Plans.Count > 0`（历史上创建过方案），
即使真实原因是「需要管理员权限」（L551），界面也只会显示「请先选择一个本软件创建的电源方案。」
（写入 `TxtDisplayTimeoutState.ToolTip` / `BtnApplyAll.ToolTip`）。这是「功能不可用但看不到真实原因」的直接来源之一。

### 5.3 电竞模式

```
EsportsModePage.xaml.cs:116 → new EsportsModeStateService(engine, baselineGate, () => _privilegeService.IsAdministrator)
  └─ EsportsModeStateService.cs:101   isAdministrator = _isAdministrator()
        ├─ 异常 → 整个 plan fail-closed（:105–112）
        └─ :129  if (registration.Operation.RequiresAdmin && !isAdministrator)
                    → 该步骤标记 Blocked / permission denied（:145 亦读 RequiresAdmin）
契约字段：EsportsModeWorkflowModels.cs:109  bool RequiresAdministrator
```
注意 `() => _privilegeService.IsAdministrator` 是**惰性委托**，因此它读的是 `UserPrivilegeService` 当前值；
但该值只在构造时刷新过一次（§1.3），实际等价于启动快照。

### 5.4 预装应用清理

```
AppxCleanupViewModel.RefreshAsync
  └─ probe = owner.ProbeAsync()                                     ViewModels/AppxCleanupViewModel.cs:149
       ├─ !probe.HasRequiredPermission → PageState = Unauthorized   :151
       ├─ probe.ReasonCode == "permission.administrator-required"
       │     → StatusText = "需要以管理员身份运行本软件后才能管理预装应用。"   :152
       └─ IsUnauthorized => PageState == Unauthorized               :109（XAML 绑定控制可用性）
平台来源：WindowsAppxRemovalOwner（IsCurrentUserAdministrator → WindowsElevation.IsElevated，:323）
```

### 5.5 系统还原点

```
SystemRestorePointOwner / WindowsSystemRestorePointPlatform
  └─ WindowsSystemRestorePointPlatform.cs:44,72   SystemRestorePointProbe(..., IsAdministrator(), ...)
        └─ SystemRestoreService.GetSystemRestoreStatusAsync
              └─ SystemRestoreStatus.IsAdministrator = result.Probe?.IsAdministrator == true   Services/SystemRestoreService.cs:182
                    └─ RestorePointViewModel.IsNotAdministrator => SystemRestoreStatus is { IsAdministrator: false }   ViewModels/RestorePointViewModel.cs:152
                          └─ :300–302  非管理员 → 显示 LocalizationService.Get("Restore.NeedAdmin")
另：StartupDiagnosticService.cs:90  if (!restoreStatus.IsEnabled && restoreStatus.IsAdministrator)  （用同一 DTO 位判断）
写还原点的独立进程：SystemRestorePointWriterEntryPoint.cs:59  if (!native.IsAdministrator) → WriteFailure("Administrator permission is required by Windows System Restore.")
```

### 5.6 其余页面的门禁一览

| 页面/VM | 判定点 | 非管理员时的行为 | 是否禁用控件 |
| --- | --- | --- | --- |
| `SystemCleanupViewModel.CleanAsync` | `:355` | `StatusText` + `InfoDialog`，return | 否（命令仍可点，点后弹窗） |
| `BackupManagerViewModel.RestoreRegistryBackupAsync` | `:1922` | `StatusText` + `InfoDialog`，return | 否 |
| `RepairCenterViewModel`（3 处） | `:698,948,1080` | `InfoDialog`，return | 否；`RepairItemViewModel.CanRepair`（`RepairModels.cs:73`）/`CanMutate` 与提权无关 |
| `FirstRunBaselineOrchestrator.RunPreflightAsync` | `:426` | 流程失败 + 错误文案 | — |
| `CpuCoreManagementViewModel.InitializeAsync` | `:177` | 仅 Toast 警告 | 否（`IsAdmin` 未绑定任何控件） |
| `ProcessPriorityPage.OnLoaded` | `:41,104` | 仅 IFEO 阻断 Toast（**无提权判定**，见 §6.6） | 否 |
| `PowerPlanEditorPage` | §5.2 | 禁用滑块/按钮 + ToolTip | 是 |
| `AppxCleanupViewModel` | §5.4 | `Unauthorized` 页面态 | 是（页面态驱动） |
| `RestorePointViewModel` | §5.5 | `Restore.NeedAdmin` 文案 | 部分（由 `IsNotAdministrator` 绑定的控件） |

### 5.7 外壳状态显示

| 位置 | 内容 |
| --- | --- |
| `MainWindow.xaml.cs:277–283` | `AdminModeText.Text` ← `_viewModel.IsAdministrator` |
| `MainWindow.xaml.cs:284–291` | `AdminStatusDot.Fill` ← 绿 `#22C55E`（管理员）/ 橙 `#F59E0B`（普通） |
| `MainWindow.xaml:525,537` | `AdminModeText` / `AdminStatusDot` 元素定义 |
| `DashboardViewModel.cs:421` | `PrivilegeHint => IsAdministrator ? "Dashboard.Privilege.Admin" : "Dashboard.Privilege.Normal"`（`LocalizationService.ZhCN.cs:5560–5561`） |
| `OverviewPage.xaml:119` | `{Binding PrivilegeHint}` |
| `DashboardViewModel.cs:3474` | `OnIsAdministratorChanged(bool)` → 仅 `OnPropertyChanged(nameof(PrivilegeHint))` |
| `MainWindow.xaml.cs:300,683–700` | `_viewModel.PropertyChanged += OnViewModelPropertyChanged`；该处理器**只处理 `SelectedOptimizationModule`**，不处理 `IsAdministrator` ⇒ 角标不会随权限变化即时刷新（只有下一次 `UpdateLocalizedTexts()` 才更新） |
| `App.xaml.cs:568–580` | 启动后读 `IUserPrivilegeService.IsAdministrator`，**仅** `Debug.WriteLine` + 诊断日志（非管理员时写 "Running without administrator privileges"） |

---

## 6. **与实际提权状态无关（或恒真/恒假/提前短路）的判定清单**（重点）

> 这一节直接回答验收项「指出哪些判定与实际提权状态无关」。
> 每条都给出：位置 → 判定内容 → 为什么与真实提权无关 → 用户可见后果。

### 6.1 `RequiresAdmin` 声明位 ≠ 进程提权位（大面积误判来源）

| 位置 | 说明 |
| --- | --- |
| `OptimizationToggleModel.RequiresAdmin { get; init; } = true`（`OptimizationToggleModel.cs:212`） | **默认 `true`**。构造时由 `op.RequiresAdmin` 覆盖（`DashboardViewModel.cs:2619` 传入最后一个参数前的 `op.RequiresAdmin`）。若某个 operation 的声明为 `true` 而进程未提权，开关即被 `ResolveCapabilityState()` L688 判为 `AdminRequired` |
| `FailClosedGraphicsOptimizationOperation.RequiresAdmin => true`（`FailClosedGraphicsOptimizationOperation.cs:37`） | 恒 `true`（见 §6.3） |
| `SolutionStep.RequiresAdmin`（`SolutionProvider.cs:45,56–57`、`BackupDiagnosticService.cs:52`） | 方案级标记，与进程状态无关；`SmartErrorDialog.xaml.cs:219` / `OperationDetailCardDialog.cs:121` 直接把它当「需要管理员」显示 |
| `OperationTransparencyDialog.xaml.cs:341,521–523` | 读 `toggle.RequiresAdmin` 输出「需要管理员权限。」——同样是声明位 |
| `GraphicsSafetyRequest.RequiresAdministrator = true`（`GraphicsSafetyModels.cs:48`） | 恒 `true` 的请求默认值 |
| `EsportsModeWorkflowModels.cs:109 bool RequiresAdministrator` | 由注册表声明 |

### 6.2 「管理员状态」被固化为进程生命周期快照

| 位置 | 判定内容 | 为什么与运行时提权状态无关 | 后果 |
| --- | --- | --- | --- |
| `SystemCompatibilityChecker._cachedSystemInfo`（`:18`）+ `IsAdminMode`（`:314`） | `Lazy<SystemInfo>`，只求值一次 | 值在首次访问时冻结（虽然令牌本身不变，但**任何后续提权/降权/令牌切换场景都无法反映**） | `PowerOperationDiagnostics` 6 处诊断上下文长期使用旧值 |
| `RuntimeWindowsCompatibilityEnvironmentProbe._cached`（`:616,625`） | 兼容性环境（含 `IsAdministrator`）永久缓存 | 同上 | 能力探测的权限位不会随任何状态变化重算 |
| `UserPrivilegeService`（`:14–17`） | 构造时 `Refresh()` 一次 | 运行期无调用方（`:26` `Refresh()` 公开但无 call site） | `UserPrivilegeService.IsAdministrator → DashboardViewModel.IsAdministrator → 所有 toggle` 只在启动时定一次；`MainWindow` 的 `PropertyChanged` 处理器也不监听它 |
| `SystemGuard`（`HEAD` 版本，`:32–51`） | 5 分钟缓存 `_isAdminCached` | 缓存窗口内的提权变化不可见 | 已被工作树改为转发（§2.5） |

### 6.3 图形/显示项：**恒定与提权无关**的裁决

| 位置 | 说明 |
| --- | --- |
| `FailClosedGraphicsOptimizationOperation.UnknownEnvironment`（`:7–8`） | 硬编码 `GraphicsEnvironmentSnapshot([Unknown], false, false, false, false, null, "", "", false)` —— 其中 `IsAdministrator = false`、`DriverVersion = null`、`HasRecoveryReceipt = false` |
| `FailClosedGraphicsOptimizationOperation.EvaluateAsync`（`:70–71`） | 每个图形项都拿这个**常量环境**去评估，与真实机器、真实提权状态无关 |
| 继承者 | `FullscreenOptimizationOperation.cs:5`、`AmdOptimizationOperation.cs:12`（`AmdRadeonAntiLagOperation`）、`GpuAdvancedOperation.cs:18,138,149,196`（`DisableDwmAnimationsOperation`/`DisableXboxGameMonitoringOperation`/`DisableVrrOperation`/`AmdAntiLagOperation`），以及组合持有者 `GpuVendorOperation`（`GpuVendorOperation.cs:9`）与 `GpuAdvancedOperation` 内部的 `_boundary`（`GpuVendorOperation.cs:38`） |
| 实际返回值 | 这些项默认用 `UnavailableGraphicsSafetyBoundary.Instance`（`:26`）→ `GraphicsSafetyBoundary.cs:103–119` **直接返回 `PolicyDenied`**（"No approved graphics mutation platform is registered; the legacy entry is blocked."），**根本不看环境** |
| 环境相关的分支 | `GraphicsSafetyBoundary.EvaluateAsync`（`:11–94`）里 L59 的 `request.RequiresAdministrator && !environment.IsAdministrator → AdministratorRequired` **在当前接线中永远走不到**：唯一构造它的地方是测试（`GraphicsSafetyBoundaryTests.cs:7`），`GraphicsSafetyBoundary` 在 DI 中未注册、也没有 `new GraphicsSafetyBoundary(...)` 的生产调用点 |
| 图形修复 | `Services/Repair/GraphicsRepairBoundary.cs:7–8,27–29` 用同一硬编码环境 + `UnavailableGraphicsSafetyBoundary` ⇒ 恒 `PolicyDenied`（`:65`） |
| 路由 | `OptimizationEngine.cs:1773` `IsFailClosedCatalogOperation` → Apply 前阻断（`:625`）、`:1777` fail-closed 图形路由 |

结论：图形/显示类入口的「不可用」**与是否提权完全无关**，其原因是「没有受批准的写入平台」。
若 UI 把它渲染成「需要管理员」，属于文案误归因。

### 6.4 进程优先级页：名为权限检查、实为固定 Toast

```csharp
// GameOptimizer.Shell/Views/ProcessPriorityPage.xaml.cs
private const string IfeoBlockedMessage = "IFEO 持久优先级尚无独立 Owner、durable restore 与受控 Windows 验证，当前已零写入阻断。";  // :16
private static bool IsIfeoBlocked => true;                                                                                      // :17
private void OnLoaded(...) { ... CheckAdminPrivileges(); ... }                                                                   // :37–43
private void CheckAdminPrivileges()
{
    ToastService.Instance.Warning(_loc.Get("Common.Warning"), IfeoBlockedMessage);   // :106
}
```

`CheckAdminPrivileges()` **没有任何提权判定**，`IsIfeoBlocked` 恒 `true`，因此该提示恒定出现且与权限无关。
同时 `RegistryPriorityService` 的**两处**提权判定**都在死代码里**：
`SetProcessPriority` 在 `#if` 生效前已于 **L32** 直接返回 `"IFEO persistent priority is blocked: …"`，
其 `if (!HasAdminPrivileges())` 位于 `#if false` 块（L33–95）内的 **L39**；
`RemoveProcessPriority` 同样在 **L101** 直接返回阻断结果，其提权判定位于第二个 `#if false` 块（L102–167）内的 **L108**。
⇒ **`RegistryPriorityService.HasAdminPrivileges()`（L26）当前没有任何可达调用方**，
「进程优先级需要管理员」这类提示不可能来自这里。

### 6.5 无条件「以管理员身份运行」建议

| 位置 | 说明 |
| --- | --- |
| `BackupDiagnosticService.cs:40–54` | 只要抛 `UnauthorizedAccessException`（L40）就追加 `Backup.Solution.RunAsAdmin`（L51，`RequiresAdmin = true` L52），**不检查当前是否已提权** ⇒ 已提权用户会收到无意义的提权建议。⚠ 且该键**只存在于两个 JSON 目录**（`Core/Localization/Strings.zh-CN.json:410` 与 `Strings.en-US.json:410`），两处值都是英文 `"Restart program as administrator"` —— 中文界面下会显示英文（见 附录 A） |
| `SolutionProvider.cs:39–48` | `context.IsAdminMode == false` 时把 `Solution.RunAsAdmin` 作为第一条方案（L44，`RequiresAdmin = true` L45）；`IsAdminMode` 默认 `false`（`Diagnostics/ErrorModels.cs:75`），任何忘记赋值的 `DiagnosticContext` 都会得到提权建议 |
| `OperationErrorAnalyzer` 之外的调用方 | `PowerOperationDiagnostics`/`PowerSettingIndexOperation` 用了冻结/即时值，其余诊断入口若自行构造 `DiagnosticContext` 而不设 `IsAdminMode`，默认即「非管理员」 |

### 6.6 「恒 false」与「恒 true」的提权相关常量

| 位置 | 常量 | 后果 |
| --- | --- | --- |
| `FailClosedGraphicsOptimizationOperation.cs:7` | `IsAdministrator = false` | 见 §6.3 |
| `GraphicsRepairBoundary.cs:7` | `IsAdministrator = false` | 见 §6.3 |
| `OptimizationToggleModel.cs:212` | `RequiresAdmin = true`（默认） | 未显式声明的开关默认需要管理员 |
| `ProcessPriorityPage.xaml.cs:17` | `IsIfeoBlocked => true` | 恒定阻断提示 |
| `WindowsElevation`（`HEAD` 前的 `SystemGuard`） | `CheckAdministrator()` 的 `catch → false` | 令牌读取异常时默认「非管理员」（fail-closed 且无提示） |
| 各平台 accessor 的私有 `IsAdministrator()` | 无；现在统一转发 | — |

### 6.7 与提权无关的门禁（易与权限混淆）

| 位置 | 真实门禁维度 |
| --- | --- |
| `BaselineChangeExecutionGate.EnsureCanExecuteAsync`（`BaselineChangeExecutionGate.cs:19–30`） | 安全基线与快照策略 |
| `IsFailClosedCatalogOperation`（`OptimizationEngine.cs:1773`） | 目录项是否 fail-closed |
| `ManagedPowerPlanService.CanMutate`（`:548`） | OS 版本 / 探测可靠性 / 策略接管 / Modern Standby / 电池供电（**其中只有 L551 是权限**） |
| `AppxRemovalDenyList.IsProtected` / `IsSystemAppsLocation`（`WindowsAppxRemovalOwner.cs:81–84`） | 受保护组件/收件箱位置（与权限无关的不可移除原因） |
| `RepairResult.CanMutate`（`RepairModels.cs:73`） | `Availability == WorkingBidirectional` |
| `WindowsNetworkResetPlatform.AssessResetSafetyAsync`（`:36`） | VPN/代理/静态地址风险 |
| `SystemGuard.CheckPrerequisites` 的 `IsGameRunning()` / 磁盘空间（`:165–178`） | 进程与磁盘（该 API 零调用方） |

---

## 7. 提权语义：进程如何获得管理员权限（以及缺失的路径）

| 事实 | 位置 | 影响 |
| --- | --- | --- |
| 清单级别 = `highestAvailable` | `GameOptimizer.Shell/app.manifest:19` `<requestedExecutionLevel level="highestAvailable" uiAccess="false" />` | 标准用户账户下**不会**提权；只有管理员账户才会拿到完整令牌。用户「右键以管理员身份运行」在标准用户下失效 |
| 全仓唯一 `runas` 启动 | `Views/RegistryInfoDialog.xaml.cs:308`（`regedit.exe`，`UseShellExecute = true`，L307） | 这是**唯一**显式请求提升的地方，且仅用于打开注册表编辑器 |
| 无自提权重启 | `PermissionChecker.CanRestartAsAdmin()`（`:77`）零调用方且不检测提权；全仓无 `Process.Start` + `runas` 自我重启、无 `Application.Restart` 提权变体 | 「以管理员身份重新启动」只能靠用户手动操作——所有文案（`Module.Capability.AdminRequired.Reason` 等）都依赖用户手动做 |
| 子进程继承令牌 | `Infrastructure.Windows/SystemRestore/SystemRestorePointWriterProcessRunner.cs:54–66`（`UseShellExecute = false`，无 `Verb`） | 还原点写入子进程**继承父进程令牌**：父进程未提权 ⇒ 子进程在 `SystemRestorePointWriterEntryPoint.cs:59` 直接 `WriteFailure("Administrator permission is required by Windows System Restore.")` |
| 其余外部进程同样继承 | `SafeProcessHelper.StartProcess`（`SafeProcessHelper.cs:302–328`）、`ProcessRunner`（`ProcessRunner.cs:43+`，`UseShellExecute = false`）、`PowerCfgProcessRunner.cs:120`、`RegExeUserRegistryExportBackend.cs:35`、`NetshUtility.cs:37`、`BcdSettingOperation.cs:325,406`、`MemoryConfigOperation.cs:155,307`、`PowerCfgUtility.cs:213`、`PowerSettingIndexOperation.cs:400`、`SystemCompatibilityChecker.cs:130,401,435`、`CompatibilityChecker.cs:760` 等 | 所有平台写入都在**当前令牌**下执行；权限不足时只能返回失败，没有二次提权机会 |
| `UseShellExecute = true` 的启动点 | `MainWindow.xaml.cs:562–565`、`SystemRestoreManualLauncher.cs:31–34`、`RepairCenterViewModel.cs:944,1048,1448,1472`、`SettingsPage.xaml.cs:292,781,823,927`、`Views/NetworkResetDialog.xaml.cs:117`、`OperationInfoDialog.xaml.cs:182`、`OperationTransparencyDialog.xaml.cs:897`、`AboutDialog.xaml.cs:164`、`HardwareInfoPage.xaml.cs:399`、`GuardFirstTimeDialog.xaml.cs:48`、`CleanupFileListDialog.xaml.cs:229`、`FirstRunBaselineDialog.xaml.cs:923`、`DashboardViewModel.cs:1626,1907`、`BackupManagerViewModel.cs:1783,2057,2099`、`LatencyTestViewModel.cs:279`、`SafeProcessHelper.StartProcess` 的调用方（`SystemRestoreManualLauncher`） | 走 Shell 打开系统工具/目录；**除 `RegistryInfoDialog` 外都没有 `Verb = "runas"`**，因此不会弹 UAC |

---

## 8. 诊断信息缺失 / 误归因点（与「失败没有错误信息」相关）

| 位置 | 缺失/误归因方式 | 用户可见表现 |
| --- | --- | --- |
| `ProcessPriorityPage.CheckAdminPrivileges()`（`:104–107`） | 提示内容与权限无关，真正原因（IFEO 无 Owner）与页面可用性都不体现 | 每次进页面固定弹同一 Toast |
| `PowerPlanEditorPage.ApplyCapabilityState()`（`:159`） | `Plans.Count > 0` 时用「请先选择一个方案」覆盖 `CanMutate` 的真实原因（可能正是「需要管理员权限」） | 控件禁用，但看不到权限原因 |
| `CpuCoreManagementViewModel`（`:177–182`） | 只用 Toast 提示 `CoreAccel.PermissionHint`，不落日志、不写状态文本 | Toast 消失后无痕迹 |
| `UserPrivilegeService.Refresh()` 无触发点 | 权限状态变化后（例如以管理员重新启动后的新一轮交互）UI 不重算 | 侧栏/开关与真实令牌不一致 |
| `MainWindow.OnViewModelPropertyChanged`（`:683–700`） | 只处理 `SelectedOptimizationModule`，不处理 `IsAdministrator` | 角标颜色滞后 |
| 各 accessor 的 `catch → IsAdministrator() == false` | 令牌读取异常与「真的没提权」无法区分（现在由 `WindowsElevation.Probe()` 的 `elevation.probe-failed` 才能区分，但该 reason code 无消费者） | 失败原因被压缩成「需要管理员」 |
| `CompatibilityChecker.ProbeAsync` 返回 `null` 表示「可执行」 | 权限通过时**不产生评估对象**，`Details` 为空 | 执行失败时没有权限侧上下文 |
| `OptimizationToggleModel.EvaluateCapabilities()` 的 `BlockedReason` | 权限分支是硬编码中文（`:638`），与同文件其它分支的本地化键混用 | 语言切换后仍显示中文 |
| `OptimizationResultMessageFormatter.Format`（`:22–27`） | 依赖**关键词匹配**原始消息（`administrator`/`管理员权限`/`access denied`…）来决定话术；消息不含这些词时落到兜底「应用失败…」 | 原因文案依赖上游消息文本 |

---

## 9. Windows 平台调用封装地图

### 9.1 项目与注册入口

| 层 | 项目 | 注册入口 |
| --- | --- | --- |
| 平台实现 | `GameOptimizer.Infrastructure.Windows` | `Extensions/ServiceCollectionExtensions.cs`（800+ 行；`AddSingleton<IXxxPlatform/Accessor/Capability, WindowsXxx>`，含 `TryAddSingleton<CompatibilityChecker>()` 的 Core 侧注册） |
| Core 服务 | `GameOptimizer.Core` | `Extensions/ServiceCollectionExtensions.cs`（`:52–55` 注册 `IChangeExecutionGate`/`CompatibilityChecker` 与两个兼容性策略接口指向同一单例；`:70` `IOptimizationEngine → OptimizationEngine`；`:119` `IMemoryCleanerService → MemoryCleanerService`；`:133` `SystemCompatibilityChecker`） |
| Shell | `GameOptimizer.Shell` | `App.xaml.cs:254` 等（内联 `Host.CreateDefaultBuilder().ConfigureServices(...)`） |

`GameOptimizer.Infrastructure.Windows` 的子目录即平台能力分组：
`ApplicationRepair`、`Changes`、`DeviceRepair`、`Extensions`、`MaintenanceDiagnostics`、`Memory`、`NetworkDiagnostics`、
`NetworkReset`、`PowerManagement`、`ProcessTuning`、`Registry`、`Services`、`Startup`、`StorageSafety`、`SystemRestore`。

主要平台服务（节选，均位于 `Infrastructure.Windows/Extensions/ServiceCollectionExtensions.cs`）：

```
IManagedAutostartPlatform → WindowsManagedAutostartPlatform        :42
IPowerProcessFactory      → WindowsPowerProcessFactory             :58
IPowerCfgProcessRunner    → PowerCfgProcessRunner                  :59
IPowerPlatform            → WindowsPowerPlatform                   :60
IManagedPowerPlanPlatform → WindowsManagedPowerPlanPlatform        :61
INetworkResetPlatform     → WindowsNetworkResetPlatform            :80
IRegistryDwordAccessor    → WindowsRegistryDwordAccessor           :85
IRegistryStringAccessor   → WindowsRegistryStringAccessor          :86
IRegistryValueSetAccessor → WindowsRegistryValueSetAccessor        :87
IPerInterfaceRegistryAccessor → WindowsPerInterfaceRegistryAccessor:88
IWindowsServiceAccessor   → WindowsServiceAccessor                 :89
IProcessTuningPlatform    → WindowsProcessTuningPlatform           :90
IScheduledTaskAccessor    → WindowsScheduledTaskAccessor           :133
IPowerSchemeCatalog       → WindowsPowerSchemeCatalog              :829
IPowerProfilePreparationService → PowerProfilePreparationService   :830
FileChangeStateStore      → IRecoveryRepository/IChangeJournal/IChangeAuditStore/IChangePlanStore  :837–841
FileChangeResourceLockProvider                                     :842
```

### 9.2 原生调用（P/Invoke）清单：86 处，12 个 DLL

| DLL | 数量 | 位置（仓库相对路径:行） | 用途 |
| --- | --- | --- | --- |
| `advapi32` | 4 | `Core/Services/WindowsElevation.cs:350`；`Infrastructure.Windows/Memory/WindowsMemoryCleaningPlatform.cs:46`；`NetworkDiagnostics/WindowsNetworkAdapterDhcpPlatform.cs:206`；`ProcessTuning/WindowsProcessTuningPlatform.cs:423` | `GetTokenInformation`（提权/组/SessionLuid）、`OpenProcessToken`（取目标进程 SID） |
| `kernel32` | 32 | `Core/Services/CpuTopologyService.cs:151,154`；`Core/Services/Latency/LatencyTestService.cs:19,22`；`Core/Services/LiveSystemInsightService.cs:629`；`Core/Services/Memory/MemoryCleanerService.cs:27`；`Core/Services/PowerSettingCapabilityGuard.cs:361`；`Infrastructure.Windows/Memory/WindowsMemoryCleaningPlatform.cs:28,32,39,42,50,54,57`；`PowerManagement/FilePowerWorkflowStore.cs:77`；`PowerManagement/PowerSchemeChangeCapability.cs:113,116,227`；`PowerManagement/PowerSettingChangeCapability.cs:233`；`PowerManagement/WindowsPowerPlatform.cs:184`；`ProcessTuning/WindowsProcessTuningPlatform.cs:416,419,427,431,434`；`SystemRestore/FileSystemRestorePointWorkflowStore.cs:726`；`SystemRestore/WindowsSystemRestorePointPlatform.cs:253`；`Shell/Services/ClipboardHelper.cs:21,24,27,30`；`Shell/Services/SystemRestorePointWriterEntryPoint.cs:243` | 内存/进程句柄/回读、系统卷信息、进程优先级与亲和性、时间/编码工具 |
| `powrprof` | 18 | `Core/Services/PowerSettingCapabilityGuard.cs:364`；`Infrastructure.Windows/PowerManagement/PowerSchemeChangeCapability.cs:110,118,161,163,221,224`；`PowerSettingChangeCapability.cs:218,221,224,227,230`；`WindowsPowerPlatform.cs:171,173,175,177,179,181` | 电源方案/设置读写（`PowerReadACValue`、`PowerWriteACValueIndex`、`PowerSetActiveScheme` 等） |
| `user32` | 19 | `Core/Optimization/Operations/AccessibilityToggleOperation.cs:163,167,171`；`Core/Optimization/Operations/InputDeviceApplicabilityCapability.cs:118`；`Core/Services/LiveSystemInsightService.cs:632,635`；`Shell/Program.cs:183`；`Shell/Services/ClipboardHelper.cs:9,12,15,18`；`Shell/Services/WindowVisibilityGuard.cs:19,22,25,28,31`；`Shell/Startup/StartupOrchestrator.cs:376,379,383` | `SystemParametersInfoW`（无障碍开关）、窗口查找/置顶/剪贴板、启动失败原生弹窗 `MessageBoxW` |
| `ntdll` | 3 | `Core/Optimization/Operations/TimerResolutionOperation.cs:182,185`；`Core/Services/Latency/LatencyTestService.cs:16` | 定时器精度（`NtSetTimerResolution` 等）、延迟测量 |
| `iphlpapi` | 1 | `Infrastructure.Windows/NetworkDiagnostics/WindowsNetworkAdapterDhcpPlatform.cs:198` | `GetAdaptersAddresses`（结构化只读网卡/DHCP 探测） |
| `dnsapi` | 3 | `Core/Optimization/Operations/DnsCacheFlushOperation.cs:128,131,134` | `DnsGetCacheDataTable` / `DnsFree` / `DnsFlushResolverCache` |
| `srclient` | 1 | `Shell/Services/SystemRestorePointWriterEntryPoint.cs:237` | `SRSetRestorePointW`（独立进程写还原点） |
| `crypt32` | 4 | `Infrastructure.Windows/PowerManagement/FilePowerWorkflowStore.cs:71,74`；`SystemRestore/FileSystemRestorePointWorkflowStore.cs:704,715` | DPAPI 保护受管工作流载荷 |
| `dxgi` | 1 | `Core/Services/DxgiGpuAdapterDiscovery.cs:83` | 显卡适配器枚举 |
| `kernel32`（Shell 剪贴板组） | 见上 | — | — |

> 无 `[DllImport]` 但同属原生面的：`System.Management`（WMI）与 `System.ServiceProcess`（见 §9.5）。

### 9.3 进程启动封装

| 封装 | 位置 | 特征 |
| --- | --- | --- |
| `SafeProcessHelper` | `Core/Services/SafeProcessHelper.cs` | `StartProcess`（`:302–328`，把 `Win32Exception`/`InvalidOperationException` 转成中文错误串返回，不抛）、`RunProcessAsync`（`:333+`，`UseShellExecute=false` + 重定向输出 + OEM 编码 + 超时）、`EnumerateWindowProcesses`、`SetProcessorAffinity`、`SetPriorityClass`、`KillProcess` |
| `ProcessRunner` | `Core/Services/ProcessRunner.cs` | 统一命令执行：`UseShellExecute=false`、`RedirectStandardOutput/Error=true`、`CreateNoWindow=true`（`:64`）；PowerShell 命令白名单（`:14–24`）+ `DangerousPatterns` 正则（`:26`）；`RunAsync`（`:43`）、`RunPowerShellAsync`（`:131`）、`RunCmdAsync`（`:151`）；异常路径返回 `ProcessResult(-1, "", ex.Message, false)`（`:125` 附近） |
| `PowerCfgProcessRunner` | `Infrastructure.Windows/PowerManagement/PowerCfgProcessRunner.cs:120` | `powercfg` 专用，`UseShellExecute=false` |
| `SystemRestorePointWriterProcessRunner` | `Infrastructure.Windows/SystemRestore/SystemRestorePointWriterProcessRunner.cs:54–66` | 以 `--system-restore-writer --request <path> --response <path>` 拉起自身 (`YuqiEngine.exe`)，`IsAvailable` 校验文件名（`:31–33`），JSON 文件协议 + 超时（`:84–95`） |
| `RegExeUserRegistryExportBackend` | `Core/Services/Snapshot/RegExeUserRegistryExportBackend.cs:35` | `reg export` 子进程，`UseShellExecute=false` |
| `NetshUtility` / `PowerCfgUtility` | `Core/Optimization/Operations/NetshUtility.cs:37`、`PowerCfgUtility.cs:213` | 系统命令包装（含 `PowerCfgUtility.cs:178,191` 的提权相关分支） |
| WPF 侧 `Process.Start`（Shell 打开系统工具） | 见 §7 表 | 多为 `UseShellExecute=true`，无 `Verb=runas`（除 `RegistryInfoDialog`） |

### 9.4 注册表封装

| 封装 | 位置 |
| --- | --- |
| `SafeRegistryHelper` | `Core/Services/SafeRegistryHelper.cs:13`（`OpenKeyWritable`/`CreateOrOpenKey` 等安全打开，返回 `(key, error)`） |
| Registry accessor（平台侧） | `Infrastructure.Windows/Registry/WindowsRegistryDwordAccessor.cs`、`WindowsRegistryStringAccessor.cs`、`WindowsRegistryValueSetAccessor.cs`、`WindowsPerInterfaceRegistryAccessor.cs`（注册于 `ServiceCollectionExtensions.cs:85–88`） |
| Registry typed capability | `RegistryDwordCapability.cs`、`RegistryStringCapability.cs`、`RegistryValueSetCapability.cs`、`RegistryValueDeleteCapability.cs`、`PerInterfaceRegistryCapability.cs`（均含 `Registry*CapabilityEnvironment` 记录，其中带 `IsAdministrator` 的见 §2.1 #25–#28） |

### 9.5 服务 / WMI / 系统信息封装

| 面 | 封装位置 |
| --- | --- |
| 服务控制 | `Infrastructure.Windows/Services/WindowsServiceAccessor.cs:3,55,165,246,252,265,290–304`（`ServiceController`）；`DeviceRepair/WindowsDeviceDiagnosticPlatform.cs:28`；`Services/ServiceDependencyChecker.cs:93,123`；`Core/Services/SystemCompatibilityChecker.cs:40,57,73`；`Shell/Services/StartupDiagnosticService.cs:50`；`Optimization/Operations/ServiceToggleOperation.cs:249`、`MultiServiceToggleOperation.cs:342`、`GpuAdvancedOperation.cs:6` |
| WMI | `Core/Services/SafeWmiHelper.cs`（统一包装）；使用方：`Core/Services/SystemCompatibilityChecker.cs:718`（`Win32_VideoController`）、`Infrastructure.Windows/SystemRestore/WindowsSystemRestorePointPlatform.cs:98`、`StorageSafety/WindowsStorageFeatureDiagnosticPlatform.cs:33,67`、`Services/WindowsDeviceStateAccessor.cs:81–84`、`MaintenanceDiagnostics/WindowsMaintenanceDiagnosticPlatform.cs`、`Shell/Services/SystemMonitorService.cs`、`Shell/Services/GameProcessMonitorService.cs`、`Shell/Models/HardwareInfo.cs`、`Shell/Services/DeviceManagerService.cs` |
| 计划任务 | `Infrastructure.Windows/Services/WindowsScheduledTaskAccessor.cs`（`IsAdministrator` 判定 `:142`） |
| hosts 备份 | `Infrastructure.Windows/Services/WindowsHostsBackupOwner.cs`（`:163`） |
| BCD | `Infrastructure.Windows/Services/WindowsBcdEditAccessor.cs`（`:147`） |
| 设备状态 | `Infrastructure.Windows/Services/WindowsDeviceStateAccessor.cs`（`:103`） |
| 内存代理（MmAgent） | `Infrastructure.Windows/Services/WindowsMmAgentAccessor.cs`（`:74`） |
| AppX | `Infrastructure.Windows/Services/WindowsAppxRemovalOwner.cs`（`:323`） |
| 电源方案/设置 | `Infrastructure.Windows/PowerManagement/`（`WindowsPowerPlatform`、`PowerSettingChangeCapability`、`PowerSchemeChangeCapability`、`ManagedPowerPlanPlatform`、`ManagedPowerPlanService`、`PowerProfilePreparationService`） |
| 系统还原 | `Infrastructure.Windows/SystemRestore/`（`WindowsSystemRestorePointPlatform`、`FileSystemRestorePointWorkflowStore`、`SystemRestorePointWriterProcessRunner`）+ `Shell/Services/SystemRestorePointWriterEntryPoint.cs` |

---

## 10. 针对团队目标的两条定位结论

**目标 A：「以管理员身份运行后仍有功能显示需要管理员且不可用」**

按优先级：

1. **判定层已收敛**（§2.5）：当前工作树所有提权判定都走 `WindowsElevation`，`IsElevated` 用 `TOKEN_ELEVATION` + `TOKEN_GROUPS`，
   UAC 筛选令牌不会再被误判成「标准用户」。若问题仍复现，说明**显示源不是提权布尔值**。
2. **显示源很可能是 `RequiresAdmin` 声明位**（§6.1）：`OptimizationToggleModel.RequiresAdmin` 默认 `true`，
   `ModuleCapabilityState.AdminRequired` 的两个分支（`SupportState == PermissionRequired`，L670；`RequiresAdmin && !_isAdministrator`，L688）
   中**前者完全由 operation/兼容性策略决定**，与提权无关。
3. **图形/显示类**（§6.3）恒为「无写入平台」裁决，若被渲染成「需要管理员」即为误归因。
4. **UI 刷新缺口**（§1.3、§5.7）：`UserPrivilegeService` 只在启动刷新一次，`DashboardViewModel` 的推送依赖 `PropertyChanged`；
   `MainWindow` 的处理器不监听 `IsAdministrator`。凡是「运行中权限状态变化」的场景都不会自动纠正。
5. **文案遮蔽**（§5.2、§6.4）：电源页 L159 与进程优先级页的固定 Toast 会把真实原因盖掉。

**目标 B：「打开/执行失败时完全没有错误信息」**

平台/权限层的三个具体缺口：

1. `ProcessPriorityPage.CheckAdminPrivileges()`（`:104–107`）——有提示但与真实原因无关；真实阻断在
   `ProcessPriorityPage.xaml.cs:111–115`（`AddGameButton_Click` 的 `IsIfeoBlocked` 分支）。
2. `#if false` 与「直接返回阻断」的路径（`RegistryPriorityService.SetProcessPriority:41–45`）——
   调用方拿到的是 `OperationResult.Failed("IFEO persistent priority is blocked: …")`，
   是否被 UI 呈现取决于各页面的 `OperationResult` 处理（不在本层）。
3. 权限原因在**评估通过时不存在**：`CompatibilityChecker.ProbeAsync` 返回 `null`（`:139–142`）⇒
   `CapabilityAssessment.Details` 无权限侧上下文；执行期失败只能依赖
   `OptimizationResultMessageFormatter`（`:22–27`）的关键词匹配来决定是否说「需要管理员」。

> 具体代码改动不属于本文范围（本文是地图）。上述条目均已在 §6/§8 给出文件与成员，可直接作为修改点定位。

---

## 11. 复核清单（可直接执行）

```powershell
# 1) 提权判定是否只剩一个实现（应只命中 WindowsElevation 的降级路径与文档）
Select-String -Path (Get-ChildItem -Recurse -File -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }).FullName `
  -Pattern 'IsInRole\(.*Administrator\)'

# 2) 提权权威实现的全部消费点
Select-String -Path (Get-ChildItem -Recurse -File -Filter *.cs).FullName -Pattern 'WindowsElevation\.'

# 3) 全仓 runas 数量（应为 1：RegistryInfoDialog）
Select-String -Path (Get-ChildItem -Recurse -File -Filter *.cs).FullName -Pattern 'runas'

# 4) 原生调用面清单
Select-String -Path (Get-ChildItem -Recurse -File -Filter *.cs).FullName -Pattern 'DllImport\("'

# 5) 能力门禁三个执行点
Select-String -Path 'GameOptimizer.Core\Services\Changes\ChangeOrchestrator.cs' -Pattern 'HasRequiredPermission|permission\.denied'

# 6) 开关可用性绑定
Select-String -Path 'GameOptimizer.Shell\Views\MasterModuleTemplate.xaml.cs' -Pattern 'CanToggle|HasCapabilityConstraint'
```

**契约测试（证明「主模块面不得使用全局权限猜测」的既有约束）**

| 测试 | 断言 |
| --- | --- |
| `GameOptimizer.Tests/Shell/MasterOperationCapabilityContractTests.cs:312–324` | `MasterSurface_UsesCanonicalManifestAndCapabilityGatesInsteadOfGlobalPermissionGuess`；L323 `Assert.DoesNotContain("PermissionChecker.IsRunningAsAdministrator", master, …)` |
| 同文件 `:155` | `ModuleCapabilityState.AdminRequired` 的状态映射 |
| `GameOptimizer.Tests/Compatibility/WindowsCompatibilityPolicyTests.cs:370,400` | `PermissionRequired` / `AlternativeAvailable` 映射 |
| `GameOptimizer.Tests/GraphicsSafety/GraphicsSafetyBoundaryTests.cs:7,32,51` | 直接用 `new GraphicsSafetyBoundary()`（生产未接线，见 §6.3） |
| `GameOptimizer.Tests/Changes/CoreCompositionWiringTests.cs:49–65` | Core 必须提供 fail-closed 的 `IChangeExecutionGate` |
| `GameOptimizer.Tests/Shell/CommandResultPresentationTests.cs:120` | "Administrator permission is required." → `CommandResultStatus.PermissionDenied` |
| `GameOptimizer.Tests/Snapshot/BackupRestoreSafetyClosureContractTests.cs:60,69` | 对 `IsRunningAsAdministrator` 的引用白名单 |

> 当前**没有任何测试**引用 `WindowsElevation`（`GameOptimizer.Tests` 内 0 命中），
> 也没有测试使用 `WindowsElevation.OverrideProbeForTesting` —— 新增判定逻辑缺少测试锚点。

---

## 附录 A · 相关本地化键

| 键 | 中文 | 位置 |
| --- | --- | --- |
| `Module.Capability.AdminRequired` | 需要管理员 | `LocalizationService.ZhCN.cs:1269` |
| `Module.Capability.AdminRequired.Reason` | 请以管理员身份重新启动后再试。 | `:1270` |
| `Guard.NeedAdmin` | 此操作需要管理员权限，请右键以管理员身份运行程序 | `:4037` |
| `Engine.AdminRequired` | 此操作需要管理员权限，请右键以管理员身份运行程序 | `:5209` |
| `Compatibility.AdminRequired` | （`CompatibilityChecker.cs:364` 使用） | `LocalizationService.ZhCN.cs`（Compatibility 段） |
| `Restore.NeedAdmin` | 需要管理员权限 | `:1537`；另 `:4062` / `:5702` 变体 |
| `Restore.NeedAdminToEnable` | 启用系统还原需要管理员权限。\n请右键点击程序，选择【以管理员身份运行】后重试。 | `:1601` |
| `Common.AdminRequired` | 需要管理员权限 | `:112` / `:631` |
| `Dashboard.Privilege.Admin` / `.Normal` | 管理员模式：可执行所有优化策略。 / 普通模式：部分策略需右键「以管理员身份运行」。 | `:5560–5561` |
| `Cleanup.AdminRequired` / `Cleanup.AdminRequiredMessage` | （`SystemCleanupViewModel.cs:357,362`） | `LocalizationService.ZhCN.cs` |
| `Backup.AdminRequired` / `Backup.AdminRequiredMessage` / `Backup.Solution.RunAsAdmin` | （`BackupManagerViewModel.cs:1924,1927`、`BackupDiagnosticService.cs:51`） | `LocalizationService.ZhCN.cs`（≈ `:4808`） |
| `Repair.AdminRequiredMessage` | （`RepairCenterViewModel.cs:705`） | `LocalizationService.ZhCN.cs` |
| `Solution.RunAsAdmin` / `Solution.RunAsAdminGeneric` | 以管理员身份运行 | `:4875` |
| `Diagnostic.NoAdminTitle` / `.NoAdminDesc` / `.NoAdminAction` | （`StartupDiagnosticService.cs:41–43`） | `LocalizationService.ZhCN.cs` |
| `Error.RequiresAdminTag` | （`SmartErrorDialog.xaml.cs:219`） | `LocalizationService.ZhCN.cs` |
| `CoreAccel.PermissionHint` / `.PermissionHintDesc` | （`CpuCoreManagementViewModel.cs:180–181`） | `LocalizationService.ZhCN.cs` |

## 附录 B · 命名/语义对照（避免再次误归因）

| 名称 | 真实含义 | 不是 |
| --- | --- | --- |
| `WindowsElevation.IsElevated` | 进程令牌已提权 | 「账户是管理员」 |
| `WindowsElevation.IsAdministratorAccount` | 账户在管理员组（令牌可能被筛选） | 「能写 HKLM」 |
| `CapabilityDescriptor.RequiresAdministrator` | capability 声明需要管理员 | 当前进程是否已提权 |
| `OptimizationToggleModel.RequiresAdmin` | operation 声明需要管理员（默认 `true`） | 当前进程是否已提权 |
| `SolutionStep.RequiresAdmin` | 该建议步骤需要管理员 | 失败原因是权限 |
| `CapabilityAssessment.HasRequiredPermission` | 探测得到的权限是否满足 | 提权判定本身（值来自探测链） |
| `OperationStatus.RequiresAdmin` | operation 元数据 | 运行时状态 |
| `SystemRestoreStatus.IsAdministrator` | 还原点探测时的提权快照 | 当前实时状态 |
| `MemoryCleanerService.IsAdministrator` | 转发（无消费者） | 内存清理的必要条件（清理已 fail-closed） |
| `GraphicsSafetyStatus.AdministratorRequired` | 图形边界的一种状态码（当前接线中不可达） | 真实提权结论 |
| `PermissionChecker.CanRestartAsAdmin()` | 仅检查 `MainModule != null` | 能否自提权重启 |
