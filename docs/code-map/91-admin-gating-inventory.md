# 91 · 管理员门禁清单（置灰 / 「需要管理员」提示 / 权限封装分支）

> **目的**：把「软件里所有会把功能置灰、或提示需要管理员权限」的位置一次性列成可核对的表，并逐条判定
> 「这一处读的是不是进程**真实**的提权状态」。
>
> **非目标**：本文件只做整理，不修复、不重构、不改任何源码；不重写 `01`–`05`、`90`、`README` 的内容。
>
> **快照基线**（2026-09-14 22:12 实测，仓库根 = `YHRJ - 副本`）：
> - `git rev-parse --short HEAD` = **`aa45545`**
> - `git status --porcelain` = **51 行**（`?? docs/` 未跟踪；工作树含未提交改动）
> - `docs/` 整个目录仍未纳入 git ⇒ 本文件是**新增未跟踪文件**，本轮不修改 `docs/code-map/` 下任何既有文件
>   （写入前实测：`04-platform-and-privilege.md` = 853 行，`91-admin-gating-inventory.md` 不存在）
> - 行号对应**本工作树当前内容**；改动源码后行号会漂移，届时按「文件 + 类型 + 方法名」重新定位
>
> **本轮复核与补录（2026-09-14 23:5x，同一工作树）**：`HEAD` 仍为 `aa45545`、`git status --porcelain` 仍为 51 行（实测）。
> 已把 §1–§3 每行的首个 `文件:行号` 逐条回读源码核对：140 个被引文件全部存在、无越界行号、抽读行内容与描述一致（§7 第 10 条的批量核对脚本）。
> 本轮新增：**§2.1**（写入工具/消息层返回的「请以管理员身份运行」文案）、**§2.2**（报告/对话框/默认文案补录）、
> **§3.4**（`RequiresAdmin` 声明位**全量枚举**，73 处）、§4 新增不确定条目、§7 新增第 7–10 条复核命令与 §7.1 实测事实。
> 本轮只改本文件，未改动 `docs/code-map/` 下任何既有文件，未改任何源码（写入后实测：`01/02/03/04/05/90/README` 的 mtime 与 size 均未变化）。

---

## 0. 口径与结论速览

### 0.1 「是否读取真实提权状态」三值口径

| 取值 | 判定标准 |
| --- | --- |
| **是** | 该点的进入条件 / 可用性最终来自 `GameOptimizer.Core/Services/WindowsElevation.cs`（读令牌 `TOKEN_ELEVATION` + `TOKEN_GROUPS`），或来自同一进程内由它派生的布尔值快照 |
| **否** | 与进程提权状态**无关**：声明位（`RequiresAdmin` / `RequiresAdministrator` 这类元数据）、无条件文案、恒真/恒假、只查其他系统状态 |
| **不确定** | 依赖运行时条件、上游异常被吞掉、关键词匹配、或读的是**过期快照**，静态上无法保证反映「当前真实提权状态」（逐条原因见 §4） |

### 0.2 三句话结论（本清单的直接产出）

1. **权威判定只有一个**，且确实读真实令牌：`WindowsElevation.Current / Probe()`（`WindowsElevation.cs:76–202`）。
   下游 `PermissionChecker`（`:17`）、`SystemGuard`（`:36`）都只是转发。
2. **「置灰」的主路径不是读令牌，而是读「声明位」再叠加一个提权布尔值**：
   `OptimizationToggleModel.RequiresAdmin`（默认 `true`，`:212`）→ `ResolveCapabilityState()`（`:688`）
   → `CanToggle`（`:293`）→ `MasterModuleTemplate.xaml.cs:1133` 的 `IsEnabled` 绑定。
   这里的提权布尔值本身是真的（`DashboardViewModel.cs:1148 / 3021 / 1184` 取自 `UserPrivilegeService`→`WindowsElevation`），
   但**是否要求管理员完全由声明位决定**，与进程实际能否写入无关。
3. **「提示需要管理员」的文案分三类**：读真实提权后分流（`SystemCleanupViewModel.cs:355` 等）、
   与提权无关的兜底/声明（`NetworkTestPage.xaml.cs:800`、`OperationTransparencyDialog.xaml.cs:341`）、
   关键词猜测（`CommandResultPresentation.cs:208–212`、`OptimizationResultMessageFormatter.cs:22–27`）。
   另外有 **19 个管理员文案键在本地化文件里定义、却没有对应显示点**（18 个全仓零消费，1 个只消费了重复定义中的一条；见 §6）。

---

## 1. UI 置灰（可用性由提权 / 权限声明驱动）

| 文件 | 类型 | 方法 / 成员 | 行号 | 调用方 | 是否读取真实提权状态 |
| --- | --- | --- | --- | --- | --- |
| `GameOptimizer.Shell/Models/OptimizationToggleModel.cs` | `OptimizationToggleModel` | `EvaluateCapabilities()` 的 `RequiresAdmin && !_isAdministrator` 分支（阻断原因写死中文） | 636–639 | `SyncOperationContract()` `:544–565`；`RefreshPrivilege()` `:576–580`；构造函数 `:120` | **是** |
| `GameOptimizer.Shell/Models/OptimizationToggleModel.cs` | `OptimizationToggleModel` | `ResolveCapabilityState()`（`PermissionRequired` → `AdminRequired` 映射） | 670、688 | `EvaluateCapabilities()` `:587` | **是** |
| `GameOptimizer.Shell/Models/OptimizationToggleModel.cs` | `OptimizationToggleModel` | `CanToggle` | 293 | `MasterModuleTemplate.xaml.cs:1133`（`IsEnabled` 绑定） | **是**（间接：汇总 `CanApply`/`CanRestore`） |
| `GameOptimizer.Shell/Models/OptimizationToggleModel.cs` | `OptimizationToggleModel` | `HasCapabilityConstraint` | 321 | `MasterModuleTemplate.xaml.cs:1021`（原因文本 `Visibility` 绑定） | **是**（间接） |
| `GameOptimizer.Shell/Models/OptimizationToggleModel.cs` | `OptimizationToggleModel` | `RequiresAdmin { get; init; } = true`（**默认 true**） | 212 | 值由 `DashboardViewModel.cs:2619` 从 `op.RequiresAdmin` 传入 | **否**（声明位，与提权无关） |
| `GameOptimizer.Shell/Views/MasterModuleTemplate.xaml.cs` | `MasterModuleTemplate` | 开关 `IsEnabled ← CanToggle`；`ToolTip ← CapabilityStatusReason` | 1133–1142 | 模块行构建 | **是**（间接） |
| `GameOptimizer.Shell/Views/MasterModuleTemplate.xaml.cs` | `MasterModuleTemplate` | 能力原因文本 `Visibility ← HasCapabilityConstraint` | 1021–1026 | 模块行构建 | **是**（间接） |
| `GameOptimizer.Shell/Views/MasterModuleTemplate.xaml.cs` | `MasterModuleTemplate` | `GetCapabilityColors()`（`AdminRequired` → 琥珀色徽标，仅视觉提示） | 1175–1182 | 模块行构建 | **是**（间接） |
| `GameOptimizer.Shell/ViewModels/DashboardViewModel.cs` | `DashboardViewModel` | `IsAdministrator` 属性 + 构造注入 | 805–818、1148 | `MainWindow.xaml.cs:279,286`；`MasterModuleTemplate` 经 toggle | **是** |
| `GameOptimizer.Shell/ViewModels/DashboardViewModel.cs` | `DashboardViewModel` | `OnPrivilegePropertyChanged()` → `toggle.RefreshPrivilege()` | 1173–1190（刷新在 1184） | `UserPrivilegeService.PropertyChanged`（`:1151–1155`） | **是**（但只在 `UserPrivilegeService.Refresh()` 被调用时才触发） |
| `GameOptimizer.Shell/ViewModels/DashboardViewModel.cs` | `DashboardViewModel` | `SyncModuleStatusAsyncCore()` → `SyncOperationContract(statuses[i], IsAdministrator)` | 3019–3022 | Engine 状态轮询 | **是** |
| `GameOptimizer.Shell/Services/UserPrivilegeService.cs` | `UserPrivilegeService` | 构造函数调用 `Refresh()`；`Refresh()` ← `WindowsElevation.Probe()` | 14–17、90–98 | DI 单例；`App.xaml.cs:568` 读取 | **是**（**唯一刷新点 = 构造期一次**，之后无自动刷新） |
| `GameOptimizer.Shell/Services/EsportsModeStateService.cs` | `EsportsModeStateService` | `BuildPlan()` 权限分支（`Operation.RequiresAdmin && !isAdministrator`） | 98–113、129–139 | `EsportsModePage.xaml.cs:116` 传入惰性委托 `() => _privilegeService.IsAdministrator` | **是**（值等价启动快照） |
| `GameOptimizer.Shell/ViewModels/AppxCleanupViewModel.cs` | `AppxCleanupViewModel` | `IsUnauthorized`（`:109`）/ `LoadAsync()` 置 `PageState = Unauthorized`（`:151`） | 109、149–156 | `AppxCleanupPage.xaml:231–236`（「需要管理员权限」面板，`:231` 绑 `IsUnauthorized`） | **是**（间接：`WindowsAppxRemovalOwner.cs:36–37、:323` 读真实提权） |
| `GameOptimizer.Shell/ViewModels/AppxCleanupViewModel.cs` | `AppxCleanupViewModel` | `CanRemove`（未授权态下为 false）；`UninstallSelectedCommand` 的 `CanExecute` | 122、188（`NotifyCanExecuteChanged` 在 285、342） | `AppxCleanupPage.xaml:258–261`（按钮绑 `UninstallSelectedCommand`，无显式 `IsEnabled` 绑定 ⇒ 置灰由命令 `CanExecute` 决定） | **是**（间接） |
| `GameOptimizer.Shell/ViewModels/RestorePointViewModel.cs` | `RestorePointViewModel` | `IsNotAdministrator` | 152 | `ProtectionStatusText` `:296–312`；`RestorePointPage.xaml:54,110,168` | **不确定**（读的是还原点**探测 DTO 快照** `SystemRestoreService.cs:182`） |
| `GameOptimizer.Shell/ViewModels/RestorePointViewModel.cs` | `RestorePointViewModel` | `CanCreateRestorePoint` | 157 | `RestorePointPage.xaml:90`（`IsEnabled` 绑定） | **不确定**（同快照 + 冷却时间 + writer 可用性） |
| `GameOptimizer.Shell/ViewModels/RestorePointViewModel.cs` | `RestorePointViewModel` | `BuildCreateAvailabilityText()`（`PermissionDenied` → 「需要管理员权限…」） | 690–717（709） | `RestorePointPage.xaml:88` | **不确定**（同上） |
| `GameOptimizer.Shell/Views/PowerPlanEditorPage.xaml.cs` | `PowerPlanEditorPage` | `ApplyCapabilityState()`：滑块/按钮 `IsEnabled ← canEditManagedPlan` | 146–166 | `RefreshPageAsync` / `LoadProfilePreflightAsync` `:126–144` | **不确定**（`CanMutate` 来自受管计划服务；该服务只在 `ManagedPowerPlanService.cs:551` 用提权决定**文案**） |
| `GameOptimizer.Shell/Views/PowerPlanEditorPage.xaml.cs` | `PowerPlanEditorPage` | `TxtPowerStatus`：`state.Environment.IsAdministrator ? "管理员权限已就绪" : "将请求管理员确认"` | 116–118 | 页面刷新 | **是** |
| `GameOptimizer.Shell/Views/ProcessPriorityPage.xaml.cs` | `ProcessPriorityPage` | `IsIfeoBlocked`（恒 `true`）+ `CheckAdminPrivileges()`（**方法名像权限检查，函数体不含任何提权判定**） | 16–17、104–107、111–114 | `OnLoaded` `:37–43`；`AddGameButton_Click` `:111` | **否**（固定阻断提示，与权限无关） |
| `GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.cs` | `CpuCoreManagementViewModel` | `CheckAdminPrivilege()` / `IsAdmin`；`InitializeAsync()` 里 `if (!IsAdmin)` 仅弹 Toast | 110、113–117、169–174 | 构造函数 / 页面初始化 | **是**（读 `WindowsElevation.IsElevated`；但 `IsAdmin` **未绑定任何控件**，只影响提示） |
| `GameOptimizer.Shell/Views/EsportsModePage.xaml.cs` | `EsportsModePage` | `UpdateActionAvailability()` / `SetActionButtonState()`（按钮置灰 + `Opacity=0.55`） | 437–458 | 页面初始化 / 硬件探测回调 | **否**（条件只有 `_isInitialized`/忙碌/硬件检测，不含提权） |
| `GameOptimizer.Shell/MainWindow.xaml.cs` | `MainWindow` | `UpdateLocalizedTexts()`：`AdminModeText` + `AdminStatusDot` 颜色 | 277–291 | 启动 / 语言切换 | **是** |
| `GameOptimizer.Shell/ViewModels/DashboardViewModel.cs` | `DashboardViewModel` | `PrivilegeHint`（管理员/普通两种文案） | 421 | `OverviewPage.xaml:119` | **是** |
| `GameOptimizer.Shell/App.xaml.cs` | `App` | 启动后读 `IUserPrivilegeService.IsAdministrator`，仅写 `Debug` + 诊断日志 | 568–574 | `OnStartup` | **是** |

