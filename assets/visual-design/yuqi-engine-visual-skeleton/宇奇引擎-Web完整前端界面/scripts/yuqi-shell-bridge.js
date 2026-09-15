(function () {
    'use strict';

    const hasHost = Boolean(window.chrome && window.chrome.webview);
    if (hasHost) {
        window.YuqiMigratedViews = window.YuqiMigratedViews || ['overview'];
    }

    function post(type, payload) {
        const message = {
            type,
            payload: payload || {},
            timestamp: Date.now()
        };

        if (hasHost) {
            window.chrome.webview.postMessage(JSON.stringify(message));
        } else {
            window.dispatchEvent(new CustomEvent('yuqi-shell-debug-message', { detail: message }));
            console.debug('[yuqi-shell-bridge]', message);
        }
    }

    function setText(selector, value) {
        if (value === undefined || value === null) return;
        const node = document.querySelector(selector);
        if (node) node.textContent = String(value);
    }

    function setActiveView(view) {
        if (!view) return;
        const sideBar = document.querySelector('side-bar');
        const root = sideBar || document;
        const sidebarNav = root.querySelector('#sidebarNav');
        const activeBg = root.querySelector('#navActiveBg');
        const items = Array.from(root.querySelectorAll('.nav-item'));
        const target = items.find(item => item.getAttribute('data-view') === view);
        if (!target) return;

        items.forEach(item => item.classList.remove('active'));
        target.classList.add('active');

        if (activeBg && sidebarNav) {
            const itemRect = target.getBoundingClientRect();
            const navRect = sidebarNav.getBoundingClientRect();
            activeBg.style.transform = `translateY(${itemRect.top - navRect.top}px)`;
            activeBg.style.opacity = '1';
        }
    }

    function applyState(state) {
        if (!state || typeof state !== 'object') return;

        setText('.telemetry-item:nth-child(1) .telemetry-value', state.cpuText);
        setText('.telemetry-item:nth-child(2) .telemetry-value', state.memoryText);

        if (state.statusKind && window.setAppStatus) {
            window.setAppStatus(state.statusKind, state.statusText);
        } else if (state.statusText) {
            setText('#appStatus .status-text', state.statusText);
        }

        if (state.versionText) {
            setText('.admin-version', state.versionText);
        }

        if (state.adminText) {
            setText('.admin-title', state.adminText);
        }

        if (state.activeView) {
            setActiveView(state.activeView);
            if ((!Array.isArray(window.YuqiMigratedViews) || window.YuqiMigratedViews.includes(state.activeView)) &&
                window.YuqiViewLoader &&
                typeof window.YuqiViewLoader.loadView === 'function') {
                window.YuqiViewLoader.loadView(state.activeView);
            }
        }
    }

    function validateRegionRender() {
        if (document.body.classList.contains('sidebar-region')) {
            return document.querySelectorAll('side-bar .nav-item').length > 0
                ? null
                : 'sidebar navigation was not rendered';
        }

        if (document.body.classList.contains('titlebar-region')) {
            return document.querySelector('.titlebar')
                ? null
                : 'title bar was not rendered';
        }

        return null;
    }

    document.addEventListener('view-changed', (event) => {
        const view = event && event.detail && event.detail.view;
        if (!view) return;
        post('navigate', { view });
    }, true);

    document.addEventListener('click', (event) => {
        const button = event.target && event.target.closest && event.target.closest('[data-action]');
        if (!button) return;

        const action = button.getAttribute('data-action');
        if (!action) return;

        if (['minimize', 'maximize', 'close', 'about', 'feedback', 'github'].includes(action)) {
            event.preventDefault();
            post('window-action', { action });
        }
    }, true);

    document.addEventListener('pointerdown', (event) => {
        if (event.button !== 0) return;
        if (event.target && event.target.closest && event.target.closest('button, a, input, textarea, select')) return;

        const border = 8;
        const x = event.clientX;
        const y = event.clientY;
        const nearLeft = x <= border;
        const nearRight = x >= window.innerWidth - border;
        const nearTop = y <= border;

        let action = null;
        if (nearTop && nearLeft) action = 'resize-top-left';
        else if (nearTop && nearRight) action = 'resize-top-right';
        else if (nearTop) action = 'resize-top';
        else if (nearLeft) action = 'resize-left';
        else if (nearRight) action = 'resize-right';

        if (action) {
            event.preventDefault();
            post('window-action', { action });
        }
    }, true);

    if (hasHost) {
        window.chrome.webview.addEventListener('message', (event) => {
            let message = event.data;
            if (typeof message === 'string') {
                try {
                    message = JSON.parse(message);
                } catch {
                    return;
                }
            }

            if (!message || typeof message !== 'object') return;

            if (message.type === 'state') {
                applyState(message.payload);
            }

            if (message.type === 'navigate') {
                setActiveView(message.payload && message.payload.view);
            }
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        requestAnimationFrame(() => {
            const renderError = validateRegionRender();
            if (renderError) {
                post('render-error', {
                    reason: renderError,
                    href: location.href
                });
                return;
            }

            post('ready', {
                userAgent: navigator.userAgent,
                href: location.href
            });
        });
    });

    window.YuqiShellBridge = {
        post,
        applyState,
        setActiveView
    };
})();
