// File: GameOptimizer.Shell/Views/MasterModuleTemplate.xaml.cs
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Helpers;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class MasterModuleTemplate : UserControl
{
    private readonly SemaphoreSlim _initLock = new(1, 1); // OPT-015: 并发保护
    private readonly ThreadSafeBatchOperationService _threadSafeBatchService = new(); // Task 9.1: 新的线程安全批量操作服务
    private readonly CentralizedStateManager _stateManager = new(); // Task 9.2: 集中式状态管理器
    private readonly object _operationSync = new();
    private readonly CommandExecutionGate _operationGate = new();
    private DashboardViewModel? _viewModel;
    private OptimizationModuleDefinition? _module;
    private List<OptimizationToggleModel> _allToggles = new();
    private OperationRiskLevel? _currentFilter;
    private string? _currentCategoryFilter;
    private bool _isPopulatingCategoryFilter;
    private bool _isInitialized;
    private bool _isOperating; // 防止并发操作
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _debounceBindCts;
    private CancellationTokenSource? _batchRenderCts;
    private SyncStatusHelper? _syncStatusHelper;
    
    // 主题颜色缓存
    private Color _rowBg, _rowBorder, _rowBgHover, _rowBorderHover;
    private Color _rowBgExtreme, _rowBorderExtreme, _rowBgHoverExtreme, _rowBorderHoverExtreme;
    private Color _iconBg, _iconBorder, _iconBgExtreme, _iconBorderExtreme;
    private Color _btnBg, _btnBgHover;
    private Color _textPrimary, _textMuted, _categoryText;
    
    public static readonly DependencyProperty ModuleKeyProperty =
        DependencyProperty.Register(nameof(ModuleKey), typeof(string), typeof(MasterModuleTemplate),
            new PropertyMetadata(string.Empty, OnModuleKeyChanged));
    
    public static readonly DependencyProperty ModuleTitleProperty =
        DependencyProperty.Register(nameof(ModuleTitle), typeof(string), typeof(MasterModuleTemplate),
            new PropertyMetadata(string.Empty));
    
    public static readonly DependencyProperty ModuleSubtitleProperty =
        DependencyProperty.Register(nameof(ModuleSubtitle), typeof(string), typeof(MasterModuleTemplate),
            new PropertyMetadata(string.Empty));

    public string ModuleKey
    {
        get => (string)GetValue(ModuleKeyProperty);
        set => SetValue(ModuleKeyProperty, value);
    }
    
    public string ModuleTitle
    {
        get => (string)GetValue(ModuleTitleProperty);
        set => SetValue(ModuleTitleProperty, value);
    }
    
    public string ModuleSubtitle
    {
        get => (string)GetValue(ModuleSubtitleProperty);
        set => SetValue(ModuleSubtitleProperty, value);
    }

    public MasterModuleTemplate()
    {
        PageDiagnosticService.Log("MasterModuleTemplate", $"Constructor called, ModuleKey={ModuleKey}");
        InitializeComponent();
        LoadThemeColors();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnVisibilityChanged;
        
        // Task 9.2: 订阅状态变更事件
        _stateManager.StateChanged += OnStateChanged;
    }
    
    /// <summary>
    /// Why: MainWindow 中所有模块页面通过 Visibility 切换，实例永不销毁，
    ///      _isInitialized 只在首次设为 true 后不再重置。
    ///      用户切换页面再回来时，必须强制同步 IsEnabled = IsApplied，
    ///      消除"即将取消/即将应用"的残留状态。
    /// </summary>
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not Visibility newVis || newVis != Visibility.Visible) return;
        if (!_isInitialized || _isOperating) return;
        
        PageDiagnosticService.Log("MasterModuleTemplate",
            $"[{ModuleKey}] OnVisibilityChanged → Visible, syncing toggle states to system actual");
        
        SyncToggleStates();
        BindGroupedToggles();
        UpdateAllCounts();
        
        // Why: 后台异步刷新系统实际状态，完成后再次同步 UI
        RefreshModuleStatusInBackground();
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelActiveOperation();
        _debounceBindCts?.Cancel();
        _debounceBindCts?.Dispose();
        _batchRenderCts?.Cancel();
        _batchRenderCts?.Dispose();
        _syncStatusHelper?.Dispose();
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        IsVisibleChanged -= OnVisibilityChanged;
        
        // Task 9.2: 取消订阅状态变更事件
        _stateManager.StateChanged -= OnStateChanged;
        
        // Why: Unload 时 DataContext 不一定变为 null，需显式取消 ViewModel 事件订阅防止无用回调
        if (DataContext is DashboardViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }
    
    /// <summary>
    /// Task 9.2: 响应CentralizedStateManager的状态变更事件
    /// 当状态管理器中的状态发生变化时，更新对应的Toggle UI
    /// </summary>
    private void OnStateChanged(object? sender, Services.StateChangedEventArgs e)
    {
        // Guard: 只处理当前模块的状态变更
        if (e.ModuleKey != ModuleKey) return;
        if (!_isInitialized) return;
        
        PageDiagnosticService.Log("MasterModuleTemplate", 
            $"[{ModuleKey}] Task 9.2: StateChanged - {e.OperationId}, IsApplied={e.NewState.IsApplied}, IsSelected={e.NewState.IsSelected}");
        
        // 查找对应的Toggle并更新UI状态
        var toggle = _allToggles.FirstOrDefault(t => t.OperationId == e.OperationId);
        if (toggle == null)
        {
            PageDiagnosticService.Log("MasterModuleTemplate", 
                $"[{ModuleKey}] Task 9.2: Toggle not found for {e.OperationId}");
            return;
        }
        
        // Why: 状态变更回写必须尊重状态机与Pending保护，避免“自动勾回/自动取消”
        if (toggle.CurrentState is OptimizationState.Applying or OptimizationState.Restoring)
        {
            PageDiagnosticService.Log("MasterModuleTemplate",
                $"[{ModuleKey}] 跳过StateChanged回写（操作中）: {e.OperationId}");
            return;
        }

        // 先同步系统实际生效状态（内部自带操作中/Pending/冷却期保护）
        toggle.SyncStatus(e.NewState.IsApplied);

        // 若当前本地存在待处理意图，且外部状态是“已收敛快照”(IsSelected==IsApplied)，
        // 说明这次回写可能是旧快照，不覆盖用户意图。
        var looksLikeSettledSnapshot = e.NewState.IsSelected == e.NewState.IsApplied;
        if (toggle.HasPending && looksLikeSettledSnapshot)
        {
            PageDiagnosticService.Log("MasterModuleTemplate",
                $"[{ModuleKey}] 跳过StateChanged选择态覆盖（本地Pending）: {e.OperationId}");
            return;
        }

        // 外部状态是显式用户选择（可能来自其他入口）时，同步勾选态
        if (toggle.IsEnabled != e.NewState.IsSelected)
        {
            toggle.IsEnabled = e.NewState.IsSelected;
        }
        
        // 刷新UI计数器
        UpdateAllCounts();
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        // Why: Invoke 是同步阻塞，如果从后台线程调用且 UI 线程正忙，会死锁。
        //      InvokeAsync 异步投递，不阻塞调用线程。
        _ = Dispatcher.InvokeAsync(() =>
        {
            UpdateLocalizedTexts();
            if (_isInitialized)
            {
                PopulateCategoryFilter();
                BindGroupedToggles();
            }
        });
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        // 根据ModuleKey获取本地化的标题和副标题
        var moduleKeyMap = new Dictionary<string, string>
        {
            ["performance"] = "Performance", // FIX: 添加缺失的performance映射
            ["audio"] = "Audio",
            ["services"] = "Services",
            ["scheduler"] = "TaskScheduler",
            ["tasks"] = "TaskScheduler",
            ["tuning"] = "SystemTuning",
            ["system"] = "SystemTuning",
            ["privacy"] = "Privacy",
            ["peripheral"] = "Peripheral",
            ["network"] = "Network",
            ["cpu"] = "Cpu",
            ["graphics"] = "Graphics",
            ["explorer"] = "Explorer",
            ["memory"] = "Memory"
        };
        
        if (moduleKeyMap.TryGetValue(ModuleKey, out var locKey))
        {
            TitleText.Text = loc.Get($"ModulePage.{locKey}.Title");
            SubtitleText.Text = loc.Get($"ModulePage.{locKey}.Subtitle");
        }
        else
        {
            TitleText.Text = ModuleTitle;
            SubtitleText.Text = ModuleSubtitle;
        }
        
        // 筛选按钮
        if (FindName("FilterAllText") is TextBlock filterAll) filterAll.Text = loc.Get("Module.FilterAll");
        if (FindName("FilterExtremeText") is TextBlock filterExtreme) filterExtreme.Text = loc.Get("Module.FilterExtreme");
        if (FindName("FilterBalancedText") is TextBlock filterBalanced) filterBalanced.Text = loc.Get("Module.FilterBalanced");
        if (FindName("FilterSafeText") is TextBlock filterSafe) filterSafe.Text = loc.Get("Module.FilterSafe");
        
        // 操作按钮
        if (FindName("RestoreDefaultText") is TextBlock restore) restore.Text = loc.Get("Module.RestoreDefault");
        
        // 恢复弹出菜单
        if (FindName("RestoreUserText") is TextBlock restoreUser) restoreUser.Text = loc.Get("Module.RestoreUser");
        if (FindName("RestoreUserDescText") is TextBlock restoreUserDesc) restoreUserDesc.Text = loc.Get("Module.RestoreUserDesc");
        if (FindName("RestoreWindowsText") is TextBlock restoreWin) restoreWin.Text = loc.Get("Module.RestoreWindows");
        if (FindName("RestoreWindowsDescText") is TextBlock restoreWinDesc) restoreWinDesc.Text = loc.Get("Module.RestoreWindowsDesc");
        
        // 底部状态栏
        if (FindName("SelectedLabel") is TextBlock selLabel) selLabel.Text = loc.Get("Module.Selected");
        if (FindName("AppliedLabel") is TextBlock appLabel) appLabel.Text = loc.Get("Module.Applied");
        if (FindName("ApplyBtnText") is TextBlock applyBtn) applyBtn.Text = loc.Get("Module.ApplyChanges");
        
        // 统计标签
        if (FindName("OptCountLabel") is TextBlock optLabel) optLabel.Text = loc.Get("Module.OptimizationCount");

        UpdateWebParityTopbarText();
        
        // 空状态文案（语言切换时刷新）
        if (FindName("EmptyStatePanel") is Border emptyPanel && emptyPanel.Visibility == Visibility.Visible)
            UpdateEmptyState(visible: true);
        
        _syncStatusHelper?.RefreshLocalizedText();
    }
    
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // 当背景色变化时（主题切换），重新加载颜色
        if (e.Property == BackgroundProperty)
        {
            LoadThemeColors();
            if (_isInitialized) BindGroupedToggles();
        }
    }
    
    private void LoadThemeColors()
    {
        // 从主题资源加载颜色
        _rowBg = GetThemeColor("Theme.Row.Background", Color.FromRgb(0x18, 0x18, 0x1B));
        _rowBorder = GetThemeColor("Theme.Row.Border", Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
        _rowBgHover = GetThemeColor("Theme.Row.BackgroundHover", Color.FromRgb(0x1F, 0x1F, 0x23));
        _rowBorderHover = GetThemeColor("Theme.Row.BorderHover", Color.FromArgb(0x40, 0x3B, 0x82, 0xF6));
        
        _rowBgExtreme = GetThemeColor("Theme.Row.BackgroundExtreme", Color.FromRgb(0x1A, 0x14, 0x14));
        _rowBorderExtreme = GetThemeColor("Theme.Row.BorderExtreme", Color.FromArgb(0x40, 0xDC, 0x6B, 0x6B));
        _rowBgHoverExtreme = GetThemeColor("Theme.Row.BackgroundHoverExtreme", Color.FromRgb(0x22, 0x18, 0x18));
        _rowBorderHoverExtreme = GetThemeColor("Theme.Row.BorderHoverExtreme", Color.FromArgb(0x60, 0xDC, 0x6B, 0x6B));
        
        _iconBg = GetThemeColor("Theme.Icon.Background", Color.FromRgb(0x27, 0x27, 0x2A));
        _iconBorder = GetThemeColor("Theme.Icon.Border", Color.FromRgb(0x3F, 0x3F, 0x46));
        _iconBgExtreme = GetThemeColor("Theme.Icon.BackgroundExtreme", Color.FromRgb(0x2D, 0x1A, 0x1A));
        _iconBorderExtreme = GetThemeColor("Theme.Icon.BorderExtreme", Color.FromRgb(0x5C, 0x3A, 0x3A));
        
        _btnBg = GetThemeColor("Theme.Button.Background", Color.FromRgb(0x27, 0x27, 0x2A));
        _btnBgHover = GetThemeColor("Theme.Button.BackgroundHover", Color.FromRgb(0x3F, 0x3F, 0x46));
        
        _textPrimary = GetThemeColor("Theme.Text.PrimaryAlt", Color.FromRgb(0xFA, 0xFA, 0xFA));
        _textMuted = GetThemeColor("Theme.Text.Muted", Color.FromRgb(0x71, 0x71, 0x7A));
        _categoryText = GetThemeColor("Theme.Category.Text", Color.FromRgb(0x71, 0x71, 0x7A));
    }
    
    private Color GetThemeColor(string key, Color fallback)
    {
        try
        {
            if (Application.Current.Resources[key] is Color color)
                return color;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MasterModuleTemplate] Failed to get theme color '{key}': {ex.Message}");
        }
        return fallback;
    }
    
    private static void OnModuleKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MasterModuleTemplate template) return;

        template.ApplyWebParityVisualSkeleton();
        template.TryInitialize();
    }

    // Every route using this shared template must share the same information hierarchy.
    // Module-specific extensions can still add their own live data above the common controls.
    private bool UsesWebParityVisualSkeleton => !string.IsNullOrWhiteSpace(ModuleKey);

    private void ApplyWebParityVisualSkeleton()
    {
        if (!UsesWebParityVisualSkeleton) return;

        var accent = GetModuleAccentColor();
        var accentBrush = new SolidColorBrush(accent);
        var accentSoftBrush = new SolidColorBrush(Color.FromArgb(36, accent.R, accent.G, accent.B));
        var accentBorderBrush = new SolidColorBrush(Color.FromArgb(92, accent.R, accent.G, accent.B));

        PageLayout.Margin = new Thickness(22, 14, 22, 14);
        HeroGrid.Margin = new Thickness(4, 4, 4, 0);
        ModuleTopbar.Visibility = Visibility.Visible;
        UpdateWebParityTopbarText();
        ModuleAccentMarker.Background = accentBrush;
        ModuleTopbarTitle.Foreground = accentBrush;
        ModuleSpecChip.Background = accentSoftBrush;
        ModuleSpecChip.BorderBrush = accentBorderBrush;
        ModuleSpecChipText.Foreground = accentBrush;
        ControlBarCard.Margin = new Thickness(0, 12, 0, 0);
        ControlBarCard.Padding = new Thickness(16, 12, 16, 12);
        ControlBarCard.CornerRadius = new CornerRadius(16);
        ControlBarCard.BorderThickness = new Thickness(1);
        ControlBarCard.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.CardBackground");
        ControlBarCard.SetResourceReference(Border.BorderBrushProperty, "Theme.Brush.CardBorder");

        if (ModuleKey == "memory")
        {
            // The live memory action is the page's primary task, so it precedes risk filtering.
            Grid.SetRow(ModuleExtensionContainer, 1);
            ModuleExtensionContainer.Margin = new Thickness(0, 16, 0, 0);
            Grid.SetRow(ControlBarCard, 2);
            ControlBarCard.Margin = new Thickness(0, 16, 0, 0);
        }

        FilterSurface.BorderThickness = new Thickness(0);
        FilterSurface.CornerRadius = new CornerRadius(10);
        FilterSurface.Padding = new Thickness(4);

        HealthCard.Padding = new Thickness(18, 14, 18, 14);
        HealthCard.CornerRadius = new CornerRadius(16);

        FilterAllCountText.Visibility = Visibility.Visible;
        FilterExtremeCountText.Visibility = Visibility.Visible;
        FilterBalancedCountText.Visibility = Visibility.Visible;
        FilterSafeCountText.Visibility = Visibility.Visible;
    }

    private Color GetModuleAccentColor() => ModuleKey switch
    {
        "memory" => Color.FromRgb(0x22, 0xC5, 0x5E),
        "audio" => Color.FromRgb(0xF4, 0x72, 0xB6),
        "performance" => Color.FromRgb(0xF5, 0x9E, 0x0B),
        "cpu" => Color.FromRgb(0x60, 0xA5, 0xFA),
        "graphics" => Color.FromRgb(0x22, 0xD3, 0xEE),
        "network" => Color.FromRgb(0x38, 0xBD, 0xF8),
        "privacy" => Color.FromRgb(0xA7, 0x8B, 0xFA),
        "system" => Color.FromRgb(0x94, 0xA3, 0xB8),
        "explorer" => Color.FromRgb(0xF5, 0x9E, 0x0B),
        "tasks" => Color.FromRgb(0xFB, 0x92, 0x3C),
        "services" => Color.FromRgb(0x2D, 0xD4, 0xBF),
        "peripheral" => Color.FromRgb(0xF4, 0x72, 0xB6),
        _ => Color.FromRgb(0x2E, 0x88, 0xF2)
    };

    private string GetModuleTopbarTitle() => ModuleKey switch
    {
        "memory" => LocalizationService.Instance.CurrentLanguage == "en-US" ? "Memory Optimization" : "内存优化",
        "audio" => LocalizationService.Instance.CurrentLanguage == "en-US" ? "Audio Optimization" : "音频优化",
        "performance" => LocalizationService.Instance.CurrentLanguage == "en-US" ? "Performance Boost" : "性能加速",
        _ => TitleText.Text
    };

    private string GetModuleTopbarSubtitle()
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        return ModuleKey switch
        {
            "memory" => isEnglish ? "Make every recovery visible instantly" : "让每一次回收都即时可见",
            "audio" => isEnglish ? "Reduce latency · Restore in-game audio detail" : "降低处理延迟 · 还原游戏音效层次",
            "performance" => isEnglish ? "CPU / GPU / system-level tuning" : "聚焦 CPU / GPU / 系统级调校",
            _ => SubtitleText.Text
        };
    }

    private void UpdateWebParityTopbarText()
    {
        if (!UsesWebParityVisualSkeleton) return;
        ModuleTopbarTitle.Text = GetModuleTopbarTitle();
        ModuleTopbarSubtitle.Text = GetModuleTopbarSubtitle();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PageDiagnosticService.Log("MasterModuleTemplate", $"OnLoaded called, ModuleKey={ModuleKey}, ModuleTitle={ModuleTitle}");
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
        TryInitialize();
    }
    
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // OPT-016: 取消旧 ViewModel 的订阅
        if (e.OldValue is DashboardViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        
        // OPT-016: 订阅新 ViewModel 的 PropertyChanged 事件
        if (e.NewValue is DashboardViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
        
        TryInitialize();
    }
    
    /// <summary>
    /// OPT-016: 监听 ViewModel 属性变化，实现跨页面状态同步
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DashboardViewModel vm) return;
        if (!_isInitialized) return;  // 未初始化时忽略
        // Why: 操作期间 ApplyBtn_Click/RestoreOperationsInternalAsync 会自行刷新 UI，
        //      此处跳过防止 BindGroupedToggles 被重复调用 4+ 次导致卡顿
        if (_isOperating) return;
        
        // Why: 必须用 InvokeAsync 替代 Invoke，Invoke 是同步阻塞——
        //      如果后台线程持有 _syncStatusLock 并通过 PropertyChanged 触发此回调，
        //      Dispatcher.Invoke 会等待 UI 线程处理，而 UI 线程可能正在等待 _syncStatusLock，
        //      形成经典死锁。InvokeAsync 是异步投递，不阻塞调用线程。
        _ = Dispatcher.InvokeAsync(() =>
        {
            // OPT-016: 监听模块状态变化
            if (e.PropertyName == nameof(DashboardViewModel.AppliedOptimizationCount))
            {
                PageDiagnosticService.Log("MasterModuleTemplate", 
                    $"[{ModuleKey}] OPT-016: 检测到 AppliedOptimizationCount 变化，刷新UI");
                RefreshUIFromViewModel(vm);
            }
            
            // OPT-016: 监听优化模块集合变化（虽然不太可能，但保险起见）
            if (e.PropertyName == nameof(DashboardViewModel.OptimizationModules))
            {
                PageDiagnosticService.Log("MasterModuleTemplate", 
                    $"[{ModuleKey}] OPT-016: 检测到 OptimizationModules 变化，重新初始化");
                TryInitialize();
            }
        });
    }
    
    /// <summary>
    /// OPT-016: 从 ViewModel 刷新 UI 状态
    /// 当 DashboardViewModel 的状态变化时调用
    /// </summary>
    private void RefreshUIFromViewModel(DashboardViewModel vm)
    {
        if (_module == null) return;
        
        PageDiagnosticService.Log("MasterModuleTemplate", 
            $"[{ModuleKey}] OPT-016: 开始刷新UI");
        
        // 重新获取模块引用（Toggle 状态已由 DashboardViewModel 更新）
        var updatedModule = vm.OptimizationModules.FirstOrDefault(m => m.Key == ModuleKey);
        if (updatedModule == null)
        {
            PageDiagnosticService.LogError("MasterModuleTemplate", 
                $"[{ModuleKey}] OPT-016: 找不到模块");
            return;
        }
        
        _module = updatedModule;
        _allToggles = _module.Toggles.ToList();
        PopulateCategoryFilter();
        
        PageDiagnosticService.Log("MasterModuleTemplate", 
            $"[{ModuleKey}] OPT-016: 模块状态 - Applied={_allToggles.Count(t => t.IsApplied)}, Enabled={_allToggles.Count(t => t.IsEnabled)}");
        
        // 刷新UI - 重新绑定所有Toggle和更新计数器
        BindGroupedToggles();
        Dispatcher.BeginInvoke(UpdateAllCounts, DispatcherPriority.Background);
            PageDiagnosticService.Log("MasterModuleTemplate", 
            $"[{ModuleKey}] OPT-016: UI刷新完成");
    }

    private async void TryInitialize()
    {
        if (_isInitialized || string.IsNullOrEmpty(ModuleKey)) return;
        
        // OPT-015: 并发保护 - 防止多次初始化
        if (!await _initLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            PageDiagnosticService.LogError("MasterModuleTemplate", $"[{ModuleKey}] 初始化超时 - 可能已在初始化中");
            return;
        }
        
        try
        {
            _viewModel = DataContext as DashboardViewModel;
            if (_viewModel == null) return;
            
            _module = _viewModel.OptimizationModules.FirstOrDefault(m => m.Key == ModuleKey);
            if (_module == null)
            {
                PageDiagnosticService.LogError("MasterModuleTemplate", $"Module '{ModuleKey}' not found");
                return;
            }
            
			_isInitialized = true;
			_allToggles = _module.Toggles.ToList();
			PopulateCategoryFilter();
            
            LoadingOverlay.Visibility = Visibility.Visible;
            var loc = LocalizationService.Instance;
            
            try
            {
                // OPT-015: 新的初始化流程
                PageDiagnosticService.Log("MasterModuleTemplate", $"[{ModuleKey}] === OPT-015 初始化开始 ===");
                PageDiagnosticService.Log("MasterModuleTemplate", $"[{ModuleKey}] Toggle数量: {_allToggles.Count}");
                
                // Step 1: 检测系统实际状态（Backend）
                // Why: 只刷新当前模块，不刷新所有模块，避免启动时 spawn 大量进程
                PageDiagnosticService.Log("MasterModuleTemplate", $"[{ModuleKey}] Step 1: 检测当前模块系统实际状态");
                // A status probe can depend on Windows components that are absent or slow.
                // Do not let that read-only probe keep the whole page (and its extension)
                // in a perpetual loading state. Acceptance screenshots use a shorter bound,
                // but retain the real "unknown" state instead of inventing applied values.
                await _viewModel.RefreshModuleStatusAsync(ModuleKey)
                    .WaitAsync(GetInitialStatusProbeTimeout());
                
                // Why: RefreshModuleStatusAsync 内部的 SyncModuleStatusAsyncCore 用 InvokeAsync 
                //      fire-and-forget 投递 SyncStatus 调用，await 返回时 SyncStatus 还没执行。
                //      必须等待 Dispatcher 队列排空，确保 SyncStatus 已执行完毕、IsApplied 已更新。
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                
                // 输出系统状态
                foreach (var toggle in _allToggles.Take(7))
                {
                    PageDiagnosticService.Log("MasterModuleTemplate", 
                        $"[{ModuleKey}] 系统状态: {toggle.OperationId} - IsApplied={toggle.IsApplied}");
                }
                
                // OPT-018: 简化初始化 - 直接同步系统实际状态到UI
                // "开了就是开，关了就是关" - 不再有配置持久化和复杂策略
                PageDiagnosticService.Log("MasterModuleTemplate", $"[{ModuleKey}] Step 2: OPT-018 直接同步系统状态到UI");
                
                foreach (var toggle in _allToggles)
                {
                    // Why: 本地已有待处理意图时，不强制覆写，避免页面回流把用户勾选重置
                    if (!toggle.HasPending)
                    {
                        toggle.IsEnabled = toggle.IsApplied;
                    }

                    toggle.SyncStatus(toggle.IsApplied);
                    
                    // Task 9.2: 同步状态到CentralizedStateManager
                    if (!string.IsNullOrEmpty(toggle.OperationId))
                    {
                        _stateManager.UpdateState(ModuleKey, toggle.OperationId, new OptimizationItemState
                        {
                            IsSelected = toggle.IsEnabled,
                            IsApplied = toggle.IsApplied,
                            LastModified = DateTime.UtcNow
                        });
                    }
                }
                
                PageDiagnosticService.Log("MasterModuleTemplate", $"[{ModuleKey}] OPT-018: UI状态已同步到系统实际状态");
                PageDiagnosticService.Log("MasterModuleTemplate", $"[{ModuleKey}] Task 9.2: 状态已同步到CentralizedStateManager");
                
                // 输出最终状态
                foreach (var toggle in _allToggles.Take(7))
                {
                    PageDiagnosticService.Log("MasterModuleTemplate", 
                        $"[{ModuleKey}] 最终状态: {toggle.OperationId} - IsEnabled={toggle.IsEnabled}, IsApplied={toggle.IsApplied}");
                }
                
                PageDiagnosticService.Log("MasterModuleTemplate", $"[{ModuleKey}] === OPT-015 初始化完成 ===");
                
                var unavailableReadbackCount = _allToggles.Count(toggle => !toggle.IsReadbackKnown);
                StatusText.Text = unavailableReadbackCount == 0
                    ? loc.Get("Module.SystemReady")
                    : loc.Get("Module.PartialStateUnavailable", unavailableReadbackCount);
                StatusDot.Fill = unavailableReadbackCount == 0
                    ? ThemeColors.SuccessLightBrush
                    : ThemeColors.WarningBrush;
                
                _syncStatusHelper = new SyncStatusHelper(StatusDot, StatusText, Dispatcher);
            }
            catch (Exception ex)
            {
                StatusText.Text = loc.Get("Module.DetectionFailed");
                StatusDot.Fill = ThemeColors.DangerBrush;
                PageDiagnosticService.LogError("MasterModuleTemplate", "Status detection failed", ex);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            
            BindGroupedToggles();
            UpdateAllCounts();
            
            // Task 9.3: 加载模块扩展
            LoadModuleExtension();
            
            PageDiagnosticService.LogLoaded("MasterModuleTemplate", true, $"{_module.Toggles.Count} toggles loaded for {ModuleKey}");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("MasterModuleTemplate", "TryInitialize failed", ex);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static TimeSpan GetInitialStatusProbeTimeout()
    {
        var isAcceptance = string.Equals(
            Environment.GetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_MODE"),
            "1",
            StringComparison.Ordinal);

        return isAcceptance ? TimeSpan.FromSeconds(1.5) : TimeSpan.FromSeconds(15);
    }

    private void DebouncedBindGroupedToggles()
    {
        _debounceBindCts?.Cancel();
        _debounceBindCts = new CancellationTokenSource();
        var token = _debounceBindCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        BindGroupedToggles();
                });
            }
            catch (OperationCanceledException) { /* 被新请求取消，正常行为 */ }
        });
    }

    private void RefreshModuleStatusInBackground(int delayMs = 0)
    {
        var moduleKey = ModuleKey;
        var viewModel = _viewModel;
        if (viewModel == null || string.IsNullOrEmpty(moduleKey)) return;

        // Why: 必须用 InvokeAsync 替代 Invoke，防止后台线程持有 _syncStatusLock 时
        //      通过 PropertyChanged 触发 Dispatcher.Invoke 造成经典死锁
        _ = Dispatcher.InvokeAsync(() => _syncStatusHelper?.BeginSync());

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0)
                    await Task.Delay(delayMs).ConfigureAwait(false);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await viewModel.RefreshModuleStatusAsync(moduleKey)
                    .WaitAsync(cts.Token)
                    .ConfigureAwait(false);

                await Dispatcher.InvokeAsync(() =>
                {
                    _syncStatusHelper?.OnSyncCompleted();
                    // Why: 后台刷新通过 SyncStatus→ForceSetState→UpdatePropertiesFromState 更新状态，
                    //      但冷却期/操作中保护可能导致部分 toggle 跳过同步，
                    //      此处兜底强制 IsEnabled = IsApplied，确保无"即将取消/即将应用"残留
                    SyncToggleStates();
                    DebouncedBindGroupedToggles();
                    UpdateAllCounts();
                });
            }
            catch (OperationCanceledException)
            {
                PageDiagnosticService.Log("MasterModuleTemplate",
                    $"[{moduleKey}] RefreshModuleStatusInBackground 超时(15s)，跳过");
                await Dispatcher.InvokeAsync(() => _syncStatusHelper?.OnSyncFailed());
            }
            catch (Exception ex)
            {
                PageDiagnosticService.LogError("MasterModuleTemplate",
                    $"[{moduleKey}] RefreshModuleStatusInBackground failed", ex);
                await Dispatcher.InvokeAsync(() => _syncStatusHelper?.OnSyncFailed());
            }
        });
    }

    private void BindGroupedToggles()
    {
        // OPT-BUG1: 取消上一次未完成的批量渲染
        _batchRenderCts?.Cancel();
        _batchRenderCts = new CancellationTokenSource();
        var cts = _batchRenderCts;

        IEnumerable<OptimizationToggleModel> filtered = _allToggles;
        if (_currentFilter.HasValue)
            filtered = filtered.Where(t => t.RiskLevel == _currentFilter.Value);
        if (!string.IsNullOrWhiteSpace(_currentCategoryFilter))
            filtered = filtered.Where(t => string.Equals(t.Category, _currentCategoryFilter, StringComparison.Ordinal));
        var toggles = filtered.ToList();

        // Why: 筛选后无匹配项 → 显示空状态占位，隐藏列表
        if (toggles.Count == 0)
        {
            UpdateEmptyState(visible: true);
            return;
        }
        UpdateEmptyState(visible: false);

        var loc = LocalizationService.Instance;
        var grouped = toggles
            .GroupBy(t => t.Category ?? loc.Get("PerfBoost.Category.Other"))
            .OrderBy(g => g.Key);

        // 同步构建所有元素到列表，但不添加到可视树
        var elements = new List<UIElement>();
        foreach (var group in grouped)
        {
            if (UsesWebParityVisualSkeleton)
            {
                elements.Add(CreateWebParityGroupCard(group.Key, group.ToList()));
            }
            else
            {
                elements.Add(CreateCategoryHeader(group.Key));
                foreach (var toggle in group)
                    elements.Add(CreateToggleRow(toggle));
            }
        }

        // 清空并分批添加到可视树，每批15个，释放UI线程处理输入
        var panel = new StackPanel();
        GroupedItemsControl.Items.Clear();
        GroupedItemsControl.Items.Add(panel);
        _ = BatchAddChildrenAsync(panel, elements, 50, cts.Token);
    }

    /// <summary>
    /// 控制空状态面板的显隐和文案，根据当前筛选类型动态切换
    /// </summary>
    private void UpdateEmptyState(bool visible)
    {
        if (FindName("EmptyStatePanel") is not Border emptyPanel) return;
        if (FindName("ToggleListScrollViewer") is ScrollViewer scrollViewer)
            scrollViewer.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;

        emptyPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        // Why: 清空列表防止空状态和旧列表同时可见
        GroupedItemsControl.Items.Clear();

        var loc = LocalizationService.Instance;
        var titleKey = _currentFilter switch
        {
            OperationRiskLevel.Extreme => "Module.EmptyFilter.Extreme",
            OperationRiskLevel.Balanced => "Module.EmptyFilter.Balanced",
            OperationRiskLevel.Safe => "Module.EmptyFilter.Safe",
            _ => "Module.EmptyFilter.Title"
        };

        if (FindName("EmptyStateTitleText") is TextBlock title)
            title.Text = loc.Get(titleKey);
        if (FindName("EmptyStateHintText") is TextBlock hint)
            hint.Text = _currentFilter.HasValue ? loc.Get("Module.EmptyFilter.Hint") : "";
    }

    /// <summary>
    /// 分批将预构建的UI元素添加到面板，每批之间让出UI线程防卡顿。
    /// </summary>
    private async Task BatchAddChildrenAsync(
        StackPanel panel, List<UIElement> elements, int batchSize, CancellationToken ct)
    {
        for (int i = 0; i < elements.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) return;
            var end = Math.Min(i + batchSize, elements.Count);
            for (int j = i; j < end; j++)
                panel.Children.Add(elements[j]);

            // 让出UI线程一帧，使输入/动画得以响应
            if (end < elements.Count)
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        await Dispatcher.InvokeAsync(ApplyAcceptanceScrollPosition, DispatcherPriority.Loaded);
    }

    private void ApplyAcceptanceScrollPosition()
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Any(argument => argument.Equals("--acceptance-ui", StringComparison.OrdinalIgnoreCase)))
            return;

        var requested = args.FirstOrDefault(argument =>
            argument.StartsWith("--acceptance-scroll=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        ToggleListScrollViewer.UpdateLayout();
        if (string.Equals(requested, "middle", StringComparison.OrdinalIgnoreCase))
            ToggleListScrollViewer.ScrollToVerticalOffset(ToggleListScrollViewer.ScrollableHeight / 2.0);
        else if (string.Equals(requested, "end", StringComparison.OrdinalIgnoreCase))
            ToggleListScrollViewer.ScrollToEnd();
    }

    private Border CreateCategoryHeader(string category)
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        var translatedCategory = OptimizationTranslations.Instance.GetCategory(category, isEnglish);
        var categoryText = new TextBlock { Text = translatedCategory.ToUpperInvariant(), FontWeight = FontWeights.SemiBold };
        ApplyTypographyTokens(categoryText, "Token.FontSize.ModuleCategory");
        categoryText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextMuted");
        
        return new Border
        {
            Margin = new Thickness(0, 20, 0, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Border { Width = 3, Height = 12, CornerRadius = new CornerRadius(1.5),
                        Background = ThemeColors.InfoBrush, Margin = new Thickness(0, 0, 10, 0) },
                    categoryText
                }
            }
        };
    }

    private Border CreateToggleRow(OptimizationToggleModel toggle)
    {
        var isExtreme = toggle.RiskLevel == OperationRiskLevel.Extreme;
        
        // 使用动态资源键名
        var bgKey = isExtreme ? "Theme.Brush.RowBackgroundExtreme" : "Theme.Brush.RowBackground";
        var borderKey = isExtreme ? "Theme.Brush.RowBorderExtreme" : "Theme.Brush.RowBorder";
        var hoverBgKey = isExtreme ? "Theme.Brush.RowBackgroundHoverExtreme" : "Theme.Brush.RowBackgroundHover";
        var hoverBorderKey = isExtreme ? "Theme.Brush.RowBorderHoverExtreme" : "Theme.Brush.RowBorderHover";
        var iconBgKey = isExtreme ? "Theme.Brush.IconBackgroundExtreme" : "Theme.Brush.IconBackground";
        var iconBorderKey = isExtreme ? "Theme.Brush.IconBorderExtreme" : "Theme.Brush.IconBorder";
        
        var row = new Border
        {
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            MinHeight = 64,
            Height = double.NaN
        };
        row.SetResourceReference(Border.BackgroundProperty, bgKey);
        row.SetResourceReference(Border.BorderBrushProperty, borderKey);
        
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); // 详情 + 重新检测
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        
        var iconBorder = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left
        };
        iconBorder.SetResourceReference(Border.BackgroundProperty, iconBgKey);
        iconBorder.SetResourceReference(Border.BorderBrushProperty, iconBorderKey);
        var iconText = new TextBlock { Text = GetSemanticIcon(toggle.Title), FontSize = 14, FontFamily = new FontFamily("Segoe MDL2 Assets"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        iconText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextPrimaryAlt");
        iconBorder.Child = iconText;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);
        
        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 16, 0) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        var displayTitle = !string.IsNullOrEmpty(toggle.OperationId) 
            ? OptimizationTranslations.Instance.GetTitle(toggle.OperationId, toggle.Title, isEnglish) 
            : toggle.Title;
        var titleText = new TextBlock { Text = displayTitle, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        ApplyTypographyTokens(titleText, "Token.FontSize.ModuleItemTitle");
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextPrimaryAlt");
        titleRow.Children.Add(titleText);
        
        // 显示"已应用"标签（只有用户手动应用过才显示）
        var loc = LocalizationService.Instance;
        if (toggle.ShowAppliedTag)
        {
            titleRow.Children.Add(new Border
            {
                Background = ThemeColors.SuccessLightAlpha33Brush,
                CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = CreateModuleText(loc.Get("Module.AppliedTag"), "Token.FontSize.ModuleStatus", ThemeColors.SuccessLightBrush, FontWeights.Medium)
            });
        }
        
        // 需要重启标记
        if (toggle.RequiresReboot)
        {
            titleRow.Children.Add(new Border
            {
                Background = ThemeColors.WarningLightAlpha33Brush,
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = CreateModuleText(loc.Get("Module.RequiresReboot"), "Token.FontSize.ModuleStatus", ThemeColors.WarningLightBrush)
            });
        }
        
        var (riskBg, riskFg) = GetRiskColors(toggle.RiskLevel);
        titleRow.Children.Add(new Border
        {
            Background = riskBg, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = CreateModuleText(GetRiskText(toggle.RiskLevel), "Token.FontSize.ModuleStatus", riskFg, familyToken: "Token.FontFamily.Mono")
        });

        var (capabilityBg, capabilityFg) = GetCapabilityColors(toggle.CapabilityState);
        var capabilityLabel = CreateModuleText(toggle.CapabilityStateLabel, "Token.FontSize.ModuleStatus", capabilityFg, FontWeights.Medium);
        capabilityLabel.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.CapabilityStateLabel))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        titleRow.Children.Add(new Border
        {
            Background = capabilityBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = capabilityLabel
        });
        
        textPanel.Children.Add(titleRow);
        
        // 描述文案 - 优先使用翻译，回退到人性化描述
        var description = !string.IsNullOrEmpty(toggle.OperationId) 
            ? OptimizationTranslations.Instance.GetDescription(toggle.OperationId, GetHumanizedDescription(toggle.Title, toggle.Description), isEnglish) 
            : GetHumanizedDescription(toggle.Title, toggle.Description);
        var descText = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        ApplyTypographyTokens(descText, "Token.FontSize.ModuleDescription");
        descText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
        textPanel.Children.Add(descText);

        var feedbackText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        ApplyTypographyTokens(feedbackText, "Token.FontSize.ModuleStatus");
        feedbackText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.Warning");
        feedbackText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.OperationFeedback))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        feedbackText.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.HasOperationFeedback))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay,
            Converter = new BooleanToVisibilityConverter()
        });
        textPanel.Children.Add(feedbackText);

        // Capability blocks and terminal execution receipts answer different questions.
        // Keep both visible so an unavailable readback cannot hide the Engine's actual result.
        var receiptText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        ApplyTypographyTokens(receiptText, "Token.FontSize.ModuleStatus");
        receiptText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
        receiptText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.LastOperationReceipt))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay,
            StringFormat = isEnglish ? "Execution receipt: {0}" : "执行回执：{0}"
        });
        receiptText.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.LastOperationReceipt))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay,
            Converter = (System.Windows.Data.IValueConverter)FindResource("StringToVisibility")
        });
        AutomationProperties.SetName(receiptText, isEnglish ? "Per-operation execution and readback receipt" : "逐项执行与回读回执");
        textPanel.Children.Add(receiptText);

        var capabilityReasonText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        ApplyTypographyTokens(capabilityReasonText, "Token.FontSize.ModuleStatus");
        capabilityReasonText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
        capabilityReasonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.CapabilityStatusReason))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        capabilityReasonText.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.HasCapabilityConstraint))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay,
            Converter = new BooleanToVisibilityConverter()
        });
        textPanel.Children.Add(capabilityReasonText);
        
        // Why: 硬件不兼容时在描述下方显示琥珀色警告，帮助用户理解为何此项不适用
        if (toggle.IsHardwareIncompatible)
        {
            var hintText = new TextBlock
            {
                Text = "⚠ " + toggle.HardwareIncompatibleHint,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = ThemeColors.WarningLightBrush
            };
            ApplyTypographyTokens(hintText, "Token.FontSize.ModuleStatus");
            textPanel.Children.Add(hintText);
        }
        
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);
        
        // 详情/重新检测：重新检测只调用既有 Engine 状态刷新，不触发任何系统写入。
        var loc2 = LocalizationService.Instance;
        var actionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var detailBtn = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = toggle.HasRegistryInfo ? loc2.Get("Module.ViewRegistryDetails") : loc2.Get("Module.ViewOperationDetails")
        };
        KeyboardNavigation.SetIsTabStop(detailBtn, true);
        AutomationProperties.SetName(detailBtn, toggle.Title);
        detailBtn.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.ButtonBackground");
        detailBtn.SetResourceReference(Border.BorderBrushProperty, "Token.Brush.BorderFocus");
        detailBtn.BorderThickness = new Thickness(0);
        detailBtn.GotKeyboardFocus += (s, e) => detailBtn.BorderThickness = new Thickness(2);
        detailBtn.LostKeyboardFocus += (s, e) => detailBtn.BorderThickness = new Thickness(0);
        detailBtn.MouseLeftButtonUp += (s, e) => ShowOperationInfo(toggle);
        detailBtn.KeyDown += (s, e) =>
        {
            if (!IsActivationKey(e.Key)) return;
            e.Handled = true;
            ShowOperationInfo(toggle);
        };
        var detailIcon = new TextBlock { Text = IconGlyphs.Task, FontSize = 12, FontFamily = new FontFamily("Segoe MDL2 Assets"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        detailIcon.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextPrimaryAlt");
        detailBtn.Child = detailIcon;
        detailBtn.MouseEnter += (s, e) => detailBtn.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.ButtonBackgroundHover");
        detailBtn.MouseLeave += (s, e) => detailBtn.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.ButtonBackground");
        actionButtons.Children.Add(detailBtn);

        var retryBtn = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = loc2.Get("Module.Capability.Retry")
        };
        retryBtn.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.ButtonBackground");
        retryBtn.Child = new TextBlock
        {
            Text = "\uE72C",
            FontSize = 12,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ((TextBlock)retryBtn.Child).SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextPrimaryAlt");
        retryBtn.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.CanRetryCapabilityProbe))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay,
            Converter = new BooleanToVisibilityConverter()
        });
        retryBtn.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            RetryCapabilityProbe(toggle);
        };
        retryBtn.KeyDown += (_, args) =>
        {
            if (!IsActivationKey(args.Key)) return;
            args.Handled = true;
            RetryCapabilityProbe(toggle);
        };
        actionButtons.Children.Add(retryBtn);
        Grid.SetColumn(actionButtons, 2);
        grid.Children.Add(actionButtons);
        
        // Toggle按钮 - 使用数据绑定确保UI自动同步
        var toggleBtn = new ToggleButton { 
            Style = (Style)Resources["IOSToggleStyle"], 
            VerticalAlignment = VerticalAlignment.Center, 
            HorizontalAlignment = HorizontalAlignment.Center
        };

        toggleBtn.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.CanToggle))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        toggleBtn.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.OperationFeedback))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        
        // Why: IsEnabled 是用户意图的唯一模型来源；控件只显示模型状态，不能用双向绑定绕过能力门禁。
        var binding = new System.Windows.Data.Binding("IsEnabled")
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        toggleBtn.SetBinding(ToggleButton.IsCheckedProperty, binding);
        toggleBtn.SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding(nameof(OptimizationToggleModel.Title))
        {
            Source = toggle,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        toggleBtn.Click += (s, e) => HandleToggleClick(toggleBtn, toggle);
        Grid.SetColumn(toggleBtn, 3);
        grid.Children.Add(toggleBtn);
        
        row.Child = grid;
        
        // Why: 硬件不兼容时整行半透明，视觉上暗示此项不可用
        if (toggle.IsHardwareIncompatible)
        {
            row.Opacity = 0.55;
        }
        
        // 悬停效果使用动态资源
        row.MouseEnter += (s, e) => { row.SetResourceReference(Border.BorderBrushProperty, hoverBorderKey); row.SetResourceReference(Border.BackgroundProperty, hoverBgKey); };
        row.MouseLeave += (s, e) => { row.SetResourceReference(Border.BorderBrushProperty, borderKey); row.SetResourceReference(Border.BackgroundProperty, bgKey); };
        
        return row;
    }

    private static (Brush background, Brush foreground) GetCapabilityColors(ModuleCapabilityState state) => state switch
    {
        ModuleCapabilityState.Ready => (ThemeColors.SuccessLightAlpha33Brush, ThemeColors.SuccessLightBrush),
        ModuleCapabilityState.Executing => (ThemeColors.InfoLightAlpha20Brush, ThemeColors.InfoBrush),
        ModuleCapabilityState.AdminRequired or ModuleCapabilityState.RecoveryUnavailable => (ThemeColors.WarningLightAlpha33Brush, ThemeColors.WarningLightBrush),
        ModuleCapabilityState.Unsupported or ModuleCapabilityState.NotApplicable or ModuleCapabilityState.ReadOnly => (ThemeColors.WhiteAlpha10Brush, ThemeColors.TextSecondaryBrush),
        _ => (ThemeColors.DangerAlpha20Brush, ThemeColors.DangerBrush)
    };

    private void RetryCapabilityProbe(OptimizationToggleModel toggle)
    {
        if (!toggle.CanRetryCapabilityProbe || _isOperating) return;
        RefreshModuleStatusInBackground();
    }

    private Border CreateWebParityGroupCard(string category, IReadOnlyList<OptimizationToggleModel> toggles)
    {
        var cardContent = new StackPanel();
        var header = CreateWebParityGroupHeader(category, toggles);
        header.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.SurfaceMuted");
        cardContent.Children.Add(header);

        for (var index = 0; index < toggles.Count; index++)
        {
            var row = CreateToggleRow(toggles[index]);
            row.Margin = new Thickness(0);
            row.CornerRadius = new CornerRadius(0);
            row.BorderThickness = index == toggles.Count - 1
                ? new Thickness(0)
                : new Thickness(0, 0, 0, 1);
            row.Padding = new Thickness(18, 11, 18, 11);
            cardContent.Children.Add(row);
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            ClipToBounds = true,
            Child = cardContent
        };
        card.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.CardBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "Theme.Brush.CardBorder");
        card.Effect = new DropShadowEffect
        {
            BlurRadius = 16,
            ShadowDepth = 3,
            Opacity = 0.12,
            Color = Colors.Black
        };
        return card;
    }

    private Border CreateWebParityGroupHeader(string category, IReadOnlyList<OptimizationToggleModel> toggles)
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        var translatedCategory = OptimizationTranslations.Instance.GetCategory(category, isEnglish);
        var applied = toggles.Count(toggle => toggle.IsApplied);
        var accent = GetModuleAccentColor();

        var title = new TextBlock { Text = translatedCategory, FontWeight = FontWeights.SemiBold };
        ApplyTypographyTokens(title, "Token.FontSize.ModuleItemTitle");
        title.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextPrimaryAlt");

        var subtitle = new TextBlock
        {
            Text = GetGroupSubtitle(category, isEnglish),
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ApplyTypographyTokens(subtitle, "Token.FontSize.ModuleDescription");
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextMuted");

        var meta = new TextBlock
        {
            Text = isEnglish ? $"{applied} / {toggles.Count} enabled" : $"{applied} / {toggles.Count} 启用",
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyTypographyTokens(meta, "Token.FontSize.ModuleStatus", "Token.FontFamily.Mono");
        meta.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextMuted");

        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var marker = new Border
        {
            Width = 2,
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 2, 12, 2),
            Background = new SolidColorBrush(accent)
        };
        Grid.SetColumn(marker, 0);
        heading.Children.Add(marker);

        var text = new StackPanel { Children = { title, subtitle } };
        Grid.SetColumn(text, 1);
        heading.Children.Add(text);
        Grid.SetColumn(meta, 2);
        heading.Children.Add(meta);

        return new Border
        {
            Padding = new Thickness(18, 12, 18, 12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = heading
        };
    }

    private string GetGroupSubtitle(string category, bool isEnglish)
    {
        if (isEnglish)
        {
            return ModuleKey switch
            {
                "memory" => "Memory recovery and system allocation policies",
                "audio" => "Reduce processing latency and keep output stable",
                "performance" when category.Contains("安全", StringComparison.Ordinal) => "Security trade-offs require explicit confirmation",
                "performance" => "Release more system resources for foreground games",
                "cpu" => "Scheduling and affinity policies for foreground workloads",
                "graphics" => "Rendering responsiveness and graphics driver policies",
                "network" => "Connection stability and latency-aware network policies",
                "privacy" => "Privacy controls with clear system-impact boundaries",
                "system" => "Core Windows responsiveness and resource policies",
                "explorer" => "Desktop shell responsiveness and interaction policies",
                "tasks" => "Scheduled-task policies with explicit recovery boundaries",
                "services" => "Service policies that preserve required dependencies",
                "peripheral" => "Input and peripheral responsiveness policies",
                _ => "Review the available policies before applying changes"
            };
        }

        return ModuleKey switch
        {
            "memory" => "主动回收与系统内存策略",
            "audio" => "降低处理延迟，保持输出稳定",
            "performance" when category.Contains("安全", StringComparison.Ordinal) => "安全能力与性能取舍，修改前需确认",
            "performance" => "为前台游戏争取更多系统资源",
            "cpu" => "面向前台负载的调度与亲和性策略",
            "graphics" => "渲染响应与图形驱动相关策略",
            "network" => "网络稳定性与延迟感知策略",
            "privacy" => "隐私控制与系统影响边界",
            "system" => "Windows 核心响应与资源策略",
            "explorer" => "桌面外壳响应与交互策略",
            "tasks" => "具备明确恢复边界的计划任务策略",
            "services" => "保留必要依赖的服务策略",
            "peripheral" => "输入与外设响应策略",
            _ => "应用前请先核对可用策略"
        };
    }

    private static TextBlock CreateModuleText(
        string text,
        string sizeToken,
        Brush foreground,
        FontWeight? fontWeight = null,
        string familyToken = "Token.FontFamily.Body")
    {
        var textBlock = new TextBlock { Text = text, Foreground = foreground };
        if (fontWeight.HasValue)
            textBlock.FontWeight = fontWeight.Value;
        ApplyTypographyTokens(textBlock, sizeToken, familyToken);
        return textBlock;
    }

    private static void ApplyTypographyTokens(
        TextBlock textBlock,
        string sizeToken,
        string familyToken = "Token.FontFamily.Body")
    {
        textBlock.SetResourceReference(TextBlock.FontSizeProperty, sizeToken);
        textBlock.SetResourceReference(TextBlock.FontFamilyProperty, familyToken);
    }

    private void HandleToggleClick(ToggleButton toggleButton, OptimizationToggleModel toggle)
    {
        // Click 的内部 IsChecked 切换时序由 WPF 控件实现决定；模型意图才是稳定事实。
        var requestedState = !toggle.IsEnabled;

        if (_isOperating || !DebounceService.CanExecute($"toggle:{toggle.OperationId}", TimeSpan.FromMilliseconds(200)))
        {
            RestoreToggleVisualState(toggleButton);
            return;
        }

        if (!toggle.OnUserToggled(requestedState))
        {
            // The model is authoritative; a denied click must never remain visually selected.
            RestoreToggleVisualState(toggleButton);
            UpdateAllCounts();
            return;
        }

        if (requestedState)
        {
            EnforceMutualExclusion(toggle.OperationId, true);
        }

        if (!string.IsNullOrEmpty(toggle.OperationId))
        {
            _stateManager.UpdateState(ModuleKey, toggle.OperationId, new OptimizationItemState
            {
                IsSelected = toggle.IsEnabled,
                IsApplied = toggle.IsApplied,
                LastModified = DateTime.UtcNow
            });
        }

        Dispatcher.BeginInvoke(UpdateAllCounts, DispatcherPriority.Render);
        PageDiagnosticService.Log("MasterModuleTemplate",
            $"[{ModuleKey}] Toggle intent {(requestedState ? "开启" : "关闭")}: {toggle.OperationId ?? toggle.Title}");
    }

    private static void RestoreToggleVisualState(ToggleButton toggleButton)
    {
        System.Windows.Data.BindingOperations
            .GetBindingExpression(toggleButton, ToggleButton.IsCheckedProperty)
            ?.UpdateTarget();
    }

    private static bool IsActivationKey(Key key) => key is Key.Enter or Key.Space;
    
    private string GetHumanizedDescription(string title, string? description)
    {
        var loc = LocalizationService.Instance;
        var lower = title.ToLowerInvariant();
        if (lower.Contains("禁用音频增强") || lower.Contains("disable audio enhancement")) 
            return loc.Get("Audio.Desc.DisableEnhancement");
        if (lower.Contains("独占模式") || lower.Contains("exclusive mode")) 
            return loc.Get("Audio.Desc.ExclusiveMode");
        if (lower.Contains("空间音效") || lower.Contains("spatial sound")) 
            return loc.Get("Audio.Desc.SpatialSound");
        if (lower.Contains("缓冲") || lower.Contains("buffer")) 
            return loc.Get("Audio.Desc.Buffer");
        if (lower.Contains("音频服务") || lower.Contains("audio service")) 
            return loc.Get("Audio.Desc.AudioService");
        if (lower.Contains("采样率") || lower.Contains("sample rate")) 
            return loc.Get("Audio.Desc.SampleRate");
        if (lower.Contains("通信") || lower.Contains("communication")) 
            return loc.Get("Audio.Desc.Communication");
        return description ?? "";
    }
    
    // PAGE-007: 使用Segoe MDL2 Assets字体图标替代emoji
    private string GetSemanticIcon(string title) => Constants.IconGlyphs.GetByKeyword(title);
    
    private static (Brush bg, Brush fg) GetRiskColors(OperationRiskLevel risk) => risk switch
    {
        OperationRiskLevel.Extreme => (ThemeColors.RiskExtremeBgBrush, ThemeColors.RiskExtremeFgBrush),
        OperationRiskLevel.Balanced => (ThemeColors.RiskBalancedBgBrush, ThemeColors.RiskBalancedFgBrush),
        _ => (ThemeColors.RiskSafeBgBrush, ThemeColors.RiskSafeFgBrush)
    };
    
    private string GetRiskText(OperationRiskLevel risk)
    {
        var loc = LocalizationService.Instance;
        return risk switch 
        { 
            OperationRiskLevel.Extreme => loc.Get("Module.RiskExtreme"), 
            OperationRiskLevel.Balanced => loc.Get("Module.RiskBalanced"), 
            _ => loc.Get("Module.RiskSafe") 
        };
    }
    
    private void ShowOperationInfo(OptimizationToggleModel toggle)
    {
        // 优先展示统一透明说明卡片，确保所有优化项都能看到完整参数与回滚信息。
        try
        {
            OperationTransparencyDialog.Show(Window.GetWindow(this), toggle, TitleText.Text);
            return;
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("MasterModuleTemplate", $"[{ModuleKey}] Failed to open OperationTransparencyDialog", ex);
        }

        // 优先显示注册表信息
        if (toggle.HasRegistryInfo)
        {
            ShowRegistryInfo(toggle);
            return;
        }
        
        // 显示非注册表操作信息
        if (toggle.HasManualInfo || toggle.OperationType != Core.Optimization.OperationType.Other)
        {
            OperationInfoDialog.Show(
                Window.GetWindow(this),
                toggle.Title,
                toggle.Description,
                toggle.OperationType,
                toggle.TargetName,
                toggle.TargetValue,
                toggle.ManualPath,
                toggle.ManualSteps);
            return;
        }
        
        // 没有详细信息时显示通用提示
        var loc = LocalizationService.Instance;
        InfoDialog.Show(
            Window.GetWindow(this),
            toggle.Title,
            $"{toggle.Description ?? loc.Get("Module.NoDetailInfo")}\n\n{loc.Get("Module.AutoOperation")}",
            IconGlyphs.Lightbulb);
    }
    
    private void ShowRegistryInfo(OptimizationToggleModel toggle)
    {
        if (string.IsNullOrEmpty(toggle.RegistryPath)) return;
        
        // 分离键路径和值名称
        var path = toggle.RegistryPath;
        string keyPath, valueName;
        
        if (!string.IsNullOrEmpty(toggle.RegistryValueName))
        {
            keyPath = path;
            valueName = toggle.RegistryValueName;
        }
        else if (path.Contains('\\'))
        {
            var lastSlash = path.LastIndexOf('\\');
            if (lastSlash > 0 && lastSlash < path.Length - 1)
            {
                keyPath = path.Substring(0, lastSlash);
                valueName = path.Substring(lastSlash + 1);
            }
            else
            {
                keyPath = path;
                valueName = path;
            }
        }
        else
        {
            keyPath = path;
            valueName = path;
        }
        
        var dialog = new RegistryInfoDialog(
            toggle.RegistryHiveText ?? "HKLM",
            keyPath,
            null, // currentValue 会自动读取
            toggle.OptimizedValue ?? toggle.ValuePreview,
            toggle.Title,
            toggle.Description,
            valueName,
            toggle.DefaultValue,
            toggle.ValueType,
            toggle.WhyExplanation);
        dialog.Owner = Window.GetWindow(this);
        dialog.ShowDialog();
    }

    private void UpdateAllCounts()
    {
        if (_module == null) return;

        RestoreBtn.IsEnabled = _viewModel != null && !_isOperating;
        RestoreBtn.IsHitTestVisible = RestoreBtn.IsEnabled;
        
        var loc = LocalizationService.Instance;
        var selected = _allToggles.Count(t => t.IsEnabled);
        var applied = _allToggles.Count(t => t.IsApplied);
        var total = _allToggles.Count;
        var extreme = _allToggles.Count(t => t.RiskLevel == OperationRiskLevel.Extreme);
        var balanced = _allToggles.Count(t => t.RiskLevel == OperationRiskLevel.Balanced);
        var safe = _allToggles.Count(t => t.RiskLevel == OperationRiskLevel.Safe);
        
        // OPT-011: 计算待处理项
        var pendingApply = _allToggles.Count(t => t.IsPending && t.IsEnabled && t.CanApply);
        var pendingRestore = _allToggles.Count(t => t.IsPending && !t.IsEnabled && t.CanRestore);
        var hasPending = pendingApply > 0 || pendingRestore > 0;
        
        SelectedCountText.Text = selected.ToString();
        AppliedCountText.Text = applied.ToString();
        AppliedCountRing.Text = applied.ToString();
        TotalCountRing.Text = total.ToString();
        ExtremeCountText.Text = loc.Get("Module.ExtremeCount", extreme);
        BalancedCountText.Text = loc.Get("Module.BalancedCount", balanced);
        SafeCountText.Text = loc.Get("Module.SafeCount", safe);
        if (UsesWebParityVisualSkeleton)
        {
            FilterAllCountText.Text = total.ToString();
            FilterExtremeCountText.Text = extreme.ToString();
            FilterBalancedCountText.Text = balanced.ToString();
            FilterSafeCountText.Text = safe.ToString();
            ModuleSpecChipText.Text = LocalizationService.Instance.CurrentLanguage == "en-US"
                ? $"{total} items · synchronized"
                : $"{total} 项 · 状态已同步";
        }
        
        // OPT-011: 更新待处理提示
        if (FindName("PendingActionHintText") is TextBlock hintText)
        {
            hintText.Text = GetPendingActionHint(pendingApply, pendingRestore);
            hintText.Visibility = hasPending ? Visibility.Visible : Visibility.Collapsed;
        }
        
        // OPT-011: 更新"立即应用更改"按钮状态
        var canExecutePending = hasPending && !_isOperating;
        ApplyBtnBorder.Opacity = canExecutePending ? 1.0 : 0.5;
        ApplyBtnBorder.IsHitTestVisible = canExecutePending;
        ApplyBtnBorder.IsEnabled = canExecutePending;
        
        UpdateProgressRing(applied, total);
    }
    
    /// <summary>
    /// OPT-011: 生成待处理操作提示文本
    /// </summary>
    private string GetPendingActionHint(int applyCount, int restoreCount)
    {
        var loc = LocalizationService.Instance;
        
        if (applyCount > 0 && restoreCount > 0)
            return loc.Get("Module.PendingBoth", applyCount, restoreCount);
        else if (applyCount > 0)
            return loc.Get("Module.PendingApply", applyCount);
        else if (restoreCount > 0)
            return loc.Get("Module.PendingRestore", restoreCount);
        else
            return loc.Get("Module.NoChanges");
    }

    private void PresentBatchReceipt(BatchResult result)
    {
        var loc = LocalizationService.Instance;
        var allSucceeded = result.SuccessCount > 0 && result.FailedCount == 0;
        var summary = result.SuccessCount > 0 && result.FailedCount > 0
            ? loc.Get("Module.PartialOperationsResult", result.SuccessCount, result.FailedCount)
            : result.SuccessCount > 0
                ? loc.Get("Module.AllOperationsSuccess")
                : loc.Get("Module.AllOperationsFailed");

        StatusText.Text = summary;
        StatusDot.Fill = allSucceeded
            ? ThemeColors.SuccessLightBrush
            : result.SuccessCount > 0
                ? ThemeColors.WarningBrush
                : ThemeColors.DangerBrush;

        PageDiagnosticService.Log(
            "MasterModuleTemplate",
            $"[{ModuleKey}] terminal batch receipt: success={result.SuccessCount}, failed={result.FailedCount}");
    }
    
    /// <summary>
    /// OPT-018: 删除配置持久化检查方法
    /// 不再需要检查用户配置，直接同步系统状态
    /// </summary>
    
    private void UpdateProgressRing(int applied, int total)
    {
        if (total == 0) return;
        var percent = (double)applied / total;
        var circumference = Math.PI * 68;
        var dashLength = circumference * percent;
        ProgressRing.StrokeDashArray = new DoubleCollection { dashLength / 6, 100 };
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        
        if (FilterAll.IsChecked == true) _currentFilter = null;
        else if (FilterExtreme.IsChecked == true) _currentFilter = OperationRiskLevel.Extreme;
        else if (FilterBalanced.IsChecked == true) _currentFilter = OperationRiskLevel.Balanced;
        else if (FilterSafe.IsChecked == true) _currentFilter = OperationRiskLevel.Safe;
        
        BindGroupedToggles();
    }

    private void PopulateCategoryFilter()
    {
        _isPopulatingCategoryFilter = true;
        try
        {
            var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
            CategoryFilterLabel.Text = isEnglish ? "Category" : "分类";
            CategoryFilterComboBox.Items.Clear();
            AddCategoryFilterOption(isEnglish ? "All" : "全部", category: null, _allToggles.Count);

            foreach (var group in _allToggles
                         .Select(toggle => toggle.Category)
                         .Where(category => !string.IsNullOrWhiteSpace(category))
                         .GroupBy(category => category!, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                AddCategoryFilterOption(
                    OptimizationTranslations.Instance.GetCategory(group.Key, isEnglish),
                    group.Key,
                    group.Count());
            }
        }
        finally
        {
            _isPopulatingCategoryFilter = false;
        }
    }

    private void AddCategoryFilterOption(string label, string? category, int count)
    {
        var option = new ComboBoxItem
        {
            Tag = category,
            Content = $"{label} · {count}"
        };
        CategoryFilterComboBox.Items.Add(option);
        if (string.Equals(category, _currentCategoryFilter, StringComparison.Ordinal)
            || (category is null && _currentCategoryFilter is null))
        {
            CategoryFilterComboBox.SelectedItem = option;
        }
    }

    private void CategoryFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingCategoryFilter || !_isInitialized || sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem option)
            return;

        _currentCategoryFilter = option.Tag as string;
        BindGroupedToggles();
    }

    private void CategoryFilterComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isInitialized || sender is not ComboBox comboBox || !comboBox.IsEnabled)
            return;

        // The category field must open from any point in its hit area, not only its arrow.
        comboBox.Focus();
        comboBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void RestoreBtn_Click(object sender, MouseButtonEventArgs e) => OpenRestoreMenu();

    private void RestoreBtn_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActivationKey(e.Key)) return;
        e.Handled = true;
        OpenRestoreMenu();
    }

    private void OpenRestoreMenu()
    {
        if (_isOperating || !RestoreBtn.IsEnabled) return;

        RestorePopup.IsOpen = true;
        Dispatcher.BeginInvoke(() => RestoreUserMenuItem.Focus(), DispatcherPriority.Input);
    }
    /// <summary>
    /// Task 13.3: 恢复按钮鼠标进入 - 使用动画平滑过渡
    /// </summary>
    private void Restore_MouseEnter(object sender, MouseEventArgs e)
    {
        var colorAnim = new System.Windows.Media.Animation.ColorAnimation
        {
            To = _btnBg,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        
        var brush = new SolidColorBrush(Colors.Transparent);
        RestoreBtn.Background = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
    }
    
    /// <summary>
    /// Task 13.3: 恢复按钮鼠标离开 - 使用动画平滑过渡
    /// </summary>
    private void Restore_MouseLeave(object sender, MouseEventArgs e)
    {
        var colorAnim = new System.Windows.Media.Animation.ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        
        var brush = RestoreBtn.Background as SolidColorBrush;
        if (brush != null)
        {
            var animBrush = brush.Clone();
            RestoreBtn.Background = animBrush;
            animBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
        }
    }
    private void MenuItem_MouseEnter(object sender, MouseEventArgs e) { if (sender is Border b) b.Background = new SolidColorBrush(_btnBg); }
    private void MenuItem_MouseLeave(object sender, MouseEventArgs e) { if (sender is Border b) b.Background = Brushes.Transparent; }

    



    private async void Open_BackupManager_Click(object sender, MouseButtonEventArgs e) => await OpenBackupManagerAsync();

    private async void Open_BackupManager_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActivationKey(e.Key)) return;
        e.Handled = true;
        await OpenBackupManagerAsync();
    }

    private Task OpenBackupManagerAsync()
    {
        RestorePopup.IsOpen = false;
        if (_isOperating || _operationGate.IsBusy)
        {
            return Task.CompletedTask;
        }

        if (Application.Current?.MainWindow is GameOptimizer.Shell.MainWindow mainWindow)
        {
            mainWindow.NavigateToPage("backup-manager");
        }
        return Task.CompletedTask;
    }
    
    private async void Undo_ModuleChanges_Click(object sender, MouseButtonEventArgs e) => await ExecuteUndoModuleChangesAsync();

    private async void Undo_ModuleChanges_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActivationKey(e.Key)) return;
        e.Handled = true;
        await ExecuteUndoModuleChangesAsync();
    }

    private async Task ExecuteUndoModuleChangesAsync()
    {
        // 统一通过 ThreadSafeBatchOperationService 执行批量操作。
        RestorePopup.IsOpen = false;
        if (_viewModel == null || _module == null) return;
        
        var dl = "UndoModuleChanges";
        Services.DebugLogger.Log(dl, $"[{ModuleKey}] 入口");
        var operationStarted = false;
        
        try
        {
            var loc = LocalizationService.Instance;
            
            var applied = _allToggles.Where(t => t.IsApplied && t.CanRestore && !string.IsNullOrEmpty(t.OperationId)).ToList();
            if (applied.Count == 0) 
            { 
                var blocked = _allToggles.Count(t => t.IsApplied && !t.CanRestore);
                var message = blocked > 0
                    ? $"检测到 {blocked} 项已生效设置，但没有可验证的 Engine 恢复回执，已安全阻断恢复。"
                    : loc.Get("Module.NoApplied");
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Dialog.Info"), message, IconGlyphs.Lightbulb); 
                return; 
            }
            
            Services.DebugLogger.Log(dl, $"[{ModuleKey}] 待恢复 {applied.Count} 项, 弹出确认对话框...");
            var itemNames = string.Join("\n• ", applied.Select(t => t.Title));
            var confirm = ConfirmDialog.Show(Window.GetWindow(this), loc.Get("Module.RestoreWindowsTitle"), 
                $"{loc.Get("Module.RestoreWindowsConfirm", applied.Count)}\n\n• {itemNames}", IconGlyphs.Warning);
            Services.DebugLogger.Log(dl, $"[{ModuleKey}] 确认对话框返回: confirm={confirm}");
            if (!confirm) return;
            
            if (!TryBeginOperation(loc.Get("Module.Restoring"), out var operationToken)) return;
            operationStarted = true;
            
            var ownerWindow = Window.GetWindow(this);
            var moduleKey = ModuleKey;
            
            try
            {
                // FIX-PROGRESS-DIALOG: 删除ProgressDialog，避免模态对话框阻塞UI线程
                // FIX-STATE-MACHINE: 驱动状态机进入Restoring状态，否则OnRestoreSucceeded无效
                foreach (var t in applied) t.OnStartRestore();
                
                var result = await _threadSafeBatchService.ExecuteRestoreAsync(
                    _viewModel, moduleKey, applied, null, operationToken);
                
                Services.DebugLogger.Log(dl, $"[{moduleKey}] 恢复操作完成: success={result.SuccessCount}, failed={result.FailedCount}");
                
                // Task 9.2: 批量更新StateManager
                var stateUpdates = new Dictionary<string, OptimizationItemState>();
                var restoreSummaryMap = result.OperationSummaries
                    .GroupBy(summary => summary.OperationId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
                
                foreach (var toggle in applied)
                {
                    if (!string.IsNullOrEmpty(toggle.OperationId) && restoreSummaryMap.TryGetValue(toggle.OperationId, out var summary))
                    {
                        var presentation = CommandResultPresentationMapper.FromOptimization(summary);
                        if (presentation.Status == CommandResultStatus.RequiresRecovery)
                        {
                            toggle.CompleteRestoreFailure(presentation.DisplayText);
                        }
                        else
                        {
                            toggle.CompleteRestoreFromSummary(summary);
                        }
                    }
                    else
                    {
                        toggle.CompleteRestoreFailure("Engine 未返回该 operation 的逐项恢复回执。");
                    }
                    
                    if (!string.IsNullOrEmpty(toggle.OperationId))
                    {
                        stateUpdates[toggle.OperationId] = new OptimizationItemState
                        {
                            IsSelected = toggle.IsEnabled,
                            IsApplied = toggle.IsApplied,
                            LastModified = DateTime.UtcNow
                        };
                    }
                }
                
                if (stateUpdates.Count > 0)
                {
                    _stateManager.UpdateStates(moduleKey, stateUpdates);
                }
                
                // FIX-UI-SYNC-V2: 删除_allToggles重新赋值，因为Toggle对象引用相同，状态已更新
                // Why: _allToggles中的对象和_module.Toggles中的是同一个引用，
                //      OnRestoreSucceeded/OnRestoreFailed已经更新了对象状态，
                //      重新ToList()可能在状态传播期间创建快照导致读取旧值
                await Dispatcher.InvokeAsync(() =>
                {
                    BindGroupedToggles();
                    UpdateAllCounts();
                }, DispatcherPriority.Send);
                
                // FIX-UI-SYNC: 强制等待UI渲染完成,确保用户看到更新后的界面
                // Why: CompletionSummaryDialog是模态对话框,会阻塞UI线程,必须先让UI更新完成
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                
                CompletionSummaryDialog.Show(ownerWindow, result);
                ShowRebootNotice(ownerWindow, result.OperationSummaries);
            }
            catch (OperationCanceledException ex)
            {
                foreach (var toggle in applied.Where(t => t.CurrentState == OptimizationState.Restoring))
                {
                    toggle.CompleteRestoreFailure("操作已取消，UI 意图已回滚到最后一次回读状态。");
                }
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Dialog.Info"), 
                    CommandResultPresentationMapper.FromException(ex).DisplayText, IconGlyphs.Warning);
            }
            catch (TimeoutException)
            {
                StatusText.Text = loc.Get("Module.PartialStateUnavailable", _allToggles.Count);
                StatusDot.Fill = ThemeColors.WarningBrush;
                PageDiagnosticService.Log(
                    "MasterModuleTemplate",
                    $"[{ModuleKey}] 状态读取超时；保留未验证状态并继续呈现页面。");
            }
            catch (Exception ex)
            {
                foreach (var toggle in applied.Where(t => t.CurrentState == OptimizationState.Restoring))
                {
                    toggle.CompleteRestoreFailure(ex.Message);
                }
                PageDiagnosticService.LogError("MasterModuleTemplate", $"[{ModuleKey}] Restore operation failed", ex);
                
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), 
                    CommandResultPresentationMapper.FromException(ex).DisplayText, IconGlyphs.Warning);
            }
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("MasterModuleTemplate", $"[{ModuleKey}] Undo_ModuleChanges_Click failed", ex);
            InfoDialog.Show(Window.GetWindow(this), LocalizationService.Instance.Get("Common.Error"),
                CommandResultPresentationMapper.FromException(ex).DisplayText, IconGlyphs.Warning);
        }
        finally
        {
            if (operationStarted)
            {
                EndOperation();
            }
            Services.DebugLogger.Flush();
        }
    }
    
    /// <summary>
    /// Task 13.3: 应用按钮鼠标进入 - 背景色变深，阴影增强
    /// </summary>
    private void ApplyBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!ApplyBtnBorder.IsHitTestVisible) return; // 禁用时不响应
        
        // 背景色动画：#3B82F6 → #2563EB
        var colorAnim = new System.Windows.Media.Animation.ColorAnimation
        {
            To = Color.FromRgb(0x25, 0x63, 0xEB),
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        
        var brush = ApplyBtnBorder.Background as SolidColorBrush;
        if (brush != null)
        {
            // 创建可动画的画刷副本
            var animBrush = brush.Clone();
            ApplyBtnBorder.Background = animBrush;
            animBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
        }
        
        // 阴影增强
        if (ApplyBtnBorder.Effect is DropShadowEffect shadow)
        {
            var shadowAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 0.6,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, shadowAnim);
        }
    }
    
    /// <summary>
    /// Task 13.3: 应用按钮鼠标离开 - 恢复原色和阴影
    /// </summary>
    private void ApplyBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        // 恢复原色：当前色 → #3B82F6
        var colorAnim = new System.Windows.Media.Animation.ColorAnimation
        {
            To = Color.FromRgb(0x3B, 0x82, 0xF6),
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        
        var brush = ApplyBtnBorder.Background as SolidColorBrush;
        if (brush != null)
        {
            var animBrush = brush.Clone();
            ApplyBtnBorder.Background = animBrush;
            animBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
        }
        
        // 阴影恢复
        if (ApplyBtnBorder.Effect is DropShadowEffect shadow)
        {
            var shadowAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 0.4,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, shadowAnim);
        }
        
        // 恢复缩放（如果有按下效果残留）
        if (ApplyBtnBorder.RenderTransform is ScaleTransform scale)
        {
            var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }
    }
    
    /// <summary>
    /// Task 13.3: 应用按钮鼠标按下 - 缩放到0.96，阴影减弱
    /// </summary>
    private void ApplyBtn_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ApplyBtnBorder.IsHitTestVisible) return;
        
        // 缩放到0.96
        if (ApplyBtnBorder.RenderTransform is ScaleTransform scale)
        {
            var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 0.96,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }
        
        // 阴影减弱
        if (ApplyBtnBorder.Effect is DropShadowEffect shadow)
        {
            var shadowAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 0.2,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, shadowAnim);
        }
    }
    
    /// <summary>
    /// Task 13.3: 应用按钮鼠标释放 - 恢复缩放和阴影
    /// </summary>
    private void ApplyBtn_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        // 恢复缩放
        if (ApplyBtnBorder.RenderTransform is ScaleTransform scale)
        {
            var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }
        
        // 阴影恢复到悬停状态
        if (ApplyBtnBorder.Effect is DropShadowEffect shadow)
        {
            var shadowAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 0.6,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, shadowAnim);
        }
    }
    
    private async void ApplyBtn_Click(object sender, RoutedEventArgs e) => await ExecutePendingChangesAsync();

    private async void ApplyBtn_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActivationKey(e.Key)) return;
        e.Handled = true;
        await ExecutePendingChangesAsync();
    }

    private async Task ExecutePendingChangesAsync()
    {
        // 统一通过 ThreadSafeBatchOperationService 执行批量操作。
        var moduleKey = ModuleKey;
        PageDiagnosticService.Log("MasterModuleTemplate", $"[{moduleKey}] ApplyBtn_Click CALLED");
        
        if (_viewModel == null || _module == null)
        {
            PageDiagnosticService.Log("MasterModuleTemplate", $"[{moduleKey}] Early return: _isOperating={_isOperating}");
            return;
        }

        var dl = "ApplyBtn";
        Services.DebugLogger.Log(dl, $"[{moduleKey}] ApplyBtn_Click 入口");
        var loc = LocalizationService.Instance;

        try
        {
            var settings = AppSettings.Load();

            var toApply = _allToggles.Where(t => t.IsPending && t.IsEnabled && t.CanApply && !string.IsNullOrEmpty(t.OperationId)).ToList();
            var toRestore = _allToggles.Where(t => t.IsPending && !t.IsEnabled && t.CanRestore && !string.IsNullOrEmpty(t.OperationId)).ToList();

            if (toApply.Count == 0 && toRestore.Count == 0)
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Dialog.Info"), loc.Get("Module.NoChanges"), IconGlyphs.Lightbulb);
                return;
            }

            var extremeItems = toApply.Where(t => t.RiskLevel == OperationRiskLevel.Extreme).ToList();

            var confirmationMessage = BuildBatchConfirmationMessage(toApply, toRestore, extremeItems, settings.ShowRiskWarnings);
            var confirm = ConfirmDialog.Show(Window.GetWindow(this), loc.Get("Module.ConfirmChangesTitle"), 
                confirmationMessage,
                (extremeItems.Count > 0 && settings.ShowRiskWarnings) ? IconGlyphs.Warning : IconGlyphs.Question);
            if (!confirm)
            {
                PageDiagnosticService.Log("MasterModuleTemplate", $"[{moduleKey}] 用户取消确认，未进入执行链。");
                return;
            }

            // Confirmation is the authorization boundary. Do not set an applying state,
            // acquire an execution gate, or allocate an execution token before it succeeds.
            if (!TryBeginOperation(loc.Get("Module.Applying"), out var operationToken)) return;

            var ownerWindow = Window.GetWindow(this);
            var totalSuccessCount = 0;
            var totalFailedCount = 0;
            var allErrors = new List<OperationError>();
            var allSummaries = new List<OperationExecutionSummary>();
            
            try
            {
                // The user has just confirmed the complete, scrollable item list.
                // Keep that authorization scoped to this one batch; the core still
                // issues plan-bound confirmation tokens per real change.
                using var changeConfirmationScope = BatchChangeConfirmationScope.Begin();

                // FIX-PROGRESS-DIALOG: 删除ProgressDialog，避免模态对话框阻塞UI线程
                // 原因：ProgressDialog.Show()是模态对话框，会阻塞UI直到关闭
                // 解决方案：直接执行操作，只在最后显示CompletionSummaryDialog
                
                if (toRestore.Count > 0)
                {
                    // FIX-STATE-MACHINE: 驱动状态机进入Restoring状态，否则OnRestoreSucceeded无效
                    foreach (var t in toRestore) t.OnStartRestore();
                    
                    Services.DebugLogger.Log(dl, $"[{moduleKey}] 执行恢复操作，共 {toRestore.Count} 项");
                    var restoreResult = await _threadSafeBatchService.ExecuteRestoreAsync(
                        _viewModel, moduleKey, toRestore, null, operationToken);
                    totalSuccessCount += restoreResult.SuccessCount;
                    totalFailedCount += restoreResult.FailedCount;
                    allErrors.AddRange(restoreResult.Errors); // Task 9.4: 收集错误
                    allSummaries.AddRange(restoreResult.OperationSummaries);
                    var stateUpdates = new Dictionary<string, OptimizationItemState>();
                    
                    var restoreSummaryMap = restoreResult.OperationSummaries
                        .GroupBy(summary => summary.OperationId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

                    foreach (var toggle in toRestore)
                    {
                        if (!string.IsNullOrEmpty(toggle.OperationId) && restoreSummaryMap.TryGetValue(toggle.OperationId, out var summary))
                        {
                            var presentation = CommandResultPresentationMapper.FromOptimization(summary);
                            if (presentation.Status == CommandResultStatus.RequiresRecovery)
                            {
                                toggle.CompleteRestoreFailure(presentation.DisplayText);
                            }
                            else
                            {
                                toggle.CompleteRestoreFromSummary(summary);
                            }
                            Services.DebugLogger.Log(dl, $"[{moduleKey}] {(toggle.IsApplied ? "恢复未达目标" : "恢复成功")}: {toggle.Title}");
                        }
                        else
                        {
                            toggle.CompleteRestoreFailure("Engine 未返回该 operation 的逐项恢复回执。");
                            Services.DebugLogger.Log(dl, $"[{moduleKey}] 恢复失败: {toggle.Title}");
                        }
                        
                        if (!string.IsNullOrEmpty(toggle.OperationId))
                        {
                            stateUpdates[toggle.OperationId] = new OptimizationItemState
                            {
                                IsSelected = toggle.IsEnabled,
                                IsApplied = toggle.IsApplied,
                                LastModified = DateTime.UtcNow
                            };
                        }
                    }
                    
                    if (stateUpdates.Count > 0)
                    {
                        _stateManager.UpdateStates(moduleKey, stateUpdates);
                    }
                }

                if (toApply.Count > 0)
                {
                    // FIX-STATE-MACHINE: 驱动状态机进入Applying状态，否则OnApplySucceeded无效
                    foreach (var t in toApply) t.OnStartApply();
                    
                    Services.DebugLogger.Log(dl, $"[{moduleKey}] 执行应用操作，共 {toApply.Count} 项");
                    var applyResult = await _threadSafeBatchService.ExecuteApplyAsync(
                        _viewModel, moduleKey, toApply, null, operationToken);
                    totalSuccessCount += applyResult.SuccessCount;
                    totalFailedCount += applyResult.FailedCount;
                    allErrors.AddRange(applyResult.Errors); // Task 9.4: 收集错误
                    allSummaries.AddRange(applyResult.OperationSummaries);
                    var stateUpdates = new Dictionary<string, OptimizationItemState>();
                    var applySummaryMap = applyResult.OperationSummaries
                        .GroupBy(summary => summary.OperationId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

                    foreach (var toggle in toApply)
                    {
                        if (!string.IsNullOrEmpty(toggle.OperationId) && applySummaryMap.TryGetValue(toggle.OperationId, out var summary))
                        {
                            var presentation = CommandResultPresentationMapper.FromOptimization(summary);
                            if (presentation.Status == CommandResultStatus.RequiresRecovery)
                            {
                                toggle.CompleteApplyFailure(presentation.DisplayText);
                            }
                            else
                            {
                                toggle.CompleteApplyFromSummary(summary);
                            }
                            Services.DebugLogger.Log(dl, $"[{moduleKey}] {(toggle.IsApplied ? "应用成功" : "应用未达目标")}: {toggle.Title}");
                        }
                        else
                        {
                            toggle.CompleteApplyFailure("Engine 未返回该 operation 的逐项应用回执。");
                            Services.DebugLogger.Log(dl, $"[{moduleKey}] 应用失败: {toggle.Title}");
                        }
                        
                        if (!string.IsNullOrEmpty(toggle.OperationId))
                        {
                            stateUpdates[toggle.OperationId] = new OptimizationItemState
                            {
                                IsSelected = toggle.IsEnabled,
                                IsApplied = toggle.IsApplied,
                                LastModified = DateTime.UtcNow
                            };
                        }
                    }
                    
                    if (stateUpdates.Count > 0)
                    {
                        _stateManager.UpdateStates(moduleKey, stateUpdates);
                    }
                }
                
                // FIX-UI-FREEZE: 删除Task.Delay，依赖Dispatcher优先级机制确保UI更新顺序
                // FIX-UI-SYNC-V2: 删除_allToggles重新赋值，因为Toggle对象引用相同，状态已更新
                // Why: _allToggles中的对象和_module.Toggles中的是同一个引用，
                //      OnApplySucceeded/OnRestoreSucceeded已经更新了对象状态，
                //      重新ToList()可能在状态传播期间创建快照导致读取旧值
                await Dispatcher.InvokeAsync(() =>
                {
                    BindGroupedToggles();
                    UpdateAllCounts();
                }, DispatcherPriority.Send);
                
                // FIX-UI-SYNC: 强制等待UI渲染完成,确保用户看到更新后的界面
                // Why: CompletionSummaryDialog是模态对话框,会阻塞UI线程,必须先让UI更新完成
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                
                // Task 9.4: 显示CompletionSummaryDialog
                var combinedResult = new BatchResult
                {
                    SuccessCount = totalSuccessCount,
                    FailedCount = totalFailedCount,
                    Errors = allErrors,
                    OperationSummaries = allSummaries
                };
                PresentBatchReceipt(combinedResult);
                CompletionSummaryDialog.Show(ownerWindow, combinedResult);
                ShowRebootNotice(ownerWindow, allSummaries);
                
                Services.DebugLogger.Log(dl, $"[{moduleKey}] 混合操作完成: success={totalSuccessCount}, failed={totalFailedCount}");
            }
            catch (OperationCanceledException ex)
            {
                foreach (var toggle in toRestore.Where(t => t.CurrentState == OptimizationState.Restoring))
                {
                    toggle.CompleteRestoreFailure("操作已取消，UI 意图已回滚到最后一次回读状态。");
                }
                foreach (var toggle in toApply.Where(t => t.CurrentState == OptimizationState.Applying))
                {
                    toggle.CompleteApplyFailure("操作已取消，UI 意图已回滚到最后一次回读状态。");
                }
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Dialog.Info"), 
                    CommandResultPresentationMapper.FromException(ex).DisplayText, IconGlyphs.Warning);
            }
            catch (Exception ex)
            {
                foreach (var toggle in toRestore.Where(t => t.CurrentState == OptimizationState.Restoring))
                {
                    toggle.CompleteRestoreFailure(ex.Message);
                }
                foreach (var toggle in toApply.Where(t => t.CurrentState == OptimizationState.Applying))
                {
                    toggle.CompleteApplyFailure(ex.Message);
                }
                Services.DebugLogger.Log(dl, $"[{ModuleKey}] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                PageDiagnosticService.LogError("MasterModuleTemplate", $"[{ModuleKey}] Apply operation failed", ex);

                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), 
                    CommandResultPresentationMapper.FromException(ex).DisplayText, IconGlyphs.Warning);
            }
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("MasterModuleTemplate", $"[{ModuleKey}] ApplyBtn_Click failed", ex);
            InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"),
                CommandResultPresentationMapper.FromException(ex).DisplayText, IconGlyphs.Warning);
        }
        finally
        {
            EndOperation();
            Services.DebugLogger.Flush();
        }
    }

    
    
    /// <summary>
    /// OPT-010: 同步开关状态到系统实际状态
    /// ?? 警告：此方法会强制覆盖用户的 IsEnabled 意图，仅在以下场景使用：
    /// 1. 初始化时（TryInitialize）- 从系统读取初始状态
    /// 2. 撤销已有改动完成后（Undo_ModuleChanges_Click）- 从系统读取已确认的状态
    /// ? 禁止在用户操作后调用（Apply/Restore），会破坏 UserOperation 策略
    /// </summary>
    private void SyncToggleStates()
    {
        // 同步开关状态：已生效的开，未生效的关
        PageDiagnosticService.Log("MasterModuleTemplate", $"[{ModuleKey}] OPT-010: 强制同步所有Toggle状态到系统实际状态");
        foreach (var toggle in _allToggles)
        {
            // Why: 用户存在待应用/待还原意图时，不覆盖其勾选，避免“自动勾回”。
            if (toggle.HasPending)
            {
                continue;
            }

            var oldEnabled = toggle.IsEnabled;
            toggle.IsEnabled = toggle.IsApplied;
            if (oldEnabled != toggle.IsEnabled)
            {
                PageDiagnosticService.Log("MasterModuleTemplate", 
                    $"[{ModuleKey}] Toggle状态强制同步: {toggle.OperationId ?? toggle.Title} - {oldEnabled} → {toggle.IsEnabled}");
            }
        }
    }
    
    // P2-1: CPU Boost 互斥规则表 — operationId 对应其互斥项
    private static readonly Dictionary<string, string> _mutualExclusionRules = new()
    {
        { "cpu.processor_boost_mode", "cpu.disable_boost_mode" },
        { "cpu.disable_boost_mode", "cpu.processor_boost_mode" }
    };
    
    private void EnforceMutualExclusion(string? operationId, bool enabled)
    {
        if (!enabled || operationId == null) return;
        if (!_mutualExclusionRules.TryGetValue(operationId, out var conflictId)) return;
        
        var conflictToggle = _allToggles.FirstOrDefault(t => t.OperationId == conflictId);
        if (conflictToggle == null || !conflictToggle.IsEnabled) return;
        
        // 自动关闭互斥项
        conflictToggle.OnUserToggled(false);
        PageDiagnosticService.Log("MasterModuleTemplate", 
            $"[{ModuleKey}] P2-1 互斥: 启用 {operationId} → 自动关闭 {conflictId}");
        
        // 刷新 UI（重建 Toggle 行以反映状态变化）
        BindGroupedToggles();
    }
    
    private void SetApplyingState(bool isApplying, string? text = null)
    {
        var hasExecutablePending = _allToggles.Any(t => t.IsPending &&
            ((t.IsEnabled && t.CanApply) || (!t.IsEnabled && t.CanRestore)));

        ApplyBtnBorder.IsHitTestVisible = !isApplying && hasExecutablePending;
        ApplyBtnBorder.IsEnabled = !isApplying && hasExecutablePending;
        RestoreBtn.IsEnabled = !isApplying;
        ToggleListScrollViewer.IsEnabled = !isApplying;
        ApplyNormalContent.Visibility = isApplying ? Visibility.Collapsed : Visibility.Visible;
        ApplyProgressContent.Visibility = isApplying ? Visibility.Visible : Visibility.Collapsed;
        if (text != null) ApplyProgressText.Text = text;
        else if (isApplying) ApplyProgressText.Text = LocalizationService.Instance.Get("Module.Applying");
    }

    private string BuildBatchConfirmationMessage(
        IReadOnlyList<OptimizationToggleModel> toApply,
        IReadOnlyList<OptimizationToggleModel> toRestore,
        IReadOnlyList<OptimizationToggleModel> extremeItems,
        bool showRiskWarnings)
    {
        var lines = new List<string>
        {
            "本次确认会授权下面列出的全部项目；每项仍会执行原有的权限、能力、备份、写后校验和失败阻断。"
        };

        AppendBatchConfirmationSection(lines, "应用优化", toApply);
        AppendBatchConfirmationSection(lines, "恢复已应用设置", toRestore);

        if (showRiskWarnings && extremeItems.Count > 0)
        {
            lines.Add($"风险提示：包含 {extremeItems.Count} 项高风险操作。请确认已阅读其逐项说明与恢复限制。");
        }

        lines.Add("确认后将作为一个批次执行；若某项失败，系统会返回逐项结果并按原有策略尝试回滚已变更项。");
        return string.Join(Environment.NewLine, lines);
    }

    private void AppendBatchConfirmationSection(
        ICollection<string> lines,
        string heading,
        IReadOnlyList<OptimizationToggleModel> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        lines.Add($"{heading}（{items.Count} 项）：");
        foreach (var item in items)
        {
            lines.Add($"• [{GetRiskText(item.RiskLevel)}] {item.Title}");
        }
    }

    private bool TryBeginOperation(string progressText, out CancellationToken cancellationToken)
    {
        lock (_operationSync)
        {
            if (_isOperating || !_operationGate.TryEnter())
            {
                cancellationToken = default;
                return false;
            }

            _operationCts = new CancellationTokenSource();
            _isOperating = true;
            cancellationToken = _operationCts.Token;
        }

        SetApplyingState(true, progressText);
        return true;
    }

    private void CancelActiveOperation()
    {
        CancellationTokenSource? operationCts;
        lock (_operationSync)
        {
            operationCts = _operationCts;
        }

        try
        {
            operationCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed concurrently and already disposed its CTS.
        }
    }

    private void EndOperation()
    {
        CancellationTokenSource? operationCts;
        lock (_operationSync)
        {
            if (!_isOperating && _operationCts is null)
            {
                return;
            }

            _isOperating = false;
            operationCts = _operationCts;
            _operationCts = null;
        }

        _operationGate.Exit();
        operationCts?.Dispose();

        void RestoreInteractiveState()
        {
            SetApplyingState(false);
            UpdateAllCounts();
        }

        if (Dispatcher.CheckAccess())
        {
            RestoreInteractiveState();
        }
        else if (IsLoaded)
        {
            _ = Dispatcher.InvokeAsync(RestoreInteractiveState, DispatcherPriority.Send);
        }
    }

    private void ShowRebootNotice(Window? owner, IEnumerable<OperationExecutionSummary> summaries)
    {
        var dispatcher = owner?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ShowRebootNotice(owner, summaries));
            return;
        }

        var rebootPresentation = summaries
            .Select(CommandResultPresentationMapper.FromOptimization)
            .FirstOrDefault(presentation => presentation.Status == CommandResultStatus.RequiresReboot);
        if (rebootPresentation is null)
        {
            return;
        }

        InfoDialog.Show(owner, LocalizationService.Instance.Get("Dialog.Info"),
            rebootPresentation.DisplayText, IconGlyphs.Warning);
    }
    
    #region OPT-018: 配置持久化已删除 - 不再需要保存/加载Toggle状态
    // 新逻辑：Toggle状态永远等于系统实际状态，每次打开页面重新检测
    #endregion
    
    /// <summary>
    /// Task 9.3: 加载并渲染模块扩展
    /// 检查ModuleExtensionRegistry中是否有当前ModuleKey的扩展，如果有则创建并显示
    /// </summary>
    private void LoadModuleExtension()
    {
        // Guard: 确保在UI线程上执行
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(LoadModuleExtension);
            return;
        }
        
        // Guard: ModuleKey为空时不加载扩展
        if (string.IsNullOrWhiteSpace(ModuleKey))
        {
            PageDiagnosticService.Log("MasterModuleTemplate", 
                "Task 9.3: ModuleKey为空，跳过扩展加载");
            return;
        }
        
        try
        {
            var registry = ModuleExtensionRegistry.Instance;
            
            // 检查是否有注册的扩展
            if (!registry.HasExtension(ModuleKey))
            {
                PageDiagnosticService.Log("MasterModuleTemplate", 
                    $"Task 9.3: [{ModuleKey}] 没有注册的扩展");
                return;
            }
            
            var extension = registry.GetExtension(ModuleKey);
            if (extension == null)
            {
                PageDiagnosticService.Log("MasterModuleTemplate", 
                    $"Task 9.3: [{ModuleKey}] GetExtension返回null");
                return;
            }
            
            PageDiagnosticService.Log("MasterModuleTemplate", 
                $"Task 9.3: [{ModuleKey}] 找到扩展 {extension.GetType().Name}，开始创建内容");
            
            // 创建扩展内容
            var extensionContent = extension.CreateExtensionContent(_viewModel!);
            
            if (extensionContent == null)
            {
                PageDiagnosticService.Log("MasterModuleTemplate", 
                    $"Task 9.3: [{ModuleKey}] CreateExtensionContent返回null");
                return;
            }
            
            // 设置到ContentPresenter并显示
            ModuleExtensionContainer.Content = extensionContent;
            ModuleExtensionContainer.Visibility = Visibility.Visible;
            
            PageDiagnosticService.Log("MasterModuleTemplate", 
                $"Task 9.3: [{ModuleKey}] 扩展内容已加载并显示");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("MasterModuleTemplate", 
                $"Task 9.3: [{ModuleKey}] 加载扩展失败", ex);
            
            // 失败时确保容器隐藏
            ModuleExtensionContainer.Visibility = Visibility.Collapsed;
        }
    }
    
    /// <summary>
    /// Task 9.1: 显示恢复操作结果
    /// </summary>
    private void ShowRestoreResult(Window ownerWindow, BatchResult result, string operationName)
    {
        var loc = LocalizationService.Instance;
        
        if (result.SuccessCount > 0 && result.FailedCount == 0)
        {
            InfoDialog.Show(ownerWindow, loc.Get("Module.OperationComplete"),
                loc.Get("Module.AllOperationsSuccess"), IconGlyphs.Success);
        }
        else if (result.SuccessCount == 0 && result.FailedCount > 0)
        {
            InfoDialog.Show(ownerWindow, loc.Get("Common.Error"),
                loc.Get("Module.AllOperationsFailed"), IconGlyphs.Warning);
        }
        else if (result.SuccessCount > 0 && result.FailedCount > 0)
        {
            InfoDialog.Show(ownerWindow, loc.Get("Module.PartialSuccess"),
                loc.Get("Module.PartialOperationsResult", result.SuccessCount, result.FailedCount), IconGlyphs.Warning);
        }
    }
}
