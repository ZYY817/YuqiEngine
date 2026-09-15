# 90 · 症状 → 代码位置 → 相关测试

**用法**：先在这里找到与你手上现象最接近的一行，按「先看」列的顺序查。
位置标 `文件:行` 的以 2026-09-13 工作树为准；行号漂移后按类型/方法名重新定位。

**状态图例**

- ✅ 本次实测过（命令/构建/测试有输出）
- ⚠️ 已确认存在、但尚未修复
- ❓ 仅代码级推断，未在真实环境复现

---

## A. 权限 / 管理员

| 症状 | 先看 | 相关测试 | 状态 |
| --- | --- | --- | --- |
| 以管理员身份运行后，仍有选项灰着、提示「需要管理员」 | ① 权限真相：`WindowsElevation` ② **已提权时不得再走 AdminRequired**：`OptimizationToggleModel` 对 `SupportState=PermissionRequired` 且 `_isAdministrator` 的误归因按已受支持继续评估；`CommandResultPresentation` 已提权时拒绝访问归为 Blocked ③ 启动后 `UserPrivilegeService.EnsureAuthoritativeSnapshot()` + `DashboardViewModel` 同步时读权威门面 | `GameOptimizer.Tests/Shell/ElevationMisattributionTests.cs` | 根治已落地 ✅（2026-09） |
| 已是管理员，仍提示「请以管理员身份运行重试」 | `PermissionChecker.GetAdminRequiredMessage()`（`GameOptimizer.Core/Services/PermissionChecker.cs:41`）——现在会区分「管理员组但令牌未提权（UAC 筛选令牌）」与「账户不在管理员组」；调用点示例 `SystemCleanupViewModel.CleanAsync()`（`:355`） | 无 | 文案与归因已改进 ✅；**真实 UAC 场景未人工验证** ❓ |
| 已提权，但 Windows 仍拒绝写入，却被归因为权限不足 | `PermissionChecker.GetSystemDeniedMessage()`（`:61`）——明确列出策略托管 / ACL / 安全软件 / 驱动占用，并说明重复提权无用 | 无 | 已实现 ✅；未人工验证 ❓ |
| 打开「进程优先级」页恒定弹提示 | `GameOptimizer.Shell/Views/ProcessPriorityPage.xaml.cs:16,17,106`——`CheckAdminPrivileges()` **不含任何提权判定**，`IsIfeoBlocked` 恒 `true` | — | 与权限无关，属固定阻断提示 ⚠️（`04` §6.4） |
| 图形 / 显示类项不可用，提示被理解成权限问题 | `FailClosedGraphicsOptimizationOperation.cs:7–8,70–71`（硬编码 `UnknownEnvironment`，其中 `IsAdministrator=false`）→ `UnavailableGraphicsSafetyBoundary.Instance:26` → `GraphicsSafetyBoundary.cs:103–119` **直接 `PolicyDenied`**；`GraphicsSafetyBoundary.EvaluateAsync:59` 的 `RequiresAdministrator` 分支在当前接线中不可达 | 无 | **与提权完全无关**，真实原因「没有受批准的写入平台」⚠️（`04` §6.3） |
| 「管理员状态」在启动后就再也不更新 | `UserPrivilegeService.Refresh()`（`GameOptimizer.Shell/Services/UserPrivilegeService.cs:92`）为唯一刷新点；`SystemCompatibilityChecker._cachedSystemInfo`（`:18`，`Lazy<>` 只求值一次）、`RuntimeWindowsCompatibilityEnvironmentProbe._cached`（`:616,625`） | 无 | 已确认存在 ⚠️（`04` §6.2） |
| 还原点 / 服务 / 注册表 / 预装应用清理提示需要管理员 | `04-platform-and-privilege.md` §5（各页面门禁一览）与 §2（全部判定点与调用方） | `GameOptimizer.Tests/Changes/*CapabilityTests.cs`（能力层） | ❓ |

---

## B. 启动与「没反应」

| 症状 | 先看 | 相关测试 | 状态 |
| --- | --- | --- | --- |
| 双击没反应、连进程都没有 | `Program.RunEnvironmentPreCheck()` 返回值；`<LogsDir>/startup_diagnostic.log`；`AppPaths.IsFallbackDataRoot` / `DataRootFallbackReason` | — | ❓ |
| 主窗口不出现，且没有任何报错 | `GameOptimizer.Shell/App.xaml.cs` 约 `402–412` 的 `hostEx` 分支 + `StartupOrchestrator.ShowError`（**空操作**）；`App` 从不调用 `ShowSplash()` → DI 失败路径没有错误面 | — | ⚠️ 已确认（`01` §10 第 1 条） |
| 首屏出来了，但内容全空 | `MainWindow.OnLoaded` 的 `PageDiagnosticService.LogError("MainWindow", "ViewModel 初始化失败", ex)`；`App.InitializeServices` 记录 | — | ⚠️ 半可用状态只落在日志里（`01` §2.2、§5 D） |
| 点侧栏导航没反应 | `page_diagnostics.log` 中 `MainWindow` 的「忽略未知导航键 / 路由宿主不存在」 | `GameOptimizer.Tests/Visual/*VisualContractTests.cs` | ❓ |
| 第二个实例「没反应」（不激活已有窗口） | `StartupOrchestrator.ActivateExistingWindow()`——窗口标题硬编码 `"宇奇引擎"`，被改标题后失配 | — | ⚠️（`01` §4） |
| 某个优化模块整块消失 | `ModuleManifestProvider.Load()` + `ModuleLoader.RegisterModule` 的 `Debug` 输出；发布目录里 `modules.json` 是否存在 | — | ⚠️ 空 manifest 与「本来没有模块」UI 上不可区分（`01` §10 第 4 条） |
| 找不到崩溃日志 | `App.CrashLogPathSafe` 的 fallback（`<BaseDirectory>/crash.log`）；默认 `<AppDataRoot>/crash.log` | — | ❓ |

