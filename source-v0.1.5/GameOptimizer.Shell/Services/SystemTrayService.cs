using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using GameOptimizer.Core.Localization;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services;

public interface ISystemTrayService
{
    bool IsMinimizedToTray { get; }
    void Initialize(Window mainWindow);
    void MinimizeToTray();
    void RestoreFromTray();
    void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info);
    void Dispose();
}

public sealed class SystemTrayService : ISystemTrayService, IDisposable
{
    private readonly ILogger<SystemTrayService> _logger;
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;
    private bool _isMinimizedToTray;
    private bool _disposed;

    public bool IsMinimizedToTray => _isMinimizedToTray;

    public SystemTrayService(ILogger<SystemTrayService> logger)
    {
        _logger = logger;
    }

    public void Initialize(Window mainWindow)
    {
        if (_notifyIcon != null)
        {
            _logger.LogWarning("[SystemTray] 托盘服务已初始化，跳过重复初始化");
            return;
        }
        
        _mainWindow = mainWindow;

        _notifyIcon = new NotifyIcon
        {
            Text = "宇奇引擎",
            Visible = false
        };

        // 尝试加载图标
        try
        {
            var trayIconResource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/tray.ico", UriKind.Absolute));
            if (trayIconResource != null)
            {
                using var sourceIcon = new Icon(trayIconResource.Stream);
                _notifyIcon.Icon = (Icon)sourceIcon.Clone();
            }
            else
            {
                var executablePath = Assembly.GetEntryAssembly()?.Location
                    ?? Assembly.GetExecutingAssembly().Location;
                _notifyIcon.Icon = Icon.ExtractAssociatedIcon(executablePath)
                    ?? SystemIcons.Application;
            }
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        // 创建右键菜单
        var loc = LocalizationService.Instance;
        var contextMenu = new ContextMenuStrip();
        
        var showItem = new ToolStripMenuItem(loc.Get("Tray.ShowWindow"));
        showItem.Click += (_, _) => RestoreFromTray();
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem(loc.Get("Tray.Exit"));
        exitItem.Click += (_, _) =>
        {
            _notifyIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        };
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        _logger.LogInformation("[SystemTray] 托盘服务已初始化");
    }

    public void MinimizeToTray()
    {
        if (_mainWindow == null || _notifyIcon == null) return;

        _mainWindow.Hide();
        _notifyIcon.Visible = true;
        _isMinimizedToTray = true;

        var loc = LocalizationService.Instance;
        ShowBalloonTip("宇奇引擎", loc.Get("Tray.MinimizedToTray"), ToolTipIcon.Info);
        _logger.LogInformation("[SystemTray] Window minimized to tray");
    }

    public void RestoreFromTray()
    {
        if (_mainWindow == null || _notifyIcon == null) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _notifyIcon.Visible = false;
        _isMinimizedToTray = false;

        _logger.LogInformation("[SystemTray] 窗口已从托盘恢复");
    }

    public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
