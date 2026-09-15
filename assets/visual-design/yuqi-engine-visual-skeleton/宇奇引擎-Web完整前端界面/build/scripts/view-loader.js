/* ==========================================================================
   View Loader · 视图路由加载器
   --------------------------------------------------------------------------
   职责：
     1. 监听 side-bar 的 view-changed 事件
     2. 按 VIEW_REGISTRY 加载对应的 HTML / CSS / JS
     3. 注入到 #mainContent；调用 window.__VIEW_INIT__[viewName](root) 初始化
     4. 离开旧视图时调用 window.__VIEW_DESTROY__[viewName]?.() 清理
     5. 未注册视图自动渲染"占位页"（敬请期待）

   约定：
     - 每个视图 JS 必须自注册：window.__VIEW_INIT__[name] = (root) => {...}
     - 视图私有的 timer / listener / state，请挂到模块闭包并通过 __VIEW_DESTROY__ 注销
   ========================================================================== */
(function () {
    'use strict';

    /* ----------------- 视图注册表 ----------------- */
    const VIEW_REGISTRY = {
        'overview': {
            html: 'views/overview.html',
            css:  'styles/modules/home-overview.css',
            js:   'scripts/views/overview.js',
            title: '系统概览'
        },
        'system-cleanup': {
            html: 'views/system-cleanup.html',
            css:  'styles/views/system-cleanup.css',
            js:   'scripts/views/system-cleanup.js',
            title: '系统清理'
        },
        'restore-point': {
            html: 'views/restore-point.html',
            css:  'styles/views/restore-point.css',
            js:   'scripts/views/restore-point.js',
            title: '系统还原点'
        },
        'repair-center': {
            html: 'views/repair-center.html',
            css:  'styles/views/repair-center.css',
            js:   'scripts/views/repair-center.js',
            title: '修复中心'
        },
        'backup-manager': {
            html: 'views/backup-manager.html',
            css:  'styles/views/backup-manager.css',
            js:   'scripts/views/backup-manager.js',
            title: '备份管理'
        },
        'network-test': {
            html: 'views/network-test.html',
            css:  'styles/views/network-test.css',
            js:   'scripts/views/network-test.js',
            title: '网络测试'
        },
        'cpu-core-management': {
            html: 'views/cpu-core-management.html',
            css:  'styles/views/cpu-core-management.css',
            js:   'scripts/views/cpu-core-management.js',
            title: 'CPU 核心调度'
        },
        'startup-manager': {
            html: 'views/startup-manager.html',
            css:  'styles/views/startup-manager.css',
            js:   'scripts/views/startup-manager.js',
            title: '启动项管理'
        },
        'power-plan-editor': {
            html: 'views/power-plan-editor.html',
            css:  'styles/views/power-plan-editor.css',
            js:   'scripts/views/power-plan-editor.js',
            title: '电源优化'
        },
        'hardware-info': {
            html: 'views/hardware-info.html',
            css:  'styles/views/hardware-info.css',
            js:   'scripts/views/hardware-info.js',
            title: '硬件信息'
        },
        'memory-optimization': {
            html: 'views/memory-optimization.html',
            css:  'styles/views/memory-optimization.css',
            js:   'scripts/views/memory-optimization.js',
            title: '内存优化'
        },
        // —— 以下 11 个共用 module-template.css + module-matrix.js（通用矩阵模板）——
        'audio-optimization': {
            html: 'views/audio-optimization.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '音频优化'
        },
        'performance-boost': {
            html: 'views/performance-boost.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '性能加速'
        },
        'cpu-nav': {
            html: 'views/cpu-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: 'CPU 优化'
        },
        'graphics-nav': {
            html: 'views/graphics-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '显卡优化'
        },
        'network-nav': {
            html: 'views/network-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '网络优化'
        },
        'privacy-nav': {
            html: 'views/privacy-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '隐私保护'
        },
        'system-nav': {
            html: 'views/system-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '系统调校'
        },
        'explorer-nav': {
            html: 'views/explorer-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '桌面体验'
        },
        'tasks-nav': {
            html: 'views/tasks-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '任务调度'
        },
        'services-nav': {
            html: 'views/services-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '系统服务'
        },
        'peripheral-nav': {
            html: 'views/peripheral-nav.html',
            css:  'styles/module-template.css',
            js:   'scripts/views/module-matrix.js',
            title: '外设优化'
        },
        // —— 软件设置页（独立形态，复用 module-template + 专属 settings.css）——
        'settings': {
            html: 'views/settings.html',
            css:  'styles/views/settings.css',
            js:   'scripts/views/settings.js',
            title: '软件设置'
        }
        // 后续每个视图按需挂上来；未挂的会走通用占位页
    };

    /* ----------------- 内部状态 ----------------- */
    const loadedCSS = new Set();
    const loadedJS  = new Set();
    let currentView = null;

    /* ----------------- 工具函数 ----------------- */
    function fetchText(url) {
        return fetch(url, { cache: 'no-cache' }).then(res => {
            if (!res.ok) throw new Error(`fetch ${url} → ${res.status}`);
            return res.text();
        });
    }

    function injectCSS(href) {
        if (loadedCSS.has(href)) return Promise.resolve();
        return new Promise((resolve) => {
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            // 防 HTTP 缓存：开发调试阶段附时间戳。其他页面也能受益
            link.href = href + (href.includes('?') ? '&' : '?') + '_v=' + Date.now();
            link.onload = () => { loadedCSS.add(href); resolve(); };
            link.onerror = () => { console.warn('[view-loader] CSS 加载失败（忽略）：', href); resolve(); };
            document.head.appendChild(link);
        });
    }

    function injectJS(src) {
        if (loadedJS.has(src)) return Promise.resolve();
        return new Promise((resolve, reject) => {
            const s = document.createElement('script');
            // 防 HTTP 缓存：在开发调试阶段附加时间戳，避免浏览器用旧 JS
            s.src = src + (src.includes('?') ? '&' : '?') + '_v=' + Date.now();
            s.async = false;
            s.onload = () => { loadedJS.add(src); resolve(); };
            s.onerror = () => reject(new Error(`script load failed: ${src}`));
            document.body.appendChild(s);
        });
    }

    /* ----------------- 渲染占位页（未注册视图） ----------------- */
    function renderPlaceholder(mainContent, viewName) {
        const sideBar = document.querySelector('side-bar');
        const navItem = sideBar?.querySelector(`.nav-item[data-view="${viewName}"]`);
        const label = navItem?.querySelector('.nav-label')?.textContent || viewName;

        mainContent.innerHTML = `
            <div class="view-placeholder">
                <div class="view-placeholder-card">
                    <i data-lucide="hammer" class="view-placeholder-icon"></i>
                    <h2 class="view-placeholder-title">${label}</h2>
                    <p class="view-placeholder-desc">该模块视觉稿正在制作中，敬请期待</p>
                    <span class="view-placeholder-meta mono-text">view: ${viewName}</span>
                </div>
            </div>
        `;
        if (window.lucide) lucide.createIcons({ root: mainContent });
    }

    /* ----------------- 渲染错误页 ----------------- */
    function renderError(mainContent, viewName, err) {
        const isFileProtocol = location.protocol === 'file:';
        const isFetchFail = err && /fetch/i.test(String(err.message || err));

        // 在 file:// 协议下命中 fetch 错误 → 显示专属引导页
        if (isFileProtocol && isFetchFail) {
            mainContent.innerHTML = `
                <div class="view-placeholder">
                    <div class="view-placeholder-card view-placeholder-card--error" style="max-width:560px;">
                        <i data-lucide="server-off" class="view-placeholder-icon"></i>
                        <h2 class="view-placeholder-title">需要通过本地服务器访问</h2>
                        <p class="view-placeholder-desc">
                            浏览器在 <code>file://</code> 协议下出于安全限制不允许加载视图模块。<br>
                            请双击目录下的 <b>启动预览.bat</b> 自动启动本地服务器和浏览器。
                        </p>
                        <div style="display:flex;flex-direction:column;gap:8px;align-items:center;margin-top:14px;">
                            <span class="view-placeholder-meta mono-text">手动方式：在本目录打开终端运行</span>
                            <span class="view-placeholder-meta mono-text" style="font-weight:600;color:#1F2937;">python -m http.server 8765</span>
                            <span class="view-placeholder-meta mono-text">然后访问 http://127.0.0.1:8765/</span>
                        </div>
                    </div>
                </div>
            `;
            if (window.lucide) lucide.createIcons({ root: mainContent });
            return;
        }

        mainContent.innerHTML = `
            <div class="view-placeholder">
                <div class="view-placeholder-card view-placeholder-card--error">
                    <i data-lucide="alert-triangle" class="view-placeholder-icon"></i>
                    <h2 class="view-placeholder-title">加载失败</h2>
                    <p class="view-placeholder-desc">${viewName} 视图无法加载</p>
                    <span class="view-placeholder-meta mono-text">${(err && err.message) || err}</span>
                </div>
            </div>
        `;
        if (window.lucide) lucide.createIcons({ root: mainContent });
    }

    /* ----------------- 卸载当前视图 ----------------- */
    function destroyCurrent() {
        if (!currentView) return;
        const destroyer = window.__VIEW_DESTROY__ && window.__VIEW_DESTROY__[currentView];
        if (typeof destroyer === 'function') {
            try { destroyer(); } catch (e) { console.warn('[view-loader] destroy err', e); }
        }
        currentView = null;
    }

    /* ----------------- 加载视图（核心） ----------------- */
    async function loadView(viewName) {
        const mainContent = document.getElementById('mainContent');
        if (!mainContent) return;

        // 1. 卸载旧视图
        destroyCurrent();

        const meta = VIEW_REGISTRY[viewName];

        // 2. 未注册 → 占位页
        if (!meta) {
            renderPlaceholder(mainContent, viewName);
            return;
        }

        // 3. 显示加载态（极短，避免闪屏）
        mainContent.classList.add('is-loading');

        try {
            // 4. 并行预热 CSS（不阻塞 HTML 注入）
            const cssTask = meta.css ? injectCSS(meta.css) : Promise.resolve();

            // 5. 拉 HTML
            const html = await fetchText(meta.html);

            // 6. 注入
            mainContent.innerHTML = html;

            // 7. 等 CSS 就绪
            await cssTask;

            // 8. 加载 JS（自注册到 __VIEW_INIT__）
            if (meta.js) await injectJS(meta.js);

            // 9. 重新生成图标
            if (window.lucide) lucide.createIcons({ root: mainContent });

            // 10. 调用视图 init
            const initFn = window.__VIEW_INIT__ && window.__VIEW_INIT__[viewName];
            if (typeof initFn === 'function') {
                try { initFn(mainContent); } catch (e) { console.error('[view-loader] init err', e); }
            }

            currentView = viewName;
        } catch (err) {
            console.error('[view-loader] 视图加载失败', viewName, err);
            renderError(mainContent, viewName, err);
        } finally {
            mainContent.classList.remove('is-loading');
        }
    }

    /* ----------------- 事件总线 ----------------- */
    document.addEventListener('view-changed', (e) => {
        const viewName = e.detail && e.detail.view;
        if (!viewName) return;
        loadView(viewName);
    });

    /* ----------------- 启动：默认加载 overview ----------------- */
    document.addEventListener('DOMContentLoaded', () => {
        // 等 side-bar 组件完成 connectedCallback 再加载
        setTimeout(() => loadView('overview'), 0);
    });

    // 暴露给外部（调试用）
    window.YuqiViewLoader = { loadView, VIEW_REGISTRY };
})();
