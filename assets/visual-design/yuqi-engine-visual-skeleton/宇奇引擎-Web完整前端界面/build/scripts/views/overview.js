/* ==========================================================================
   View · Overview · 系统概览
   --------------------------------------------------------------------------
   入场动画 + 矩阵磁贴跳转
   ========================================================================== */
(function () {
    'use strict';

    function init(root) {
        // 数字滚动入场
        const numEl = root.querySelector('.ov-hero-stat-num');
        if (numEl) {
            const target = parseInt(numEl.textContent.trim(), 10);
            if (Number.isFinite(target)) {
                numEl.textContent = '0';
                const duration = 900;
                const delay = 280;
                const easeOut = (t) => 1 - Math.pow(1 - t, 3);
                const start = performance.now() + delay;
                function tick(now) {
                    if (now < start) { requestAnimationFrame(tick); return; }
                    const p = Math.min(1, (now - start) / duration);
                    numEl.textContent = String(Math.round(easeOut(p) * target));
                    if (p < 1) requestAnimationFrame(tick);
                }
                requestAnimationFrame(tick);
            }
        }

        // 矩阵磁贴 → 触发侧边栏对应项
        root.querySelectorAll('.ov-matrix-tile').forEach(tile => {
            tile.addEventListener('click', (e) => {
                e.preventDefault();
                const view = tile.getAttribute('data-view');
                if (!view) return;
                const sideBar = document.querySelector('side-bar');
                if (!sideBar) return;
                const target = sideBar.querySelector(`.nav-item[data-view="${view}"]`);
                if (target) target.click();
            });
        });
    }

    // 注册到全局视图入口
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_INIT__['overview'] = init;
})();
