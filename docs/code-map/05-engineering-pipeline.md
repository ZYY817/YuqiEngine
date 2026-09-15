# 05 · 工程链路地图：构建 / 发布 / 安装器 / 版本号 / 测试布局（YuqiEngine）

> 目标：把「源码 → 编译 → 发布目录 → 便携 ZIP → 安装包 EXE → 校验文件」这条工程链路，以及**版本号的每一处来源**、测试工程布局与准确命令行，落到**具体文件 + 具体行号 + 具体路径**。
> 所有路径均为仓库相对路径（仓库根 = `YHRJ - 副本`）。行号锚点对应本文**当前工作树**文件（`git HEAD = aa45545`；注意 `docs/` 整个目录尚未纳入 git，`eng/Publish-Release.ps1`、`README.md` 相对 HEAD 有未提交改动，`README_EN.md`、`GameOptimizer.Core/Services/WindowsElevation.cs` 未跟踪，本文行号一律以工作树为准）。2026-09-13 复验记录见 §8。
> 相关文档：[01 · 启动链路与应用外壳地图](./01-startup-and-shell.md)、[02 · 页面与 ViewModel 地图](./02-ui-pages-and-viewmodels.md)。

---

## 0. 一页速查

| 环节 | 唯一入口 | 关键命令 / 字面量 | 产物落点 |
| --- | --- | --- | --- |
| 全量编译 | `GameOptimizer.sln` | `dotnet build GameOptimizer.sln -c Debug` | `<工程>\bin\<Config>\net8.0-windows10.0.19041.0\` |
| 发布（便携目录 + ZIP + 安装包 + 校验和） | `eng/Publish-Release.ps1` | `pwsh -File eng\Publish-Release.ps1 -Version 0.1.3-preview` | `artifacts/release/v<Version>/…` |
| 安装器 | `installer/YuqiEngine.iss` | 由发布脚本以 4 个 `/D` 宏调用 `ISCC.exe` | `artifacts/release/v<Version>/installer/YuqiEngine-v<Version>-win-x64-setup.exe` |
| 版本号权威入口 | `GameOptimizer.Shell/GameOptimizer.Shell.csproj` L41–44 | 发布时 `-p:Version=` 覆盖 | deps.json / ProductVersion / ZIP 名 / ISCC `AppVersion` |
| 测试 | `GameOptimizer.Tests/GameOptimizer.Tests.csproj` | `dotnet test GameOptimizer.sln -c Debug` | `GameOptimizer.Tests\bin\Debug\net8.0-windows10.0.19041.0\`；自证证据 `GameOptimizer.Tests\artifacts\<run>\` |
| 证据隔离自检 | `eng/Verify-EvidenceIsolation.ps1` | `pwsh -NoProfile -File eng\Verify-EvidenceIsolation.ps1` | 只读，输出 9 个受保护文件的 SHA256 |

---

## 1. 解决方案与工程集合（用哪个 sln）

### 1.1 两个 sln 的关系

| sln | 正式工程数 | 谁在用 | 结论 |
| --- | --- | --- | --- |
| `GameOptimizer.sln` | 6 | 人工/脚本的常规入口（L6–17 列出 Core、Infrastructure.Windows、ModuleContracts、Modules.PerformanceHealth、Shell、Tests） | **构建、测试统一用这个** |
| `GameOptimizer.Main.sln` | 6（同一批工程，工程 GUID 全部不同，例如 Shell 为 `{5D1E169A-54C5-44EF-8275-CAABA18BE975}` vs `{03BC2CE9-1508-408E-A9FE-2B13A37323FF}`） | 仓库内**没有任何脚本/文档引用**；唯一引用点是 `eng/Verify-EvidenceIsolation.ps1` L20 的 `$solutions` 列表 | 保留为并行清单；发布脚本只按 **csproj** 走，两个 sln 都不参与发布 |

- 两个 sln 都声明了 `Debug|Any CPU`、`Debug|x64`、`Debug|x86`、`Release|Any CPU`、`Release|x64`、`Release|x86`，但**所有 x64/x86 配置都映射到 `Any CPU`**（如 `GameOptimizer.sln` L30–33），因此 `-p:Platform=x64` **不会**改变输出目录，也不会生成 x64 专属产物。
- `GameOptimizer.Main.sln` 与 `GameOptimizer.sln` 的工程集合完全一致，`Verify-EvidenceIsolation.ps1` L48–66 会强制校验「每个 sln 恰好 6 个正式工程、且都在正式清单内」——往任一 sln 里加临时/证据工程都会让该自检失败。

### 1.2 工程集合与依赖方向

```
GameOptimizer.ModuleContracts        (net8.0，无工程引用)
        ↑                ↑
GameOptimizer.Core ──────┘           (net8.0-windows10.0.19041.0，引用 ModuleContracts)
        ↑
GameOptimizer.Infrastructure.Windows (→ Core)
GameOptimizer.Modules.PerformanceHealth (→ ModuleContracts + Core)
        ↑
GameOptimizer.Shell                  (→ Core + Infrastructure.Windows + Modules.PerformanceHealth)
                                     WinExe / AssemblyName=YuqiEngine / StartupObject=GameOptimizer.Shell.Program
        ↑
GameOptimizer.Tests                  (→ Core + Infrastructure.Windows + Shell)
```

- `GameOptimizer.Modules\` **不是工程**：其中 `ModuleLoader.cs`、`ModuleDescriptor.cs`、`ModuleManifest.cs`、`ModuleManifestProvider.cs` 通过 `<Compile Include="..\GameOptimizer.Modules\*.cs" />` 直接编进 Shell（`GameOptimizer.Shell.csproj` L57–62）。
- `TreatWarningsAsErrors=true` 出现在 **Core / Infrastructure.Windows / ModuleContracts / Modules.PerformanceHealth** 四个工程，以及 Shell 的 Release 条件块（`GameOptimizer.Shell.csproj` L49–55）。也就是说：**任何编译警告都会直接让构建失败**（SDK 另外把 `NU1605`、`SYSLIB0011` 当错误；`NU1900` 这类 NuGet 审计警告不算）。

### 1.3 构建期全局规则：`Directory.Build.targets`

| 行 | 内容 | 工程含义 |
| --- | --- | --- |
| L4 | `EvidenceIsolationGlobs`（13 条，按文件内顺序）：`**\_decompile*\**`、`**\*.bak`、`**\*_Backup.*`、`**\*_New.*`、`**\.verify_build\**`、`**\.verify-power-audit\**`、`**\_verify-build\**`、`**\docs\RECOVERY\**`、`**\_evidence*\**`、`**\_staging*\**`、`**\_recovery-history*\**`、`**\_temporary-validation*\**`、`**\obj\RestoreTruthfulnessTestsC\**` | 事故恢复/证据类文件永远不能成为源码、资源或发布输入（`eng/Verify-EvidenceIsolation.ps1` L21 另有正则版清单，两者需同步改） |
| L14–20 | 对 `Compile`/`Content`/`None`/`Page`/`ApplicationDefinition`/`EmbeddedResource`/`Resource` 全部 `Remove="$(EvidenceIsolationGlobs)"` | 即使工程里手写 `<Compile Include>`，这些路径也会被剔除 |
| L8–11 | Release：`DebugType=embedded`、`DebugSymbols=true` | 不生成独立 `*.pdb` 文件（但发布目录里仍可能出现残留 PDB，所以发布脚本 L81–82 显式删除） |

---

## 2. 构建

### 2.1 准确命令行

```powershell
# 全量（推荐；与本地/后续验证一致）
dotnet build GameOptimizer.sln -c Debug
dotnet build GameOptimizer.sln -c Release

# 串行口径（压住并行写 obj\，与发布脚本 L46–48 一致；本文件 §8 的实测即用这条）
dotnet build GameOptimizer.sln -c Release -m:1 -p:BuildInParallel=false

# 只编主程序（发布/启动问题的最小验证）
dotnet build GameOptimizer.Shell\GameOptimizer.Shell.csproj -c Release

