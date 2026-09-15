# 代码地图索引（YuqiEngine / `YHRJ - 副本`）

> **主入口（目录级总览）**：仓库根 [`CODE_MAP.md`](../../CODE_MAP.md) —— 全仓文件夹职责、工程分层、「想改 X 去哪」速查；本目录 01–91 是行号级深潜专题。

**用途**：给定一个「现象」或一个「模块名」，用最短路径定位到代码位置、耦合关系和相关测试，不再靠全仓搜索。

**边界**：本目录只写「定位信息」（文件、类型、方法、行号、依赖方向），不复制实现、不做设计评审。
行号以 2026-09-13 工作树为准（`git HEAD = aa45545`；`docs/` 整个目录尚未纳入 git，`eng/Publish-Release.ps1`、`README.md` 有未提交改动，`README_EN.md`、`GameOptimizer.Core/Services/WindowsElevation.cs` 仍未跟踪），改动代码后行号会漂移，届时以类型/方法名为准重新定位。

---

## 一、三步定位法

| 你现在手里有什么 | 直接去 |
| --- | --- |
| 一句用户反馈的现象（「点了没反应」「显示需要管理员」） | `90-symptom-index.md` |
| 一个模块名（内存清理 / 电源 / 网络 / 游戏模式…） | `03-core-domains.md` §14「变更导航速查」 |
| 一个页面或按钮 | `02-ui-pages-and-viewmodels.md` §2 路由总表、§3 页面↔ViewModel↔服务 |
| 「程序为什么起不来 / 为什么不报错」 | `01-startup-and-shell.md` §5、§10 |
| 「为什么显示需要管理员 / 权限判定在哪」 | `04-platform-and-privilege.md` §1、§2、§6 |
| 「怎么构建 / 怎么打包 / 产物在哪」 | `05-engineering-pipeline.md` §2、§3 |

---

## 二、文档清单

| 文档 | 覆盖内容 |
| --- | --- |
| 仓库根 `CODE_MAP.md` | **目录级代码地图**（工程分层、各项目文件夹职责、变更写入地基、快速导航）——日常改代码优先看这里 |
| 本目录 01–05 / 90–91 | 启动、UI、业务域、权限、工程链路、症状索引（行号级） |

> 规模 = 该文件的**总行数**（含空行）。口径：逐文件执行 `(Get-Content -LiteralPath <file>).Count`，或在 `docs\code-map` 下对 `*.md` 批量跑同一表达式。
> 下表为 **2026-09-13 23:01 实测快照**；`01`/`02`/`03` 在核查期间仍被另一路编辑写入（mtime 22:57:38–23:00:54；`02` 在定稿过程中由 384 变为 420），这三行是移动值 —— 有疑问时重跑上述命令，**以实测为准**。

| 文档 | 覆盖内容 | 规模 |
| --- | --- | --- |
| `01-startup-and-shell.md` | `Program.Main` → `App.OnStartup` → DI 组合根 → `MainWindow` 首屏；**启动期失败被吞掉的位置**；数据目录与日志落点 | 409 行 † |
| `02-ui-pages-and-viewmodels.md` | 导航模型、27 条路由总表、页面↔ViewModel↔服务对应表、44 个对话框、页面级失败静默点 | 420 行 † |
| `03-core-domains.md` | 统一写入地基 + 九大业务域（内存/清理/网络/电源/游戏模式/硬件/还原点/安全/诊断）各自的入口、契约、状态所有者、依赖方向、跨域耦合；跨域共享清单；「注册但无消费者」清单 | 1115 行 † |
| `04-platform-and-privilege.md` | 提权判定的唯一权威实现、**全部判定点与调用方**、能力门禁路径、**与实际提权状态无关的判定清单**、P/Invoke 面、诊断缺失/误归因点 | 829 行 |
| `05-engineering-pipeline.md` | 用哪个 sln、构建/测试准确命令、发布脚本与安装器逐项对照、版本号来源与漂移点、测试布局、**验证记录（§8）** | 501 行 |
| `90-symptom-index.md` | 症状 → 位置 → 相关测试 → 当前状态 | 72 行 |

