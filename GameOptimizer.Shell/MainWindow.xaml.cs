using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Diagnostics;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Navigation;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell;

public partial class MainWindow : Window
{
    private readonly TaskCompletionSource _initializationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task InitializationCompleted => _initializationCompleted.Task;
    private static readonly string OfficialGitHubRepository = new AppSettings().GitHubRepo;
    private readonly DashboardViewModel _viewModel;
    private readonly ISystemTrayService _trayService;
    private readonly AppSettings _appSettings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly INavigationCatalog _navigationCatalog;
    private readonly INavigationRouter _navigationRouter;
    private readonly IPageFactory _pageFactory;
    private readonly IActiveOperationRegistry _activeOperations;
    private readonly UiSourceInspector _uiSourceInspector;
    private readonly Dictionary<string, ContentControl> _hosts;
    private NavigationSectionModel? _selectedNavigationSection;
    private bool _suppressNavigationSelection;
    private bool _suppressModuleNavigation;
    private bool _closeApproved;
    private bool _closeCheckRunning;

    // Isolated screenshot probes must render their requested page without waiting for the
    // overview's full machine-wide status and health scan. The page itself still performs
    // its normal read-only initialization; this only skips unrelated dashboard work.
    private static bool IsAcceptanceUiInstance => string.Equals(
        Environment.GetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_MODE"),
        "1",
        StringComparison.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<NavigationSectionModel> VisibleNavigationSections => _navigationCatalog.VisibleSections;

    public NavigationSectionModel? SelectedNavigationSection
    {
        get => _selectedNavigationSection;
        private set
        {
            if (ReferenceEquals(_selectedNavigationSection, value)) return;
            _selectedNavigationSection = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNavigationSection)));
        }
    }

    public MainWindow(
        DashboardViewModel viewModel,
        ISystemTrayService trayService,
        INavigationCatalog navigationCatalog,
        INavigationRouter navigationRouter,
        IPageFactory pageFactory,
        IAppSettingsStore settingsStore,
        IActiveOperationRegistry activeOperations)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _trayService = trayService;
        _navigationCatalog = navigationCatalog;
        _navigationRouter = navigationRouter;
        _pageFactory = pageFactory;
        _activeOperations = activeOperations;
        BindNavigationSource();
        _hosts = CreateHostMap();
        _settingsStore = settingsStore;
        _appSettings = settingsStore.Current;
        _uiSourceInspector = new UiSourceInspector(this);
        _settingsStore.Changed += (_, change) =>
        {
            _appSettings.MinimizeToTray = change.Current.MinimizeToTray;
        };
        DataContext = _viewModel;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        _navigationRouter.Navigated += OnNavigated;
        _viewModel.NavigateToPageRequested += OnNavigateToPageRequested;

        // P2-005: 恢复窗口状态
        RestoreWindowState();
        UpdateShellFrameState();
        
        // 订阅语言切换事件
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void BindNavigationSource()
    {
        if (Resources["GroupedNavItems"] is not CollectionViewSource navigationView)
        {
            throw new InvalidOperationException("Main navigation collection view is unavailable.");
        }

        navigationView.Source = _navigationCatalog.VisibleSections;
    }

    private Dictionary<string, ContentControl> CreateHostMap()
    {
        var hosts = new Dictionary<string, ContentControl>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(OverviewPageHost)] = OverviewPageHost,
            [nameof(RestorePointPageHost)] = RestorePointPageHost,
            [nameof(RepairCenterPageHost)] = RepairCenterPageHost,
            [nameof(BackupManagerPageHost)] = BackupManagerPageHost,
            [nameof(CpuAffinityPageHost)] = CpuAffinityPageHost,
            [nameof(CoreAcceleratorPageHost)] = CoreAcceleratorPageHost,
            [nameof(CpuCoreManagementPageHost)] = CpuCoreManagementPageHost,
            [nameof(SystemCleanupPageHost)] = SystemCleanupPageHost,
            [nameof(AppxCleanupPageHost)] = AppxCleanupPageHost,
            [nameof(HardwareInfoPageHost)] = HardwareInfoPageHost,
            [nameof(NetworkTestPageHost)] = NetworkTestPageHost,
            [nameof(CpuModulePageHost)] = CpuModulePageHost,
            [nameof(GraphicsModulePageHost)] = GraphicsModulePageHost,
            [nameof(NetworkModulePageHost)] = NetworkModulePageHost,
            [nameof(PrivacyModulePageHost)] = PrivacyModulePageHost,
            [nameof(SystemModulePageHost)] = SystemModulePageHost,
            [nameof(ExplorerModulePageHost)] = ExplorerModulePageHost,
            [nameof(TasksModulePageHost)] = TasksModulePageHost,
            [nameof(ServicesModulePageHost)] = ServicesModulePageHost,
            [nameof(PeripheralModulePageHost)] = PeripheralModulePageHost,
            [nameof(AudioOptimizationPageHost)] = AudioOptimizationPageHost,
            [nameof(PerformanceBoostPageHost)] = PerformanceBoostPageHost,
            [nameof(MemoryOptimizationPageHost)] = MemoryOptimizationPageHost,
            [nameof(PowerPlanEditorPageHost)] = PowerPlanEditorPageHost,
            [nameof(EsportsModePageHost)] = EsportsModePageHost,
            [nameof(SettingsPageHost)] = SettingsPageHost,
            [nameof(ModulePageHost)] = ModulePageHost
        };

        if (hosts.Count != _navigationCatalog.Routes.Count ||
            _navigationCatalog.Routes.Any(route => !hosts.ContainsKey(route.HostName)))
        {
            throw new InvalidOperationException("导航路由与 XAML 宿主映射不完整或存在重复。");
        }

        foreach (var host in hosts.Values)
        {
            host.Content = null;
            host.Visibility = Visibility.Hidden;
        }

        return hosts;
    }
    
    // P2-005: 恢复窗口位置和大小
    // File: GameOptimizer.Shell/MainWindow.xaml.cs
    private void RestoreWindowState()
    {
        try
        {
            if (!double.IsNaN(_appSettings.WindowLeft) && !double.IsNaN(_appSettings.WindowTop))
            {
                // Why: VirtualScreen 只反映当前连接的显示器总范围，
                // 但用户可能拔掉了副屏，导致保存的坐标在负数区域或超出当前范围。
                // 还需要检查 VirtualScreenLeft/Top（多显示器时可能为负值）。
                var vsLeft = SystemParameters.VirtualScreenLeft;
                var vsTop = SystemParameters.VirtualScreenTop;
                var vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
                var vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

                var left = _appSettings.WindowLeft;
                var top = _appSettings.WindowTop;

                // 窗口至少有 100px 在可见区域内
                if (left >= vsLeft - 100 && left < vsRight - 100 &&
                    top >= vsTop - 100 && top < vsBottom - 100)
                {
                    Left = left;
                    Top = top;
                    WindowStartupLocation = WindowStartupLocation.Manual;
                }
                // else: 坐标不合法，使用默认 CenterScreen
            }

            if (_appSettings.WindowWidth >= 800 && _appSettings.WindowHeight >= 600)
            {
                // 限制不超过当前虚拟屏幕尺寸
                Width = Math.Min(_appSettings.WindowWidth, SystemParameters.VirtualScreenWidth);
                Height = Math.Min(_appSettings.WindowHeight, SystemParameters.VirtualScreenHeight);
            }

            if (_appSettings.WindowMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex)
        {
            // 窗口状态恢复失败不应阻止启动，使用默认位置
            System.Diagnostics.Debug.WriteLine($"[MainWindow][RestoreWindowState] {ex.Message}");
        }
    }
    
    // P2-005: 保存窗口位置和大小
    private void SaveWindowState()
    {
        if (WindowState == WindowState.Normal)
        {
            _appSettings.WindowLeft = Left;
            _appSettings.WindowTop = Top;
            _appSettings.WindowWidth = Width;
            _appSettings.WindowHeight = Height;
        }
        _appSettings.WindowMaximized = WindowState == WindowState.Maximized;
        _appSettings.Save();
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        var selectedKey = _navigationRouter.CurrentKey == "module-detail"
            ? _navigationRouter.LastVisibleKey
            : _navigationRouter.CurrentKey;
        var section = _navigationCatalog.VisibleSections.FirstOrDefault(item =>
            string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
        if (section is not null)
        {
            SelectedNavigationSection = section;
        }

        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        // 更新工具提示
        if (FindName("AboutButton") is Button aboutBtn)
            aboutBtn.ToolTip = loc.CurrentLanguage == "en-US" ? "Author information" : "作者信息";
        if (FindName("SupportAuthorHeaderButton") is Button supportHeaderBtn)
            supportHeaderBtn.ToolTip = loc.CurrentLanguage == "en-US" ? "Support the author" : "支持作者";
        if (FindName("GitHubProjectButton") is Button githubBtn)
            githubBtn.ToolTip = loc.CurrentLanguage == "en-US" ? "Open GitHub project" : "打开 GitHub 项目";
        if (FindName("FeedbackButton") is Button feedbackBtn)
            feedbackBtn.ToolTip = loc.Get("About.FeedbackGitHub");
        if (FindName("GitHubProjectText") is TextBlock githubText)
            githubText.Text = loc.CurrentLanguage == "en-US" ? "GitHub" : "GitHub 项目";
        if (FindName("AboutButtonText") is TextBlock aboutText)
            aboutText.Text = loc.CurrentLanguage == "en-US" ? "Author" : "作者信息";
        if (FindName("SupportAuthorHeaderButtonText") is TextBlock supportHeaderText)
            supportHeaderText.Text = loc.CurrentLanguage == "en-US" ? "Support" : "支持作者";
        if (FindName("FeedbackButtonText") is TextBlock feedbackText)
            feedbackText.Text = loc.CurrentLanguage == "en-US" ? "Feedback" : "反馈";
        if (FindName("MinimizeButton") is Button minBtn)
            minBtn.ToolTip = loc.Get("Window.Minimize");
        if (FindName("MaximizeButton") is Button maxBtn)
            maxBtn.ToolTip = loc.Get("Window.Maximize");
        if (FindName("CloseButton") is Button closeBtn)
            closeBtn.ToolTip = loc.Get("Window.Close");
        if (FindName("SettingsButton") is Button settingsBtn)
            settingsBtn.ToolTip = loc.Get("Window.Settings");
        if (FindName("SupportAuthorButton") is Button supportBtn)
            supportBtn.ToolTip = loc.Get("Sidebar.SupportAuthor");
            
        // 更新侧边栏状态文本 - 根据管理员状态显示不同文字
        if (FindName("AdminModeText") is TextBlock adminText)
        {
            var isAdmin = _viewModel?.IsAdministrator ?? false;
            adminText.Text = loc.Get(isAdmin ? "Window.AdminMode" : "Window.NormalMode");
        }
        
        // 更新状态指示灯颜色
        if (FindName("AdminStatusDot") is Ellipse statusDot)
        {
            var isAdmin = _viewModel?.IsAdministrator ?? false;
            var color = isAdmin ? Color.FromRgb(0x22, 0xC5, 0x5E) : Color.FromRgb(0xF5, 0x9E, 0x0B); // 绿色/橙色
            statusDot.Fill = new SolidColorBrush(color);
            if (statusDot.Effect is DropShadowEffect effect)
                effect.Color = color;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        // PageDiagnosticService.ClearLog(); // 暂时禁用，以便查看所有日志
        PageDiagnosticService.Log("MainWindow", "=== 应用启动 ===");
        
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        
        // 初始化 Toast 通知服务
        ToastService.Instance.Initialize(ToastContainer);

        // 首屏必须先可见；系统检测、游戏识别等耗时初始化在概览已经呈现后进行。
        _navigationRouter.Navigate("overview");
        UpdateLocalizedTexts();
        
        try
        {
            if (IsAcceptanceUiInstance)
            {
                PageDiagnosticService.Log(
                    "MainWindow",
                    "Acceptance UI: skip unrelated dashboard telemetry/status/health scan; requested page initializes normally.");
            }
            else
            {
                PageDiagnosticService.Log("MainWindow", "开始初始化 ViewModel...");
                await _viewModel.InitializeAsync();
                PageDiagnosticService.Log("MainWindow", $"ViewModel 初始化完成, OptimizationModules: {_viewModel.OptimizationModules.Count}");
                foreach (var m in _viewModel.OptimizationModules)
                {
                    PageDiagnosticService.Log("MainWindow", $"  - Module: {m.Key} ({m.Title}) - {m.Toggles.Count} toggles");
                }
            }
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("MainWindow", "ViewModel 初始化失败", ex);
        }
        _initializationCompleted.TrySetResult();
        PageDiagnosticService.Log("MainWindow", "=== 应用启动完成 ===");
        
        // 显示欢迎通知
        var loc = LocalizationService.Instance;
        ToastService.Instance.Success(loc.Get("Toast.WelcomeBack"), loc.Get("Toast.AppReady"));
        
        // 不自动触发新手引导，避免首次运行覆盖实际界面。
    }
    
    private async void TriggerOnboardingIfNeeded()
    {
        try
        {
            // Why: WelcomeGuideDialog (App.xaml.cs) 已通过 first_run.flag 处理首次启动引导，
            // 如果 flag 已存在说明首次启动流程已走过，此处不再重复触发
            if (_appSettings.HasShownWelcomeGuide) return;
            if (System.IO.File.Exists(GameOptimizer.Core.Constants.AppPaths.FirstRunFlagPath)) return;
            
            _appSettings.HasShownWelcomeGuide = true;
            _appSettings.Save();
            
            await UserGuideService.Instance.StartMainOnboardingAsync(RootContainer);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow][TriggerOnboardingIfNeeded] {ex.Message}");
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_appSettings.MinimizeToTray)
        {
            _trayService.MinimizeToTray();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_appSettings.MinimizeToTray)
        {
            _trayService.MinimizeToTray();
        }
        else
        {
            Close();
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeApproved) return;

        var active = _activeOperations.Snapshot();
        if (active.Count > 0)
        {
            e.Cancel = true;
            if (_closeCheckRunning) return;
            _closeCheckRunning = true;
            try
            {
                var blockers = active.Where(item => item.ClosePolicy == ActiveOperationClosePolicy.BlockUntilComplete).ToArray();
                if (blockers.Length > 0)
                {
                    InfoDialog.Show(this, "操作仍在进行",
                        "以下恢复关键操作不能安全中断，请等待完成：\n" + string.Join("\n", blockers.Select(item => $"• {item.DisplayName}")),
                        Constants.IconGlyphs.Warning);
                    return;
                }

                var confirmed = ConfirmDialog.Show(this,
                    "关闭前取消正在进行的操作？",
                    string.Join("\n", active.Select(item => $"• {item.DisplayName}")),
                    Constants.IconGlyphs.Warning);
                if (!confirmed) return;

                var closeResult = await _activeOperations.CancelAndDrainAsync(TimeSpan.FromSeconds(15));
                if (!closeResult.CanClose)
                {
                    var recovery = closeResult.Remaining
                        .SelectMany(item => item.Handoff?.ReceiptIds ?? [])
                        .Distinct()
                        .ToArray();
                    var detail = closeResult.TimedOut ? "取消后等待超时，窗口将保持打开。" : "仍有操作未安全结束。";
                    if (recovery.Length > 0) detail += "\n恢复凭据：" + string.Join(", ", recovery);
                    InfoDialog.Show(this, "暂时无法关闭", detail, Constants.IconGlyphs.Warning);
                    return;
                }

                _closeApproved = true;
                Close();
            }
            finally
            {
                _closeCheckRunning = false;
            }
            return;
        }

        // P2-008: 检查是否有操作正在进行
        if (_viewModel?.IsApplyingModule == true)
        {
            var loc = LocalizationService.Instance;
            var confirmed = ConfirmDialog.Show(
                this,
                loc.Get("Window.CloseWhileOptimizing"),
                loc.Get("Controls.Warning"),
                Constants.IconGlyphs.Warning);
            
            if (!confirmed)
            {
                e.Cancel = true;
                return;
            }
        }
        
        // P2-005: 保存窗口状态
        SaveWindowState();
        
        // 取消订阅事件
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        _navigationRouter.Navigated -= OnNavigated;
        
        // P2-039: 取消 ViewModel 的 PropertyChanged 订阅，防止事件泄漏
        if (_viewModel != null)
        {
            _viewModel.NavigateToPageRequested -= OnNavigateToPageRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnStateChanged(object? sender, System.EventArgs e)
    {
        UpdateShellFrameState();
    }

    private void UpdateShellFrameState()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        var useQuietFrame = isMaximized || SystemParameters.HighContrast;

        RootContainer.BorderThickness = isMaximized ? new Thickness(0) : new Thickness(1);
        RootContainer.BorderBrush = SystemParameters.HighContrast
            ? TryFindResource("Theme.Brush.GlassBorder") as Brush
            : TryFindResource("Shell.Frame.Brush.OuterBorder") as Brush;
        ShellFrameInnerHighlight.Visibility = useQuietFrame ? Visibility.Collapsed : Visibility.Visible;
        ShellFrameAmbient.Visibility = useQuietFrame
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AboutDialog { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow][AboutButton_Click] {ex.Message}");
        }
    }

    private void SupportAuthorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AboutDialog(focusSupportSection: true) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow][SupportAuthorButton_Click] {ex.Message}");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 检查点击源是否为按钮或按钮内部元素，如果是则不执行拖动
        if (e.OriginalSource is DependencyObject source)
        {
            var button = FindParent<Button>(source);
            if (button != null)
                return; // 点击的是按钮，不执行拖动
        }
        
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
    
    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typedParent)
                return typedParent;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }



    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        _trayService.MinimizeToTray();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage("settings");
    }

    private void GitHubProjectButton_Click(object sender, RoutedEventArgs e)
        => OpenOfficialGitHubPage();

    private void FeedbackButton_Click(object sender, RoutedEventArgs e)
        => OpenOfficialGitHubPage("issues");

    private void OpenOfficialGitHubPage(string? relativePath = null)
    {
        var repository = OfficialGitHubRepository.Trim().Trim('/');
        var suffix = string.IsNullOrWhiteSpace(relativePath) ? string.Empty : $"/{relativePath.Trim('/')}";
        var url = $"https://github.com/{repository}{suffix}";
        var (process, error) = SafeProcessHelper.StartProcess(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });

        if (process is not null)
        {
            process.Dispose();
            return;
        }

        InfoDialog.Show(this, "打开失败", $"无法打开链接：{error}", Constants.IconGlyphs.Warning);
    }

    public void NavigateToPage(string pageKey)
    {
        var route = _navigationCatalog.Resolve(pageKey);
        if (route is null)
        {
            PageDiagnosticService.Log("MainWindow", $"忽略未知导航键: {pageKey}");
            return;
        }

        if (route.IsVisible && _viewModel.SelectedOptimizationModule is not null)
        {
            _suppressModuleNavigation = true;
            try
            {
                _viewModel.SelectedOptimizationModule = null;
            }
            finally
            {
                _suppressModuleNavigation = false;
            }
        }

        _navigationRouter.Navigate(route.Key);
    }

    private void OnNavigateToPageRequested(string pageKey)
    {
        NavigateToPage(pageKey);
    }

    private void OnNavigated(object? sender, NavigationChangedEventArgs e)
    {
        if (!_hosts.TryGetValue(e.Route.HostName, out var host))
        {
            PageDiagnosticService.Log("MainWindow", $"路由宿主不存在: {e.Route.HostName}");
            return;
        }

        foreach (var pageHost in _hosts.Values)
        {
            pageHost.BeginAnimation(OpacityProperty, null);
            pageHost.Opacity = 1;
            pageHost.Visibility = Visibility.Hidden;
            Panel.SetZIndex(pageHost, 0);
        }

        var page = _pageFactory.GetOrCreate(e.Route);
        host.Content = page;
        host.BeginAnimation(OpacityProperty, null);
        host.Opacity = 1;
        host.Visibility = Visibility.Visible;
        Panel.SetZIndex(host, 1);
        host.UpdateLayout();

        // 氛围色只归属“功能模块”；概览、工具箱与设置使用安静的基础表面。
        AmbientBackdrop.Visibility = string.Equals(e.Route.GroupKey, "modules", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Why: 切页淡入（仅 opacity，160ms）。旧宿主即时隐藏后新页同步创建，
        // 无过渡会让人感觉背景/内容"闪没又闪现"；常驻背景层 + 淡入消除突兀感。
        if (SystemParameters.ClientAreaAnimation)
        {
            host.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase(),
                    FillBehavior = FillBehavior.Stop
                });
        }
        else
        {
            host.Opacity = 1;
        }

        PageDiagnosticService.Log(
            "MainWindow",
            $"导航宿主状态: route={e.Route.Key}; host={e.Route.HostName}; "
            + $"content={page.GetType().Name}; dataContext={page.DataContext?.GetType().Name ?? "null"}; "
            + $"visibility={host.Visibility}; opacity={host.Opacity:F2}; zIndex={Panel.GetZIndex(host)}; "
            + $"size={host.ActualWidth:F0}x{host.ActualHeight:F0}");

        if (e.Route.IsVisible)
        {
            var section = _navigationCatalog.VisibleSections.FirstOrDefault(item =>
                string.Equals(item.Key, e.Route.Key, StringComparison.OrdinalIgnoreCase));
            if (section is not null)
            {
                _suppressNavigationSelection = true;
                try
                {
                    SelectedNavigationSection = section;
                    PrimaryNavigationList.ScrollIntoView(section);
                    _ = Dispatcher.InvokeAsync(UpdateNavigationSelectionIndicator, DispatcherPriority.Loaded);
                }
                finally
                {
                    _suppressNavigationSelection = false;
                }
            }
        }

        PageDiagnosticService.Log("MainWindow", $"导航完成: {e.Route.Key} -> {e.Route.HostName}");
    }
    
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardViewModel.SelectedOptimizationModule))
        {
            return;
        }

        if (_suppressModuleNavigation) return;

        if (_viewModel.SelectedOptimizationModule is not null)
        {
            _navigationRouter.Navigate("module-detail");
        }
        else if (string.Equals(_navigationRouter.CurrentKey, "module-detail", StringComparison.OrdinalIgnoreCase))
        {
            _navigationRouter.Navigate(_navigationRouter.LastVisibleKey);
        }
    }
    
    private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressNavigationSelection
            && sender is ListBox listBox
            && listBox.SelectedItem is NavigationSectionModel section
            && !string.Equals(_navigationRouter.CurrentKey, section.Key, StringComparison.OrdinalIgnoreCase))
        {
            NavigateToPage(section.Key);
        }

        // The selected row owns its complete visual state. Keep the retired shared overlay hidden.
        _ = Dispatcher.InvokeAsync(UpdateNavigationSelectionIndicator, DispatcherPriority.Loaded);
    }

    private void PrimaryNavigationList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressNavigationSelection
            || sender is not ListBox listBox
            || e.OriginalSource is not DependencyObject originalSource)
        {
            return;
        }

        var item = ItemsControl.ContainerFromElement(listBox, originalSource) as ListBoxItem;
        if (item?.DataContext is not NavigationSectionModel section
            || string.Equals(_navigationRouter.CurrentKey, section.Key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Some WPF templates can retain the same selected item while the mouse is released on a row.
        // Route the concrete row as a fallback so pointer navigation is never dependent on SelectionChanged alone.
        NavigateToPage(section.Key);
    }

    private void UpdateNavigationSelectionIndicator()
    {
        NavigationSelectionIndicator.Visibility = Visibility.Collapsed;
        NavigationSelectionShadow.Visibility = Visibility.Collapsed;
        NavigationSelectionIndicator.Opacity = 0;
        NavigationSelectionShadow.Opacity = 0;
    }
    


    private void SidebarScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Capture wheel input for the complete sidebar, not only the thin scroll track.
        // This keeps tools below the fold reachable while the pointer is over a nav row,
        // a group label, or the persistent status footer.
        SidebarScrollViewer.ScrollToVerticalOffset(
            Math.Max(0, SidebarScrollViewer.VerticalOffset - e.Delta / 3.0));
        e.Handled = true;
    }
}
