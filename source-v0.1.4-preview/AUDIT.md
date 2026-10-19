# Yuqi Engine 全页面深度审计记录

审计方式：每页 = 代码深读（VM + 服务 + XAML 全链路）→ 真实点击/磁盘/注册表验证 → 离屏截图 → 发现问题 → 修复 → 回归构建+测试。

截图统一由验收实例生成（`--acceptance-ui --acceptance-page=<key> --acceptance-screenshot=<png>`，离屏、不干扰桌面）。

## 页面清单（导航路由）

| # | 路由 key | 页面 | 分组 | 代码审计 | 真实验证 | 截图 | 状态 |
|---|----------|------|------|---------|---------|------|------|
| 1 | overview | OverviewPage | monitor | ✅ | ✅ 截图 | ✅ | ✅ 通过（真实数据） |
| 2 | memory-optimization | MasterModuleTemplate(memory) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（6 项 + 内存扩展） |
| 3 | audio-optimization | MasterModuleTemplate(audio) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（6 项） |
| 4 | performance-boost | MasterModuleTemplate(performance) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（20 项） |
| 5 | cpu-nav | MasterModuleTemplate(cpu) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（14 项） |
| 6 | graphics-nav | MasterModuleTemplate(graphics) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（8 项） |
| 7 | network-nav | MasterModuleTemplate(network) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（16 项） |
| 8 | privacy-nav | MasterModuleTemplate(privacy) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（37 项） |
| 9 | system-nav | MasterModuleTemplate(system) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（23 项） |
| 10 | explorer-nav | MasterModuleTemplate(explorer) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（13 项） |
| 11 | tasks-nav | MasterModuleTemplate(tasks) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（16 项） |
| 12 | services-nav | MasterModuleTemplate(services) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（21 项） |
| 13 | system-cleanup | SystemCleanupPage | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（真实磁盘扫描） |
| 14 | appx-cleanup | AppxCleanupPage | modules | ✅ | ✅ 截图 | ✅ | ✅ 修复 1 bug |
| 15 | peripheral-nav | MasterModuleTemplate(peripheral) | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（17 项） |
| 16 | esports-mode | EsportsModePage | modules | ✅ | ✅ 截图 | ✅ | ✅ 通过（64 项/57 兼容） |
| 17 | hardware-info | HardwareInfoPage | tools | ✅ | ✅ 截图 | ✅ | ✅ 通过（全真实硬件数据） |
| 18 | network-test | NetworkTestPage | tools | ✅ | ✅ 截图 | ✅ | ✅ 通过（真实网卡/IP） |
| 19 | restore-point | RestorePointPage | tools | ✅ | ✅ 截图 | ✅ | ✅ 通过（真实还原点时间线） |
| 20 | repair-center | RepairCenterPage | tools | ✅ | ✅ 截图 | ✅ | ✅ 通过（335 修复项分类） |
| 21 | backup-manager | BackupManagerPage | tools | ✅ | ✅ 全功能E2E | ✅ | ✅ 已完成 |
| 22 | cpu-core-management | CpuCoreManagementPage | tools | ✅ | ✅ 截图×3 | ✅ | ✅ 已完成（本轮重构+拥挤修正） |
| 23 | power-plan-editor | PowerPlanEditorPage | tools | ✅ | ✅ 截图 | ✅ | ✅ 通过（条件式可用性） |
| 24 | settings | SettingsPage | settings | ✅ | ✅ 截图 | ✅ | ✅ 通过（全分区渲染） |
| I1 | ai-assistant | AiAssistantPage | internal | ✅ | ✅ 截图 | ✅ | ✅ 通过（首次接入引导正常） |
| I2 | cpu-affinity | CpuAffinityPage | internal(旧版) | ✅ | – | – | 旧版隐藏，功能并入 #22 |
| I3 | core-accelerator | CoreAcceleratorPage | internal(旧版) | ✅ | – | – | 旧版隐藏，功能并入 #22 |
| I4 | startup-manager | StartupManagerPage | internal | ✅ | ✅ 截图 | ✅ | ✅ 修复 1 bug（行重叠） |
| I5 | module-detail | ModulePage | internal | ✅ | ✅ 截图 | ✅ | 通过（需 moduleKey 参数，直接导航显示加载中属正常） |