† **编辑中**：`01`/`02`/`03` 在核查期间被另一路编辑持续写入，行数/行号是移动值 —— 本轮末次读数（23:01:17）为 `01`=409（mtime 23:00:54，最后一次写入未再改变行数）、`02`=420（23:00:32）、`03`=1115（22:57:38）；`01` 在 22:58:09 首测时是 407，`02` 在 22:58:09 首测时是 384，**同一轮内都已变化**，因此这三行只是瞬时值。`04-platform-and-privilege.md`（mtime 01:20）、`90-symptom-index.md`（02:27）稳定；`05`/`README.md` 由本次核查定稿（同一次改动一并补写了 `05-engineering-pipeline.md` §8.3）。

- 本目录共 **7 个 `.md`**：上表 6 份地图 + 本索引文件 `README.md`（本文件不参与上表规模统计，避免自引用）。
- 行数复核口径与末次读数已同时写入 `05-engineering-pipeline.md` §8.3 第 12 行（§8.2 第 7 行的 19:5x 读数已标注过期）；本表与 §8.3 第 12 行取自同一次 `(Get-Content).Count` 测量（2026-09-13 23:01:17）。

---

## 三、全局锚点（最常用的几个位置）

| 我想找 | 位置 |
| --- | --- |
| DI 组合根（唯一） | `GameOptimizer.Shell/App.xaml.cs` 的 `ConfigureServices(services => { ... })` 内联 lambda（L224–396）；**不存在 Startup.cs** |
| 进程入口 | `GameOptimizer.Shell/Program.cs` |
| 提权判定的唯一权威实现 | `GameOptimizer.Core/Services/WindowsElevation.cs`（`WindowsElevation.Current` / `.IsElevated`，**工作树内尚未提交**） |
| 提权状态的 UI 门面 | `GameOptimizer.Shell/Services/UserPrivilegeService.cs`（`Refresh()` 在 L90，唯一刷新点） |
| 统一写入地基（typed change） | `GameOptimizer.Core/Changes/**` + 各 `*Capability`（见 `03-core-domains.md` §1） |
| 路径常量（日志/数据根全部出口） | `GameOptimizer.Core/Constants/AppPaths.cs` |
| UI 诊断日志 | `<LogsDir>/page_diagnostics.log`（`AppPaths.PageDiagnosticLogPath`，`AppPaths.cs:68`） |
| 启动期崩溃日志 | `<AppDataRoot>/crash.log`（`AppPaths.CrashLogPath`，`AppPaths.cs:61`） |
| 数据根目录 | `AppPaths.AppDataRoot`（默认 `F:\YuqiEngine`，可用环境变量 `YUQIENGINE_DATA_ROOT` 覆盖，`AppPaths.cs:11,15,21`） |
| 发布脚本 / 安装器 | `eng/Publish-Release.ps1` / `installer/YuqiEngine.iss` |

---

## 四、常用命令（2026-09-13 逐条对照脚本/工程文件核对）

```powershell
# 全量构建（推荐；与验证口径一致）——19:4x 的实测记录：0 警告 0 错误，用时 56.23 s（本轮文档复核未重跑）
dotnet build GameOptimizer.sln -c Release -m:1 -p:BuildInParallel=false

# 全量测试（先按上面的命令构建过 Release，才可加 --no-build）
dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Release --no-build

# 只跑某一类（全量串行约 25 秒，缩范围更快；--filter 参数是 VSTest 的，不是应用的）
dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~GameOptimizer.Tests.Changes"

# 发布便携版 + 安装包（注意：目标目录已存在时必须换版本号或加 -Force）
pwsh -File eng\Publish-Release.ps1 -Version 0.1.3-preview
```