# 需要先还原时（离线环境会打印 NU1900 警告，不影响）
dotnet restore GameOptimizer.sln
```

### 2.2 输出路径

| 工程 | 输出目录 | 关键产物 |
| --- | --- | --- |
| `GameOptimizer.Shell` | `GameOptimizer.Shell\bin\<Config>\net8.0-windows10.0.19041.0\` | `YuqiEngine.exe`、`YuqiEngine.dll`、`YuqiEngine.deps.json`、`YuqiEngine.runtimeconfig.json`（`AssemblyName=YuqiEngine`，csproj L36） |
| `GameOptimizer.Tests` | `GameOptimizer.Tests\bin\<Config>\net8.0-windows10.0.19041.0\` | `GameOptimizer.Tests.dll` + 被测程序集副本 |
| `GameOptimizer.Core` / `Infrastructure.Windows` / `Modules.PerformanceHealth` | `<工程>\bin\<Config>\net8.0-windows10.0.19041.0\` | 同名 DLL |
| `GameOptimizer.ModuleContracts` | `GameOptimizer.ModuleContracts\bin\<Config>\net8.0\`（**没有 windows 后缀**） | `GameOptimizer.ModuleContracts.dll` |

> TFM 后缀容易看错：只有 `ModuleContracts` 是 `net8.0`，其余是 `net8.0-windows10.0.19041.0`。

### 2.3 仓库里还有别的 bin 目录，但都不是发布物

| 目录 | 来源 | 说明 |
| --- | --- | --- |
| `.build-check-bin\<name>\` | 历次滚动构建（2026-09-13 实测 17 个：`esports-ux`、`esports-ux-r2`、`release-notice-r3`、`memory-grouping`、`first-backup-onboarding-r1..r4`、`onboarding-matrix-download-r1/r2`…） | `.gitignore` L8 忽略；属临时验证产物。体积：16 个约 99.7–99.8 MB，`release-notice` 仅 5.0 MB |
| `<工程>\artifacts\<name>\` | 把输出指到工程内的旧构建（如 `GameOptimizer.Shell\artifacts\visual-check\`、`dark-only-review\`、`overview-shell-build\`） | `.gitignore` L4 忽略 |
| `GameOptimizer.Shell\bin\Release\net8.0-windows10.0.19041.0\win-x64\` | `dotnet publish --runtime win-x64`（= 发布脚本）留下的 RID 专属中间输出 | 与同级的非 RID 目录并存；`Publish-Release.ps1` 的最终产物不在这里，而在 `artifacts\release\` |
| `GameOptimizer.Tests\bin\Release\<自定义名>\` | 历史上用自定义输出名/自定义配置构建（实测 `visual-migration-validation`、`visual-migration-validation --logger console`、`hardware-visual-validation`、`overview-v2-validation`） | 其中 `visual-migration-validation --logger console` 明显是命令行参数拼进了输出路径的事故残留 |
| **`artifacts/release/`** | **唯一**发布输出根（见 §3） | `.gitignore` L4 忽略，但它是本链路的目标产物。当前实际含 **2** 个版本目录：`v0.1.3-preview\`（脚本产物，三层 `portable\`/`installer\`/`checksums\`）与 `v0.1.3-preview-fixed\`（**手工重发**，布局不符脚本约定，见 §3.8） |

### 2.4 常见失败原因（本节均为实测）

| 症状 | 真实原因 | 处置 |
| --- | --- | --- |
| `error MSB3491: 未能向文件"obj\…AssemblyInfoInputs.cache"写入行。Access to the path is denied.` | `obj\` 缓存被另一个 `dotnet`/MSBuild/VS 进程占用，或由**提权/其它会话**生成后带上无法被普通令牌覆盖的 ACL。首次核查时实测 `GameOptimizer.ModuleContracts\obj\Debug\net8.0\*AssemblyInfoInputs.cache` 与 `obj\project.assets.json` 的属主为 `BUILTIN\Administrators`；**2026-09-13 复验时两者属主均为普通用户 `ZHIYU\57156`**（`obj\Debug\net8.0.locked\*` 同），该 ACL 子情形在当前机器上已不复现——排查时先用 `(Get-Acl <文件>).Owner` 确认，不要直接假设是 ACL | 关掉 VS/其它构建进程；或用隔离输出绕过（实测有效）：`dotnet build GameOptimizer.sln -c Debug --artifacts-path .build-check-bin\artifacts-verify`；确认属主异常时才需以管理员清理对应 `obj\` |
| 输出只有「生成失败。/ 0 个警告 / 0 个错误 / 已用时间 00:00:01」，看不出原因 | 失败发生在 `Restore` 目标或子工程的 `MSBuild` 任务里，默认 verbosity 把它汇总成 0 错误 | 用 `-v:diag`（或 `-tl:off -v n`）重跑；实测只有 `-v:diag` 才显示出 `MSB3491` 与 `_FilterRestoreGraphProjectInputItems` 失败 |
| `error MSB4276: 默认 SDK 解析程序解析 SDK"Microsoft.NET.SDK.WorkloadAutoImportPropsLocator"失败，因为目录 …\sdk\10.0.302\Sdks\… 不存在` | 本机默认 SDK 是 `10.0.302`，其 `Sdks\` 下没有这两个 workload locator（`8.0.423` 同样没有）——属**环境 SDK 布局**问题，不是工程缺陷 | 一般是噪声；如需干净输出，可在仓库根加 `global.json` 固定 `8.0.423`，或用 VS 自带 MSBuild |
| `error CS1061: "nint"未包含"DangerousGetHandle"的定义` 等编译错误 + 依赖工程 `MSB4181` | 工作区存在**未完成的并发编辑**。首次核查时实测 `GameOptimizer.Core/Services/WindowsElevation.cs` 由另一会话新建且当时仍在写入（文件 mtime 01:02），Core 一挂，`Infrastructure.Windows`/`Shell` 就报 `MSB4181: "MSBuild"任务返回了 false`（**该实例已修复，2026-09-13 复验全量 Release 构建 0 错误，见 §8.2**） | 不是链路问题：等该文件编译通过；定位真实语法/API 错误只需看**第一个** `error CS####`，后面的 `MSB4181` 都是连带失败 |
| `warning NU1900: 获取包漏洞数据时出错: 无法加载源 https://api.nuget.org/v3/index.json` | 当前网络无法访问 NuGet 审计源 | 只是 warning，不影响构建；若本地包缓存缺失，才会升级为 `NU1101`/`NETSDK1004` |
| 构建成功但没有产物变化 | 增量构建认为“最新”；`Directory.Build.targets` 的移除规则或 `obj` 时间戳被扰动 | 需要强制重编时加 `-t:Rebuild` 或清理对应 `obj\`/`bin\` |

---

## 3. 发布链路：`eng/Publish-Release.ps1`

### 3.1 参数与防覆盖

| 参数（L1–10） | 约束 | 默认 | 影响 |
| --- | --- | --- | --- |
| `-Version` | `ValidatePattern '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$'`（L3） | `0.1.3-preview`（L4） | ZIP/EXE 名、`-p:Version`、ISCC `AppVersion`、`artifacts\release\v<Version>` |
| `-Runtime` | `ValidateSet('win-x64')`（L6） | `win-x64` | 目录名与 RID |
| `-Force` | switch（L9） | 关 | **关**：若 `artifacts\release\v<Version>` 已存在直接 `throw`（L29–32）。**开**：`Directory.Delete($releaseRoot, true)`，只清这一个版本目录（L35） |

### 3.2 必须先 restore（脚本内部用了 `--no-restore`，L45）

```powershell
dotnet restore GameOptimizer.Shell\GameOptimizer.Shell.csproj

pwsh -File eng\Publish-Release.ps1 -Version 0.1.3-preview          # 新版本
pwsh -File eng\Publish-Release.ps1 -Version 0.1.3-preview -Force  # 覆盖同一版本目录
```

### 3.3 真实的 publish 命令（L41–56 展开）

```powershell
dotnet publish GameOptimizer.Shell\GameOptimizer.Shell.csproj `
  --configuration Release `
  --no-restore `
  -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts\release\v0.1.3-preview\portable\YuqiEngine-v0.1.3-preview-win-x64 `
  -p:Version=0.1.3-preview `
  -p:PublishSingleFile=false `
  -p:PublishTrimmed=false
