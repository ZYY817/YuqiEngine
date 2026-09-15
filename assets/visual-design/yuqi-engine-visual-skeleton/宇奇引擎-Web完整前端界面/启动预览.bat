@echo off
chcp 65001 >nul
title 宇奇引擎 · Web 预览服务器

echo.
echo  ========================================
echo   宇奇引擎 Web 预览  Yuqi Engine Preview
echo  ========================================
echo.
echo   正在启动本地服务器 ...
echo   端口: 8765
echo   地址: http://127.0.0.1:8765/
echo.
echo   提示：关闭此窗口即可停止服务器
echo  ----------------------------------------
echo.

REM 切到脚本所在目录
cd /d "%~dp0"

REM 启动自定义预览服务器（自动打开浏览器 + 正确加载首页）
python preview-server.py

pause