> 说明：`GameOptimizer.Shell/Views/**/*.xaml` 全目录检索 `IsAdmin|IsAdministrator` **零命中** ⇒
> 提权状态**没有任何直接的 XAML 绑定**，全部经 ViewModel 属性（`CanToggle` / `IsUnauthorized` /
> `CanCreateRestorePoint` / `PrivilegeHint`）转手。

---

## 2. 提示「需要管理员权限」

| 文件 | 类型 | 方法 / 成员 | 行号 | 调用方 | 是否读取真实提权状态 |
| --- | --- | --- | --- | --- | --- |
| `GameOptimizer.Shell/ViewModels/SystemCleanupViewModel.cs` | `SystemCleanupViewModel` | `CleanAsync()`：`PermissionChecker.IsRunningAsAdministrator()` → `StatusText` + `InfoDialog` | 351–366 | `CleanCommand`（`RelayCommand`，`SystemCleanupPage`） | **是** |
| `GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs` | `BackupManagerViewModel` | `RestoreRegistryBackupAsync()` | 1915–1930 | `RestoreRegistryBackupCommand`（`BackupManagerPage.xaml:332` 等） | **是** |
| `GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs` | `RepairCenterViewModel` | 单项修复（**确认异常之后**才检查权限） | 697–709 | 修复项命令 | **是** |
| `GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs` | `RepairCenterViewModel` | `RunResetPowerDefaultsAsync()` | 944–960 | 电源默认值修复命令 | **是** |
| `GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs` | `RepairCenterViewModel` | `RunFullRestoreAsync()` | 1076–1092 | 全量恢复命令 | **是** |
| `GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.cs` | `CpuCoreManagementViewModel` | `InitializeAsync()` 非管理员 Toast（`CoreAccel.PermissionHint`） | 169–174 | 页面加载 | **是** |
| `GameOptimizer.Shell/Views/NetworkTestPage.xaml.cs` | `NetworkTestPage` | 重置网络 `catch` 兜底 → `Toast("Network.NeedAdmin")` | 797–800 | `BtnResetNetwork` 点击处理 | **否**（任何异常都提示「需要管理员」，不看真实原因） |
| `GameOptimizer.Shell/Services/StartupDiagnosticService.cs` | `StartupDiagnosticService` | `RunDiagnosticsAsync()` 第 1 项：`Diagnostic.NoAdmin*` | 34–46 | 启动 / 诊断页 | **是** |
| `GameOptimizer.Shell/Program.cs` | `Program` | `RunEnvironmentPreCheck()`：数据目录不可写 → 「请尝试以管理员身份运行」原生弹窗 | 94–101 | `Main` | **否**（写目录失败直接归因权限，未读提权） |
| `GameOptimizer.Shell/Startup/SplashWindow.xaml.cs` | `SplashWindow` | `Tips` 常量中的「建议以管理员身份运行…」 | 34 | 构造函数随机提示 `:43` | **否**（无条件静态文案） |
| `GameOptimizer.Shell/ViewModels/AppxCleanupViewModel.cs` | `AppxCleanupViewModel` | `LoadAsync()`：权限不足文案（**硬编码中文**，非本地化键） | 149–156（153） | `InitializeAsync()` `:125–130` | **是**（间接：`ReasonCode == "permission.administrator-required"` 来自平台真实判定） |
| `GameOptimizer.Core/Optimization/OptimizationEngine.cs` | `OptimizationEngine` | `ApplyAsync` 前置：`operation.RequiresAdmin && !SystemGuard.IsAdministrator` → `Engine.AdminRequired` | 633–640 | 各模块 Apply 流程 | **是** |
| `GameOptimizer.Core/Services/SystemGuard.cs` | `SystemGuard` | `CheckPrerequisites(..., requiresAdmin)` → `Guard.NeedAdmin` | 156–163 | **零调用方**（全仓无引用） | **是**（判定为真，但当前不可达） |
| `GameOptimizer.Shell/Services/EsportsModeStateService.cs` | `EsportsModeStateService` | `BuildPlan()`：读取提权异常 → 整计划 `PermissionDenied` 阻断 | 103–113；`129–138` | `EsportsModePage.xaml.cs:116` 的惰性委托 | **不确定**（`catch (Exception)` 把任何异常都转成「无法确认管理员权限」） |
| `GameOptimizer.Shell/Services/Baseline/FirstRunBaselineOrchestrator.cs` | `FirstRunBaselineOrchestrator` | `RunPreflightAsync()`：非管理员直接失败 | 421–429 | 首次安全基线流程 | **是** |
| `GameOptimizer.Shell/Views/OperationTransparencyDialog.xaml.cs` | `OperationTransparencyDialog` | `BuildAdvancedFields()`：「需要管理员」字段 ← `toggle.RequiresAdmin` | 341 | 操作详情弹窗 | **否**（声明位） |
| `GameOptimizer.Shell/Views/OperationTransparencyDialog.xaml.cs` | `OperationTransparencyDialog` | `BuildEffectiveCondition()`：追加「需要管理员权限。」 | 517–524 | 操作详情弹窗 | **否**（声明位） |
| `GameOptimizer.Shell/Views/OperationTransparencyDialog.xaml.cs` | `OperationTransparencyDialog` | 回滚命令说明「2) 使用管理员权限执行回滚命令。」 | 573 | 操作详情弹窗 | **否**（静态文案） |
| `GameOptimizer.Shell/Services/CommandResultPresentation.cs` | `CommandResultPresentation` | `PermissionDenied` → 「需要管理员权限才能继续。」 | 48 | `DisplayText` `:41` | **不确定**（状态由 `:199–212` 的异常类型 + 关键词匹配得出） |
| `GameOptimizer.Shell/Services/CommandResultPresentation.cs` | `CommandResultPresentation` | `ClassifyFailure()`：`UnauthorizedAccessException` 或文本含 `permission/administrator/权限/拒绝访问` → `PermissionDenied` | 199–212 | `DisplayText` 链路 | **不确定**（关键词匹配，不读提权） |
| `GameOptimizer.Shell/Models/OptimizationResultMessageFormatter.cs` | `OptimizationResultMessageFormatter` | `Format()`：关键词命中后按 `isAdministrator` 分流「已提权但被拒」/「需要管理员」 | 22–27 | `OperationResultItem.cs:31–35` | **不确定**（提权值真实，但**是否进入该分支取决于原始消息文本关键词**） |
| `GameOptimizer.Core/Utilities/OperationErrorAnalyzer.cs` | `OperationErrorAnalyzer` | `DiagnosePermissionIssue()`：`IsAdminMode = WindowsElevation.IsElevated` → 已提权走 `GetSystemDeniedMessage`，否则 `GetAdminRequiredMessage` | 50–86（判定 58、63–78、吞异常 82–85） | **零调用方**（全仓仅定义，见 §7 复核命令） | **是**（读真实提权；`catch` 时降级为 `(false, ex.Message)`） |
| `GameOptimizer.Core/Diagnostics/SolutionProvider.cs` | `SolutionProvider` | `GetPermissionSolutions()`：`if (!context.IsAdminMode)` → 「以管理员身份运行」步骤 | 35–48、56–57、362 | 诊断服务（`DiagnosticContext.IsAdminMode` 由 `OperationErrorAnalyzer.cs:26/44/58`、`SystemCompatibilityChecker.cs:314` 写入） | **是**（间接；同文件另有大量 `RequiresAdmin = true` 步骤属**声明位**） |
| `GameOptimizer.Core/Diagnostics/BackupDiagnosticService.cs` | `BackupDiagnosticService` | `UnauthorizedAccessException` → 「以管理员身份运行」步骤 | 40–54 | 备份诊断 | **否**（按异常类型归因，未读提权） |
| `GameOptimizer.Core/Diagnostics/DiagnosticPackageService.cs` | `DiagnosticPackageService` | 诊断包字段 `["IsAdministrator"] = SystemGuard.IsAdministrator` | 155 | 诊断导出 | **是** |
| `GameOptimizer.Core/Optimization/Operations/PowerCfgUtility.cs` | `PowerCfgUtility` | 诊断文本「• 管理员权限: 是/否 ⚠️」+ `GetSuggestion()` | 178、191–192 | 电源计划读取失败路径 | **是**（`PowerServiceDiagnostics.cs:30/82` 读 `WindowsElevation`） |
| `GameOptimizer.Core/Services/WindowsElevation.cs` | `WindowsElevation` | `DescribeMachineScopeUnavailable()`（按三态给出「缺权限 / 被策略拒绝」不同措辞） | 207 起 | `PermissionChecker` 等 | **是** |
| `GameOptimizer.Core/Services/PermissionChecker.cs` | `PermissionChecker` | `GetAdminRequiredMessage()`（区分「管理员组但令牌未提权」与「账户不在管理员组」） | 41–55 | `CleanAsync:355`、`RepairCenterViewModel:698/948/1080` 等分支 | **是** |
| `GameOptimizer.Core/Services/PermissionChecker.cs` | `PermissionChecker` | `GetSystemDeniedMessage()`（已提权仍被拒时的真实原因说明） | 61–72 | `OperationErrorAnalyzer.cs:66`（该调用点不可达） | **否**（纯文案，自身不读提权） |
| `GameOptimizer.Shell/Views/MemoryControlPanel.xaml.cs` | `MemoryControlPanel` | 内存清理结果 `Denied` → 「权限不足」 | 401–409 | 内存清理命令回执 | **不确定**（按 `MemoryCleaningResultKind` 结果码，非提权读取） |
| `GameOptimizer.Shell/Views/MemoryCleanDialog.xaml.cs` | `MemoryCleanDialog` | `memory.process-permission-denied` → 「Windows 不允许修改…（权限不足或进程受保护）」 | 605 | 逐进程清理结果 | **不确定**（结果码，含受保护进程等非提权原因） |
| `GameOptimizer.Shell/Views/AppxCleanupPage.xaml` | `AppxCleanupPage` | 权限不足面板「需要管理员权限」 | 231–236 | `IsUnauthorized` 绑定 | **是**（间接） |
| `GameOptimizer.Shell/Views/NetworkTestPage.xaml.cs` | `NetworkTestPage` | 测速失败 `PermissionDenied` → 「请求被策略拒绝…应用不会尝试提权」 | 412、550 | 测速结果分类 | **否**（网络策略/节点授权，非本进程提权） |