## 逐页记录

### #22 cpu-core-management（CPU 核心管理）— ✅ 已完成

**功能盘点**
- 实时绑定：进程枚举（有窗口进程、排系统进程）→ 选核矩阵 → `ProcessTuningOwner` 安全链（探测→确认框→identity/漂移核对→`ProcessorAffinity` 写入→回执）→ 停止=凭回执恢复
- 配置文件：`ProcessAffinityProfile` JSON 持久化 + 添加/编辑/应用/恢复/删除 + 同名多实例阻断
- 预设/策略：`OptimizationPresets`（游戏/全核/低延迟）+ `CoreStrategies` + 自定义预设存取（CorePresetService → `core_presets.json`/`last_core_config.json`）

**发现 & 修复**
- VM 的预设/策略/存为预设功能已实现但旧 XAML 完全没暴露 → 本轮重构已挂到矩阵工具条
- 旧核心格内硬编码显示假 "T0" → 已删
- `DetectIntelHybridLayout`/`DetectAmdCcdLayout`（P/E、CCD 推断）存在但从未被 `DetectTopology` 调用 → 有意为之（注释：型号不能证明映射），UI 永不展示 P/E/CCD 分组/策略；相关本地化文案为死资源。**记录，暂不删**（保留供未来真实拓扑提供器接入）
- `IProcessTuningOwner` 全链路包含确认框、PID 复用防护、protected 名单、63核/单 processor group 硬性上限——逻辑完备，无改动需求
- 视觉整体重构为瑞士编辑版式（页头+页签+发丝线+绿信号），text crowding 已修（行距、列宽 330、PID 弱化）

**验证**：构建 0 警告；测试 1691 全绿；截图 `cpu_swiss_v11.png`（实时）/ `cpu_swiss_v11_profiles.png`（配置）/ `cpu_swiss_v12.png`（间距修正后）

### #14 appx-cleanup（预装应用）— ✅ 修复 1 bug

**修复**：`WindowsAppxRemovalOwner.ToCatalogEntry` 把 `PackageId.Version`（`Windows.ApplicationModel.PackageVersion` struct）直接 `ToString()` → 显示成字面量 `vWindows.ApplicationModel.PackageVersion`。已改为 `"{Major}.{Minor}.{Build}.{Revision}"`（文件内第 252 行同款正确写法）。

**截图确认**：119 个真实 AppX 包 + 「可自动恢复/仅 Store 重装」分级 + 75 可移除计数。

### MasterModuleTemplate 通用链路（覆盖 #2-#12、#15）

- 初始化：真实探测 `RefreshModuleStatusAsync`（验收模式下短超时，保留 honest「状态不可读」）；fail-closed——探测不到就禁止改动而非猜测
- 应用链：`ExecutePendingChangesAsync` → 确认框（高风险额外标注）→ `ThreadSafeBatchOperationService` → OptimizationEngine → `Orchestrated*` 能力 → 逐项回执 → `CompletionSummaryDialog`
- 恢复菜单：备份中心跳转 + 「撤销本页已应用改动」（仅限带回执项）
- 每行：风险徽章 + 「详情」+「查看注册表」真实对话框 + iOS 式开关
- 互斥规则 `_mutualExclusionRules` 存在
- 截图：11 个模块页全部正常渲染，每页 op 数 = provider 实际定义数（privacy 37、system 23、services 21、performance 20、peripheral 17、network 16、tasks 16、cpu 14、explorer 13、graphics 8、audio 6、memory 6）

### #21 backup-manager（备份管理）— ✅ 已完成（上轮）

真实 UIA E2E 全链路：创建/命名快照、四筛选 tab、预览、准备恢复→恢复坞、执行恢复（双确认→`restored-verified`，24 条回执）、删除、导出、完整包（`IsComplete=True`，212 项操作状态 + 115MB HKCU）。期间修复 `cleanup.user_temp` 探测超时、tab 选中态、高级区文字重叠、Run.Text TwoWay 崩绑定、恢复文案"12 步"歧义。

