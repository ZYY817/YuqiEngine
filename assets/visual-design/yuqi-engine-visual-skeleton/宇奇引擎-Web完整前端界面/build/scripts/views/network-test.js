/* ==========================================================================
   网络测试 · 视图脚本（Sonar Pulse Engine）
   --------------------------------------------------------------------------
   职责：
     1. 状态机：idle / scanning / settled
     2. 雷达扫描时同步激活扇区点（基于扫描指针角度）
     3. 数字滚动：requestAnimationFrame ease-out
     4. 迷你折线图（spark）：实时填充
     5. 自定义 Ping 模拟（流式输出）
     6. 报告生成 + 等级判定
   ========================================================================== */
(function () {
    'use strict';

    const VIEW_NAME = 'network-test';

    let state = {
        timers: [],
        rafs: [],
        boundClickEl: null,
        boundClickHandler: null,
        modalEscHandler: null,
        scanStartTs: 0,
        sectorActivated: { ping: false, down: false, up: false, jitter: false },
        sweepRaf: null,
    };

    /* ---------------- 工具 ---------------- */
    function $(root, sel) { return root.querySelector(sel); }
    function $$(root, sel) { return Array.from(root.querySelectorAll(sel)); }

    function clearAll() {
        state.timers.forEach(t => clearTimeout(t));
        state.timers = [];
        state.rafs.forEach(r => cancelAnimationFrame(r));
        state.rafs = [];
        if (state.sweepRaf) { cancelAnimationFrame(state.sweepRaf); state.sweepRaf = null; }
    }
    function setTimer(fn, ms) { const t = setTimeout(fn, ms); state.timers.push(t); return t; }
    function setPageState(root, st) { root.querySelector('.nt-page')?.setAttribute('data-state', st); }
    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, c => ({
            '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'
        }[c]));
    }

    /* ---------------- 数字滚动（ease-out cubic） ---------------- */
    function rollNumber(el, from, to, ms, decimals) {
        if (!el) return;
        const start = performance.now();
        const dec = (typeof decimals === 'number') ? decimals : 0;
        function step(now) {
            const t = Math.min(1, (now - start) / ms);
            const k = 1 - Math.pow(1 - t, 3); // ease-out cubic
            const v = from + (to - from) * k;
            el.textContent = v.toFixed(dec);
            if (t < 1) {
                const r = requestAnimationFrame(step);
                state.rafs.push(r);
            }
        }
        const r = requestAnimationFrame(step);
        state.rafs.push(r);
    }

    /* ---------------- Sparkline ---------------- */
    function drawSpark(svgEl, data, maxOverride) {
        if (!svgEl) return;
        const poly = svgEl.querySelector('polyline');
        if (!poly) return;
        const W = 200, H = 40, pad = 2;
        const max = maxOverride || Math.max(...data, 1);
        const min = Math.min(...data, 0);
        const span = Math.max(max - min, 0.001);
        const n = data.length;
        const pts = data.map((v, i) => {
            const x = (i / (n - 1 || 1)) * (W - pad * 2) + pad;
            const y = H - pad - ((v - min) / span) * (H - pad * 2);
            return `${x.toFixed(1)},${y.toFixed(1)}`;
        }).join(' ');
        poly.setAttribute('points', pts);
    }

    /* ---------------- 等级判定 ---------------- */
    function gradePing(v) {
        if (v <= 30)  return '优秀';
        if (v <= 60)  return '良好';
        if (v <= 100) return '一般';
        return '较差';
    }
    function gradeBandwidth(v) {
        if (v >= 200) return '优秀';
        if (v >= 80)  return '良好';
        if (v >= 30)  return '一般';
        return '较差';
    }
    function gradeJitter(v) {
        if (v <= 5)  return '优秀';
        if (v <= 15) return '良好';
        if (v <= 30) return '一般';
        return '较差';
    }

    /* ---------------- 模拟生成最终结果 ---------------- */
    function rollDice() {
        return {
            ping:   Math.round(12 + Math.random() * 28),       // 12~40
            down:   Math.round(80 + Math.random() * 220),      // 80~300 Mbps
            up:     Math.round(15 + Math.random() * 60),       // 15~75 Mbps
            jitter: +(2 + Math.random() * 8).toFixed(1),       // 2~10 ms
            loss:   +(Math.random() * 1.2).toFixed(1),         // 0~1.2 %
        };
    }

    /* ---------------- 扫描态：扇区指针角度监听 ---------------- */
    // 扇区角度（基于 SVG 顶部 0°、顺时针）：
    //   ping=90°(右), down=0°(上), up=270°(左), jitter=180°(下)
    const SECTOR_ANGLES = { down: 0, ping: 90, jitter: 180, up: 270 };
    const SCAN_TOTAL_MS = 4200; // 与 CSS ntSweep 一致

    function startSweepWatcher(root, finalData) {
        state.scanStartTs = performance.now();
        state.sectorActivated = { ping: false, down: false, up: false, jitter: false };

        const ping  = $(root, '#ntPingVal');
        const down  = $(root, '#ntDownVal');
        const up    = $(root, '#ntUpVal');
        const jit   = $(root, '#ntJitterVal');
        const loss  = $(root, '#ntLossVal');

        const coreVal   = $(root, '#ntCoreValue');
        const coreUnit  = $(root, '#ntCoreUnit');
        const coreLabel = $(root, '#ntCoreLabel');
        const coreHint  = $(root, '#ntCoreHint');

        const stageFill = $(root, '#ntStageFill');
        const stagePct  = $(root, '#ntStagePct');
        const stageText = $(root, '#ntStageText');

        // 居中文本节奏
        const PHASES = [
            { at: 0.00, label: '脉冲', text: '点火 · 信号正在扩散',          coreUnit: '',    coreVal: '...' },
            { at: 0.18, label: '探测延迟', text: '正在测量 RTT 延迟',         coreUnit: 'ms',  coreVal: ()=>finalData.ping },
            { at: 0.42, label: '探测下行', text: '测绘下载带宽',              coreUnit: 'Mbps',coreVal: ()=>finalData.down },
            { at: 0.66, label: '探测上行', text: '测绘上传带宽',              coreUnit: 'Mbps',coreVal: ()=>finalData.up },
            { at: 0.86, label: '探测稳定性', text: '采样抖动与丢包',          coreUnit: 'ms',  coreVal: ()=>finalData.jitter },
        ];
        let phaseIdx = -1;

        function tick(now) {
            const elapsed = now - state.scanStartTs;
            const t = Math.min(1, elapsed / SCAN_TOTAL_MS);
            const angle = (t * 360) % 360;

            // 进度条
            const pctVal = Math.round(t * 100);
            stageFill.style.width = pctVal + '%';
            stagePct.textContent = pctVal;

            // 阶段文本切换
            for (let i = PHASES.length - 1; i >= 0; i--) {
                if (t >= PHASES[i].at && phaseIdx < i) {
                    phaseIdx = i;
                    const ph = PHASES[i];
                    coreLabel.textContent = ph.label;
                    coreUnit.textContent = ph.coreUnit;
                    if (typeof ph.coreVal === 'function') {
                        const target = ph.coreVal();
                        rollNumber(coreVal, parseFloat(coreVal.textContent) || 0, target,
                                   500, (i === 4 ? 1 : 0));
                    } else {
                        coreVal.textContent = ph.coreVal;
                    }
                    stageText.textContent = ph.text;
                    coreHint.textContent  = '— 扫描进行中 —';
                    break;
                }
            }

            // 激活扇区点 & 同步指标晶片（指针扫到对应角度时点亮）
            for (const key of ['down', 'ping', 'jitter', 'up']) {
                if (state.sectorActivated[key]) continue;
                const target = SECTOR_ANGLES[key];
                // 扫描指针角度需大于目标角度（容差 4°）
                if (angle >= target + 4 || (target === 0 && angle >= 4 && t > 0.05)) {
                    state.sectorActivated[key] = true;
                    activateSector(root, key, finalData);
                }
            }

            if (t < 1) {
                state.sweepRaf = requestAnimationFrame(tick);
            } else {
                onScanComplete(root, finalData);
            }
        }
        state.sweepRaf = requestAnimationFrame(tick);
    }

    /* ---------------- 扇区激活：点亮 + 数字滚动 + spark ---------------- */
    function activateSector(root, key, data) {
        const sector = root.querySelector(`.nt-sector[data-key="${key}"]`);
        if (sector) sector.setAttribute('data-active', 'true');
        const chip = root.querySelector(`.nt-chip[data-key="${key}"]`);
        if (chip) chip.setAttribute('data-active', 'true');

        if (key === 'ping') {
            const el = $(root, '#ntPingVal');
            rollNumber(el, 0, data.ping, 700, 0);
            const grade = gradePing(data.ping);
            chip?.querySelector('.nt-chip-grade')?.setAttribute('data-grade', grade);
            chip && (chip.querySelector('.nt-chip-grade').textContent = grade);
            const arr = Array.from({length: 24}, (_, i) =>
                data.ping + Math.sin(i * 0.6) * (data.ping * 0.18) + (Math.random() - 0.5) * (data.ping * 0.12)
            );
            drawSpark($(root, '#ntPingSpark'), arr);
        } else if (key === 'down') {
            const el = $(root, '#ntDownVal');
            rollNumber(el, 0, data.down, 1100, 0);
            const grade = gradeBandwidth(data.down);
            chip?.querySelector('.nt-chip-grade')?.setAttribute('data-grade', grade);
            chip && (chip.querySelector('.nt-chip-grade').textContent = grade);
            // 下载典型曲线：缓启 → 满速 → 收尾抖动
            const arr = Array.from({length: 24}, (_, i) => {
                const ramp = i < 6 ? (i / 6) * data.down : data.down;
                return ramp - Math.random() * (data.down * 0.10);
            });
            drawSpark($(root, '#ntDownSpark'), arr);
        } else if (key === 'up') {
            const el = $(root, '#ntUpVal');
            rollNumber(el, 0, data.up, 1100, 0);
            const grade = gradeBandwidth(data.up);
            chip?.querySelector('.nt-chip-grade')?.setAttribute('data-grade', grade);
            chip && (chip.querySelector('.nt-chip-grade').textContent = grade);
            const arr = Array.from({length: 24}, (_, i) => {
                const ramp = i < 5 ? (i / 5) * data.up : data.up;
                return ramp - Math.random() * (data.up * 0.12);
            });
            drawSpark($(root, '#ntUpSpark'), arr);
        } else if (key === 'jitter') {
            const el  = $(root, '#ntJitterVal');
            const els = $(root, '#ntLossVal');
            rollNumber(el,  0, data.jitter, 700, 1);
            rollNumber(els, 0, data.loss,   700, 1);
            const grade = gradeJitter(data.jitter);
            chip?.querySelector('.nt-chip-grade')?.setAttribute('data-grade', grade);
            chip && (chip.querySelector('.nt-chip-grade').textContent = grade);
            const arr = Array.from({length: 24}, () =>
                data.jitter + (Math.random() - 0.5) * 4
            );
            drawSpark($(root, '#ntJitterSpark'), arr);
        }
    }

    /* ---------------- 扫描结束 ---------------- */
    function onScanComplete(root, data) {
        setPageState(root, 'settled');

        const coreVal   = $(root, '#ntCoreValue');
        const coreUnit  = $(root, '#ntCoreUnit');
        const coreLabel = $(root, '#ntCoreLabel');
        const coreHint  = $(root, '#ntCoreHint');
        const stageFill = $(root, '#ntStageFill');
        const stagePct  = $(root, '#ntStagePct');
        const stageText = $(root, '#ntStageText');

        // 中心定格在 ping
        coreLabel.textContent = '延迟 PING';
        coreUnit.textContent  = 'ms';
        coreVal.textContent   = data.ping;
        coreHint.textContent  = `当前节点 · 上海 BGP · 综合等级 ${comprehensiveGrade(data)}`;

        stageFill.style.width = '100%';
        stagePct.textContent  = '100';
        stageText.textContent = '扫描完成 · 信号已沉淀';

        // 报告
        renderReport(root, data);

        // 显示再测按钮
        const re = $(root, '#ntReIgnite');
        if (re) re.hidden = false;
    }

    function comprehensiveGrade(data) {
        const order = ['较差', '一般', '良好', '优秀'];
        const grades = [
            gradePing(data.ping),
            gradeBandwidth(data.down),
            gradeBandwidth(data.up),
            gradeJitter(data.jitter),
        ];
        const minIdx = Math.min(...grades.map(g => order.indexOf(g)));
        return order[minIdx];
    }

    /* ---------------- 报告生成 ---------------- */
    function renderReport(root, data) {
        const list = $(root, '#ntReportList');
        const time = $(root, '#ntReportTime');
        if (!list) return;

        const now = new Date();
        const pad = n => String(n).padStart(2, '0');
        time.textContent = `${now.getFullYear()}-${pad(now.getMonth()+1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;

        const items = [];

        // 延迟诊断
        if (data.ping <= 30) {
            items.push({ st: 'good', icon: 'check-circle-2', t: '延迟优秀',
                d: `RTT ${data.ping}ms · 适合在线游戏、视频会议、远程协作。` });
        } else if (data.ping <= 60) {
            items.push({ st: 'good', icon: 'check-circle-2', t: '延迟良好',
                d: `RTT ${data.ping}ms · 一般在线场景流畅，竞技类游戏可能轻微感知。` });
        } else if (data.ping <= 100) {
            items.push({ st: 'fair', icon: 'alert-circle', t: '延迟一般',
                d: `RTT ${data.ping}ms · 视频通话基本可用，建议改用 5GHz Wi-Fi 或有线连接。` });
        } else {
            items.push({ st: 'poor', icon: 'alert-triangle', t: '延迟偏高',
                d: `RTT ${data.ping}ms · 实时交互体验受影响。建议检查路由器位置、关闭后台占用大流量的应用。` });
        }

        // 带宽诊断
        if (data.down >= 200) {
            items.push({ st: 'good', icon: 'gauge', t: '下载带宽优秀',
                d: `${data.down} Mbps · 4K 流畅、大文件秒传。同时上传 ${data.up} Mbps。` });
        } else if (data.down >= 80) {
            items.push({ st: 'good', icon: 'gauge', t: '下载带宽良好',
                d: `${data.down} Mbps · 1080P 视频与日常下载流畅。上传 ${data.up} Mbps。` });
        } else if (data.down >= 30) {
            items.push({ st: 'fair', icon: 'gauge', t: '下载带宽一般',
                d: `${data.down} Mbps · 高清视频可能受限。上传 ${data.up} Mbps。` });
        } else {
            items.push({ st: 'poor', icon: 'gauge', t: '下载带宽偏低',
                d: `${data.down} Mbps · 建议联系运营商或重启光猫。上传 ${data.up} Mbps。` });
        }

        // 稳定性诊断
        if (data.jitter <= 5 && data.loss < 0.5) {
            items.push({ st: 'good', icon: 'shield-check', t: '链路稳定',
                d: `抖动 ${data.jitter}ms · 丢包 ${data.loss}% · 链路质量稳定可靠。` });
        } else if (data.jitter <= 15 && data.loss < 1) {
            items.push({ st: 'fair', icon: 'shield', t: '链路轻微波动',
                d: `抖动 ${data.jitter}ms · 丢包 ${data.loss}% · 偶发卡顿可能。` });
        } else {
            items.push({ st: 'poor', icon: 'shield-alert', t: '链路不稳定',
                d: `抖动 ${data.jitter}ms · 丢包 ${data.loss}% · 建议检查 Wi-Fi 信号或重启网络设备。` });
        }

        list.innerHTML = items.map(it => `
            <li class="nt-report-item" data-state="${it.st}">
                <i data-lucide="${it.icon}" class="nt-report-item-icon"></i>
                <div class="nt-report-item-main">
                    <span class="nt-report-item-title">${escapeHtml(it.t)}</span>
                    <span class="nt-report-item-desc">${escapeHtml(it.d)}</span>
                </div>
            </li>
        `).join('');

        if (window.lucide) window.lucide.createIcons({ root: list });
    }

    /* ---------------- 点火 ---------------- */
    function ignite(root) {
        // 重置
        clearAll();
        resetSectors(root);
        resetChipGrades(root);

        const finalData = rollDice();
        setPageState(root, 'scanning');

        // 中心数字 placeholder
        $(root, '#ntCoreValue').textContent = '...';
        $(root, '#ntCoreUnit').textContent  = '';
        $(root, '#ntCoreLabel').textContent = '脉冲';
        $(root, '#ntCoreHint').textContent  = '— 扫描进行中 —';

        // 隐藏"再测一次"
        const re = $(root, '#ntReIgnite');
        if (re) re.hidden = true;

        // 启动扫描动画 + 数据生成
        startSweepWatcher(root, finalData);
    }

    function resetSectors(root) {
        $$(root, '.nt-sector').forEach(s => s.removeAttribute('data-active'));
        $$(root, '.nt-chip').forEach(c => c.removeAttribute('data-active'));
        ['ntPingVal', 'ntDownVal', 'ntUpVal', 'ntJitterVal', 'ntLossVal'].forEach(id => {
            const el = root.querySelector('#' + id);
            if (el) el.textContent = '—';
        });
        ['ntPingSpark', 'ntDownSpark', 'ntUpSpark', 'ntJitterSpark'].forEach(id => {
            const el = root.querySelector('#' + id + ' polyline');
            if (el) el.setAttribute('points', '');
        });
    }
    function resetChipGrades(root) {
        $$(root, '.nt-chip-grade').forEach(g => {
            g.setAttribute('data-grade', '—');
            g.textContent = '—';
        });
    }

    /* ---------------- 复制报告 ---------------- */
    function copyReport(root) {
        const time = $(root, '#ntReportTime')?.textContent || '';
        const ping = $(root, '#ntPingVal')?.textContent || '—';
        const down = $(root, '#ntDownVal')?.textContent || '—';
        const up   = $(root, '#ntUpVal')?.textContent || '—';
        const jit  = $(root, '#ntJitterVal')?.textContent || '—';
        const loss = $(root, '#ntLossVal')?.textContent || '—';

        if (ping === '—') { showToast(root, '尚未生成报告，请先发起测试'); return; }

        const txt = [
            '宇奇引擎 · 网络测试报告',
            `时间: ${time}`,
            `节点: 上海 BGP`,
            `延迟: ${ping} ms`,
            `下载: ${down} Mbps`,
            `上传: ${up} Mbps`,
            `抖动: ${jit} ms · 丢包 ${loss}%`,
        ].join('\n');

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(txt).then(
                () => showToast(root, '报告已复制到剪贴板'),
                () => showToast(root, '复制失败，请手动选择')
            );
        } else {
            showToast(root, '当前环境不支持剪贴板写入');
        }
    }

    /* ---------------- 自定义 Ping Modal ---------------- */
    function openModal(root) {
        const modal = $(root, '#ntModal');
        const mask  = $(root, '#ntModalMask');
        if (!modal) return;
        modal.hidden = false;
        mask.hidden = false;
        requestAnimationFrame(() => {
            modal.classList.add('is-open');
            mask.classList.add('is-open');
        });
        if (window.lucide) window.lucide.createIcons({ root: modal });

        state.modalEscHandler = (e) => { if (e.key === 'Escape') closeModal(root); };
        document.addEventListener('keydown', state.modalEscHandler);
    }
    function closeModal(root) {
        const modal = $(root, '#ntModal');
        const mask  = $(root, '#ntModalMask');
        if (!modal) return;
        modal.classList.remove('is-open');
        mask.classList.remove('is-open');
        setTimer(() => { modal.hidden = true; mask.hidden = true; }, 250);
        if (state.modalEscHandler) {
            document.removeEventListener('keydown', state.modalEscHandler);
            state.modalEscHandler = null;
        }
    }

    function runCustomPing(root) {
        const host = $(root, '#ntPingHost')?.value.trim() || 'www.bilibili.com';
        const out  = $(root, '#ntPingOutput');
        if (!out) return;

        out.innerHTML = '';
        appendLine(out, `> ping ${host}`, 'ok');
        appendLine(out, `正在 Ping ${host} [模拟解析中...]`, 'dim');

        setTimer(() => {
            const ip = `${randInt(20, 220)}.${randInt(0, 255)}.${randInt(0, 255)}.${randInt(0, 255)}`;
            appendLine(out, `Pinging ${host} [${ip}] with 32 bytes of data:`, '');
            const seq = [0, 700, 1400, 2100];
            const rtts = [];
            seq.forEach((delay, i) => {
                setTimer(() => {
                    const lost = Math.random() < 0.05;
                    if (lost) {
                        appendLine(out, `Request timed out.`, 'err');
                    } else {
                        const rtt = randInt(8, 48);
                        rtts.push(rtt);
                        appendLine(out, `Reply from ${ip}: bytes=32 time=${rtt}ms TTL=${randInt(48, 64)}`, '');
                    }
                    if (i === seq.length - 1) {
                        setTimer(() => {
                            appendLine(out, '', '');
                            appendLine(out, `Ping statistics for ${ip}:`, '');
                            const lossN = 4 - rtts.length;
                            appendLine(out,
                                `    Packets: Sent = 4, Received = ${rtts.length}, Lost = ${lossN} (${Math.round(lossN/4*100)}% loss),`,
                                lossN ? 'warn' : '');
                            if (rtts.length) {
                                const min = Math.min(...rtts);
                                const max = Math.max(...rtts);
                                const avg = Math.round(rtts.reduce((a,b)=>a+b,0)/rtts.length);
                                appendLine(out, `Approximate round trip times in milli-seconds:`, '');
                                appendLine(out, `    Minimum = ${min}ms, Maximum = ${max}ms, Average = ${avg}ms`, 'ok');
                            }
                        }, 500);
                    }
                }, delay);
            });
        }, 600);
    }

    function appendLine(container, text, kind) {
        const span = document.createElement('span');
        span.className = 'nt-modal-output-line' + (kind ? ' nt-modal-output-line--' + kind : '');
        span.textContent = text || '\u00A0';
        container.appendChild(span);
        container.scrollTop = container.scrollHeight;
    }
    function randInt(a, b) { return Math.floor(a + Math.random() * (b - a + 1)); }

    /* ---------------- Toast ---------------- */
    function showToast(root, text) {
        let toast = document.getElementById('ntToast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'ntToast';
            toast.style.cssText = `
                position: fixed;
                bottom: 32px;
                left: 50%;
                transform: translateX(-50%) translateY(20px);
                padding: 10px 18px;
                background: linear-gradient(135deg, #0F1E36, #07101F);
                color: #E2E8F0;
                font-size: 12.5px;
                font-weight: 600;
                border-radius: 10px;
                border: 1px solid rgba(34, 211, 238, 0.32);
                box-shadow: 0 8px 24px rgba(7, 16, 31, 0.5), 0 0 18px rgba(34, 211, 238, 0.18);
                z-index: 1100;
                opacity: 0;
                transition: all 0.25s cubic-bezier(0.22, 1, 0.36, 1);
                pointer-events: none;
                backdrop-filter: blur(8px);
                letter-spacing: 0.4px;
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

    /* ---------------- 节点切换（演示） ---------------- */
    function switchNode(root) {
        const NODES = ['上海 · BGP', '北京 · 联通', '广州 · 电信', '香港 · CN2', '东京 · 国际'];
        const cur = $(root, '.nt-node-pill .nt-node-name');
        if (!cur) return;
        const idx = NODES.indexOf(cur.textContent.trim());
        const next = NODES[(idx + 1) % NODES.length];
        cur.textContent = next;
        showToast(root, `节点已切换至 ${next}`);
    }

    /* ---------------- 主初始化 ---------------- */
    function init(root) {
        clearAll();

        const handler = (e) => {
            const target = e.target.closest('[data-action]');
            if (!target || !root.contains(target)) return;

            const action = target.getAttribute('data-action');
            switch (action) {
                case 'ignite':
                case 're-ignite':
                    ignite(root);
                    break;
                case 'switch-node':
                    switchNode(root);
                    break;
                case 'ping-target':
                    openModal(root);
                    break;
                case 'modal-close':
                    closeModal(root);
                    break;
                case 'modal-ping':
                    runCustomPing(root);
                    break;
                case 'copy-report':
                    copyReport(root);
                    break;
            }
        };
        root.addEventListener('click', handler);
        state.boundClickEl = root;
        state.boundClickHandler = handler;

        const mask = $(root, '#ntModalMask');
        if (mask) mask.addEventListener('click', () => closeModal(root));
    }

    /* ---------------- 销毁 ---------------- */
    function destroy() {
        clearAll();
        if (state.boundClickEl && state.boundClickHandler) {
            state.boundClickEl.removeEventListener('click', state.boundClickHandler);
            state.boundClickEl = null;
            state.boundClickHandler = null;
        }
        if (state.modalEscHandler) {
            document.removeEventListener('keydown', state.modalEscHandler);
            state.modalEscHandler = null;
        }
        const toast = document.getElementById('ntToast');
        if (toast) toast.remove();
    }

    /* ---------------- 注册 ---------------- */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_INIT__[VIEW_NAME] = init;
    window.__VIEW_DESTROY__[VIEW_NAME] = destroy;
})();