### 2.1 写入工具 / 消息层返回的「请以管理员身份运行」文案（本轮补录）

> 这一层不在 §2 主表里，但每一处都会把「请以管理员身份运行」送到用户眼前（返回值最终进结果面板/对话框）。
> **共同点：全部按「异常类型 / Win32 错误码 5」判定，没有一处读 `WindowsElevation`** ⇒ 判定列一律为「否」。
> 其中带「**零调用方**」标记的条目当前不可达（死代码），已单独标注。

| 文件 | 类型 | 方法 / 成员 | 行号 | 调用方 | 是否读取真实提权状态 |
| --- | --- | --- | --- | --- | --- |
| `GameOptimizer.Core/Services/SafeProcessHelper.cs` | `SafeProcessHelper` | `SetProcessorAffinity()` 的 `Win32Exception(5)` 分支 → 「无权限修改进程亲和性。请以管理员身份运行程序。」 | 199–202（202） | **零调用方**（全仓无 `SetProcessorAffinity(` 调用点；同文件 `SetPriorityClass` 另被 `GameOptimizer.Tests/ProcessTuning/LegacyProcessWriterClosureTests.cs:40` 断言为「不应被使用」） | **否**（Win32 错误码 5；且不可达） |
| 同上 | `SafeProcessHelper` | `SetPriorityClass()` 同分支 → 「无权限修改进程优先级…」 | 244–247（247） | **零调用方** | **否**（且不可达） |
| 同上 | `SafeProcessHelper` | `KillProcess()` 同分支 → 「无权限终止进程…」 | 435–438（438） | **零调用方**（`ProcessRunner.cs:121` 调的是自己的 `ProcessRunner.TryKillProcess`，定义在 `:159`） | **否**（且不可达） |
| 同上 | `SafeProcessHelper` | `BuildEnumerationFailureMessage()`：错误码 5 → 「无法读取进程列表：权限不足…」 | 466–473（471） | `EnumerateWindowProcesses` `:75`（`:90` 填入 `DiagnosticMessage`）→ `ProcessAffinityService.cs:239`、`CoreAcceleratorViewModel.cs:419`、`CpuCoreManagementViewModel.Acceleration.cs:74`；**用户可见**：`CoreAcceleratorViewModel.cs:455/460`、`CpuCoreManagementViewModel.Acceleration.cs:92/121` 以 `StatusText` + `ToastService.Warning` 显示 | **否**（Win32 错误码 5） |
| 同上 | `SafeProcessHelper` | `BuildEnumerationDiagnosticMessage()`：「建议管理员运行并检查安全软件拦截」 | 476–488（487） | `:157`–`:168` 填入 `DiagnosticMessage`，消费点同上一行（**用户可见**） | **否**（按跳过/失败计数 > 0 即提示，不读提权） |
| `GameOptimizer.Core/Services/SafeRegistryHelper.cs` | `SafeRegistryHelper` | `OpenKeyWritable()` 的 `SecurityException` / `UnauthorizedAccessException` → 「无权限修改注册表项…」「写入访问被拒绝…请以管理员身份运行程序。」 | 60–68（63、68） | `SetValue`（`:158`）/ `DeleteValue`（`:201`）；`SetValue` → `CpuAdvancedOperation.cs:469、493、509`；另 `RegistryPriorityService.cs:47、116`（位于 `#if false` 块 `:33–95`、`:102–167` 内） | **否**（异常类型） |
| 同上 | `SafeRegistryHelper` | `CreateOrOpenKey()` 同类分支 → 「无权限创建注册表项…」「创建访问被拒绝…」 | 91–99（94、99） | `SetValue`（`:158`）→ `CpuAdvancedOperation.cs:469、493、509`；另 `RegistryPriorityService.cs:56、65`（`#if false` 内） | **否**（异常类型） |
| 同上 | `SafeRegistryHelper` | `SetValue()` 写入失败 → 「无权限修改注册表值…」「写入访问被拒绝…」 | 179–187（182、187） | `CpuAdvancedOperation.cs:469、493、509` | **否**（异常类型） |
| 同上 | `SafeRegistryHelper` | `DeleteValue()` 删除失败 → 「无权限删除注册表值…」「删除访问被拒绝…」 | 220–228（223、228） | **零调用方**（全仓无 `SafeRegistryHelper.DeleteValue` 引用） | **否**（且不可达） |
| `GameOptimizer.Core/Services/SafeServiceHelper.cs` | `SafeServiceHelper` | `TryStartService()`（同步）错误码 5 → 「无权限启动服务…」 | 109–112（112） | `PowerServiceDiagnostics.cs:143`（**该返回值只被 `Debug.WriteLine` 消费**：`PowerServiceDiagnostics.cs:153`，用户看不到这行文案） | **否**（Win32 错误码 5） |
| 同上 | `SafeServiceHelper` | `TryStopService()`（同步）同分支 → 「无权限停止服务…」 | 166–169（169） | **零调用方**（全仓无 `SafeServiceHelper.TryStopService(` 调用；写入路径走异步版 `TryStopServiceAsync`） | **否**（且不可达） |
| 同上 | `SafeServiceHelper` | `TryStartServiceAsync()` 错误码 5 → 「无权限启动服务…」 | 241、258 | `ServiceToggleOperation.cs:311`、`MultiServiceToggleOperation.cs:403` | **否**（Win32 错误码 5） |
| 同上 | `SafeServiceHelper` | `TryStopServiceAsync()` 错误码 5 → 「无权限停止服务…」 | 278、297 | `ServiceToggleOperation.cs:302`、`MultiServiceToggleOperation.cs:394` | **否**（Win32 错误码 5） |
| `GameOptimizer.Core/Services/AdvancedTuning/AdvancedTuningService.cs` | `AdvancedTuningService` | `SetSystemResponsivenessAsync` / `SetWin32PrioritySeparationAsync` / `SetSvchostSplitThresholdAsync` / `Set8Dot3NameDisabledAsync` 的 `UnauthorizedAccessException` → 「权限不足，请以管理员身份运行」 | 39、77、109、141 | **零调用方**：`IAdvancedTuningService` 在 DI 组合根（`App.xaml.cs` `ConfigureServices`）与全仓均无注册/消费者 | **否**（异常类型；且不可达） |

### 2.2 报告 / 对话框 / 默认文案补录（本轮补录）

