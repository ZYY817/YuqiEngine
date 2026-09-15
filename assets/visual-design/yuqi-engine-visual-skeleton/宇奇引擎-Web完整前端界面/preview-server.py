#!/usr/bin/env python3
"""宇奇引擎 Web 预览服务器
   修复：Python 内置 http.server 访问 / 不会自动加载 index.html 的问题
   访问 / 自动重定向到 /index.html
"""
import http.server
import functools
import socketserver
import webbrowser
import threading
import os

PORT = 8765
BIND = '127.0.0.1'

class YuqiHandler(http.server.SimpleHTTPRequestHandler):
    """自定义处理器：/ 自动 302 到 /index.html"""
    def do_GET(self):
        if self.path == '/' or self.path == '':
            self.send_response(302)
            self.send_header('Location', '/index.html')
            self.end_headers()
            return
        super().do_GET()

    def log_message(self, fmt, *args):
        # 简化日志，只显示请求路径和状态
        print(f"  [{self.log_date_time_string()}] {args[0]} {args[1]} {args[2] if len(args) > 2 else ''}")

def main():
    os.chdir(os.path.dirname(os.path.abspath(__file__)))

    handler = functools.partial(YuqiHandler, directory=os.getcwd())
    with socketserver.TCPServer((BIND, PORT), handler) as httpd:
        print()
        print('  ========================================')
        print('   宇奇引擎 Web 预览  Yuqi Engine Preview')
        print('  ========================================')
        print()
        print(f'   本地服务器已启动')
        print(f'   端口: {PORT}')
        print(f'   地址: http://{BIND}:{PORT}/')
        print()
        print('   提示：关闭此窗口即可停止服务器')
        print('  ----------------------------------------')
        print()

        # 延迟打开浏览器
        def open_browser():
            webbrowser.open(f'http://{BIND}:{PORT}/')
        threading.Timer(1.5, open_browser).start()

        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print('\n   服务器已停止')

if __name__ == '__main__':
    main()