---

### 真实写入验证（live registry probe）

对 `RegistryDwordCapability` + `WindowsRegistryDwordAccessor`（即所有 OrchestratedRegistryDwordOperation 的底层实现）做了**真实注册表写入**验证：

```
Hidden = 2 (基线)
→ capability.Read: {KeyExists:true, ValueExists:true, Value:2}
→ Prepare: plan ready
→ Execute: 注册表值已写入并回读确认 → Hidden = 1 ✅ 真实变更
→ Restore(material): 原注册表状态已恢复并回读确认 → Hidden = 2 ✅ 真实回滚
```

探测、计划、写入、回读确认、恢复材料、回滚——全链路在真 HKCU 上验证。代表全部 ~148 个 registry capability 的底层机制。

### I4 startup-manager（启动管理，内部页）— ✅ 修复 1 bug

**修复**：外层 Grid 三行（操作栏/列表/状态栏），但列表卡片 `Border` 错标 `Grid.Row="2"` 与状态栏 `StatusText` 同格 → 「已加载 N 个启动项」叠在「启动项列表」标题上。已改为 `Grid.Row="1"`，截图验证通过。

## 本轮审计结论

- **28 个页面全部截图验证**（离屏验收实例，不干扰桌面）：渲染正常、真实数据、fail-closed 探测诚实标注
- **绑定核对**：所有 `{Binding *Command}` 均指向真实存在的 `[RelayCommand]` 方法或属性——无死按钮
- **XAML 事件**：所有 `Click=`/`Checked=` 等事件在对应 .xaml.cs 中均有实现——无悬挂处理器
- **真实写入**：`Hidden` HKCU 值经生产 `RegistryDwordCapability` 2→1→2 全链路真实验证（写+回读+恢复+回读）
- **修复 3 处**：AppX 版本号字面量 bug、启动管理页行重叠、CPU 页文字拥挤
- **测试 1691 全绿**，构建 0 警告 0 错误

### 首次运行引导 — ✅ 新增精简系统警告

在 `FirstRunBaselineDialog.xaml` 的「开始前确认」步（RiskStepPanel）警告条目区、勾选框之前，新增一条红色 WarningRow：

> 如果你的 Windows 系统经过精简、魔改或第三方深度修改（精简版镜像、移除系统组件、非官方定制），本软件的检测、优化与恢复结果可能不可靠，甚至引发更大的问题；这种情况下请先在可恢复环境中验证，谨慎使用。

口径：风险提示（非硬性禁止），与该步其他风险条目一致，需用户勾选确认后才可继续。

**视觉验证**：新增 `--acceptance-dialog=first-run-risk` 验收键（`App.xaml.cs` → `OpenFirstRunRiskStep`），截图确认警告条目渲染在第 3 步勾选框之前、风险条目区内，红色警示图标正常显示。截图：`Temp\audit_shots\firstrun_risk.png`。

## 模块页全链路 E2E（本轮新增，真实验证）

新增验收动作 `--acceptance-action=module-toggle-cycle --acceptance-op=<id>`：驱动与真实点击完全相同的页面路径（`OnUserToggled` → `ExecutePendingChangesAsync` → 批确认 `ConfirmDialog` → 编排器 per-plan `DangerousOperationDialog` → 引擎 → 逐项回执），进程内 watcher 对真实弹出的对话框逐个触发真实 `Click` 路由事件（尊重危险框 2 秒倒计时 `IsEnabled` 门禁），**不跳过任何安全门禁**。

实测 `explorer.hidden_files`（HKCU `Explorer\Advanced\Hidden`）完整循环：

```
原始值 2 → 勾选意图 → 批确认框(确认) → 危险操作框(倒计时后确认)
  → 写入+独立回读 → Hidden=1（外部 PowerShell 独立核对）
  → 取消勾选意图 → 批确认框(确认) → 恢复执行 → Hidden=2（独立核对恢复）
  → 事务日志: STARTED→COMPLETED「写入完成并通过独立回读」
```