| 文件 | 类型 | 方法 / 成员 | 行号 | 调用方 | 是否读取真实提权状态 |
| --- | --- | --- | --- | --- | --- |
| `GameOptimizer.Core/Diagnostics/ErrorReportGenerator.cs` | `ErrorReportGenerator` | `ExportAsHtmlAsync()`：解决方案若 `solution.RequiresAdmin` → 徽标「需要管理员」 | 193–194 | `ExportAsync` `:33`（诊断报告 HTML 导出） | **否**（声明位 `SolutionStep.RequiresAdmin`） |
| `GameOptimizer.Core/Diagnostics/ErrorModels.cs` | `ErrorReport` | `ToText()`：`solution.RequiresAdmin` → 前缀 `[需要管理员] ` | 157–161（159） | `ErrorReportGenerator.cs:34`（文本报告导出） | **否**（声明位） |
| `GameOptimizer.Core/Services/OperationResult.cs` | `OperationResult` | `PermissionDenied()` 的**默认文案**「需要管理员权限」 | 39 | 各调用方（未传 message 时） | **否**（工厂方法，与提权无关） |
| `GameOptimizer.Shell/Views/OperationDetailCardDialog.cs` | `OperationDetailCardDialog` | `BuildContent()`：「需管理员: Yes/No」← `model.RequiresAdmin` | 121 | 操作详情卡纯文本视图 | **否**（声明位：`OperationDetailCardModel` / `OperationDetailCardFactory` 同名字段） |
| `GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs` | `FirstRunBaselineDialog` | `NormalizeFailureReason()`：失败原因含 `access denied / unauthorized / permission / 权限 / 管理员` → 「权限不足…请用管理员身份重新打开软件后再试。」 | 474–478（475、477） | 首次基线备份失败提示（对话框） | **不确定**（关键词匹配，见 §4） |
| `GameOptimizer.Shell/Views/ManagedPowerPlanCreateDialog.xaml.cs` | `ManagedPowerPlanCreateDialog` | `RefreshPreview()`：无兼容方案时提示「请刷新电源状态或检查管理员权限后重试。」 | 46–58（54） | 创建受管电源计划对话框 | **否**（触发条件是 `profile is null`，与提权无关） |
| `GameOptimizer.Core/Services/PowerOperationDiagnostics.cs` | `PowerOperationDiagnostics` | `AnalyzeStdErrOutput()`：`stderr` 含 `access`+`denied` → `UserMessage`「需要管理员权限才能修改电源设置」+「程序未以管理员身份运行」+ 解决步骤「以管理员身份运行」 | 142–166（150、154、163）、297–302（300） | `DiagnosePowerOperationFailureAsync()` `:33` 的 `:76` 分支 | **不确定**（按 `powercfg` 的 stderr 关键词判定，见 §4） |
| `GameOptimizer.Core/Optimization/Operations/CoreParkingDisableOperation.cs` | `CoreParkingDisableOperation` | `GetStatusAsync()`：读不到方案 → 「无法读取电源计划（可能需要管理员权限或系统电源服务未运行）」 | 49–52（51） | 模块状态刷新 | **不确定**（把提权与「服务未运行」并列猜测，不读提权） |
| 同上 | `CoreParkingDisableOperation` | `BuildSchemeErrorMessage()`：`!diagnostics.IsAdministrator` → 「• 需要管理员权限才能修改电源设置」；末尾固定建议「1. 以管理员身份重新运行本程序」 | 394–416（397–399、413） | 电源方案获取/创建失败路径 | **是**（`IsAdministrator` 来自 `PowerServiceStatus`，其源头是 `WindowsElevation`）；`413` 的建议行本身是无条件文案 |
| `GameOptimizer.Core/Optimization/Operations/NetshTcpGlobalOperation.cs` | `NetshTcpGlobalOperation` | `GetStatusAsync()`：读取失败兜底 → 「无法读取 TCP 参数。请确认已使用管理员权限运行。」；`ManualSteps` 引导「终端(管理员)」 | 105–110（110）、101 | 网络模块状态刷新 | **否**（固定兜底文案，不读提权） |
| `GameOptimizer.Core/Optimization/Operations/NetshTcpSupplementalOperation.cs` | `NetshTcpSupplementalOperation` | `GetStatusAsync()` 同款兜底 → 「无法读取 TCP Supplemental 参数…请确认已使用管理员权限运行。」；`ManualSteps` 引导「终端(管理员)」 | 78、87 | 网络模块状态刷新 | **否**（固定兜底文案） |

---

## 3. 权限检查封装与门禁分支

### 3.1 唯一权威实现与转发层

| 文件 | 类型 | 方法 / 成员 | 行号 | 调用方 | 是否读取真实提权状态 |
| --- | --- | --- | --- | --- | --- |
| `GameOptimizer.Core/Services/WindowsElevation.cs` | `WindowsElevation` | `Current` / `Level` / `IsElevated` / `IsAdministratorAccount` / `IsAdministratorAccountWithoutElevation` / `Refresh()` | 76–121 | 全仓唯一提权来源 | **是**（唯一权威） |
| `GameOptimizer.Core/Services/WindowsElevation.cs` | `WindowsElevation` | `Probe()` 的令牌 API 不可用回退：`WindowsPrincipal.IsInRole` | 148–161 | `Current` `:92` | **不确定**（回退路径不读 `TOKEN_ELEVATION`） |
| `GameOptimizer.Core/Services/WindowsElevation.cs` | `WindowsElevation` | `Probe()` 的 `catch` → `ElevationSnapshot.Unknown` | 198–201 | `Current` `:92` | **不确定**（异常被吞成 `Unknown`，调用方若只取 `IsElevated` 会得到 `false`） |
| `GameOptimizer.Core/Services/WindowsElevation.cs` | `ElevationSnapshot` | `CanWriteMachineScope => IsElevated` | 42 | — | **是** |
| `GameOptimizer.Core/Services/PermissionChecker.cs` | `PermissionChecker` | `IsRunningAsAdministrator()` / `IsAdministratorAccount()` / `CurrentElevationLevel` | 17、20、23 | 多处（§2 各行） | **是** |
| `GameOptimizer.Core/Services/PermissionChecker.cs` | `PermissionChecker` | `CheckOperationPermission(string)` | 28–35 | **零调用方** | **是**（判定为真，但当前不可达） |
| `GameOptimizer.Core/Services/PermissionChecker.cs` | `PermissionChecker` | `CanRestartAsAdmin()`（**伪判定**：只查 `MainModule != null`） | 77–88（82） | **零调用方** | **否**（不检测提权、也不重启进程） |
| `GameOptimizer.Core/Services/SystemGuard.cs` | `SystemGuard` | `IsAdministrator` / `IsAdministratorAccount` / `ElevationLevel` / `RefreshPrivilegeStatus()` | 36、39、42、47 | `OptimizationEngine.cs:633`、`DiagnosticPackageService.cs:155`、`PowerSettingIndexOperation.cs:81,141` | **是** |
| `GameOptimizer.Core/Services/ProcessPriority/RegistryPriorityService.cs` | `RegistryPriorityService` | `HasAdminPrivileges()` | 26（引用点 39、108） | 39 / 108 **都在 `#if false` 块内** ⇒ 当前无任何可达调用方 | **是**（转发 `WindowsElevation.IsElevated`，但不可达） |

### 3.2 typed change / 能力探测门禁