```

| 参数 | 为什么这么写 |
| --- | --- |
| `--no-restore` | 发布不重复还原；漏掉前置 `dotnet restore` 就会 `NETSDK1004` |
| `-m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` | 压住并行写 `obj\`，正是 §2.4 第一类 `MSB3491` 的官方缓解手法 |
| `--self-contained true --runtime win-x64` | 目录内自带 .NET 8 运行时；实测 `YuqiEngine.runtimeconfig.json` 记录 `Microsoft.NETCore.App 8.0.29` + `Microsoft.WindowsDesktop.App 8.0.29` |
| `-p:Version=$Version` | 覆盖工程里的 `<Version>`，并写进 `YuqiEngine.deps.json`（实测 `"YuqiEngine/0.1.3-preview"`） |
| `PublishSingleFile=false` | 发布目录保持 `YuqiEngine.exe` + `YuqiEngine.dll` + `YuqiEngine.deps.json` + `runtimeconfig.json` 多文件形态（本项目 `Assets`/`Fonts`/`Resources` 依赖目录结构，不能打单文件） |
| `PublishTrimmed=false` | 与 WPF 反射/XAML 场景不兼容，必须关闭 |

### 3.4 路径公式 ↔ 实际路径（`-Version 0.1.3-preview -Runtime win-x64`）

| 变量（行号） | 公式 | 实际路径 |
| --- | --- | --- |
| `$releaseRoot`（L18） | `artifacts\release\v$Version` | `artifacts\release\v0.1.3-preview\` |
| `$portableRoot`（L19） | `$releaseRoot\portable` | `artifacts\release\v0.1.3-preview\portable\` |
| `$publishDirectory`（L20） | `$portableRoot\YuqiEngine-v$Version-$Runtime` | `…\portable\YuqiEngine-v0.1.3-preview-win-x64\` |
| `$archiveRootName`（L21） | `YuqiEngine-v$Version-$Runtime-portable` | `YuqiEngine-v0.1.3-preview-win-x64-portable` |
| `$archivePath`（L22） | `$portableRoot\$archiveRootName.zip` | `…\portable\YuqiEngine-v0.1.3-preview-win-x64-portable.zip` |
| `$installerRoot`（L23） | `$releaseRoot\installer` | `artifacts\release\v0.1.3-preview\installer\` |
| `$installerBaseName`（L24） | `YuqiEngine-v$Version-$Runtime-setup` | `YuqiEngine-v0.1.3-preview-win-x64-setup` |
| `$installerPath`（L25） | `$installerRoot\$installerBaseName.exe` | `…\installer\YuqiEngine-v0.1.3-preview-win-x64-setup.exe` |
| `$hashPath`（L27） | `$releaseRoot\checksums\SHA256SUMS.txt` | `…\checksums\SHA256SUMS.txt` |

### 3.5 发布后处理顺序（按行号，不要凭印象）

1. **L29–36** 版本目录冲突检查 / `-Force` 清理
2. **L38–39** 建 `portable`、`installer`、`checksums` 三个子目录
3. **L41–59** `dotnet publish`（失败即 `throw "dotnet publish 失败，退出码：…"`）
4. **L61–74** **必需文件白名单**：`YuqiEngine.exe`、`YuqiEngine.deps.json`、`YuqiEngine.runtimeconfig.json`、`GameOptimizer.Core.dll`、`GameOptimizer.Infrastructure.Windows.dll`、`GameOptimizer.Modules.PerformanceHealth.dll`、`Assets\Fonts\OFL-1.1.txt`
   ⚠️ 白名单里**没有** `GameOptimizer.ModuleContracts.dll`（实测存在）与 `modules.json`（实测**不存在**，见 §3.7）
5. **L76–78** 把 `README.md`、`README_EN.md`、`LICENSE` 拷进发布目录（所以发布目录里的 README 版本徽章会随仓库一起漂移，见 §5.3）
6. **L81–82** 递归删除发布目录内所有 `*.pdb`
7. **L84–103** 打 ZIP：`ZipArchiveMode.Create` + `CompressionLevel.Optimal`；**每个 entry 都加前缀 `$archiveRootName/`**（L92–93）→ 解压后得到 `YuqiEngine-v0.1.3-preview-win-x64-portable/`（实测 510 个 entry，首条为 `…-portable/Accessibility.dll`）；打包时再按扩展名过滤一次 `.pdb`（L90）
8. **L105–122** ISCC 编译安装包（见 §4）
9. **L124–129** 写 `checksums/SHA256SUMS.txt`：只有 ZIP 与 setup EXE 两行，格式 `<小写 sha256>␣␣<文件名>`（实测两行）。README「校验下载文件」（`README.md` L124–132）让用户用 `Get-FileHash -Algorithm SHA256` 对照该文件

### 3.6 ISCC 定位顺序（L109–117）

1. `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` ← **本机命中**
2. `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
3. `C:\Program Files\Inno Setup 6\ISCC.exe`

三个都找不到 → `throw '未找到 Inno Setup 6。请安装后重新运行发布脚本。'`（L116）。L120 还会要求 `$LASTEXITCODE -eq 0` **且** setup EXE 真实存在。

### 3.7 常见失败原因

| 症状 | 原因（行号） | 处置 |
| --- | --- | --- |
| `发布目录已存在：… 请使用新版本号` | L29–32 防覆盖 | 换版本号，或 `-Force`（只删该版本目录） |
| `dotnet publish 失败，退出码：N` | L57–59 | 先按 §2 单独 `dotnet build … -c Release` 复现真实编译错误 |
| `NETSDK1004: Assets file … not found` | L45 `--no-restore` 但没先 restore | `dotnet restore GameOptimizer.Shell\GameOptimizer.Shell.csproj` |
| `发布目录缺少必要文件：…` | L71–74 白名单 | 说明 publish 不完整（常见于 restore 缺失、Assets 未复制） |
| `未找到 Inno Setup 6` | 三个候选路径都没有 | 安装 Inno Setup 6（用默认用户级安装即可被命中） |
| `安装包生成失败，退出码：N` | ISCC 报错：`.iss` L48 引用的 `languages\ChineseSimplified.isl` 缺失、L33 `..\GameOptimizer.Shell\Assets\app.ico` 缺失、L35 `..\LICENSE` 缺失等 | 看 ISCC 原始输出；本仓库这三个文件实测都在 |
| ZIP 解压层数与说明不符 | 改了 `$archiveRootName` 却没同步 README 的「ZIP 免安装版」说明 | ZIP 内根目录 = 便携目录名 + `-portable`，两者必须一致 |
| 便携版里模块不生效且**毫无提示** | `GameOptimizer.Modules\modules.json` 只存在于源码树，没有任何 csproj 把它声明为 `Content`/`None`，因此**不进发布目录**（实测发布目录无此文件）；而 `ModuleManifestProvider.Load()`（`GameOptimizer.Modules/ModuleManifestProvider.cs` L12–38：文件缺失 L14–18、读取失败 L23–27、反序列化异常 L33–37 三条路径）在文件缺失/读取失败时**只写 `Debug.WriteLine` 就返回空清单**，`ModuleLoader.RegisterModule` 的异常也被 `Debug.WriteLine` 吞掉（`ModuleLoader.cs` L66–69）；Release 下没有 Debug 监听器 ⇒ 完全无痕 | 若要启用清单式模块装载，必须把 `modules.json` 加进 Shell 的 `Content` 并 `CopyToOutputDirectory`；否则 `App.xaml.cs` L253 的 `ModuleLoader.RegisterConfiguredModules` 是静默空操作（当前模块实际靠 L252 `services.AddModule<PerformanceHealthModule>()` 显式注册） |

### 3.8 人工产物偏离脚本约定（实测）

`artifacts/release/v0.1.3-preview-fixed/` **不符合**脚本布局。实测内容（2026-09-13）：