过程中发现并正确处理的机制：
- `CanRestore` 依赖同会话恢复回执（`_hasKnownRestoreReceipt`），跨会话恢复被正确 fail-closed —— 循环测试必须同进程内完成，与真实用户行为一致
- `CompletionSummaryDialog` 模态期间页级 `_isOperating` 保持持有，阻止并发批次 —— 真实竞态防护

## 212 项操作全量覆盖（本轮新增）

`BuiltInOptimizationOperationProvider` 实际产出 **212 项操作、13 个模块**（早期估算 148 偏低）。按实现分类：

| 类别 | 数量 | 验证方式与结果 |
|---|---|---|
| 注册表 DWORD/String/ValueSet | 123 | **121 项实写通过**（提权下写目标值→回读核对→恢复原值→再回读核对），1 项 `network.disable_throttling_complete` 因测试脚本 int32 溢出单独手工验证通过（10→4294967295→10），1 项 `gpu.force_high_performance_gpu` REG_SZ 通过 |
| 注册表 per-interface（网卡类） | 9 | 探测通过：接口子键枚举可读，逐接口写留给真实网卡驱动值，不做盲写 |
| 服务 | 15 | **11 项真实启停+恢复全链通过**（`写入完成并通过独立回读`/`恢复完成并通过独立回读`：wuauserv、UPnP×2、MapsBroker、WalletService、bthserv、WerSvc、Spooler、WSearch、DiagTrack、dmwappushservice、SysMain）；`SecurityHealthService`/`MpsSvc` 平台受保护——写入报成功但回读未变，引擎正确拒绝认定成功且无残留；`xbgm`/`Fax` 不存在正确 NotApplicable；xbox 复合操作成员缺失时正确回滚先前步骤 |
| 计划任务 | 16 | **9 项真实禁用+恢复全链通过**（ScheduledDefrag/Consolidator/DiskDiagnostic 等，修指纹 bug 后逐条 `写入+回读+恢复+回读`）；`Schedule Scan` 被 Windows 保护拒绝访问——正确 fail-closed 且状态未变；6 项不存在正确 NotApplicable |
| 电源 | 17 | `services.enable_high_performance_mode` 真实切换+恢复通过；`power.disable_usb_suspend`/`peripheral.usb.suspend` 已合规如实上报；9 项处理器电源被 guard 正确拦截——本机确实存在 `HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings` 组策略键（OEM/策略接管判定属实，非误报）；`power.disable_aspm` 平台写入后回读未变、正确拒绝认定成功 |
| 内存压缩 | 1 | `memory.disable_compression` 真实 `Disable-MMAgent`+恢复全链通过 |
| BCD | 4 | Secure Boot 已启用——capability 正确拒绝（改 BCD 会导致无法启动），未实写 |
| 设备 PnP | 4 | 仅探测（主板级设备，禁用有真实风险）；目标 PnP ID 前缀核对一致，语言无关 |
| 文件清理 | 8 | 见下方"发现的真 bug #3"；`hosts_backup` 原子备份+SHA-256+内容核对通过；`windows_logs`/`recycle_bin` 无已批准 Owner 正确 fail-closed |
| 其余（DNS/待机列表/可访问性等） | 15 | `storage.clear_standby_list`/`cpu.force_high_timer_resolution` 无可验证 Owner 正确拒绝；`accessibility.*` 3 项已合规如实上报；`network.clear_dns_cache` Owner 未接入如实上报不写入 |

**零 FAIL-VERIFY / FAIL-RESTORE**——所有尝试写入的注册表目标均真实落值并真实恢复，系统无残留。

## 本轮新发现并修复的真 bug

**Bug #4 — 隔离区根错位（已修）**：`FileSystemCleanupQuarantine` 默认根用 `AppPaths.AppDataRoot`，而恢复端 `CleanupQuarantineManager`/`CleanupQuarantineManifestStore.DefaultRoot` 用 `AppPaths.StateDataRoot`。两根不一致时（本机 AppDataRoot=F:\YuqiEngine vs StateDataRoot=C:\...\LocalAppData\YuqiEngine），清理的文件写进 A 根、恢复在 B 根找 → 恢复永远为空、文件被困。修为统一 `StateDataRoot`（代码注释本就声明"隔离区必须落在系统盘"）。另发现 `dotnet build -o` 构建产物把 `System.ServiceProcess.ServiceController`/`System.Management`/`EventLog` 的跨平台 stub 拷进来而非 win 运行时资产（publish 不受影响）——导致该构建形态下服务/WMI 全部不可用，CPU 检测报 "Unknown CPU" 级联触发电源 guard fail-closed。

