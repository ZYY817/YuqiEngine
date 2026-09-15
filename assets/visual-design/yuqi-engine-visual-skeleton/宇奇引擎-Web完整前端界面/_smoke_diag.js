const puppeteer = require('puppeteer-core');
const http = require('http'), fs = require('fs'), path = require('path');
const root = 'f:/优化软件/docs/视觉升级/宇奇引擎-Web完整复刻';
const types = {
    '.html':'text/html','.css':'text/css','.js':'application/javascript',
    '.svg':'image/svg+xml','.png':'image/png','.ico':'image/x-icon','.json':'application/json'
};
const srv = http.createServer((req,res)=>{
    const f = path.join(root, decodeURIComponent(req.url.split('?')[0]));
    fs.readFile(f,(e,d)=>{
        if(e){res.statusCode=404;res.end('NF');}
        else{res.setHeader('Content-Type', types[path.extname(f)]||'text/plain'); res.end(d);}
    });
});

const candidates = [
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe'
];
const exe = candidates.find(p => fs.existsSync(p));
if (!exe) { console.log('FAIL: no browser'); process.exit(1); }

srv.listen(8868, async () => {
    try {
        const b = await puppeteer.launch({ executablePath: exe, headless:'new', args:['--no-sandbox'] });
        const p = await b.newPage();
        // 模拟用户截图的窗口尺寸（看起来 1280×740 主视窗）
        await p.setViewport({ width:1280, height:740, deviceScaleFactor:1 });
        await p.goto('http://127.0.0.1:8868/index.html', { waitUntil:'networkidle0', timeout:15000 });
        await p.waitForSelector('.nav-item[data-view="memory-optimization"]', { timeout:8000 });
        await p.click('.nav-item[data-view="memory-optimization"]');
        await new Promise(r => setTimeout(r, 1800));
        const info = await p.evaluate(() => {
            const sel = s => document.querySelector(s);
            const all = s => Array.from(document.querySelectorAll(s));
            const rect = el => {
                if(!el) return null;
                const r = el.getBoundingClientRect();
                const cs = getComputedStyle(el);
                return {
                    x:Math.round(r.x), y:Math.round(r.y),
                    w:Math.round(r.width), h:Math.round(r.height),
                    display:cs.display, position:cs.position,
                    overflow:cs.overflow, zIndex:cs.zIndex,
                    visible: r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none'
                };
            };
            return {
                viewport: { w: window.innerWidth, h: window.innerHeight },
                mainContent: rect(sel('.main-content')),
                modPage: rect(sel('.mod-page')),
                topbar: rect(sel('.mod-topbar')),
                hero: rect(sel('.mem-hero')),
                heroRing: rect(sel('.mem-hero-ring-wrap')),
                heroCta: rect(sel('.mem-hero-cta')),
                heroStats: rect(sel('.mem-hero-stats')),
                heroActionCol: rect(sel('.mem-hero-action-col')),
                topStrip: rect(sel('.mem-top-strip')),
                controlBar: rect(sel('.mod-control-bar')),
                list: rect(sel('.mod-list')),
                firstGroup: rect(sel('.mod-group')),
                island: rect(sel('.mod-action-island')),
                groupCount: all('.mod-group').length,
                rowCount: all('.mod-toggle-row').length,
                heroChildren: all('.mem-hero > *').map(el => ({
                    tag: el.tagName,
                    cls: el.className,
                    rect: rect(el)
                }))
            };
        });
        console.log('=== 实测布局 ===');
        console.log(JSON.stringify(info, null, 2));
        await p.screenshot({ path: root + '/_smoke_v4.png', fullPage:false });
        console.log('截图: _smoke_v4.png');
        await b.close();
    } catch (e) {
        console.log('FAIL:', e.message);
    }
    srv.close();
});