| 文件 | 类型 | 方法 / 成员 | 行号 | 调用方 | 是否读取真实提权状态 |
| --- | --- | --- | --- | --- | --- |
| `GameOptimizer.Core/Contracts/Changes/ChangeModels.cs` | `CapabilityDescriptor` | `RequiresAdministrator` 字段 | 81 | 各 `*ChangeCapability` 声明 | **否**（声明位） |
| `GameOptimizer.Core/Contracts/Changes/ChangeModels.cs` | `CapabilityAssessment` | `HasRequiredPermission` 字段 | 104–108 | `ChangeOrchestrator` `:94/249/545` | **是**（值来自平台探测链） |
| `GameOptimizer.Core/Services/CompatibilityChecker.cs` | `CompatibilityEnvironment` | `IsAdministrator` 字段 + 默认值 `_administratorOverride ?? WindowsElevation.IsElevated` | 48、641 | `ProbeAsync()` | **是**（`_administratorOverride` 为测试注入） |
| `GameOptimizer.Core/Services/CompatibilityChecker.cs` | `CompatibilityChecker` | `ProbeAsync()` 把 `descriptor.RequiresAdministrator` 传入规则求值 | 135 | `ChangeOrchestrator.cs:131` | **是**（声明 + 真实环境） |
| `GameOptimizer.Core/Services/CompatibilityChecker.cs` | `CompatibilityChecker` | 权限阻断：`requiresAdmin && !environment.IsAdministrator` → `permission.administrator-required` | 362–366 | 各 Operation `GetStatusAsync()` | **是** |
| `GameOptimizer.Core/Services/CompatibilityChecker.cs` | `CompatibilityChecker` | `CompatibilityLevel.RequiresAdmin` → `OperationSupportState.PermissionRequired` | 181 | Engine 状态 → `OptimizationToggleModel.SyncOperationContract:548` | **是**（间接） |
| `GameOptimizer.Core/Services/Changes/ChangeOrchestrator.cs` | `ChangeOrchestrator` | `PrepareAsync`：[`!assessment.HasRequiredPermission` → `permission.denied`] | 88–97 | 写入准备流程 | **是**（间接） |
| `GameOptimizer.Core/Services/Changes/ChangeOrchestrator.cs` | `ChangeOrchestrator` | `ExecuteAsync`：同上，写 `ChangeOutcome.Denied` 回执 | 249–251 | 写入执行流程 | **是**（间接） |
| `GameOptimizer.Core/Services/Changes/ChangeOrchestrator.cs` | `ChangeOrchestrator` | `RestoreAsync`：同上 | 545–547 | 回滚流程 | **是**（间接） |
| `GameOptimizer.Core/Services/Changes/ChangeOrchestrator.cs` | `ChangeOrchestrator` | `plan.RequiresAdministrator != descriptor.RequiresAdministrator` 一致性校验 | 789 | 计划校验 | **否**（元数据一致性，非提权） |
| `GameOptimizer.Shell/Services/Baseline/BaselineChangeExecutionGate.cs` | `BaselineChangeExecutionGate` | `EnsureCanExecuteAsync()`（**基线门禁，不含任何提权判定**） | 19–44 | `ChangeOrchestrator` 前置门控 | **否** |
| `GameOptimizer.Core/Services/DeviceRepair/DeviceStateChangeCapability.cs` | `DeviceStateChangeCapability` | `assessment.ReasonCode == "permission.administrator-required"` → `PermissionRequired` | 79 | Engine 状态 → toggle | **是**（间接） |
| `GameOptimizer.Core/Optimization/Operations/OrchestratedDnsCacheFlushOperation.cs` | `OrchestratedDnsCacheFlushOperation` | `!status.IsAdministrator` → `SupportState: PermissionRequired` | 88–93 | Engine 状态 → toggle 置灰 | **是**（间接） |
| `GameOptimizer.Core/Optimization/Operations/OrchestratedHostsBackupOperation.cs` | `OrchestratedHostsBackupOperation` | 权限不足 → `PermissionRequired` | 65 | Engine 状态 → toggle | **是**（间接） |
| `GameOptimizer.Core/Optimization/Operations/OrchestratedRegistryDwordOperation.cs` | `OrchestratedRegistryDwordOperation` | `reasonCode` 以 `permission.` 开头 → `PermissionRequired` | 88 | Engine 状态 → toggle | **是**（间接） |
| `GameOptimizer.Core/Services/Memory/MemoryCleaningCapability.cs` | `MemoryCleaningCapability` | `RequiresAdministrator: capability == StandbyListPurge` | 29 | `ChangeOrchestrator`（执行侧另看平台探测） | **否**（声明位） |
| `GameOptimizer.Core/Services/Appx/AppxRemovalChangeCapability.cs` | `AppxRemovalChangeCapability` | `RequiresAdministrator: true` | 47 | `ChangeOrchestrator` | **否**（声明位） |
| `GameOptimizer.Core/Services/Bcd/BcdEditChangeCapability.cs` | `BcdEditChangeCapability` | `RequiresAdministrator: true` | 68 | 同上 | **否** |
| `GameOptimizer.Core/Services/DeviceRepair/DeviceStateChangeCapability.cs` | `DeviceStateChangeCapability` | `RequiresAdministrator: true` | 59 | 同上 | **否** |
| `GameOptimizer.Core/Services/Memory/MmAgentChangeCapability.cs` | `MmAgentChangeCapability` | `RequiresAdministrator: true` | 35 | 同上 | **否** |
| `GameOptimizer.Core/Services/ScheduledTasks/ScheduledTaskChangeCapability.cs` | `ScheduledTaskChangeCapability` | `RequiresAdministrator: true` | 36 | 同上 | **否** |
| `GameOptimizer.Core/Services/SystemServices/ServiceChangeCapability.cs` | `ServiceChangeCapability` | `RequiresAdministrator: true` | 37 | 同上 | **否** |
| `GameOptimizer.Core/Services/Repair/UnavailableRepairPlatformCapability.cs` | `UnavailableRepairPlatformCapability` | `RequiresAdministrator: true` | 21 | 同上 | **否** |
| `GameOptimizer.Core/Services/NetworkReset/NetworkResetOwner.cs` | `NetworkResetOwner` | `RequiresAdministrator: true`；`!status.IsAdministrator` 分支；`!assessment.IsAdministrator` | 50、83、185、564 | 网络重置流程 | **是**（探测分支读真实提权） |
| `GameOptimizer.Core/Services/NetworkReset/NetworkResetPolicy.cs` | `NetworkResetPolicy` | `plan.ChangePlan.RequiresAdministrator` 参与策略判定 | 25、72、236 | 网络重置策略 | **否**（声明位） |
| `GameOptimizer.Core/Services/Startup/StartupTaskOwners.cs` | `StartupTaskOwners` | `RequiresAdministrator` 字段与指纹 | 44、227 | 启动项计划 | **否**（声明位） |
| `GameOptimizer.Core/Services/ProcessTuning/ProcessTuningCapability.cs` | `ProcessTuningCapability` | `RequiresAdministrator: false` | 28 | 同上 | **否** |
| `GameOptimizer.Core/Services/Snapshot/SnapshotService.cs` | `SnapshotService` | `RequiresAdministrator: false` | 342 | 同上 | **否** |
| `GameOptimizer.Core/Optimization/Operations/AccessibilityChangeCapability.cs` | `AccessibilityChangeCapability` | `RequiresAdministrator: false`（同处 `:59` 定义类型） | 83 | 同上 | **否** |
| `GameOptimizer.Core/GraphicsSafety/GraphicsSafetyBoundary.cs` | `GraphicsSafetyBoundary` | `request.RequiresAdministrator && !environment.IsAdministrator` 分支（**当前接线不可达**） | 59 | 图形安全边界 | **否**（不可达；真实原因是 `PolicyDenied`） |
| `GameOptimizer.Core/GraphicsSafety/GraphicsSafetyModels.cs` | `GraphicsSafetyRequest` | `RequiresAdministrator = true` 默认值 | 48 | 图形安全边界 | **否**（声明位） |
| `GameOptimizer.Core/FileShellSafety/FailClosedFileShellOptimizationOperation.cs` | `FailClosedFileShellOptimizationOperation` | `RequiresAdmin => true`（fail-closed） | 36 | Engine 目录 → toggle 置灰 | **否**（声明位；与提权无关） |
| `GameOptimizer.Core/Optimization/Operations/InputLatencyOperation.cs` | 各 operation | `RequiresAdmin => true`（412、530）／`false`（25、118、211、316） | 25、118、211、316、412、530 | Engine 目录 → `OptimizationToggleModel.RequiresAdmin:212` | **否**（声明位） |
| `GameOptimizer.Core/Optimization/Operations/MemoryConfigOperation.cs` | 各 operation | `RequiresAdmin => true` | 25、204、337、380、441、484 | 同上 | **否**（声明位） |

### 3.3 平台探测层（`permission.administrator-required` 的产生处）

> 这些位置的**判定值都是真的**（统一转发 `WindowsElevation.IsElevated`）；它们决定能力探测结果，
> 经 `CompatibilityChecker` → `OperationStatus.SupportState` 回灌到 §1 的开关置灰。

| 文件 | 类型 | 方法 / 成员 | 行号 | 调用方 | 是否读取真实提权状态 |
| --- | --- | --- | --- | --- | --- |
| `GameOptimizer.Infrastructure.Windows/Services/WindowsAppxRemovalOwner.cs` | `WindowsAppxRemovalOwner` | `ProbeAsync()` / `IsCurrentUserAdministrator()` | 36–37、323 | Appx 能力探测 → `AppxCleanupViewModel:152` | **是** |
| `GameOptimizer.Infrastructure.Windows/Services/WindowsServiceAccessor.cs` | `WindowsServiceAccessor` | `IsAdministrator()` 参与 `applicable` / `permission.administrator-required` | 31、38、314 | 服务能力探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/Services/WindowsScheduledTaskAccessor.cs` | `WindowsScheduledTaskAccessor` | 同上 | 22、28、142 | 计划任务探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/Services/WindowsBcdEditAccessor.cs` | `WindowsBcdEditAccessor` | 同上 | 19、40、147 | BCD 探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/Services/WindowsDeviceStateAccessor.cs` | `WindowsDeviceStateAccessor` | 同上 | 20、23、103 | 设备状态探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/Services/WindowsMmAgentAccessor.cs` | `WindowsMmAgentAccessor` | 同上 | 17、20、74 | 内存管理代理探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/Services/WindowsHostsBackupOwner.cs` | `WindowsHostsBackupOwner` | 同上 | 22、25、163 | Hosts 备份探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/NetworkReset/WindowsNetworkResetPlatform.cs` | `WindowsNetworkResetPlatform` | `administrator` → `platform.ready` / `permission.administrator-required` | 26、30 | 网络重置探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/NetworkDiagnostics/WindowsNetworkAdapterDhcpPlatform.cs` | `WindowsNetworkAdapterDhcpPlatform` | `isAdministrator` 进入环境 DTO | 31 | DHCP 探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/SystemRestore/WindowsSystemRestorePointPlatform.cs` | `WindowsSystemRestorePointPlatform` | `SystemRestorePointProbe(..., IsAdministrator(), ...)` | 44、72、238 | 还原点探测 → `SystemRestoreService.cs:182` | **是** |
| `GameOptimizer.Infrastructure.Windows/PowerManagement/WindowsPowerPlatform.cs` | `WindowsPowerPlatform` | 环境 DTO 的 `administrator` 位 | 30 | 电源环境探测 | **是** |
| `GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSchemeChangeCapability.cs` | `PowerSchemeChangeCapability` | `IsAdministrator` 字段 + `!facts.IsAdministrator` 阻断 + `RequiresAdministrator: true` | 27、52、65、266、323–328、460 | 电源方案能力 | **是**（探测）／`:266` **否**（声明） |
| `GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSettingChangeCapability.cs` | `PowerSettingChangeCapability` | `!environment.IsAdministrator` 阻断 + `RequiresAdministrator: true` | 54、67、273、305–310、460 | 电源设置能力 | **是**（探测）／`:273` **否**（声明） |
| `GameOptimizer.Infrastructure.Windows/PowerManagement/ManagedPowerPlanService.cs` | `ManagedPowerPlanService` | `!facts.IsAdministrator` → 状态文案「创建或修改受管电源计划需要管理员权限。」 | 551 | `PowerPlanEditorPage` 受管计划状态 | **是**（仅文案分支） |
| `GameOptimizer.Infrastructure.Windows/Registry/PerInterfaceRegistryCapability.cs` | `PerInterfaceRegistryCapability` | `isAdministrator` → `applicable` / `permission.administrator-required`；`RequiresAdministrator: true` | 43、74–78、401 | 注册表能力 | **是**（探测）／`:43` **否**（声明） |
| `GameOptimizer.Infrastructure.Windows/Registry/RegistryStringCapability.cs` | `RegistryStringCapability` | 同上（`RequiresAdministrator: isMachine`） | 244、277–281、623 | 注册表能力 | **是**（探测）／`:244` **否**（声明） |
| `GameOptimizer.Infrastructure.Windows/Registry/RegistryDwordCapability.cs` | `RegistryDwordCapability` | 同上 | 49、64、83、109、143–147、324 | 注册表能力 | **是**（探测）／`:109` **否**（声明） |
| `GameOptimizer.Infrastructure.Windows/Registry/RegistryValueSetCapability.cs` | `RegistryValueSetCapability` | 同上 | 52、85–89、364 | 注册表能力 | **是**（探测）／`:52` **否**（声明） |
| `GameOptimizer.Infrastructure.Windows/Registry/RegistryValueDeleteCapability.cs` | `RegistryValueDeleteCapability` | `RequiresAdministrator: false` | 53 | 注册表删除能力 | **否**（声明位） |
| `GameOptimizer.Core/SystemRestore/SystemRestorePointOwner.cs` | `SystemRestorePointOwner` | `!probe.IsAdministrator` 阻断；`RequiresAdministrator: true` | 104、548、630、646、710、743 | 还原点写入流程 | **是**（探测）／`:104,548` **否**（声明） |
| `GameOptimizer.Core/SystemRestore/SystemRestorePointModels.cs` | `SystemRestorePointModels` | `!probe.IsAdministrator` 判定；DTO 字段 | 112、205、250、263 | 还原点能力评估 | **是** |
| `GameOptimizer.Core/PowerManagement/PowerManagementOwner.cs` | `PowerManagementOwner` | `!environment.IsAdministrator` → `PermissionDenied`；漂移判定 | 283、323 | 电源写入流程 | **是** |
| `GameOptimizer.Core/Security/SecurityProtectionOwner.cs` | `SecurityProtectionOwner` | `!environment.IsAdministrator` 分支 | 58 | 安全防护流程 | **是** |
| `GameOptimizer.Core/Services/PowerServiceDiagnostics.cs` | `PowerServiceDiagnostics` | `IsAdministrator()` / `!status.IsAdministrator` | 16、30、48、82 | `PowerCfgUtility` / 电源诊断 | **是** |
| `GameOptimizer.Core/Services/SystemCompatibilityChecker.cs` | `SystemCompatibilityChecker` | `IsAdminMode = WindowsElevation.IsElevated`；`IsAdministrator()` | 314、343 | 兼容性诊断 | **是** |
| `GameOptimizer.Core/Optimization/Operations/CoreParkingDisableOperation.cs` | `CoreParkingDisableOperation` | `!diagnostics.IsAdministrator` 分支 | 397 | 核心停车操作 | **是** |
| `GameOptimizer.Core/Optimization/Operations/PowerSettingIndexOperation.cs` | `PowerSettingIndexOperation` | `!SystemGuard.IsAdministrator`；`IsAdminMode = SystemGuard.IsAdministrator` | 81、141 | 电源设置索引操作 | **是** |
| `GameOptimizer.Shell/Services/SystemRestorePointWriterEntryPoint.cs` | `SystemRestorePointWriterEntryPoint` | `!native.IsAdministrator` → 写入失败；`IsAdministrator => WindowsElevation.IsElevated` | 59、178、187 | 独立还原点 writer 进程 | **是** |
| `GameOptimizer.Shell/Views/RegistryInfoDialog.xaml.cs` | `RegistryInfoDialog` | `OpenRegedit_Click()`：`Verb = "runas"`（**全仓唯一显式 UAC 提升点**） | 304–309（308） | 「打开注册表编辑器」按钮 | **否**（无条件请求提升，不预读提权） |

