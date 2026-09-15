/* ==========================================================================
   系统还原点 · 视图脚本
   --------------------------------------------------------------------------
   职责：
     1. 注册到 window.__VIEW_INIT__['restore-point']
     2. 维护页面状态机：idle / creating / restoring-confirm / deleting-confirm
     3. 处理：创建还原点（带进度模拟）、筛选、还原确认、删除确认、刷新
     4. 通过 __VIEW_DESTROY__ 清理 timer / listener
   ========================================================================== */
(function () {
    'use strict';

    const VIEW_NAME = 'restore-point';

    // 视图私有状态（每次 init 重置）
    let state = {
        timers: [],
        boundDocHandler: null,
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

    /* ---------------- 状态切换 ---------------- */
    function setPageState(root, st) {
        const page = $(root, '.rp-page');
        if (page) page.setAttribute('data-state', st);
    }

    /* ---------------- 创建还原点（模拟） ---------------- */
    function startCreate(root) {
        const btn       = $(root, '#rpCreateBtn');
        const cancelBtn = $(root, '#rpCreateCancel');
        const progress  = $(root, '#rpCreateProgress');
        const fill      = $(root, '#rpProgressFill');
        const pct       = $(root, '#rpProgressPct');
        const txt       = $(root, '#rpProgressText');
        const input     = $(root, '#rpCreateDesc');
        if (!btn || !progress) return;

        // 进入 creating 态
        setPageState(root, 'creating');
        btn.disabled = true;
        btn.style.opacity = '0.5';
        btn.style.pointerEvents = 'none';
        if (input) input.disabled = true;
        cancelBtn.hidden = false;
        progress.hidden = false;

        // 模拟分阶段进度
        const phases = [
            { to: 18,  ms: 600,  text: '检查系统保护服务…' },
            { to: 42,  ms: 1100, text: '挂起卷影副本（VSS）…' },
            { to: 70,  ms: 1300, text: '写入还原点元数据…' },
            { to: 92,  ms: 900,  text: '同步注册表快照…' },
            { to: 100, ms: 500,  text: '完成扫尾…' },
        ];

        let cur = 0;
        function runPhase(i) {
            if (i >= phases.length) {
                finishCreate(root, true);
                return;
            }
            const p = phases[i];
            txt.textContent = p.text;
            // 平滑插值
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
                    setTimer(() => runPhase(i + 1), 80);
                }
            }
            requestAnimationFrame(tick);
        }
        runPhase(0);
    }

    function finishCreate(root, success) {
        const btn       = $(root, '#rpCreateBtn');
        const cancelBtn = $(root, '#rpCreateCancel');
        const progress  = $(root, '#rpCreateProgress');
        const input     = $(root, '#rpCreateDesc');
        const desc      = (input && input.value.trim()) || '手动还原点';

        if (success) {
            // 在时间线最前面插入新条目
            insertNewRestorePoint(root, desc);
            // 反馈：按钮变成"已创建"短暂闪烁
            const txt = $(root, '#rpProgressText');
            if (txt) txt.textContent = '✓ 还原点创建成功';
        }

        setTimer(() => {
            // 复位
            setPageState(root, 'idle');
            btn.disabled = false;
            btn.style.opacity = '';
            btn.style.pointerEvents = '';
            if (input) input.disabled = false;
            cancelBtn.hidden = true;
            progress.hidden = true;
            const fill = $(root, '#rpProgressFill');
            const pct = $(root, '#rpProgressPct');
            if (fill) fill.style.width = '0%';
            if (pct) pct.textContent = '0';
        }, success ? 900 : 200);
    }

    function cancelCreate(root) {
        clearTimers();
        finishCreate(root, false);
    }

    /* ---------------- 在时间线插入新还原点 ---------------- */
    function insertNewRestorePoint(root, desc) {
        const tl = $(root, '#rpTimeline');
        if (!tl) return;

        // 计算下一个 id
        const items = $$(tl, '.rp-tl-item');
        const maxId = items.reduce((m, el) => {
            const n = parseInt(el.getAttribute('data-id'), 10);
            return isNaN(n) ? m : Math.max(m, n);
        }, 0);
        const newId = String(maxId + 1).padStart(3, '0');

        const now = new Date();
        const pad = n => String(n).padStart(2, '0');
        const absTime = `${now.getFullYear()}-${pad(now.getMonth()+1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
        const relTime = `刚刚`;
        const sizeMb = Math.round(500 + Math.random() * 400);

        const li = document.createElement('li');
        li.className = 'rp-tl-item';
        li.setAttribute('data-type', 'manual');
        li.setAttribute('data-id', newId);
        li.style.opacity = '0';
        li.style.transform = 'translateY(-8px)';
        li.innerHTML = `
            <span class="rp-tl-node" data-type="manual">
                <i data-lucide="user" class="rp-tl-node-icon"></i>
            </span>
            <div class="rp-tl-card">
                <header class="rp-tl-card-head">
                    <span class="rp-tl-time">
                        <span class="rp-tl-time-rel">${relTime}</span>
                        <span class="rp-tl-time-abs mono-text">${absTime}</span>
                    </span>
                    <span class="rp-type-pill" data-type="manual">手动</span>
                </header>
                <div class="rp-tl-desc">${escapeHtml(desc)}</div>
                <div class="rp-tl-meta">
                    <span class="rp-tl-meta-cell mono-text"><i data-lucide="hash" class="icon-xs"></i>#${newId}</span>
                    <span class="rp-tl-meta-cell mono-text"><i data-lucide="database" class="icon-xs"></i>${sizeMb} MB</span>
                    <span class="rp-tl-meta-cell mono-text"><i data-lucide="hard-drive" class="icon-xs"></i>C:</span>
                </div>
                <div class="rp-tl-actions">
                    <button class="rp-row-btn rp-row-btn-restore" data-action="restore" data-id="${newId}">
                        <i data-lucide="rotate-ccw" class="icon-sm"></i><span>还原到此点</span>
                    </button>
                    <button class="rp-row-btn" data-action="detail" data-id="${newId}">
                        <i data-lucide="info" class="icon-sm"></i><span>详情</span>
                    </button>
                    <button class="rp-row-btn rp-row-btn-danger" data-action="delete" data-id="${newId}">
                        <i data-lucide="trash-2" class="icon-sm"></i><span>删除</span>
                    </button>
                </div>
            </div>
        `;
        tl.insertBefore(li, tl.firstElementChild);

        // 重新生成 lucide 图标
        if (window.lucide) window.lucide.createIcons({ root: li });

        // 入场动画
        requestAnimationFrame(() => {
            li.style.transition = 'all 0.4s cubic-bezier(0.4, 0, 0.2, 1)';
            li.style.opacity = '1';
            li.style.transform = 'translateY(0)';
        });

        // 更新计数
        updateCount(root);
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    function updateCount(root) {
        const visibleItems = $$($(root, '#rpTimeline'), '.rp-tl-item:not([data-hidden="true"])').length;
        const cnt = $(root, '#rpTotalCount');
        if (cnt) cnt.textContent = visibleItems;
    }

    /* ---------------- 筛选 ---------------- */
    function applyFilter(root, cat) {
        const items = $$($(root, '#rpTimeline'), '.rp-tl-item');
        items.forEach(it => {
            const t = it.getAttribute('data-type');
            const visible = (cat === 'all') || (t === cat);
            it.setAttribute('data-hidden', visible ? 'false' : 'true');
        });
        updateCount(root);
    }

    /* ---------------- Modal（还原 / 删除确认） ---------------- */
    function openModal(root, kind, id) {
        const modal = $(root, '#rpModal');
        const mask  = $(root, '#rpModalMask');
        const head  = $(root, '#rpModalHead');
        const title = $(root, '#rpModalTitle');
        const sub   = $(root, '#rpModalSub');
        const icon  = $(root, '#rpModalIcon');
        const targetName = $(root, '#rpModalTargetName');
        const targetMeta = $(root, '#rpModalTargetMeta');
        const warns = $(root, '#rpModalWarns');
        const confirmBtn = $(root, '#rpModalConfirm');
        if (!modal) return;

        // 抓取目标条目信息
        const item = $$(root, `.rp-tl-item[data-id="${id}"]`)[0];
        if (!item) return;
        const desc = item.querySelector('.rp-tl-desc')?.textContent.trim() || '';
        const abs  = item.querySelector('.rp-tl-time-abs')?.textContent.trim() || '';
        const sizeCell = item.querySelectorAll('.rp-tl-meta-cell')[1];
        const size = sizeCell ? sizeCell.textContent.replace(/[^\d.A-Za-z ]/g, '').trim() : '';

        if (kind === 'restore') {
            head.setAttribute('data-kind', 'restore');
            icon.setAttribute('data-lucide', 'rotate-ccw');
            title.textContent = '确认还原到此还原点';
            sub.textContent = '系统将重启并回到该时刻的状态';
            confirmBtn.innerHTML = `<i data-lucide="rotate-ccw" class="rp-btn-icon"></i><span>确认还原</span>`;
            warns.innerHTML = `
                <li><i data-lucide="alert-triangle" class="icon-xs"></i><span>该操作 <b>不可撤销</b>，请确认后继续</span></li>
                <li><i data-lucide="refresh-cw" class="icon-xs"></i><span>系统将自动 <b>重启</b>，预计耗时 5 - 10 分钟</span></li>
                <li><i data-lucide="file-check" class="icon-xs"></i><span>个人文件 <b>不会受影响</b>，但还原点之后安装的程序将被移除</span></li>
            `;
        } else if (kind === 'delete') {
            head.setAttribute('data-kind', 'delete');
            icon.setAttribute('data-lucide', 'trash-2');
            title.textContent = '确认删除该还原点';
            sub.textContent = '删除后无法恢复该还原点';
            confirmBtn.innerHTML = `<i data-lucide="trash-2" class="rp-btn-icon"></i><span>确认删除</span>`;
            warns.innerHTML = `
                <li><i data-lucide="alert-triangle" class="icon-xs"></i><span>删除后 <b>无法恢复</b> 该还原点</span></li>
                <li><i data-lucide="database" class="icon-xs"></i><span>将释放 <b>${size || '-'}</b> 磁盘空间</span></li>
                <li><i data-lucide="info" class="icon-xs"></i><span>不会影响其他还原点和系统当前状态</span></li>
            `;
        }

        targetName.textContent = `#${id} · ${desc}`;
        targetMeta.textContent = `${abs} · ${size} · C:`;
        confirmBtn.setAttribute('data-pending-id', id);
        confirmBtn.setAttribute('data-pending-kind', kind);

        modal.hidden = false;
        mask.hidden = false;
        // 异步触发 transition
        requestAnimationFrame(() => {
            modal.classList.add('is-open');
            mask.classList.add('is-open');
        });

        if (window.lucide) window.lucide.createIcons({ root: modal });

        // ESC 关闭
        state.modalEscHandler = (e) => {
            if (e.key === 'Escape') closeModal(root);
        };
        document.addEventListener('keydown', state.modalEscHandler);
    }

    function closeModal(root) {
        const modal = $(root, '#rpModal');
        const mask  = $(root, '#rpModalMask');
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
        const confirmBtn = $(root, '#rpModalConfirm');
        if (!confirmBtn) return;
        const id = confirmBtn.getAttribute('data-pending-id');
        const kind = confirmBtn.getAttribute('data-pending-kind');
        closeModal(root);

        if (kind === 'delete') {
            const item = $$(root, `.rp-tl-item[data-id="${id}"]`)[0];
            if (item) {
                item.style.transition = 'all 0.3s ease';
                item.style.opacity = '0';
                item.style.transform = 'translateX(-12px)';
                setTimer(() => {
                    item.remove();
                    updateCount(root);
                }, 320);
            }
        } else if (kind === 'restore') {
            // 这里只做演示：弹一个临时提示
            showToast(root, `已下发还原指令 · 还原点 #${id}（演示）`);
        }
    }

    /* ---------------- 简易 Toast ---------------- */
    function showToast(root, text) {
        let toast = $(root, '#rpToast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'rpToast';
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
                z-index: 1000;
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

    /* ---------------- 输入框计数 ---------------- */
    function bindInputCounter(root) {
        const input = $(root, '#rpCreateDesc');
        const len = $(root, '#rpInputLen');
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

        // 全局事件代理
        const handler = (e) => {
            const target = e.target.closest('[data-action], [data-filter]');
            if (!target || !root.contains(target)) return;

            // 筛选
            const filter = target.getAttribute('data-filter');
            if (filter) {
                $$(root, '.rp-filter-btn').forEach(b => b.classList.remove('is-active'));
                target.classList.add('is-active');
                applyFilter(root, filter);
                return;
            }

            // 行为
            const action = target.getAttribute('data-action');
            const id = target.getAttribute('data-id');
            switch (action) {
                case 'create-restore-point':
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
                    showToast(root, `查看还原点 #${id} 详情（演示）`);
                    break;
                case 'modal-cancel':
                    closeModal(root);
                    break;
                case 'modal-confirm':
                    modalConfirm(root);
                    break;
                case 'refresh-list':
                    showToast(root, '已刷新还原点列表');
                    break;
                case 'cleanup-old':
                    showToast(root, '清理旧还原点（演示）');
                    break;
                case 'open-system-protect':
                    showToast(root, '已请求打开 系统属性 → 系统保护（演示）');
                    break;
            }
        };
        root.addEventListener('click', handler);
        state.boundDocHandler = { el: root, handler };

        // mask 点击关闭 modal
        const mask = $(root, '#rpModalMask');
        if (mask) {
            mask.addEventListener('click', () => closeModal(root));
        }

        updateCount(root);
    }

    /* ---------------- 销毁 ---------------- */
    function destroy() {
        clearTimers();
        if (state.boundDocHandler) {
            state.boundDocHandler.el.removeEventListener('click', state.boundDocHandler.handler);
            state.boundDocHandler = null;
        }
        if (state.modalEscHandler) {
            document.removeEventListener('keydown', state.modalEscHandler);
            state.modalEscHandler = null;
        }
        const toast = document.getElementById('rpToast');
        if (toast) toast.remove();
        state.currentRoot = null;
    }

    /* ---------------- 注册 ---------------- */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_INIT__[VIEW_NAME] = init;
    window.__VIEW_DESTROY__[VIEW_NAME] = destroy;
})();
