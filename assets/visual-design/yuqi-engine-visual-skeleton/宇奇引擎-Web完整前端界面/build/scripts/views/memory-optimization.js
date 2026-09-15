/* ==========================================================================
   内存优化 · Memory Optimization · View Controller v3 (方案 C)
   --------------------------------------------------------------------------
   骨架基于通用 mod-page 模板，本控制器负责：
     1. 即时清理 Hero 卡：圆环渲染 + 一键清理（动画 + 弹窗）
     2. TOP5 横向条：刷新动画
     3. 控制栏：筛选 chip / 一键启用安全项 / 恢复默认 popover
     4. 开关矩阵：14 项切换 + 按风险筛选 + 健康环 + 风险分布
     5. 应用岛屿：未应用计数 + 立即应用
     6. 模态弹窗：清理详情 + ESC + 遮罩点击 + 再次清理
   ========================================================================== */
(function () {
    'use strict';

    const VIEW = 'memory-optimization';
    const HERO_RING_C = 2 * Math.PI * 86;   // 圆环半径 r=86 → C ≈ 540.35
    const HEALTH_RING_C = 2 * Math.PI * 22; // 健康环 r=22 → C ≈ 138.23

    /* ---------- 模块级状态（destroy 时清理） ---------- */
    let escHandler = null;
    let docClickHandler = null;
    let cleaningTimer = null;

    /* ---------- 工具 ---------- */
    function $(root, sel) { return root.querySelector(sel); }
    function $$(root, sel) { return Array.from(root.querySelectorAll(sel)); }
    function fmtMB(mb) {
        if (mb >= 1024) return (mb / 1024).toFixed(2) + ' GB';
        return Math.round(mb) + ' MB';
    }

    /* ==========================================================
       1. 即时清理 Hero 圆环
       ========================================================== */
    function renderHeroRing(root, pct) {
        const ring = $(root, '#memHeroRingProgress');
        if (!ring) return;
        const ratio = Math.max(0, Math.min(1, pct / 100));
        ring.setAttribute(
            'stroke-dasharray',
            `${(HERO_RING_C * ratio).toFixed(2)} ${HERO_RING_C.toFixed(2)}`
        );
        const stateEl = $(root, '#memHeroRingState');
        if (!stateEl) return;
        if (pct < 50) {
            stateEl.textContent = '运行良好';
            stateEl.style.background = 'rgba(34, 197, 94, 0.10)';
            stateEl.style.color = '#15803D';
        } else if (pct < 80) {
            stateEl.textContent = '负载偏高';
            stateEl.style.background = 'rgba(245, 158, 11, 0.10)';
            stateEl.style.color = '#B45309';
        } else {
            stateEl.textContent = '内存吃紧';
            stateEl.style.background = 'rgba(239, 68, 68, 0.10)';
            stateEl.style.color = '#B91C1C';
        }
    }

    function updateMemDisplay(root, used, total) {
        const pctVal = Math.round((used / total) * 100);
        const set = (id, txt) => { const el = $(root, '#' + id); if (el) el.textContent = txt; };
        set('memHeroUsed', used.toFixed(2));
        set('memHeroTotal', total.toFixed(0));
        const pctEl = $(root, '#memHeroPct');
        if (pctEl) pctEl.textContent = pctVal;
        set('memHeroAvail', (total - used).toFixed(1) + ' GB');
        set('memHeroCommit', (used * 1.18).toFixed(1) + ' GB');
        renderHeroRing(root, pctVal);

        const est = Math.min(4, used * (0.18 + Math.random() * 0.04));
        set('memHeroEstimate', est.toFixed(1) + ' GB');
    }

    /* ==========================================================
       2. 一键清理 + 模态弹窗
       ========================================================== */
    function openModal(root) {
        const modal = $(root, '#memModal');
        if (!modal) return;
        modal.hidden = false;
        modal.setAttribute('aria-hidden', 'false');
        if (window.lucide) lucide.createIcons({ root: modal });
    }
    function closeModal(root) {
        const modal = $(root, '#memModal');
        if (!modal) return;
        modal.hidden = true;
        modal.setAttribute('aria-hidden', 'true');
    }

    function fillModalDetail(root, beforeUsed, afterUsed) {
        const totalReleasedMB = (beforeUsed - afterUsed) * 1024;
        const ratios = [0.51, 0.29, 0.145, 0.020, 0.035];
        const keys = ['standby', 'workingset', 'syscache', 'dnscache', 'handles'];
        keys.forEach((key, i) => {
            const el = root.querySelector(`.mem-modal-item-val[data-key="${key}"]`);
            if (el) el.textContent = fmtMB(totalReleasedMB * ratios[i]);
        });
        const setText = (id, txt) => { const el = $(root, '#' + id); if (el) el.textContent = txt; };
        setText('memModalAmount', (beforeUsed - afterUsed).toFixed(2) + ' GB');
        setText('memModalDuration', (0.8 + Math.random() * 0.7).toFixed(1));
        setText('memModalBefore', beforeUsed.toFixed(2) + ' GB');
        setText('memModalAfter', afterUsed.toFixed(2) + ' GB');
    }

    function performClean(root) {
        const cta = $(root, '#memHeroCleanBtn');
        if (!cta || cta.disabled) return;

        const usedEl = $(root, '#memHeroUsed');
        const totalEl = $(root, '#memHeroTotal');
        const beforeUsed = parseFloat(usedEl?.textContent) || 12.6;
        const total = parseFloat(totalEl?.textContent) || 32;

        cta.disabled = true;
        cta.classList.add('is-loading');
        const ctaText = cta.querySelector('.mem-hero-cta-text');
        const originalText = ctaText ? ctaText.textContent : '一键清理内存';
        if (ctaText) ctaText.textContent = '正在清理…';

        cleaningTimer = setTimeout(() => {
            const releaseGB = Math.min(4.2, beforeUsed * (0.18 + Math.random() * 0.10));
            const afterUsed = Math.max(0.8, beforeUsed - releaseGB);

            updateMemDisplay(root, afterUsed, total);

            if (usedEl) {
                usedEl.style.transition = 'color 600ms ease';
                usedEl.style.color = '#22C55E';
                setTimeout(() => { usedEl.style.color = ''; }, 1200);
            }

            fillModalDetail(root, beforeUsed, afterUsed);
            openModal(root);

            cta.disabled = false;
            cta.classList.remove('is-loading');
            if (ctaText) ctaText.textContent = originalText;
            cleaningTimer = null;
        }, 1100);
    }

    function initCleanCTA(root) {
        const cta = $(root, '#memHeroCleanBtn');
        if (cta) cta.addEventListener('click', () => performClean(root));
    }

    function initModal(root) {
        const modal = $(root, '#memModal');
        if (!modal) return;

        modal.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-action]');
            if (!btn) return;
            const action = btn.dataset.action;
            if (action === 'modal-close') closeModal(root);
            else if (action === 'modal-clean-again') {
                closeModal(root);
                setTimeout(() => performClean(root), 250);
            }
        });

        escHandler = (e) => {
            if (e.key === 'Escape' && !modal.hidden) closeModal(root);
        };
        document.addEventListener('keydown', escHandler);
    }

    /* ==========================================================
       3. TOP5 横向条刷新
       ========================================================== */
    function initTopRefresh(root) {
        const btn = $(root, '#memTopRefresh');
        if (!btn) return;
        btn.addEventListener('click', () => {
            const icon = btn.querySelector('svg');
            if (icon) {
                icon.style.transition = 'transform 600ms ease';
                icon.style.transform = 'rotate(360deg)';
                setTimeout(() => {
                    icon.style.transition = 'none';
                    icon.style.transform = 'rotate(0deg)';
                }, 650);
            }
            $$(root, '.mem-top-strip-bar').forEach(bar => {
                const cur = parseFloat(bar.style.getPropertyValue('--w')) || 50;
                const next = Math.max(5, Math.min(95, cur + (Math.random() - 0.5) * 8));
                bar.style.setProperty('--w', next.toFixed(0) + '%');
            });
            $$(root, '.mem-top-strip-size').forEach((el, i) => {
                const base = [2.10, 1.78, 0.92, 0.43, 0.22][i] || 1;
                const next = Math.max(0.05, base + (Math.random() - 0.5) * 0.15);
                el.textContent = next >= 1
                    ? next.toFixed(2) + ' GB'
                    : Math.round(next * 1024) + ' MB';
            });
        });
    }

    /* ==========================================================
       4. 开关矩阵 + 筛选 + 健康环 + 风险分布
       ========================================================== */
    function getRows(root) {
        return $$(root, '.mod-toggle-row');
    }
    function isOn(row) {
        const sw = row.querySelector('.mod-switch');
        return !!(sw && sw.classList.contains('is-on'));
    }
    function isApplied(row) {
        return row.dataset.applied === 'true';
    }

    /* 健康环 + 风险分布 + 应用岛屿 */
    function refreshStats(root) {
        const rows = getRows(root);
        const total = rows.length;
        let on = 0, pending = 0, applied = 0;
        const distOn = { safe: 0, warn: 0, danger: 0 };

        rows.forEach(row => {
            const onState = isOn(row);
            const appliedState = isApplied(row);
            if (onState) {
                on++;
                const risk = row.dataset.risk || 'safe';
                if (distOn[risk] !== undefined) distOn[risk]++;
            }
            if (appliedState) applied++;
            if (onState !== appliedState) pending++;
        });

        // 健康环
        const ring = $(root, '#memHealthRing');
        if (ring) {
            const ratio = total > 0 ? on / total : 0;
            ring.setAttribute(
                'stroke-dasharray',
                `${(HEALTH_RING_C * ratio).toFixed(2)} ${HEALTH_RING_C.toFixed(2)}`
            );
        }
        const setText = (id, txt) => { const el = $(root, '#' + id); if (el) el.textContent = txt; };
        setText('memHealthOn', String(on));
        setText('memHealthTotal', String(total));
        setText('memHealthPct', total > 0 ? Math.round(on / total * 100) + '%' : '0%');
        setText('memDistSafe', distOn.safe + ' 安全');
        setText('memDistWarn', distOn.warn + ' 中');
        setText('memDistDanger', distOn.danger + ' 高');

        // 应用岛屿
        setText('memIslandPending', String(pending));
        setText('memIslandApplied', String(applied));
        const status = $(root, '#memIslandStatus');
        const dot = $(root, '.mod-status-dot');
        const applyBtn = $(root, '#memIslandApply');
        if (pending > 0) {
            if (status) status.textContent = `有 ${pending} 项更改待应用`;
            if (dot) {
                dot.style.background = '#F59E0B';
                dot.style.boxShadow = '0 0 0 3px rgba(245, 158, 11, 0.16)';
            }
            if (applyBtn) applyBtn.disabled = false;
        } else {
            if (status) status.textContent = '无未应用更改';
            if (dot) {
                dot.style.background = '';
                dot.style.boxShadow = '';
            }
            if (applyBtn) applyBtn.disabled = true;
        }

        // 分组的"X / Y 启用"刷新
        $$(root, '.mod-group').forEach(g => {
            const groupRows = $$(g, '.mod-toggle-row');
            const groupOn = groupRows.filter(r => isOn(r)).length;
            const onEl = g.querySelector('.mem-group-on');
            if (onEl) onEl.textContent = String(groupOn);
        });

        // 筛选 chip 计数
        const counts = countByFilter(rows);
        const setFilterCount = (id, n) => { const el = $(root, '#' + id); if (el) el.textContent = String(n); };
        setFilterCount('memFilterCountAll', counts.all);
        setFilterCount('memFilterCountSafe', counts.safe);
        setFilterCount('memFilterCountWarn', counts.warn);
        setFilterCount('memFilterCountDanger', counts.danger);
        setFilterCount('memFilterCountRecommend', counts.recommend);
    }

    function countByFilter(rows) {
        const c = { all: rows.length, safe: 0, warn: 0, danger: 0, recommend: 0 };
        rows.forEach(r => {
            const risk = r.dataset.risk || 'safe';
            if (c[risk] !== undefined) c[risk]++;
            if (r.dataset.recommend === 'true') c.recommend++;
        });
        return c;
    }

    function applyFilter(root, filter) {
        const rows = getRows(root);
        rows.forEach(r => {
            let visible = true;
            if (filter === 'all') visible = true;
            else if (filter === 'recommend') visible = r.dataset.recommend === 'true';
            else visible = (r.dataset.risk || 'safe') === filter;
            r.classList.toggle('is-hidden', !visible);
        });
        // 隐藏空分组
        $$(root, '.mod-group').forEach(g => {
            const visible = $$(g, '.mod-toggle-row').some(r => !r.classList.contains('is-hidden'));
            g.classList.toggle('is-empty', !visible);
        });
        // 空状态
        const empty = $(root, '#memEmpty');
        if (empty) {
            const anyVisible = rows.some(r => !r.classList.contains('is-hidden'));
            empty.hidden = anyVisible;
        }
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
                chips.forEach(c => {
                    c.classList.remove('is-active');
                    c.setAttribute('aria-selected', 'false');
                });
                chip.classList.add('is-active');
                chip.setAttribute('aria-selected', 'true');
                applyFilter(root, chip.dataset.filter || 'all');
            });
        });
    }

    /* ==========================================================
       5. 控制栏：一键启用安全项 + 恢复默认 popover
       ========================================================== */
    function setSwitch(row, on) {
        const sw = row.querySelector('.mod-switch');
        if (!sw) return;
        sw.classList.toggle('is-on', !!on);
        sw.setAttribute('aria-checked', on ? 'true' : 'false');
    }

    function initBulkSafe(root) {
        const btn = $(root, '#memBulkSafe');
        if (!btn) return;
        btn.addEventListener('click', () => {
            getRows(root).forEach(r => {
                if ((r.dataset.risk || 'safe') === 'safe' || r.dataset.recommend === 'true') {
                    setSwitch(r, true);
                }
            });
            refreshStats(root);
        });
    }

    function initRestorePopover(root) {
        const trigger = $(root, '#memRestoreDefault');
        const popover = $(root, '#memRestorePopover');
        if (!trigger || !popover) return;

        trigger.addEventListener('click', (e) => {
            e.stopPropagation();
            popover.hidden = !popover.hidden;
        });

        docClickHandler = (e) => {
            if (popover.hidden) return;
            if (!popover.contains(e.target) && !trigger.contains(e.target)) {
                popover.hidden = true;
            }
        };
        document.addEventListener('click', docClickHandler);

        popover.addEventListener('click', (e) => {
            const item = e.target.closest('[data-restore]');
            if (!item) return;
            const mode = item.dataset.restore;
            getRows(root).forEach(r => {
                if (mode === 'recommend') {
                    setSwitch(r, r.dataset.recommend === 'true');
                } else if (mode === 'off') {
                    setSwitch(r, false);
                } else if (mode === 'reset') {
                    // 重置为出厂：data-applied 维持原状作为基线
                    setSwitch(r, isApplied(r));
                }
            });
            popover.hidden = true;
            refreshStats(root);
        });
    }

    /* ==========================================================
       6. 应用岛屿：立即应用
       ========================================================== */
    function initIslandApply(root) {
        const btn = $(root, '#memIslandApply');
        if (!btn) return;
        btn.addEventListener('click', () => {
            if (btn.disabled) return;
            const original = btn.textContent;
            btn.disabled = true;
            btn.textContent = '应用中…';

            setTimeout(() => {
                getRows(root).forEach(row => {
                    const on = isOn(row);
                    row.dataset.applied = on ? 'true' : 'false';
                    const titleWrap = row.querySelector('.mod-toggle-row-title');
                    if (titleWrap) {
                        const oldTag = titleWrap.querySelector('.mod-tag--applied');
                        if (oldTag) oldTag.remove();
                        if (on) {
                            const tag = document.createElement('span');
                            tag.className = 'mod-tag mod-tag--applied';
                            tag.textContent = '已生效';
                            titleWrap.appendChild(tag);
                        }
                    }
                });
                btn.textContent = original;
                refreshStats(root);
            }, 700);
        });
    }

    /* ==========================================================
       注册到 view-loader
       ========================================================== */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};

    window.__VIEW_INIT__[VIEW] = function (root) {
        try {
            updateMemDisplay(root, 12.6, 32);

            initCleanCTA(root);
            initModal(root);
            initTopRefresh(root);
            initSwitches(root);
            initFilters(root);
            initBulkSafe(root);
            initRestorePopover(root);
            initIslandApply(root);

            refreshStats(root);
        } catch (e) {
            console.error('[memory-optimization] init failed', e);
        }
    };

    window.__VIEW_DESTROY__[VIEW] = function () {
        if (cleaningTimer) { clearTimeout(cleaningTimer); cleaningTimer = null; }
        if (escHandler) {
            document.removeEventListener('keydown', escHandler);
            escHandler = null;
        }
        if (docClickHandler) {
            document.removeEventListener('click', docClickHandler);
            docClickHandler = null;
        }
    };
})();
