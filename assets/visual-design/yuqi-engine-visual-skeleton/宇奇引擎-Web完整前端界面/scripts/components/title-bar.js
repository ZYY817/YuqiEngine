class TitleBar extends HTMLElement {
    connectedCallback() {
        this.innerHTML = `
            <header class="titlebar">
                <!-- 左侧：Logo + 三段式标题（极简墨色派：Y 与 Yuqi 全部使用 ink 主色） -->
                <div class="titlebar-left">
                    <img class="logo-icon" src="assets/yq_icon.ico" alt="宇奇引擎" draggable="false">
                    <span class="brand-text">
                        <span class="brand-cn">宇奇引擎</span>
                        <span class="brand-accent">Yuqi</span>
                        <span class="brand-secondary">Engine</span>
                    </span>
                </div>

                <!-- 右侧：遥测 + 状态 + 分隔线 + 三个动作按钮 + 三个窗口控制 -->
                <div class="titlebar-right">
                    <!-- 遥测数据：CPU / MEM (来自 WPF TitleBarTelemetry) -->
                    <div class="telemetry-group">
                        <div class="telemetry-item">
                            <span class="telemetry-label mono-text">CPU</span>
                            <span class="telemetry-value mono-text">12%</span>
                        </div>
                        <div class="telemetry-item">
                            <span class="telemetry-label mono-text">MEM</span>
                            <span class="telemetry-value mono-text">45%</span>
                        </div>
                    </div>

                    <!-- 动态状态：四档（idle / running / done / warn），默认空闲态"就绪" -->
                    <div class="status-caption" data-status="idle" id="appStatus">
                        <span class="status-dot"></span>
                        <span class="status-text">就绪</span>
                    </div>

                    <!-- 分隔线 (复刻 WPF: 1×16px 发丝线) -->
                    <div class="titlebar-separator"></div>

                    <!-- 三个动作图标按钮：关于 / 反馈 / GitHub / 通知 -->
                    <div class="action-controls">
                        <button class="action-btn" title="关于作者" data-action="about">
                            <i data-lucide="user" class="icon-sm"></i>
                        </button>
                        <button class="action-btn" title="反馈问题" data-action="feedback">
                            <i data-lucide="message-square" class="icon-sm"></i>
                        </button>
                        <button class="action-btn" title="访问项目主页" data-action="github">
                            <!-- GitHub 官方 Mark 路径，独立 SVG，不依赖第三方图标库 -->
                            <svg class="icon-github" viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
                                <path fill="currentColor" fill-rule="evenodd" d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0 0 16 8c0-4.42-3.58-8-8-8z"/>
                            </svg>
                        </button>
                        <!-- 参数预览按钮 -->
                        <button class="action-btn" type="button" data-action="param-toggle" title="当前生效参数">
                            <i data-lucide="settings" class="icon-sm"></i>
                        </button>
                        <button class="action-btn action-btn-notify" title="通知" data-action="notify-toggle">
                            <i data-lucide="bell" class="icon-sm"></i>
                            <span class="notify-bell-dot" aria-hidden="true"></span>
                        </button>
                    </div>

                    <!-- 三个窗口控制按钮 (复刻 WPF：粗横线 / 空心方框 / X) -->
                    <div class="window-controls">
                        <button class="control-btn" title="最小化" data-action="minimize">
                            <span class="ctrl-glyph ctrl-minimize"></span>
                        </button>
                        <button class="control-btn" title="最大化" data-action="maximize">
                            <span class="ctrl-glyph ctrl-maximize"></span>
                        </button>
                        <button class="control-btn close-btn" title="关闭" data-action="close">
                            <svg class="ctrl-close-svg" viewBox="0 0 14 14" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round">
                                <path d="M 1,1 L 13,13 M 13,1 L 1,13"/>
                            </svg>
                        </button>
                    </div>
                </div>
            </header>
        `;

        // 如果 lucide 已经加载，重新渲染图标（GitHub 已用原生 SVG，不再依赖 Lucide）
        if (window.lucide) {
            window.lucide.createIcons({ root: this });
        }

        const titlebar = this.querySelector('.titlebar');
        titlebar?.addEventListener('pointerdown', (event) => {
            if (event.button !== 0) return;
            if (event.target.closest('button, a, input, textarea, select, [data-action]')) return;
            event.preventDefault();
            window.YuqiShellBridge?.post?.('window-action', { action: 'drag' });
        });

        titlebar?.addEventListener('dblclick', (event) => {
            if (event.target.closest('button, a, input, textarea, select, [data-action]')) return;
            event.preventDefault();
            window.YuqiShellBridge?.post?.('window-action', { action: 'maximize' });
        });
    }
}

customElements.define('title-bar', TitleBar);

/**
 * 全局状态切换 API：window.setAppStatus(status, text?)
 *   status: 'idle' | 'running' | 'done' | 'warn'
 *   text:   可选自定义文案，不传则使用默认文案
 */
window.setAppStatus = function (status, text) {
    const el = document.getElementById('appStatus');
    if (!el) return;
    const map = {
        idle:    '就绪',
        running: '优化中',
        done:    '已完成',
        warn:    '检测到异常'
    };
    el.setAttribute('data-status', status);
    const t = el.querySelector('.status-text');
    if (t) t.textContent = text || map[status] || '就绪';
};
