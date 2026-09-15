/* ==========================================================================
   备份管理 · 视图脚本
   --------------------------------------------------------------------------
   职责：
     1. 注册到 window.__VIEW_INIT__['backup-manager']
     2. 维护页面状态机：idle / creating / restoring-confirm / deleting-confirm
     3. 处理：创建备份（带进度模拟）、筛选、回滚确认、删除确认、刷新、清理
     4. 联动入口：跳转到系统还原点
     5. 通过 __VIEW_DESTROY__ 清理 timer / listener
   ========================================================================== */
(function () {
    'use strict';

    const VIEW_NAME = 'backup-manager';

    let state = {
        timers: [],
        boundClickEl: null,
        boundClickHandler: null,
        modalEscHandler: null,
        currentRoot: null,
    };

    /* ---------------- 工具 ---------------- */
    function $(root, sel) { return root.querySelector(sel); }
    function $$(root, sel) { return Array.from(root.querySelectorAll(sel)); }

    function clearTimers() {
        state.timers.forEach(t => clearTimeout(t));
        state.timers = [];
    }

    function setTimer(fn, ms) {
        const t = setTimeout(fn, ms);
        state.timers.push(t);
        return t;
    }

    function setPageState(root, st) {
        const page = $(root, '.bk-page');
        if (page) page.setAttribute('data-state', st);
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    /* ---------------- 创建备份（模拟） ---------------- */
    function startCreate(root) {
        const btn       = $(root, '#bkCreateBtn');
        const cancelBtn = $(root, '#bkCreateCancel');
        const progress  = $(root, '#bkCreateProgress');
        const fill      = $(root, '#bkProgressFill');
        const pct       = $(root, '#bkProgressPct');
        const txt       = $(root, '#bkProgressText');
        const input     = $(root, '#bkCreateDesc');
        if (!btn || !progress) return;

        setPageState(root, 'creating');
        btn.disabled = true;
        btn.style.opacity = '0.5';
        btn.style.pointerEvents = 'none';
        if (input) input.disabled = true;
        cancelBtn.hidden = false;
        progress.hidden = false;

        // 模拟分阶段进度（备份比还原点更轻量，预计 8 秒）
        const phases = [
            { to: 22,  ms: 400, text: '采集当前优化项清单…' },
            { to: 48,  ms: 600, text: '快照注册表关键键值…' },
            { to: 72,  ms: 700, text: '打包服务与启动项状态…' },
            { to: 92,  ms: 500, text: '生成备份元数据…' },
            { to: 100, ms: 300, text: '完成扫尾…' },
        ];

        let cur = 0;
        function runPhase(i) {
            if (i >= phases.length) {
                finishCreate(root, true);
                return;
            }
            const p = phases[i];
            txt.textContent = p.text;
            const from = cur;
            const to = p.to;
            const startMs = performance.now();
            function tick(now) {
                const elapsed = now - startMs;
                const ratio = Math.min(1, elapsed / p.ms);
                const v = Math.round(from + (to - from) * ratio);
                fill.style.width = v + '%';
                pct.textContent = v;
                cur = v;
                if (ratio < 1) {
                    requestAnimationFrame(tick);
                } else {
                    setTimer(() => runPhase(i + 1), 60);
                }
            }
            requestAnimationFrame(tick);
        }
        runPhase(0);
    }

    function finishCreate(root, success) {
        const btn       = $(root, '#bkCreateBtn');
        const cancelBtn = $(root, '#bkCreateCancel');
        const progress  = $(root, '#bkCreateProgress');
        const input     = $(root, '#bkCreateDesc');
        const desc      = (input && input.value.trim()) || '手动备份';

        if (success) {
            insertNewBackup(root, desc);
            const txt = $(root, '#bkProgressText');
            if (txt) txt.textContent = '✓ 备份创建成功';
        }

        setTimer(() => {
            setPageState(root, 'idle');
            btn.disabled = false;
            btn.style.opacity = '';
            btn.style.pointerEvents = '';
            if (input) input.disabled = false;
            cancelBtn.hidden = true;
            progress.hidden = true;
            const fill = $(root, '#bkProgressFill');
            const pct = $(root, '#bkProgressPct');
            if (fill) fill.style.width = '0%';
            if (pct) pct.textContent = '0';
        }, success ? 900 : 200);
    }

    function cancelCreate(root) {
        clearTimers();
        finishCreate(root, false);
    }

    /* ---------------- 在时间线插入新备份 ---------------- */
    function insertNewBackup(root, desc) {
        const tl = $(root, '#bkTimeline');
        if (!tl) return;

        // 取当前最大 id（排除出厂底片 #000）
        const items = $$(tl, '.bk-tl-item');
        const maxId = items.reduce((m, el) => {
            const n = parseInt(el.getAttribute('data-id'), 10);
            return isNaN(n) ? m : Math.max(m, n);
        }, 0);
        const newId = String(maxId + 1).padStart(3, '0');

        const now = new Date();
        const pad = n => String(n).padStart(2, '0');
        const absTime = `${now.getFullYear()}-${pad(now.getMonth()+1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
        const sizeMb = Math.round(30 + Math.random() * 40);

        const li = document.createElement('li');
        li.className = 'bk-tl-item';
        li.setAttribute('data-type', 'manual');
        li.setAttribute('data-id', newId);
        li.style.opacity = '0';
        li.style.transform = 'translateY(-8px)';
        li.innerHTML = `
            <span class="bk-tl-node" data-type="manual">
                <i data-lucide="user" class="bk-tl-node-icon"></i>
            </span>
            <div class="bk-tl-card">
                <header class="bk-tl-card-head">
                    <span class="bk-tl-time">
                        <span class="bk-tl-time-rel">刚刚</span>
                        <span class="bk-tl-time-abs mono-text">${absTime}</span>
                    </span>
                    <span class="bk-type-pill" data-type="manual">手动</span>
                </header>
                <div class="bk-tl-desc">${escapeHtml(desc)}</div>
                <div class="bk-tl-meta">
                    <span class="bk-tl-meta-cell mono-text"><i data-lucide="hash" class="icon-xs"></i>#${newId}</span>
                    <span class="bk-tl-meta-cell mono-text"><i data-lucide="database" class="icon-xs"></i>${sizeMb} MB</span>
                    <span class="bk-tl-meta-cell mono-text"><i data-lucide="check-circle-2" class="icon-xs"></i>已封存</span>
                </div>
                <div class="bk-tl-actions">
                    <button class="bk-row-btn bk-row-btn-restore" data-action="restore" data-id="${newId}">
                        <i data-lucide="rotate-ccw" class="icon-sm"></i><span>回滚到此点</span>
                    </button>
                    <button class="bk-row-btn" data-action="detail" data-id="${newId}">
                        <i data-lucide="info" class="icon-sm"></i><span>详情</span>
                    </button>
                    <button class="bk-row-btn bk-row-btn-danger" data-action="delete" data-id="${newId}">
                        <i data-lucide="trash-2" class="icon-sm"></i><span>删除</span>
                    </button>
                </div>
            </div>
        `;

        // 插入到出厂底片之后（保持出厂底片永远在最上）
        const factoryItem = tl.querySelector('.bk-tl-item--factory');
        if (factoryItem && factoryItem.nextElementSibling) {
            tl.insertBefore(li, factoryItem.nextElementSibling);
        } else if (factoryItem) {
            tl.appendChild(li);
        } else {
            tl.insertBefore(li, tl.firstElementChild);
        }

        if (window.lucide) window.lucide.createIcons({ root: li });

        requestAnimationFrame(() => {
            li.style.transition = 'all 0.4s cubic-bezier(0.4, 0, 0.2, 1)';
            li.style.opacity = '1';
            li.style.transform = 'translateY(0)';
        });

        // 更新筛选计数（手动备份 +1，全部 +1）
        updateAllCounts(root);
    }

    /* ---------------- 计数更新 ---------------- */
    function updateAllCounts(root) {
        const tl = $(root, '#bkTimeline');
        if (!tl) return;
        const items = $$(tl, '.bk-tl-item');
        const total = items.length;
        const byType = { factory: 0, auto: 0, manual: 0, config: 0 };
        items.forEach(it => {
            const t = it.getAttribute('data-type');
            if (t in byType) byType[t]++;
        });

        // 头部总数
        const totalCnt = $(root, '#bkTotalCount');
        if (totalCnt) totalCnt.textContent = total;

        // 各分类计数
        const setCount = (filter, n) => {
            const btn = root.querySelector(`.bk-filter-btn[data-filter="${filter}"] .bk-filter-count`);
            if (btn) btn.textContent = n;
        };
        setCount('all', total);
        setCount('factory', byType.factory);
        setCount('auto', byType.auto);
        setCount('manual', byType.manual);
        setCount('config', byType.config);
    }

    /* ---------------- 筛选 ---------------- */
    function applyFilter(root, cat) {
        const items = $$($(root, '#bkTimeline'), '.bk-tl-item');
        items.forEach(it => {
            const t = it.getAttribute('data-type');
            const visible = (cat === 'all') || (t === cat);
            it.setAttribute('data-hidden', visible ? 'false' : 'true');
        });
    }

    /* ---------------- Modal（回滚/删除确认） ---------------- */
    function openModal(root, kind, id) {
        const modal = $(root, '#bkModal');
        const mask  = $(root, '#bkModalMask');
        const head  = $(root, '#bkModalHead');
        const title = $(root, '#bkModalTitle');
        const sub   = $(root, '#bkModalSub');
        const icon  = $(root, '#bkModalIcon');
        const targetName = $(root, '#bkModalTargetName');
        const targetMeta = $(root, '#bkModalTargetMeta');
        const warns = $(root, '#bkModalWarns');
        const confirmBtn = $(root, '#bkModalConfirm');
        if (!modal) return;

        const item = $$(root, `.bk-tl-item[data-id="${id}"]`)[0];
        if (!item) return;
        const desc = item.querySelector('.bk-tl-desc')?.textContent.trim() || '';
        const abs  = item.querySelector('.bk-tl-time-abs')?.textContent.trim() || '';
        const sizeCell = item.querySelectorAll('.bk-tl-meta-cell')[1];
        const size = sizeCell ? sizeCell.textContent.trim() : '';
        const type = item.getAttribute('data-type');
        const isFactory = type === 'factory';

        // 设置 modal 整体 kind（影响主按钮配色）
        modal.setAttribute('data-kind', kind);

        if (kind === 'restore') {
            head.setAttribute('data-kind', 'restore');
            icon.setAttribute('data-lucide', isFactory ? 'archive-restore' : 'rotate-ccw');
            title.textContent = isFactory ? '确认恢复出厂状态' : '确认回滚到此备份';
            sub.textContent = isFactory
                ? '所有优化项将被还原到首次启动时的状态'
                : '软件将关闭并回到该备份时刻的状态';
            confirmBtn.innerHTML = `<i data-lucide="rotate-ccw" class="bk-btn-icon"></i><span>${isFactory ? '确认恢复出厂' : '确认回滚'}</span>`;

            if (isFactory) {
                warns.innerHTML = `
                    <li><i data-lucide="alert-triangle" class="icon-xs"></i><span>该操作 <b>不可撤销</b>，会清除所有手动/自动备份后的更改</span></li>
                    <li><i data-lucide="shield" class="icon-xs"></i><span>建议同时创建一个 <b>系统还原点</b> 作为双保险</span></li>
                    <li><i data-lucide="file-check" class="icon-xs"></i><span>个人文件 <b>不会受影响</b>，仅回滚优化项与软件配置</span></li>
                `;
            } else {
                warns.innerHTML = `
                    <li><i data-lucide="alert-triangle" class="icon-xs"></i><span>该操作 <b>不可撤销</b>，请确认后继续</span></li>
                    <li><i data-lucide="rotate-ccw" class="icon-xs"></i><span>软件将自动重启，预计耗时 <b>20 秒</b></span></li>
                    <li><i data-lucide="file-check" class="icon-xs"></i><span>个人文件 <b>不会受影响</b>，仅回滚优化项状态</span></li>
                `;
            }
        } else if (kind === 'delete') {
            if (isFactory) {
                // 出厂底片不可删除：直接 toast 提示
                showToast(root, '出厂底片不可删除');
                return;
            }
            head.setAttribute('data-kind', 'delete');
            icon.setAttribute('data-lucide', 'trash-2');
            title.textContent = '确认删除该备份';
            sub.textContent = '删除后无法恢复';
            confirmBtn.innerHTML = `<i data-lucide="trash-2" class="bk-btn-icon"></i><span>确认删除</span>`;
            warns.innerHTML = `
                <li><i data-lucide="alert-triangle" class="icon-xs"></i><span>删除后 <b>无法恢复</b> 该备份</span></li>
                <li><i data-lucide="database" class="icon-xs"></i><span>将释放 <b>${size.replace(/[^\d.A-Za-z ]/g, '').trim() || '-'}</b> 磁盘空间</span></li>
                <li><i data-lucide="info" class="icon-xs"></i><span>不会影响其他备份和当前优化状态</span></li>
            `;
        }

        targetName.textContent = `#${id} · ${desc}`;
        targetMeta.textContent = `${abs} · ${size}`;
        confirmBtn.setAttribute('data-pending-id', id);
        confirmBtn.setAttribute('data-pending-kind', kind);

        modal.hidden = false;
        mask.hidden = false;
        requestAnimationFrame(() => {
            modal.classList.add('is-open');
            mask.classList.add('is-open');
        });

        if (window.lucide) window.lucide.createIcons({ root: modal });

        state.modalEscHandler = (e) => {
            if (e.key === 'Escape') closeModal(root);
        };
        document.addEventListener('keydown', state.modalEscHandler);
    }

    function closeModal(root) {
        const modal = $(root, '#bkModal');
        const mask  = $(root, '#bkModalMask');
        if (!modal) return;
        modal.classList.remove('is-open');
        mask.classList.remove('is-open');
        setTimer(() => {
            modal.hidden = true;
            mask.hidden = true;
        }, 250);

        if (state.modalEscHandler) {
            document.removeEventListener('keydown', state.modalEscHandler);
            state.modalEscHandler = null;
        }
    }

    function modalConfirm(root) {
        const confirmBtn = $(root, '#bkModalConfirm');
        if (!confirmBtn) return;
        const id = confirmBtn.getAttribute('data-pending-id');
        const kind = confirmBtn.getAttribute('data-pending-kind');
        closeModal(root);

        if (kind === 'delete') {
            const item = $$(root, `.bk-tl-item[data-id="${id}"]`)[0];
            if (item) {
                item.style.transition = 'all 0.3s ease';
                item.style.opacity = '0';
                item.style.transform = 'translateX(-12px)';
                setTimer(() => {
                    item.remove();
                    updateAllCounts(root);
                    showToast(root, `已删除备份 #${id}`);
                }, 320);
            }
        } else if (kind === 'restore') {
            const item = $$(root, `.bk-tl-item[data-id="${id}"]`)[0];
            const isFactory = item && item.getAttribute('data-type') === 'factory';
            showToast(root, isFactory
                ? `已下发恢复出厂指令 · 备份 #${id}（演示）`
                : `已下发回滚指令 · 备份 #${id}（演示）`);
        }
    }

    /* ---------------- Toast ---------------- */
    function showToast(root, text) {
        let toast = document.getElementById('bkToast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'bkToast';
            toast.style.cssText = `
                position: fixed;
                bottom: 32px;
                left: 50%;
                transform: translateX(-50%) translateY(20px);
                padding: 10px 18px;
                background: rgba(26, 29, 36, 0.92);
                color: #fff;
                font-size: 12.5px;
                font-weight: 600;
                border-radius: 10px;
                box-shadow: 0 8px 24px rgba(0,0,0,0.25);
                z-index: 1100;
                opacity: 0;
                transition: all 0.25s ease;
                pointer-events: none;
                backdrop-filter: blur(8px);
            `;
            document.body.appendChild(toast);
        }
        toast.textContent = text;
        requestAnimationFrame(() => {
            toast.style.opacity = '1';
            toast.style.transform = 'translateX(-50%) translateY(0)';
        });
        setTimer(() => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateX(-50%) translateY(20px)';
        }, 2200);
    }

    /* ---------------- 联动跳转：系统还原点 ---------------- */
    function jumpToRestorePoint(root) {
        // 通过 sidebar 的 click 事件触发标准路由切换
        const sideBar = document.querySelector('side-bar');
        const navItem = sideBar?.querySelector('.nav-item[data-view="restore-point"]');
        if (navItem) {
            navItem.click();
        } else {
            // 兜底：直接派发 view-changed
            document.dispatchEvent(new CustomEvent('view-changed', {
                detail: { view: 'restore-point' },
                bubbles: true
            }));
        }
    }

    /* ---------------- 输入框计数 ---------------- */
    function bindInputCounter(root) {
        const input = $(root, '#bkCreateDesc');
        const len = $(root, '#bkInputLen');
        if (!input || !len) return;
        const update = () => { len.textContent = input.value.length; };
        update();
        input.addEventListener('input', update);
    }

    /* ---------------- 主初始化 ---------------- */
    function init(root) {
        state.currentRoot = root;
        clearTimers();

        bindInputCounter(root);

        const handler = (e) => {
            const target = e.target.closest('[data-action], [data-filter]');
            if (!target || !root.contains(target)) return;

            // 筛选
            const filter = target.getAttribute('data-filter');
            if (filter) {
                $$(root, '.bk-filter-btn').forEach(b => b.classList.remove('is-active'));
                target.classList.add('is-active');
                applyFilter(root, filter);
                return;
            }

            // 行为
            const action = target.getAttribute('data-action');
            const id = target.getAttribute('data-id');
            switch (action) {
                case 'create-backup':
                    startCreate(root);
                    break;
                case 'cancel-create':
                    cancelCreate(root);
                    break;
                case 'restore':
                    openModal(root, 'restore', id);
                    break;
                case 'delete':
                    openModal(root, 'delete', id);
                    break;
                case 'detail':
                    showToast(root, `查看备份 #${id} 详情（演示）`);
                    break;
                case 'export':
                    showToast(root, `导出配置 #${id}（演示）`);
                    break;
                case 'modal-cancel':
                    closeModal(root);
                    break;
                case 'modal-confirm':
                    modalConfirm(root);
                    break;
                case 'refresh-list':
                    showToast(root, '已刷新备份列表');
                    break;
                case 'cleanup-old':
                    showToast(root, '正在清理旧备份（演示）…');
                    break;
                case 'open-policy':
                    showToast(root, '打开高级策略配置（演示）');
                    break;
                case 'open-restore-point':
                    jumpToRestorePoint(root);
                    break;
                case 'toggle-policy':
                    // 仅本地反馈
                    break;
            }
        };
        root.addEventListener('click', handler);
        state.boundClickEl = root;
        state.boundClickHandler = handler;

        // mask 点击关闭 modal
        const mask = $(root, '#bkModalMask');
        if (mask) {
            mask.addEventListener('click', () => closeModal(root));
        }

        updateAllCounts(root);
    }

    /* ---------------- 销毁 ---------------- */
    function destroy() {
        clearTimers();
        if (state.boundClickEl && state.boundClickHandler) {
            state.boundClickEl.removeEventListener('click', state.boundClickHandler);
            state.boundClickEl = null;
            state.boundClickHandler = null;
        }
        if (state.modalEscHandler) {
            document.removeEventListener('keydown', state.modalEscHandler);
            state.modalEscHandler = null;
        }
        const toast = document.getElementById('bkToast');
        if (toast) toast.remove();
        state.currentRoot = null;
    }

    /* ---------------- 注册 ---------------- */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_INIT__[VIEW_NAME] = init;
    window.__VIEW_DESTROY__[VIEW_NAME] = destroy;
})();
