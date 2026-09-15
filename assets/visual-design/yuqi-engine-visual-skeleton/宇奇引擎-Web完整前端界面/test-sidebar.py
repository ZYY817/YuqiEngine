#!/usr/bin/env python3
"""侧边栏浮层位置自动化验证脚本

启动本地 preview-server，使用 Playwright 无头浏览器打开 index.html，
然后依次点击所有 nav-item，每次点击后读取浮层与 item 的真实位置，
校验浮层是否对齐到被点击的 item。

运行：  python test-sidebar.py
依赖：  pip install playwright && playwright install chromium
"""
import asyncio
import http.server
import functools
import socketserver
import threading
import os
import sys

PORT = 19987
BIND = '127.0.0.1'
URL = f'http://{BIND}:{PORT}/index.html'

# 校验阈值：浮层 top 与 item top 的最大允许偏差（像素）
TOLERANCE = 2.0


def start_server():
    """后台线程启动静态文件服务器"""
    os.chdir(os.path.dirname(os.path.abspath(__file__)))

    class Handler(http.server.SimpleHTTPRequestHandler):
        def do_GET(self):
            if self.path == '/' or self.path == '':
                self.send_response(302)
                self.send_header('Location', '/index.html')
                self.end_headers()
                return
            super().do_GET()

        def log_message(self, fmt, *args):
            pass  # 静默日志

    handler = functools.partial(Handler, directory=os.getcwd())
    httpd = socketserver.TCPServer((BIND, PORT), handler)
    th = threading.Thread(target=httpd.serve_forever, daemon=True)
    th.start()
    return httpd


