// File: GameOptimizer.Shell/Services/WindowVisibilityGuard.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 窗口显示保障服务 - 确保窗口始终可见和可访问
/// </summary>
public class WindowVisibilityGuard
{
    private readonly ILogger<WindowVisibilityGuard> _logger;
    
    // Windows API 声明
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    
    public WindowVisibilityGuard(ILogger<WindowVisibilityGuard> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// 确保窗口在可见区域内
    /// </summary>
    public void EnsureWindowVisible(Window window)
    {
        try
        {
            _logger.LogInformation("检查窗口可见性...");
            
            // 获取窗口句柄
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                _logger.LogWarning("窗口句柄无效，跳过可见性检查");
                return;
            }
            
            // 检查窗口是否最小化
            if (IsIconic(hwnd))
            {
                _logger.LogInformation("窗口被最小化，正在恢复...");
                ShowWindow(hwnd, SW_RESTORE);
            }
            
            // 检查窗口位置是否在屏幕范围内
            if (!IsWindowInVisibleArea(window))
            {
                _logger.LogWarning("窗口位置超出屏幕范围，正在调整...");
                MoveWindowToVisibleArea(window);
            }
            
            // 确保窗口显示
            if (!window.IsVisible)
            {
                _logger.LogInformation("窗口不可见，正在显示...");
                window.Show();
            }
            
            _logger.LogInformation("窗口可见性检查完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "确保窗口可见时发生错误");
        }
    }
    
    /// <summary>
    /// 强制窗口前置
    /// </summary>
    public void ForceWindowToFront(Window window)
    {
        try
        {
            _logger.LogInformation("强制窗口前置...");
            
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                _logger.LogWarning("窗口句柄无效，无法前置");
                return;
            }
            
            // 如果窗口最小化，先恢复
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }
            
            // 激活窗口
            window.Activate();
            
            // 使用 Windows API 强制前置
            SetForegroundWindow(hwnd);
            
            // 确保窗口在最顶层（暂时）
            window.Topmost = true;
            window.Topmost = false;
            
            _logger.LogInformation("窗口已前置");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "强制窗口前置时发生错误");
        }
    }
    
    /// <summary>
    /// 诊断并修复窗口状态异常
    /// </summary>
    public void DiagnoseAndFixWindowState(Window window)
    {
        try
        {
            _logger.LogInformation("开始诊断窗口状态...");
            
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                _logger.LogError("窗口句柄无效 (IntPtr.Zero)");
                return;
            }
            
            // 诊断信息
            var diagnostics = new
            {
                IsVisible = window.IsVisible,
                IsLoaded = window.IsLoaded,
                WindowState = window.WindowState,
                Left = window.Left,
                Top = window.Top,
                Width = window.Width,
                Height = window.Height,
                IsIconic = IsIconic(hwnd),
                ScreenCount = System.Windows.Forms.Screen.AllScreens.Length,
                PrimaryScreenBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            };
            
            _logger.LogInformation("窗口诊断信息: {@Diagnostics}", diagnostics);
            
            // 修复策略
            if (!window.IsVisible)
            {
                _logger.LogWarning("窗口不可见，尝试显示...");
                window.Show();
            }
            
            if (IsIconic(hwnd))
            {
                _logger.LogWarning("窗口被最小化，尝试恢复...");
                ShowWindow(hwnd, SW_RESTORE);
            }
            
            if (!IsWindowInVisibleArea(window))
            {
                _logger.LogWarning("窗口位置异常，尝试移动到可见区域...");
                MoveWindowToVisibleArea(window);
            }
            
            // 强制前置
            ForceWindowToFront(window);
            
            _logger.LogInformation("窗口状态诊断和修复完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "诊断窗口状态时发生错误");
        }
    }
    
    /// <summary>
    /// 记录窗口显示诊断日志
    /// </summary>
    public void LogWindowDiagnostics(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            
            var info = new
            {
                WindowTitle = window.Title,
                IsVisible = window.IsVisible,
                IsLoaded = window.IsLoaded,
                WindowState = window.WindowState,
                Position = new { window.Left, window.Top },
                Size = new { window.Width, window.Height },
                Handle = hwnd,
                IsIconic = hwnd != IntPtr.Zero && IsIconic(hwnd),
                Screens = System.Windows.Forms.Screen.AllScreens.Length,
                PrimaryScreen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds,
                WorkingArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
            };
            
            _logger.LogInformation("窗口诊断日志: {@WindowInfo}", info);
            
            // 检查窗口是否在任何屏幕上
            var isOnAnyScreen = false;
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var screenBounds = screen.Bounds;
                if (window.Left >= screenBounds.Left && 
                    window.Left < screenBounds.Right &&
                    window.Top >= screenBounds.Top && 
                    window.Top < screenBounds.Bottom)
                {
                    isOnAnyScreen = true;
                    _logger.LogInformation("窗口位于屏幕: {DeviceName} ({Bounds})", 
                        screen.DeviceName, screen.Bounds);
                    break;
                }
            }
            
            if (!isOnAnyScreen)
            {
                _logger.LogWarning("警告: 窗口不在任何可见屏幕上！");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录窗口诊断日志时发生错误");
        }
    }
    
    // ==================== 私有辅助方法 ====================
    
    /// <summary>
    /// 检查窗口是否在可见区域内
    /// </summary>
    private bool IsWindowInVisibleArea(Window window)
    {
        try
        {
            // 检查窗口是否在任何屏幕的工作区内
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var workingArea = screen.WorkingArea;
                
                // 窗口左上角在屏幕内
                if (window.Left >= workingArea.Left && 
                    window.Left < workingArea.Right - 100 && // 至少100像素可见
                    window.Top >= workingArea.Top && 
                    window.Top < workingArea.Bottom - 100)
                {
                    return true;
                }
            }
            
            return false;
        }
        catch
        {
            return true; // 如果检查失败，假设窗口可见
        }
    }
    
    /// <summary>
    /// 将窗口移动到可见区域
    /// </summary>
    private void MoveWindowToVisibleArea(Window window)
    {
        try
        {
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
            if (primaryScreen == null) return;
            
            var workingArea = primaryScreen.WorkingArea;
            
            // 计算居中位置
            var centerX = workingArea.Left + (workingArea.Width - window.Width) / 2;
            var centerY = workingArea.Top + (workingArea.Height - window.Height) / 2;
            
            // 确保窗口完全在屏幕内
            var newLeft = Math.Max(workingArea.Left, Math.Min(centerX, workingArea.Right - window.Width));
            var newTop = Math.Max(workingArea.Top, Math.Min(centerY, workingArea.Bottom - window.Height));
            
            _logger.LogInformation("移动窗口从 ({OldLeft}, {OldTop}) 到 ({NewLeft}, {NewTop})", 
                window.Left, window.Top, newLeft, newTop);
            
            window.Left = newLeft;
            window.Top = newTop;
            
            // 如果窗口有句柄，使用 Windows API 确保位置更新
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOP, (int)newLeft, (int)newTop, 0, 0, 
                    SWP_NOSIZE | SWP_NOZORDER);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移动窗口到可见区域时发生错误");
        }
    }
}
