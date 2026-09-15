class SideBar extends HTMLElement {
    connectedCallback() {
        this.innerHTML = `
            <aside class="sidebar">
                <nav class="sidebar-nav" id="sidebarNav">
                    <!-- 阻尼滑动指示器背景 -->
                    <div class="nav-active-bg" id="navActiveBg">
                        <div class="nav-active-glow"></div>
                    </div>
                    
                    <!-- 监控与概览 -->
                    <div class="nav-group">
                        <div class="nav-group-title">监控</div>
                        <a href="#" class="nav-item active" data-index="0" data-view="overview">
                            <i data-lucide="layout-dashboard" class="nav-icon"></i>
                            <span class="nav-label">系统概览</span>
                        </a>
                    </div>

                    <!-- 优化模块 -->
                    <div class="nav-group">
                        <div class="nav-group-title">模块</div>
                        <a href="#" class="nav-item" data-index="1" data-view="memory-optimization">
                            <i data-lucide="memory-stick" class="nav-icon"></i>
                            <span class="nav-label">内存优化</span>
                        </a>
                        <a href="#" class="nav-item" data-index="2" data-view="audio-optimization">
                            <i data-lucide="speaker" class="nav-icon"></i>
                            <span class="nav-label">音频优化</span>
                        </a>
                        <a href="#" class="nav-item" data-index="3" data-view="performance-boost">
                            <i data-lucide="rocket" class="nav-icon"></i>
                            <span class="nav-label">性能加速</span>
                        </a>
                        <a href="#" class="nav-item" data-index="4" data-view="cpu-nav">
                            <i data-lucide="cpu" class="nav-icon"></i>
                            <span class="nav-label">CPU 优化</span>
                        </a>
                        <a href="#" class="nav-item" data-index="5" data-view="graphics-nav">
                            <i data-lucide="monitor" class="nav-icon"></i>
                            <span class="nav-label">显卡优化</span>
                        </a>
                        <a href="#" class="nav-item" data-index="6" data-view="network-nav">
                            <i data-lucide="network" class="nav-icon"></i>
                            <span class="nav-label">网络优化</span>
                        </a>
                        <a href="#" class="nav-item" data-index="7" data-view="privacy-nav">
                            <i data-lucide="shield" class="nav-icon"></i>
                            <span class="nav-label">隐私保护</span>
                        </a>
                        <a href="#" class="nav-item" data-index="8" data-view="system-nav">
                            <i data-lucide="settings-2" class="nav-icon"></i>
                            <span class="nav-label">系统调校</span>
                        </a>
                        <a href="#" class="nav-item" data-index="9" data-view="explorer-nav">
                            <i data-lucide="layout" class="nav-icon"></i>
                            <span class="nav-label">桌面体验</span>
                        </a>
                        <a href="#" class="nav-item" data-index="10" data-view="tasks-nav">
                            <i data-lucide="calendar-clock" class="nav-icon"></i>
                            <span class="nav-label">任务调度</span>
                        </a>
                        <a href="#" class="nav-item" data-index="11" data-view="services-nav">
                            <i data-lucide="server-cog" class="nav-icon"></i>
                            <span class="nav-label">系统服务</span>
                        </a>
                        <a href="#" class="nav-item" data-index="12" data-view="system-cleanup">
                            <i data-lucide="trash-2" class="nav-icon"></i>
                            <span class="nav-label">系统清理</span>
                        </a>
                        <a href="#" class="nav-item" data-index="13" data-view="peripheral-nav">
                            <i data-lucide="mouse" class="nav-icon"></i>
                            <span class="nav-label">外设优化</span>
                        </a>
                    </div>

                    <!-- 工具与维护 -->
                    <div class="nav-group">
                        <div class="nav-group-title">工具</div>
                        <a href="#" class="nav-item" data-index="14" data-view="hardware-info">
                            <i data-lucide="hard-drive" class="nav-icon"></i>
                            <span class="nav-label">硬件信息</span>
                        </a>
                        <a href="#" class="nav-item" data-index="15" data-view="network-test">
                            <i data-lucide="wifi" class="nav-icon"></i>
                            <span class="nav-label">网络测试</span>
                        </a>
                        <a href="#" class="nav-item" data-index="16" data-view="restore-point">
                            <i data-lucide="history" class="nav-icon"></i>
                            <span class="nav-label">还原点</span>
                        </a>
                        <a href="#" class="nav-item" data-index="17" data-view="repair-center">
                            <i data-lucide="wrench" class="nav-icon"></i>
                            <span class="nav-label">修复中心</span>
                        </a>
                        <a href="#" class="nav-item" data-index="18" data-view="backup-manager">
                            <i data-lucide="save" class="nav-icon"></i>
                            <span class="nav-label">备份管理</span>
                        </a>
                        <a href="#" class="nav-item" data-index="19" data-view="cpu-core-management">
                            <i data-lucide="microchip" class="nav-icon"></i>
                            <span class="nav-label">CPU 核心调度</span>
                        </a>
                        <a href="#" class="nav-item" data-index="20" data-view="power-plan-editor">
                            <i data-lucide="battery-charging" class="nav-icon"></i>
                            <span class="nav-label">电源优化</span>
                        </a>
                        <a href="#" class="nav-item" data-index="21" data-view="startup-manager">
                            <i data-lucide="power" class="nav-icon"></i>
                            <span class="nav-label">启动项管理</span>
                        </a>
                    </div>

                    <!-- 设置 -->
                    <div class="nav-group">
                        <div class="nav-group-title">设置</div>
                        <a href="#" class="nav-item" data-index="22" data-view="settings">
                            <i data-lucide="settings" class="nav-icon"></i>
                            <span class="nav-label">软件设置</span>
                        </a>
                    </div>
                </nav>
                
                <div class="sidebar-footer">
                    <div class="admin-status">
                        <div class="admin-icon-wrapper">
                            <i data-lucide="shield-check" class="admin-icon"></i>
                            <div class="status-dot-green"></div>
                        </div>
                        <div class="admin-text">
                            <span class="admin-title">普通模式运行中</span>
                            <span class="admin-version">v0.11.0</span>
                        </div>
                        <div class="admin-actions">
                            <button class="action-btn heart-btn" title="支持作者">
                                <i data-lucide="heart" class="icon-xs"></i>
                            </button>
                            <button class="action-btn gear-btn" title="设置">
                                <i data-lucide="settings" class="icon-xs"></i>
                            </button>
                        </div>
                    </div>
                </div>
            </aside>
        `;

        if (window.lucide) {
            window.lucide.createIcons({ root: this });
        }

        this.initNavLogic();
    }

    initNavLogic() {
        const navItems = this.querySelectorAll('.nav-item');
        const activeBg = this.querySelector('#navActiveBg');
        const sidebarNav = this.querySelector('#sidebarNav');

        // ========== 定位算法（终极简洁版）==========
        // 前置条件（CSS 配合）：
        //   .nav-active-bg 显式设置 top: 0，把 static position 锁死为内容顶端 0。
        //   这样浮层"未平移"时严格位于 sidebar-nav 内容坐标系的 (left:10, top:0) 处。
        //
        // 公式：translateY = itemRect.top - navRect.top + sidebarNav.scrollTop
        //   含义："目标 item 在 sidebar-nav 内容坐标系（未滚动）中的纵向偏移"
        //   - itemRect.top - navRect.top：item 相对容器视口顶端的距离（受滚动影响）
        //   - + scrollTop：还原滚动量，得到 item 在内容坐标系的真实纵向 offset
        //   浮层 translateY 这个值后，正好对齐到 item，无论容器是否滚动、滚到哪儿。
        //
        // 该公式优势：
        //   1) 完全不依赖浮层自身的 getBoundingClientRect，绕开 transition 中间帧坑
        //   2) 完全不依赖 offsetTop/offsetParent，绕开 flex absolute static position 推断坑
        //   3) 视口坐标系 + 显式 scrollTop，跨浏览器行为完全一致
        const DEBUG = false; // 需要诊断时改为 true，会在控制台打印关键数值
        const updateActiveBg = (targetItem) => {
            if (!targetItem || !activeBg || !sidebarNav) return;

            const itemRect = targetItem.getBoundingClientRect();
            const navRect = sidebarNav.getBoundingClientRect();
            const scrollTop = sidebarNav.scrollTop;

            const delta = itemRect.top - navRect.top + scrollTop;

            if (DEBUG) {
                console.log('[side-bar] updateActiveBg', {
                    label: targetItem.querySelector('.nav-label')?.textContent?.trim(),
                    itemTop: itemRect.top,
                    navTop: navRect.top,
                    scrollTop,
                    delta,
                });
            }

            activeBg.style.transform = `translateY(${delta}px)`;
            activeBg.style.opacity = '1';
        };

        // 取当前 active 项并刷新浮层位置（仅在尺寸变化等会改变 offsetTop 的场景调用）
        const refreshActiveBg = () => {
            const active = this.querySelector('.nav-item.active');
            if (active) updateActiveBg(active);
        };

        // 初始化时等待首帧布局完成，确保 offsetTop 已可用
        const initialActive = this.querySelector('.nav-item.active');
        if (initialActive) {
            requestAnimationFrame(() => updateActiveBg(initialActive));
        }

        navItems.forEach(item => {
            item.addEventListener('click', (e) => {
                e.preventDefault();

                navItems.forEach(nav => nav.classList.remove('active'));
                item.classList.add('active');
                updateActiveBg(item);

                // 触发自定义事件，通知主内容区切换视图
                const viewName = item.getAttribute('data-view');
                this.dispatchEvent(new CustomEvent('view-changed', {
                    detail: { view: viewName },
                    bubbles: true,
                    composed: true
                }));
            });
        });

        // 注意：不监听 sidebarNav 的 scroll 事件 —— 浮层和 nav-item 同属
        // 一个 position:relative 滚动容器内的 absolute 子元素，会自动跟随滚动，
        // 一旦在 scroll 时去"修正"反而会导致浮层和菜单项错位。

        // 仅在窗口尺寸变化时刷新（item 的 offsetTop 可能因布局重排变化）
        window.addEventListener('resize', refreshActiveBg);
    }
}

customElements.define('side-bar', SideBar);