### 3.4 `RequiresAdmin` 声明位全量枚举（本轮补录，置灰的**声明层**）

> §1 的置灰链是：**声明位** → `OptimizationToggleModel.RequiresAdmin`（`:212`，默认 `true`）→ `ResolveCapabilityState()`（`:688`）→ `CanToggle`（`:293`）→ `MasterModuleTemplate.xaml.cs:1133` 的 `IsEnabled`。
> 因此「所有会被置灰的功能」在**声明层**的完整集合就是下表。**统一调用方**：`BuiltInOptimizationOperationProvider` 构造各 operation → `IOptimizationOperation.RequiresAdmin` → `OptimizationEngine.ApplyAsync:633`（`RequiresAdmin && !SystemGuard.IsAdministrator`）与 `DashboardViewModel.cs:2619`（写入 `OptimizationToggleModel.RequiresAdmin`）。
> 判定列对全部行恒为「**否**（声明位，与进程实际提权无关）」：声明为 `true` 的功能即使进程已提权也仍按声明位走管理员门禁，声明为 `false` 的功能即使未提权也不会因此被置灰。
>
> 复核命令（§7 第 5 条）实测：全仓 `RequiresAdmin =>` 共 **74** 处 = 下表 **73** 处声明 + `CompatibilityChecker.cs:181` 的枚举映射 1 处（已在 §3.2 收录）。

| 文件 | 类型 | 方法 / 成员 | 行号 | 调用方 | 是否读取真实提权状态 |
| --- | --- | --- | --- | --- | --- |
| `GameOptimizer.Core/Optimization/IOptimizationOperation.cs` | `IOptimizationOperation`（**接口默认实现**） | `RequiresAdmin => true`（**未覆写即等于要求管理员**） | 46 | 所有未覆写该成员的 operation | **否**（声明位） |
| `GameOptimizer.Core/FileShellSafety/FailClosedFileShellOptimizationOperation.cs` | `FailClosedFileShellOptimizationOperation` | `=> true` | 36 | 统一链 | **否** |
| `GameOptimizer.Core/GraphicsSafety/FailClosedGraphicsOptimizationOperation.cs` | `FailClosedGraphicsOptimizationOperation` | `=> true` | 37 | 统一链 | **否** |
| `GameOptimizer.Core/PowerManagement/FailClosedPowerOptimizationOperation.cs` | `FailClosedPowerOptimizationOperation` | `=> true` | 43 | 统一链 | **否** |
| `GameOptimizer.Core/Security/FailClosedSecurityOptimizationOperation.cs` | `FailClosedSecurityOptimizationOperation` | `=> true` | 36 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/AccessibilityToggleOperation.cs` | `AccessibilityToggleOperation`（3 个内部类） | `=> false` | 470、512、554 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/AdvancedPowerPlanOperation.cs` | `AdvancedPowerPlanOperation` | `virtual => true`（子类可覆写） | 96 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/AmdOptimizationOperation.cs` | `AmdOptimizationOperation`（2 个内部类） | `=> true` | 38、125 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/AudioOptimizationOperation.cs` | `AudioOptimizationOperation`（7 个内部类） | `=> false` / `=> true` | 220、409、793＝`false`；319、494、594、680＝`true` | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/BcdSettingOperation.cs` | `BcdSettingOperation` | `=> true` | 49 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/CoreParkingDisableOperation.cs` | `CoreParkingDisableOperation` | `=> true` | 42 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/CpuAdvancedOperation.cs` | `CpuAdvancedOperation`（3 个内部类） | `=> true` | 38、379、450 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/DeviceToggleOperation.cs` | `DeviceToggleOperation` | `=> true` | 37 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/DirectoryCleanupOperation.cs` | `DirectoryCleanupOperation` | `=> _inner.RequiresAdmin`（转发） | 45 | 统一链 | **否**（转发声明位） |
| `GameOptimizer.Core/Optimization/Operations/GpuAdvancedOperation.cs` | `GpuAdvancedOperation` | `=> _boundary.RequiresAdmin` | 179 | 统一链 | **否**（转发声明位） |
| `GameOptimizer.Core/Optimization/Operations/GpuVendorOperation.cs` | `GpuVendorOperation`（2 个内部类） | `=> _boundary.RequiresAdmin` | 24、53 | 统一链 | **否**（转发声明位） |
| `GameOptimizer.Core/Optimization/Operations/InputLatencyOperation.cs` | `InputLatencyOperation`（6 个内部类） | `=> false` / `=> true` | 25、118、211、316＝`false`；412、530＝`true` | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/IntelOptimizationOperation.cs` | `IntelOptimizationOperation` | `=> true` | 25 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/KeyboardResponseOperation.cs` | `KeyboardResponseOperation` | `=> false` | 37 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/MemoryConfigOperation.cs` | `MemoryConfigOperation`（7 个内部类） | `=> true` | 25、204、337、380、441、484、527 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/MouseAccelerationOperation.cs` | `MouseAccelerationOperation` | `=> false` | 40 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/MultiRegistryValueOperation.cs` | `MultiRegistryValueOperation` | 按定义是否含 `HKLM` 计算 | 58 | 统一链 | **否**（声明位，由目标 hive 推导） |
| `GameOptimizer.Core/Optimization/Operations/MultiServiceToggleOperation.cs` | `MultiServiceToggleOperation` | `=> true` | 52 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/NetshTcpGlobalOperation.cs` | `NetshTcpGlobalOperation` | `=> true` | 102 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/NetshTcpSupplementalOperation.cs` | `NetshTcpSupplementalOperation` | `=> true` | 79 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/NetworkAdapterAdvancedPropertyOperation.cs` | `NetworkAdapterAdvancedPropertyOperation` | `=> true` | 67 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/NetworkAdvancedOperation.cs` | `NetworkAdvancedOperation`（6 个内部类） | `=> true` | 39、91、143、195、240、533 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/NetworkInterfaceRegistryOperation.cs` | `NetworkInterfaceRegistryOperation` | `=> true` | 56 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/OrchestratedServiceOptimizationOperation.cs` | `OrchestratedServiceOptimizationOperation` | `=> true` | 71 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/PolicyDeniedOptimizationOperation.cs` | `PolicyDeniedOptimizationOperation` | `=> true` | 31 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/PowerPlanOperation.cs` | `PowerPlanOperation` | `=> true` | 38 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/PowerSettingIndexOperation.cs` | `PowerSettingIndexOperation` | `=> true` | 47 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/RegistryToggleOperation.cs` | `RegistryToggleOperation` | 按 hive 是否 `HKLM` 计算 | 65 | 统一链 | **否**（声明位） |
| `GameOptimizer.Core/Optimization/Operations/ScheduledTaskOperation.cs` | `ScheduledTaskOperation` | `=> true` | 36 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/SecurityMitigationOperation.cs` | `SecurityMitigationOperation` | `=> _inner.RequiresAdmin` | 38 | 统一链 | **否**（转发声明位） |
| `GameOptimizer.Core/Optimization/Operations/StorageOptimizationOperation.cs` | `StorageOptimizationOperation`（4 个内部类） | `=> true` | 22、51、79、115 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/TimerResolutionOperation.cs` | `TimerResolutionOperation` | `=> false` | 38 | 统一链 | **否** |
| `GameOptimizer.Core/Optimization/Operations/WindowsFeaturesOperation.cs` | `WindowsFeaturesOperation`（5 个内部类） | `=> false` / `=> true` | 35、89、142、193＝`false`；245＝`true` | 统一链 | **否** |

**参数化声明位（同一层，写在 provider 里）**：`GameOptimizer.Core/Optimization/Providers/BuiltInOptimizationOperationProvider.cs` 有 **21** 处 `requiresAdministrator: true` 命名实参
（行号：341、357、476、647、735、820、850、1221、1281、1408、1685、1729、1804、1891、1944、1974、2202、2409、2726、2742、2758），
分别传入 `OrchestratedRegistryDwordOperation`（7）、`OrchestratedRegistryStringOperation`（5）、`OrchestratedRegistryValueSetOperation`（8）、`MultiServiceCompositeOperation`（1）的构造参数，
最终由 `OrchestratedRegistryDwordOperation.cs:44` 的 `RequiresAdmin = requiresAdministrator;` 落成声明位；另有 `NetworkResetOwner.cs:83`、`AppxRemovalChangeCapability.cs:47`、`BcdEditChangeCapability.cs:68` 等 capability 层声明（见 §3.2）。
同一文件里的「需管理员」字样还有 4 处是**描述/备注文案**（`:242`、`:1450`、`:1464`、`:1584`，另 `:1467`、`:1483` 为注释），不参与判定。

