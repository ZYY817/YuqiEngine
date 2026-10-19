// File: GameOptimizer.Shell/Startup/SplashWindow.xaml.cs
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace GameOptimizer.Shell.Startup;

/// <summary>
/// 启动加载屏 — 零依赖 WPF Window。
/// 不依赖 DI 容器、ThemeService、LocalizationService 或任何业务服务。
/// </summary>
public partial class SplashWindow : Window
{
    private Action? _onRetry;
    private Action? _onExit;
    private Action? _onCancel;

    // Why: 游戏载入风格的随机提示，让等待不那么无聊
    private static readonly string[] Tips =
    {
        "宇奇引擎可以自动检测你的硬件配置，并推荐最佳的优化方案。",
        "可在各模块中逐项查看、应用并恢复优化设置。",
        "每次优化前，引擎会自动创建系统还原点，随时可以回退。",
        "CPU 核心管理功能可以为游戏分配专属核心，减少后台干扰。",
        "内存优化模块会智能释放不活跃的内存页面，提升游戏帧率。",
        "你可以在设置中切换深色/浅色主题，以及中英文界面语言。",
        "所有优化操作都有详细的操作日志，可在诊断页面查看。",
        "电源优化只使用 Windows 实际支持的模式；不匹配时会提供系统设置引导。",
        "建议以管理员身份运行，以获得完整的系统优化权限。",
        "快捷键 ESC 可以在启动过程中取消加载。",
    };

    public SplashWindow()
    {
        InitializeComponent();
        LoadLogo();
        LoadVersion();
        ShowRandomTip();
    }

    public void SetCancelAction(Action? cancelAction) => _onCancel = cancelAction;

    /// <summary>
    /// 更新状态文本（线程安全）。
    /// </summary>
    public void UpdateStatus(string statusText)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateStatus(statusText));
            return;
        }
        StatusText.Text = statusText ?? string.Empty;
    }

    /// <summary>
    /// 显示警告文本（不中断加载）。
    /// </summary>
    public void ShowWarning(string warningText)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowWarning(warningText));
            return;
        }
        if (string.IsNullOrWhiteSpace(warningText)) return;
        AlertPanel.Visibility = Visibility.Visible;
        WarningText.Text = warningText;
        WarningText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 显示错误信息 + 可选的重试/退出按钮。
    /// </summary>
    public void ShowError(string errorText, Action? onRetry = null, Action? onExit = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowError(errorText, onRetry, onExit));
            return;
        }
        _onRetry = onRetry;
        _onExit = onExit;
        LoadingBar.IsIndeterminate = false;
        LoadingBar.Value = 0;
        AlertPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = errorText ?? "未知错误";
        RetryButton.Visibility = onRetry != null ? Visibility.Visible : Visibility.Collapsed;
        ExitButton.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 淡出动画后关闭窗口。
    /// </summary>
    public async Task FadeOutAsync(int durationMs = 300)
    {
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(() => FadeOutAsync(durationMs)).Task
                .Unwrap().ConfigureAwait(false);
            return;
        }

        if (durationMs <= 0) { SafeClose(); return; }

        var tcs = new TaskCompletionSource<bool>();
        var animation = new DoubleAnimation
        {
            From = 1.0, To = 0.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) => { SafeClose(); tcs.TrySetResult(true); };
        BeginAnimation(OpacityProperty, animation);

        var timeout = Task.Delay(durationMs + 1000);
        if (await Task.WhenAny(tcs.Task, timeout).ConfigureAwait(false) == timeout)
            Dispatcher.Invoke(SafeClose);
    }

    public string GetStatusText()
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(GetStatusText);
        return StatusText.Text;
    }

    private void LoadLogo()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/yuqi-engine-mark.png", UriKind.Absolute);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            LogoImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SplashWindow][LoadLogo] {ex.Message}");
        }
    }

    private void LoadVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(version))
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
            VersionText.Text = $"v{version ?? "0.0.0"}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SplashWindow][LoadVersion] {ex.Message}");
            VersionText.Text = "v0.0.0";
        }
    }

    private void ShowRandomTip()
    {
        try
        {
            var index = Random.Shared.Next(Tips.Length);
            TipText.Text = Tips[index];
        }
        catch
        {
            TipText.Text = Tips[0];
        }
    }

    private void SafeClose()
    {
        try { if (IsLoaded) Close(); }
        catch (Exception ex) { Debug.WriteLine($"[SplashWindow][SafeClose] {ex.Message}"); }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) _onCancel?.Invoke();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => _onRetry?.Invoke();

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_onExit != null) _onExit.Invoke();
        else Application.Current?.Shutdown(1);
    }
}
