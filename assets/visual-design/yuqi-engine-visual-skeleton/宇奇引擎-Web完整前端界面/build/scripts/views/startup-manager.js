/* ==========================================================================
   启动项管理 · Startup Manager（视图模块）
   --------------------------------------------------------------------------
   职责：
     1. 渲染真实感 mock 数据（覆盖登录项 / 计划任务 / 后台服务 / 隐藏后台）
     2. 分类筛选 / 搜索 / 排序 / 多选 / 批量启用-禁用-延迟
     3. 详情抽屉（路径 / 发布商 / 影响 / 风险解读）
     4. 一键优化 → 还原点联动确认弹窗 → Toast 反馈
     5. localStorage 桥接到 restore-point 模块（跨视图持久化）
   ========================================================================== */
(function () {
    'use strict';

    const VIEW_NAME = 'startup-manager';

    /* ----------------- Mock 数据：30 条真实中国用户 Top 启动项 ----------------- */
    const STARTUP_DATASET = [
        // ====== 登录启动项 (Login) ======
        {
            id: 'st-001', name: '微信', publisher: 'Tencent Technology (Shenzhen) Co., Ltd.',
            cat: 'login', risk: 'safe', status: 'enabled',
            path: 'C:\\Program Files\\Tencent\\WeChat\\WeChat.exe',
            time: 1.8, impact: 'med', signed: true,
            desc: '腾讯微信桌面客户端启动器',
            why: '开机自启会延长系统启动时间约 1.8 秒。可选择延迟 60 秒启动，登录后再运行。'
        },
        {
            id: 'st-002', name: 'Steam', publisher: 'Valve Corp.',
            cat: 'login', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files (x86)\\Steam\\Steam.exe',
            time: 2.4, impact: 'high', signed: true,
            desc: 'Steam 游戏平台客户端',
            why: '占用 ~280MB 内存且会持续后台更新游戏。建议禁用，需要时手动启动。'
        },
        {
            id: 'st-003', name: '网易云音乐', publisher: 'NetEase Cloud Music Co., Ltd.',
            cat: 'login', risk: 'suggest', status: 'enabled',
            path: 'D:\\Program Files\\Netease\\CloudMusic\\cloudmusic.exe',
            time: 1.6, impact: 'med', signed: true,
            desc: '网易云音乐桌面端',
            why: '非核心工具，建议禁用开机启动。'
        },
        {
            id: 'st-004', name: '搜狗输入法', publisher: 'Sogou (Beijing) Information Service Co., Ltd.',
            cat: 'login', risk: 'safe', status: 'enabled',
            path: 'C:\\Program Files (x86)\\SogouInput\\SogouCloud.exe',
            time: 0.4, impact: 'low', signed: true,
            desc: '输入法云服务后台守护',
            why: '系统输入法正常运行所需，禁用会影响文字输入。建议保留。'
        },
        {
            id: 'st-005', name: 'NVIDIA GeForce Experience',
            publisher: 'NVIDIA Corporation',
            cat: 'login', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files\\NVIDIA Corporation\\NVIDIA GeForce Experience\\NVIDIA GeForce Experience.exe',
            time: 2.8, impact: 'high', signed: true,
            desc: 'NVIDIA 驱动管理与录制工具',
            why: '驱动更新可在控制面板手动进行。常驻后台占用资源较多。'
        },
        {
            id: 'st-006', name: 'OneDrive', publisher: 'Microsoft Corporation',
            cat: 'login', risk: 'suggest', status: 'delayed',
            path: 'C:\\Users\\admin\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe',
            time: 1.9, impact: 'med', signed: true,
            desc: '微软云盘同步客户端',
            why: '若未使用 OneDrive 同步，可禁用。已设为延迟 60 秒启动。'
        },
        {
            id: 'st-007', name: 'WPS Office 助手',
            publisher: 'Kingsoft Office Software Inc.',
            cat: 'login', risk: 'suggest', status: 'enabled',
            path: 'C:\\Users\\admin\\AppData\\Local\\Kingsoft\\WPS Office\\ksomisc.exe',
            time: 1.2, impact: 'med', signed: true,
            desc: 'WPS 办公套件后台守护',
            why: '主要用于推送广告和升级提示。可安全禁用。'
        },
        {
            id: 'st-008', name: 'QQ', publisher: 'Tencent Technology (Shenzhen) Co., Ltd.',
            cat: 'login', risk: 'safe', status: 'disabled',
            path: 'C:\\Program Files (x86)\\Tencent\\QQ\\Bin\\QQ.exe',
            time: 1.5, impact: 'med', signed: true,
            desc: '腾讯 QQ 客户端',
            why: '当前已禁用。'
        },

        // ====== 计划任务 (Task Scheduler) ======
        {
            id: 'st-009', name: 'Adobe Acrobat Update Task',
            publisher: 'Adobe Inc.',
            cat: 'task', risk: 'suggest', status: 'enabled',
            path: '\\Adobe\\Acrobat Update Task',
            trigger: '登录时触发 · 每天 03:00 重复',
            time: 0.3, impact: 'low', signed: true,
            desc: 'Adobe Acrobat 自动更新任务',
            why: '该计划任务以登录用户身份周期运行，会偶发占用磁盘 IO。可禁用，必要时手动检查更新。'
        },
        {
            id: 'st-010', name: 'GoogleUpdateTaskMachineUA',
            publisher: 'Google LLC',
            cat: 'task', risk: 'suggest', status: 'enabled',
            path: '\\GoogleUpdateTaskMachineUA',
            trigger: '每小时执行一次',
            time: 0.2, impact: 'low', signed: true,
            desc: 'Google 软件更新检查任务',
            why: 'Chrome 浏览器自带的更新检查器，每小时唤醒一次。可禁用。'
        },
        {
            id: 'st-011', name: 'MicrosoftEdgeUpdateTaskMachineUA',
            publisher: 'Microsoft Corporation',
            cat: 'task', risk: 'safe', status: 'enabled',
            path: '\\MicrosoftEdgeUpdateTaskMachineUA',
            trigger: '每天 02:30 触发',
            time: 0.2, impact: 'low', signed: true,
            desc: 'Edge 浏览器更新检查',
            why: '系统级浏览器更新检查任务，建议保留。'
        },
        {
            id: 'st-012', name: 'CCleanerSkipUAC',
            publisher: 'Piriform Software Ltd.',
            cat: 'task', risk: 'danger', status: 'enabled',
            path: '\\CCleanerSkipUAC',
            trigger: '登录时触发，提升至最高权限',
            time: 0.5, impact: 'med', signed: true,
            desc: 'CCleaner 跳过 UAC 提示',
            why: '该任务以"提升的权限"启动，绕过 UAC 控制。安全风险较高，谨慎禁用前请确认 CCleaner 工作正常。'
        },

        // ====== 后台服务 (Service) ======
        {
            id: 'st-013', name: 'Adobe Genuine Monitor Service',
            publisher: 'Adobe Inc.',
            cat: 'service', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files (x86)\\Common Files\\Adobe\\AdobeGCClient\\AGMService.exe',
            time: 0.6, impact: 'med', signed: true,
            desc: 'Adobe 正版验证后台服务',
            why: '会持续后台运行验证 Adobe 软件许可。停止此服务不影响 Adobe 产品的正常使用。可禁用。'
        },
        {
            id: 'st-014', name: 'Tencent QMEmulatorService',
            publisher: 'Tencent Technology (Shenzhen) Co., Ltd.',
            cat: 'service', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files (x86)\\Tencent\\AppCenter\\TPHelper.exe',
            time: 0.8, impact: 'high', signed: true,
            desc: '腾讯手游模拟器后台服务',
            why: '即使未启动模拟器也常驻后台占用 ~120MB。可禁用，使用时手动启动。'
        },
        {
            id: 'st-015', name: 'NVIDIA Display Container LS',
            publisher: 'NVIDIA Corporation',
            cat: 'service', risk: 'danger', status: 'enabled',
            path: 'C:\\Windows\\System32\\DriverStore\\FileRepository\\nv_dispi.inf...',
            time: 0.4, impact: 'high', signed: true,
            desc: 'NVIDIA 显示驱动核心服务',
            why: 'NVIDIA 显卡驱动运行所必需。禁用将导致显示异常和黑屏。请勿禁用。'
        },
        {
            id: 'st-016', name: 'Windows Search', publisher: 'Microsoft Corporation',
            cat: 'service', risk: 'safe', status: 'enabled',
            path: 'C:\\Windows\\System32\\SearchIndexer.exe',
            time: 1.2, impact: 'med', signed: true,
            desc: 'Windows 文件搜索索引',
            why: '系统文件搜索功能依赖此服务。SSD 用户可考虑禁用以减少 IO，HDD 用户建议保留。'
        },
        {
            id: 'st-017', name: 'Microsoft Edge Update Service',
            publisher: 'Microsoft Corporation',
            cat: 'service', risk: 'safe', status: 'enabled',
            path: 'C:\\Program Files (x86)\\Microsoft\\EdgeUpdate\\MicrosoftEdgeUpdate.exe',
            time: 0.3, impact: 'low', signed: true,
            desc: 'Edge 自动更新服务',
            why: '保持浏览器安全更新，建议保留。'
        },

        // ====== 隐藏后台 (Hidden) ======
        {
            id: 'st-018', name: 'WeChatAppEx.exe (微信小程序框架)',
            publisher: 'Tencent Technology (Shenzhen) Co., Ltd.',
            cat: 'hidden', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files\\Tencent\\WeChat\\WeChatAppEx.exe',
            time: 0.5, impact: 'med', signed: true,
            desc: '微信关闭后仍后台驻留的小程序运行框架',
            why: '关闭微信后此进程仍驻留。可在微信设置中关闭"小程序后台运行"。'
        },
        {
            id: 'st-019', name: 'PluginContainer (Office)',
            publisher: 'Microsoft Corporation',
            cat: 'hidden', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files\\Microsoft Office\\root\\Office16\\PluginContainer.exe',
            time: 0.4, impact: 'low', signed: true,
            desc: 'Office 插件容器进程',
            why: '即使 Office 未启动也可能后台驻留。占用资源较小。'
        },
        {
            id: 'st-020', name: 'svchost.exe (DiagTrack)',
            publisher: 'Microsoft Corporation',
            cat: 'hidden', risk: 'suggest', status: 'enabled',
            path: 'C:\\Windows\\System32\\svchost.exe -k utcsvc',
            time: 0.6, impact: 'med', signed: true,
            desc: '诊断跟踪服务（用户体验改善计划）',
            why: '向微软发送诊断和遥测数据。可在隐私设置中关闭。'
        },
        {
            id: 'st-021', name: 'IntelCpHDCPSvc',
            publisher: 'Intel Corporation',
            cat: 'hidden', risk: 'safe', status: 'enabled',
            path: 'C:\\Windows\\System32\\DriverStore\\FileRepository\\igcc.inf...',
            time: 0.3, impact: 'low', signed: true,
            desc: 'Intel 内容保护 HDCP 服务',
            why: 'Intel 集成显卡正版内容保护组件，保留。'
        },

        // ====== 关键勿动 (Danger) ======
        {
            id: 'st-022', name: 'Windows Defender Service (WinDefend)',
            publisher: 'Microsoft Corporation',
            cat: 'service', risk: 'danger', status: 'enabled',
            path: 'C:\\ProgramData\\Microsoft\\Windows Defender\\Platform\\...\\MsMpEng.exe',
            time: 1.5, impact: 'high', signed: true,
            desc: 'Windows 安全中心核心防护进程',
            why: '系统反病毒核心服务。禁用将使系统失去基本安全防护。如需替换为第三方杀软请先安装并启用。'
        },
        {
            id: 'st-023', name: 'Windows Audio (Audiosrv)',
            publisher: 'Microsoft Corporation',
            cat: 'service', risk: 'danger', status: 'enabled',
            path: 'C:\\Windows\\System32\\svchost.exe -k LocalServiceNetworkRestricted',
            time: 0.5, impact: 'med', signed: true,
            desc: 'Windows 音频服务',
            why: '禁用后系统将无声音输出。请勿禁用。'
        },
        {
            id: 'st-024', name: 'Print Spooler (Spooler)',
            publisher: 'Microsoft Corporation',
            cat: 'service', risk: 'safe', status: 'enabled',
            path: 'C:\\Windows\\System32\\spoolsv.exe',
            time: 0.4, impact: 'low', signed: true,
            desc: 'Windows 打印后台处理程序',
            why: '若不使用打印机可考虑禁用。需要打印时请先启动此服务。'
        },

        // ====== 其他常见 ======
        {
            id: 'st-025', name: 'Logitech G HUB',
            publisher: 'Logitech Inc.',
            cat: 'login', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files\\LGHUB\\lghub.exe',
            time: 2.1, impact: 'high', signed: true,
            desc: '罗技外设管理软件',
            why: '占用资源较高，仅在调整外设时需要运行。可设为延迟启动。'
        },
        {
            id: 'st-026', name: '迅雷', publisher: 'Xunlei Networking Technologies Ltd.',
            cat: 'login', risk: 'suggest', status: 'disabled',
            path: 'C:\\Program Files (x86)\\Thunder Network\\Thunder\\Program\\Thunder.exe',
            time: 2.6, impact: 'high', signed: true,
            desc: '迅雷下载客户端',
            why: '当前已禁用。'
        },
        {
            id: 'st-027', name: 'Razer Synapse Service',
            publisher: 'Razer Inc.',
            cat: 'service', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files\\Razer\\Synapse3\\Service\\Razer Synapse Service.exe',
            time: 1.8, impact: 'med', signed: true,
            desc: '雷蛇外设驱动后台服务',
            why: '若不使用雷蛇外设可禁用。'
        },
        {
            id: 'st-028', name: 'Dropbox Update', publisher: 'Dropbox, Inc.',
            cat: 'task', risk: 'suggest', status: 'enabled',
            path: '\\DropboxUpdateTaskMachineUA',
            trigger: '每小时检查',
            time: 0.2, impact: 'low', signed: true,
            desc: 'Dropbox 自动更新',
            why: '若未使用 Dropbox 可禁用此计划任务。'
        },
        {
            id: 'st-029', name: '小米服务框架', publisher: 'Xiaomi Inc.',
            cat: 'hidden', risk: 'suggest', status: 'enabled',
            path: 'C:\\Program Files\\Xiaomi\\MiSmartShare\\MiSmartShare.exe',
            time: 0.7, impact: 'med', signed: true,
            desc: '小米跨设备协同后台框架',
            why: '若未使用小米手机互联功能可禁用。'
        },
        {
            id: 'st-030', name: '钉钉', publisher: 'DingTalk (China) Information Technology Co., Ltd.',
            cat: 'login', risk: 'safe', status: 'delayed',
            path: 'C:\\Program Files (x86)\\DingDing\\DingtalkLauncher.exe',
            time: 2.2, impact: 'high', signed: true,
            desc: '阿里钉钉桌面端',
            why: '工作场景常用。已设为延迟 60 秒启动。'
        }
    ];

    /* ----------------- 模块状态（闭包内单例） ----------------- */
    const STATE = {
        items: [],            // deep clone of dataset
        category: 'all',
        sort: 'impact',
        keyword: '',
        selected: new Set(),
        // 副作用 / 监听器记录（destroy 时清理）
        listeners: [],
        timers: [],
        toastSeq: 0
    };

    /* ----------------- 工具函数 ----------------- */
    const $ = (sel, root) => (root || document).querySelector(sel);
    const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));

    function on(el, evt, handler) {
        if (!el) return;
        el.addEventListener(evt, handler);
        STATE.listeners.push({ el, evt, handler });
    }

    function setTimer(fn, ms) {
        const t = setTimeout(() => {
            STATE.timers = STATE.timers.filter(x => x !== t);
            fn();
        }, ms);
        STATE.timers.push(t);
        return t;
    }

    /* ----------------- 类别 / 风险显示映射 ----------------- */
    const CAT_LABELS = {
        login:   { tag: '登录', icon: 'log-in' },
        task:    { tag: '任务', icon: 'calendar-clock' },
        service: { tag: '服务', icon: 'server-cog' },
        hidden:  { tag: '后台', icon: 'eye-off' }
    };

    const ITEM_ICON_MAP = {
        '微信': 'message-circle',
        'Steam': 'gamepad-2',
        '网易云音乐': 'music-2',
        '搜狗输入法': 'keyboard',
        'NVIDIA GeForce Experience': 'monitor',
        'OneDrive': 'cloud',
        'WPS Office 助手': 'file-text',
        'QQ': 'message-circle',
        'Logitech G HUB': 'mouse',
        '迅雷': 'download',
        '钉钉': 'briefcase',
        '小米服务框架': 'smartphone'
    };

    function getItemIcon(item) {
        if (ITEM_ICON_MAP[item.name]) return ITEM_ICON_MAP[item.name];
        if (item.cat === 'task') return 'calendar-clock';
        if (item.cat === 'service') return 'server-cog';
        if (item.cat === 'hidden') return 'ghost';
        return 'app-window';
    }

    /* ----------------- 过滤 / 排序 ----------------- */
    function filterItems() {
        let items = STATE.items.slice();

        // 类别
        switch (STATE.category) {
            case 'all':      break;
            case 'login':    items = items.filter(x => x.cat === 'login'); break;
            case 'task':     items = items.filter(x => x.cat === 'task'); break;
            case 'service':  items = items.filter(x => x.cat === 'service'); break;
            case 'hidden':   items = items.filter(x => x.cat === 'hidden'); break;
            case 'suggest':  items = items.filter(x => x.risk === 'suggest' && x.status !== 'disabled'); break;
            case 'danger':   items = items.filter(x => x.risk === 'danger'); break;
            case 'disabled': items = items.filter(x => x.status === 'disabled'); break;
        }

        // 搜索
        if (STATE.keyword) {
            const kw = STATE.keyword.toLowerCase();
            items = items.filter(x =>
                x.name.toLowerCase().includes(kw) ||
                (x.publisher || '').toLowerCase().includes(kw) ||
                (x.path || '').toLowerCase().includes(kw)
            );
        }

        // 排序
        const impactRank = { high: 3, med: 2, low: 1 };
        const riskRank = { danger: 3, suggest: 2, safe: 1 };
        switch (STATE.sort) {
            case 'name':
                items.sort((a, b) => a.name.localeCompare(b.name, 'zh-CN'));
                break;
            case 'time':
                items.sort((a, b) => b.time - a.time);
                break;
            case 'impact':
            default:
                items.sort((a, b) => {
                    const r = (impactRank[b.impact] || 0) - (impactRank[a.impact] || 0);
                    if (r !== 0) return r;
                    return (riskRank[b.risk] || 0) - (riskRank[a.risk] || 0);
                });
        }

        return items;
    }

    /* ----------------- 渲染：列表 ----------------- */
    function renderList(root) {
        const list = $('#stItemList', root);
        if (!list) return;

        const items = filterItems();
        // 删除非空状态项
        list.querySelectorAll('.st-item').forEach(el => el.remove());
        const emptyEl = $('.st-empty', list);

        if (items.length === 0) {
            if (emptyEl) emptyEl.hidden = false;
        } else {
            if (emptyEl) emptyEl.hidden = true;
        }

        const frag = document.createDocumentFragment();
        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'st-item';
            li.dataset.id = item.id;
            li.dataset.cat = item.cat;
            li.dataset.risk = item.risk;
            if (item.status === 'disabled') li.classList.add('is-disabled');
            if (STATE.selected.has(item.id)) li.classList.add('is-checked');

            const isLocked = item.risk === 'danger';
            const catLabel = CAT_LABELS[item.cat] || { tag: '其他' };

            // 影响等级图标（3 个雷电）
            const impactBolts = ['', '', ''].map((_, i) => {
                const lvl = item.impact;
                const onCount = lvl === 'high' ? 3 : (lvl === 'med' ? 2 : 1);
                return `<i data-lucide="zap" class="${i < onCount ? 'is-on' : ''}"></i>`;
            }).join('');

            // 状态 pill
            let statusHtml = '';
            if (item.status === 'enabled') {
                statusHtml = `<span class="st-status-pill st-status-pill--enabled">
                    <span class="st-status-pill-dot"></span>启用</span>`;
            } else if (item.status === 'delayed') {
                statusHtml = `<span class="st-status-pill st-status-pill--delayed">
                    <i data-lucide="hourglass" class="icon-xs"></i>延迟</span>`;
            } else {
                statusHtml = `<span class="st-status-pill st-status-pill--disabled">
                    <i data-lucide="circle-slash" class="icon-xs"></i>已禁用</span>`;
            }

            // 操作按钮
            let actionsHtml = '';
            if (item.status === 'disabled') {
                actionsHtml = `
                    <button class="st-item-action" data-action="enable" title="启用"><i data-lucide="play" class="icon-xs"></i></button>
                    <button class="st-item-action" data-action="detail" title="详情"><i data-lucide="info" class="icon-xs"></i></button>
                `;
            } else {
                actionsHtml = `
                    <button class="st-item-action is-warn" data-action="delay" title="延迟启动"><i data-lucide="hourglass" class="icon-xs"></i></button>
                    <button class="st-item-action ${isLocked ? 'is-danger' : ''}" data-action="disable" title="${isLocked ? '危险 · 禁用前请确认' : '禁用'}"><i data-lucide="ban" class="icon-xs"></i></button>
                    <button class="st-item-action" data-action="detail" title="详情"><i data-lucide="info" class="icon-xs"></i></button>
                `;
            }

            li.innerHTML = `
                <span class="st-col-checkbox">
                    <label class="st-checkbox" onclick="event.stopPropagation()">
                        <input type="checkbox" data-check-id="${item.id}" ${STATE.selected.has(item.id) ? 'checked' : ''} />
                        <span class="st-checkbox-box"><i data-lucide="check" class="icon-xs"></i></span>
                    </label>
                </span>

                <div class="st-item-app">
                    <span class="st-item-icon"><i data-lucide="${getItemIcon(item)}"></i></span>
                    <div class="st-item-name-wrap">
                        <div class="st-item-name-row">
                            <span class="st-item-name" title="${item.name}">${item.name}</span>
                            <span class="st-item-cat-tag st-item-cat-tag--${item.cat}">${catLabel.tag}</span>
                        </div>
                        <span class="st-item-path" title="${item.path}">${item.path}</span>
                    </div>
                </div>

                <div class="st-item-publisher ${item.signed ? '' : 'is-unsigned'}" title="${item.publisher || '未知发布商'}">
                    <i data-lucide="${item.signed ? 'badge-check' : 'badge-alert'}" class="icon-xs"></i>
                    <span>${item.publisher || '未知'}</span>
                </div>

                <div class="st-item-impact" data-level="${item.impact}">
                    <span class="st-item-impact-bolt">${impactBolts}</span>
                    <span class="st-item-impact-time">${item.time.toFixed(1)}s</span>
                </div>

                <div class="st-item-status">${statusHtml}</div>

                <div class="st-item-actions">${actionsHtml}</div>
            `;
            frag.appendChild(li);
        });

        // 插入到空状态前
        if (emptyEl) {
            list.insertBefore(frag, emptyEl);
        } else {
            list.appendChild(frag);
        }

        // 重渲图标
        if (window.lucide) window.lucide.createIcons({ root: list });

        // 更新 footer
        updateFootMeta(root, items.length);
        updateActionbar(root);
        updateSelectAllState(root);
    }

    /* ----------------- 更新 UI 辅助 ----------------- */
    function updateFootMeta(root, shownCount) {
        const meta = $('#stFootMeta', root);
        if (!meta) return;
        const total = STATE.items.length;
        const sel = STATE.selected.size;
        meta.textContent = `显示 ${shownCount} / ${total} 项 · 已选 ${sel} 项`;
    }

    function updateActionbar(root) {
        const bar = $('#stActionbar', root);
        const text = $('#stActionbarText', root);
        const btnEnable = $('#stBtnEnable', root);
        const btnDisable = $('#stBtnDisable', root);
        const btnDelay = $('#stBtnDelay', root);
        if (!bar || !text) return;

        const sel = STATE.selected;
        const selItems = STATE.items.filter(x => sel.has(x.id));

        if (sel.size === 0) {
            bar.dataset.state = 'idle';
            text.innerHTML = '选中启动项后可批量操作';
            [btnEnable, btnDisable, btnDelay].forEach(b => b && (b.disabled = true));
            return;
        }

        bar.dataset.state = 'ready';
        const totalTime = selItems.reduce((s, x) => s + x.time, 0);
        const dangerCount = selItems.filter(x => x.risk === 'danger').length;
        const dangerSuffix = dangerCount > 0
            ? `<span style="color: var(--st-red); font-weight: 700;"> · ${dangerCount} 项关键勿动</span>`
            : '';
        text.innerHTML = `已选 <strong>${sel.size}</strong> 项 · 累计启动耗时 <strong>${totalTime.toFixed(1)}s</strong>${dangerSuffix}`;

        const allDisabled = selItems.every(x => x.status === 'disabled');
        const allEnabled = selItems.every(x => x.status !== 'disabled');
        if (btnEnable) btnEnable.disabled = !selItems.some(x => x.status === 'disabled');
        if (btnDisable) btnDisable.disabled = !selItems.some(x => x.status !== 'disabled');
        if (btnDelay) btnDelay.disabled = !selItems.some(x => x.status === 'enabled');
    }

    function updateCategoryCounts(root) {
        const counts = {
            all: STATE.items.length,
            login: STATE.items.filter(x => x.cat === 'login').length,
            task: STATE.items.filter(x => x.cat === 'task').length,
            service: STATE.items.filter(x => x.cat === 'service').length,
            suggest: STATE.items.filter(x => x.risk === 'suggest' && x.status !== 'disabled').length,
            hidden: STATE.items.filter(x => x.cat === 'hidden').length,
            danger: STATE.items.filter(x => x.risk === 'danger').length,
            disabled: STATE.items.filter(x => x.status === 'disabled').length
        };
        Object.entries(counts).forEach(([cat, n]) => {
            const el = $(`.st-category[data-cat="${cat}"] .st-category-count`, root);
            if (el) el.textContent = n;
        });

        // 顶部统计
        const enabled = STATE.items.filter(x => x.status === 'enabled').length;
        const total = STATE.items.length;
        const totalBoot = STATE.items
            .filter(x => x.status === 'enabled')
            .reduce((s, x) => s + x.time, 0);

        const statBoot = $('#stStatBoot', root);
        const statEnabled = $('#stStatEnabled', root);
        const statSuggest = $('#stStatSuggest', root);
        const statHidden = $('#stStatHidden', root);

        if (statBoot)    statBoot.innerHTML = `${totalBoot.toFixed(1)}<em>s</em>`;
        if (statEnabled) statEnabled.innerHTML = `${enabled}<em>/${total}</em>`;
        if (statSuggest) statSuggest.innerHTML = `${counts.suggest}<em>项</em>`;
        if (statHidden)  statHidden.innerHTML = `${counts.hidden}<em>项</em>`;
    }

    function updateSelectAllState(root) {
        const cb = $('#stSelectAll', root);
        if (!cb) return;
        const items = filterItems();
        const allSel = items.length > 0 && items.every(x => STATE.selected.has(x.id));
        const someSel = items.some(x => STATE.selected.has(x.id));
        cb.checked = allSel;
        cb.indeterminate = !allSel && someSel;
    }

    /* ----------------- 交互：行点击 / 操作按钮 ----------------- */
    function handleItemClick(root, e) {
        const li = e.target.closest('.st-item');
        if (!li) return;

        // 操作按钮
        const actionBtn = e.target.closest('.st-item-action');
        if (actionBtn) {
            e.stopPropagation();
            const action = actionBtn.dataset.action;
            const id = li.dataset.id;
            const item = STATE.items.find(x => x.id === id);
            if (!item) return;

            if (action === 'detail') {
                openDetailDrawer(root, item);
            } else if (action === 'enable') {
                applyAction(root, [item], 'enable');
            } else if (action === 'disable') {
                if (item.risk === 'danger') {
                    if (!confirm(`"${item.name}" 是关键系统组件，禁用可能导致系统功能异常。确定要禁用吗？`)) return;
                }
                applyAction(root, [item], 'disable');
            } else if (action === 'delay') {
                applyAction(root, [item], 'delay');
            }
            return;
        }

        // 复选框
        const checkInput = e.target.closest('input[data-check-id]');
        if (checkInput) {
            // 由 change 事件处理
            return;
        }

        // 行点击：切换选择
        const id = li.dataset.id;
        toggleSelect(id);
        renderList(root);
    }

    function handleCheckboxChange(root, e) {
        const inp = e.target.closest('input[data-check-id]');
        if (!inp) return;
        const id = inp.dataset.checkId;
        if (inp.checked) {
            STATE.selected.add(id);
        } else {
            STATE.selected.delete(id);
        }
        // 不重渲整个列表（避免抖动），只更新对应行 + actionbar
        const li = $(`.st-item[data-id="${id}"]`, root);
        if (li) li.classList.toggle('is-checked', inp.checked);
        updateActionbar(root);
        updateFootMeta(root, filterItems().length);
        updateSelectAllState(root);
    }

    function toggleSelect(id) {
        if (STATE.selected.has(id)) STATE.selected.delete(id);
        else STATE.selected.add(id);
    }

    function handleSelectAllChange(root, e) {
        const cb = e.target;
        const items = filterItems();
        if (cb.checked) {
            items.forEach(x => STATE.selected.add(x.id));
        } else {
            items.forEach(x => STATE.selected.delete(x.id));
        }
        renderList(root);
    }

    /* ----------------- 应用动作（启用/禁用/延迟） ----------------- */
    function applyAction(root, items, action) {
        const verbMap = { enable: '启用', disable: '禁用', delay: '设为延迟启动' };
        const statusMap = { enable: 'enabled', disable: 'disabled', delay: 'delayed' };

        items.forEach(item => {
            item.status = statusMap[action];
        });

        // 全量重渲（状态变化影响排序/筛选）
        renderList(root);
        updateCategoryCounts(root);

        showToast(root, {
            kind: 'success',
            title: `已${verbMap[action]} ${items.length} 项`,
            sub: items.length === 1
                ? items[0].name
                : `${items[0].name} 等 ${items.length} 个启动项`
        });
    }

    /* ----------------- 详情抽屉 ----------------- */
    function openDetailDrawer(root, item) {
        const mask = $('#stDetailMask', root);
        const drawer = $('#stDetailDrawer', root);
        const titleEl = $('#stDetailTitle', root);
        const subEl = $('#stDetailSub', root);
        const bodyEl = $('#stDetailBody', root);
        const iconEl = $('#stDetailIcon', root);
        const toggleBtn = $('#stDetailToggle', root);
        if (!drawer || !mask || !bodyEl) return;

        const catLabel = CAT_LABELS[item.cat] || { tag: '其他' };

        titleEl.textContent = item.name;
        subEl.textContent = `${item.publisher || '未知发布商'} · ${catLabel.tag}启动项`;
        if (iconEl) iconEl.setAttribute('data-lucide', getItemIcon(item));

        const impactLabel = { high: '高影响', med: '中等影响', low: '低影响' }[item.impact];

        const triggerOrPath = item.cat === 'task' && item.trigger
            ? `<div class="st-detail-meta-key">触发条件</div>
               <div class="st-detail-meta-val">${item.trigger}</div>`
            : '';

        const dangerWarn = item.risk === 'danger'
            ? `<div class="st-detail-warning">
                  <i data-lucide="shield-alert"></i>
                  <span>该项标记为<strong>关键勿动</strong>，禁用可能导致系统功能异常或安全风险。请仅在确实了解后果时操作。</span>
               </div>`
            : '';

        bodyEl.innerHTML = `
            <div class="st-detail-section">
                <div class="st-detail-section-title">
                    <i data-lucide="zap" class="icon-xs"></i>
                    <span>启动影响</span>
                </div>
                <div class="st-detail-impact-bar">
                    <div class="st-detail-impact-time">${item.time.toFixed(1)}<em>s</em></div>
                    <div class="st-detail-impact-meta">
                        <span class="st-detail-impact-label">每次开机延长</span>
                        <span class="st-detail-impact-tag" data-level="${item.impact}">${impactLabel}</span>
                    </div>
                </div>
            </div>

            <div class="st-detail-section">
                <div class="st-detail-section-title">
                    <i data-lucide="file-cog" class="icon-xs"></i>
                    <span>基本信息</span>
                </div>
                <div class="st-detail-meta-grid">
                    <div class="st-detail-meta-key">类别</div>
                    <div class="st-detail-meta-val is-prose">${catLabel.tag}启动项</div>
                    <div class="st-detail-meta-key">发布商</div>
                    <div class="st-detail-meta-val is-prose">${item.publisher || '未知'} ${item.signed ? '· <span style="color: var(--st-good);">已签名</span>' : '· <span style="color: var(--st-amber);">未签名</span>'}</div>
                    <div class="st-detail-meta-key">路径</div>
                    <div class="st-detail-meta-val">${item.path}</div>
                    ${triggerOrPath}
                </div>
            </div>

            <div class="st-detail-section">
                <div class="st-detail-section-title">
                    <i data-lucide="book-open" class="icon-xs"></i>
                    <span>它做什么</span>
                </div>
                <div class="st-detail-meta-val is-prose" style="line-height: 1.6;">
                    ${item.desc}
                </div>
            </div>

            <div class="st-detail-section">
                <div class="st-detail-section-title">
                    <i data-lucide="lightbulb" class="icon-xs"></i>
                    <span>建议</span>
                </div>
                <div class="st-detail-meta-val is-prose" style="line-height: 1.6;">
                    ${item.why}
                </div>
            </div>

            ${dangerWarn}
        `;

        // 切换按钮文本
        if (toggleBtn) {
            toggleBtn.dataset.targetId = item.id;
            const toggleSpan = toggleBtn.querySelector('span');
            const toggleIcon = toggleBtn.querySelector('i');
            if (item.status === 'disabled') {
                if (toggleSpan) toggleSpan.textContent = '启用';
                if (toggleIcon) toggleIcon.setAttribute('data-lucide', 'play');
                toggleBtn.classList.remove('st-btn--warn');
            } else {
                if (toggleSpan) toggleSpan.textContent = '禁用';
                if (toggleIcon) toggleIcon.setAttribute('data-lucide', 'ban');
                toggleBtn.classList.add('st-btn--warn');
            }
        }

        if (window.lucide) window.lucide.createIcons({ root: drawer });

        // 显示
        mask.hidden = false;
        drawer.hidden = false;
        // 强制重排再加 class，触发动画
        void drawer.offsetWidth;
        mask.classList.add('is-open');
        drawer.classList.add('is-open');
    }

    function closeDetailDrawer(root) {
        const mask = $('#stDetailMask', root);
        const drawer = $('#stDetailDrawer', root);
        if (!drawer || !mask) return;
        mask.classList.remove('is-open');
        drawer.classList.remove('is-open');
        setTimer(() => {
            mask.hidden = true;
            drawer.hidden = true;
        }, 320);
    }

    /* ----------------- 一键优化 · 还原点联动确认弹窗 ----------------- */
    function openConfirmModal(root) {
        const mask = $('#stConfirmMask', root);
        const modal = $('#stConfirmModal', root);
        if (!mask || !modal) return;

        // 收集"安全可禁用 + 启动耗时 > 0.8s"的项
        const targets = STATE.items.filter(x =>
            x.status === 'enabled' &&
            x.risk === 'suggest' &&
            x.time > 0.8
        );

        const totalSave = targets.reduce((s, x) => s + x.time, 0);

        // 标题副文本
        const subEl = modal.querySelector('.st-confirm-sub');
        if (subEl) {
            subEl.textContent = `检测到 ${targets.length} 项可安全禁用 · 预计提速 ${totalSave.toFixed(1)}s`;
        }
        const countEl = $('#stConfirmCount', root);
        if (countEl) countEl.textContent = `${targets.length} 项`;

        // 渲染预览列表
        const list = $('#stConfirmPreviewList', root);
        if (list) {
            list.innerHTML = targets.map(item => `
                <li class="st-confirm-preview-item">
                    <span class="st-confirm-preview-item-icon"><i data-lucide="${getItemIcon(item)}"></i></span>
                    <span class="st-confirm-preview-item-name">${item.name}</span>
                    <span class="st-confirm-preview-item-time">-${item.time.toFixed(1)}s</span>
                </li>
            `).join('');
            if (window.lucide) window.lucide.createIcons({ root: list });
        }

        // 暂存目标
        modal.dataset.targets = JSON.stringify(targets.map(x => x.id));

        mask.hidden = false;
        modal.hidden = false;
        void modal.offsetWidth;
        mask.classList.add('is-open');
        modal.classList.add('is-open');
    }

    function closeConfirmModal(root) {
        const mask = $('#stConfirmMask', root);
        const modal = $('#stConfirmModal', root);
        if (!mask || !modal) return;
        mask.classList.remove('is-open');
        modal.classList.remove('is-open');
        setTimer(() => {
            mask.hidden = true;
            modal.hidden = true;
        }, 320);
    }

    function applyOptimize(root) {
        const modal = $('#stConfirmModal', root);
        if (!modal) return;
        const ids = JSON.parse(modal.dataset.targets || '[]');
        const targets = STATE.items.filter(x => ids.includes(x.id));
        const createRestore = $('#stConfirmRestore', root)?.checked;

        // 1. 创建还原点（联动）
        if (createRestore) {
            createRestorePointBridge(targets.length);
        }

        // 2. 应用禁用
        targets.forEach(item => { item.status = 'disabled'; });

        renderList(root);
        updateCategoryCounts(root);
        closeConfirmModal(root);

        // 3. Toast 反馈（错时）
        const totalSave = targets.reduce((s, x) => s + x.time, 0);
        if (createRestore) {
            showToast(root, {
                kind: 'restore',
                title: '已创建系统还原点',
                sub: `优化前快照已保存 · 可在"还原点"模块查看`
            });
        }
        setTimer(() => {
            showToast(root, {
                kind: 'success',
                title: `已优化 ${targets.length} 个启动项`,
                sub: `预计开机提速 ${totalSave.toFixed(1)}s`
            });
        }, createRestore ? 600 : 0);
    }

    /* ----------------- 还原点联动桥（localStorage） ----------------- */
    function createRestorePointBridge(targetCount) {
        try {
            const now = new Date();
            const pad = (n) => String(n).padStart(2, '0');
            const stamp = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())}`;

            const point = {
                id: `rp-${Date.now()}`,
                name: `启动项优化前快照`,
                desc: `自动创建于一键优化前 · 影响 ${targetCount} 个启动项`,
                time: stamp,
                source: 'startup-manager',
                createdAt: now.toISOString()
            };

            // 读取已有的桥接列表
            const KEY = 'yuqi:restore-points:bridge';
            let arr = [];
            try { arr = JSON.parse(localStorage.getItem(KEY) || '[]'); } catch (e) {}
            arr.unshift(point);
            // 保留最近 20 条
            arr = arr.slice(0, 20);
            localStorage.setItem(KEY, JSON.stringify(arr));

            // 派发事件供还原点页接收
            document.dispatchEvent(new CustomEvent('restore-point-created', {
                detail: point,
                bubbles: true
            }));
        } catch (e) {
            console.warn('[startup-manager] 还原点桥接失败', e);
        }
    }

    /* ----------------- Toast ----------------- */
    function showToast(root, opt) {
        const stack = $('#stToastStack', root);
        if (!stack) return;
        const id = ++STATE.toastSeq;

        const iconMap = {
            success: 'check-circle-2',
            info:    'info',
            warn:    'alert-triangle',
            restore: 'history'
        };
        const icon = iconMap[opt.kind] || 'info';

        const toast = document.createElement('div');
        toast.className = `st-toast st-toast--${opt.kind}`;
        toast.dataset.toastId = id;
        toast.innerHTML = `
            <i data-lucide="${icon}" class="st-toast-icon"></i>
            <span class="st-toast-text">
                ${opt.title}
                ${opt.sub ? `<small>${opt.sub}</small>` : ''}
            </span>
        `;
        stack.appendChild(toast);
        if (window.lucide) window.lucide.createIcons({ root: toast });

        // 入场
        requestAnimationFrame(() => toast.classList.add('is-in'));

        // 自动消失
        setTimer(() => {
            toast.classList.remove('is-in');
            setTimer(() => toast.remove(), 280);
        }, 3200);
    }

    /* ----------------- 分类 / 排序 / 搜索 / 工具栏 ----------------- */
    function bindEvents(root) {
        // 分类
        $$('.st-category', root).forEach(el => {
            on(el, 'click', () => {
                $$('.st-category', root).forEach(x => x.classList.remove('is-active'));
                el.classList.add('is-active');
                STATE.category = el.dataset.cat;
                STATE.selected.clear();
                renderList(root);
            });
        });

        // 排序
        $$('.st-sort-chip', root).forEach(el => {
            on(el, 'click', () => {
                $$('.st-sort-chip', root).forEach(x => x.classList.remove('is-active'));
                el.classList.add('is-active');
                STATE.sort = el.dataset.sort;
                renderList(root);
            });
        });

        // 搜索
        const searchInput = $('#stSearchInput', root);
        const searchClear = $('#stSearchClear', root);
        on(searchInput, 'input', () => {
            STATE.keyword = searchInput.value.trim();
            if (searchClear) searchClear.hidden = !STATE.keyword;
            renderList(root);
        });
        on(searchClear, 'click', () => {
            if (searchInput) searchInput.value = '';
            STATE.keyword = '';
            searchClear.hidden = true;
            renderList(root);
            searchInput?.focus();
        });

        // 全选 + 行内复选框
        on($('#stSelectAll', root), 'change', (e) => handleSelectAllChange(root, e));

        const itemList = $('#stItemList', root);
        on(itemList, 'click', (e) => handleItemClick(root, e));
        on(itemList, 'change', (e) => handleCheckboxChange(root, e));

        // 顶部按钮
        on($('#stBtnScan', root), 'click', () => {
            const btn = $('#stBtnScan', root);
            const icon = btn?.querySelector('i');
            if (icon) icon.style.animation = 'cpuSpin 800ms linear infinite';
            setTimer(() => {
                if (icon) icon.style.animation = '';
                showToast(root, {
                    kind: 'success',
                    title: '扫描完成',
                    sub: `发现 ${STATE.items.length} 个启动项 · ${STATE.items.filter(x => x.risk === 'suggest' && x.status !== 'disabled').length} 项可优化`
                });
            }, 1200);
        });

        on($('#stBtnOptimize', root), 'click', () => openConfirmModal(root));

        // 底部操作条
        on($('#stBtnEnable', root), 'click', () => {
            const items = STATE.items.filter(x => STATE.selected.has(x.id) && x.status === 'disabled');
            if (items.length === 0) return;
            applyAction(root, items, 'enable');
            STATE.selected.clear();
            renderList(root);
        });

        on($('#stBtnDisable', root), 'click', () => {
            const items = STATE.items.filter(x => STATE.selected.has(x.id) && x.status !== 'disabled');
            if (items.length === 0) return;
            const dangerCount = items.filter(x => x.risk === 'danger').length;
            if (dangerCount > 0) {
                if (!confirm(`所选项目中包含 ${dangerCount} 项关键系统组件，禁用可能导致系统异常。确定继续吗？`)) return;
            }
            applyAction(root, items, 'disable');
            STATE.selected.clear();
            renderList(root);
        });

        on($('#stBtnDelay', root), 'click', () => {
            const items = STATE.items.filter(x => STATE.selected.has(x.id) && x.status === 'enabled');
            if (items.length === 0) return;
            applyAction(root, items, 'delay');
            STATE.selected.clear();
            renderList(root);
        });

        // 详情抽屉
        on($('#stDetailMask', root), 'click', () => closeDetailDrawer(root));
        on($('#stDetailClose', root), 'click', () => closeDetailDrawer(root));
        on($('#stDetailToggle', root), 'click', (e) => {
            const btn = e.currentTarget;
            const id = btn.dataset.targetId;
            const item = STATE.items.find(x => x.id === id);
            if (!item) return;
            const action = item.status === 'disabled' ? 'enable' : 'disable';
            if (action === 'disable' && item.risk === 'danger') {
                if (!confirm(`"${item.name}" 是关键系统组件，禁用可能导致系统功能异常。确定要禁用吗？`)) return;
            }
            applyAction(root, [item], action);
            closeDetailDrawer(root);
        });
        on($('[data-detail-action="locate"]', root), 'click', () => {
            showToast(root, { kind: 'info', title: '已模拟打开文件位置', sub: '真实环境中将调用资源管理器定位' });
        });

        // 工具栏 toggle
        $$('.st-toolbar-btn', root).forEach(el => {
            on(el, 'click', () => el.classList.toggle('is-active'));
        });

        // 一键优化弹窗
        on($('#stConfirmMask', root), 'click', () => closeConfirmModal(root));
        on($('#stConfirmClose', root), 'click', () => closeConfirmModal(root));
        on($('#stConfirmCancel', root), 'click', () => closeConfirmModal(root));
        on($('#stConfirmApply', root), 'click', () => applyOptimize(root));

        // ESC 关闭
        on(document, 'keydown', (e) => {
            if (e.key !== 'Escape') return;
            const drawer = $('#stDetailDrawer', root);
            const modal = $('#stConfirmModal', root);
            if (drawer && drawer.classList.contains('is-open')) closeDetailDrawer(root);
            else if (modal && modal.classList.contains('is-open')) closeConfirmModal(root);
        });
    }

    /* ----------------- 初始化 ----------------- */
    function init(root) {
        // 重置状态
        STATE.items = STATE.items.length === 0
            ? STARTUP_DATASET.map(x => ({ ...x }))   // deep clone（浅）
            : STATE.items;
        STATE.category = 'all';
        STATE.sort = 'impact';
        STATE.keyword = '';
        STATE.selected.clear();

        renderList(root);
        updateCategoryCounts(root);
        bindEvents(root);
    }

    function destroy() {
        // 清理监听器
        STATE.listeners.forEach(({ el, evt, handler }) => {
            try { el.removeEventListener(evt, handler); } catch (e) {}
        });
        STATE.listeners = [];

        // 清理定时器
        STATE.timers.forEach(t => clearTimeout(t));
        STATE.timers = [];

        // 不清空 STATE.items（保留状态以便切回时恢复）
        STATE.selected.clear();
    }

    /* ----------------- 注册到 view-loader ----------------- */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_INIT__[VIEW_NAME] = init;
    window.__VIEW_DESTROY__[VIEW_NAME] = destroy;
})();