---

## 4. 标为「不确定」的条目与原因

| 位置 | 不确定的原因（可复核） |
| --- | --- |
| `RestorePointViewModel.cs:152 / 157 / 690–717`（`IsNotAdministrator` / `CanCreateRestorePoint` / `BuildCreateAvailabilityText`） | `IsNotAdministrator => SystemRestoreStatus is { IsAdministrator: false }`（`:152`）读的是**探测阶段的 DTO 快照**（`SystemRestoreService.cs:182` `IsAdministrator = result.Probe?.IsAdministrator == true`），不是当前进程令牌：探测早于状态变化时该值即为过期。**并且** `SystemRestoreStatus == null`（探测未返回）时 `IsNotAdministrator` 为 `false`，`ProtectionStatusText`（`:304–309`）落到「系统保护状态无法权威确认」而**不是**权限结论 —— 同一属性在两种失败态下给出不同答案 |
| `PowerPlanEditorPage.xaml.cs:146–166`（滑块/按钮置灰） | `canEditManagedPlan` 取决于 `_managedOverview.CanMutate` 与所选的受管方案；提权只影响 `ManagedPowerPlanService.cs:551` 的**状态文案**，因此「灰掉」的直接原因可能是「没选方案/没有受管方案」而非权限 |
| `EsportsModeStateService.cs:103–113` | `catch (Exception)` 把任何异常（含探测异常、DI 异常）统一转成 `PermissionDenied` + 「无法确认管理员权限」，真实原因被吞掉 |
| `CommandResultPresentation.cs:199–212` + `:48` | `PermissionDenied` 由**关键词匹配**（`permission`/`administrator`/`权限`/`拒绝访问`）或 `UnauthorizedAccessException` 得出，不读取提权状态；上游消息把这些词用于非权限原因时会误报 |
| `OptimizationResultMessageFormatter.cs:22–27` | `isAdministrator` 参数是真实的（`OperationResultItem.cs:31–35` 传 `PermissionChecker.IsRunningAsAdministrator()`），但**是否进入该分支**取决于原始文本是否含 `administrator/管理员权限/access denied` ⇒ 结果取决于上游消息措辞 |
| `MemoryControlPanel.xaml.cs:401–409`、`MemoryCleanDialog.xaml.cs:605` | 文案由 `MemoryCleaningResultKind.Denied` / `memory.process-permission-denied` 结果码决定；同类结果也包含「受保护进程」等非提权原因 |
| `WindowsElevation.cs:148–161`（`IsInRole` 回退） | 仅当 `TOKEN_ELEVATION` 与 `TOKEN_GROUPS` **两个令牌 API 都失败**时才走；该路径不读真实提权位，只读组角色 |
| `WindowsElevation.cs:198–201`（`Probe` 的 catch） | 读取令牌抛异常时返回 `ElevationLevel.Unknown` 且 **被吞掉**；消费方若只取 `IsElevated`（`false`）会得到「非管理员」的结论 |
| `OperationErrorAnalyzer.cs:82–85`（`catch`） | 诊断过程异常时降级为 `(false, ex.Message)`——即「不需要管理员」，同样吞掉真实原因（且该 API 当前零调用方） |
| `NetworkTestPage.xaml.cs:797–800` | 重置网络失败一律提示「需要管理员权限」，不看异常类型（超时、策略拒绝、服务不可用都可能命中） |
| `UserPrivilegeService.cs:90–98`（唯一刷新点） | 真实，但**只在构造时刷新一次**；`ElevationLevel` 变化没有其它触发点 ⇒ UI 上的提权结论是启动期快照（`90-symptom-index.md` A 段同结论） |
| `PowerOperationDiagnostics.cs:142–166`（`:76` 调用） | 由 `powercfg` 的 **stderr 文本关键词**（`access` 且 `denied`）决定是否给出「需要管理员权限才能修改电源设置」；同一失败也可能落入服务异常/策略分支，取决于 stderr 与退出码，全程不读提权状态 |
| `FirstRunBaselineDialog.xaml.cs:474–478` | 由**失败原因字符串关键词**（含「管理员」二字即命中）判定为权限不足；上游任何含该词的消息都会被归成权限问题，也可能是磁盘/策略原因 |
| `CoreParkingDisableOperation.cs:49–52` | 固定兜底文案把「可能需要管理员权限」与「系统电源服务未运行」并列在同一句里，不区分二者，也不读提权 |
| `NetshTcpGlobalOperation.cs:110`、`NetshTcpSupplementalOperation.cs:87` | 读值失败的兜底文案一律提示「请确认已使用管理员权限运行」；当 `error` 为 null（如命令缺失/解析失败）时提示与真实原因无关 |
| `ManagedPowerPlanCreateDialog.xaml.cs:46–58` | 该分支与提权**无关**（触发条件是 `profile is null`），但文案引导用户「检查管理员权限」，属于把非权限原因说成权限原因 |

---

## 5. 与非提权原因相邻、容易被当成「权限问题」的置灰点

| 位置 | 实际原因 | 与提权的关系 |
| --- | --- | --- |
| `ProcessPriorityPage.xaml.cs:16–17、104–107、111–114` | IFEO 持久优先级缺独立 Owner / durable restore / 受控验证 ⇒ 零写入阻断（常量恒 `true`） | **无关**；提示文案被当作权限提示（`90-symptom-index.md` A 段第 4 行） |
| `GameOptimizer.Core/GraphicsSafety/FailClosedGraphicsOptimizationOperation.cs:37` + `GraphicsSafetyBoundary.cs:59` | 没有受批准的写入平台 ⇒ 直接 `PolicyDenied`；`RequiresAdministrator` 分支不可达 | **无关**（本轮实测 4 个 fail-closed operation 全部 `RequiresAdmin => true`：`FileShellSafety:36`、`GraphicsSafety:37`、`PowerManagement:43`、`Security:36`） |
| `EsportsModePage.xaml.cs:437–458` | 硬件探测未成功 / 正在操作 / 状态读取中 | **不含提权条件** |
| `MemoryCleanDialog.xaml.cs:65`（`CanSelect`）+ `MemoryCleanDialog.xaml:184`（`IsEnabled` 绑定） | 进程类别为系统关键或白名单 | **无关** |
| `RepairCenterViewModel.cs:1789`（`IsActionAvailable`） | `Availability is null or WorkingBidirectional` | **与提权无关**；同文件 `IsAdmin`（`:121/157`）只赋值、无 XAML 绑定、无其它消费点 |
| `PowerPlanEditorPage.xaml.cs:116–118` + `ManagedPowerPlanService.cs:551` | `IsPolicyManaged` 优先于 `IsAdministrator` ⇒ 策略托管时即使已提权也不可用 | 提权只是**文案分支**之一 |

---

## 6. 附录 A：本地化里已定义、却没有显示点的管理员文案键

> 判定方法：以键名在整个仓库（含 `*.cs` / `*.xaml` / `*.json` / `*.md`）检索，命中仅出现在
> `GameOptimizer.Core/Localization/LocalizationService.ZhCN.cs` / `...EnUS.cs` / `Strings.zh-CN.json` / `Strings.en-US.json` / `docs/`。
> ⇒ 「提示需要管理员」在这些位置**只有文案、没有显示点**，也就不可能读取提权状态。
> 「定义行」列的两组数字分别对应上述 `ZhCN` / `EnUS` 两个文件（全路径见上；`/` 前的行号属 ZhCN，`/` 后属 EnUS）。

| 本地化键 | 定义行（ZhCN / EnUS） | 消费点 |
| --- | --- | --- |
| `Optimize.RequiresAdmin` | 261 / 269 | 无 |
| `Module.AdminRequired` | 395 / 409（另 `Strings.*.json:487`） | 无 |
| `Restore.NeedAdmin` | 1537、4062 / 1557、5111 | **部分**：仅 `RestorePointViewModel.cs:302` 消费该**键名**；两处定义中生效的是**后一条**（见下方口径补充） |
| `Restore.NeedAdminToEnable` | 1601 / 1621 | 无 |
| `Restore.NeedAdminForPoints` | 5702 / 6758 | 无 |
| `Restore.DeleteNeedAdmin` | 4081 / 5130 | 无 |
| `Restore.EnableNeedAdmin` | 5703 / 6759 | 无 |
| `Restore.StatusDisabledNeedAdmin` | 5701 / 6757 | 无 |
| `Restore.AdminRequired` | 5081 / 6320 | 无 |
| `Restore.AdminRequiredMessage` | 5082 / 6321 | 无 |
| `Memory.AdminRequiredMessage` | 5078 / 6317 | 无 |
| `CpuAffinity.AdminRequired` | 5079 / 6318 | 无 |
| `CpuAffinity.AdminRequiredMessage` | 5080 / 6319 | 无 |
| `MemClean.RequiresAdmin` | 2355 / 2434 | 无 |
| `Memory.NeedAdminOrManual` | 2771 / 3154、4217 | 无 |
| `Module.NeedAdminOrManual` | 3002 / 3300、4249 | 无 |
| `Esports.NeedAdminOrManual` | 3579 / 4601 | 无 |
| `PerfBoost.NeedAdminOrManual` | 3653、3665 / 4629 | 无 |
| `GameProfiles.AdminRequiredForOptimization` | 5085 / 6324 | 无（「游戏优化需要管理员权限，是否不优化启动」这条交互当前**没有实现**） |

**口径补充（本轮实测）**：本地化资源的容器是 `Dictionary<string, string>` 的**索引器初始化**
（`LocalizationService.ZhCN.cs:6` `GetChineseResources() => new() { ["键"] = "文案", ... }`）。
索引器初始化允许同一字典内出现重复键，**后写覆盖先写、不报错**；因此 `Restore.NeedAdmin` 运行时生效的是 `4062 / 5111`
（「需要管理员权限才能创建还原点」），`1537 / 1557`（「需要管理员权限」）是被覆盖的旧文案。
表格「消费点」列统计的是**键名**被引用的位置（唯一一处为 `RestorePointViewModel.cs:302`），与「哪条定义生效」是两件事。

