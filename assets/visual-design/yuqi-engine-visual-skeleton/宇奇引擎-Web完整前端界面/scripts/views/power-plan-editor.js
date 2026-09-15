/* ==========================================================================
   电源优化 · The Power Console v3 · View JS
   --------------------------------------------------------------------------
   叙事重构：CPU 智能识别 → 单按钮 AI 智能优化 → Windows 电源计划集中管理
   职责：
     1. CPU 识别面板 + CPU 演示切换器（4 种 CPU 配置档案）
     2. 智能优化 Hero CTA + 单按钮触发 AI 推荐弹窗
     3. Windows 电源计划列表（默认 3 + AI/自定义 N · 切换/重命名/删除/新建）
     4. 当前生效参数面板（核心 6 + 展开 22 · 只读）
     5. 高级参数 6 大分组（可编辑 · 输入/下拉 · 脏标 · 应用/重置）
     6. 智能优化弹窗（Diff 预览） + 重命名 + 删除 + 命令岛 + Toast
   ========================================================================== */
(function () {
    'use strict';

    /* ==========================================================================
       1. CPU 配置档案（4 种真实架构）
    ========================================================================== */
    const CPU_PROFILES = {
        intel_hybrid: {
            name: 'Intel Core i7-14700K',
            vendor: 'Intel',
            architecture: 'IntelHybrid',
            archDesc: 'Intel 混合架构 (P-Core + E-Core)',
            archShort: 'Intel 混合架构',
            spec: '8 P-Core + 12 E-Core · 28 线程',
            recommend: '推荐锁定到 P-Core 以获得最佳单线程性能',
            isHybrid: true,
            injects: [
                { vibe: 'violet', icon: 'git-branch',  name: '大小核策略',     key: 'hetero_policy' },
                { vibe: 'amber',  icon: 'gauge',        name: '大核响应阈值',   key: 'class1_eff_threshold' },
                { vibe: 'rose',   icon: 'trending-up',  name: '大核性能级别',   key: 'class1_perf_level' }
            ],
            heroBullets: [
                '为 P-Core 注入大小核调度策略',
                '锁定大核响应阈值以降低延迟',
                '保留 E-Core 用于后台调度'
            ]
        },
        intel_standard: {
            name: 'Intel Core i7-10700K',
            vendor: 'Intel',
            architecture: 'IntelStandard',
            archDesc: 'Intel 标准架构 · 8 核 16 线程',
            archShort: 'Intel 标准架构',
            spec: '8 物理核心 · 16 线程 · 无大小核',
            recommend: '推荐使用全核心策略以获得稳定性能',
            isHybrid: false,
            injects: [],
            heroBullets: [
                '保持 100% 频率上限',
                '禁用 Core Parking',
                '启用激进 Boost'
            ]
        },
        amd_x3d: {
            name: 'AMD Ryzen 7 7800X3D',
            vendor: 'AMD',
            architecture: 'AmdX3D',
            archDesc: 'AMD X3D 架构 · 3D V-Cache 加持',
            archShort: 'AMD X3D 架构',
            spec: '8 核 16 线程 · 1 CCD · V-Cache 96MB',
            recommend: '推荐锁定到 V-Cache CCD 以获得最佳游戏性能',
            isHybrid: false,
            injects: [
                { vibe: 'green', icon: 'cpu',         name: '全处理器策略', key: 'hetero_policy' },
                { vibe: 'cyan',  icon: 'database',    name: 'V-Cache 优先', key: 'amd_vcache_priority' }
            ],
            heroBullets: [
                '锁定到 V-Cache CCD',
                '禁用 ASPM 减少缓存干扰',
                '保持 Boost 全开'
            ]
        },
        amd_multi_ccd: {
            name: 'AMD Ryzen 9 7950X',
            vendor: 'AMD',
            architecture: 'AmdMultiCcd',
            archDesc: 'AMD 多 CCD 架构 · 16 核',
            archShort: 'AMD 多 CCD 架构',
            spec: '16 核 32 线程 · 2 CCD · 无 V-Cache',
            recommend: '推荐锁定到主 CCD 减少跨 CCD 延迟',
            isHybrid: false,
            injects: [
                { vibe: 'green', icon: 'cpu', name: '全处理器策略', key: 'hetero_policy' }
            ],
            heroBullets: [
                '保持全处理器策略',
                '禁用 Core Parking',
                '关闭跨 CCD 节能'
            ]
        }
    };

    /* ==========================================================================
       2. 参数显示元数据 + 编辑控件描述符
    ========================================================================== */
    const PARAM_META = {
        proc_min_state:           { name: 'CPU 最低状态',     unit: '%',  type: 'number', min: 0,   max: 100, step: 5,  desc: 'CPU 频率下限百分比' },
        proc_max_state:           { name: 'CPU 最高状态',     unit: '%',  type: 'number', min: 0,   max: 100, step: 5,  desc: 'CPU 频率上限百分比' },
        boost_mode:               { name: '处理器加速模式',   unit: '',   type: 'enum',   enumMap: { 0: '禁用', 1: '启用', 2: '激进', 3: '高效启用', 4: '激进保守' } },
        core_parking_min:         { name: 'Core Parking 最小', unit: '%',  type: 'number', min: 0,   max: 100, step: 5 },
        core_parking_max:         { name: 'Core Parking 最大', unit: '%',  type: 'number', min: 0,   max: 100, step: 5 },
        core_parking_concurrency: { name: 'Parking 并发',      unit: '',   type: 'number', min: 0,   max: 100, step: 1 },
        perf_increase_threshold:  { name: '性能提升阈值',     unit: '%',  type: 'number', min: 1,   max: 100, step: 1 },
        perf_decrease_threshold:  { name: '性能降低阈值',     unit: '%',  type: 'number', min: 1,   max: 100, step: 1 },
        idle_demote_threshold:    { name: '闲置降级阈值',     unit: '%',  type: 'number', min: 0,   max: 100, step: 5 },
        idle_promote_threshold:   { name: '闲置提升阈值',     unit: '%',  type: 'number', min: 0,   max: 100, step: 5 },
        perf_time_check:          { name: '性能检查间隔',     unit: 'ms', type: 'number', min: 1,   max: 60000, step: 1 },
        throttle_policy:          { name: 'CPU 节流策略',     unit: '',   type: 'enum',   enumMap: { 0: '禁用节流', 1: '启用' } },
        idle_disable:             { name: '空闲禁用',         unit: '',   type: 'enum',   enumMap: { 0: '允许', 1: '禁用' } },
        pcie_aspm:                { name: 'PCIe ASPM 节能',   unit: '',   type: 'enum',   enumMap: { 0: '关闭', 1: 'L0s', 2: 'L1' } },
        usb_selective_suspend:    { name: 'USB 选择性暂停',   unit: '',   type: 'enum',   enumMap: { 0: '禁用', 1: '启用' } },
        usb_link_power:           { name: 'USB 链接节能',     unit: '',   type: 'enum',   enumMap: { 0: '关闭', 1: '中等', 2: '最大' } },
        network_power_saving:     { name: '网卡节能',         unit: '',   type: 'enum',   enumMap: { 0: '禁用', 1: '启用' } },
        cooling_policy:           { name: '散热策略',         unit: '',   type: 'enum',   enumMap: { 0: '被动', 1: '主动' } },
        disk_turn_off:            { name: '硬盘休眠',         unit: '分', type: 'number', min: 0,   max: 9999, step: 1 },
        display_turn_off:         { name: '显示器关闭',       unit: '分', type: 'number', min: 0,   max: 9999, step: 1 },
        sleep_after:              { name: '系统睡眠',         unit: '分', type: 'number', min: 0,   max: 9999, step: 1 },
        hibernate_after:          { name: '系统休眠',         unit: '分', type: 'number', min: 0,   max: 9999, step: 1 },
        hetero_policy:            { name: '大小核调度策略',   unit: '',   type: 'enum',   enumMap: { 0: '全处理器', 1: '首选 E-Core', 2: '首选 P-Core', 3: '自动 1', 4: '自动 2', 5: '自动' }, ai: true },
        class1_eff_threshold:     { name: '大核响应阈值',     unit: '',   type: 'number', min: 0,   max: 100, step: 1, ai: true },
        class1_perf_level:        { name: '大核性能级别',     unit: '',   type: 'number', min: 0,   max: 100, step: 5, ai: true },
        amd_vcache_priority:      { name: 'V-Cache 优先级',   unit: '',   type: 'enum',   enumMap: { 0: '关闭', 1: '启用' }, ai: true }
    };

    /* ==========================================================================
       3. Windows 默认 3 计划（每个计划自带一套真实参数）
    ========================================================================== */
    const WIN_DEFAULT_PLANS = [
        {
            id: 'win_balanced',
            name: '平衡',
            sub: 'Windows 默认 · 381b4222',
            icon: 'circle-equal',
            source: 'windows',
            readonly: true,
            settings: {
                proc_min_state: 5, proc_max_state: 100, boost_mode: 1,
                core_parking_min: 50, core_parking_max: 100,
                perf_increase_threshold: 10, perf_decrease_threshold: 8,
                idle_demote_threshold: 40, idle_promote_threshold: 60,
                perf_time_check: 15,
                pcie_aspm: 1, usb_selective_suspend: 1, usb_link_power: 1,
                network_power_saving: 1, cooling_policy: 1,
                disk_turn_off: 20, display_turn_off: 10, sleep_after: 30, hibernate_after: 0
            }
        },
        {
            id: 'win_high_perf',
            name: '高性能',
            sub: 'Windows 默认 · 8c5e7fda',
            icon: 'zap',
            source: 'windows',
            readonly: true,
            settings: {
                proc_min_state: 100, proc_max_state: 100, boost_mode: 2,
                core_parking_min: 100, core_parking_max: 100,
                perf_increase_threshold: 1, perf_decrease_threshold: 1,
                idle_demote_threshold: 100, idle_promote_threshold: 100,
                perf_time_check: 5000,
                pcie_aspm: 0, usb_selective_suspend: 0, usb_link_power: 0,
                network_power_saving: 0, cooling_policy: 1,
                disk_turn_off: 0, display_turn_off: 15, sleep_after: 0, hibernate_after: 0
            }
        },
        {
            id: 'win_power_saver',
            name: '节能',
            sub: 'Windows 默认 · a1841308',
            icon: 'battery-low',
            source: 'windows',
            readonly: true,
            settings: {
                proc_min_state: 5, proc_max_state: 50, boost_mode: 0,
                core_parking_min: 5, core_parking_max: 50,
                perf_increase_threshold: 30, perf_decrease_threshold: 50,
                idle_demote_threshold: 20, idle_promote_threshold: 40,
                perf_time_check: 30,
                pcie_aspm: 2, usb_selective_suspend: 1, usb_link_power: 2,
                network_power_saving: 1, cooling_policy: 0,
                disk_turn_off: 5, display_turn_off: 5, sleep_after: 15, hibernate_after: 60
            }
        }
    ];

    /* 高级参数 6 大分组 */
    const PARAM_GROUPS = [
        { id: 'cpu',     name: 'CPU 性能',     icon: 'cpu',           keys: ['proc_min_state', 'proc_max_state', 'boost_mode', 'core_parking_min', 'core_parking_max'] },
        { id: 'thresh',  name: '响应阈值',     icon: 'gauge',         keys: ['perf_increase_threshold', 'perf_decrease_threshold', 'idle_demote_threshold', 'idle_promote_threshold', 'perf_time_check'] },
        { id: 'hetero',  name: 'AI 大小核',    icon: 'sparkles',      keys: ['hetero_policy', 'class1_eff_threshold', 'class1_perf_level', 'amd_vcache_priority'], vibe: 'violet' },
        { id: 'eco',     name: '节能与外设',   icon: 'plug-zap',      keys: ['pcie_aspm', 'usb_selective_suspend', 'usb_link_power', 'network_power_saving', 'cooling_policy'] },
        { id: 'sleep',   name: '休眠时序',     icon: 'moon',          keys: ['disk_turn_off', 'display_turn_off', 'sleep_after', 'hibernate_after'] },
        { id: 'other',   name: '其他',         icon: 'more-horizontal', keys: ['throttle_policy', 'idle_disable', 'core_parking_concurrency'] }
    ];

    /* ==========================================================================
       4. 模块状态
    ========================================================================== */
    const state = {
        currentCpuId: 'intel_hybrid',
        plans: [],            // 用户拥有的所有电源计划
        activePlanId: 'win_balanced',
        editingPlanId: null,  // 重命名/删除目标
        advancedDirty: {},    // { key: newValue } · 未保存的高级参数修改
        applying: false
    };

    let rootEl = null;
    let escHandler = null;
    let portaledNodes = [];

    /* ==========================================================================
       5. 工具函数
    ========================================================================== */
    function getCpu() { return CPU_PROFILES[state.currentCpuId]; }
    function getPlan(id) { return state.plans.find(p => p.id === id); }
    function getActivePlan() { return getPlan(state.activePlanId) || state.plans[0]; }

    function $(sel) {
        if (!rootEl) return null;
        return rootEl.querySelector(sel) || document.querySelector(sel);
    }
    function $$(sel) { return rootEl ? Array.from(rootEl.querySelectorAll(sel)) : []; }

    function isAiKey(key) {
        return key === 'hetero_policy' || key === 'class1_eff_threshold' || key === 'class1_perf_level' || key === 'amd_vcache_priority';
    }

    function formatValue(key, val) {
        if (val === undefined || val === null) return '—';
        const meta = PARAM_META[key];
        if (!meta) return String(val);
        if (meta.type === 'enum' && meta.enumMap && meta.enumMap[val] !== undefined) return `${val} · ${meta.enumMap[val]}`;
        if (meta.unit) return `${val}${meta.unit}`;
        return String(val);
    }

    /** 基于 CPU 自动生成一份 AI 智能优化的电源计划参数 */
    function buildAiRecommendedSettings(cpu) {
        // 通用基础（趋向"高性能 + 智能"风格，但保持时序合理）
        const settings = {
            proc_min_state: 100, proc_max_state: 100, boost_mode: 2,
            core_parking_min: 100, core_parking_max: 100,
            perf_increase_threshold: 1, perf_decrease_threshold: 1,
            idle_demote_threshold: 100, idle_promote_threshold: 100,
            perf_time_check: 5000,
            pcie_aspm: 0, usb_selective_suspend: 0, usb_link_power: 0,
            network_power_saving: 0, cooling_policy: 1,
            disk_turn_off: 0, display_turn_off: 15, sleep_after: 0, hibernate_after: 0,
            throttle_policy: 0, idle_disable: 1, core_parking_concurrency: 0
        };
        if (cpu.architecture === 'IntelHybrid') {
            settings.hetero_policy = 2;          // 首选 P-Core
            settings.class1_eff_threshold = 1;
            settings.class1_perf_level = 100;
        } else if (cpu.architecture === 'AmdX3D') {
            settings.hetero_policy = 0;
            settings.amd_vcache_priority = 1;
        } else if (cpu.architecture === 'AmdMultiCcd') {
            settings.hetero_policy = 0;
        }
        return settings;
    }

    /* ==========================================================================
       6. 渲染：CPU 识别面板
    ========================================================================== */
    function renderCpuPanel() {
        const cpu = getCpu();
        const nameEl = $('#pwCpuName');
        const archEl = $('#pwCpuArchText');
        const specEl = $('#pwCpuSpec');
        const recEl  = $('#pwCpuRecommend');
        const cntEl  = $('#pwInjectionCount');
        const tagsEl = $('#pwInjectionTags');

        if (nameEl) nameEl.textContent = cpu.name;
        if (archEl) archEl.textContent = cpu.archDesc;
        if (specEl) specEl.textContent = cpu.spec;
        if (recEl)  recEl.textContent  = cpu.recommend;
        if (cntEl)  cntEl.textContent  = cpu.injects.length || cpu.heroBullets.length;

        if (tagsEl) {
            if (cpu.injects.length === 0) {
                tagsEl.innerHTML = `
                    <span class="pw-injection-tag" data-vibe="slate" style="opacity:0.85">
                        <i data-lucide="info" class="icon-xs"></i>
                        <span>通用优化 · 你的 CPU 不需要厂商专属注入</span>
                    </span>
                `;
            } else {
                tagsEl.innerHTML = cpu.injects.map(inj => `
                    <span class="pw-injection-tag" data-vibe="${inj.vibe}">
                        <i data-lucide="${inj.icon}" class="icon-xs"></i>
                        <span>${inj.name}</span>
                        <code class="pw-tag-key">${inj.key}</code>
                    </span>
                `).join('');
            }
        }

        $$('.pw-switcher-card').forEach(card => {
            card.dataset.active = card.dataset.cpuId === state.currentCpuId ? 'true' : 'false';
        });

        if (rootEl) rootEl.dataset.cpu = state.currentCpuId;

        if (window.lucide && tagsEl) lucide.createIcons({ root: tagsEl });
    }

    /* ==========================================================================
       7. 渲染：智能优化 Hero CTA
    ========================================================================== */
    function renderSmartHero() {
        const cpu = getCpu();
        const cpuEl = $('#pwSmartHeroCpu');
        const bulletsEl = $('#pwSmartHeroBullets');
        if (cpuEl) cpuEl.textContent = cpu.archShort;
        if (bulletsEl) {
            bulletsEl.innerHTML = cpu.heroBullets.map(b => `
                <li><i data-lucide="check"></i><span>${b}</span></li>
            `).join('');
            if (window.lucide) lucide.createIcons({ root: bulletsEl });
        }
    }

    /* ==========================================================================
       8. 渲染：电源计划列表
    ========================================================================== */
    function renderPlanList() {
        const list = $('#pwPlanList');
        if (!list) return;
        list.innerHTML = state.plans.map(p => {
            const isActive = p.id === state.activePlanId;
            const sourceTag = p.source === 'windows'
                ? '<span class="pw-plan-tag" data-vibe="windows"><i data-lucide="archive-restore"></i>系统</span>'
                : p.source === 'ai'
                ? '<span class="pw-plan-tag" data-vibe="ai"><i data-lucide="sparkles"></i>AI</span>'
                : '<span class="pw-plan-tag" data-vibe="custom"><i data-lucide="user"></i>自定义</span>';

            const activeTag = isActive
                ? '<span class="pw-plan-tag" data-vibe="active"><i data-lucide="check"></i>当前激活</span>'
                : '';

            const actions = [];
            if (isActive) {
                actions.push('<span class="pw-plan-action-btn" data-variant="active-pin"><i data-lucide="check"></i>已激活</span>');
            } else {
                actions.push(`<button class="pw-plan-action-btn" data-variant="primary" type="button" data-action="activate-plan" data-plan-id="${p.id}"><i data-lucide="zap"></i>切换</button>`);
            }
            if (!p.readonly) {
                actions.push(`<button class="pw-plan-action-btn pw-plan-icon-only" type="button" data-action="rename-plan" data-plan-id="${p.id}" title="重命名"><i data-lucide="pen-line"></i></button>`);
                actions.push(`<button class="pw-plan-action-btn pw-plan-icon-only" data-variant="danger" type="button" data-action="delete-plan" data-plan-id="${p.id}" title="删除"><i data-lucide="trash-2"></i></button>`);
            }

            return `
                <li class="pw-plan-row" data-plan-id="${p.id}" data-active="${isActive}" data-source="${p.source}">
                    <span class="pw-plan-icon-chip"><i data-lucide="${p.icon}"></i></span>
                    <div class="pw-plan-meta">
                        <div class="pw-plan-name-row">
                            <span class="pw-plan-name">${p.name}</span>
                            ${sourceTag}
                            ${activeTag}
                        </div>
                        <span class="pw-plan-sub">${p.sub}</span>
                    </div>
                    <div class="pw-plan-actions">${actions.join('')}</div>
                </li>
            `;
        }).join('');
        if (window.lucide) lucide.createIcons({ root: list });
    }

    /* ==========================================================================
       9. 渲染：状态条
    ========================================================================== */
    function renderActiveBar() {
        const p = getActivePlan();
        if (!p) return;
        const planEl = $('#pwActivePlan');
        const sourceEl = $('#pwActiveSource');
        if (planEl) planEl.textContent = p.name;
        if (sourceEl) {
            sourceEl.textContent = p.source === 'windows' ? 'Windows 默认' : p.source === 'ai' ? 'AI 智能优化' : '自定义计划';
            sourceEl.dataset.source = p.source;
        }
    }

    /* ==========================================================================
       10. 渲染：当前生效参数面板（核心 6 + 全部 · 只读）
    ========================================================================== */
    function renderCurrentPanel() {
        const p = getActivePlan();
        if (!p) return;
        const settings = p.settings;

        const sourceEl = $('#pwCurrentSource');
        if (sourceEl) {
            sourceEl.textContent = p.source === 'windows'
                ? `${p.name}（Windows 默认）`
                : `${p.name}`;
        }

        // 核心 6 项：proc_max_state / proc_min_state / boost_mode / core_parking_min / cooling_policy + 第 1 个 AI 项
        const coreCandidates = ['proc_max_state', 'proc_min_state', 'boost_mode', 'core_parking_min', 'cooling_policy'];
        const aiKeys = Object.keys(settings).filter(isAiKey);
        const coreKeys = coreCandidates.filter(k => settings[k] !== undefined);
        if (aiKeys.length && coreKeys.length < 6) coreKeys.push(aiKeys[0]);
        const coreKeysFinal = coreKeys.slice(0, 6);

        const renderRow = (key) => {
            const meta = PARAM_META[key] || { name: key, unit: '' };
            const v = settings[key];
            if (v === undefined) return '';
            const ai = isAiKey(key);
            return `<li class="pw-param-row" data-ai="${ai}">
                <div class="pw-param-meta">
                    <span class="pw-param-name">${ai ? '<span class="pw-ai-dot"></span>' : ''}${meta.name}</span>
                    <span class="pw-param-key">${key}</span>
                </div>
                <span class="pw-param-value">${formatValue(key, v)}</span>
            </li>`;
        };

        const coreEl = $('#pwParamCore');
        const fullEl = $('#pwParamFull');
        const totalEl = $('#pwParamTotal');

        if (coreEl) coreEl.innerHTML = coreKeysFinal.map(renderRow).join('');
        const allKeys = Object.keys(settings);
        if (fullEl) fullEl.innerHTML = allKeys.map(renderRow).join('');
        if (totalEl) totalEl.textContent = allKeys.length;
    }

    /* ==========================================================================
       11. 渲染：高级参数 6 大分组（可编辑 · 输入/下拉）
    ========================================================================== */
    function renderGroups() {
        const grid = $('#pwGroupGrid');
        if (!grid) return;
        const p = getActivePlan();
        if (!p) return;
        const settings = p.settings;
        const editable = !p.readonly;

        grid.innerHTML = PARAM_GROUPS.map((g, idx) => {
            const rows = g.keys.map(k => {
                const meta = PARAM_META[k];
                if (!meta) return '';
                const baseV = settings[k];
                const dirtyV = state.advancedDirty[k];
                const hasDirty = state.advancedDirty.hasOwnProperty(k);
                const v = hasDirty ? dirtyV : baseV;
                const ai = isAiKey(k);

                // 不存在该参数且只读时跳过；可编辑时允许显示空控件
                if (v === undefined && !editable) {
                    return `<div class="pw-param-row" data-ai="${ai}">
                        <div class="pw-param-meta">
                            <span class="pw-param-name">${ai ? '<span class="pw-ai-dot"></span>' : ''}${meta.name}</span>
                            <span class="pw-param-key">${k}</span>
                        </div>
                        <span class="pw-param-value">—</span>
                    </div>`;
                }

                if (!editable) {
                    return `<div class="pw-param-row" data-ai="${ai}">
                        <div class="pw-param-meta">
                            <span class="pw-param-name">${ai ? '<span class="pw-ai-dot"></span>' : ''}${meta.name}</span>
                            <span class="pw-param-key">${k}</span>
                        </div>
                        <span class="pw-param-value">${formatValue(k, v)}</span>
                    </div>`;
                }

                // 可编辑控件
                let control;
                if (meta.type === 'enum') {
                    const opts = Object.entries(meta.enumMap).map(([val, label]) =>
                        `<option value="${val}" ${String(val) === String(v) ? 'selected' : ''}>${val} · ${label}</option>`
                    ).join('');
                    control = `<div class="pw-param-input-wrap">
                        <select class="pw-param-input pw-param-input--select" data-action="edit-param" data-key="${k}">${opts}</select>
                    </div>`;
                } else {
                    const minAttr = meta.min !== undefined ? `min="${meta.min}"` : '';
                    const maxAttr = meta.max !== undefined ? `max="${meta.max}"` : '';
                    const stepAttr = meta.step !== undefined ? `step="${meta.step}"` : '';
                    const suffix = meta.unit ? `<span class="pw-param-input-suffix">${meta.unit}</span>` : '';
                    const cls = meta.unit ? 'pw-param-input pw-param-input--with-suffix' : 'pw-param-input';
                    control = `<div class="pw-param-input-wrap">
                        <input class="${cls}" type="number" ${minAttr} ${maxAttr} ${stepAttr}
                               value="${v ?? ''}"
                               data-action="edit-param" data-key="${k}">
                        ${suffix}
                    </div>`;
                }

                return `<div class="pw-param-row pw-param-row--editable" data-ai="${ai}" data-dirty="${hasDirty}">
                    <div class="pw-param-meta">
                        <span class="pw-param-name">${ai ? '<span class="pw-ai-dot"></span>' : ''}${meta.name}</span>
                        <span class="pw-param-key">${k}</span>
                    </div>
                    ${control}
                </div>`;
            }).join('');
            const validCount = g.keys.filter(k => settings[k] !== undefined).length;
            return `<details class="pw-group" ${idx < 3 ? 'open' : ''} ${g.vibe ? `data-vibe="${g.vibe}"` : ''}>
                <summary class="pw-group-summary">
                    <i data-lucide="${g.icon}" class="pw-group-icon"></i>
                    <span class="pw-group-name">${g.name}</span>
                    <span class="pw-group-count">${validCount}/${g.keys.length}</span>
                    <i data-lucide="chevron-down" class="pw-group-chev"></i>
                </summary>
                <div class="pw-group-body">${rows}</div>
            </details>`;
        }).join('');

        if (window.lucide) lucide.createIcons({ root: grid });
    }

    function refreshDirtyUi() {
        const dirtyKeys = Object.keys(state.advancedDirty);
        const count = dirtyKeys.length;
        const dirtyEl = $('#pwAdvancedDirty');
        const cntEl = $('#pwAdvancedDirtyCount');
        const applyBtn = $('#pwAdvancedApplyBtn');
        const resetBtn = $('#pwAdvancedResetBtn');
        if (dirtyEl) dirtyEl.hidden = count === 0;
        if (cntEl) cntEl.textContent = count;
        if (applyBtn) applyBtn.disabled = count === 0;
        if (resetBtn) resetBtn.disabled = count === 0;
    }

    /* ==========================================================================
       12. 智能优化弹窗
    ========================================================================== */
    function openSmartModal() {
        const cpu = getCpu();
        const fromPlan = getActivePlan();
        const fromSettings = fromPlan ? fromPlan.settings : {};
        const toSettings = buildAiRecommendedSettings(cpu);

        // Diff
        const allKeys = new Set([...Object.keys(fromSettings), ...Object.keys(toSettings)]);
        const baseDiff = [];
        const aiDiff = [];
        allKeys.forEach(k => {
            const a = fromSettings[k];
            const b = toSettings[k];
            if (a === b) return;
            (isAiKey(k) ? aiDiff : baseDiff).push({ key: k, from: a, to: b });
        });

        // 摘要
        const sub = $('#pwSmartSub');
        if (sub) sub.textContent = `基于 ${cpu.archShort} · 已为你生成最优配置`;
        const tags = $('#pwSmartTags');
        if (tags) {
            tags.innerHTML = `
                <span class="pw-smart-summary-tag"><i data-lucide="cpu"></i><span>${cpu.name}</span></span>
                <span class="pw-smart-summary-tag"><i data-lucide="layers"></i><span>${cpu.archShort}</span></span>
                ${cpu.injects.length ? `<span class="pw-smart-summary-tag"><i data-lucide="sparkles"></i><span>${cpu.injects.length} 项 AI 注入</span></span>` : ''}
            `;
        }
        // 默认计划名（避免重名）
        const nameInput = $('#pwSmartNameInput');
        if (nameInput) {
            const existingNames = state.plans.map(p => p.name);
            let baseName = `YuqiEngine - AI 智能优化`;
            let candidate = baseName;
            let n = 2;
            while (existingNames.includes(candidate)) {
                candidate = `${baseName} ${n++}`;
            }
            nameInput.value = candidate;
        }

        // Diff 列表
        const renderDiffRow = (d) => {
            const meta = PARAM_META[d.key] || { name: d.key };
            const fromText = d.from === undefined ? '—' : formatValue(d.key, d.from);
            const toText = d.to === undefined ? '—' : formatValue(d.key, d.to);
            return `<li class="pw-diff-row">
                <span class="pw-diff-row-name">${meta.name}</span>
                <span class="pw-diff-row-from">${fromText}</span>
                <i data-lucide="arrow-right" class="pw-diff-row-arrow"></i>
                <span class="pw-diff-row-to">${toText}</span>
            </li>`;
        };
        const baseList = $('#pwSmartBaseList');
        const aiList = $('#pwSmartAiList');
        if (baseList) baseList.innerHTML = baseDiff.map(renderDiffRow).join('') || '<li style="padding:8px;text-align:center;color:var(--text-tertiary);font-size:12px">基础参数无变更</li>';
        if (aiList) aiList.innerHTML = aiDiff.map(renderDiffRow).join('') || '<li style="padding:8px;text-align:center;color:var(--text-tertiary);font-size:12px">未注入 AI 专属参数（你的 CPU 不需要）</li>';
        const baseCntEl = $('#pwSmartBaseCount');
        const aiCntEl = $('#pwSmartAiCount');
        if (baseCntEl) baseCntEl.textContent = baseDiff.length;
        if (aiCntEl) aiCntEl.textContent = aiDiff.length;

        // 暂存到弹窗 dataset
        const modal = $('#pwSmartModal');
        if (modal) {
            modal._aiSettings = toSettings;
            modal.hidden = false;
            if (window.lucide) lucide.createIcons({ root: modal });
        }
        rootEl.dataset.state = 'smart-previewing';
    }

    function closeSmartModal() {
        const modal = $('#pwSmartModal');
        if (modal) modal.hidden = true;
        rootEl.dataset.state = 'idle';
    }

    /* --- 右侧参数抽屉 --- */
    function openParamDrawer() {
        const drawer = document.getElementById('pwParamDrawer');
        if (!drawer) return;
        // 强制 reflow 后再加 class 触发动画
        void drawer.offsetHeight;
        drawer.classList.add('is-open');
    }

    function closeParamDrawer() {
        const drawer = document.getElementById('pwParamDrawer');
        if (!drawer) return;
        drawer.classList.remove('is-open');
    }

    function confirmSmart() {
        const modal = $('#pwSmartModal');
        if (!modal || !modal._aiSettings) return;
        const nameInput = $('#pwSmartNameInput');
        let name = (nameInput && nameInput.value.trim()) || 'YuqiEngine - AI 智能优化';
        // 防重名
        const existing = state.plans.map(p => p.name);
        if (existing.includes(name)) {
            let n = 2;
            while (existing.includes(`${name} ${n}`)) n++;
            name = `${name} ${n}`;
        }
        const cpu = getCpu();
        const newPlan = {
            id: 'ai_' + Date.now().toString(36),
            name,
            sub: `AI 智能优化 · 基于 ${cpu.archShort}`,
            icon: 'sparkles',
            source: 'ai',
            readonly: false,
            settings: { ...modal._aiSettings }
        };
        state.plans.push(newPlan);
        closeSmartModal();
        runApplyFlow(newPlan, '正在创建 AI 智能优化计划');
    }

    /* ==========================================================================
       13. 应用流程（命令岛）
    ========================================================================== */
    function runApplyFlow(plan, title) {
        state.applying = true;
        rootEl.dataset.state = 'applying';

        const island = $('#pwCommandIsland');
        const titleEl = $('#pwIslandTitle');
        const stepEl = $('#pwIslandStep');
        const progEl = $('#pwIslandProgress');
        if (island) {
            island.hidden = false;
            if (titleEl) titleEl.textContent = title || `正在应用 · ${plan.name}`;
        }

        const steps = [
            { p: 18, t: '检查管理员权限…' },
            { p: 38, t: '备份当前激活计划…' },
            { p: 58, t: plan.source === 'ai' ? '创建 AI 智能优化计划…' : '切换电源计划…' },
            { p: 80, t: `注入 ${Object.keys(plan.settings).length} 项参数…` },
            { p: 96, t: '激活新计划…' },
            { p: 100, t: '应用完成 ✓' }
        ];

        let stepIdx = 0;
        const runStep = () => {
            if (stepIdx >= steps.length) {
                state.activePlanId = plan.id;
                state.advancedDirty = {};
                state.applying = false;
                if (island) island.hidden = true;
                rootEl.dataset.state = 'idle';
                renderActiveBar();
                renderPlanList();
                renderCurrentPanel();
                renderGroups();
                refreshDirtyUi();
                showToast(`已应用 · ${plan.name}`, 'success');
                return;
            }
            const s = steps[stepIdx];
            if (stepEl) stepEl.textContent = s.t;
            if (progEl) progEl.style.width = s.p + '%';
            stepIdx++;
            setTimeout(runStep, stepIdx === steps.length ? 380 : 280);
        };
        setTimeout(runStep, 240);
    }

    /* ==========================================================================
       14. 计划管理
    ========================================================================== */
    function activatePlan(planId) {
        const p = getPlan(planId);
        if (!p || p.id === state.activePlanId) return;
        // 若有未保存高级参数，先警告（这里采用直接覆盖方式，保持简洁）
        if (Object.keys(state.advancedDirty).length > 0) {
            showToast('已切换计划 · 未保存的高级参数修改已丢弃', 'warn');
            state.advancedDirty = {};
        }
        runApplyFlow(p, `正在切换至 · ${p.name}`);
    }

    function openRenameModal(planId) {
        const p = getPlan(planId);
        if (!p || p.readonly) return;
        state.editingPlanId = planId;
        const modal = $('#pwRenameModal');
        const input = $('#pwRenameInput');
        const sub = $('#pwRenameSub');
        if (sub) sub.textContent = `当前名称：${p.name}`;
        if (input) {
            input.value = p.name;
            setTimeout(() => input.select(), 50);
        }
        if (modal) modal.hidden = false;
    }
    function closeRenameModal() {
        state.editingPlanId = null;
        const modal = $('#pwRenameModal');
        if (modal) modal.hidden = true;
    }
    function confirmRename() {
        const p = getPlan(state.editingPlanId);
        if (!p) return closeRenameModal();
        const input = $('#pwRenameInput');
        const newName = input && input.value.trim();
        if (!newName) {
            showToast('名称不能为空', 'error');
            return;
        }
        if (state.plans.some(x => x.id !== p.id && x.name === newName)) {
            showToast('已存在同名计划', 'error');
            return;
        }
        p.name = newName;
        closeRenameModal();
        renderPlanList();
        renderActiveBar();
        renderCurrentPanel();
        showToast(`已重命名为 · ${newName}`, 'success');
    }

    function openDeleteModal(planId) {
        const p = getPlan(planId);
        if (!p || p.readonly) return;
        state.editingPlanId = planId;
        const modal = $('#pwDeleteModal');
        const text = $('#pwDeleteText');
        if (text) text.innerHTML = `即将删除「<strong>${p.name}</strong>」，确认操作？`;
        if (modal) modal.hidden = false;
    }
    function closeDeleteModal() {
        state.editingPlanId = null;
        const modal = $('#pwDeleteModal');
        if (modal) modal.hidden = true;
    }
    function confirmDelete() {
        const p = getPlan(state.editingPlanId);
        if (!p) return closeDeleteModal();
        const wasActive = p.id === state.activePlanId;
        state.plans = state.plans.filter(x => x.id !== p.id);
        if (wasActive) {
            // 切回 Windows 平衡
            state.activePlanId = 'win_balanced';
            state.advancedDirty = {};
        }
        closeDeleteModal();
        renderPlanList();
        renderActiveBar();
        renderCurrentPanel();
        renderGroups();
        refreshDirtyUi();
        showToast(`已删除 · ${p.name}`, 'success');
    }

    function createBlankPlan() {
        const cpu = getCpu();
        const baseSettings = { ...WIN_DEFAULT_PLANS[0].settings };  // 从平衡复制
        const existing = state.plans.map(p => p.name);
        let baseName = '自定义计划';
        let candidate = baseName;
        let n = 2;
        while (existing.includes(candidate)) candidate = `${baseName} ${n++}`;
        const newPlan = {
            id: 'custom_' + Date.now().toString(36),
            name: candidate,
            sub: `自定义 · 基于 ${cpu.archShort}`,
            icon: 'circle',
            source: 'custom',
            readonly: false,
            settings: baseSettings
        };
        state.plans.push(newPlan);
        renderPlanList();
        showToast(`已创建 · ${candidate}`, 'success');
    }

    /* ==========================================================================
       15. 高级参数编辑
    ========================================================================== */
    function onEditParam(key, rawVal) {
        const p = getActivePlan();
        if (!p || p.readonly) {
            showToast('Windows 默认计划不可编辑 · 请先切换到自定义/AI 计划', 'warn');
            return;
        }
        const meta = PARAM_META[key];
        if (!meta) return;
        let val;
        if (meta.type === 'enum') {
            val = parseInt(rawVal, 10);
            if (isNaN(val)) return;
        } else {
            val = parseInt(rawVal, 10);
            if (isNaN(val)) return;
            if (meta.min !== undefined) val = Math.max(meta.min, val);
            if (meta.max !== undefined) val = Math.min(meta.max, val);
        }
        const baseV = p.settings[key];
        if (val === baseV) {
            // 与基线相同，从 dirty 中移除
            delete state.advancedDirty[key];
        } else {
            state.advancedDirty[key] = val;
        }
        // 只更新该行的 dirty 标记（避免整体重渲导致 input 失焦）
        const row = rootEl.querySelector(`.pw-param-row--editable [data-key="${key}"]`);
        if (row) {
            const rowEl = row.closest('.pw-param-row--editable');
            if (rowEl) rowEl.dataset.dirty = state.advancedDirty.hasOwnProperty(key) ? 'true' : 'false';
        }
        refreshDirtyUi();
    }

    function applyAdvanced() {
        const p = getActivePlan();
        if (!p || p.readonly) return;
        const dirtyKeys = Object.keys(state.advancedDirty);
        if (dirtyKeys.length === 0) return;
        dirtyKeys.forEach(k => { p.settings[k] = state.advancedDirty[k]; });
        state.advancedDirty = {};
        renderCurrentPanel();
        renderGroups();
        refreshDirtyUi();
        showToast(`已应用 · ${dirtyKeys.length} 项参数变更`, 'success');
    }

    function resetAdvanced() {
        if (Object.keys(state.advancedDirty).length === 0) return;
        state.advancedDirty = {};
        renderGroups();
        refreshDirtyUi();
        showToast('已重置未保存的修改', 'success');
    }

    /* ==========================================================================
       16. Toast
    ========================================================================== */
    let toastTimer = null;
    function showToast(text, type = 'success') {
        const toast = $('#pwToast');
        const txt = $('#pwToastText');
        const icon = $('#pwToastIcon');
        if (!toast) return;
        toast.dataset.type = type;
        if (txt) txt.textContent = text;
        if (icon) {
            const iconName = type === 'error' ? 'x-circle' : type === 'warn' ? 'alert-triangle' : 'check-circle-2';
            icon.setAttribute('data-lucide', iconName);
            if (window.lucide) lucide.createIcons({ root: toast });
        }
        toast.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => { toast.hidden = true; }, 2400);
    }

    /* ==========================================================================
       17. CPU 切换器
    ========================================================================== */
    function toggleCpuSwitcher(forceShow) {
        const sw = $('#pwCpuSwitcher');
        if (!sw) return;
        if (forceShow === true) sw.hidden = false;
        else if (forceShow === false) sw.hidden = true;
        else sw.hidden = !sw.hidden;
    }

    function switchCpu(newCpuId) {
        if (!CPU_PROFILES[newCpuId] || newCpuId === state.currentCpuId) return;
        state.currentCpuId = newCpuId;
        // 清理之前生成的 AI 计划（保留 Windows 默认 + 自定义）
        state.plans = state.plans.filter(p => p.source !== 'ai');
        if (!getPlan(state.activePlanId)) state.activePlanId = 'win_balanced';
        state.advancedDirty = {};
        renderCpuPanel();
        renderSmartHero();
        renderActiveBar();
        renderPlanList();
        renderCurrentPanel();
        renderGroups();
        refreshDirtyUi();
        showToast(`已切换到 · ${CPU_PROFILES[newCpuId].name}（演示）`, 'success');
    }

    /* ==========================================================================
       18. 事件代理
    ========================================================================== */
    function bindEvents() {
        const handleClick = (e) => {
            const t = e.target;
            const actionEl = t.closest('[data-action]');
            const cpuCard = t.closest('.pw-switcher-card[data-cpu-id]');

            if (cpuCard) {
                switchCpu(cpuCard.dataset.cpuId);
                return;
            }
            if (!actionEl) return;

            const action = actionEl.dataset.action;
            switch (action) {
                case 'toggle-cpu-switcher': toggleCpuSwitcher(); break;
                case 'close-cpu-switcher':  toggleCpuSwitcher(false); break;
            case 'refresh-plans': showToast('已刷新电源计划列表（演示）', 'success'); break;
            case 'open-param-drawer': openParamDrawer(); break;
            case 'close-param-drawer': closeParamDrawer(); break;

                case 'open-smart-modal': openSmartModal(); break;
                case 'close-smart': closeSmartModal(); break;
                case 'confirm-smart': confirmSmart(); break;

                case 'activate-plan': activatePlan(actionEl.dataset.planId); break;
                case 'rename-plan': openRenameModal(actionEl.dataset.planId); break;
                case 'delete-plan': openDeleteModal(actionEl.dataset.planId); break;
                case 'create-plan': createBlankPlan(); break;

                case 'close-rename': closeRenameModal(); break;
                case 'confirm-rename': confirmRename(); break;
                case 'close-delete': closeDeleteModal(); break;
                case 'confirm-delete': confirmDelete(); break;

                case 'apply-advanced': applyAdvanced(); break;
                case 'reset-advanced': resetAdvanced(); break;

                case 'toggle-all-groups': {
                    const groups = $$('details.pw-group');
                    const anyOpen = groups.some(g => g.open);
                    groups.forEach(g => g.open = !anyOpen);
                    const span = actionEl.querySelector('span');
                    if (span) span.textContent = anyOpen ? '全部展开' : '全部折叠';
                    break;
                }
            }
        };

        const handleChange = (e) => {
            const t = e.target;
            if (t && t.dataset && t.dataset.action === 'edit-param') {
                onEditParam(t.dataset.key, t.value);
            }
        };
        const handleInput = (e) => {
            const t = e.target;
            if (t && t.dataset && t.dataset.action === 'edit-param' && t.tagName === 'INPUT') {
                onEditParam(t.dataset.key, t.value);
            }
        };
        // 重命名/智能优化弹窗里的回车确认
        const handleKeyForInputs = (e) => {
            if (e.key !== 'Enter') return;
            const t = e.target;
            if (!t) return;
            if (t.id === 'pwRenameInput') { e.preventDefault(); confirmRename(); }
            else if (t.id === 'pwSmartNameInput') { e.preventDefault(); confirmSmart(); }
        };

        rootEl.addEventListener('click', handleClick);
        rootEl.addEventListener('change', handleChange);
        rootEl.addEventListener('input', handleInput);

        // 浮层 portal 到 body 后单独绑定
        ['pwSmartModal', 'pwRenameModal', 'pwDeleteModal', 'pwCommandIsland', 'pwParamDrawer'].forEach(id => {
            const el = document.getElementById(id);
            if (el) {
                el.addEventListener('click', handleClick);
                el.addEventListener('change', handleChange);
                el.addEventListener('input', handleInput);
                el.addEventListener('keydown', handleKeyForInputs);
            }
        });

        escHandler = (e) => {
            if (e.key !== 'Escape') return;
            const drawer = document.getElementById('pwParamDrawer');
            const smart = $('#pwSmartModal');
            const rename = $('#pwRenameModal');
            const del = $('#pwDeleteModal');
            const switcher = $('#pwCpuSwitcher');
            if (drawer && drawer.classList.contains('is-open')) closeParamDrawer();
            else if (smart && !smart.hidden) closeSmartModal();
            else if (rename && !rename.hidden) closeRenameModal();
            else if (del && !del.hidden) closeDeleteModal();
            else if (switcher && !switcher.hidden) toggleCpuSwitcher(false);
        };
        document.addEventListener('keydown', escHandler);
    }

    /* ==========================================================================
       19. 初始化
    ========================================================================== */
    function portalFloatingLayers(root) {
        portaledNodes = [];
        const ids = ['pwSmartModal', 'pwRenameModal', 'pwDeleteModal', 'pwCommandIsland', 'pwToast', 'pwParamDrawer'];
        ids.forEach(id => {
            const el = root.querySelector('#' + id);
            if (el && el.parentNode !== document.body) {
                document.body.appendChild(el);
                portaledNodes.push(el);
            }
        });
    }

    function removePortaledLayers() {
        portaledNodes.forEach(el => {
            if (el && el.parentNode) el.parentNode.removeChild(el);
        });
        portaledNodes = [];
    }

    function initPlans() {
        // 初始：3 个 Windows 默认 + 1 个示例自定义
        state.plans = WIN_DEFAULT_PLANS.map(p => ({ ...p, settings: { ...p.settings } }));
        state.plans.push({
            id: 'custom_demo',
            name: '我的办公模式',
            sub: '自定义 · 笔记本日常使用',
            icon: 'briefcase',
            source: 'custom',
            readonly: false,
            settings: {
                proc_min_state: 10, proc_max_state: 90, boost_mode: 1,
                core_parking_min: 30, core_parking_max: 100,
                perf_increase_threshold: 15, perf_decrease_threshold: 10,
                idle_demote_threshold: 30, idle_promote_threshold: 50,
                perf_time_check: 30,
                pcie_aspm: 1, usb_selective_suspend: 1, usb_link_power: 1,
                network_power_saving: 1, cooling_policy: 1,
                disk_turn_off: 15, display_turn_off: 8, sleep_after: 20, hibernate_after: 60
            }
        });
        state.activePlanId = 'win_balanced';
        state.advancedDirty = {};
    }

    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_INIT__['power-plan-editor'] = function (root) {
        rootEl = root.querySelector('.pw-page') || root;
        if (!rootEl) return;

        portalFloatingLayers(root);
        initPlans();

        renderCpuPanel();
        renderSmartHero();
        renderActiveBar();
        renderPlanList();
        renderCurrentPanel();
        renderGroups();
        refreshDirtyUi();
        bindEvents();



        if (window.lucide) lucide.createIcons({ root: document.body });
    };

    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_DESTROY__['power-plan-editor'] = function () {
        if (escHandler) {
            document.removeEventListener('keydown', escHandler);
            escHandler = null;
        }
        clearTimeout(toastTimer);
        removePortaledLayers();
        rootEl = null;
    };
})();