---

## C. 「失败了但什么都没显示」

| 症状 | 先看 | 相关测试 | 状态 |
| --- | --- | --- | --- |
| 操作失败，界面既不报错也无提示 | 四层逐一看：`01-startup-and-shell.md` §5 A（进程/预检）B（异常注册）C（DI/编排）D（首屏渲染）；链路成因 `03-core-domains.md` §11.4 | — | ⚠️ 结构性缺口已定位 |
| UI 线程异常对用户完全不可见 | `App.OnDispatcherUnhandledException`——「只弹一次」代码被注释（约 `1145–1149`）且 `e.Handled = true` | — | ⚠️（`01` §10 第 2 条） |
| 连诊断日志都可能丢 | `PageDiagnosticService`：单文件上限 5MB（超限直接 `File.Delete`），写入失败只 `Debug.WriteLine` | — | ⚠️（`01` §8） |
| 不知道日志在哪 | `<LogsDir>/page_diagnostics.log`、`<AppDataRoot>/crash.log`、`<LogsDir>/startup_diagnostic.log`；`AppPaths.cs` | — | ✅ 位置已核实 |

---

## D. 工程 / 构建 / 发布

| 症状 | 先看 | 状态 |
| --- | --- | --- |
| `error CS1061: "nint" 未包含 "DangerousGetHandle"`（`WindowsElevation.cs:142`） | `WindowsIdentity.Token` 是 `IntPtr`，必须用 `AccessToken`（`SafeAccessTokenHandle`）。**2026-09-13 已修**，全量构建 0 错误 | ✅ 已修复 |
| 构建报「0 个错误」却「生成失败」 | `05-engineering-pipeline.md` §2.4 第 2 行——必须加 `-v:diag` 才能看到真实错误 | ❓ |
| 构建报 `Access to the path … denied` | `obj\` 被占用 / ACL，或曾用管理员权限构建过（`05` §2.4 第 1 行） | ❓ |
| 测试跑不起来 | `05` §6.5 第 1 行（环境令牌）vs §2.4（`obj\`） | ❓ |
| 发布脚本报「目录已存在」 | `eng/Publish-Release.ps1` 的 `-Force` 语义（`05` §3.1） | ❓ |
| 安装包生成失败 | ISCC 原始输出 + 4 个 `/D` 定义（`05` §4.1）+ 相对路径资源（`05` §4.4） | ❓ |
| 用户反馈「版本不对」 | `artifacts/release/v<Version>/checksums/SHA256SUMS.txt` + EXE 属性 `ProductVersion`（含 `+git sha`）反查 `-Version`（`05` §3.4、§5.1） | ✅ 位置已核实 |
| 便携版「模块不生效但没有任何提示」 | `modules.json` 未随发布 + `ModuleManifestProvider.Load()` 只有 `Debug.WriteLine`（`05` §3.7 末行） | ❓ |

---

## E. 当前已知的测试状态（2026-09-13 实测）

- 全量：**1628 用例，通过 1599，失败 29**（`dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Release --no-build`）。
- 与 `HEAD` 干净检出对比（1627 用例，失败 30）：工作树**未修复的失败与基线一致**，属既有失败；本次未提交改动**修好了 2 条**（`Architecture.EvidenceIsolationArchitectureTests.Memory_pages_receive_the_single_composed_service_chain`、`Changes.MemoryCleaningBoundaryTests.ReachableMemorySourcesContainNoLegacyWriterAndExposeBlockedUiState`）。
- **唯一疑似新回归**：`Changes.CleanupQuarantineDurabilityTests.PreviewContentHashBlocksSameSizeAndTimestampDriftAfterAuthorization`（期望 `ChangedSincePreview`，实得 `Quarantined`，测试文件 `GameOptimizer.Tests/Changes/CleanupQuarantineDurabilityTests.cs:27`）。成因未定位 → 见 `03-core-domains.md` §3（系统清理）与 `CleanupSafety.cs` / `CleanupQuarantineManagement.cs` 的未提交改动。
