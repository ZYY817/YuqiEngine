/* ==========================================================================
   硬件信息 · 视图脚本
   --------------------------------------------------------------------------
   职责：
     · 注入 Mock 硬件数据到 DOM（仅前端视觉演示）
     · 详情表格 / KV 网格的渲染
     · 顶部"刷新"按钮的视觉反馈（图标自旋 + 数据轻微抖动）
     · "导出"按钮：下载一份纯文本硬件摘要
     · "复制整机摘要"按钮 + 顶部右侧操作的 Toast 反馈

   自注册到 window.__VIEW_INIT__['hardware-info']
   ========================================================================== */
(function () {
    'use strict';

    const VIEW_NAME = 'hardware-info';

    /* ---------------- Mock 硬件数据 ---------------- */
    const MOCK = {
        pcName: 'DESKTOP-YQENGINE',
        user: 'YuqiUser',
        os: 'Windows 11 专业版',
        osVersion: '23H2 · 22631.4317',
        arch: 'x64',
        activated: true,
        uptime: '3 天 12 小时 47 分',
        installDate: '2024-08-12',
        serial: 'YQE-DEMO-001-2K9F',

        cpu: {
            name: 'Intel Core i7-13700K',
            family: 'Raptor Lake · 13th Gen',
            cores: 16, threads: 24,
            base: 3.4, boost: 5.4,
            tdp: 125,
            tempC: 42, load: 12,
            socket: 'LGA1700', cache: '30 MB L3'
        },
        ram: {
            totalGb: 32, slotsUsed: 2, slotsMax: 4,
            speedMt: 5600, timing: '36-36-36-76',
            type: 'DDR5', channel: '双通道',
            usedGb: 14.7
        },
        gpu: {
            name: 'NVIDIA GeForce RTX 4070 Ti',
            vendor: 'NVIDIA',
            vramGb: 12, driver: '552.22',
            api: 'DirectX 12 Ultimate · CUDA 8.9',
            tempC: 38, powerW: 35
        },
        disks: [
            { letter: 'C:', model: 'Samsung 990 Pro 2TB',  iface: 'NVMe Gen4', sizeGb: 2048, usedPct: 63, healthTone: 'green', health: '良好',  tempC: 36 },
            { letter: 'D:', model: 'WD Blue SN580 1TB',    iface: 'NVMe Gen4', sizeGb: 1024, usedPct: 41, healthTone: 'green', health: '良好',  tempC: 34 },
            { letter: 'E:', model: 'Seagate ST1000DM010',  iface: 'SATA III',  sizeGb: 1000, usedPct: 78, healthTone: 'amber', health: '注意',  tempC: 31 }
        ],
        mainDiskUsedPct: 63,
        diskUsedTb: 2.47,
        diskFreeTb: 1.45,

        nics: [
            { name: 'Realtek RTL8125 2.5GbE',     type: '有线',  ipv4: '192.168.1.108', mac: '24-4B-FE-AB-12-90', gateway: '192.168.1.1',  dns: '223.5.5.5',  state: 'on',  linkText: '2.5 Gbps · 全双工' },
            { name: 'Intel Wi-Fi 6E AX211 160MHz', type: '无线',  ipv4: '192.168.1.114', mac: '8C-1D-96-3F-2A-7B', gateway: '192.168.1.1',  dns: '119.29.29.29', state: 'idle', linkText: '已连接 · 1.2 Gbps' }
        ],
        netLinkText: '2.5 Gbps',
        netDownText: '12.3 MB/s',
        netUpText:   '1.8 MB/s',

        monitor: {
            count: 2,
            primary: '27" 2K · 144Hz',
            res: '2560 × 1440',
            hz: 144,
            gamut: 'sRGB 99%',
            scale: '125%',
            hdr: '支持'
        },

        bios: {
            mfr: 'MSI', model: 'MAG B760M MORTAR WIFI',
            biosVendor: 'American Megatrends',
            biosVersion: '7E20v17',
            biosDate: '2024-06-28',
            uefi: 'UEFI · 安全启动开启',
            tpm: 'TPM 2.0',
            chipset: 'Intel B760'
        },

        // 演示：留空表示台式机；如需展示电池详情，把 isLaptop 改 true
        isLaptop: false,
        battery: null
    };

    /* ---------------- 工具函数 ---------------- */
    function $(root, sel) { return root.querySelector(sel); }
    function setText(root, id, text) {
        const el = root.querySelector('#' + id);
        if (el) el.textContent = text;
    }

    function showToast(text) {
        let t = document.querySelector('.hi-toast');
        if (!t) {
            t = document.createElement('div');
            t.className = 'hi-toast';
            document.body.appendChild(t);
        }
        t.textContent = text;
        // 重启动画
        t.classList.remove('is-show');
        // eslint-disable-next-line no-unused-expressions
        t.offsetWidth;
        t.classList.add('is-show');
        clearTimeout(t._hideTimer);
        t._hideTimer = setTimeout(() => t.classList.remove('is-show'), 1800);
    }

    /* ---------------- 渲染：核心卡片文本 ---------------- */
    function renderSummary(root) {
        setText(root, 'hiPcName',      MOCK.pcName);
        setText(root, 'hiUser',        MOCK.user);
        setText(root, 'hiUptime',      MOCK.uptime);
        setText(root, 'hiMobo',        MOCK.bios.mfr + ' ' + MOCK.bios.model);
        setText(root, 'hiInstallDate', MOCK.installDate);
        setText(root, 'hiOsBadge',     MOCK.os.replace('Windows 11 专业版', 'Windows 11') + ' · ' + MOCK.osVersion.split(' · ')[0]);
        setText(root, 'hiFingerprint', 'SN · ' + MOCK.serial);
    }

    function renderCpu(root) {
        const c = MOCK.cpu;
        setText(root, 'hiCpuName',  c.name);
        setText(root, 'hiCpuBase',  c.base.toFixed(1));
        setText(root, 'hiCpuBoost', c.boost.toFixed(1));
        setText(root, 'hiCpuTdp',   c.tdp + ' ');
        setText(root, 'hiCpuTemp',  c.tempC + ' °C');
        setText(root, 'hiCpuLoad',  c.load + '%');
        const coresEl = root.querySelector('#hiCpuCores');
        if (coresEl) coresEl.innerHTML = c.cores + '<small>C</small> / ' + c.threads + '<small>T</small>';
    }

    function renderRam(root) {
        const r = MOCK.ram;
        setText(root, 'hiRamSize',     r.totalGb + ' GB ' + r.type);
        setText(root, 'hiRamSpeed',    r.speedMt + ' ');
        setText(root, 'hiRamTiming',   r.timing);
        setText(root, 'hiRamSlots',    r.slotsUsed);
        setText(root, 'hiRamSlotsMax', r.slotsMax);
        setText(root, 'hiRamUsed',     r.usedGb.toFixed(1));
        const fill = root.querySelector('#hiRamFill');
        if (fill) fill.style.width = Math.round(r.usedGb / r.totalGb * 100) + '%';
    }

    function renderGpu(root) {
        const g = MOCK.gpu;
        setText(root, 'hiGpuName',   g.name);
        setText(root, 'hiGpuVram',   g.vramGb + ' ');
        setText(root, 'hiGpuDriver', g.driver);
        setText(root, 'hiGpuApi',    'DX12 · CUDA 8.9');
        setText(root, 'hiGpuTemp',   g.tempC + ' °C');
        setText(root, 'hiGpuPwr',    g.powerW + ' W');
    }

    function renderDiskCard(root) {
        const main = MOCK.disks[0];
        setText(root, 'hiDiskName',  MOCK.disks.length + ' 块磁盘 · 共 ' + (MOCK.diskUsedTb + MOCK.diskFreeTb).toFixed(2) + ' TB');
        setText(root, 'hiDiskUsed',  MOCK.diskUsedTb.toFixed(2));
        setText(root, 'hiDiskFree',  MOCK.diskFreeTb.toFixed(2));
        setText(root, 'hiDiskHealth', main.health);
        const fill = root.querySelector('#hiDiskFill');
        if (fill) fill.style.width = MOCK.mainDiskUsedPct + '%';
        setText(root, 'hiDiskCPct', MOCK.mainDiskUsedPct + '%');
    }

    function renderNetCard(root) {
        const n = MOCK.nics[0];
        setText(root, 'hiNetName',  n.name);
        setText(root, 'hiNetLink',  '2.5 ');
        setText(root, 'hiNetIp',    n.ipv4);
        setText(root, 'hiNetWifi',  'Wi-Fi 6E · BT 5.3');
        setText(root, 'hiNetDn',    MOCK.netDownText);
        setText(root, 'hiNetUp',    MOCK.netUpText);
    }

    function renderMonitorCard(root) {
        const m = MOCK.monitor;
        setText(root, 'hiMonName',  m.count + ' 台 · 主屏 ' + m.primary.split(' · ')[0]);
        setText(root, 'hiMonRes',   m.res);
        setText(root, 'hiMonHz',    m.hz + ' ');
        setText(root, 'hiMonGamut', m.gamut);
        setText(root, 'hiMonScale', m.scale);
        setText(root, 'hiMonHdr',   m.hdr);
    }

    /* ---------------- 渲染：详情区 ---------------- */
    function renderDiskTable(root) {
        const tbody = root.querySelector('#hiDiskTbody');
        if (!tbody) return;
        tbody.innerHTML = MOCK.disks.map(d => {
            const sizeText = d.sizeGb >= 1000 ? (d.sizeGb / 1024).toFixed(2) + ' TB' : d.sizeGb + ' GB';
            return `
                <tr>
                    <td><span class="hi-cell-strong">${d.letter}</span></td>
                    <td>${d.model}</td>
                    <td><span class="hi-cell-mono">${d.iface}</span></td>
                    <td><span class="hi-cell-mono">${sizeText}</span></td>
                    <td>
                        <div class="hi-cell-bar">
                            <span class="hi-cell-bar-track"><span class="hi-cell-bar-fill" style="width:${d.usedPct}%"></span></span>
                            <span class="hi-cell-bar-text">${d.usedPct}%</span>
                        </div>
                    </td>
                    <td><span class="hi-cell-pill" data-tone="${d.healthTone}">${d.health}</span></td>
                    <td><span class="hi-cell-mono">${d.tempC} °C</span></td>
                </tr>
            `;
        }).join('');
    }

    function renderNetTable(root) {
        const tbody = root.querySelector('#hiNetTbody');
        if (!tbody) return;
        tbody.innerHTML = MOCK.nics.map(n => {
            const tone = n.state === 'on' ? 'green' : n.state === 'idle' ? 'blue' : 'red';
            const stateText = n.state === 'on' ? '已连接' : n.state === 'idle' ? '空闲' : '断开';
            return `
                <tr>
                    <td><span class="hi-cell-strong">${n.name}</span></td>
                    <td><span class="hi-cell-pill" data-tone="${n.type === '有线' ? 'blue' : 'amber'}">${n.type}</span></td>
                    <td><span class="hi-cell-mono">${n.ipv4}</span></td>
                    <td><span class="hi-cell-mono">${n.mac}</span></td>
                    <td><span class="hi-cell-mono">${n.gateway}</span></td>
                    <td><span class="hi-cell-mono">${n.dns}</span></td>
                    <td><span class="hi-cell-pill" data-tone="${tone}">${stateText}</span></td>
                </tr>
            `;
        }).join('');
    }

    function renderBiosGrid(root) {
        const grid = root.querySelector('#hiBiosGrid');
        if (!grid) return;
        const b = MOCK.bios;
        const rows = [
            ['主板厂商',  b.mfr],
            ['主板型号',  b.model],
            ['芯片组',    b.chipset],
            ['BIOS 厂商', b.biosVendor],
            ['BIOS 版本', b.biosVersion],
            ['BIOS 日期', b.biosDate],
            ['启动模式',  b.uefi],
            ['安全模块',  b.tpm]
        ];
        grid.innerHTML = rows.map(([k, v]) => `<div><dt>${k}</dt><dd>${v}</dd></div>`).join('');
    }

    function renderBattery(root) {
        const grp = root.querySelector('#hiBatteryGroup');
        if (!grp) return;
        if (!MOCK.isLaptop || !MOCK.battery) {
            grp.hidden = true;
            return;
        }
        grp.hidden = false;
        // 此处略：若 isLaptop=true 时再补全电池字段
    }

    /* ---------------- 行为：刷新按钮 ---------------- */
    function bindRefresh(root) {
        const btn = root.querySelector('#hiRefreshBtn');
        const icon = root.querySelector('#hiRefreshIcon');
        if (!btn || !icon) return;
        btn.addEventListener('click', () => {
            if (icon.classList.contains('is-spinning')) return;
            icon.classList.add('is-spinning');
            // 模拟一次"重新读取"：抖动 CPU 占用 / 内存使用
            const newCpuLoad = 8 + Math.floor(Math.random() * 25);
            const newRamUsed = (10 + Math.random() * 12).toFixed(1);
            const newCpuTemp = 38 + Math.floor(Math.random() * 12);
            setTimeout(() => {
                setText(root, 'hiCpuLoad', newCpuLoad + '%');
                setText(root, 'hiCpuTemp', newCpuTemp + ' °C');
                setText(root, 'hiRamUsed', newRamUsed);
                const fill = root.querySelector('#hiRamFill');
                if (fill) fill.style.width = Math.round(parseFloat(newRamUsed) / MOCK.ram.totalGb * 100) + '%';
                icon.classList.remove('is-spinning');
                showToast('硬件信息已刷新');
            }, 900);
        });
    }

    /* ---------------- 行为：复制摘要 ---------------- */
    function buildSummaryText() {
        const c = MOCK.cpu, r = MOCK.ram, g = MOCK.gpu, b = MOCK.bios;
        return [
            `设备：${MOCK.pcName}（${MOCK.user}）`,
            `系统：${MOCK.os} ${MOCK.osVersion} · ${MOCK.arch}${MOCK.activated ? ' · 已激活' : ''}`,
            `主板：${b.mfr} ${b.model} · ${b.chipset} · BIOS ${b.biosVersion} (${b.biosDate})`,
            `处理器：${c.name} · ${c.cores}C/${c.threads}T · ${c.base}↑${c.boost}GHz · ${c.tdp}W`,
            `内存：${r.totalGb}GB ${r.type} ${r.speedMt}MT/s · ${r.timing} · ${r.slotsUsed}/${r.slotsMax} 槽 · ${r.channel}`,
            `显卡：${g.name} · 显存 ${g.vramGb}GB · 驱动 ${g.driver}`,
            `磁盘：${MOCK.disks.map(d => `${d.letter} ${d.model}`).join(' / ')}`,
            `网络：${MOCK.nics.map(n => `${n.name}(${n.ipv4})`).join(' / ')}`,
            `导出时间：${new Date().toLocaleString('zh-CN')}`
        ].join('\n');
    }

    function bindCopySummary(root) {
        const btn = root.querySelector('[data-action="copy-summary"]');
        if (!btn) return;
        btn.addEventListener('click', async () => {
            const text = buildSummaryText();
            try {
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    await navigator.clipboard.writeText(text);
                } else {
                    const ta = document.createElement('textarea');
                    ta.value = text;
                    ta.style.position = 'fixed';
                    ta.style.opacity = '0';
                    document.body.appendChild(ta);
                    ta.select();
                    document.execCommand('copy');
                    document.body.removeChild(ta);
                }
                showToast('整机摘要已复制');
            } catch (e) {
                showToast('复制失败，请手动选择');
            }
        });
    }

    /* ---------------- 行为：导出 TXT ---------------- */
    function bindExport(root) {
        const btn = root.querySelector('#hiExportBtn');
        if (!btn) return;
        btn.addEventListener('click', () => {
            const text = '【硬件信息 · 宇奇引擎】\n' + '='.repeat(40) + '\n' + buildSummaryText() + '\n';
            const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `硬件信息_${MOCK.pcName}_${new Date().toISOString().slice(0,10)}.txt`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
            showToast('已导出到下载目录');
        });
    }

    /* ---------------- 行为：打开系统信息（仅演示提示） ---------------- */
    function bindOpenSysinfo(root) {
        const btn = root.querySelector('[data-action="open-sysinfo"]');
        if (!btn) return;
        btn.addEventListener('click', () => {
            showToast('部署后将调起系统信息（msinfo32）');
        });
    }

    /* ---------------- 视图初始化 ---------------- */
    window.__VIEW_INIT__ = window.__VIEW_INIT__ || {};
    window.__VIEW_INIT__[VIEW_NAME] = function (root) {
        // 渲染概要 + 6 张卡
        renderSummary(root);
        renderCpu(root);
        renderRam(root);
        renderGpu(root);
        renderDiskCard(root);
        renderNetCard(root);
        renderMonitorCard(root);

        // 渲染详情区
        renderDiskTable(root);
        renderNetTable(root);
        renderBiosGrid(root);
        renderBattery(root);

        // 绑定行为
        bindRefresh(root);
        bindCopySummary(root);
        bindExport(root);
        bindOpenSysinfo(root);

        // 重新生成 lucide 图标（详情表格里有 pill / mono，无需图标，但统一调用一次保险）
        if (window.lucide) {
            try { window.lucide.createIcons({ root: root }); } catch (_) { /* noop */ }
        }
    };

    /* ---------------- 视图卸载 ---------------- */
    window.__VIEW_DESTROY__ = window.__VIEW_DESTROY__ || {};
    window.__VIEW_DESTROY__[VIEW_NAME] = function () {
        const t = document.querySelector('.hi-toast');
        if (t && t._hideTimer) clearTimeout(t._hideTimer);
    };
})();