async def run_tests():
    from playwright.async_api import async_playwright

    print(f'[server] 启动于 {URL}')
    httpd = start_server()

    pass_cnt = 0
    fail_cnt = 0
    failures = []

    try:
        async with async_playwright() as p:
            browser = await p.chromium.launch(headless=True)
            context = await browser.new_context(viewport={'width': 1280, 'height': 800})
            page = await context.new_page()

            # 监听控制台输出（看 DEBUG 日志）
            page.on('console', lambda msg: print(f'  [console.{msg.type}] {msg.text}') if msg.type in ('error', 'warning') else None)

            print(f'[browser] 加载页面...')
            await page.goto(URL, wait_until='networkidle')
            # 等待 side-bar 组件挂载
            await page.wait_for_selector('side-bar .nav-item', timeout=5000)
            await asyncio.sleep(0.6)  # 等首帧浮层定位

            # 收集所有 nav-item 的标签
            labels = await page.eval_on_selector_all(
                'side-bar .nav-item',
                '''items => items.map(it => it.querySelector('.nav-label')?.textContent?.trim() || '?')'''
            )
            print(f'[info] 共 {len(labels)} 个 nav-item: {labels}')

            # 测试函数：测量浮层与某个 item 的对齐情况
            async def measure(label_to_check):
                return await page.evaluate(
                    '''(label) => {
                        const sb = document.querySelector('side-bar');
                        const items = sb.querySelectorAll('.nav-item');
                        let target = null;
                        for (const it of items) {
                            if (it.querySelector('.nav-label')?.textContent?.trim() === label) {
                                target = it; break;
                            }
                        }
                        if (!target) return { error: 'item not found: ' + label };
                        const bg = sb.querySelector('#navActiveBg');
                        const nav = sb.querySelector('#sidebarNav');
                        const itemRect = target.getBoundingClientRect();
                        const bgRect = bg.getBoundingClientRect();
                        const navRect = nav.getBoundingClientRect();
                        return {
                            label,
                            itemTop: itemRect.top,
                            itemHeight: itemRect.height,
                            bgTop: bgRect.top,
                            bgHeight: bgRect.height,
                            navScrollTop: nav.scrollTop,
                            transform: bg.style.transform,
                            opacity: bg.style.opacity,
                            // 浮层是否对齐 item（中心点对齐，更宽容）
                            itemCenter: itemRect.top + itemRect.height / 2,
                            bgCenter: bgRect.top + bgRect.height / 2,
                        };
                    }''',
                    label_to_check
                )

            # ========== 测试 1：默认 active 项（系统概览）位置 ==========
            print('\n=== 测试 1: 初始 active 项位置 ===')
            r = await measure('系统概览')
            diff = abs(r['itemCenter'] - r['bgCenter'])
            status = 'PASS' if diff < TOLERANCE else 'FAIL'
            print(f"  [{status}] 系统概览  itemCenter={r['itemCenter']:.2f} bgCenter={r['bgCenter']:.2f} diff={diff:.2f}px")
            print(f"           transform={r['transform']} scrollTop={r['navScrollTop']}")
            if status == 'PASS':
                pass_cnt += 1
            else:
                fail_cnt += 1
                failures.append(('init-系统概览', r))

            # ========== 测试 2：依次点击所有 nav-item ==========
            print('\n=== 测试 2: 依次点击所有 nav-item ===')
            for idx, label in enumerate(labels):
                # 让目标项可见（滚到视口）
                await page.evaluate(
                    '''(idx) => {
                        const items = document.querySelectorAll('side-bar .nav-item');
                        items[idx]?.scrollIntoView({ block: 'center', behavior: 'instant' });
                    }''',
                    idx
                )
                await asyncio.sleep(0.15)
                # 点击
                await page.evaluate(
                    '''(idx) => {
                        const items = document.querySelectorAll('side-bar .nav-item');
                        items[idx]?.click();
                    }''',
                    idx
                )
                # 等浮层 transition 完成（CSS 动画 ~400ms）
                await asyncio.sleep(0.55)
                r = await measure(label)
                if 'error' in r:
                    print(f"  [FAIL] [{idx}] {label}  {r['error']}")
                    fail_cnt += 1
                    failures.append((f'click-{idx}-{label}', r))
                    continue
                diff = abs(r['itemCenter'] - r['bgCenter'])
                # 还要确保浮层在可视区内（item 滚到中心后浮层应该也在可视区）
                in_view = (r['bgTop'] >= -1 and r['bgTop'] + r['bgHeight'] <= 800 + 1)
                status = 'PASS' if (diff < TOLERANCE and in_view) else 'FAIL'
                marker = '✓' if status == 'PASS' else '✗'
                print(f"  [{status}] {marker} [{idx:2d}] {label:10s}  diff={diff:6.2f}px  bgTop={r['bgTop']:7.2f}  itemTop={r['itemTop']:7.2f}  scrollTop={r['navScrollTop']:6.1f}")
                if status == 'PASS':
                    pass_cnt += 1
                else:
                    fail_cnt += 1
                    failures.append((f'click-{idx}-{label}', r))

            # ========== 测试 3：滚到底部再点击第一个项 ==========
            print('\n=== 测试 3: 滚到底部 → 点击系统概览（应回到顶部位置）===')
            await page.evaluate('document.querySelector("#sidebarNav").scrollTop = 9999')
            await asyncio.sleep(0.2)
            await page.evaluate('''() => {
                const items = document.querySelectorAll('side-bar .nav-item');
                for (const it of items) {
                    if (it.querySelector('.nav-label')?.textContent?.trim() === '系统概览') {
                        it.scrollIntoView({ block: 'center', behavior: 'instant' });
                        it.click();
                        break;
                    }
                }
            }''')
            await asyncio.sleep(0.55)
            r = await measure('系统概览')
            diff = abs(r['itemCenter'] - r['bgCenter'])
            status = 'PASS' if diff < TOLERANCE else 'FAIL'
            print(f"  [{status}] 系统概览  diff={diff:.2f}px  bgTop={r['bgTop']:.2f}  itemTop={r['itemTop']:.2f}  scrollTop={r['navScrollTop']:.1f}")
            if status == 'PASS':
                pass_cnt += 1
            else:
                fail_cnt += 1
                failures.append(('scroll-bottom-click-top', r))

            # 截图保存（成功失败都留证据）
            await page.screenshot(path='test-sidebar-result.png', full_page=False)
            print(f'\n[screenshot] 已保存 test-sidebar-result.png')

            await browser.close()

    finally:
        httpd.shutdown()
        httpd.server_close()
        print('[server] 已停止')

    print('\n' + '=' * 60)
    print(f'  PASS: {pass_cnt}    FAIL: {fail_cnt}')
    print('=' * 60)
    if failures:
        print('\n失败明细：')
        for name, r in failures:
            print(f'  - {name}: {r}')
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(asyncio.run(run_tests()))