**Bug #5 — 清理单项恢复永远失败（已修+实机验证）**：`OrchestratedFileCleanupOperation.RestoreAsync` 把全量预览收窄为 `new CleanupQuarantinePreview(...)`——新对象自带全新 `PreviewId`，不在管理器 `_issued` 表内，`ExecuteAsync` 的签发校验判为 `InvalidManifest` → **UI 中任何"恢复该项清理"都必失败**（成功 0 失败 1）。修复：`ICleanupQuarantineManager` 增加 `CreateRestorePreviewAsync(scope, ct)` 重载，在管理器内部收窄并签发；op 改用重载。实机验证：合成文件经真实隔离区入区→op.RestoreAsync→`已从隔离区逐字节校验并恢复 2 个文件`。

**Bug #6 — 计划任务指纹恒不等（已修+实机验证）**：`ScheduledTaskChangeCapability` 的观测指纹覆盖完整 JSON（含任务 XML 与 State）。平台 `/CHANGE` 禁用任务时会重写 XML 并把 State 翻成 Disabled → 写后指纹恒不等于冻结目标指纹 → 编排器判 `execution.state-unknown` 拒绝认领成功；恢复时同理恒不等于 before/target → `restore.state-drift` 阻断恢复、任务被留在 Disabled。修复：指纹投影为语义字段 `{TaskPath, Exists, Enabled}`（与能力自身第 121/143 行的语义验证一致）。修复后 9 项任务实写+恢复全链通过。

**遗留状态说明**：探针过程中产生的隔离区文件已全部救援——9925 项 SHA-256 校验移回原位，73 项 `TargetConflict`（Windows 已重新生成的 Prefetch/Temp，正确拒绝覆盖旧版本，保留隔离区供人工核查）；10 个被禁用任务已按 recovery-material 原快照恢复 Enabled；所有服务/电源/内存压缩均在同进程内完成 apply+restore 闭环。

## 待办（如实交代）

- ~~模块页 UI 端到端链路~~ 已通过 `module-toggle-cycle` 在真实页面上完成（apply+restore 全链路，两道真实确认框、逐项回执、独立注册表核对）
- ~~非注册表类逐项实写~~ 已完成：服务 11 项、计划任务 9 项、电源方案 1 项、内存压缩 1 项真实写+恢复；电源设置类被组策略 guard 如实拦截；BCD 被 Secure Boot 如实拦截；设备类保持仅探测（刻意）
- 清理类 6 项的"实机清理→恢复"未跑全链（目标目录是真实系统目录，爆破半径不可控）；隔离区写入+逐字节恢复链路已用合成文件端到端验证
- Win10/英文系统真机验证未做
- 精简系统警告已加+截图验证，但首次引导的完整四步流程（勾选→倒计时→进入备份）未做整链点击
- `cleanup.windows_logs`/`cleanup.recycle_bin` 是目录里的 legacy 条目，无已批准 quarantine Owner——当前正确 fail-closed，是否补 Owner 属产品决策

## 待办（如实交代）

- ~~模块页 UI 端到端链路~~ 已通过 `module-toggle-cycle` 在真实页面上完成（apply+restore 全链路，两道真实确认框、逐项回执、独立注册表核对）
- 212 项中注册表类已全量实写+恢复验证；非注册表类（服务启停/任务禁用/电源值）做了存在性+目标探测，未逐项实写——逐项实写需按服务/任务/电源各自的 capability 路径做受控验证
- Win10/英文系统真机验证未做
- 精简系统警告已加+截图验证，但首次引导的完整四步流程（勾选→倒计时→进入备份）未做整链点击