```
artifacts\release\v0.1.3-preview-fixed\
├─ installer\YuqiEngine-v0.1.3-preview-win-x64-setup.exe   (100,623,497 字节)
├─ YuqiEngine-v0.1.3-preview-win-x64\                      ← 便携目录直接在版本根（没有 portable\）
├─ YuqiEngine-v0.1.3-preview-win-x64-portable.zip
└─ SHA256SUMS.txt                                          ← 哈希在版本根（没有 checksums\）
```

内部文件名仍是 `…-win-x64-setup.exe` / `…-win-x64-portable.zip` → 它是 `0.1.3-preview` 的**手工重发**，不是用 `-Version 0.1.3-preview-fixed` 跑出来的（脚本会命名成 `…-v0.1.3-preview-fixed-…`）。

- ⚠️ **它不是 `v0.1.3-preview` 的副本**：同名 setup EXE 的大小/哈希都不同（`-fixed` 为 `100,623,497` 字节 / `b49217dd…aaf2`，`v0.1.3-preview` 为 `100,632,641` 字节 / `477d7f69…c9a1`），ZIP 也各自不同 → 两套包是**两次不同的构建**，不能互相替代。
- 脚本的 `-Force` 只清理 `artifacts\release\v<Version>`，这类手工目录**永远不会**被脚本识别或更新（`v0.1.3-preview-fixed` 的目录名也不匹配任何 `$releaseRoot`，除非有人真的传 `-Version 0.1.3-preview-fixed`）。
- 更早的 `artifacts/release/v0.1.3-preview/使用前必读.txt`（首行为「宇奇引擎 Yuqi Engine v0.1.3-preview 使用前必读」）同样不是当前脚本产物（脚本 L76–78 只拷 README/LICENSE 三个文件）。
- 判断“用户装的到底是哪个包”时，以 `checksums/SHA256SUMS.txt`（`v0.1.3-preview`）或版本根 `SHA256SUMS.txt`（`-fixed`）+ EXE 的 `ProductVersion` 为准（含 `+git sha`）。

---

## 4. 安装器：`installer/YuqiEngine.iss`

### 4.1 预处理器定义 ↔ 发布脚本传参（一一对应）

| `.iss` 定义 | 兜底值 | 由谁注入（脚本 L119） | 实际值 |
| --- | --- | --- | --- |
| `AppVersion`（L2–4） | `"0.1.3-preview"` | `/DAppVersion=$Version` | `0.1.3-preview` |
| `SourceDir`（L5–7） | **无兜底**，缺失即 `#error` | `/DSourceDir=$publishDirectory` | `…\portable\YuqiEngine-v0.1.3-preview-win-x64` |
| `OutputDir`（L8–10） | **无兜底**，缺失即 `#error` | `/DOutputDir=$installerRoot` | `artifacts\release\v0.1.3-preview\installer` |
| `OutputBaseFilename`（L11–13） | `YuqiEngine-Setup` | `/DOutputBaseFilename=$installerBaseName` | `YuqiEngine-v0.1.3-preview-win-x64-setup` |

> **直接双击/单独编译 `.iss` 一定会失败**（缺 `SourceDir`/`OutputDir` 命中 `#error`）。它只能由 `eng/Publish-Release.ps1` 带这 4 个宏调用。

### 4.2 关键项 → 落盘/落注册表位置

