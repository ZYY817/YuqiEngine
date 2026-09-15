/* ==========================================================================
   CPU 核心调度 · The Console
   --------------------------------------------------------------------------
   职责：
     · 渲染 P/E 核棋盘（含 HT 副线程、SMT 拓扑微图）
     · 渲染进程列表（搜索 / 过滤 / 单选）
     · 进程 ↔ 棋盘双向联动；亲和性写入到进程
     · 应用 / 重置 / 快速预设 / 保存预设 / 推荐预设抽屉
     · Toast 反馈 + 顶部 CPU 占用伪刷新

   自注册到 window.__VIEW_INIT__['cpu-core-management']
   ========================================================================== */
(function () {
    'use strict';

    const VIEW_NAME = 'cpu-core-management';

    /* ---------------- Mock CPU 拓扑（i7-14700K 风） ---------------- */
    /* 规则：8 个 P 物理核（每核 2 SMT 线程） + 12 个 E 物理核（每核 1 线程） = 28 逻辑核 */
    function buildTopology() {
        const cores = [];
        let logical = 0;

        // P-Core × 8（每核 2 线程：主线程 + HT 副线程）
        for (let p = 0; p < 8; p++) {
            const pair = [];
            for (let t = 0; t < 2; t++) {
                cores.push({
                    cpuIndex: logical,
                    type: 'p',
                    physical: p,
                    isHT: t === 1,
                    label: `P${p}` + (t === 1 ? 'ʹ' : ''),
                    load: 18 + Math.floor(Math.random() * 60)
                });
                pair.push(logical);
                logical++;
            }
        }

        // E-Core × 12（每核 1 线程，无 HT）
        for (let e = 0; e < 12; e++) {
            cores.push({
                cpuIndex: logical,
                type: 'e',
                physical: 8 + e,
                isHT: false,
                label: `E${e}`,
                load: 6 + Math.floor(Math.random() * 30)
            });
            logical++;
        }

        return cores;
    }

    /* ---------------- Mock 进程列表 ---------------- */
    function buildProcesses() {
        return [
            { name: 'YuqiEngine.exe',  pid: 4726, cpu: 0.4,  mem: 142, kind: 'system',  tag: '本软件',     foreground: true,  game: false, custom: false },
            { name: 'valorant.exe',    pid: 8812, cpu: 22.7, mem: 1864, kind: 'game',    tag: 'Riot · 游戏', foreground: true,  game: true,  custom: false },
            { name: 'chrome.exe',      pid: 5210, cpu: 14.2, mem: 2210, kind: 'browser', tag: 'Chrome',     foreground: true,  game: false, custom: false },
            { name: 'WeChat.exe',      pid: 3104, cpu: 1.8,  mem: 386,  kind: 'system',  tag: '即时通讯',    foreground: false, game: false, custom: false },
            { name: 'obs64.exe',       pid: 9871, cpu: 8.4,  mem: 612,  kind: 'media',   tag: 'OBS · 直播',  foreground: false, game: false, custom: true,  affNote: 'P0–P3' },
            { name: 'Code.exe',        pid: 6633, cpu: 3.2,  mem: 920,  kind: 'system',  tag: 'VS Code',    foreground: true,  game: false, custom: false },
            { name: 'steam.exe',       pid: 7421, cpu: 0.6,  mem: 220,  kind: 'game',    tag: 'Steam',      foreground: false, game: true,  custom: false },
            { name: 'devenv.exe',      pid: 5512, cpu: 4.8,  mem: 1450, kind: 'system',  tag: 'VS 2022',    foreground: false, game: false, custom: false },
            { name: 'spotify.exe',     pid: 4083, cpu: 1.1,  mem: 184,  kind: 'media',   tag: 'Spotify',    foreground: false, game: false, custom: true,  affNote: 'E0–E11' },
            { name: 'SystemSettings.exe', pid: 1812, cpu: 0.2, mem: 64, kind: 'system',  tag: 'Windows',    foreground: false, game: false, custom: false },
            { name: 'explorer.exe',    pid: 1208, cpu: 0.9,  mem: 288,  kind: 'system',  tag: '资源管理器',  foreground: false, game: false, custom: false },
            { name: 'Discord.exe',     pid: 7702, cpu: 1.6,  mem: 412,  kind: 'media',   tag: 'Discord',    foreground: false, game: false, custom: false },
            { name: 'NVIDIA Broadcast', pid: 9241, cpu: 5.3, mem: 524,  kind: 'media',   tag: 'NVIDIA',     foreground: false, game: false, custom: false },
            { name: 'csgo.exe',        pid: 8104, cpu: 0.0,  mem: 0,    kind: 'game',    tag: 'CS · 已挂起', foreground: false, game: true,  custom: false, suspended: true },
            { name: 'svchost.exe',     pid: 988,  cpu: 2.1,  mem: 156,  kind: 'system',  tag: '系统服务',    foreground: false, game: false, custom: false }
        ];
    }

    /* ---------------- 模块状态 ---------------- */
    const state = {
        cores: [],
        processes: [],
        currentFilter: 'all',
        currentKeyword: '',
        selectedPid: null,
        selectedSet: new Set(),       // 当前棋盘上勾选的 cpuIndex（亲和性）
        currentPreferred: null,       // 当前首选 cpuIndex（最多 1 个，必须在 selectedSet 中）
        affinityMap: new Map(),       // pid → Set<cpuIndex>（已应用的亲和性）
        preferredMap: new Map(),      // pid → cpuIndex（已应用的首选核心）
        recommendedIdx: null,         // 推荐的最强核（一般是 P0 主线程）
        loadTimer: null
    };

    /* 获取某个 cpuIndex 对应的核心信息 */
    function getCore(idx) { return state.cores.find(c => c.cpuIndex === idx); }

    /* 获取首选核的展示名（如 P0 / E3） */
    function preferredLabel(idx) {
        const c = getCore(idx);
        if (!c) return '—';
        return c.label;
    }

    /* ---------------- DOM 引用 ---------------- */
    let root = null;

    /* ---------------- 工具函数 ---------------- */
    function $(sel) { return root.querySelector(sel); }
    function $$(sel) { return Array.from(root.querySelectorAll(sel)); }

    function iconHTML(name, cls) {
        return `<i data-lucide="${name}"${cls ? ` class="${cls}"` : ''}></i>`;
    }

    function refreshIcons(container) {
        if (window.lucide) lucide.createIcons({ root: container || root });
    }

    /* 进程图标映射 */
    function procIconKind(p) {
        if (p.kind === 'game')    return { icon: 'gamepad-2', cls: 'cpu-proc-icon--game' };
        if (p.kind === 'browser') return { icon: 'globe',     cls: 'cpu-proc-icon--browser' };
        if (p.kind === 'media')   return { icon: 'video',     cls: 'cpu-proc-icon--media' };
        return { icon: 'box', cls: 'cpu-proc-icon--system' };
    }

    /* 把已选 cpuIndex 集合压成可读的简短摘要：P0-P3 / E0-E5 / 全部 / 12/28 */
    function summarizeAffinity(set, cores) {
        if (!set || set.size === 0) return '未指定';
        if (set.size === cores.length) return '全部核心';

        // 按 P 主线程顺序与 E 核顺序压缩区间
        const pIdx = [], eIdx = [];
        cores.forEach(c => {
            if (!set.has(c.cpuIndex)) return;
            if (c.type === 'p' && !c.isHT) pIdx.push(c.physical);
            else if (c.type === 'e') eIdx.push(c.physical - 8);
        });

        const compress = (arr, prefix) => {
            if (arr.length === 0) return null;
            arr.sort((a, b) => a - b);
            const ranges = [];
            let s = arr[0], p = arr[0];
            for (let i = 1; i < arr.length; i++) {
                if (arr[i] === p + 1) { p = arr[i]; continue; }
                ranges.push(s === p ? `${prefix}${s}` : `${prefix}${s}-${prefix}${p}`);
                s = p = arr[i];
            }
            ranges.push(s === p ? `${prefix}${s}` : `${prefix}${s}-${prefix}${p}`);
            return ranges.join(',');
        };

        const parts = [];
        const pStr = compress(pIdx, 'P');
        const eStr = compress(eIdx, 'E');
        if (pStr) parts.push(pStr);
        if (eStr) parts.push(eStr);
        return parts.join(' · ') || `${set.size}/${cores.length}`;
    }

    /* ============================================================
       渲染：核心矩阵
       ============================================================ */
    function renderCoreMatrix() {
        const matrix = $('#coreMatrix');
        matrix.innerHTML = '';

        state.cores.forEach(c => {
            const cell = document.createElement('button');
            cell.type = 'button';
            cell.className = 'cpu-cell';
            cell.dataset.idx = String(c.cpuIndex);
            cell.dataset.type = c.type;
            cell.dataset.ht = String(c.isHT);
            cell.disabled = state.selectedPid === null;

            const tagText = c.type === 'p' ? (c.isHT ? 'HT' : 'P') : 'E';
            const isRecommended = c.cpuIndex === state.recommendedIdx;

            cell.innerHTML = `
                ${isRecommended ? '<span class="cpu-cell-recommend">推荐</span>' : ''}
                <button type="button" class="cpu-cell-star" data-star="${c.cpuIndex}" title="设为首选核心 · 帧数玄学党专用" tabindex="-1">
                    <i data-lucide="star"></i>
                </button>
                <span class="cpu-cell-id">${c.label}</span>
                <span class="cpu-cell-bottom">
                    <span class="cpu-cell-load">${c.load}%</span>
                    <span class="cpu-cell-tag">${tagText}</span>
                </span>
            `;

            // 左键：切换亲和性（star 按钮除外）
            cell.addEventListener('click', (e) => {
                if (e.target.closest('.cpu-cell-star')) return;
                onCellToggle(c.cpuIndex);
            });
            // 右键：快捷设为首选
            cell.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                onPreferredToggle(c.cpuIndex);
            });

            matrix.appendChild(cell);
        });

        // 星标点击事件（事件委托）
        matrix.querySelectorAll('.cpu-cell-star').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                onPreferredToggle(+btn.dataset.star);
            });
        });

        renderTopology();
    }

    /* SMT 拓扑微图 */
    function renderTopology() {
        const wrap = $('#cpuTopology');
        wrap.innerHTML = '';

        // 先渲 P 物理核（每个 2 线程）
        const pPhysicals = {};
        const ePhysicals = {};
        state.cores.forEach(c => {
            const map = c.type === 'p' ? pPhysicals : ePhysicals;
            if (!map[c.physical]) map[c.physical] = [];
            map[c.physical].push(c);
        });

        Object.keys(pPhysicals).sort((a, b) => +a - +b).forEach(p => {
            const group = document.createElement('div');
            group.className = 'cpu-topo-group cpu-topo-group--p';
            const dots = pPhysicals[p].map(c => {
                const isOn = state.selectedSet.has(c.cpuIndex);
                return `<span class="cpu-topo-pair-dot ${isOn ? 'is-on' : ''} ${c.isHT ? 'is-ht' : ''}" title="${c.label} (CPU${c.cpuIndex})">${c.isHT ? '·' : '●'}</span>`;
            }).join('');
            group.innerHTML = `
                <span class="cpu-topo-group-label">P${p}</span>
                <span class="cpu-topo-group-pair">${dots}</span>
            `;
            wrap.appendChild(group);
        });

        Object.keys(ePhysicals).sort((a, b) => +a - +b).forEach(p => {
            const group = document.createElement('div');
            group.className = 'cpu-topo-group cpu-topo-group--e';
            const dots = ePhysicals[p].map(c => {
                const isOn = state.selectedSet.has(c.cpuIndex);
                return `<span class="cpu-topo-pair-dot ${isOn ? 'is-on' : ''}" title="${c.label} (CPU${c.cpuIndex})">●</span>`;
            }).join('');
            const eIdx = +p - 8;
            group.innerHTML = `
                <span class="cpu-topo-group-label">E${eIdx}</span>
                <span class="cpu-topo-group-pair">${dots}</span>
            `;
            wrap.appendChild(group);
        });
    }

    /* ============================================================
       渲染：进程列表
       ============================================================ */
    function renderProcessList() {
        const list = $('#procList');
        list.innerHTML = '';

        const filtered = state.processes.filter(p => {
            if (state.currentFilter === 'foreground' && !p.foreground) return false;
            if (state.currentFilter === 'game'       && !p.game)        return false;
            if (state.currentFilter === 'high-cpu'   && p.cpu < 5)      return false;
            if (state.currentFilter === 'custom'     && !state.affinityMap.has(p.pid) && !p.custom) return false;
            const kw = state.currentKeyword.trim().toLowerCase();
            if (kw && !p.name.toLowerCase().includes(kw) && !String(p.pid).includes(kw)) return false;
            return true;
        });

        $('#procCount').textContent = String(filtered.length);

        if (filtered.length === 0) {
            list.innerHTML = `
                <div class="cpu-proclist-empty">
                    ${iconHTML('search-x')}
                    <div>没有匹配的进程</div>
                    <div style="font-size:11px;margin-top:4px;opacity:.7">尝试更换关键词或切换过滤标签</div>
                </div>
            `;
            refreshIcons(list);
            return;
        }

        filtered.forEach(p => {
            const li = document.createElement('li');
            li.className = 'cpu-proc';
            li.dataset.pid = String(p.pid);
            if (state.selectedPid === p.pid) li.classList.add('is-selected');

            const ic = procIconKind(p);

            // 亲和性摘要
            const setForP = state.affinityMap.get(p.pid);
            let affHtml;
            if (setForP) {
                affHtml = `<span class="cpu-proc-aff is-custom" title="${setForP.size} / ${state.cores.length}">${summarizeAffinity(setForP, state.cores)}</span>`;
            } else if (p.custom && p.affNote) {
                // 模拟"已被外部工具绑定"的展示
                affHtml = `<span class="cpu-proc-aff is-custom" title="外部已设定">${p.affNote}</span>`;
            } else {
                affHtml = `<span class="cpu-proc-aff is-default">默认</span>`;
            }

            const cpuCls = p.cpu >= 20 ? 'is-hot' : (p.cpu >= 8 ? 'is-warm' : '');

            // 进程行末尾 · 首选核小星
            const hasPreferred = state.preferredMap.has(p.pid);
            const starHtml = hasPreferred
                ? `<i data-lucide="star" class="cpu-proc-aff-star" title="首选 ${preferredLabel(state.preferredMap.get(p.pid))}"></i>`
                : '';

            li.innerHTML = `
                <span class="cpu-proc-name-wrap">
                    <span class="cpu-proc-icon ${ic.cls}">${iconHTML(ic.icon)}</span>
                    <span class="cpu-proc-name-text">
                        <span class="cpu-proc-name">${p.name}${starHtml}</span>
                        <span class="cpu-proc-tag">${p.tag}</span>
                    </span>
                </span>
                <span class="cpu-proc-pid">${p.pid}</span>
                <span class="cpu-proc-cpu ${cpuCls}">${p.cpu.toFixed(1)}%</span>
                <span class="cpu-proc-mem">${p.mem} MB</span>
                ${affHtml}
            `;
            li.addEventListener('click', () => onProcessSelect(p.pid));
            list.appendChild(li);
        });

        refreshIcons(list);
    }

    /* ============================================================
       行为：进程选择
       ============================================================ */
    function onProcessSelect(pid) {
        state.selectedPid = pid;
        const proc = state.processes.find(p => p.pid === pid);
        if (!proc) return;

        // 已应用的亲和性 → 棋盘恢复；否则默认全选
        const stored = state.affinityMap.get(pid);
        state.selectedSet = stored ? new Set(stored) : new Set(state.cores.map(c => c.cpuIndex));
        // 首选核同步恢复
        state.currentPreferred = state.preferredMap.has(pid) ? state.preferredMap.get(pid) : null;

        // 顶部目标信息
        const ic = procIconKind(proc);
        const targetWrap = $('#targetIconWrap');
        targetWrap.classList.add('is-active');
        targetWrap.innerHTML = iconHTML(ic.icon, 'cpu-target-icon');
        $('#targetName').textContent = proc.name;
        $('#targetSub').textContent  = `PID ${proc.pid} · ${proc.tag} · CPU ${proc.cpu.toFixed(1)}%`;

        // 启用预设按钮、动作按钮
        $$('.cpu-preset-btn').forEach(b => b.disabled = false);
        $('[data-action="cpu-reset"]').disabled = false;
        $('[data-action="cpu-save-preset"]').disabled = false;
        $('#cpuApplyBtn').disabled = false;

        renderProcessList();
        renderCoreMatrix();
        applyCellSelectionToDOM();
        updateActionSummary();
        refreshIcons(targetWrap);
    }

    /* ============================================================
       行为：核心格切换
       ============================================================ */
    function onCellToggle(idx) {
        if (state.selectedPid === null) return;
        if (state.selectedSet.has(idx)) {
            // 至少保留 1 个核
            if (state.selectedSet.size <= 1) {
                showToast('warn', '至少保留 1 个核心', '亲和性不能为空，否则进程将无法运行');
                return;
            }
            state.selectedSet.delete(idx);
            // 取消勾选的是首选核 → 同步清空首选
            if (state.currentPreferred === idx) {
                state.currentPreferred = null;
                showToast('info', '首选核心已同步取消', `${getCore(idx).label} 已不在亲和集内`);
            }
        } else {
            state.selectedSet.add(idx);
        }
        applyCellSelectionToDOM();
        renderTopology();
        updateActionSummary();
    }

    function applyCellSelectionToDOM() {
        $$('.cpu-cell').forEach(cell => {
            const idx = +cell.dataset.idx;
            cell.classList.toggle('is-on', state.selectedSet.has(idx));
            cell.classList.toggle('is-preferred', state.currentPreferred === idx);
            cell.disabled = state.selectedPid === null;
        });
        renderPreferredBadge();
    }

    /* ============================================================
       行为：首选核心切换
       ============================================================ */
    function onPreferredToggle(idx) {
        if (state.selectedPid === null) return;
        const core = getCore(idx);
        if (!core) return;

        // 取消首选（点同一颗）
        if (state.currentPreferred === idx) {
            state.currentPreferred = null;
            applyCellSelectionToDOM();
            updateActionSummary();
            showToast('info', '已取消首选核心', '交回系统在亲和集内自由调度');
            return;
        }

        // 首选必须在亲和集中；不在则自动勾进并提示
        let autoAdded = false;
        if (!state.selectedSet.has(idx)) {
            state.selectedSet.add(idx);
            autoAdded = true;
        }

        state.currentPreferred = idx;
        applyCellSelectionToDOM();
        renderTopology();
        updateActionSummary();

        // 金色光环脚本
        const cell = $(`.cpu-cell[data-idx="${idx}"]`);
        if (cell) {
            cell.classList.add('is-halo');
            setTimeout(() => cell.classList.remove('is-halo'), 720);
        }

        const tip = autoAdded
            ? `已同时勾进亲和集 · 它必须被允许运行才能被优先调度`
            : `主线程优先调度到 ${core.label} · 其他核心仕为备选`;
        showToast('success', `首选核心 → ${core.label}`, tip);
    }

    function onClearPreferred() {
        if (state.currentPreferred === null) return;
        state.currentPreferred = null;
        applyCellSelectionToDOM();
        updateActionSummary();
        showToast('info', '已清除首选核心', '');
    }

    /* 顶部目标条首选徐者同步 */
    function renderPreferredBadge() {
        const wrap = $('#targetPreferred');
        const lbl  = $('#targetPreferredLabel');
        if (!wrap || !lbl) return;
        if (state.selectedPid !== null && state.currentPreferred !== null) {
            lbl.textContent = preferredLabel(state.currentPreferred);
            wrap.hidden = false;
        } else {
            wrap.hidden = true;
        }
        refreshIcons(wrap);
    }

    /* ============================================================
       行为：快速预设（页内栏）
       ============================================================ */
    function onQuickPreset(kind) {
        if (state.selectedPid === null) return;
        const next = new Set();
        if (kind === 'all') {
            state.cores.forEach(c => next.add(c.cpuIndex));
        } else if (kind === 'p-only') {
            state.cores.forEach(c => { if (c.type === 'p') next.add(c.cpuIndex); });
        } else if (kind === 'e-only') {
            state.cores.forEach(c => { if (c.type === 'e') next.add(c.cpuIndex); });
        } else if (kind === 'invert') {
            state.cores.forEach(c => { if (!state.selectedSet.has(c.cpuIndex)) next.add(c.cpuIndex); });
            // 防止反选成空集
            if (next.size === 0) {
                showToast('warn', '反选结果为空', '已忽略本次操作');
                return;
            }
        }
        state.selectedSet = next;
        applyCellSelectionToDOM();
        renderTopology();
        updateActionSummary();
    }

    /* ============================================================
       行为：应用 / 重置 / 保存预设
       ============================================================ */
    function onApply() {
        if (state.selectedPid === null) return;
        state.affinityMap.set(state.selectedPid, new Set(state.selectedSet));

        // 首选核心同步入库
        if (state.currentPreferred !== null) {
            state.preferredMap.set(state.selectedPid, state.currentPreferred);
        } else {
            state.preferredMap.delete(state.selectedPid);
        }

        // 应用脉冲：依次错峰（仅对已选核心）
        const onCells = $$('.cpu-cell.is-on');
        onCells.forEach((cell, i) => {
            setTimeout(() => {
                cell.classList.add('is-pulsing');
                setTimeout(() => cell.classList.remove('is-pulsing'), 320);
            }, i * 28);
        });

        // 首选核心 · 金色光环补一脚
        if (state.currentPreferred !== null) {
            const starCell = $(`.cpu-cell[data-idx="${state.currentPreferred}"]`);
            if (starCell) {
                setTimeout(() => {
                    starCell.classList.add('is-halo');
                    setTimeout(() => starCell.classList.remove('is-halo'), 720);
                }, onCells.length * 28 + 80);
            }
        }

        const proc = state.processes.find(p => p.pid === state.selectedPid);
        const summary = summarizeAffinity(state.selectedSet, state.cores);
        const prefStr = state.currentPreferred !== null
            ? ` · 首选 ${preferredLabel(state.currentPreferred)}⭐`
            : '';
        showToast('success', `已应用到 ${proc.name}`,
            `亲和性：${summary}（${state.selectedSet.size}/${state.cores.length} 核）${prefStr}`);

        renderProcessList();
        updateActionSummary();
    }

    function onReset() {
        if (state.selectedPid === null) return;
        state.affinityMap.delete(state.selectedPid);
        state.preferredMap.delete(state.selectedPid);
        state.selectedSet = new Set(state.cores.map(c => c.cpuIndex));
        state.currentPreferred = null;
        applyCellSelectionToDOM();
        renderTopology();
        renderProcessList();
        updateActionSummary();

        const proc = state.processes.find(p => p.pid === state.selectedPid);
        showToast('info', `已恢复默认调度`, `${proc.name} 现由 Windows 自动调度（首选也已清空）`);
    }

    function onSavePreset() {
        if (state.selectedPid === null) return;
        const proc = state.processes.find(p => p.pid === state.selectedPid);
        showToast('success', '预设已保存', `已为 ${proc.name} 创建专属预设（${state.selectedSet.size} 核）`);
    }

    /* ============================================================
       推荐预设抽屉
       ============================================================ */
    const RECIPES = {
        game:        { kind: 'p-only',     setPreferred: 'recommended' },  // 仅 P 核 + 首选最强核
        stream:      { kind: 'p-range', from: 0, to: 3 },
        background:  { kind: 'e-only' },
        'single-best': { kind: 'preferred-only', setPreferred: 'recommended' }, // 单核独占
        default:     { kind: 'all',        clearPreferred: true }
    };

    function applyRecipe(name) {
        if (state.selectedPid === null) {
            showToast('warn', '请先选择进程', '从左侧列表点击一个进程后再应用预设');
            return;
        }
        const recipe = RECIPES[name];
        if (!recipe) return;

        const next = new Set();
        if (recipe.kind === 'p-only') {
            state.cores.forEach(c => { if (c.type === 'p') next.add(c.cpuIndex); });
        } else if (recipe.kind === 'e-only') {
            state.cores.forEach(c => { if (c.type === 'e') next.add(c.cpuIndex); });
        } else if (recipe.kind === 'all') {
            state.cores.forEach(c => next.add(c.cpuIndex));
        } else if (recipe.kind === 'p-range') {
            state.cores.forEach(c => {
                if (c.type === 'p' && c.physical >= recipe.from && c.physical <= recipe.to) {
                    next.add(c.cpuIndex);
                }
            });
        } else if (recipe.kind === 'preferred-only') {
            // 单核独占：只选中推荐核
            if (state.recommendedIdx !== null) next.add(state.recommendedIdx);
        }

        state.selectedSet = next;

        // 预设联动首选核
        if (recipe.setPreferred === 'recommended' && state.recommendedIdx !== null) {
            state.currentPreferred = state.recommendedIdx;
        } else if (recipe.clearPreferred) {
            state.currentPreferred = null;
        }
        // 首选不在亲和集中了→清空
        if (state.currentPreferred !== null && !state.selectedSet.has(state.currentPreferred)) {
            state.currentPreferred = null;
        }

        applyCellSelectionToDOM();
        renderTopology();
        updateActionSummary();
        closePresetDrawer();

        // 直接连带应用
        onApply();
    }

    function openPresetDrawer() {
        $('#cpuPresetDrawer').classList.add('is-open');
        $('#cpuPresetDrawer').setAttribute('aria-hidden', 'false');
        $('#cpuPresetMask').classList.add('is-open');
    }
    function closePresetDrawer() {
        $('#cpuPresetDrawer').classList.remove('is-open');
        $('#cpuPresetDrawer').setAttribute('aria-hidden', 'true');
        $('#cpuPresetMask').classList.remove('is-open');
    }

    /* ============================================================
       底部摘要文字
       ============================================================ */
    function updateActionSummary() {
        const summaryEl = $('#actionSummary');
        const wrap = summaryEl.parentElement;
        if (state.selectedPid === null) {
            summaryEl.innerHTML = `未选择进程 · 0 / ${state.cores.length} 核已勾选`;
            wrap.classList.remove('is-ready');
            renderPreferredBadge();
            return;
        }
        const proc = state.processes.find(p => p.pid === state.selectedPid);
        const sum = summarizeAffinity(state.selectedSet, state.cores);
        const prefHtml = state.currentPreferred !== null
            ? `<span class="cpu-summary-preferred"><i data-lucide="star"></i>首选 ${preferredLabel(state.currentPreferred)}</span>`
            : '';
        summaryEl.innerHTML = `已选择 <strong>${proc.name}</strong> · <strong>${state.selectedSet.size}</strong> / ${state.cores.length} 核 · ${sum}${prefHtml}`;
        wrap.classList.add('is-ready');
        refreshIcons(summaryEl);
        renderPreferredBadge();
    }

    /* ============================================================
       Toast
       ============================================================ */
    function showToast(type, title, sub) {
        const stack = $('#cpuToastStack');
        const t = document.createElement('div');
        t.className = `cpu-toast cpu-toast--${type}`;
        const ic = type === 'success' ? 'check-circle-2'
                 : type === 'warn'    ? 'alert-triangle'
                 : 'info';
        t.innerHTML = `
            <i data-lucide="${ic}" class="cpu-toast-icon"></i>
            <div class="cpu-toast-text">${title}${sub ? `<small>${sub}</small>` : ''}</div>
        `;
        stack.appendChild(t);
        refreshIcons(t);
        requestAnimationFrame(() => t.classList.add('is-in'));
        setTimeout(() => {
            t.classList.remove('is-in');
            setTimeout(() => t.remove(), 240);
        }, 2600);
    }

    /* ============================================================
       顶部 CPU 占用 · 伪刷新（让数字活起来）
       ============================================================ */
    function startLoadTicker() {
        const totalEl = $('#cpuLoadTotal');
        const totalBar = $('#cpuLoadBar');
        let v = 23;
        state.loadTimer = setInterval(() => {
            v = Math.max(8, Math.min(72, v + (Math.random() - 0.5) * 8));
            const rounded = Math.round(v);
            totalEl.innerHTML = `${rounded}<em>%</em>`;
            totalBar.style.width = `${rounded}%`;
        }, 1800);
    }

    /* ============================================================
       事件绑定
       ============================================================ */
    function bindEvents() {
        // 搜索
        $('#procSearch').addEventListener('input', (e) => {
            state.currentKeyword = e.target.value;
            renderProcessList();
        });

        // 过滤
        $('#procFilters').addEventListener('click', (e) => {
            const btn = e.target.closest('.cpu-filter-chip');
            if (!btn) return;
            $$('.cpu-filter-chip').forEach(b => b.classList.remove('is-active'));
            btn.classList.add('is-active');
            state.currentFilter = btn.dataset.filter;
            renderProcessList();
        });

        // 快速预设
        $('#presetQuick').addEventListener('click', (e) => {
            const btn = e.target.closest('.cpu-preset-btn');
            if (!btn || btn.disabled) return;
            onQuickPreset(btn.dataset.preset);
        });

        // 重新检测
        $('[data-action="cpu-refresh"]').addEventListener('click', (e) => {
            const btn = e.currentTarget;
            btn.classList.add('is-spinning');
            setTimeout(() => {
                btn.classList.remove('is-spinning');
                showToast('info', '已重新扫描', `${state.cores.length} 个逻辑核 · ${state.processes.length} 个进程`);
            }, 900);
        });

        // 应用 / 重置 / 保存预设
        $('[data-action="cpu-apply"]').addEventListener('click', onApply);
        $('[data-action="cpu-reset"]').addEventListener('click', onReset);
        $('[data-action="cpu-save-preset"]').addEventListener('click', onSavePreset);

        // 首选核心清除
        const clearBtn = $('[data-action="cpu-clear-preferred"]');
        if (clearBtn) clearBtn.addEventListener('click', onClearPreferred);

        // 预设抽屉
        $('[data-action="cpu-preset-toggle"]').addEventListener('click', () => {
            $('#cpuPresetDrawer').classList.contains('is-open') ? closePresetDrawer() : openPresetDrawer();
        });
        $('[data-action="cpu-preset-close"]').addEventListener('click', closePresetDrawer);
        $('#cpuPresetMask').addEventListener('click', closePresetDrawer);

        // 预设卡点击
        root.querySelectorAll('.cpu-preset-card').forEach(card => {
            card.addEventListener('click', () => applyRecipe(card.dataset.recipe));
        });
    }

    /* ============================================================
       View 入口
       ============================================================ */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_INIT__[VIEW_NAME] = function (rootEl) {
        root = rootEl.querySelector('.cpu-view') || rootEl;

        state.cores = buildTopology();
        state.processes = buildProcesses();
        state.currentFilter = 'all';
        state.currentKeyword = '';
        state.selectedPid = null;
        state.selectedSet = new Set();
        state.currentPreferred = null;
        state.affinityMap = new Map();
        state.preferredMap = new Map();
        // 推荐最强核：默认是第一个 P 核主线程（P0）
        const p0 = state.cores.find(c => c.type === 'p' && c.physical === 0 && !c.isHT);
        state.recommendedIdx = p0 ? p0.cpuIndex : null;

        // 顶部逻辑核数同步
        const cntEl = root.querySelector('#cpuLogicalCount');
        if (cntEl) cntEl.textContent = `${state.cores.length} 逻辑核`;

        renderCoreMatrix();
        renderProcessList();
        bindEvents();
        startLoadTicker();
        refreshIcons(root);

        // 顶部告知一下首次状态
        setTimeout(() => {
            showToast('info', '已检测到 28 个逻辑核', '从左侧选择进程，开始为它指定核心');
        }, 320);
    };

    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_DESTROY__[VIEW_NAME] = function () {
        if (state.loadTimer) {
            clearInterval(state.loadTimer);
            state.loadTimer = null;
        }
        root = null;
    };
})();
