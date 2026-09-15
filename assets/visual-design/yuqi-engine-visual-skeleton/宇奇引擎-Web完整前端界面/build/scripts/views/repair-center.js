/* ==========================================================================
   修复中心 · 视图脚本
   --------------------------------------------------------------------------
   职责：
     1. 注册 window.__VIEW_INIT__['repair-center']
     2. 渲染分组卡片（mock 数据库）
     3. 一键诊断扫描状态机：idle → scanning → detected/done
     4. 单卡修复状态机：idle → fixing → fixed/failed
     5. 详情抽屉（What/Why/How/Risk）
     6. 通过 __VIEW_DESTROY__ 清理 timer / listener
   ========================================================================== */
(function () {
    'use strict';

    const VIEW_NAME = 'repair-center';

    /* ==========================================================================
       修复项数据库
       --------------------------------------------------------------------------
       字段：
         id    - 唯一 id
         name  - 标题
         desc  - 一句话描述
         icon  - lucide 图标名
         risk  - low | medium | high
         time  - 预计耗时
         what  - 这是什么问题
         why   - 为什么会发生 (string[])
         how   - 修复原理 (string)
         cmd   - 修复使用的命令（可空）
         warns - 风险与影响 (Array<{tone, text}>)
       ========================================================================== */
    const REPAIR_DB = {
        common: [
            {
                id: 'mstore',
                name: 'Microsoft Store 无法打开',
                desc: '商店进程崩溃 / 闪退 / 加载白屏',
                icon: 'shopping-bag',
                risk: 'low', time: '约 30 秒',
                what: 'Microsoft Store 客户端启动后立刻崩溃、白屏或卡在加载，常见于系统更新后或系统语言切换后。',
                why: ['Store 缓存损坏', 'AppX 包注册表项异常', 'WSReset 未正确执行', '用户权限或沙箱目录损坏'],
                how: '通过 wsreset 重置 Store 缓存，并对 WindowsStore AppX 包重新注册，必要时检查关键服务（AppXSvc、WSService）。',
                cmd: 'wsreset.exe\nGet-AppxPackage *WindowsStore* | Foreach {Add-AppxPackage -DisableDevelopmentMode -Register "$($_.InstallLocation)\\AppXManifest.xml"}',
                warns: [
                    { tone: 'ok',   text: '不影响 <b>已安装的应用</b>，仅清空 Store 客户端缓存' },
                    { tone: 'info', text: '执行过程中 <b>Store 进程会被关闭并重启</b>' },
                    { tone: 'risk', text: '若问题由系统镜像损坏引起，需先运行 SFC 修复' },
                ],
            },
            {
                id: 'noaudio',
                name: '系统无声音',
                desc: '扬声器图标显示叉号 / 音频设备未识别',
                icon: 'volume-x',
                risk: 'low', time: '约 20 秒',
                what: '系统输出不出声音，任务栏音量图标显示红叉或"未安装音频设备"。',
                why: ['Windows 音频服务（Audiosrv）已停止', '音频驱动异常', '默认输出设备被切换或禁用', '音频增强功能冲突'],
                how: '重启 Windows Audio / AudioEndpointBuilder 两个服务，重置默认输出设备，必要时重新加载音频驱动。',
                cmd: 'net stop Audiosrv & net start Audiosrv\nnet start AudioEndpointBuilder',
                warns: [
                    { tone: 'ok',   text: '<b>不会卸载</b> 音频驱动，只是重启服务并重置端点' },
                    { tone: 'info', text: '执行后系统音量会 <b>恢复到默认 50%</b>' },
                ],
            },
            {
                id: 'wifi',
                name: 'WiFi 无法连接',
                desc: '搜不到 WiFi / 连接后无网络 / 频繁掉线',
                icon: 'wifi-off',
                risk: 'medium', time: '约 1 分钟',
                what: '无线网卡能识别但搜不到信号，或能连上但浏览器无法访问任何网站，或频繁自动断开。',
                why: ['Winsock 目录损坏', 'TCP/IP 协议栈异常', 'DNS 缓存污染', '无线网卡驱动状态异常'],
                how: '依次执行 Winsock 重置 → TCP/IP 重置 → DNS 缓存清理 → 重启网络适配器，恢复出厂网络栈。',
                cmd: 'netsh winsock reset\nnetsh int ip reset\nipconfig /flushdns\nipconfig /release && ipconfig /renew',
                warns: [
                    { tone: 'risk', text: '执行后 <b>需要重启电脑</b> 才能完全生效' },
                    { tone: 'info', text: '会 <b>清除自定义代理</b> 和静态 IP 设置，请提前记录' },
                    { tone: 'ok',   text: '不会删除已保存的 WiFi 密码' },
                ],
            },
            {
                id: 'bluetooth',
                name: '蓝牙打不开',
                desc: '蓝牙开关消失 / 配对失败 / 设备识别异常',
                icon: 'bluetooth-off',
                risk: 'low', time: '约 25 秒',
                what: '设置中蓝牙开关消失或灰色不可点，或者设备能识别但无法配对，已配对设备频繁掉线。',
                why: ['蓝牙支持服务（bthserv）未启动', '蓝牙驱动未加载', '系统电源管理把蓝牙挂起'],
                how: '启动并设置 bthserv 为自动启动，重新枚举蓝牙适配器，关闭"为节省电源关闭此设备"。',
                cmd: 'sc config bthserv start= auto\nnet start bthserv',
                warns: [
                    { tone: 'ok',   text: '<b>已配对设备</b> 不会丢失，无需重新配对' },
                    { tone: 'info', text: '执行后蓝牙会 <b>暂时关闭并重新打开</b>' },
                ],
            },
            {
                id: 'taskbar',
                name: '任务栏无响应',
                desc: '任务栏卡死 / 图标不显示 / 右键无菜单',
                icon: 'app-window',
                risk: 'low', time: '约 15 秒',
                what: '任务栏点击没反应、图标全部消失、右键空白无菜单，但其他程序仍能正常使用。',
                why: ['Explorer.exe 进程异常', '任务栏 IconCache / Iris 缓存损坏', 'Shell 扩展第三方插件冲突'],
                how: '安全重启 explorer.exe 进程，并清除 IconCache.db。',
                cmd: 'taskkill /f /im explorer.exe & start explorer.exe',
                warns: [
                    { tone: 'info', text: '执行时桌面会 <b>闪烁一次</b>，所有打开的窗口保留' },
                    { tone: 'ok',   text: '不会关闭任何应用程序' },
                ],
            },
            {
                id: 'rclick',
                name: '右键菜单卡顿',
                desc: '右键打开慢 / 菜单卡几秒才出现',
                icon: 'mouse-pointer-click',
                risk: 'medium', time: '约 30 秒',
                what: '在桌面或资源管理器中点击右键，菜单要等 2-5 秒甚至更久才出现。',
                why: ['第三方 Shell 扩展过多或异常', '右键菜单缓存损坏', '部分网络驱动器响应超时'],
                how: '扫描并禁用响应超时的 Shell 扩展，重建右键菜单缓存。',
                warns: [
                    { tone: 'info', text: '会 <b>临时禁用</b> 检出的异常扩展，可在详情中查看' },
                    { tone: 'risk', text: '部分压缩软件、云盘的右键菜单可能 <b>需要重新启用</b>' },
                ],
            },
            {
                id: 'ime',
                name: '输入法切换异常',
                desc: '中英切换失效 / 输入法状态错乱',
                icon: 'languages',
                risk: 'low', time: '约 20 秒',
                what: 'Shift / Ctrl+Space 无法切换中英文，输入法图标消失或一直显示英文。',
                why: ['CTF Loader（ctfmon.exe）未启动', '输入法 TIP 列表异常', '输入法默认项被覆盖'],
                how: '启动 ctfmon.exe 并加入启动项，重置 TIP 输入法列表为系统默认。',
                cmd: 'start ctfmon.exe',
                warns: [
                    { tone: 'ok',   text: '不会卸载已安装的输入法' },
                    { tone: 'info', text: '默认输入法会 <b>重置为系统中文</b>' },
                ],
            },
            {
                id: 'explorer-crash',
                name: '文件资源管理器崩溃',
                desc: '打开文件夹闪退 / 桌面频繁刷新',
                icon: 'folder-x',
                risk: 'medium', time: '约 40 秒',
                what: '打开"此电脑"或某个文件夹时窗口直接消失，或桌面图标反复刷新。',
                why: ['某个 Shell 扩展崩溃', '缩略图缓存损坏', '硬盘存在坏扇区索引'],
                how: '清理缩略图与图标缓存，禁用近期崩溃日志中的 Shell 扩展。',
                warns: [
                    { tone: 'info', text: '执行时会 <b>清除缩略图缓存</b>，下次打开图片文件夹会重新生成' },
                    { tone: 'ok',   text: '不会修改用户文件' },
                ],
            },
        ],
        system: [
            {
                id: 'sfc',
                name: '系统文件损坏',
                desc: 'SFC 检测到完整性受损的系统文件',
                icon: 'file-warning',
                risk: 'medium', time: '约 5-10 分钟',
                what: '关键系统 DLL 或可执行文件被覆盖、损坏或删除，可能导致部分功能异常。',
                why: ['不当卸载', '断电或系统崩溃', '第三方软件覆盖系统文件', '存储介质坏块'],
                how: '调用系统自带的 sfc /scannow 工具扫描并从缓存还原所有受保护的系统文件。',
                cmd: 'sfc /scannow',
                warns: [
                    { tone: 'risk', text: '<b>需要管理员权限</b>，执行期间不要关机' },
                    { tone: 'info', text: '若 SFC 无法修复，需进一步使用 DISM' },
                ],
            },
            {
                id: 'dism',
                name: '系统映像损坏',
                desc: 'Windows 组件存储损坏，SFC 无法工作',
                icon: 'package-x',
                risk: 'medium', time: '约 10-30 分钟',
                what: 'Windows 组件存储（WinSxS）损坏，SFC 无法从缓存中还原文件。',
                why: ['不完整的系统更新', '更新被中断', '存储介质故障'],
                how: '使用 DISM 联机修复，从 Windows Update 下载干净的组件副本来还原映像。',
                cmd: 'DISM /Online /Cleanup-Image /RestoreHealth',
                warns: [
                    { tone: 'risk', text: '需要 <b>稳定的网络连接</b>，会下载约 200-500 MB 数据' },
                    { tone: 'info', text: '修复完成后建议再运行一次 SFC' },
                ],
            },
            {
                id: 'wupdate',
                name: 'Windows Update 异常',
                desc: '更新卡 0% / 错误码 0x80070xxx',
                icon: 'download-cloud',
                risk: 'medium', time: '约 2 分钟',
                what: 'Windows Update 始终卡在某个百分比、报错代码或反复下载同一个补丁。',
                why: ['SoftwareDistribution 缓存损坏', 'BITS / wuauserv 服务异常', 'Catroot2 索引损坏'],
                how: '停止更新服务 → 重命名 SoftwareDistribution 与 catroot2 → 重启更新服务。',
                cmd: 'net stop wuauserv & net stop bits\nren %systemroot%\\SoftwareDistribution SoftwareDistribution.old\nnet start wuauserv & net start bits',
                warns: [
                    { tone: 'info', text: '执行后下次检查更新会 <b>重新下载补丁列表</b>' },
                    { tone: 'ok',   text: '不会回滚已经安装的更新' },
                ],
            },
            {
                id: 'icon-cache',
                name: '图标缓存损坏',
                desc: '图标显示为白色方块或错位',
                icon: 'image-off',
                risk: 'low', time: '约 30 秒',
                what: '部分应用、文件夹、快捷方式图标变成白色方块或显示成错误图标。',
                why: ['IconCache.db 文件损坏', 'Explorer 重建缓存中断'],
                how: '关闭 Explorer，删除所有 iconcache_*.db，再重启 Explorer 重新生成。',
                warns: [
                    { tone: 'info', text: '重建过程中桌面 <b>会闪烁 1-2 次</b>' },
                    { tone: 'ok',   text: '不影响任何文件和应用' },
                ],
            },
            {
                id: 'font-cache',
                name: '字体缓存损坏',
                desc: '系统字体显示异常或缺失',
                icon: 'type',
                risk: 'low', time: '约 25 秒',
                what: '部分应用文字显示为方块（豆腐块）、字形错乱或缺失。',
                why: ['FontCache 服务异常', '字体缓存文件损坏', '新字体未正确注册'],
                how: '停止 FontCache 服务，清空 FontCache 目录，重启服务重建缓存。',
                warns: [
                    { tone: 'info', text: '重建期间 <b>部分应用文字可能短暂异常</b>' },
                    { tone: 'ok',   text: '不会卸载任何字体' },
                ],
            },
            {
                id: 'time-sync',
                name: '系统时间同步失败',
                desc: '时间偏差大 / 同步服务报错',
                icon: 'clock-alert',
                risk: 'low', time: '约 15 秒',
                what: '系统时间与互联网时间偏差几分钟甚至几小时，导致 HTTPS 站点无法访问。',
                why: ['Windows Time（w32time）服务停止', 'NTP 服务器不可达', '时区设置错误'],
                how: '启动 w32time 服务并强制与 NTP 服务器同步。',
                cmd: 'net start w32time\nw32tm /resync /force',
                warns: [
                    { tone: 'info', text: '会 <b>重置默认 NTP 服务器</b> 为 time.windows.com' },
                ],
            },
        ],
        network: [
            {
                id: 'dns',
                name: 'DNS 解析异常',
                desc: '部分网站打不开 / 浏览器报 DNS 错误',
                icon: 'globe-2',
                risk: 'low', time: '约 10 秒',
                what: '部分网站完全无法访问但 ping IP 正常，浏览器返回 DNS_PROBE_FINISHED_NXDOMAIN。',
                why: ['本地 DNS 缓存被污染', 'DNS 服务器不响应', 'hosts 文件被恶意软件修改'],
                how: '清空本地 DNS 缓存，重置为系统默认 DNS。',
                cmd: 'ipconfig /flushdns',
                warns: [
                    { tone: 'ok', text: '操作 <b>仅清缓存</b>，不修改 DNS 设置' },
                ],
            },
            {
                id: 'tcp',
                name: 'TCP/IP 协议栈损坏',
                desc: '部分应用无法联网 / Ping 不通',
                icon: 'cable',
                risk: 'high', time: '约 20 秒',
                what: '应用提示无法连接网络，但其他程序正常，重启后无效。',
                why: ['TCP/IP 协议栈被恶意软件修改', 'Winsock LSP 链异常', '网卡驱动崩溃'],
                how: '重置 TCP/IP 协议栈到出厂状态。',
                cmd: 'netsh int ip reset',
                warns: [
                    { tone: 'risk', text: '执行后 <b>必须重启</b>' },
                    { tone: 'risk', text: '会 <b>清除所有自定义网卡 IP / DNS / 路由</b>，请提前备份' },
                ],
            },
            {
                id: 'winsock',
                name: 'Winsock 目录损坏',
                desc: '联网应用闪退 / 部分协议失效',
                icon: 'plug',
                risk: 'high', time: '约 15 秒',
                what: '某些应用启动即报网络错误，但浏览器仍可使用。',
                why: ['第三方 LSP 残留', '安全软件卸载不干净', '网络代理软件遗留 hook'],
                how: '重置 Winsock 目录，移除所有第三方 LSP。',
                cmd: 'netsh winsock reset',
                warns: [
                    { tone: 'risk', text: '执行后 <b>必须重启</b> 电脑' },
                    { tone: 'info', text: '部分代理软件需要重新安装' },
                ],
            },
            {
                id: 'adapter',
                name: '网络适配器异常',
                desc: '设备管理器中显示黄色感叹号',
                icon: 'router',
                risk: 'medium', time: '约 30 秒',
                what: '网络适配器在设备管理器中带黄色叹号，或代码 31/43。',
                why: ['驱动安装不完整', '电源管理把网卡禁用', '硬件冲突'],
                how: '禁用并重新启用网卡，触发驱动重新加载，必要时回滚到稳定版本。',
                warns: [
                    { tone: 'info', text: '执行期间 <b>网络会短暂中断 5-10 秒</b>' },
                ],
            },
            {
                id: 'firewall',
                name: '防火墙规则冲突',
                desc: '部分应用被静默拦截',
                icon: 'shield-x',
                risk: 'medium', time: '约 25 秒',
                what: '某些应用无法联网但又没有报错，常见于游戏、IM 工具。',
                why: ['同名规则重复', '入站规则覆盖了出站', '第三方安全软件残留'],
                how: '扫描重复规则、孤儿规则、冲突规则并清理。',
                warns: [
                    { tone: 'risk', text: '<b>不会清除</b> 用户自定义规则，仅清理重复和无效项' },
                ],
            },
        ],
        media: [
            {
                id: 'gpu-driver',
                name: '显卡驱动异常',
                desc: '画面闪烁 / 黑屏 / 驱动崩溃恢复',
                icon: 'monitor-cog',
                risk: 'high', time: '约 1 分钟',
                what: '屏幕反复闪烁、黑屏几秒后恢复，或者出现"显示驱动程序已停止响应并已恢复"。',
                why: ['驱动版本不稳定', 'TDR 超时阈值过低', '多显卡驱动冲突'],
                how: '检测当前驱动崩溃日志，重启显示服务并提高 TDR 容忍时间。',
                warns: [
                    { tone: 'info', text: '执行时屏幕 <b>会黑屏 2-3 秒</b>' },
                    { tone: 'risk', text: '如反复出现，建议在「驱动管理」中重新安装' },
                ],
            },
            {
                id: 'audio-svc',
                name: '音频服务未启动',
                desc: 'Windows Audio 服务停止',
                icon: 'volume-2',
                risk: 'low', time: '约 10 秒',
                what: '音量图标显示红叉，悬停提示"音频服务未运行"。',
                why: ['服务被禁用', '系统优化软件误关', '账户权限问题'],
                how: '启动 Audiosrv 与 AudioEndpointBuilder 服务，并设置为自动启动。',
                cmd: 'sc config Audiosrv start= auto\nnet start Audiosrv',
                warns: [
                    { tone: 'ok', text: '不影响其他音频设置' },
                ],
            },
            {
                id: 'hdr',
                name: 'HDR 模式异常',
                desc: '颜色发灰 / SDR 内容偏色',
                icon: 'sun-dim',
                risk: 'medium', time: '约 20 秒',
                what: '开启 HDR 后桌面、文档颜色发灰，关闭 HDR 后偏色仍然存在。',
                why: ['HDR 校准未完成', 'SDR 内容亮度滑块异常', '色彩管理 ICC 配置文件损坏'],
                how: '重置 HDR 校准状态，恢复默认 ICC 配置。',
                warns: [
                    { tone: 'info', text: '需要 <b>重新进行 HDR 校准</b>' },
                ],
            },
            {
                id: 'multi-display',
                name: '多显示器识别问题',
                desc: '副屏未识别 / 排列错乱',
                icon: 'monitor',
                risk: 'low', time: '约 15 秒',
                what: '插上副屏后系统不识别，或者显示器排列与物理位置错位。',
                why: ['EDID 缓存损坏', '显示器配置文件冲突'],
                how: '清除 EDID 缓存并刷新显示拓扑。',
                warns: [
                    { tone: 'info', text: '执行时 <b>所有显示器会黑屏 2 秒</b>' },
                ],
            },
        ],
        game: [
            {
                id: 'directx',
                name: 'DirectX 运行库损坏',
                desc: '游戏报错 d3dx9_xx.dll 缺失',
                icon: 'box',
                risk: 'medium', time: '约 1 分钟',
                what: '启动游戏报"找不到 d3dx9_43.dll"或"DirectX 错误"。',
                why: ['DirectX 9/10/11 运行时缺失', '游戏依赖的特定版本未安装'],
                how: '重新部署 DirectX End-User Runtime，覆盖安装缺失的 DLL。',
                warns: [
                    { tone: 'ok', text: '不影响系统集成的 DirectX 12' },
                ],
            },
            {
                id: 'vcredist',
                name: 'Visual C++ 运行库缺失',
                desc: '游戏 / 应用报 MSVCP / VCRUNTIME 错误',
                icon: 'package',
                risk: 'medium', time: '约 1-2 分钟',
                what: '启动应用报 MSVCP140.dll、VCRUNTIME140.dll 缺失或版本不匹配。',
                why: ['VC++ 2015-2022 运行库未完整安装', '版本被新版应用覆盖'],
                how: '检测缺失的 VC++ 运行库版本并补全安装（x86 + x64）。',
                warns: [
                    { tone: 'info', text: '会下载约 <b>50-80 MB</b> 安装包' },
                ],
            },
            {
                id: 'dotnet',
                name: '.NET Framework 异常',
                desc: '应用报 .NET 初始化错误',
                icon: 'binary',
                risk: 'medium', time: '约 2 分钟',
                what: '启动 .NET 应用报 "未能初始化运行时" 或哈希校验失败。',
                why: ['.NET Framework 损坏', '安全更新未应用'],
                how: '修复或重装 .NET Framework 3.5 / 4.x，必要时清理 NGEN 缓存。',
                warns: [
                    { tone: 'risk', text: '可能 <b>需要重启电脑</b>' },
                ],
            },
            {
                id: 'gpu-sched',
                name: 'GPU 调度异常',
                desc: '游戏掉帧 / GPU 利用率偏低',
                icon: 'gauge',
                risk: 'low', time: '约 15 秒',
                what: '游戏帧率不稳定，GPU 利用率长期 < 60%，CPU 利用率正常。',
                why: ['硬件加速 GPU 计划未启用', '游戏模式调度冲突'],
                how: '启用硬件加速 GPU 计划并优化任务调度。',
                warns: [
                    { tone: 'info', text: '需要 <b>重启电脑</b> 才能生效' },
                ],
            },
            {
                id: 'startup-overload',
                name: '启动项过载',
                desc: '开机后系统响应慢',
                icon: 'zap-off',
                risk: 'low', time: '约 10 秒',
                what: '开机后 5-10 分钟内系统反应迟钝，资源管理器响应慢。',
                why: ['启动项数量过多', '部分启动项占用 IO 时间过长'],
                how: '扫描启动项 IO 占用排行，提示可禁用项（不强制删除）。',
                warns: [
                    { tone: 'ok', text: '<b>仅检测和提示</b>，不会自动禁用任何启动项' },
                ],
            },
        ],
        rollback: [
            {
                id: 'rb-cortana',
                name: '撤销「禁用 Cortana」',
                desc: '恢复 Cortana 搜索助手',
                icon: 'undo-2',
                risk: 'low', time: '约 10 秒',
                what: '此前已通过本软件禁用 Cortana，现在希望恢复。',
                why: ['用户希望重新使用语音搜索', 'Cortana 集成应用需要其支持'],
                how: '还原相关注册表项与组策略到默认状态。',
                warns: [
                    { tone: 'info', text: '执行后 <b>需要重启 Explorer</b>' },
                ],
            },
            {
                id: 'rb-update',
                name: '撤销「关闭 Windows 更新」',
                desc: '恢复 Windows Update 自动更新',
                icon: 'rotate-ccw',
                risk: 'medium', time: '约 15 秒',
                what: '此前已关闭 Windows 自动更新，现在希望恢复。',
                why: ['需要安装安全补丁', '部分应用要求最新系统版本'],
                how: '启用 wuauserv 服务并恢复组策略。',
                cmd: 'sc config wuauserv start= auto\nnet start wuauserv',
                warns: [
                    { tone: 'risk', text: '恢复后系统 <b>会在后台下载更新</b>' },
                ],
            },
            {
                id: 'rb-network',
                name: '撤销「网络参数优化」',
                desc: '恢复 TCP / DNS 默认参数',
                icon: 'history',
                risk: 'medium', time: '约 20 秒',
                what: '此前已应用网络优化参数（TCP 自动调谐、DNS 缓存大小等），现在希望恢复默认值。',
                why: ['优化后某些应用兼容性下降', '希望与默认参数对比测试'],
                how: '将所有调整过的 TCP / DNS / Winsock 参数恢复为系统默认值。',
                warns: [
                    { tone: 'risk', text: '执行后 <b>需要重启网络适配器</b>，会短暂断网' },
                ],
            },
        ],
    };

    /* ---------------- 视图私有状态 ---------------- */
    let state = {
        timers: [],
        rafs: [],
        boundClickHandler: null,
        currentRoot: null,
        scanCount: 0,
        navObserver: null,
    };

    function $(root, sel)  { return root.querySelector(sel); }
    function $$(root, sel) { return Array.from(root.querySelectorAll(sel)); }

    function clearTimers() {
        state.timers.forEach(t => clearTimeout(t));
        state.timers = [];
        state.rafs.forEach(r => cancelAnimationFrame(r));
        state.rafs = [];
    }

    function setTimer(fn, ms) {
        const t = setTimeout(fn, ms);
        state.timers.push(t);
        return t;
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    function setPageState(root, st) {
        const page = $(root, '.rc-page');
        if (page) page.setAttribute('data-state', st);
    }

    /* ==========================================================================
       渲染卡片
       ========================================================================== */
    function renderCards(root) {
        Object.keys(REPAIR_DB).forEach(groupKey => {
            const grp = root.querySelector(`#rc-grp-${groupKey} .rc-card-grid`);
            if (!grp) return;
            const html = REPAIR_DB[groupKey].map(item => cardHtml(item, groupKey)).join('');
            grp.innerHTML = html;
        });
        // 重新生成图标
        if (window.lucide) window.lucide.createIcons({ root });
        updateNavCounts(root);
    }

    function cardHtml(it, groupKey) {
        const riskMap = { low: '低风险', medium: '中等', high: '高风险' };
        const riskIconMap = { low: 'shield-check', medium: 'shield-alert', high: 'shield' };
        return `
        <article class="rc-item" data-status="idle" data-id="${it.id}" data-group="${groupKey}">
            <div class="rc-item-head">
                <span class="rc-item-icon-wrap">
                    <i data-lucide="${it.icon}" class="rc-item-icon"></i>
                </span>
                <div class="rc-item-title-wrap">
                    <span class="rc-item-title">${escapeHtml(it.name)}</span>
                    <span class="rc-item-sub">${escapeHtml(it.desc)}</span>
                </div>
                <span class="rc-item-pill">
                    <i data-lucide="circle" class="icon-xs"></i>
                    <span class="rc-item-pill-text">未检测</span>
                </span>
            </div>
            <div class="rc-item-meta">
                <span class="rc-item-meta-cell" data-risk="${it.risk}">
                    <i data-lucide="${riskIconMap[it.risk]}" class="icon-xs"></i>
                    <span>${riskMap[it.risk]}</span>
                </span>
                <span class="rc-item-meta-cell">
                    <i data-lucide="clock-4" class="icon-xs"></i>
                    <span>${escapeHtml(it.time)}</span>
                </span>
            </div>
            <div class="rc-item-actions">
                <button class="rc-item-btn" data-action="show-detail" data-id="${it.id}" data-group="${groupKey}">
                    <i data-lucide="info" class="icon-sm"></i><span>详情</span>
                </button>
                <button class="rc-item-btn rc-item-btn-fix" data-action="fix-item" data-id="${it.id}" data-group="${groupKey}">
                    <i data-lucide="wand-2" class="icon-sm"></i><span>修复</span>
                </button>
            </div>
        </article>`;
    }

    /* ==========================================================================
       一键诊断扫描
       ========================================================================== */
    function runDiagnostic(root) {
        if (state.scanCount > 0) return; // 防重入
        state.scanCount++;

        // 进入 scanning 态
        setPageState(root, 'scanning');
        const runBtn = $(root, '#rcRunBtn');
        const runBtnText = $(root, '#rcRunBtnText');
        if (runBtn) { runBtn.disabled = true; runBtnText.textContent = '诊断中…'; }

        // 重置所有卡片为 checking
        const items = $$(root, '.rc-item');
        items.forEach(it => updateItemStatus(it, 'checking'));

        // 4 个阶段进度
        const phases = [
            { selector: '[data-phase="system"]',       targets: items.filter(i => i.dataset.group === 'system'),  ms: 1400 },
            { selector: '[data-phase="network"]',      targets: items.filter(i => i.dataset.group === 'network' || i.dataset.group === 'media'), ms: 1100 },
            { selector: '[data-phase="driver"]',       targets: items.filter(i => i.dataset.group === 'common' || i.dataset.group === 'media'), ms: 1300 },
            { selector: '[data-phase="optimization"]', targets: items.filter(i => i.dataset.group === 'game' || i.dataset.group === 'rollback'), ms: 1000 },
        ];

        // 顺序推进 4 个阶段
        let phaseIdx = 0;
        function nextPhase() {
            if (phaseIdx >= phases.length) {
                finishDiagnostic(root);
                return;
            }
            const p = phases[phaseIdx];
            const row = root.querySelector(p.selector);
            const fill = row ? row.querySelector('.rc-dx-progress-fill') : null;
            const pct  = row ? row.querySelector('.rc-dx-progress-pct') : null;

            const start = performance.now();
            function tick(now) {
                const t = Math.min(1, (now - start) / p.ms);
                const v = Math.round(t * 100);
                if (fill) fill.style.width = v + '%';
                if (pct)  pct.textContent = v + '%';
                if (t < 1) {
                    state.rafs.push(requestAnimationFrame(tick));
                } else {
                    if (row) row.setAttribute('data-done', 'true');
                    // 在该阶段内随机决定哪些卡片"被检出"
                    p.targets.forEach((card) => {
                        // 30% 概率检出问题，50% 健康，20% 维持 idle
                        const r = Math.random();
                        let st = 'healthy';
                        if (r < 0.30) st = 'detected';
                        else if (r < 0.80) st = 'healthy';
                        else st = 'idle';
                        updateItemStatus(card, st);
                    });
                    phaseIdx++;
                    setTimer(nextPhase, 120);
                }
            }
            state.rafs.push(requestAnimationFrame(tick));
        }
        nextPhase();
    }

    function finishDiagnostic(root) {
        // 等所有阶段完成后，判定整体状态
        setTimer(() => {
            const items = $$(root, '.rc-item');
            const detected = items.filter(it => it.dataset.status === 'detected').length;
            const healthy  = items.filter(it => it.dataset.status === 'healthy').length;
            const total    = items.length;

            // 健康分计算：100 基础，每个 detected 扣 (5-12 分)，按风险加权
            let score = 100;
            items.forEach(it => {
                if (it.dataset.status === 'detected') {
                    const meta = it.querySelector('.rc-item-meta-cell');
                    const risk = meta ? meta.dataset.risk : 'medium';
                    score -= ({ low: 5, medium: 8, high: 12 })[risk] || 8;
                }
            });
            score = Math.max(score, 50);

            // 更新 UI
            const dxScore = $(root, '#rcDxScore');
            const dxScoreLabel = $(root, '#rcDxScoreLabel');
            if (dxScore) dxScore.textContent = score;
            if (dxScoreLabel) dxScoreLabel.textContent = detected > 0 ? '需修复' : '健康';

            $(root, '#rcStatTotal').textContent = total;
            $(root, '#rcStatDetected').textContent = detected;
            $(root, '#rcStatLast').textContent = '刚刚';
            $(root, '#rcFixAllNum').textContent = detected;

            const fixAllBtn = $(root, '#rcFixAllBtn');
            if (fixAllBtn) fixAllBtn.disabled = detected === 0;

            const runBtn = $(root, '#rcRunBtn');
            const runBtnText = $(root, '#rcRunBtnText');
            if (runBtn) { runBtn.disabled = false; runBtnText.textContent = '重新诊断'; }

            // 切换页面状态
            setPageState(root, detected > 0 ? 'detected' : 'done');

            // 重置进度行
            $$(root, '.rc-dx-progress-row').forEach(r => r.removeAttribute('data-done'));

            state.scanCount = 0;
            showToast(root, detected > 0
                ? `诊断完成 · 检出 ${detected} 项问题`
                : `诊断完成 · 系统状态良好`);
        }, 200);
    }

    /* ==========================================================================
       单卡状态切换
       ========================================================================== */
    function updateItemStatus(card, status) {
        if (!card) return;
        card.setAttribute('data-status', status);
        const pillText = card.querySelector('.rc-item-pill-text');
        const pillIcon = card.querySelector('.rc-item-pill i[data-lucide]');
        const fixBtn = card.querySelector('.rc-item-btn-fix span');
        const fixBtnEl = card.querySelector('.rc-item-btn-fix');

        const labelMap = {
            idle:     { text: '未检测',  icon: 'circle' },
            checking: { text: '检测中…', icon: 'loader' },
            healthy:  { text: '正常',    icon: 'check-circle' },
            detected: { text: '已检出',  icon: 'alert-circle' },
            fixing:   { text: '修复中…', icon: 'loader' },
            fixed:    { text: '已修复',  icon: 'check-circle-2' },
            failed:   { text: '失败',    icon: 'x-circle' },
        };
        const info = labelMap[status] || labelMap.idle;
        if (pillText) pillText.textContent = info.text;
        if (pillIcon) pillIcon.setAttribute('data-lucide', info.icon);

        if (fixBtn && fixBtnEl) {
            if (status === 'fixed') {
                fixBtn.textContent = '已修复';
                fixBtnEl.disabled = true;
            } else if (status === 'fixing') {
                fixBtn.textContent = '修复中…';
                fixBtnEl.disabled = true;
            } else if (status === 'healthy') {
                fixBtn.textContent = '无需修复';
                fixBtnEl.disabled = true;
            } else if (status === 'failed') {
                fixBtn.textContent = '重试';
                fixBtnEl.disabled = false;
            } else {
                fixBtn.textContent = '修复';
                fixBtnEl.disabled = false;
            }
        }

        // 重新生成图标（pill 图标变了）
        if (window.lucide) {
            const iconEl = card.querySelector('.rc-item-pill');
            if (iconEl) window.lucide.createIcons({ root: iconEl });
        }
    }

    /* ==========================================================================
       单项修复
       ========================================================================== */
    function fixItem(root, id, group, fromDrawer) {
        const card = root.querySelector(`.rc-item[data-id="${id}"][data-group="${group}"]`);
        if (!card) return;

        // 已修复或正在修复跳过
        const cur = card.dataset.status;
        if (cur === 'fixing' || cur === 'fixed' || cur === 'healthy') return;

        if (fromDrawer) closeDrawer(root);

        updateItemStatus(card, 'fixing');

        // 模拟 1.5 - 2.5s
        const dur = 1500 + Math.random() * 1000;
        setTimer(() => {
            // 90% 成功
            const ok = Math.random() < 0.92;
            updateItemStatus(card, ok ? 'fixed' : 'failed');

            // 更新 detected 计数
            recountStats(root);

            showToast(root, ok ? `已修复：${card.querySelector('.rc-item-title').textContent.trim()}` : `修复失败，请重试`);
        }, dur);
    }

    function fixAllDetected(root) {
        const detected = $$(root, '.rc-item[data-status="detected"]');
        if (detected.length === 0) return;
        showToast(root, `开始批量修复 ${detected.length} 项…`);

        detected.forEach((card, idx) => {
            const id = card.dataset.id;
            const grp = card.dataset.group;
            // 错峰开始，避免动画一起跑显得机械
            setTimer(() => fixItem(root, id, grp, false), idx * 350);
        });
    }

    function recountStats(root) {
        const detected = $$(root, '.rc-item[data-status="detected"]').length;
        const fixed    = $$(root, '.rc-item[data-status="fixed"]').length;
        $(root, '#rcStatDetected').textContent = detected;
        $(root, '#rcStatFixed').textContent = fixed;
        $(root, '#rcFixAllNum').textContent = detected;
        const fixAllBtn = $(root, '#rcFixAllBtn');
        if (fixAllBtn) fixAllBtn.disabled = detected === 0;

        // 整页态
        if (detected === 0 && fixed > 0) {
            setPageState(root, 'done');
            const dxScoreLabel = $(root, '#rcDxScoreLabel');
            if (dxScoreLabel) dxScoreLabel.textContent = '已修复';
        }
    }

    /* ==========================================================================
       详情抽屉
       ========================================================================== */
    function findItemData(group, id) {
        const list = REPAIR_DB[group] || [];
        return list.find(x => x.id === id);
    }

    function openDrawer(root, group, id) {
        const data = findItemData(group, id);
        if (!data) return;

        const drawer = $(root, '#rcDrawer');
        const mask   = $(root, '#rcDrawerMask');
        const title  = $(root, '#rcDrawerTitle');
        const meta   = $(root, '#rcDrawerMeta');
        const icon   = $(root, '#rcDrawerIcon');
        const what   = $(root, '#rcDrawerWhat');
        const why    = $(root, '#rcDrawerWhy');
        const how    = $(root, '#rcDrawerHow');
        const cmd    = $(root, '#rcDrawerCmd');
        const warns  = $(root, '#rcDrawerWarns');
        const fixBtn = $(root, '#rcDrawerFix');

        const groupNameMap = {
            common: '常见问题', system: 'Windows 系统', network: '网络与连接',
            media: '音频与显示', game: '游戏与性能', rollback: '优化项回滚'
        };
        const riskMap = { low: '低风险', medium: '中等风险', high: '高风险' };

        title.textContent = data.name;
        meta.textContent = `${groupNameMap[group] || group} · ${riskMap[data.risk]} · 预计 ${data.time}`;
        icon.setAttribute('data-lucide', data.icon);
        what.textContent = data.what;
        why.innerHTML = data.why.map(w => `<li>${escapeHtml(w)}</li>`).join('');
        how.textContent = data.how;
        cmd.textContent = data.cmd || '';
        warns.innerHTML = data.warns.map(w =>
            `<li data-tone="${w.tone}">${w.text}</li>`
        ).join('');

        fixBtn.setAttribute('data-id', data.id);
        fixBtn.setAttribute('data-group', group);

        drawer.hidden = false;
        mask.hidden = false;
        requestAnimationFrame(() => {
            drawer.classList.add('is-open');
            mask.classList.add('is-open');
        });
        if (window.lucide) window.lucide.createIcons({ root: drawer });

        // ESC
        state.escHandler = (e) => { if (e.key === 'Escape') closeDrawer(root); };
        document.addEventListener('keydown', state.escHandler);
    }

    function closeDrawer(root) {
        const drawer = $(root, '#rcDrawer');
        const mask   = $(root, '#rcDrawerMask');
        if (!drawer) return;
        drawer.classList.remove('is-open');
        mask.classList.remove('is-open');
        setTimer(() => {
            drawer.hidden = true;
            mask.hidden = true;
        }, 320);
        if (state.escHandler) {
            document.removeEventListener('keydown', state.escHandler);
            state.escHandler = null;
        }
    }

    /* ==========================================================================
       侧导航：高亮 + 跳转
       ========================================================================== */
    function bindNav(root) {
        const items = $$(root, '.rc-nav-item');
        items.forEach(it => {
            it.addEventListener('click', (e) => {
                e.preventDefault();
                const target = it.getAttribute('href');
                const el = root.querySelector(target);
                if (el) {
                    items.forEach(x => x.classList.remove('is-active'));
                    it.classList.add('is-active');
                    el.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
            });
        });

        // IntersectionObserver 自动高亮
        const groups = $$(root, '.rc-group');
        if ('IntersectionObserver' in window) {
            const scrollContainer = root.querySelector('.rc-page');
            state.navObserver = new IntersectionObserver((entries) => {
                entries.forEach(en => {
                    if (en.isIntersecting) {
                        const id = en.target.id;
                        items.forEach(x => {
                            x.classList.toggle('is-active', x.getAttribute('href') === '#' + id);
                        });
                    }
                });
            }, { root: scrollContainer, rootMargin: '-20% 0px -70% 0px', threshold: 0 });
            groups.forEach(g => state.navObserver.observe(g));
        }
    }

    function updateNavCounts(root) {
        Object.keys(REPAIR_DB).forEach(k => {
            const navItem = root.querySelector(`.rc-nav-item[data-group="${k}"] .rc-nav-count`);
            if (navItem) navItem.textContent = REPAIR_DB[k].length;
        });
    }

    /* ==========================================================================
       Toast
       ========================================================================== */
    function showToast(root, text) {
        let toast = document.getElementById('rcToast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'rcToast';
            toast.style.cssText = `
                position: fixed;
                bottom: 32px;
                left: 50%;
                transform: translateX(-50%) translateY(20px);
                padding: 10px 18px;
                background: rgba(26, 29, 36, 0.92);
                color: #fff;
                font-size: 12.5px;
                font-weight: 600;
                border-radius: 10px;
                box-shadow: 0 8px 24px rgba(0,0,0,0.25);
                z-index: 1001;
                opacity: 0;
                transition: all 0.25s ease;
                pointer-events: none;
                backdrop-filter: blur(8px);
            `;
            document.body.appendChild(toast);
        }
        toast.textContent = text;
        requestAnimationFrame(() => {
            toast.style.opacity = '1';
            toast.style.transform = 'translateX(-50%) translateY(0)';
        });
        setTimer(() => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateX(-50%) translateY(20px)';
        }, 2400);
    }

    /* ==========================================================================
       主初始化
       ========================================================================== */
    function init(root) {
        state.currentRoot = root;
        clearTimers();

        renderCards(root);
        bindNav(root);

        // 全局点击代理
        const handler = (e) => {
            const target = e.target.closest('[data-action]');
            if (!target || !root.contains(target)) return;
            const action = target.getAttribute('data-action');
            const id = target.getAttribute('data-id');
            const grp = target.getAttribute('data-group');

            switch (action) {
                case 'run-diagnostic':
                    runDiagnostic(root);
                    break;
                case 'fix-all-detected':
                    fixAllDetected(root);
                    break;
                case 'show-detail':
                    if (id && grp) openDrawer(root, grp, id);
                    break;
                case 'fix-item':
                    if (id && grp) fixItem(root, id, grp, false);
                    break;
                case 'drawer-close':
                    closeDrawer(root);
                    break;
                case 'drawer-fix': {
                    const fid = target.getAttribute('data-id');
                    const fgrp = target.getAttribute('data-group');
                    if (fid && fgrp) fixItem(root, fid, fgrp, true);
                    break;
                }
            }
        };
        root.addEventListener('click', handler);
        state.boundClickHandler = { el: root, handler };

        // mask 点击关闭抽屉
        const mask = $(root, '#rcDrawerMask');
        if (mask) mask.addEventListener('click', () => closeDrawer(root));
    }

    /* ---------------- 销毁 ---------------- */
    function destroy() {
        clearTimers();
        if (state.boundClickHandler) {
            state.boundClickHandler.el.removeEventListener('click', state.boundClickHandler.handler);
            state.boundClickHandler = null;
        }
        if (state.escHandler) {
            document.removeEventListener('keydown', state.escHandler);
            state.escHandler = null;
        }
        if (state.navObserver) {
            state.navObserver.disconnect();
            state.navObserver = null;
        }
        const toast = document.getElementById('rcToast');
        if (toast) toast.remove();
        state.currentRoot = null;
        state.scanCount = 0;
    }

    /* ---------------- 注册 ---------------- */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_INIT__[VIEW_NAME] = init;
    window.__VIEW_DESTROY__[VIEW_NAME] = destroy;
})();