| `.iss` 行 | 项 | 效果 |
| --- | --- | --- |
| L20 | `AppId={{B01BC15D-443A-4D6F-85E9-7A6D7E80E2B7}` | 升级/卸载识别用的固定 GUID；改它等于变成另一个产品 |
| L21–22 | `AppName={#AppName}` / `AppVersion={#AppVersion}` | 控制面板「程序和功能」里的名称与版本 = 发布版本号（实测重编产物 `Product=Yuqi Engine`、`ProductVersion=0.1.3-preview`） |
| L27 | `DefaultDirName={autopf}\YuqiEngine` | 默认安装到 `C:\Program Files\YuqiEngine` |
| L31–32 | `OutputDir` / `OutputBaseFilename` | 就是 §3.4 的安装包路径 |
| L33 | `SetupIconFile=..\GameOptimizer.Shell\Assets\app.ico` | 相对 `.iss` 所在目录（`installer\`）解析 |
| L34 | `UninstallDisplayIcon={app}\YuqiEngine.exe` | 卸载项图标取自发布目录 exe |
| L35 | `LicenseFile=..\LICENSE` | 安装向导的许可页；同样相对 `installer\` |
| L36–38 | `Compression=lzma2/ultra64` + `SolidCompression=yes` | 打包最慢档（实测约 63 秒/次），别当成卡死 |
| L40–41 | `ArchitecturesAllowed=x64compatible` / `InstallIn64BitMode=x64compatible` | 只允许 x64 安装 |
| L42–43 | `PrivilegesRequired=admin` + `PrivilegesRequiredOverridesAllowed=dialog` | 默认要求管理员，用户可在向导里选择“仅为当前用户安装”；与 `GameOptimizer.Shell/app.manifest` L19 的 `requestedExecutionLevel level="highestAvailable"` 一起决定「以管理员身份运行后权限状态」的表现 |
| L44–45 | `CloseApplications=yes` / `RestartApplications=no` | 覆盖安装时提示关闭正在运行的 `YuqiEngine.exe` |
| L48–49 | `chinesesimp`（`languages\ChineseSimplified.isl`）+ `english`（`compiler:Default.isl`） | 安装向导双语；`installer\languages\ChineseSimplified.isl` 实测存在 |
| L52 | `desktopicon` 任务 | 桌面快捷方式默认不勾选 |
| L55 | `Source: "{#SourceDir}\*"; … recursesubdirs createallsubdirs` | 发布目录**整棵**进 `{app}`（含十几个运行时语言子目录） |
| L58–59 | `{autoprograms}\Yuqi Engine` / `{autodesktop}\Yuqi Engine`（`DefaultGroupName={#AppName}`，L28） | 开始菜单（程序组页被 `DisableProgramGroupPage=yes` 关闭，L29）/ 桌面快捷方式（仅当勾选 L52 的 `desktopicon` 任务） |
| L62 | `[Run] … nowait postinstall skipifsilent` | 安装完成页可勾选“立即运行” |

### 4.3 已验证：用现有发布目录重编安装包（逐字节一致）

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" `
  "/DAppVersion=0.1.3-preview" `
  "/DSourceDir=<repo>\artifacts\release\v0.1.3-preview\portable\YuqiEngine-v0.1.3-preview-win-x64" `
  "/DOutputDir=<临时目录>" `
  "/DOutputBaseFilename=YuqiEngine-v0.1.3-preview-win-x64-setup" `
  installer\YuqiEngine.iss
```

实测结果：`Successful compile (63.359 sec)`，退出码 0，产物 `100,632,641` 字节，SHA256 `477d7f69…c9a1`，**与仓库里已发布的 `artifacts/release/v0.1.3-preview/installer/YuqiEngine-v0.1.3-preview-win-x64-setup.exe` 逐字节相同**（也正是 `checksums/SHA256SUMS.txt` 的第二行）。

> 结论：§3.3 → §4.1 的「版本号 / 源目录 / 输出目录 / 输出名」传递链在现有发布目录上是**可复现**的，且该步骤确定性（同输入同哈希）。

### 4.4 常见失败原因

| 症状 | 原因 | 处置 |
| --- | --- | --- |
| `#error SourceDir must point to the verified publish directory` / `#error OutputDir must …` | 没带 `/D` 编译 | 必须由发布脚本调用，或手工补 4 个 `/D` |
| `Could not read file "languages\ChineseSimplified.isl"` | 缺 `installer\languages\ChineseSimplified.isl` | 从仓库补齐（该文件是安装向导中文语言包） |
| `SetupIconFile`/`LicenseFile` 找不到 | `.iss` 里的相对路径是相对 `installer\` 的，打包时移动了目录结构就会失效 | 保持 `installer\` 与仓库根的相对关系 |
| 编译很慢、像卡死 | `lzma2/ultra64` + 100 MB 运行时 | 正常约 1 分钟；CI 里给足超时 |
| 安装包体积异常大 | `SourceDir` 指到了未裁剪的 `bin\`（含 PDB、测试 DLL）而不是发布目录 | 只允许指向 `artifacts/release/.../portable/...` |
| 装不上/装到意外位置 | `DefaultDirName={autopf}`、`PrivilegesRequired=admin` 与用户选择有关 | 需要“按用户安装”时由用户在向导里选择（L43 允许） |

---

## 5. 版本号来源（唯一真相 vs 漂移点）

### 5.1 全部来源

| # | 位置 | 值 | 谁写入 | 说明 |
| --- | --- | --- | --- | --- |
| 1 | `GameOptimizer.Shell/GameOptimizer.Shell.csproj` L41–44 | `Version` / `AssemblyInformationalVersion` = `0.1.3-preview`；`FileVersion` / `AssemblyVersion` = `0.1.3.0` | 人工 | 编译期默认值；`-p:Version` 发布时只覆盖前者 |
| 2 | `eng/Publish-Release.ps1` L4 默认参数 + L52 `-p:Version=$Version` | `0.1.3-preview` | 发布者 | **发布链路的权威入口** |
| 3 | 程序集 `InformationalVersion` 的 `+<git sha>` 后缀 | `0.1.3-preview+f99ed5d8e5ef8122ffc7820198dac5e8346f0588` | SDK（SourceLink / `IncludeSourceRevisionInInformationalVersion`） | 自动附加，不能手改；实测来自已发布 exe 的 `ProductVersion` |
| 4 | `installer/YuqiEngine.iss` L2–4 `AppVersion` 兜底 | `0.1.3-preview` | 人工 | 只在缺 `/DAppVersion` 时生效 |
| 5 | 起屏显示 `GameOptimizer.Shell/Startup/SplashWindow.xaml.cs` L152–167 | 读 `AssemblyInformationalVersion`，为空退化到 `GetName().Version?.ToString(3)` | 运行时 | 来源 2/3（因此会显示 `+sha`） |
| 6 | UI 硬编码（3 个文件 4 处，全部为字面量 `v0.1.3-preview`）：`Views/AboutDialog.xaml` L52、`Views/SettingsPage.xaml` L469、`MainWindow.xaml` L349 + L538 | `v0.1.3-preview` | 人工 | **与来源 2 无联动**；改版本号时这 4 处必须手工改（`MainWindow.xaml` 的两处在不同区域模板里，最容易漏） |
| 7 | 发布提示 `GameOptimizer.Shell/Services/ReleaseNoticePolicy.cs` L10 | `CurrentNoticeId = "v0.1.3-preview-scope-brief-2026-08"` | 人工 | 字符串内含版本+日期，改版本时的隐藏必改点 |
| 8 | `README.md` L10 徽章 + L68「最后公开体验版本」；`README_EN.md` L10/L68 | `v0.1.2` | 人工 | **已漂移**（落后于 `0.1.3-preview`）；且 README 会被拷进发布目录（脚本 L76–78） |
| 9 | `artifacts/release/v<Version>/` 目录名 + ZIP/EXE 文件名 | `v0.1.3-preview` | 脚本 | 目录/文件名即“发布参数”的现场证据，可反查 `-Version` |

### 5.2 一次发布里版本号怎么流

```
Publish-Release.ps1 -Version V
  ├─ 目录名 / ZIP 名 / setup 名        →  artifacts\release\vV\…（含 V 的物理路径）
  ├─ dotnet publish -p:Version=V       →  YuqiEngine.deps.json 里的 "YuqiEngine/V"（实测 0.1.3-preview）
  │                                      →  InformationalVersion = V + "+<git sha>"（实测 ProductVersion=0.1.3-preview+f99ed5d…）
  │                                      →  注意：FileVersion/AssemblyVersion 仍取 csproj 的 0.1.3.0
  ├─ ISCC /DAppVersion=V               →  setup.exe 的 ProductVersion / 控制面板版本（实测 0.1.3-preview）
  └─ checksums\SHA256SUMS.txt          →  只记录**文件名**（文件名里带 V）
```

### 5.3 版本漂移清单（核查时点）

| 现象 | 证据 | 影响 |
| --- | --- | --- |
| 代码/发布物是 `0.1.3-preview`，README 徽章与「最后公开体验版本」是 `v0.1.2` | `README.md` L10/L68、`README_EN.md` L10/L68（grep 全仓库仅此四处写 `0.1.2`） | 用户按 README 判断版本会误判；README 还会被打进发布目录 |
| `FileVersion=0.1.3.0` 但 `Version=0.1.3-preview` | csproj L41–44；已发布 exe 实测 `FileVersion=0.1.3.0` | 资源管理器「文件版本」看不到 `-preview` 后缀 |
| 起屏版本（读程序集）与 About/Settings/主窗口显示的版本（硬编码）不同源 | `SplashWindow.xaml.cs` L152–167 vs `AboutDialog.xaml` L52 / `SettingsPage.xaml` L469 / `MainWindow.xaml` L349+L538 | 改版本号时容易只改一半；硬编码共 4 处 |
| `.iss` 里的 `AppVersion` 兜底值需要跟着改 | `.iss` L3 | 手工编译 `.iss` 时会打出旧版本号 |
| `使用前必读.txt`、`v0.1.3-preview-fixed\` 是历史/手工产物 | §3.8 | 容易被误认为脚本的一部分 |

---

## 6. 测试布局

### 6.1 测试工程与框架

| 项 | 值 | 位置 |
| --- | --- | --- |
| 工程 | `GameOptimizer.Tests/GameOptimizer.Tests.csproj` | — |
| TFM | `net8.0-windows10.0.19041.0` | L3 |
| 框架 | `Microsoft.NET.Test.Sdk 17.12.0`、`xunit 2.9.2`、`xunit.runner.visualstudio 2.8.2` | L10–12 |
| 被引用工程 | Core、Infrastructure.Windows、**Shell** | L16–18 |
| 全局 using | `GameOptimizer.Tests.Changes`、`Xunit` | L27–28 |
| runner 配置 | `xunit.runner.json` → 输出目录 | L32 |
| `IsPackable` | `false` | L6 |

- 引用 **Shell** 意味着：**想跑测试必须先能编过 WPF 主程序**（`UseWPF`、XAML、`TreatWarningsAsErrors` 全部生效）。
- `Changes` 被提升为全局 using（L27）——它既是最大测试区，也是跨区共享测试替身的存放地。

### 6.2 `GameOptimizer.Tests/xunit.runner.json`

```json
{ "parallelizeTestCollections": false, "parallelizeAssembly": false, "maxParallelThreads": 1 }
```

→ **全串行**。测试大量读写真实系统状态（注册表、服务、电源方案、进程、文件系统），串行是刻意设计；代价是全量很慢，调试时应优先用 `--filter`。

### 6.3 目录 → 关注点（文件数 = 非 bin/obj 的 `.cs` 数；用例数 = `[Fact]`+`[Theory]` 出现次数）

| 目录 | `.cs` | 用例 | 关注点 |
| --- | --- | --- | --- |
| `Changes/` | 92 | 657 | 体量最大：按“改动批次”堆积的回归测试（全局 using 也指向它） |
| `Shell/` | 20 | 105 | 外壳、导航、服务集成 |
| `PowerManagement/` | 14 | 70 | 电源方案与门禁 |
| `Visual/` | 11 | 82 | 视觉/布局/渲染断言 |
| `Snapshot/` | 8 | 52 | 快照与恢复 |
| `Baseline/` | 7 | 37 | 基线首备流程与授权 |
| `Optimization/` | 6 | 24 | 优化项判定 |
| `SystemRestore/` | 4 | 46 | 系统还原点 |
| `Esports/` `GraphicsSafety/` `FileShellSafety/` | 3/3/3 | 20/14/18 | 电竞模式、显卡安全、文件与外壳安全 |
| `Architecture/` `Compatibility/` `Core/` `CpuAffinity/` `Diagnostics/` `Localization/` `Navigation/` `ProcessTuning/` `Safety/` `Security/` `Telemetry/` `Utilities/` `ViewModels/` | 各 1–5 | 各 1–21 | 单点约束（架构、平台兼容、本地化、遥测…）；其中 `ProcessTuning/` 5 个文件 21 例、`ViewModels/` 3 个文件 8 例，是这一档里偏大的两个 |
| **合计** | **194** | **1244** | 共 **24** 个测试目录 |

> `artifacts/`、`bin/`、`obj/`、`TestResults/` 是输出目录，不算测试源码。

### 6.4 命令与产物落点

```powershell
# 全量（推荐用主 sln）
dotnet test GameOptimizer.sln -c Debug

# 只跑测试工程
dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Debug

# 已构建过，不重新编译
dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Debug --no-build

# 缩小范围（强烈建议，全量串行很慢）
dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Debug --filter "FullyQualifiedName~PowerManagement"
dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Debug --filter "DisplayName~Memory"
```

| 产物/证据 | 路径 |
| --- | --- |
| 测试程序集 | `GameOptimizer.Tests\bin\Debug\net8.0-windows10.0.19041.0\GameOptimizer.Tests.dll` |
| 测试自己写出的证据目录 | `GameOptimizer.Tests\artifacts\<run-name>\…`（实测例：`artifacts\workspace-layer-check\coordination-test-evidence\…`、`artifacts\dark-only-check\…`） |
| VSTest 结果 | `TestResults/`（`.gitignore` L5 忽略） |

- 用 `-c Release` 跑测试会连带用 Release 规则编译 Shell（`TreatWarningsAsErrors=true`）——警告即失败。
- 测试运行时的“验收开关”（`--acceptance-*`、`--recovery-dev` 等）属于**应用进程**参数，由 `Program.cs`/`App.OnStartup` 解析（见 [01 文档](./01-startup-and-shell.md) §1.2、§2.1），**不是** `dotnet test` 的参数。

### 6.5 常见失败原因

| 症状 | 原因 | 处置 |
| --- | --- | --- |
| `Testhost 进程已退出，但出现错误: Unhandled exception. System.ComponentModel.Win32Exception (5): 拒绝访问`，栈为 `ProcessManager.OpenProcess` → `Process.GetProcessHandle` → `DefaultEngineInvoker.SetParentProcessExitCallback` | testhost 启动时要 `OpenProcess` 父进程（`dotnet test`）以注册父进程退出回调，在受限令牌/沙箱环境被拒绝。**同一台机上 `dotnet --info` 也会在 `ProcessExtensions.GetParentProcessId` 抛同一个 `Win32Exception (5)`** ⇒ 环境限制，不是测试或代码缺陷 | 在普通（非沙箱/非受限令牌）终端运行；必要时在管理员终端复跑 |
| 测试还没开始就 `生成失败` / `MSB3491` | 见 §2.4（`obj\` 被占用或 ACL 异常） | 同上，或 `--artifacts-path` 隔离输出 |
| `找不到提供的测试源文件"…\debug_win-x64\GameOptimizer.Tests.dll"` | `--arch x64` 会把输出目录换成 `debug_win-x64`，与已构建的 `debug` 目录不一致 | 要么不带 `--arch`，要么用同样参数先构建 |
| 单跑很快、全量极慢 | `xunit.runner.json` 全串行 | 用 `--filter` 分片 |
| `dotnet test GameOptimizer.sln` 报某个依赖工程编译错误 | 测试工程引用 Shell ⇒ 主程序必须先编过（§6.1） | 先 `dotnet build GameOptimizer.Shell\GameOptimizer.Shell.csproj -c Debug` |

---

## 7. 症状 → 入口 速查

| 症状 / 需求 | 先看这里 |
| --- | --- |
| 用户反馈“版本不对” | `artifacts/release/v<Version>/checksums/SHA256SUMS.txt` + EXE 属性 `ProductVersion`（含 `+git sha`）→ 反查 `-Version`（§3.4、§5.1） |
| 发布脚本报“目录已存在” | §3.1 `-Force` 语义 |
| 安装包生成失败 | ISCC 原始输出 + §4.1 的 4 个 `/D` + §4.4 的相对路径资源 |
| 便携版“模块不生效但没有任何提示” | `modules.json` 未随发布 + `ModuleManifestProvider.Load()` 只有 `Debug.WriteLine`（§3.7 末行） |
| 构建“0 错误却生成失败” | §2.4 第二行（必须 `-v:diag`） |
| 构建报 `Access to the path … denied` | §2.4 第一行（`obj\` 占用/ACL，或曾用管理员构建过） |
| 测试跑不起来 | §6.5 第一行（环境令牌）vs §2.4（`obj\`） |
| 想知道“用哪个 sln / 产物在哪” | §1.1、§2.2、§3.4 |

---

## 8. 验证记录

### 8.1 首次核查（2026-09-13 01:00 前后，HEAD = `aa45545`）

**已验证（命令 + 观测结果）**

1. `dotnet build GameOptimizer.Core\GameOptimizer.Core.csproj -c Debug --artifacts-path .build-check-bin\artifacts-core` → **成功**，0 错误。
2. `dotnet build GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Debug --artifacts-path .build-check-bin\artifacts-tests -m:1 -nodeReuse:false -p:BuildInParallel=false` → **成功**，0 错误（13 条警告全为 `NU1900`），产出 `GameOptimizer.Tests.dll`。
3. `pwsh -NoProfile -File eng\Verify-EvidenceIsolation.ps1` → `Evidence isolation verification passed.`，退出码 0，并输出 9 个受保护文件（4 个库 csproj + Shell/Tests csproj + 两个 sln + `Directory.Build.targets`）的 SHA256。
4. 用现有发布目录 + 4 个 `/D` 重新编译安装包 → `Successful compile (63.359 sec)`，退出码 0，产物与已发布安装包**逐字节相同**（`100,632,641` 字节，SHA256 `477d7f69…c9a1`）。
5. 发布物取证（只读）：`YuqiEngine.deps.json` → `"YuqiEngine/0.1.3-preview"`；`YuqiEngine.exe` → `FileVersion=0.1.3.0`、`ProductVersion=0.1.3-preview+f99ed5d…`（`git log` 中 `f99ed5d` = “Prepare v0.1.3-preview release”）；`checksums/SHA256SUMS.txt` 两行；ZIP 510 个 entry 且根目录为 `YuqiEngine-v0.1.3-preview-win-x64-portable/`；发布目录内 `GameOptimizer.ModuleContracts.dll` 存在、`modules.json` 不存在。

**当时未通过 / 受限**

- `dotnet build GameOptimizer.sln` 失败，失败点是并发写入中的 `GameOptimizer.Core/Services/WindowsElevation.cs`（`error CS1061`，当时仍是未跟踪的新文件），`Infrastructure.Windows`/`Shell` 报 `MSB4181` 连带失败 ⇒ 并发编辑态，非链路缺陷。
- `dotnet test` 未能真正执行用例（see §8.2）。

### 8.2 文档复验（2026-09-13 19:45–19:54，HEAD 仍为 `aa45545`）

> 复验只重跑命令 + 只读取证，**不修改任何产品代码**；本节结论仅覆盖下列命令与文件。

**已复验（命令 + 实测输出）**

| # | 命令 / 取证 | 观测结果 |
| --- | --- | --- |
| 1 | `dotnet build GameOptimizer.sln -c Release -m:1 -p:BuildInParallel=false -nodeReuse:false` | **已成功生成：0 个警告、0 个错误**，用时 56.23 s；6 个工程全部产出（`ModuleContracts`→`bin\Release\net8.0\`，其余→`bin\Release\net8.0-windows10.0.19041.0\`）。§8.1 的 `CS1061`/`MSB4181` 已不复现 |
| 2 | `Get-Content artifacts\release\v0.1.3-preview\checksums\SHA256SUMS.txt` | 两行，文件名与 §3.4 公式一致：`…-win-x64-portable.zip`、`…-win-x64-setup.exe` |
| 3 | `(Get-Item …setup.exe).VersionInfo` | `ProductVersion=0.1.3-preview`、`ProductName=Yuqi Engine`、`FileVersion` 为空、`100,632,641` 字节（与 §4.3 的 `477d7f69…` 记录同一文件） |
| 4 | `(Get-Item …YuqiEngine.exe).VersionInfo` | `FileVersion=0.1.3.0`、`ProductVersion=0.1.3-preview+f99ed5d8e5ef8122ffc7820198dac5e8346f0588`（§5.1 来源 1/3 现场证据） |
| 5 | ZIP 只读枚举 `ZipFile::OpenRead` | 510 个 entry，首条 `YuqiEngine-v0.1.3-preview-win-x64-portable/Accessibility.dll`，ZIP 133.1 MB（磁盘 `139,596,252` 字节） |
| 6 | `Test-Path` 发布目录内文件 | `GameOptimizer.ModuleContracts.dll` = 存在；`modules.json` = **不存在**（§3.5 第 4 步、§3.7 末行成立） |
| 7 | `Get-ChildItem docs\code-map\*.md` + `(Get-Content <f>).Count` | 19:5x 读数：目录下 7 个 `.md`（6 份地图 + `README.md` 索引），`02`=275、`04`=829、`05`=471、`90`=72（当时稳定），`01`≈406、`03`≈1070→1071（这两份核查期间被另一路编辑持续写入）。⚠️ **该读数已过期**：`02`/`03`/`01` 之后又被改动（工作树最后写入 20:08 / 20:45 / 22:54），最新读数与核对口径见 §8.3 第 10 行，`README.md` §二 已按 §8.3 的读数改写 |
| 7b | 全量核对各文档 `## ` 标题 | `README.md` §一 引用的跨文档小节号（`01` §5/§10、`02` §2/§3、`03` §14、`04` §1/§2/§6、`05` §2/§3）**全部存在** |
| 7c | `Test-Path` 文档内全部仓库相对路径 | `05` 与 `README.md` 中出现的每一个 `GameOptimizer*`/`eng`/`installer`/`artifacts`/`docs` 相对路径均存在（0 处失效） |
| 8 | 全仓 grep `0\.1\.2` | 仅 `README.md` L10/L68 与 `README_EN.md` L10/L68 四处（§5.3 成立）；grep `0\.1\.3` 另命中 `MainWindow.xaml` L349/L538（已补进 §5.1 来源 6） |
| 9 | 全仓 grep `GameOptimizer\.Main\.sln` | 除 `eng/Verify-EvidenceIsolation.ps1` L20 外，仅 `docs/code-map/05-engineering-pipeline.md` 自身引用（§1.1 成立） |
| 10 | `Get-ChildItem .build-check-bin` / 体积统计 | 17 个子目录，16 个约 99.7–99.8 MB，`release-notice` 5.0 MB；`.gitignore` L8 已覆盖（§2.3 已按实测值改写） |
| 11 | `git rev-parse --short=12 HEAD` | `aa4554598b1a`，与文首 `aa45545` 锚点一致；`WindowsElevation.cs` 至今**未跟踪**、`eng/Publish-Release.ps1` 相对 HEAD 有未提交改动（§5 所引用的行号取自**当前工作树文件**，非 HEAD 版本） |

**仍未通过 / 受限（不要据此声称通过）**

1. **`dotnet test` 依然无法执行用例**（2026-09-13 复跑 `dotnet test GameOptimizer.Tests\GameOptimizer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~GameOptimizer.Tests.Utilities"`）：testhost 仍以 `Unhandled exception. System.ComponentModel.Win32Exception (5): 拒绝访问` 中止，栈仍为 `ProcessManager.OpenProcess` → `Process.GetProcessHandle` → `DefaultEngineInvoker.SetParentProcessExitCallback`（testhost.x86 L230/L132/L65）。同机 `dotnet --info` 同样在 `ProcessExtensions.GetParentProcessId` 抛 `Win32Exception (5)`。⇒ **环境/令牌限制，不是测试或代码缺陷**，与 §6.5 第一行一致；需在普通（非受限令牌）终端复跑才能得到用例级结论。
2. **未执行完整发布脚本** `eng/Publish-Release.ps1`：它会 `Directory.Delete($releaseRoot, true)` 删掉 `artifacts/release/v0.1.3-preview` 并重打 133 MB ZIP，属破坏性操作且超出核查边界 ⇒ §3.3 的 publish 命令是按脚本**字面量**逐参数核对（L41–55）+ 现有产物反推；**ISCC 段（§4.3）是首次核查时单独实测**，本次复验用哈希/版本信息间接复核同一产物，未重编。
3. **§6.3 的目录用例数**为静态统计（`[Fact]`+`[Theory]` 出现次数，194 个 `.cs` / 1244 处），**未经 testhost 实际跑出**；因第 1 条限制，无法用运行结果交叉验证。
4. 环境事实（本次复测）：默认 SDK `10.0.302`（另装 `8.0.423`），仓库**无 `global.json`**；运行时 `Microsoft.NETCore.App/WindowsDesktop.App 8.0.29` 与 `10.0.10`；`8.0.423` 与 `10.0.302` 的 `Sdks\` 下**均无** `Microsoft.NET.SDK.WorkloadAutoImportPropsLocator`（§2.4 第三行成立）。NuGet 源可达性未再单独探测，本次全量构建 0 警告（未出现 `NU1900`）。
5. 遗留物（仍在）：`GameOptimizer.ModuleContracts\obj\Debug\net8.0.locked\`（旧缓存目录）。本次复验用普通 `dotnet build`（未加 `--artifacts-path`），因此向各工程的 `bin\`/`obj\` 写入了 Release 产物；未新增临时目录。

### 8.3 工程链路文档复核（2026-09-13 22:57，HEAD 仍为 `aa45545`）

> 本轮只做「文档 ↔ 仓库」对照：**逐行读取**脚本 / 工程文件 / 安装器核对行号与字面量，并只读取证发布物。**本轮未重跑构建、未执行测试、未执行发布脚本**；§8.1/§8.2 的运行记录是另外的时间点，不能与本轮混用。

**已复核（读取脚本/工程文件 + 只读取证）**

| # | 对照项 | 观测结果 |
| --- | --- | --- |
| 1 | `eng/Publish-Release.ps1`（133 行） | §3.1–§3.6 引用的行号全部命中：`param` L1–10（`-Version` 模式 L3 / 默认值 L4、`-Runtime` L6–7、`-Force` L9）、`$releaseRoot` L18、`$publishDirectory` L20、`$archiveRootName` L21、`$archivePath` L22、`$installerRoot` L23、`$installerBaseName` L24、`$installerPath` L25、`$hashPath` L27、防覆盖 L29–36（`-Force` 删除在 L35）、建三层目录 L38–39、publish 参数数组 L41–55（`--no-restore` L45、`-m:1`/`BuildInParallel`/`UseSharedCompilation` L46–48、`-p:Version=$Version` L52）、退出码校验 L57–59、必需文件白名单 L61–74、README/README_EN/LICENSE 拷贝 L76–78、删 PDB L81–82、ZIP L84–103（前缀 L93）、ISCC 候选 L109–117（报错 L116）、4 个 `/D` 调用 L119、成功校验 L120–122、SHA256SUMS L124–129 |
| 2 | `installer/YuqiEngine.iss`（62 行） | §4.1/§4.2 引用的行号全部命中：`AppVersion` L2–4、`SourceDir` `#error` L5–7、`OutputDir` `#error` L8–10、`OutputBaseFilename` L11–13、`AppId` L20、`DefaultDirName={autopf}\YuqiEngine` L27、`OutputDir`/`OutputBaseFilename` L31–32、`SetupIconFile` L33、`UninstallDisplayIcon` L34、`LicenseFile` L35、`Compression`/`SolidCompression` L36–37、架构 L40–41、`PrivilegesRequired`/`OverridesAllowed` L42–43、`CloseApplications`/`RestartApplications` L44–45、语言 L48–49、`desktopicon` L52、`[Files]` 递归复制 L55、`[Icons]` L58–59、`[Run]` L62。`installer\languages\ChineseSimplified.isl`、`GameOptimizer.Shell\Assets\app.ico`、`LICENSE` 均存在（§4.4 第 2–3 行成立） |
| 3 | `GameOptimizer.Shell\GameOptimizer.Shell.csproj`（151 行） | L36 `AssemblyName=YuqiEngine`、L46 `StartupObject`、L41–44 版本四项（`Version`/`AssemblyInformationalVersion`=`0.1.3-preview`，`FileVersion`/`AssemblyVersion`=`0.1.3.0`）、L49–55 Release 条件块（含 `TreatWarningsAsErrors` L54）、L57–62 四个 `..\GameOptimizer.Modules\*.cs` 直接 `Compile Include` 全部命中（§1.2、§2.2、§5.1 来源 1 成立） |
| 4 | `GameOptimizer.Tests\GameOptimizer.Tests.csproj`（34 行） | L3 TFM、L6 `IsPackable=false`、L10–12 三个测试包版本（17.12.0 / 2.9.2 / 2.8.2）、L16–18 引用 Core / Infrastructure.Windows / **Shell**、L27–28 全局 using、L32 `xunit.runner.json` 复制项全部命中（§6.1 成立） |
| 5 | 版本字面量 grep | 硬编码 `v0.1.3-preview` 仍为 **4 处**：`Views/AboutDialog.xaml` L52、`Views/SettingsPage.xaml` L469、`MainWindow.xaml` L349 + L538；`Services/ReleaseNoticePolicy.cs` L10 `CurrentNoticeId` 命中；`README.md`/`README_EN.md` 的 `v0.1.2` 仍在 L10 与 L68；`Startup/SplashWindow.xaml.cs` L152–167 `LoadVersion()` 读 `AssemblyInformationalVersion`、空则回退 `GetName().Version?.ToString(3)`（§5.1 全部来源成立） |
| 6 | `Directory.Build.targets`（22 行）/ `.gitignore`（42 行）/ `eng/Verify-EvidenceIsolation.ps1`（73 行） | L4 `EvidenceIsolationGlobs` 恰为 13 条、L8–11 Release 符号项、L14–20 七类 `Remove` 命中；`.gitignore` L4 `artifacts/`、L5 `TestResults/`、L8 `.build-check-bin/` 命中；自检脚本 L20 `$solutions`、L21 正则清单、L48–66 每 sln 恰 6 个正式工程，输出 **9** 个受保护文件 SHA256（§1.1、§1.3、§2.3、§6.4 成立） |
| 7 | 发布物取证（只读，`artifacts\release\`） | 仍为 **2** 个版本目录。`v0.1.3-preview`：三层 `portable`/`installer`/`checksums` + 版本根 `使用前必读.txt`（首行「宇奇引擎 Yuqi Engine v0.1.3-preview 使用前必读」）；ZIP 510 个 entry、`139,596,252` 字节、首条 `YuqiEngine-v0.1.3-preview-win-x64-portable/Accessibility.dll`；setup `100,632,641` 字节、`ProductVersion=0.1.3-preview`、`ProductName=Yuqi Engine`、`FileVersion` 空；`YuqiEngine.exe` `FileVersion=0.1.3.0`、`ProductVersion=0.1.3-preview+f99ed5d8e5ef8122ffc7820198dac5e8346f0588`；`deps.json` 命中 `"YuqiEngine/0.1.3-preview"`；`runtimeconfig.json` 记 `Microsoft.NETCore.App` / `Microsoft.WindowsDesktop.App` = `8.0.29`；发布目录含 `GameOptimizer.ModuleContracts.dll`、**不含** `modules.json`；`checksums\SHA256SUMS.txt` 两行（ZIP `62a7fe3e…6057`、setup `477d7f69…c9a1`） |
| 8 | `v0.1.3-preview-fixed` 取证（只读） | 便携目录 + ZIP + `SHA256SUMS.txt` 都直接放在版本根（无 `portable\`/`checksums\`），`installer\` 内 setup `100,623,497` 字节 / `b49217dd…aaf2`，内部文件名仍是 `…-win-x64-…` ⇒ 仍是 `0.1.3-preview` 的手工重发，不是 `-Version 0.1.3-preview-fixed` 的脚本产物（§3.8 成立） |
| 9 | 测试布局静态统计 | 排除 `bin/obj/artifacts/TestResults` 后：**24** 个测试目录、**194** 个 `.cs`、`[Fact]`+`[Theory]` **1244** 处；§6.3 逐项一致（`Changes` 92/657、`Shell` 20/105、`PowerManagement` 14/70、`Visual` 11/82、`Snapshot` 8/52、`Baseline` 7/37、`Optimization` 6/24、`SystemRestore` 4/46、`Esports`/`GraphicsSafety`/`FileShellSafety` 3/3/3 + 20/14/18） |
| 10 | 测试输出目录（只读） | `GameOptimizer.Tests\bin\Debug\net8.0-windows10.0.19041.0\GameOptimizer.Tests.dll` 存在；`GameOptimizer.Tests\bin\Release\` 下另有 `visual-migration-validation`、`visual-migration-validation --logger console`、`hardware-visual-validation`、`overview-v2-validation` 四个历史自定义输出目录；`GameOptimizer.Tests\artifacts\` 下另有 `dark-only-check`、`visual-check`、`workspace-layer-check`；`GameOptimizer.Shell\bin\Release\net8.0-windows10.0.19041.0\win-x64\` 与 `GameOptimizer.Shell\artifacts\`（含 `visual-check`、`dark-only-review`、`overview-shell-build`）均存在（§2.3 成立） |
| 11 | `.build-check-bin` 体积 | 17 个子目录，16 个约 99.7–99.8 MB，`release-notice` 5.0 MB（§2.3 成立） |
| 12 | 文档清单/行数（`Get-ChildItem docs\code-map\*.md` + `(Get-Content -LiteralPath <f>).Count`，2026-09-13 23:01:17 末次复测） | 目录仍为 **7** 个 `.md`（6 份地图 + `README.md`）。读数：`01`=409（mtime 23:00:54，最后一次写入未再改变行数）、`02`=420（23:00:32）、`03`=1115（22:57:38）、`04`=829、`05`=501、`90`=72。⚠️ `01`/`02`/`03` 在本轮核查期间持续被另一路编辑写入（22:58:09 首测 407/384/1115 → 23:01:17 末测 409/420/1115），这三行是移动值；`README.md` §二 已按同一次读数改写并加 † 标注（§8.2 第 7 行的 19:5x 读数已过期） |
| 13 | `git rev-parse --short=12 HEAD` / 工作树状态 | HEAD 仍为 `aa4554598b1a`；`docs/` 整体**未跟踪**（`?? docs/`），`README.md`、`eng/Publish-Release.ps1` 为 `M`，`README_EN.md`、`GameOptimizer.Core/Services/WindowsElevation.cs` 为 `??`（文首与 `README.md` 的工作树说明已按此更新） |

**本轮未复验 / 仍受限（不要据此声称通过）**

1. **未重跑任何构建/测试/发布命令**：§2.1（`dotnet build`）、§3.2/§3.3（`dotnet restore` + `pwsh -File eng\Publish-Release.ps1`）、§4.3（ISCC 重编）、§6.4（`dotnet test`）均只做了**文件存在性 + 参数/行号字面量**核对。§8.2 的「0 警告 0 错误 / 56.23 s」是 19:45–19:54 那一次的结果，本轮没有新构建证据。
2. **`dotnet test` 环境限制未变**：本轮未尝试复跑，§8.2 第 1 条（testhost `Win32Exception (5)`，同机 `dotnet --info` 同样失败）仍是唯一结论；因此 §6.3 的 1244 处用例计数只是**静态统计**，没有用例级运行证据。
3. **未执行 `eng/Verify-EvidenceIsolation.ps1` 本身**：本轮只核对其行号、清单与「9 个文件」的输出规模，未重跑该脚本；§8.2 第 3 条（首次核查实测通过）保持不变。
4. **手工重编安装包（§4.3 的逐字节一致结论）本轮未重做**：只读取了 setup EXE 的体积/版本信息与 `SHA256SUMS.txt`，未再调用 ISCC。
5. **文档行数是移动值**：`01`/`02`/`03` 本轮核查期间仍被另一路编辑写入，`README.md` §二 只能给出带时间戳的读数；引用行号时应以类型/方法名重新定位。
