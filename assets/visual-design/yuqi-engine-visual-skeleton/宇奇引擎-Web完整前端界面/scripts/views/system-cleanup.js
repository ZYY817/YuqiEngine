/* ==========================================================================
   View · System Cleanup · 系统清理
   --------------------------------------------------------------------------
   状态机：idle → scanning → scanned → cleaning → done
   按 view-loader 约定注册到 window.__VIEW_INIT__['system-cleanup']
   ========================================================================== */
(function () {
    'use strict';

    let timer = null;

    function init(root) {
        const page = root.querySelector('.cleanup-page');
        if (!page) return;

        const $ = (sel) => root.querySelector(sel);
        const $$ = (sel) => root.querySelectorAll(sel);

        const orbBtn = $('#orbBtn');
        const ctaPrimary = $('.cl-cta-primary');
        const ctaCancel = $('.cl-cta-secondary');
        const ctaText = ctaPrimary?.querySelector('.cl-cta-text');
        const ctaIcon = ctaPrimary?.querySelector('.cl-cta-icon');
        const statusLine = $('#statusLine');
        const statusText = statusLine?.querySelector('.cl-status-text');

        const orbScanPercent = $('#orbScanPercent');
        const orbScanItem = $('#orbScanItem');
        const orbCleanPercent = $('#orbCleanPercent');
        const orbCleanCurrent = $('#orbCleanCurrent');
        const orbCleanTotal = $('#orbCleanTotal');
        const orbCleanItem = $('#orbCleanItem');
        const orbTotalSize = $('#orbTotalSize');
        const orbTotalFiles = $('#orbTotalFiles');
        const orbSelected = $('#orbSelected');

        const tabs = $$('.cl-tab');
        const rows = $$('.cl-row');
        const checkboxes = $$('.cl-row-check input[type="checkbox"]');

        function setState(state) {
            page.setAttribute('data-state', state);
            updateCTA(state);
        }

        function updateCTA(state) {
            if (!ctaPrimary || !ctaText || !ctaIcon) return;
            ctaCancel.hidden = true;
            ctaPrimary.disabled = false;

            switch (state) {
                case 'idle':
                    ctaText.textContent = '开始扫描';
                    ctaIcon.setAttribute('data-lucide', 'scan-search');
                    setStatus('info', '点击中心球体开始扫描，或按下方按钮启动');
                    break;
                case 'scanning':
                    ctaText.textContent = '正在扫描...';
                    ctaIcon.setAttribute('data-lucide', 'loader');
                    ctaPrimary.disabled = true;
                    ctaCancel.hidden = false;
                    setStatus('info', '正在分析磁盘，请稍候');
                    break;
                case 'scanned':
                    ctaText.textContent = '一键清理';
                    ctaIcon.setAttribute('data-lucide', 'sparkles');
                    refreshTotals();
                    break;
                case 'cleaning':
                    ctaText.textContent = '正在清理...';
                    ctaIcon.setAttribute('data-lucide', 'loader');
                    ctaPrimary.disabled = true;
                    ctaCancel.hidden = false;
                    break;
                case 'done':
                    ctaText.textContent = '再次扫描';
                    ctaIcon.setAttribute('data-lucide', 'rotate-ccw');
                    setStatus('success', '清理完成，磁盘空间已释放');
                    break;
            }
            if (window.lucide) lucide.createIcons({ root });
        }

        function setStatus(kind, msg) {
            if (!statusLine || !statusText) return;
            statusLine.setAttribute('data-status', kind);
            statusText.textContent = msg;
            const ico = statusLine.querySelector('.cl-status-icon');
            if (ico) {
                const iconMap = { info: 'info', success: 'check-circle-2', warn: 'alert-triangle', error: 'x-circle' };
                ico.setAttribute('data-lucide', iconMap[kind] || 'info');
                if (window.lucide) lucide.createIcons({ root });
            }
        }

        function refreshTotals() {
            const checkedRows = Array.from(rows).filter(r => r.dataset.checked === 'true');
            let totalGB = 0;
            let totalFiles = 0;
            checkedRows.forEach(r => {
                const sizeEl = r.querySelector('.cl-row-size');
                const filesEl = r.querySelector('.cl-row-files');
                if (sizeEl) {
                    const m = sizeEl.textContent.match(/([\d.]+)/);
                    if (m) totalGB += parseFloat(m[1]);
                }
                if (filesEl) {
                    const m = filesEl.textContent.match(/([\d,]+)/);
                    if (m) totalFiles += parseInt(m[1].replace(/,/g, ''), 10);
                }
            });
            if (orbTotalSize) orbTotalSize.textContent = totalGB.toFixed(2);
            if (orbTotalFiles) orbTotalFiles.textContent = totalFiles.toLocaleString('en-US');
            if (orbSelected) orbSelected.textContent = checkedRows.length;
        }

        function runScan() {
            setState('scanning');
            const items = [
                '系统临时文件', '用户临时文件', '预读取文件',
                '缩略图缓存', 'Windows 日志', '更新缓存', '回收站',
                'GPU 着色器缓存', 'Steam 缓存', 'Epic 缓存',
                'Chrome 缓存', 'Edge 缓存', 'Firefox 缓存'
            ];
            let i = 0;
            const total = items.length;
            clearInterval(timer);
            timer = setInterval(() => {
                const pct = Math.min(100, Math.round((i + 1) / total * 100));
                if (orbScanPercent) orbScanPercent.textContent = pct;
                if (orbScanItem) orbScanItem.textContent = items[i] || '完成';
                i++;
                if (i >= total) {
                    clearInterval(timer);
                    setTimeout(() => setState('scanned'), 500);
                }
            }, 240);
        }

        function runClean() {
            const checkedRows = Array.from(rows).filter(r => r.dataset.checked === 'true');
            if (checkedRows.length === 0) {
                setStatus('warn', '没有选中任何项目');
                return;
            }
            setState('cleaning');
            const total = checkedRows.length;
            let i = 0;
            if (orbCleanTotal) orbCleanTotal.textContent = total;
            const progressArc = root.querySelector('.cl-orb-progress');
            clearInterval(timer);
            timer = setInterval(() => {
                const cur = i + 1;
                const pct = Math.round(cur / total * 100);
                if (orbCleanPercent) orbCleanPercent.textContent = pct;
                if (orbCleanCurrent) orbCleanCurrent.textContent = cur;
                if (orbCleanItem) {
                    const nm = checkedRows[i]?.querySelector('.cl-row-name')?.textContent || '';
                    orbCleanItem.textContent = nm;
                }
                if (progressArc) {
                    const dashLen = 920 * (cur / total);
                    progressArc.setAttribute('stroke-dasharray', `${dashLen} 920`);
                }
                i++;
                if (i >= total) {
                    clearInterval(timer);
                    setTimeout(() => setState('done'), 500);
                }
            }, 360);
        }

        // 事件
        orbBtn?.addEventListener('click', () => {
            const state = page.getAttribute('data-state');
            if (state === 'idle' || state === 'done') runScan();
            else if (state === 'scanned') runClean();
        });

        ctaPrimary?.addEventListener('click', () => {
            const state = page.getAttribute('data-state');
            if (state === 'idle' || state === 'done') runScan();
            else if (state === 'scanned') runClean();
        });

        ctaCancel?.addEventListener('click', () => {
            clearInterval(timer);
            const state = page.getAttribute('data-state');
            if (state === 'scanning') {
                setState('idle');
                setStatus('warn', '扫描已取消');
            } else if (state === 'cleaning') {
                setState('scanned');
                setStatus('warn', '清理已取消，已选项保留');
            }
        });

        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                tabs.forEach(t => t.classList.remove('is-active'));
                tab.classList.add('is-active');
                const cat = tab.getAttribute('data-cat');
                rows.forEach(r => {
                    r.style.display = (cat === 'all' || r.dataset.cat === cat) ? '' : 'none';
                });
            });
        });

        checkboxes.forEach(cb => {
            cb.addEventListener('change', () => {
                const row = cb.closest('.cl-row');
                row.dataset.checked = cb.checked ? 'true' : 'false';
                refreshTotals();
            });
        });

        $$('.cl-bulk-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const mode = btn.getAttribute('data-bulk');
                checkboxes.forEach(cb => {
                    const row = cb.closest('.cl-row');
                    const risk = row.dataset.risk;
                    let checked = cb.checked;
                    if (mode === 'all') checked = true;
                    else if (mode === 'none') checked = false;
                    else if (mode === 'safe') checked = (risk === 'safe');
                    cb.checked = checked;
                    row.dataset.checked = checked ? 'true' : 'false';
                });
                refreshTotals();
            });
        });

        root.querySelector('[data-action="release-memory"]')?.addEventListener('click', () => {
            setStatus('success', '已释放 1.2 GB 工作集内存');
        });

        // 默认进入"已扫描"态以便用户立即看到完整效果
        setState('scanned');
        refreshTotals();
    }

    function destroy() {
        clearInterval(timer);
        timer = null;
    }

    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_INIT__['system-cleanup'] = init;

    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_DESTROY__['system-cleanup'] = destroy;
})();