- 参数来源核对：测试工程路径 `GameOptimizer.Tests\GameOptimizer.Tests.csproj` 存在，其 `TargetFramework = net8.0-windows10.0.19041.0`（csproj L3）；`--filter "FullyQualifiedName~GameOptimizer.Tests.Changes"` 里的命名空间是测试源码真实声明（如 `Changes/MemoryCleaningBoundaryTests.cs` 的 `namespace GameOptimizer.Tests.Changes;`）；`-Version 0.1.3-preview` 命中 `eng/Publish-Release.ps1` L3 的 `ValidatePattern` 与 L4 默认值。
- 为什么用 `-c Release`：它同时启用 `GameOptimizer.Shell.csproj` L49–55 的 Release 条件块（`TreatWarningsAsErrors=true`），因此 Release 下测出的编译警告即失败；`--no-build` 要求**先按同一配置**构建过，否则报「找不到测试源文件」。
- `-m:1 -p:BuildInParallel=false` 不是可选美化：它是 `obj\` 并发占用（`MSB3491`，见 `05-engineering-pipeline.md` §2.4）的缓解手段，发布脚本自己也在用（`Publish-Release.ps1` L46–48）。
- ⚠️ **`dotnet test` 在本会话（受限令牌沙箱）无法真正跑用例**：testhost 启动即以 `Win32Exception (5) 拒绝访问` 中止；同机 `dotnet --info` 也在 `GetParentProcessId` 抛同一异常。需在普通终端复跑。详见 `05-engineering-pipeline.md` §8.2。
- 构建/测试常见失败原因（`obj\` 被占用、0 错误却生成失败、testhost 拒绝访问等）见 `05-engineering-pipeline.md` §2.4、§6.5。

---

## 五、本目录的核查状态（2026-09-13）

### 已验证（命令 + 观测结果）

- **文档清单与规模**：本目录实际为 7 个 `.md`（6 份地图 + 本索引 `README.md`，文件清单见 §二）；§二 的 6 个规模数字用 `(Get-Content -LiteralPath <file>).Count` 实测（2026-09-13 23:01:17：409/420/1115/829/501/72）。19:47 的旧读数（397/275/1069/829/458/72）已全部过期，`05-engineering-pipeline.md` §8.2 第 7 行已同步标注过期。`01`/`02`/`03` 在核查期间持续由另一路编辑写入（mtime 22:57:38–23:00:54；首测 407/384/1115 → 末测 409/420/1115），这三行是移动值，已加 † 标注。§一 的跨文档小节号（`01` §5/§10、`02` §2/§3、`03` §14、`04` §1/§2/§6、`05` §2/§3）逐条对照各文档当前 `## ` 标题，全部存在。
- **全局锚点**：`App.xaml.cs` 的 `ConfigureServices` lambda 实为 L224–396；`AppPaths.PageDiagnosticLogPath` / `CrashLogPath` / `AppDataRoot` / `YUQIENGINE_DATA_ROOT` 在 `GameOptimizer.Core/Constants/AppPaths.cs` L68 / L61 / L21 / L11；`UserPrivilegeService.Refresh()` 在 L90；`GameOptimizer.Shell` 下**确无** `Startup.cs`。
- 5 份文档共 100 余处「仓库相对路径」引用，逐条 `Test-Path` 核对；发现 1 处失效并已修正（`01-startup-and-shell.md` §3.1 第 2 行：`MemoryCleaningServiceCollectionExtensions.cs` 实际在 `GameOptimizer.Infrastructure.Windows/Extensions/`，原写作 `MemoryCleaning/`）。
- **`dotnet build GameOptimizer.sln -c Release -m:1 -p:BuildInParallel=false -nodeReuse:false` → 成功，0 警告 0 错误**（用时 56.23 s，6 个工程全部产出；该记录来自 19:4x，本轮文档复核未重跑）。此前那条 `GameOptimizer.Core/Services/WindowsElevation.cs:142` 的 `CS1061` 已修复。
- **`eng/Publish-Release.ps1` 逐参数核对**：§四 的 `-Version` 与脚本 L3–4 的 `ValidatePattern`/默认值一致；产物路径公式（`portable\`、`installer\`、`checksums\` 三层）与 `artifacts/release/v0.1.3-preview/` 现场一致；`checksums/SHA256SUMS.txt` 为两行（ZIP + setup EXE）。
- **发布物取证（只读）**：`YuqiEngine.exe` → `FileVersion=0.1.3.0` / `ProductVersion=0.1.3-preview+f99ed5d8e5ef8122ffc7820198dac5e8346f0588`；setup EXE → `ProductVersion=0.1.3-preview`、`ProductName=Yuqi Engine`、`100,632,641` 字节；发布 ZIP 510 个 entry、根目录 `YuqiEngine-v0.1.3-preview-win-x64-portable/`；发布目录内**有** `GameOptimizer.ModuleContracts.dll`、**无** `modules.json`。
- **`05-engineering-pipeline.md` 工程链路复核（2026-09-13 22:57）**：`eng/Publish-Release.ps1`（133 行）、`installer/YuqiEngine.iss`（62 行）、`GameOptimizer.Shell/Tests` 的 csproj、`Directory.Build.targets`、`.gitignore`、`eng/Verify-EvidenceIsolation.ps1` 的行号与字面量**逐条读取**核对，§3/§4/§5 引用全部命中；`artifacts/release/v0.1.3-preview` 的体积/哈希/版本信息/ZIP entry 数、测试布局静态统计（24 目录 / 194 `.cs` / 1244 处用例）与 §3/§5/§6 一致。**本轮未重跑构建、测试与发布**，证据与未复验项见该文 §8.3。

### 未验证 / 待办（不要据此声称通过）

1. **提权收敛尚无自动化测试**：`GameOptimizer.Tests` 中没有任何用例引用 `WindowsElevation` / `ElevationLevel` / `IsElevated` / `GetAdminRequiredMessage` / `GetSystemDeniedMessage`（全仓 grep 确认，仍为 0 命中）。
2. **UI 侧「需要管理员」误归因未收敛**：`04-platform-and-privilege.md` §6 列出的判定点（`OptimizationToggleModel.RequiresAdmin` 默认 `true`、图形类恒 `PolicyDenied`、进程优先级页固定提示等）仍按原样工作。
3. **「失败无错误信息」的两条改造锚点未实施**：`StartupOrchestrator.ShowError` 本身只是一层委托（`_splash?.ShowError(...)`，`StartupOrchestrator.cs` L232），但**全仓没有任何地方调用 `ShowSplash()`**（只有定义 `StartupOrchestrator.cs` L124 与属性 `ShouldShowSplash` L48）⇒ `_splash` 恒为 `null`，启动失败路径实际没有错误面；`App.OnDispatcherUnhandledException` 的弹窗代码仍处于注释状态（`App.xaml.cs` L1145–1149，`e.Handled = true` 在 L1151）。**读代码时该看「无人调用 `ShowSplash()`」，而不是「`ShowError` 是空方法」。**
4. 提权场景（UAC 真实提权/标准用户）**没有在本机人工复现**，仅做了代码级与构建/测试级验证。
5. **`dotnet test` 的用例级结论无法复验**：testhost 在受限令牌下以 `Win32Exception (5)` 中止（同机 `dotnet --info` 同样异常）。因此「1628 用例 / 通过 1599 / 失败 29」以及「与 HEAD 干净检出（1627 用例 / 失败 30）比对后工作树未修复的失败与基线一致」**是 2026-09-13 早些时候的记录，本次未能复跑确认**；其中 1 条疑似与未提交改动有关（`Changes/CleanupQuarantineDurabilityTests.PreviewContentHashBlocksSameSizeAndTimestampDriftAfterAuthorization`，期望 `ChangedSincePreview` 实得 `Quarantined`），成因仍未定位 → 见 `03-core-domains.md` §3 与 `CleanupSafety.cs` / `CleanupQuarantineManagement.cs` 的未提交改动。
6. 本目录文档的行号锚点对应**当前工作树**（`git HEAD = aa45545`）；`docs/` 整个目录尚未纳入 git，`eng/Publish-Release.ps1`、`README.md` 相对 HEAD 有未提交改动，`README_EN.md`、`GameOptimizer.Core/Services/WindowsElevation.cs` 仍是未跟踪文件 ⇒ 行号与 HEAD 版本可能不一致。
