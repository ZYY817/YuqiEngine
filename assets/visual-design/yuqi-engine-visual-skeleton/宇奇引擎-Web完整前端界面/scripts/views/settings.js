/* ==========================================================================
   软件设置 · settings.js （独立形态版）
   --------------------------------------------------------------------------
   职责：本页交互骨架
   - 语言段控件切换
   - toggle 开关（自定义 .set-switch）
   - 日志保留时间滑杆（4 档吸附）
   - 标题栏指标 select 同步预览
   - 更新管理面板折叠展开
   - 重置危险动作二次确认
   ⚠️ 没有主题切换逻辑 —— WPF 真实 UI 中不暴露该入口
   ========================================================================== */
(function () {
    'use strict';

    const VIEW = 'settings';
    let cleanupFns = [];

    function on(el, evt, fn) {
        if (!el) return;
        el.addEventListener(evt, fn);
        cleanupFns.push(() => el.removeEventListener(evt, fn));
    }

    function init(root) {
        if (!root) return;

        // ---------- 1. 语言段控件 ----------
        const langBtns = root.querySelectorAll('.set-lang-btn');
        langBtns.forEach((btn) => {
            on(btn, 'click', () => {
                langBtns.forEach((b) => {
                    b.classList.remove('is-active');
                    b.setAttribute('aria-pressed', 'false');
                });
                btn.classList.add('is-active');
                btn.setAttribute('aria-pressed', 'true');
            });
        });

        // ---------- 2. 自定义 toggle 开关 ----------
        root.querySelectorAll('.set-switch').forEach((sw) => {
            on(sw, 'click', () => {
                const next = !sw.classList.contains('is-active');
                sw.classList.toggle('is-active', next);
                sw.setAttribute('aria-checked', String(next));
            });
        });

        // ---------- 3. 日志保留时间滑杆 ----------
        const TICK_DEFS = [
            { idx: 0, pct: 0,    label: '7 天' },
            { idx: 1, pct: 33.3, label: '14 天' },
            { idx: 2, pct: 66.6, label: '30 天' },
            { idx: 3, pct: 100,  label: '永久' },
        ];

        const sliderTrack = root.querySelector('.set-slider-track');
        const sliderFill  = root.querySelector('.set-slider-fill');
        const sliderThumb = root.querySelector('.set-slider-thumb');
        const sliderTicks = root.querySelectorAll('.set-slider-ticks span');
        const retentionValue = root.querySelector('#logRetentionValue');

        function applySliderTick(idx) {
            const tick = TICK_DEFS[idx];
            if (!tick) return;
            if (sliderFill)  sliderFill.style.width = tick.pct + '%';
            if (sliderThumb) sliderThumb.style.left = tick.pct + '%';
            if (retentionValue) retentionValue.textContent = tick.label;
            sliderTicks.forEach((s, i) => s.classList.toggle('is-active', i === idx));
        }

        sliderTicks.forEach((tickEl, i) => {
            on(tickEl, 'click', () => applySliderTick(i));
        });

        if (sliderTrack && sliderThumb) {
            let dragging = false;

            function snapToNearest(pct) {
                let nearest = 0;
                let minDist = Infinity;
                TICK_DEFS.forEach((t, i) => {
                    const d = Math.abs(t.pct - pct);
                    if (d < minDist) { minDist = d; nearest = i; }
                });
                applySliderTick(nearest);
            }

            function onMove(e) {
                if (!dragging) return;
                const rect = sliderTrack.getBoundingClientRect();
                const x = e.touches ? e.touches[0].clientX : e.clientX;
                const pct = Math.max(0, Math.min(100, ((x - rect.left) / rect.width) * 100));
                snapToNearest(pct);
            }
            function onDown(e) { dragging = true; e.preventDefault(); }
            function onUp()    { dragging = false; }

            on(sliderThumb, 'mousedown',  onDown);
            on(sliderThumb, 'touchstart', onDown);
            on(window, 'mousemove', onMove);
            on(window, 'touchmove', onMove);
            on(window, 'mouseup',   onUp);
            on(window, 'touchend',  onUp);

            on(sliderTrack, 'click', (e) => {
                if (e.target === sliderThumb || sliderThumb.contains(e.target)) return;
                const rect = sliderTrack.getBoundingClientRect();
                const pct = Math.max(0, Math.min(100, ((e.clientX - rect.left) / rect.width) * 100));
                snapToNearest(pct);
            });
        }

        // ---------- 4. 标题栏指标预览同步 ----------
        const ICON_MAP = {
            Cpu:     { icon: 'cpu',          value: '28%'  },
            Gpu:     { icon: 'monitor',      value: '46%'  },
            Memory:  { icon: 'memory-stick', value: '62%'  },
            Disk:    { icon: 'hard-drive',   value: '12%'  },
            Network: { icon: 'wifi',         value: '38ms' },
            None:    { icon: 'minus',        value: '—'    },
        };

        const primaryEl   = root.querySelector('[data-set-key="TitleBarTelemetryPrimary"]');
        const secondaryEl = root.querySelector('[data-set-key="TitleBarTelemetrySecondary"]');
        const previewP    = root.querySelector('#telemetryPreviewPrimary');
        const previewS    = root.querySelector('#telemetryPreviewSecondary');

        function renderChip(chip, value) {
            if (!chip) return;
            const meta = ICON_MAP[value] || ICON_MAP.None;
            chip.innerHTML = `
                <i data-lucide="${meta.icon}" class="icon-xs"></i>
                <span class="mono-text">${meta.value}</span>
            `;
            chip.style.opacity = value === 'None' ? '0.4' : '1';
            if (window.lucide) window.lucide.createIcons({ root: chip });
        }

        if (primaryEl)   on(primaryEl,   'change', () => renderChip(previewP, primaryEl.value));
        if (secondaryEl) on(secondaryEl, 'change', () => renderChip(previewS, secondaryEl.value));

        // ---------- 5. 更新管理面板折叠展开 ----------
        const updateToggle = root.querySelector('#setUpdateToggle');
        const updatePanel  = root.querySelector('#setUpdatePanel');

        if (updateToggle && updatePanel) {
            on(updateToggle, 'click', () => {
                const expanded = updateToggle.getAttribute('aria-expanded') === 'true';
                const next = !expanded;
                updateToggle.setAttribute('aria-expanded', String(next));
                if (next) {
                    updatePanel.removeAttribute('hidden');
                } else {
                    updatePanel.setAttribute('hidden', '');
                }
            });
        }

        // ---------- 6. 检查更新按钮（演示型，无真实网络逻辑）----------
        const checkBtn = root.querySelector('#setCheckUpdateBtn');
        if (checkBtn) {
            on(checkBtn, 'click', () => {
                checkBtn.disabled = true;
                const originalHTML = checkBtn.innerHTML;
                checkBtn.innerHTML = `
                    <i data-lucide="loader-2" class="icon-xs"></i><span>检查中...</span>
                `;
                if (window.lucide) window.lucide.createIcons({ root: checkBtn });
                setTimeout(() => {
                    checkBtn.disabled = false;
                    checkBtn.innerHTML = originalHTML;
                    if (window.lucide) window.lucide.createIcons({ root: checkBtn });
                }, 1500);
            });
        }

        // ---------- 7. 危险动作二次确认 ----------
        const resetBtn = root.querySelector('[data-set-action="reset-all"]');
        if (resetBtn) {
            on(resetBtn, 'click', () => {
                const confirmed = window.confirm(
                    '确认要重置所有设置吗？\n\n' +
                    '此操作不可撤销，但已应用的优化项不会被回滚。\n' +
                    '建议先导出当前配置作为备份。'
                );
                if (confirmed) {
                    console.log('[settings] reset all triggered (web preview only)');
                }
            });
        }

        const clearBtn = root.querySelector('[data-set-action="clear-log"]');
        if (clearBtn) {
            on(clearBtn, 'click', () => {
                window.confirm('确认要清除所有历史日志文件吗？\n\n该操作不可撤销。');
            });
        }
    }

    function destroy() {
        cleanupFns.forEach((fn) => { try { fn(); } catch (e) { /* noop */ } });
        cleanupFns = [];
    }

    window.__VIEW_INIT__    = window.__VIEW_INIT__    || {};
    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_INIT__[VIEW]    = init;
    window.__VIEW_DESTROY__[VIEW] = destroy;
})();