**被真实消费的管理员类键**（作为对照，避免误判为死键）：
`Common.AdminRequired`（`SystemCleanupViewModel.cs:361`、`BackupManagerViewModel.cs:1926`、`RepairCenterViewModel.cs:704/955/1087`）、
`Cleanup.Backup.Repair.*AdminRequired(Message)`、`Compatibility.AdminRequired`（`CompatibilityChecker.cs:365`）、
`Engine.AdminRequired`（`OptimizationEngine.cs:638`）、`Guard.NeedAdmin`（`SystemGuard.cs:162`，但该 API 零调用方）、
`Network.NeedAdmin`（`NetworkTestPage.xaml.cs:800`）、`Diagnostic.NoAdmin*`（`StartupDiagnosticService.cs:41–43`）、
`Solution.RunAsAdmin` / `Solution.RunAsAdminGeneric`（`SolutionProvider.cs:44/362`）、`Backup.Solution.RunAsAdmin`（`BackupDiagnosticService.cs:51`）、
`Module.Capability.AdminRequired(.Reason)`（由 `OptimizationToggleModel.cs:309` 以 `$"Module.Capability.{CapabilityState}.Reason"` **动态拼键**消费）、
`Window.AdminMode` / `Window.NormalMode`（`MainWindow.xaml.cs:280`）、`Dashboard.Privilege.Admin/Normal`（`DashboardViewModel.cs:421`）、
`CoreAccel.PermissionHint(Desc)`（`CpuCoreManagementViewModel.cs:172–173`）、`Log.AdminMode` / `Log.NormalMode`（`DashboardViewModel.cs:1170/1187`）。

---

## 7. 附录 B：复核命令（本轮实测口径）

```powershell
# 0) 基线：HEAD 与工作树未提交改动（本轮 = aa45545，51 行）
git -C 'YHRJ - 副本' rev-parse --short HEAD
git -C 'YHRJ - 副本' status --porcelain

# 1) 置灰主路径：开关可用性模型与绑定
Select-String -Path 'GameOptimizer.Shell\Models\OptimizationToggleModel.cs' -Pattern 'RequiresAdmin|CanToggle|ResolveCapabilityState|RefreshPrivilege'
Select-String -Path 'GameOptimizer.Shell\Views\MasterModuleTemplate.xaml.cs' -Pattern 'CanToggle|HasCapabilityConstraint'

# 2) 提权判定与转发（应为唯一实现 + 转发；排除 *.Tests）
Get-ChildItem -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\GameOptimizer\.Tests\\' } |
  Select-String -Pattern 'WindowsElevation\.'

# 3) 「提示需要管理员」的全部取值点
Select-String -Path (Get-ChildItem -Recurse -File -Filter *.cs).FullName -Pattern 'AdminRequired|NeedAdmin'

# 4) 任一「零消费」结论的复核（例）
Select-String -Path (Get-ChildItem -Recurse -File).FullName -Pattern 'GameProfiles.AdminRequiredForOptimization'
Select-String -Path (Get-ChildItem -Recurse -File).FullName -Pattern 'DiagnosePermissionIssue'

# 5) XAML 侧是否存在直接的提权绑定（本轮结果：Shell\Views 下零命中）
Select-String -Path (Get-ChildItem -Recurse -File -Filter *.xaml).FullName -Pattern 'IsAdmin|IsAdministrator'

# 6) 全仓显式 UAC 提升点（本轮结果：仅 RegistryInfoDialog 一处）
# ⚠ 必须带引号与等号：裸写 'runas' 会因大小写不敏感命中 RunAsync（本轮实测 100+ 条假阳性）
Select-String -Path (Get-ChildItem -Recurse -File -Include *.cs,*.xaml,*.manifest).FullName -Pattern 'Verb\s*=\s*"runas"'

# 7) 声明层全量枚举（§3.4；本轮 = 74 处，其中声明 73 + CompatibilityChecker 映射 1）
Get-ChildItem -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
  Select-String -Pattern 'RequiresAdmin\s*=>'
# 参数化声明位（§3.4 末段；本轮 = 21 处 true）
Select-String -Path 'GameOptimizer.Core\Optimization\Providers\BuiltInOptimizationOperationProvider.cs' `
  -Pattern 'requiresAdministrator\s*:\s*(true|false)' -CaseSensitive

# 8) §2.1 写入/消息层文案（本轮口径：全部按异常类型或 Win32 错误码 5，不读提权）
Get-ChildItem -Recurse -File -Include *.cs,*.xaml | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
  Select-String -Pattern '请以管理员身份运行|以管理员身份运行程序|权限不足，请以管理员身份运行|权限不足（请以管理员运行'

# 9) §2.1 中「零调用方」结论的逐个复核（本轮结果：以下名字除定义处外无命中）
#    SafeProcessHelper.SetProcessorAffinity / SetPriorityClass / KillProcess
#    SafeRegistryHelper.DeleteValue / SafeServiceHelper.TryStopService（同步）
#    IAdvancedTuningService（含 DI 组合根 App.xaml.cs 的 ConfigureServices）
Select-String -Path (Get-ChildItem -Recurse -File -Filter *.cs).FullName -Pattern 'SetProcessorAffinity\(|SafeRegistryHelper\.DeleteValue|IAdvancedTuningService'

# 10) 文档内所有 `文件:行号` 引用的批量核对（本轮：94 个被引文件全部存在、无越界行号）
#     做法：正则提取反引号内的路径 + 行号 → Test-Path 相对仓库根 → 与 (Get-Content).Count 比较
```

### 7.1 本轮实测的补充事实（供后续复核对照）

- `PermissionChecker.CheckOperationPermission`（`PermissionChecker.cs:28`）与 `SystemGuard.CheckPrerequisites`（`SystemGuard.cs:156`）**均为零调用方**。
- `OperationErrorAnalyzer.DiagnosePermissionIssue`（`OperationErrorAnalyzer.cs:50`）**零调用方** ⇒ 该「已提权不再误报权限」的修正路径当前不可达；真正生效的同类分流是 `OptimizationResultMessageFormatter.cs:22–27`（关键词触发）与各 ViewModel 的 `IsRunningAsAdministrator()` 前置检查。
- `GameOptimizer.Shell/Views/**/*.xaml` 中不存在 `IsAdmin` / `IsAdministrator` 绑定；提权只经 ViewModel 属性间接影响 UI。
- `RepairCenterViewModel.IsAdmin`（`RepairCenterViewModel.cs:121/157`）与 `CpuCoreManagementViewModel.IsAdmin`（`CpuCoreManagementViewModel.cs:110/116`）**没有 XAML 消费者**：前者无任何后续引用，后者只用于 `:169–174` 的 Toast。
- **声明层**：`IOptimizationOperation.RequiresAdmin` 的**接口默认值是 `true`**（`IOptimizationOperation.cs:46`）⇒ 任何未覆写该成员的 operation 一律按「需要管理员」处理（§3.4 第 1 行）。
- **写入工具层共 13 组**「请以管理员身份运行」文案，**没有一处读 `WindowsElevation`**（全部按异常类型或 Win32 错误码 5）；其中 6 组当前**零调用方/无生效调用**：
  `SafeProcessHelper.SetProcessorAffinity`、`SetPriorityClass`、`KillProcess`、`SafeRegistryHelper.DeleteValue`、`SafeServiceHelper.TryStopService`（同步版）、以及 `IAdvancedTuningService` 整条链（无 DI 注册、无消费者）。
- `RegistryPriorityService` 的两处 `HasAdminPrivileges()` 调用（`:39`、`:108`）位于 `#if false` 块（`:33–95`、`:102–167`）内 ⇒ 该成员只有定义、无生效调用（与 §3.1「零调用方」结论一致）；同一文件里的 `SafeRegistryHelper` 调用（`:47、56、65、116、124`）同样在该死区内。
- **声明位枚举**：全仓 `RequiresAdmin =>` 共 **74** 处 = `§3.4` 的 **73** 处声明 + `CompatibilityChecker.cs:181` 的枚举映射 1 处；`BuiltInOptimizationOperationProvider.cs` 另有 **21** 处 `requiresAdministrator: true` 命名实参（§3.4 末段）。
- **本地化重复键**：`Restore.NeedAdmin` 在 ZhCN / EnUS 各定义两次，容器是 `Dictionary<string,string>` 的索引器初始化 ⇒ **后写覆盖先写**，运行时生效值来自后一条（`4062 / 5111`），见 §6 口径补充。
- **文档引用自检**：全文 94 个不同的文件引用全部存在、反引号内引用的行号均未越界（脚本见 §7 第 10 条）。

---

## 8. 边界与未验证项

1. 本清单是**静态代码核对**结果，未在真实 UAC 场景（标准用户 / 管理员未提权 / 已提权）逐条人工复现；
   `WindowsElevation.Probe()` 的三态判定本身未在本机做人工验证（与 `04-platform-and-privilege.md` 的既有边界一致）。
2. 「全仓零消费」类结论基于键名/方法名的字面检索：**动态拼键**（如 `$"Module.Capability.{...}.Reason"`）已单独说明，
   但若后续新增其它拼键方式，§6 的结论需要重跑 §7 第 4 条核实。
3. 行号对应本工作树（`HEAD = aa45545` + 51 行未提交改动）。`docs/` 目录本身未纳入 git，本文件为新增未跟踪文件。
4. 本文件不做修复建议；「应改成读什么」「哪些判定是缺陷」不在本清单范围（见 `04-platform-and-privilege.md` §6 与 `90-symptom-index.md` A 段）。
5. §2.1 / §2.2 / §3.4 为本轮补录，取证方式与 §1–§3 相同（grep + 逐行读源），**同样未在真实 UAC 会话中逐条触发验证**。
   其中「零调用方 / 不可达」是「全仓无引用」的字面检索结果：若存在反射、DI 注册或动态调用（本轮未发现），该结论会失效。
6. §3.4 的「值」列对 `true` / `false` 直接取自源码行；对 `_inner` / `_boundary` / `_hive` / `_definitions` 这类推导式只标注「推导 / 转发」，
   **未**逐条追踪到最终布尔值（即「该功能是否真的会灰」仍取决于被转发对象的声明）。
7. §7 第 10 条的自检覆盖「文件存在性 + 行号是否越界」，**不保证**每个行号都指向所述成员：
   本轮抽读了 §1–§3 每行的首个引用行内容（与描述一致），但未逐行核对每个区间端点与说明文字。
8. 本轮复核未改动任何源码、未改动 `docs/code-map/` 下的既有文件；`docs/` 目录仍未纳入 git，本文件仍是未跟踪新增文件。
