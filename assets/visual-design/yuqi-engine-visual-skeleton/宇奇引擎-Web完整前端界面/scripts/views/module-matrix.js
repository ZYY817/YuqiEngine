/* ==========================================================================
   Module Matrix · 通用矩阵模块控制器
   --------------------------------------------------------------------------
   作用：
     - 给所有"通用矩阵模板"（.mod-page）的视图提供统一交互逻辑
     - 复刻 memory-optimization.js 中 mod-* 交互的精简版（不含 hero/strip）
   覆盖：
     audio-optimization / performance-boost / cpu-nav / graphics-nav /
     network-nav / privacy-nav / system-nav / explorer-nav /
     tasks-nav / services-nav / peripheral-nav
   ========================================================================== */
(function () {
    'use strict';

    const VIEW_NAMES = [
        'audio-optimization',
        'performance-boost',
        'cpu-nav',
        'graphics-nav',
        'network-nav',
        'privacy-nav',
        'system-nav',
        'explorer-nav',
        'tasks-nav',
        'services-nav',
        'peripheral-nav'
    ];

    const $$ = (root, sel) => Array.from((root || document).querySelectorAll(sel));
    const $  = (root, sel) => (root || document).querySelector(sel);

    /* ===== 计数 / 统计 ===== */
    function getRows(root)         { return $$(root, '.mod-toggle-row'); }
    function getOnRows(root)       { return getRows(root).filter(r => r.querySelector('.mod-switch')?.classList.contains('is-on')); }
    function getAppliedRows(root)  { return getRows(root).filter(r => r.dataset.applied === 'true'); }
    function getDirtyCount(root) {
        return getRows(root).reduce((n, r) => {
            const on = !!r.querySelector('.mod-switch')?.classList.contains('is-on');
            const applied = r.dataset.applied === 'true';
            return n + (on !== applied ? 1 : 0);
        }, 0);
    }

    function refreshStats(root) {
        const total    = getRows(root).length;
        const onCount  = getOnRows(root).length;
        const appCount = getAppliedRows(root).length;
        const dirty    = getDirtyCount(root);
        const pct      = total > 0 ? Math.round(onCount / total * 100) : 0;

        // 健康环（用 stroke-dasharray）
        const ring = $(root, '.mod-ring-progress');
        if (ring) {
            const C = 2 * Math.PI * 22; // ≈ 138
            const dash = (onCount / Math.max(1, total)) * C;
            ring.setAttribute('stroke-dasharray', `${dash.toFixed(2)} ${C - dash}`);
        }

        // 数字显示
        const numStrong = $(root, '.mod-ring-num strong');
        const numEm     = $(root, '.mod-ring-num em');
        if (numStrong) numStrong.textContent = onCount;
        if (numEm)     numEm.textContent     = total;

        const pctEl = $(root, '.mod-health-stat-title strong');
        if (pctEl) pctEl.textContent = pct + '%';

        // 分组进度
        $$(root, '.mod-group').forEach(g => {
            const meta = $(g, '.mod-group-meta strong');
            if (!meta) return;
            const groupOn = $$(g, '.mod-toggle-row').filter(r => r.querySelector('.mod-switch')?.classList.contains('is-on')).length;
            meta.textContent = groupOn;
        });

        // 应用岛屿
        const dot     = $(root, '.mod-status-dot');
        const text    = $(root, '.mod-status-text');
        const islandSel = $$(root, '.mod-island-stat .mono-text');
        if (islandSel[0]) islandSel[0].textContent = onCount;
        if (islandSel[1]) islandSel[1].textContent = appCount;

        const apply = $(root, '.mod-island-apply');
        if (apply) apply.disabled = dirty === 0;

        if (dirty > 0) {
            if (dot)  dot.classList.add('is-dirty');
            if (text) text.textContent = `有 ${dirty} 项未应用更改`;
        } else {
            if (dot)  dot.classList.remove('is-dirty');
            if (text) text.textContent = '无未应用更改';
        }
    }

    /* ===== 筛选 ===== */
    function applyFilter(root, filter) {
        const rows = getRows(root);
        rows.forEach(r => {
            let visible = true;
            if (filter === 'all') visible = true;
            else if (filter === 'recommend') visible = r.dataset.recommend === 'true';
            else visible = (r.dataset.risk || 'safe') === filter;
            r.classList.toggle('is-hidden', !visible);
        });
        $$(root, '.mod-group').forEach(g => {
            const visible = $$(g, '.mod-toggle-row').some(r => !r.classList.contains('is-hidden'));
            g.classList.toggle('is-empty', !visible);
        });
        const empty = $(root, '.mod-empty');
        if (empty) empty.hidden = rows.some(r => !r.classList.contains('is-hidden'));
    }

    function initSwitches(root) {
        $$(root, '.mod-switch').forEach(sw => {
            sw.addEventListener('click', (e) => {
                e.stopPropagation();
                const on = sw.classList.toggle('is-on');
                sw.setAttribute('aria-checked', on ? 'true' : 'false');
                refreshStats(root);
            });
        });
    }

    function initFilters(root) {
        const chips = $$(root, '.mod-filter-chip');
        chips.forEach(chip => {
            chip.addEventListener('click', () => {
                chips.forEach(c => { c.classList.remove('is-active'); c.setAttribute('aria-selected', 'false'); });
                chip.classList.add('is-active');
                chip.setAttribute('aria-selected', 'true');
                applyFilter(root, chip.dataset.filter || 'all');
            });
        });
    }

    /* ===== 控制栏：一键安全 / 恢复默认 ===== */
    function setSwitch(row, on) {
        const sw = row.querySelector('.mod-switch');
        if (!sw) return;
        sw.classList.toggle('is-on', !!on);
        sw.setAttribute('aria-checked', on ? 'true' : 'false');
    }

    function initBulkAndRestore(root) {
        const btns = $$(root, '.mod-control-actions .mod-btn-ghost');
        // 第一个：一键启用安全
        if (btns[0]) {
            btns[0].addEventListener('click', () => {
                getRows(root).forEach(r => {
                    if ((r.dataset.risk || 'safe') === 'safe' || r.dataset.recommend === 'true') {
                        setSwitch(r, true);
                    }
                });
                refreshStats(root);
            });
        }
        // 第二个：恢复默认（关闭所有）
        if (btns[1]) {
            btns[1].addEventListener('click', () => {
                getRows(root).forEach(r => setSwitch(r, false));
                refreshStats(root);
            });
        }
    }

    function initApplyButton(root) {
        const apply = $(root, '.mod-island-apply');
        if (!apply) return;
        apply.addEventListener('click', () => {
            getRows(root).forEach(r => {
                const on = !!r.querySelector('.mod-switch')?.classList.contains('is-on');
                r.dataset.applied = on ? 'true' : 'false';
            });
            refreshStats(root);
        });
    }

    /* ===== 总入口 ===== */
    function init(root) {
        if (!root) return;
        initSwitches(root);
        initFilters(root);
        initBulkAndRestore(root);
        initApplyButton(root);
        refreshStats(root);
    }

    /* ===== 注册：把同一个 init 注册给 11 个视图 ===== */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    VIEW_NAMES.forEach(name => {
        window.__VIEW_INIT__[name] = init;
    });
})();
