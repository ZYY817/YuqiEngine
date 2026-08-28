﻿// File: GameOptimizer.Shell/Views/EsportsModePage.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Controls;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Services.Baseline;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

/// <summary>
/// 电竞模式页面 - 重构版：简洁、可靠、易维护
/// </summary>
public partial class EsportsModePage : UserControl
{
    // 核心服务
    private readonly DashboardViewModel _viewModel;
    private GameModeHubViewModel? _gameModeViewModel;
    private EsportsModeStateService? _stateService;
    private readonly EsportsPresetPlanner _presetPlanner = new();
    private readonly IBaselineGateService _baselineGate;
    private readonly IUserPrivilegeService _privilegeService;
    private readonly OptimizationGuardService _guardService;
    private readonly IHardwareDetectionService _hardwareDetectionService;
    
    // 状态管理
    private bool _isInitialized;
    private bool _isOperating;
    private bool _isStateReadInProgress = true;
    private bool _isApplyingPreset;
    private bool _legacyExtremeSelectionCleared;
    private StrategyType? _selectedStrategy;
    private CancellationTokenSource? _lifetimeCancellation;
    private EsportsDetectionReport? _lastDetectionReport;
    
    // 数据集合
    private readonly Dictionary<string, OptimizationRow> _allRows = new();
    private readonly HashSet<string> _selectedItems = new();
    private readonly HashSet<string> _appliedItems = new();
    
    // UI状态
    private HardwareInfo? _hardwareInfo;
    private bool _hardwareDetectionSucceeded;
    private OptimizationRow? _selectedRow;
    
    // 守护服务通知节流
    private readonly Dictionary<string, DateTime> _lastNotificationTime = new();
    private const int NotificationThrottleSeconds = 300; // 5分钟
    private const int CurrentPresetPolicyVersion = 1;

    public EsportsModePage(
        DashboardViewModel viewModel,
        IBaselineGateService baselineGate,
        IUserPrivilegeService privilegeService,
        OptimizationGuardService guardService,
        IHardwareDetectionService hardwareDetectionService)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _baselineGate = baselineGate ?? throw new ArgumentNullException(nameof(baselineGate));
        _privilegeService = privilegeService ?? throw new ArgumentNullException(nameof(privilegeService));
        _guardService = guardService ?? throw new ArgumentNullException(nameof(guardService));
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        InitializeComponent();
        DataContext = _viewModel;
        GuardPanel.AttachGuardService(_guardService);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region 生命周期管理

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _lifetimeCancellation?.Dispose();
        var lifetimeCancellation = new CancellationTokenSource();
        _lifetimeCancellation = lifetimeCancellation;
        var lifetimeToken = lifetimeCancellation.Token;
        _isStateReadInProgress = true;
        SetDetectionStatus("正在后台读取电竞模式所需状态…", isLoading: true);
        UpdateActionAvailability();
        
        EsportsModeStartupLogger.Log("INIT", "=== 电竞模式页面启动 ===");
        
        try
        {
            // 步骤 0: 确保XAML资源完全加载
            EsportsModeStartupLogger.Log("STEP0", "等待XAML资源加载完成...");
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
            await Task.Delay(100); // 额外延迟确保动态资源绑定完成
            EsportsModeStartupLogger.LogSuccess("STEP0");
            
            // 步骤 1: 初始化服务
            EsportsModeStartupLogger.Log("STEP1", "开始初始化服务...");
            EsportsModeStartupLogger.LogSuccess("STEP1");
            
            // 步骤 2: 创建状态服务
            EsportsModeStartupLogger.Log("STEP2", "创建 EsportsModeStateService...");
            _stateService = new EsportsModeStateService(
                _viewModel.OptimizationEngine,
                _baselineGate,
                () => _privilegeService.IsAdministrator);
            EsportsModeStartupLogger.LogSuccess("STEP2");
            
            EsportsModeStartupLogger.Log("STEP3", "创建 GameModeHubViewModel...");
            _gameModeViewModel = new GameModeHubViewModel(_viewModel, _hardwareDetectionService);
            await _gameModeViewModel.UpdateHardwareHintsAsync();
            EsportsModeStartupLogger.LogSuccess("STEP3");
            
            // 步骤 4: 检测硬件
            EsportsModeStartupLogger.Log("STEP4", "检测硬件信息...");
            await DetectHardwareAsync();
            if (_hardwareDetectionSucceeded)
            {
                EsportsModeStartupLogger.LogSuccess("STEP4");
            }
            else
            {
                EsportsModeStartupLogger.LogWarning("STEP4", "硬件检测失败，写入操作将保持阻断");
            }
            
            // 步骤 5: 初始化UI
            EsportsModeStartupLogger.Log("STEP5", "初始化所有分类...");
            InitializeAllCategories();
            EsportsModeStartupLogger.LogSuccess("STEP5");
            
            EsportsModeStartupLogger.Log("STEP6", "更新本地化文本...");
            UpdateLocalizedTexts();
            EsportsModeStartupLogger.LogSuccess("STEP6");
            
            // 步骤 7: 只加载用户选择；已应用状态必须来自真实检测
            EsportsModeStartupLogger.Log("STEP7", "加载用户选择...");
            LoadSavedConfiguration();
            EsportsModeStartupLogger.LogSuccess("STEP7");

            // 步骤 8: 检测系统状态，覆盖任何历史派生缓存
            EsportsModeStartupLogger.Log("STEP8", "检测系统状态...");
            await DetectSystemStatesAsync(lifetimeToken);
            lifetimeToken.ThrowIfCancellationRequested();
            if (_lastDetectionReport?.IsReadyForPlanning == true)
            {
                EsportsModeStartupLogger.LogSuccess("STEP8");
            }
            else
            {
                EsportsModeStartupLogger.LogWarning("STEP8", "系统状态检测不完整，写入操作保持阻断");
            }
            
            // 步骤 9: 更新UI
            EsportsModeStartupLogger.Log("STEP9", "更新策略按钮...");
            UpdateStrategyButtons();
            EsportsModeStartupLogger.LogSuccess("STEP9");
            
            EsportsModeStartupLogger.Log("STEP10", "更新计数...");
            UpdateCounts();
            EsportsModeStartupLogger.LogSuccess("STEP10");
            
            // 步骤 11: 更新状态文本
            EsportsModeStartupLogger.Log("STEP11", "更新状态文本...");
            UpdateStatusText();
            EsportsModeStartupLogger.LogSuccess("STEP11");
            
            // 步骤 12: 恢复守护服务状态
            EsportsModeStartupLogger.Log("STEP12", "恢复守护服务状态...");
            await RestoreGuardServiceStateAsync(lifetimeToken);
            lifetimeToken.ThrowIfCancellationRequested();
            EsportsModeStartupLogger.Log("STEP12", "守护服务状态恢复步骤已结束；失败会在 GUARD 日志中单列");
            
            _isInitialized = true;
            if (_selectedStrategy is StrategyType.Safe or StrategyType.Balanced &&
                _hardwareDetectionSucceeded && _lastDetectionReport?.IsReadyForPlanning == true)
            {
                // Saved recommendation modes are regenerated for the current machine instead of
                // trusting selections persisted by an older or different hardware configuration.
                ApplyStrategyPreset(_selectedStrategy.Value);
            }
            else if (_legacyExtremeSelectionCleared)
            {
                SetDetectionStatus(
                    "旧版“狂暴模式”的批量选择已安全清空，请在“自定义”中重新逐项确认。",
                    isLoading: false);
                SaveConfiguration();
            }
            UpdateActionAvailability();
            
            // 订阅语言变化事件
            GameOptimizer.Core.Localization.LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            
            EsportsModeStartupLogger.Log("COMPLETE", "=== 电竞模式页面启动完成 ===");
            EsportsModeStartupLogger.Log("COMPLETE", $"日志文件: {EsportsModeStartupLogger.GetLogFilePath()}");
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            EsportsModeStartupLogger.Log("INIT", "页面已离开，后台状态读取已取消");
        }
        catch (Exception ex)
        {
            _isInitialized = false;
            _isStateReadInProgress = false;
            EsportsModeStartupLogger.LogError("FATAL", "启动失败", ex);
            SetDetectionStatus("初始化失败，应用与恢复操作已禁用。请重新进入页面重试。", isLoading: false, isError: true);
            UpdateActionAvailability();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;
        GameOptimizer.Core.Localization.LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        _guardService.OperationsReverted -= OnGuardOperationsReverted;
        _guardService.OperationsReapplied -= OnGuardOperationsReapplied;
        
        foreach (var row in _allRows.Values)
        {
            row.CheckedChanged -= OnRowCheckedChanged;
            row.DetailsClicked -= OnRowDetailsClicked;
        }
        
        _allRows.Clear();
        _selectedItems.Clear();
        _appliedItems.Clear();
        _selectedRow = null;
        _lastDetectionReport = null;
        _isStateReadInProgress = false;
        _isInitialized = false;
        UpdateActionAvailability();
        
        if (_gameModeViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        Dispatcher.Invoke(UpdateLocalizedTexts);
    }

    #endregion

    #region 硬件检测

    private async Task DetectHardwareAsync()
    {
        try
        {
            _hardwareInfo = await _hardwareDetectionService.GetOrDetectAsync();
            _hardwareDetectionSucceeded = _hardwareInfo != null;
            UpdateHardwareCards();
        }
        catch (Exception ex)
        {
            _hardwareDetectionSucceeded = false;
            _hardwareInfo = new HardwareInfo();
            EsportsModeStartupLogger.LogError("HARDWARE", "硬件检测失败", ex);
            UpdateHardwareCards();
        }
    }

    private void UpdateHardwareCards()
    {
        if (_hardwareInfo == null) return;
        
        CpuVendorText.Text = _hardwareInfo.CpuVendor == CpuVendor.Intel ? "Intel Core" 
            : _hardwareInfo.CpuVendor == CpuVendor.AMD ? "AMD Ryzen" : "Unknown CPU";
        
        CpuFeatureText.Text = _hardwareInfo.CpuVendor == CpuVendor.Intel ? "Speed Shift" 
            : _hardwareInfo.IsAmdX3D ? "3D V-Cache" : "PBO";
        
        // GPU 卡片必须使用集中清单选出的展示适配器；多 GPU 机器上不能按厂商存在性猜测当前主卡。
        GpuVendorText.Text = string.IsNullOrWhiteSpace(_hardwareInfo.GpuName) || _hardwareInfo.GpuName == "Unknown"
            ? "GPU"
            : _hardwareInfo.GpuName;
        GpuFeatureText.Text = $"{GetGpuKindText(_hardwareInfo.PrimaryGpuKind)} · Driver {_hardwareInfo.GpuDriverVersion}";
        
        HwCpuLabel.Text = _hardwareInfo.CpuVendor == CpuVendor.Intel ? "Intel" 
            : _hardwareInfo.CpuVendor == CpuVendor.AMD ? "AMD" : "CPU";
        
        HwGpuLabel.Text = _hardwareInfo.GetGpuVendorDisplayName();
    }

    private static string GetGpuKindText(GameOptimizer.Core.Models.GpuAdapterKind kind) => kind switch
    {
        GameOptimizer.Core.Models.GpuAdapterKind.Integrated => "集显",
        GameOptimizer.Core.Models.GpuAdapterKind.Discrete => "独显",
        GameOptimizer.Core.Models.GpuAdapterKind.SoftwareOrVirtual => "软件或虚拟",
        _ => "类型未知"
    };

    #endregion

    #region 状态检测（核心）

    /// <summary>
    /// 检测系统状态 - 简单直接
    /// </summary>
    private async Task DetectSystemStatesAsync(CancellationToken cancellationToken = default)
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        _isStateReadInProgress = true;
        _lastDetectionReport = null;
        _appliedItems.Clear();
        SetDetectionStatus($"{loc.Get("Esports.DetectingState")}…", isLoading: true);
        UpdateActionAvailability();
        UpdateRowStateIndicators();
        UpdateCounts();

        try
        {
            if (_stateService == null || _gameModeViewModel == null)
            {
                throw new InvalidOperationException("电竞模式状态服务尚未初始化。");
            }

            // 获取所有优化项ID
            var allOperationIds = _allRows.Keys.ToList();

            // 检测状态
            var progress = new Progress<EsportsWorkflowProgress>(p =>
            {
                SetDetectionStatus(
                    $"{loc.Get("Esports.DetectingState")} ({p.Current}/{p.Total})：{p.Title}",
                    isLoading: true);
            });

            _lastDetectionReport = await _stateService.DetectAllStatesAsync(
                allOperationIds,
                progress,
                cancellationToken);

            // A missing operation is a conclusive capability result. Keep it visible for
            // explanation, but fail closed for selection instead of leaving the whole page loading.
            _isApplyingPreset = true;
            try
            {
                foreach (var unavailable in _lastDetectionReport.Items.Where(static item =>
                             item.Outcome == EsportsProbeOutcome.MissingOperation))
                {
                    if (_allRows.TryGetValue(unavailable.OperationId, out var row))
                    {
                        row.IsCompatible = false;
                        row.IsChecked = false;
                    }

                    _selectedItems.Remove(unavailable.OperationId);
                    var item = GetItemByOperationId(unavailable.OperationId);
                    if (item != null)
                    {
                        item.IsCompatible = false;
                        item.IncompatibleReason = "当前版本未提供此操作，已按失败关闭策略禁用。";
                    }
                }
            }
            finally
            {
                _isApplyingPreset = false;
            }

            // 已应用状态是系统回读的派生值；每次检测整体替换，禁止配置缓存覆盖。
            _appliedItems.Clear();
            foreach (var (opId, isApplied) in _lastDetectionReport.States)
            {
                if (isApplied)
                {
                    _appliedItems.Add(opId);
                }
            }

            var unavailableCount = _lastDetectionReport.Items.Count(static item =>
                item.Outcome == EsportsProbeOutcome.MissingOperation);
            var failedCount = _lastDetectionReport.Items.Count(static item =>
                item.Outcome is not EsportsProbeOutcome.Detected and not EsportsProbeOutcome.MissingOperation);
            EsportsModeStartupLogger.Log(
                "DETECT",
                $"检测完成：{_appliedItems.Count} 项已应用，{failedCount} 项状态未知");
            
            // 更新UI状态指示器（不改变勾选状态）
            UpdateRowStateIndicators();
            
            // 更新计数器和状态文本
            UpdateCounts();
            UpdateStatusText();

            var detectionMessage = !_lastDetectionReport.IsReadyForPlanning
                ? $"状态读取不完整：{failedCount} 项状态未知，应用与恢复已禁用。"
                : !_hardwareDetectionSucceeded
                    ? "系统状态读取完成，但硬件信息未确认，应用与恢复已禁用。"
                    : unavailableCount > 0
                        ? $"状态读取完成：{unavailableCount} 项当前不可用并已禁选，其余项目可进入计划。"
                        : $"状态读取完成：已确认 {_lastDetectionReport.Items.Count} 项，可执行应用或恢复。";
            SetDetectionStatus(
                detectionMessage,
                isLoading: false,
                isError: !_lastDetectionReport.IsReadyForPlanning || !_hardwareDetectionSucceeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _lastDetectionReport = null;
            SetDetectionStatus("状态读取已取消，应用与恢复操作保持禁用。", isLoading: false, isError: true);
        }
        catch (Exception ex)
        {
            _lastDetectionReport = null;
            EsportsModeStartupLogger.LogError("DETECT", "状态检测失败", ex);
            SetDetectionStatus("状态读取失败，应用与恢复操作已禁用。请重新进入页面重试。", isLoading: false, isError: true);
        }
        finally
        {
            _isStateReadInProgress = false;
            UpdateActionAvailability();
        }
    }

    private void SetDetectionStatus(string message, bool isLoading, bool isError = false)
    {
        DetectionStatusText.Text = message;
        DetectionProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        DetectionStatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xF4, 0x7C, 0x7C))
            : TryFindResource("Theme.Brush.TextMuted") as Brush
              ?? new SolidColorBrush(Color.FromRgb(0xA1, 0xA9, 0xC4));
    }

    private void UpdateActionAvailability()
    {
        var canExecute = _isInitialized &&
                         !_isOperating &&
                         !_isStateReadInProgress &&
                         _stateService != null &&
                         _hardwareDetectionSucceeded &&
                         _lastDetectionReport?.IsReadyForPlanning == true;

        SetActionButtonState(ApplyBtn, canExecute);
        SetActionButtonState(RestoreUserBtn, canExecute);
        SetActionButtonState(RestoreWinBtn, canExecute);
        SetActionButtonState(SafeBtn, canExecute);
        SetActionButtonState(BalancedBtn, canExecute);
        SetActionButtonState(ExtremeBtn, canExecute);
    }

    private static void SetActionButtonState(Border button, bool isEnabled)
    {
        button.IsEnabled = isEnabled;
        button.Opacity = isEnabled ? 1 : 0.55;
        button.Cursor = isEnabled ? Cursors.Hand : Cursors.Arrow;
    }

    #endregion

    #region UI初始化

    private void InitializeAllCategories()
    {
        if (_gameModeViewModel == null) return;
        
        InitializeCategory(FpsItemsControl, _gameModeViewModel.FpsItems);
        InitializeCategory(InputItemsControl, _gameModeViewModel.InputItems);
        InitializeCategory(NetworkItemsControl, _gameModeViewModel.NetworkItems);
        InitializeCategory(StorageItemsControl, _gameModeViewModel.StorageItems);
        InitializeCategory(WindowsItemsControl, _gameModeViewModel.WindowsItems);
        InitializeCategory(SecurityItemsControl, _gameModeViewModel.SecurityItems);
        
        var cpuItems = _gameModeViewModel.CpuVendorItems
            .Where(i => i.OperationId.StartsWith("intel.") || 
                       (i.OperationId.StartsWith("amd.") && !i.OperationId.Contains("radeon")) || 
                       i.OperationId.StartsWith("cpu."))
            .ToList();
        
        var gpuItems = _gameModeViewModel.CpuVendorItems
            .Where(i => i.OperationId.StartsWith("gpu.") || 
                       i.OperationId.Contains("radeon") || 
                       i.OperationId.StartsWith("nvidia."))
            .ToList();
        
        InitializeCategory(CpuItemsControl, cpuItems);
        InitializeCategory(GpuItemsControl, gpuItems);
    }

    private void InitializeCategory(ItemsControl container, IEnumerable<GameModeOptimizationItem> items)
    {
        container.Items.Clear();
        
        foreach (var item in items)
        {
            var row = new OptimizationRow
            {
                OperationId = item.OperationId,
                Title = item.Title,
                Description = item.Description ?? "",
                Icon = GetItemIcon(item),
                IsExtreme = item.RiskLevel == OperationRiskLevel.Extreme,
                IsCompatible = item.IsCompatible,
                IsChecked = false,
                IsEnabled = true  // 始终启用，在点击时检查兼容性
            };
            
            row.CheckedChanged += OnRowCheckedChanged;
            row.DetailsClicked += OnRowDetailsClicked;
            row.MouseEnter += (s, e) => OnRowHover(row, item);
            row.MouseLeftButtonDown += (s, e) => OnRowSelect(row, item);
            
            container.Items.Add(row);
            _allRows[item.OperationId] = row;
        }
    }

    private string GetItemIcon(GameModeOptimizationItem item)
    {
        var id = item.OperationId.ToLowerInvariant();
        
        if (id.Contains("hags") || id.Contains("gpu_schedule")) return IconGlyphs.Target;
        if (id.Contains("game") && id.Contains("mode")) return IconGlyphs.Game;
        if (id.Contains("mouse") || id.Contains("keyboard")) return IconGlyphs.Peripheral;
        if (id.Contains("timer") || id.Contains("hpet")) return IconGlyphs.Clock;
        if (id.Contains("nagle") || id.Contains("tcp") || id.Contains("network")) return IconGlyphs.Network;
        if (id.Contains("security") || id.Contains("vbs") || id.Contains("hvci")) return IconGlyphs.Shield;
        if (id.Contains("storage") || id.Contains("ntfs")) return IconGlyphs.Disk;
        
        return IconGlyphs.Power;
    }

    #endregion

    #region 策略模式选择

    private void SafeBtn_Click(object sender, MouseButtonEventArgs e) => SetStrategy(StrategyType.Safe);
    private void BalancedBtn_Click(object sender, MouseButtonEventArgs e) => SetStrategy(StrategyType.Balanced);
    private void ExtremeBtn_Click(object sender, MouseButtonEventArgs e) => SetStrategy(StrategyType.Extreme);

    private void SetStrategy(StrategyType strategy)
    {
        if (!_isInitialized || _isOperating || _isStateReadInProgress ||
            _stateService == null || _gameModeViewModel == null ||
            !_hardwareDetectionSucceeded || _lastDetectionReport?.IsReadyForPlanning != true)
        {
            return;
        }

        _selectedStrategy = strategy;
        if (strategy == StrategyType.Extreme)
        {
            SetDetectionStatus(GetCustomPresetStatus(), isLoading: false);
        }
        else
        {
            ApplyStrategyPreset(strategy);
        }

        UpdateStrategyButtons();
        UpdateCounts();
        UpdateStatusText();
        SaveConfiguration();
    }

    private void ApplyStrategyPreset(StrategyType strategy)
    {
        if (_gameModeViewModel == null || _stateService == null) return;

        var presetKind = strategy switch
        {
            StrategyType.Safe => EsportsPresetKind.Conservative,
            StrategyType.Balanced => EsportsPresetKind.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Custom mode is not an automatic preset.")
        };
        
        EsportsModeStartupLogger.Log("STRATEGY", $"生成动态推荐: {presetKind}");

        var allItems = GetAllOptimizationItems();
        var preflightIds = _presetPlanner.GetPreflightOperationIds(presetKind, allItems);
        var preflight = _stateService.CreatePlan(preflightIds, EsportsWorkflowAction.Apply);
        var recommendation = _presetPlanner.CreateRecommendation(
            presetKind,
            allItems,
            preflight.Items.Select(static item => item.OperationId));
        var selectedIds = recommendation.SelectedOperationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // 清空待应用列表（但保留已应用列表）
        _selectedItems.Clear();

        _isApplyingPreset = true;
        try
        {
            foreach (var (opId, row) in _allRows)
            {
                if (_appliedItems.Contains(opId)) continue;

                var shouldSelect = selectedIds.Contains(opId);
                row.IsChecked = shouldSelect;
                if (shouldSelect)
                {
                    _selectedItems.Add(opId);
                    EsportsModeStartupLogger.Log("STRATEGY", $"✓ 动态推荐: {opId}");
                }
            }
        }
        finally
        {
            _isApplyingPreset = false;
        }
        
        EsportsModeStartupLogger.Log(
            "STRATEGY",
            $"动态推荐完成：待应用 {_selectedItems.Count} 项，预检阻断 {recommendation.BlockedCount} 项，策略排除 {recommendation.PolicyExcludedCount} 项");
        
        UpdateRowStateIndicators();
        UpdateCounts();
        UpdateStatusText();
        SetDetectionStatus(GetPresetRecommendationStatus(recommendation), isLoading: false);
        SaveConfiguration();
    }

    private void UpdateStrategyButtons()
    {
        try
        {
            EsportsModeStartupLogger.Log("UI_UPDATE", "UpdateStrategyButtons 开始");
            
            var inactiveBrush = new SolidColorBrush(Colors.Transparent);
            
            // 使用TryFindResource并提供默认值
            var safeColor = TryFindResource("Theme.Color.EsportsSafeSelected") as Color? ?? Color.FromArgb(0x15, 0x4A, 0xDE, 0x80);
            var balancedColor = TryFindResource("Theme.Color.EsportsBalancedSelected") as Color? ?? Color.FromArgb(0x30, 0x63, 0x66, 0xF1);
            var customColor = Color.FromArgb(0x18, 0x8B, 0x5C, 0xF6);
            
            var activeForeground = TryFindResource("Theme.Brush.TextPrimary") as Brush ?? new SolidColorBrush(Colors.White);
            var inactiveForeground = TryFindResource("Theme.Brush.TextMuted") as Brush ?? new SolidColorBrush(Color.FromRgb(0xA1, 0xA9, 0xC4));
            
            SafeBtn.Background = _selectedStrategy == StrategyType.Safe ? new SolidColorBrush(safeColor) : inactiveBrush;
            BalancedBtn.Background = _selectedStrategy == StrategyType.Balanced ? new SolidColorBrush(balancedColor) : inactiveBrush;
            ExtremeBtn.Background = _selectedStrategy == StrategyType.Extreme ? new SolidColorBrush(customColor) : inactiveBrush;
            
            SafeBtnText.Foreground = _selectedStrategy == StrategyType.Safe ? activeForeground : inactiveForeground;
            BalancedBtnText.Foreground = _selectedStrategy == StrategyType.Balanced ? activeForeground : inactiveForeground;
            ExtremeBtnText.Foreground = _selectedStrategy == StrategyType.Extreme ? activeForeground : inactiveForeground;
            
            SafeBtnText.FontWeight = _selectedStrategy == StrategyType.Safe ? FontWeights.Medium : FontWeights.Normal;
            BalancedBtnText.FontWeight = _selectedStrategy == StrategyType.Balanced ? FontWeights.Medium : FontWeights.Normal;
            ExtremeBtnText.FontWeight = _selectedStrategy == StrategyType.Extreme ? FontWeights.Medium : FontWeights.Normal;
            
            UpdateAmbientGlow();
            
            EsportsModeStartupLogger.LogSuccess("UI_UPDATE");
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("UI_UPDATE", "UpdateStrategyButtons 失败", ex);
            throw;
        }
    }

    private void UpdateAmbientGlow()
    {
        Color color = _selectedStrategy switch
        {
            StrategyType.Safe => Color.FromArgb(0x18, 0x4A, 0xDE, 0x80),
            StrategyType.Balanced => Color.FromArgb(0x18, 0x63, 0x66, 0xF1),
            StrategyType.Extreme => Color.FromArgb(0x18, 0x8B, 0x5C, 0xF6),
            _ => Color.FromArgb(0x15, 0x4A, 0xDE, 0x80)
        };
        
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0),
            GradientOrigin = new Point(0.5, 0),
            RadiusX = 0.8,
            RadiusY = 1.2
        };
        brush.GradientStops.Add(new GradientStop(color, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        
        AmbientGlow.Background = brush;
    }

    #endregion

    #region 应用和恢复操作

    private async void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _isOperating || _isStateReadInProgress || _stateService == null || _gameModeViewModel == null) return;

        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        if (_lastDetectionReport?.IsReadyForPlanning != true)
        {
            ShowWarning("状态未确认", "部分优化项尚未完成真实状态检测。请重新进入页面完成检测后再应用。");
            return;
        }

        if (!_hardwareDetectionSucceeded)
        {
            ShowWarning("硬件状态未知", "硬件检测未完成，电竞模式已在计划阶段阻断写入。请重新进入页面后重试。");
            return;
        }

        var pendingIds = _selectedItems.Except(_appliedItems).ToList();
        if (pendingIds.Count == 0)
        {
            ShowInfo(
                loc.Get("Common.Tip") ?? "提示", 
                loc.Get("Esports.ConfirmApply.AllApplied") ?? "所有选中项已应用");
            return;
        }

        var allItems = GetAllOptimizationItems();
        var incompatibleItems = allItems
            .Where(item => pendingIds.Contains(item.OperationId, StringComparer.OrdinalIgnoreCase) && !item.IsCompatible)
            .Select(static item => item.Title)
            .ToList();
        if (incompatibleItems.Count > 0)
        {
            ShowWarning(
                "计划已阻断",
                $"以下项目与当前硬件不兼容：\n{string.Join("\n", incompatibleItems.Take(5).Select(title => $"• {title}"))}");
            return;
        }

        var plan = _stateService.CreatePlan(pendingIds, EsportsWorkflowAction.Apply);
        if (!plan.CanExecute)
        {
            ShowPlanBlocked(plan);
            return;
        }

        var confirmInfo = BuildApplyConfirmInfo(
            plan.Items.Select(static item => item.OperationId),
            allItems,
            GetCurrentStrategyName());
        var confirmed = EsportsModeApplyConfirmDialog.Show(Window.GetWindow(this), confirmInfo);
        if (confirmed != true) return;

        _isOperating = true;
        UpdateActionAvailability();
        ProgressDialog? progressDialog = null;

        try
        {
            progressDialog = new ProgressDialog
            {
                Owner = Window.GetWindow(this),
                Title = loc.Get("Esports.Progress.Title") ?? "正在应用优化"
            };
            progressDialog.EnableCancellation();
            progressDialog.Show();
            await Task.Delay(50);

            var progress = new Progress<EsportsWorkflowProgress>(item =>
            {
                progressDialog.UpdateProgress(item.Current, item.Total, item.Title);
            });
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                progressDialog.CancellationToken,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            var report = await _stateService.ExecuteApplyPlanAsync(plan, progress, linkedCancellation.Token);
            progressDialog.Close();

            foreach (var result in report.Results.Where(static result => result.Success))
            {
                _appliedItems.Add(result.OperationId);
                _selectedItems.Remove(result.OperationId);
            }

            var (successItems, failedItems, skippedItems) = BuildResultItems(report, allItems);
            SaveConfiguration();
            UpdateRowStateIndicators();
            UpdateCounts();
            UpdateStatusText();

            var resultDialog = new OptimizationResultDialog { Owner = Window.GetWindow(this) };
            resultDialog.SetResults(successItems, failedItems, skippedItems);
            resultDialog.ShowDialog();

            // 重新检测是最终真值来源；任何回读不完整都会继续阻断下一次应用。
            await DetectSystemStatesAsync(_lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("APPLY", "应用流程异常", ex);
            ShowError("应用失败", ex.Message);
        }
        finally
        {
            progressDialog?.Close();
            _isOperating = false;
            UpdateActionAvailability();
        }
    }

    private async void RestoreUserBtn_Click(object sender, RoutedEventArgs e)
    {
        await RestoreSelectedAsync("恢复用户默认");
    }

    private async void RestoreBtn_Click(object sender, MouseButtonEventArgs e)
    {
        await RestoreSelectedAsync("恢复用户默认");
    }

    private async void RestoreWinBtn_Click(object sender, RoutedEventArgs e)
    {
        await RestoreSelectedAsync("恢复Windows默认");
    }

    private async void RestoreWinBtn_Click(object sender, MouseButtonEventArgs e)
    {
        await RestoreSelectedAsync("恢复Windows默认");
    }

    private async Task RestoreSelectedAsync(string title)
    {
        if (!_isInitialized || _isOperating || _isStateReadInProgress || _stateService == null) return;

        if (_lastDetectionReport?.IsReadyForPlanning != true)
        {
            ShowWarning("状态未确认", "部分优化项状态未知，恢复操作已阻断。请先重新检测。");
            return;
        }

        var restoreIds = _appliedItems.ToList();
        if (restoreIds.Count == 0)
        {
            ShowInfo("无需恢复", "当前未检测到已应用的电竞优化项。");
            return;
        }

        var plan = _stateService.CreatePlan(restoreIds, EsportsWorkflowAction.Restore);
        if (!plan.CanExecute)
        {
            ShowPlanBlocked(plan);
            return;
        }

        // 确认对话框
        var confirmed = ConfirmDialog.Show(
            Window.GetWindow(this),
            title,
            $"此操作将逐项恢复 {restoreIds.Count} 个已检测为已应用的电竞优化项。任一项失败时会停止后续恢复，是否继续？",
            IconGlyphs.Warning);
        
        if (!confirmed) return;
        
        _isOperating = true;
        UpdateActionAvailability();
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        ProgressDialog? progressDialog = null;
        
        try
        {
            // 显示进度对话框
            progressDialog = new ProgressDialog
            {
                Owner = Window.GetWindow(this),
                Title = title,
                Message = loc.Get("Operation.PleaseWait")
            };
            progressDialog.EnableCancellation();
            progressDialog.Show();
            await Task.Delay(50);

            var progress = new Progress<EsportsWorkflowProgress>(p =>
            {
                progressDialog.UpdateProgress(p.Current, p.Total, p.Title);
            });
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                progressDialog.CancellationToken,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            var report = await _stateService.ExecuteRestorePlanAsync(plan, progress, linkedCancellation.Token);
            progressDialog.Close();

            var allItems = GetAllOptimizationItems();
            foreach (var result in report.Results.Where(static result => result.Success))
            {
                _appliedItems.Remove(result.OperationId);
                _selectedItems.Remove(result.OperationId);
            }

            var (successItems, failedItems, skippedItems) = BuildResultItems(report, allItems);
            SaveConfiguration();
            var resultDialog = new OptimizationResultDialog { Owner = Window.GetWindow(this) };
            resultDialog.SetResults(successItems, failedItems, skippedItems);
            resultDialog.ShowDialog();

            await DetectSystemStatesAsync(_lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError("恢复失败", ex.Message);
        }
        finally
        {
            progressDialog?.Close();
            _isOperating = false;
            UpdateActionAvailability();
        }
    }

    #endregion

    #region UI事件处理

    private void OnRowCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not OptimizationRow row) return;
        if (_isApplyingPreset) return;
        
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        
        // 如果不兼容，阻止勾选并提示
        if (!row.IsCompatible && row.IsChecked)
        {
            row.IsChecked = false;
            ShowWarning(
                loc.Get("Common.Warning") ?? "警告", 
                loc.Get("Esports.IncompatibleHardware") ?? "此优化项与您的硬件不兼容，无法应用");
            return;
        }

        _selectedStrategy = StrategyType.Extreme;
        UpdateStrategyButtons();
        
        // 核心逻辑修改：已应用的项允许用户关闭（相当于恢复操作）
        if (row.IsChecked)
        {
            // 用户勾选：添加到待应用列表
            _selectedItems.Add(row.OperationId);
            EsportsModeStartupLogger.Log("TOGGLE", $"用户勾选: {row.OperationId}");
        }
        else
        {
            // 用户取消勾选只改变待执行选择；系统真实状态只能由检测/恢复回读改变。
            _selectedItems.Remove(row.OperationId);

            if (_appliedItems.Contains(row.OperationId))
            {
                row.IsChecked = true;
                ShowInfo("已应用项", "该项已由系统回读确认为已应用。请使用页面底部的恢复操作执行真实回退。");
            }
            else
            {
                EsportsModeStartupLogger.Log("TOGGLE", $"用户取消勾选: {row.OperationId}");
            }
        }
        
        // 更新UI
        UpdateRowStateIndicators();
        UpdateCounts();
        UpdateStatusText();
        SaveConfiguration();
    }

    private void OnRowDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not OptimizationRow row || _viewModel == null) return;
        
        // 从 OptimizationModules 中查找对应的 toggle 获取详情信息
        Models.OptimizationToggleModel? toggle = null;
        foreach (var module in _viewModel.OptimizationModules)
        {
            toggle = module.Toggles.FirstOrDefault(t => t.OperationId == row.OperationId);
            if (toggle != null) break;
        }
        
        if (toggle != null && !string.IsNullOrEmpty(toggle.RegistryPath))
        {
            ShowRegistryInfo(toggle);
            return;
        }
        
        if (toggle != null && (toggle.HasManualInfo || toggle.OperationType != Core.Optimization.OperationType.Other))
        {
            ShowOperationInfo(toggle);
            return;
        }
        
        // 显示通用提示
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        var item = GetItemByOperationId(row.OperationId);
        var title = item?.Title ?? row.Title;
        var desc = item?.Description ?? loc.Get("Esports.NoDescription");
        
        InfoDialog.Show(
            Window.GetWindow(this),
            title,
            $"{desc}\n\n{loc.Get("Module.AutoOperation")}",
            IconGlyphs.Lightbulb);
    }

    private void OnRowHover(OptimizationRow row, GameModeOptimizationItem item)
    {
        UpdateContextRail(item);
        UpdatePanelBorderColor(item.RiskLevel);
    }

    private void OnRowSelect(OptimizationRow row, GameModeOptimizationItem item)
    {
        if (_selectedRow != null) _selectedRow.ClearHighlight();
        _selectedRow = row;
        row.SetHighlight();
        UpdateContextRail(item);
        UpdatePanelBorderColor(item.RiskLevel);
    }

    #endregion

    #region UI更新

    private void UpdateRowStateIndicators()
    {
        EsportsModeStartupLogger.Log("UI_UPDATE", $"UpdateRowStateIndicators: _appliedItems.Count={_appliedItems.Count}, _selectedItems.Count={_selectedItems.Count}");
        
        int appliedCount = 0, pendingCount = 0, noneCount = 0;
        
        foreach (var (opId, row) in _allRows)
        {
            var isApplied = _appliedItems.Contains(opId);
            var isSelected = _selectedItems.Contains(opId);
            
            var oldState = row.StateIndicator;
            RowStateIndicator newState;
            
            // 核心逻辑修改：已应用的项显示为"已应用"，并且Toggle保持开启状态
            if (isApplied)
            {
                newState = RowStateIndicator.Applied;
                appliedCount++;
                
                // 已应用的项，Toggle应该显示为开启状态
                if (!row.IsChecked)
                {
                    row.IsChecked = true;
                    EsportsModeStartupLogger.Log("UI_UPDATE", $"同步UI: {opId} 已应用，设置Toggle为开启");
                }
            }
            else if (isSelected)
            {
                newState = RowStateIndicator.Pending;
                pendingCount++;
            }
            else
            {
                newState = RowStateIndicator.None;
                noneCount++;
            }
            
            if (oldState != newState)
            {
                row.StateIndicator = newState;
                EsportsModeStartupLogger.Log("UI_UPDATE", $"状态变更: {opId} [{oldState} → {newState}]");
            }
        }
        
        EsportsModeStartupLogger.Log("UI_UPDATE", $"状态统计: Applied={appliedCount}, Pending={pendingCount}, None={noneCount}");
    }

    private void UpdateCounts()
    {
        var totalCount = _allRows.Count;
        var appliedCount = _appliedItems.Count;
        var selectedCount = _selectedItems.Count;
        var compatibleCount = _allRows.Values.Count(r => r.IsCompatible);
        
        // 左上角状态
        ContextSelectedCount.Text = selectedCount.ToString();
        ContextActiveCount.Text = appliedCount.ToString();
        
        // 右侧面板
        TotalOptCount.Text = totalCount.ToString();
        CompatibleCount.Text = compatibleCount.ToString();
        
        // 底部状态
        SelectedCountText.Text = selectedCount.ToString();
        ActiveCountText.Text = appliedCount.ToString();
        
        // 当前策略
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        CurrentStrategyText.Text = _selectedStrategy.HasValue
            ? GetPresetDisplayName(_selectedStrategy.Value)
            : loc.Get("Esports.NoStrategy");
    }

    private void UpdateStatusText()
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        if (_lastDetectionReport == null)
        {
            StatusText.Text = "状态未检测";
            return;
        }

        if (!_lastDetectionReport.IsReadyForPlanning)
        {
            var unknownCount = _lastDetectionReport.Items.Count(static item =>
                item.Outcome is not EsportsProbeOutcome.Detected and not EsportsProbeOutcome.MissingOperation);
            StatusText.Text = $"检测不完整：{unknownCount} 项状态未知";
            return;
        }

        StatusText.Text = loc.Get("Esports.StatusApplied", _appliedItems.Count);
    }

    private void UpdateContextRail(GameModeOptimizationItem item)
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        
        ContextTitle.Text = item.Title;
        ContextDesc.Text = string.IsNullOrEmpty(item.Description) 
            ? loc.Get("Esports.NoDescription") 
            : item.Description;
        
        // 风险等级
        if (item.RiskLevel == OperationRiskLevel.Extreme)
        {
            ContextRiskText.Text = loc.Get("Esports.RiskExtreme");
            ContextRiskBg.Color = Color.FromArgb(0x25, 0xF4, 0x3F, 0x5E);
            ContextRiskText.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x3F, 0x5E));
        }
        else if (item.RiskLevel == OperationRiskLevel.Balanced)
        {
            ContextRiskText.Text = loc.Get("Esports.RiskBalanced");
            ContextRiskBg.Color = Color.FromArgb(0x25, 0xF5, 0x9E, 0x0B);
            ContextRiskText.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else
        {
            ContextRiskText.Text = loc.Get("Esports.RiskSafe");
            ContextRiskBg.Color = Color.FromArgb(0x20, 0x4A, 0xDE, 0x80);
            ContextRiskText.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
        }
        
        // 预期收益
        var benefit = GetBenefitText(item);
        if (!string.IsNullOrEmpty(benefit))
        {
            ContextBenefitSection.Visibility = Visibility.Visible;
            ContextBenefitText.Text = benefit;
        }
        else
        {
            ContextBenefitSection.Visibility = Visibility.Collapsed;
        }
        
        // 兼容性警告
        var warning = GetCompatibilityWarning(item);
        if (!string.IsNullOrEmpty(warning))
        {
            CompatWarningSection.Visibility = Visibility.Visible;
            CompatWarningText.Text = warning;
        }
        else
        {
            CompatWarningSection.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePanelBorderColor(OperationRiskLevel risk)
    {
        var color = risk switch
        {
            OperationRiskLevel.Safe => Color.FromArgb(0x25, 0x4A, 0xDE, 0x80),
            OperationRiskLevel.Balanced => Color.FromArgb(0x25, 0xF5, 0x9E, 0x0B),
            OperationRiskLevel.Extreme => Color.FromArgb(0x30, 0xF4, 0x3F, 0x5E),
            _ => Color.FromArgb(0x15, 0x63, 0x66, 0xF1)
        };
        
        // 不需要更新，XAML 中已绑定
    }

    private string GetBenefitText(GameModeOptimizationItem item)
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        var id = item.OperationId.ToLowerInvariant();
        
        if (id.Contains("nagle")) return loc.Get("Esports.Benefit.Latency2ms");
        if (id.Contains("hags") || id.Contains("gpu_schedule")) return loc.Get("Esports.Benefit.FpsStable");
        if (id.Contains("game") && id.Contains("mode")) return loc.Get("Esports.Benefit.PerfPriority");
        if (id.Contains("mouse")) return loc.Get("Esports.Benefit.ResponseUp");
        if (id.Contains("timer")) return loc.Get("Esports.Benefit.PrecisionUp");
        if (id.Contains("fullscreen")) return loc.Get("Esports.Benefit.LowLatency");
        if (id.Contains("reflex")) return loc.Get("Esports.Benefit.UltraResponse");
        
        return item.ExpectedEffect ?? "";
    }

    private string GetCompatibilityWarning(GameModeOptimizationItem item)
    {
        if (_hardwareInfo == null) return "";
        
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        var id = item.OperationId.ToLowerInvariant();
        
        if (!item.IsCompatible)
        {
            if (id.Contains("intel") && _hardwareInfo.CpuVendor != CpuVendor.Intel)
                return loc.Get("Esports.Compat.IntelOnAmd");
            if (id.Contains("amd") && _hardwareInfo.CpuVendor != CpuVendor.AMD)
                return loc.Get("Esports.Compat.AmdOnIntel");
            if (id.Contains("nvidia") && !_hardwareInfo.HasNvidiaGpu)
                return loc.Get("Esports.Compat.NvidiaNotFound");
            if (id.Contains("radeon") && !_hardwareInfo.HasAmdGpu)
                return loc.Get("Esports.Compat.RadeonNotFound");
            
            return loc.Get("Esports.Compat.HardwareIncompatible");
        }
        
        if (item.RiskLevel == OperationRiskLevel.Extreme)
            return loc.Get("Esports.Compat.ExtremeRiskWarning");
        
        return "";
    }

    #endregion

    #region 本地化

    private void UpdateLocalizedTexts()
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        
        // 页面标题
        PageTitle.Text = loc.Get("Esports.Title");
        
        // 策略按钮
        SafeBtnText.Text = GetPresetDisplayName(StrategyType.Safe);
        BalancedBtnText.Text = GetPresetDisplayName(StrategyType.Balanced);
        ExtremeBtnText.Text = GetPresetDisplayName(StrategyType.Extreme);
        SafeBtn.ToolTip = IsEnglishUi()
            ? "Recommend only Windows Game Mode after checks on this PC."
            : "仅推荐经当前机器检测可用的 Windows 游戏模式";
        BalancedBtn.ToolTip = IsEnglishUi()
            ? "Add only reversible Windows gaming settings that pass preflight checks."
            : "仅增加经预检可用且可恢复的 Windows 游戏设置";
        ExtremeBtn.ToolTip = IsEnglishUi()
            ? "Keep the current selection and configure items manually."
            : "保留当前选择，由用户逐项配置";
        
        // 分类标签
        FpsCategoryLabel.Text = loc.Get("Esports.FpsOptimization");
        StorageCategoryLabel.Text = loc.Get("Esports.StorageOptimization");
        WindowsCategoryLabel.Text = loc.Get("Esports.WindowsOptimization");
        InputCategoryLabel.Text = loc.Get("Esports.InputDevices");
        NetworkCategoryLabel.Text = loc.Get("Esports.TcpIpOptimization");
        SecurityCategoryLabel.Text = loc.Get("Esports.SecurityAdjustment");
        
        // 底部按钮
        RestoreUserBtnText.Text = loc.Get("Module.RestoreUser");
        RestoreWinBtnText.Text = loc.Get("Module.RestoreWindows");
        ApplyBtnText.Text = loc.Get("Esports.ApplySelected");
        
        // 刷新优化项文本
        _gameModeViewModel?.RefreshAllLocalizedTexts();
        
        // 更新动态文本
        UpdateStatusText();
        UpdateCounts();
    }

    #endregion

    #region 配置管理

    private void LoadSavedConfiguration()
    {
        try
        {
            var settings = AppSettings.Load();
            var config = settings.EsportsModeSettings;
            
            if (config != null)
            {
                var isLegacyPresetPolicy = config.PresetPolicyVersion < CurrentPresetPolicyVersion;

                // 加载选中的策略
                if (!string.IsNullOrEmpty(config.SelectedStrategy))
                {
                    if (Enum.TryParse<StrategyType>(config.SelectedStrategy, out var strategy))
                    {
                        _selectedStrategy = strategy;
                        EsportsModeStartupLogger.Log("CONFIG", $"已加载策略: {strategy}");
                    }
                }

                // 旧版 Extreme 会静态全选全部操作，不能把这批选择直接迁移成“自定义”。
                _legacyExtremeSelectionCleared = isLegacyPresetPolicy && _selectedStrategy == StrategyType.Extreme;
                
                // 加载勾选状态（EnabledItems）
                if (!_legacyExtremeSelectionCleared && config.EnabledItems != null && config.EnabledItems.Count > 0)
                {
                    _selectedItems.Clear();
                    _isApplyingPreset = true;
                    try
                    {
                        foreach (var (opId, isEnabled) in config.EnabledItems)
                        {
                            if (isEnabled && _allRows.ContainsKey(opId))
                            {
                                _selectedItems.Add(opId);
                                _allRows[opId].IsChecked = true;
                            }
                        }
                    }
                    finally
                    {
                        _isApplyingPreset = false;
                    }
                    EsportsModeStartupLogger.Log("CONFIG", $"已加载 {_selectedItems.Count} 个勾选项");
                }
                else if (_legacyExtremeSelectionCleared)
                {
                    _selectedItems.Clear();
                    EsportsModeStartupLogger.Log("CONFIG", "旧版狂暴模式选择已按失败关闭策略清空");
                }
            }
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("CONFIG", "加载配置失败", ex);
        }
    }

    private void SaveConfiguration()
    {
        try
        {
            var settings = AppSettings.Load();
            settings.EsportsModeSettings = new EsportsModeConfig
            {
                PresetPolicyVersion = CurrentPresetPolicyVersion,
                SelectedStrategy = _selectedStrategy?.ToString(),
                EnabledItems = _allRows.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.IsChecked
                ),
                // AppliedItems 是旧版派生缓存，保留 schema 兼容但不再写入系统真值。
                AppliedItems = new List<string>()
            };
            settings.Save();
            EsportsModeStartupLogger.Log("CONFIG", $"已保存配置：勾选 {_selectedItems.Count} 项，已应用 {_appliedItems.Count} 项");
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("CONFIG", "保存配置失败", ex);
        }
    }

    #endregion

    #region 导航和UI交互事件

    private void StrategyInfo_Click(object sender, MouseButtonEventArgs e)
    {
        InfoDialog.Show(
            Window.GetWindow(this),
            IsEnglishUi() ? "Dynamic recommendations" : "动态推荐说明",
            IsEnglishUi()
                ? "Conservative recommendation selects only Windows Game Mode. Performance recommendation may also disable background recording. Both require compatibility, registration and permission checks. Network, power, driver, timer, security and storage changes are never added automatically. Custom keeps your current selection."
                : "“稳妥推荐”只选择 Windows 游戏模式；“性能推荐”最多再加入关闭后台录制。两者都必须通过兼容性、操作注册和权限预检。网络、电源、驱动、计时器、安全防护和存储改动绝不会被快捷方案自动加入。“自定义”会保留当前选择。",
            IconGlyphs.Info);
    }

    private void NavCore_Click(object sender, MouseButtonEventArgs e)
    {
        ScrollToCategory("CoreSection");
        UpdateNavButtonStates("Core");
    }

    private void NavCpu_Click(object sender, MouseButtonEventArgs e)
    {
        ScrollToCategory("CpuSection");
        UpdateNavButtonStates("Cpu");
    }

    private void NavGpu_Click(object sender, MouseButtonEventArgs e)
    {
        ScrollToCategory("GpuSection");
        UpdateNavButtonStates("Gpu");
    }

    private void NavInput_Click(object sender, MouseButtonEventArgs e)
    {
        ScrollToCategory("InputSection");
        UpdateNavButtonStates("Input");
    }

    private void NavNetwork_Click(object sender, MouseButtonEventArgs e)
    {
        ScrollToCategory("NetworkSection");
        UpdateNavButtonStates("Network");
    }

    private void ScrollToCategory(string sectionName)
    {
        try
        {
            EsportsModeStartupLogger.Log("NAV", $"尝试滚动到: {sectionName}");
            
            // 查找主滚动视图
            var scrollViewer = FindVisualChild<ScrollViewer>(this);
            if (scrollViewer == null)
            {
                EsportsModeStartupLogger.LogWarning("NAV", "未找到 ScrollViewer");
                return;
            }

            // 查找目标分类
            var targetElement = FindName(sectionName) as FrameworkElement;
            if (targetElement == null)
            {
                EsportsModeStartupLogger.LogWarning("NAV", $"未找到锚点: {sectionName}");
                return;
            }

            // 确保元素已加载
            if (!targetElement.IsLoaded)
            {
                EsportsModeStartupLogger.Log("NAV", "元素未加载，等待加载完成");
                targetElement.Loaded += (s, e) => ScrollToElement(scrollViewer, targetElement, sectionName);
                return;
            }

            ScrollToElement(scrollViewer, targetElement, sectionName);
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("NAV", "导航失败", ex);
        }
    }

    private void ScrollToElement(ScrollViewer scrollViewer, FrameworkElement targetElement, string sectionName)
    {
        try
        {
            // 获取目标元素相对于滚动视图的位置
            var transform = targetElement.TransformToAncestor(scrollViewer);
            var position = transform.Transform(new Point(0, 0));
            
            // 滚动到目标位置（减去20像素的偏移）
            scrollViewer.ScrollToVerticalOffset(position.Y - 20);
            
            EsportsModeStartupLogger.Log("NAV", $"✓ 滚动成功: {sectionName}, Y={position.Y}");
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("NAV", $"滚动失败: {sectionName}", ex);
        }
    }

    private void UpdateNavButtonStates(string activeNav)
    {
        var activeColor = (Color)FindResource("Theme.Color.EsportsNavSelected");
        var activeForeground = (Brush)FindResource("Theme.Brush.TextPrimary");
        var inactiveForeground = (Brush)FindResource("Theme.Brush.TextMuted");

        // Core 按钮（特殊处理，因为它有命名的 Background）
        NavCoreBtn.Background = activeNav == "Core" ? new SolidColorBrush(activeColor) : new SolidColorBrush(Colors.Transparent);
        NavCore.Foreground = activeNav == "Core" ? activeForeground : inactiveForeground;
        
        // 其他按钮
        NavCpuBtn.Background = activeNav == "Cpu" ? new SolidColorBrush(activeColor) : new SolidColorBrush(Colors.Transparent);
        NavCpu.Foreground = activeNav == "Cpu" ? activeForeground : inactiveForeground;
        
        NavGpuBtn.Background = activeNav == "Gpu" ? new SolidColorBrush(activeColor) : new SolidColorBrush(Colors.Transparent);
        NavGpu.Foreground = activeNav == "Gpu" ? activeForeground : inactiveForeground;
        
        NavInputBtn.Background = activeNav == "Input" ? new SolidColorBrush(activeColor) : new SolidColorBrush(Colors.Transparent);
        NavInput.Foreground = activeNav == "Input" ? activeForeground : inactiveForeground;
        
        NavNetworkBtn.Background = activeNav == "Network" ? new SolidColorBrush(activeColor) : new SolidColorBrush(Colors.Transparent);
        NavNetwork.Foreground = activeNav == "Network" ? activeForeground : inactiveForeground;
    }

    private void RestoreBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0x63, 0x66, 0xF1));
        }
    }

    private void RestoreBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Colors.Transparent);
        }
    }

    private void RestoreWinBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xF4, 0x3F, 0x5E));
        }
    }

    private void RestoreWinBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Colors.Transparent);
        }
    }

    private void ApplyBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border && border.Background is LinearGradientBrush brush)
        {
            border.Opacity = 0.9;
        }
    }

    private void ApplyBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 1.0;
        }
    }

    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    #endregion

    #region 守护服务管理

    private async Task RestoreGuardServiceStateAsync(CancellationToken cancellationToken)
    {
        if (_guardService == null) return;
        
        try
        {
            var settings = AppSettings.Load();
            if (settings.OptimizationGuardEnabled)
            {
                // 添加已应用的项到保护列表
                if (_appliedItems.Count > 0)
                {
                    await _guardService
                        .AddProtectedOperationsAsync(_appliedItems.ToList())
                        .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                    EsportsModeStartupLogger.Log("GUARD", $"已添加 {_appliedItems.Count} 项到守护保护列表");
                }
                
                // 启用守护服务
                _guardService.Enable();
                EsportsModeStartupLogger.Log("GUARD", "守护服务已启用");
            }
            
            // 订阅守护服务事件
            _guardService.OperationsReverted += OnGuardOperationsReverted;
            _guardService.OperationsReapplied += OnGuardOperationsReapplied;
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("GUARD", "恢复守护服务状态失败", ex);
        }
    }
    
    private void OnGuardOperationsReverted(object? sender, GuardServiceEventArgs e)
    {
        if (e.RevertedOperations.Count == 0) return;
        
        Dispatcher.Invoke(() =>
        {
            var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
            var message = string.Format(
                loc.Get("Guard.Toast.Reverted.Message") ?? "检测到 {0} 项优化被系统恢复",
                e.RevertedOperations.Count);
            
            ShowGuardToast(
                loc.Get("Guard.Toast.Reverted.Title") ?? "优化被恢复",
                message,
                isWarning: true);
        });
    }
    
    private void OnGuardOperationsReapplied(object? sender, GuardServiceEventArgs e)
    {
        if (e.ReappliedOperations.Count == 0 && e.FailedOperations.Count == 0) return;
        
        Dispatcher.Invoke(() =>
        {
            var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
            
            if (e.ReappliedOperations.Count > 0)
            {
                var message = string.Format(
                    loc.Get("Guard.Toast.Reapplied.Message") ?? "已重新应用 {0} 项优化",
                    e.ReappliedOperations.Count);
                
                ShowGuardToast(
                    loc.Get("Guard.Toast.Reapplied.Title") ?? "自动恢复成功",
                    message);
            }
            
            if (e.FailedOperations.Count > 0)
            {
                var message = string.Format(
                    loc.Get("Guard.Toast.Failed.Message") ?? "{0} 项优化无法自动恢复，请手动检查",
                    e.FailedOperations.Count);
                
                ShowGuardToast(
                    loc.Get("Guard.Toast.Failed.Title") ?? "部分恢复失败",
                    message,
                    isWarning: true);
            }
        });
    }
    
    private void ShowGuardToast(string title, string message, bool isWarning = false)
    {
        // 通知节流：5分钟内同类型最多1次
        var notificationType = isWarning ? "warning" : "success";
        if (_lastNotificationTime.TryGetValue(notificationType, out var lastTime))
        {
            if ((DateTime.Now - lastTime).TotalSeconds < NotificationThrottleSeconds)
            {
                EsportsModeStartupLogger.Log("GUARD", $"通知被节流：{notificationType}");
                return;
            }
        }
        
        _lastNotificationTime[notificationType] = DateTime.Now;
        
        // 检查用户设置
        var settings = AppSettings.Load();
        if (!settings.GuardShowNotifications)
        {
            EsportsModeStartupLogger.Log("GUARD", "用户已禁用通知");
            return;
        }
        
        // 显示 Toast
        var toast = new ToastNotification
        {
            Title = title,
            Message = message,
            ToastType = isWarning ? ToastType.Warning : ToastType.Success,
            AutoHideDuration = 5000
        };
        
        // 将 Toast 添加到主窗口
        var mainWindow = Window.GetWindow(this);
        if (mainWindow?.Content is Grid mainGrid)
        {
            mainGrid.Children.Add(toast);
            Panel.SetZIndex(toast, 9999);
            toast.VerticalAlignment = VerticalAlignment.Top;
            toast.HorizontalAlignment = HorizontalAlignment.Right;
            toast.Margin = new Thickness(0, 60, 20, 0);
            
            toast.Closed += (s, e) => mainGrid.Children.Remove(toast);
            toast.Show();
        }
    }
    
    private void OnGuardToggleService(object? sender, EventArgs e)
    {
        if (_guardService == null) return;
        
        var settings = AppSettings.Load();
        
        if (_guardService.IsEnabled)
        {
            // 禁用守护服务
            _guardService.Disable();
            settings.OptimizationGuardEnabled = false;
            settings.Save();
            
            GuardPanel?.UpdateUI();
            
            var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
            ShowGuardToast(
                loc.Get("Guard.Toast.Disabled.Title") ?? "守护服务已禁用",
                loc.Get("Guard.Toast.Disabled.Message") ?? "优化项将不再受到保护");
        }
        else
        {
            // 启用守护服务
            _guardService.Enable();
            settings.OptimizationGuardEnabled = true;
            settings.Save();
            
            GuardPanel?.UpdateUI();
            
            var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
            ShowGuardToast(
                loc.Get("Guard.Toast.Enabled.Title") ?? "守护服务已启用",
                loc.Get("Guard.Toast.Enabled.Message") ?? "将自动监控并恢复优化项");
        }
    }
    
    private async void OnGuardSettings(object? sender, EventArgs e)
    {
        if (_guardService == null) return;

        try
        {
        var settings = AppSettings.Load();
        var result = GuardServiceSettingsDialog.Show(Window.GetWindow(this), _guardService, settings);
        
        if (result == true)
        {
            var dialog = Window.GetWindow(this)?.OwnedWindows.OfType<GuardServiceSettingsDialog>().FirstOrDefault();
            if (dialog != null)
            {
                // 保存设置
                settings.GuardMonitorIntervalSeconds = dialog.MonitorIntervalSeconds;
                settings.GuardShowNotifications = dialog.ShowNotifications;
                settings.GuardProtectedOperations = dialog.ProtectedOperations;
                settings.Save();
                
                // 应用设置
                _guardService.SetMonitorInterval(dialog.MonitorIntervalSeconds);
                
                // 更新保护列表
                var currentProtected = await _guardService
                    .GetProtectedOperationsAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15));
                var toRemove = currentProtected.Except(dialog.ProtectedOperations).ToList();
                if (toRemove.Count > 0)
                {
                    await _guardService
                        .RemoveProtectedOperationsAsync(toRemove)
                        .WaitAsync(TimeSpan.FromSeconds(15));
                }
                
                GuardPanel?.UpdateUI();
                
                var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
                ShowGuardToast(
                    loc.Get("Guard.Toast.SettingsSaved.Title") ?? "设置已保存",
                    loc.Get("Guard.Toast.SettingsSaved.Message") ?? "守护服务设置已更新");
            }
        }
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("GUARD", "保存守护设置失败", ex);
            ShowWarning("守护设置未保存", ex.Message);
        }
    }
    
    private async void OnGuardViewList(object? sender, EventArgs e)
    {
        if (_guardService == null) return;

        try
        {
        var protectedOps = await _guardService
            .GetProtectedOperationsAsync()
            .WaitAsync(TimeSpan.FromSeconds(15));
        var allItems = GetAllOptimizationItems();
        
        var items = protectedOps.Select(opId =>
        {
            var item = allItems.FirstOrDefault(i => i.OperationId == opId);
            return item?.Title ?? opId;
        }).ToList();
        
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        var message = items.Count > 0
            ? string.Join("\n", items.Select((t, i) => $"{i + 1}. {t}"))
            : (loc.Get("Guard.ViewList.Empty") ?? "当前没有保护项");
        
        InfoDialog.Show(
            Window.GetWindow(this),
            loc.Get("Guard.ViewList.Title") ?? "保护列表",
            message,
            IconGlyphs.Shield);
        }
        catch (Exception ex)
        {
            EsportsModeStartupLogger.LogError("GUARD", "读取守护列表失败", ex);
            ShowWarning("无法读取保护列表", ex.Message);
        }
    }
    
    private async void OnGuardCheckNow(object? sender, EventArgs e)
    {
        if (_guardService == null || !_guardService.IsEnabled) return;
        
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        ShowGuardToast(
            loc.Get("Guard.Toast.Checking.Title") ?? "正在检查",
            loc.Get("Guard.Toast.Checking.Message") ?? "正在检查优化项状态...");
        
        // 触发立即检查（通过暂停再恢复来强制检查）
        _guardService.Pause();
        await Task.Delay(100);
        _guardService.Resume();
        
        GuardPanel?.UpdateUI();
    }

    #endregion

    #region 辅助方法

    private static (
        List<ExecutionResultItem> SuccessItems,
        List<ExecutionResultItem> FailedItems,
        List<ExecutionResultItem> SkippedItems) BuildResultItems(
            EsportsWorkflowReport report,
            IReadOnlyList<GameModeOptimizationItem> allItems)
    {
        var itemIndex = allItems
            .GroupBy(static item => item.OperationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var successItems = new List<ExecutionResultItem>();
        var failedItems = new List<ExecutionResultItem>();
        var skippedItems = new List<ExecutionResultItem>();

        foreach (var result in report.Results)
        {
            itemIndex.TryGetValue(result.OperationId, out var sourceItem);
            var uiResult = new ExecutionResultItem
            {
                Title = sourceItem?.Title ?? result.Title,
                ModuleName = sourceItem?.Category ?? result.ModuleKey,
                OperationId = result.OperationId,
                IsSuccess = result.Success,
                ErrorMessage = result.Success ? null : result.Message
            };

            if (result.Success)
            {
                successItems.Add(uiResult);
            }
            else if (result.Outcome is EsportsExecutionOutcome.Blocked or EsportsExecutionOutcome.Cancelled)
            {
                uiResult.IsSkipped = true;
                uiResult.SkipReason = result.Message;
                skippedItems.Add(uiResult);
            }
            else
            {
                failedItems.Add(uiResult);
            }
        }

        return (successItems, failedItems, skippedItems);
    }

    private static ApplyConfirmInfo BuildApplyConfirmInfo(
        IEnumerable<string> pendingOperationIds,
        IEnumerable<GameModeOptimizationItem> allItems,
        string strategyName)
    {
        var pendingList = pendingOperationIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var itemIndex = allItems
            .GroupBy(static item => item.OperationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var confirmInfo = new ApplyConfirmInfo
        {
            TotalCount = pendingList.Count,
            StrategyName = strategyName,
            PendingOperationIds = pendingList
        };

        foreach (var operationId in pendingList)
        {
            if (!itemIndex.TryGetValue(operationId, out var item))
            {
                continue;
            }

            switch (item.RiskLevel)
            {
                case OperationRiskLevel.Safe:
                    confirmInfo.SafeCount++;
                    break;
                case OperationRiskLevel.Balanced:
                    confirmInfo.BalancedCount++;
                    break;
                case OperationRiskLevel.Extreme:
                    confirmInfo.ExtremeCount++;
                    break;
            }

            confirmInfo.CategoryCounts[item.Category] =
                confirmInfo.CategoryCounts.GetValueOrDefault(item.Category) + 1;
            confirmInfo.DetailedList.Add(new PendingOperationItem
            {
                OperationId = item.OperationId,
                Title = item.Title,
                Category = item.Category,
                RiskLevel = item.RiskLevel,
                IsCompatible = item.IsCompatible
            });
        }

        return confirmInfo;
    }

    private void ShowPlanBlocked(EsportsOperationPlan plan)
    {
        var details = plan.RejectedItems.Count == 0
            ? "计划为空或没有可执行项。"
            : string.Join("\n", plan.RejectedItems.Take(5).Select(result => $"• {result.Title}: {result.Message}"));
        ShowWarning("计划已阻断", details);
    }

    private List<GameModeOptimizationItem> GetAllOptimizationItems()
    {
        if (_gameModeViewModel == null) return new List<GameModeOptimizationItem>();
        
        var allItems = new List<GameModeOptimizationItem>();
        allItems.AddRange(_gameModeViewModel.FpsItems);
        allItems.AddRange(_gameModeViewModel.InputItems);
        allItems.AddRange(_gameModeViewModel.NetworkItems);
        allItems.AddRange(_gameModeViewModel.StorageItems);
        allItems.AddRange(_gameModeViewModel.WindowsItems);
        allItems.AddRange(_gameModeViewModel.SecurityItems);
        allItems.AddRange(_gameModeViewModel.CpuVendorItems);
        
        return allItems;
    }

    private string GetCurrentStrategyName()
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        return _selectedStrategy.HasValue
            ? GetPresetDisplayName(_selectedStrategy.Value)
            : loc.Get("Esports.NoStrategy") ?? "未选择策略";
    }

    private static bool IsEnglishUi()
    {
        return GameOptimizer.Core.Localization.LocalizationService.Instance.CurrentLanguage
            .StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPresetDisplayName(StrategyType strategy)
    {
        var english = IsEnglishUi();
        return strategy switch
        {
            StrategyType.Safe => english ? "Conservative recommendation" : "稳妥推荐",
            StrategyType.Balanced => english ? "Performance recommendation" : "性能推荐",
            StrategyType.Extreme => english ? "Custom" : "自定义",
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null)
        };
    }

    private static string GetPresetRecommendationStatus(EsportsPresetRecommendation recommendation)
    {
        return IsEnglishUi()
            ? $"Dynamic recommendation: {recommendation.SelectedOperationIds.Count} selected, {recommendation.BlockedCount} blocked by compatibility, registration or permission checks. Network, power and driver changes are excluded."
            : $"动态推荐：已选择 {recommendation.SelectedOperationIds.Count} 项，{recommendation.BlockedCount} 项未通过兼容、注册或权限预检；不包含网络、电源和驱动改动。";
    }

    private static string GetCustomPresetStatus()
    {
        return IsEnglishUi()
            ? "Custom selection enabled. Quick presets will not add network, power or driver changes."
            : "已切换到自定义并保留当前选择；快捷方案不会自动加入网络、电源或驱动改动。";
    }

    private GameModeOptimizationItem? GetItemByOperationId(string opId)
    {
        if (_gameModeViewModel == null) return null;
        
        return _gameModeViewModel.FpsItems.FirstOrDefault(i => i.OperationId == opId)
            ?? _gameModeViewModel.InputItems.FirstOrDefault(i => i.OperationId == opId)
            ?? _gameModeViewModel.NetworkItems.FirstOrDefault(i => i.OperationId == opId)
            ?? _gameModeViewModel.StorageItems.FirstOrDefault(i => i.OperationId == opId)
            ?? _gameModeViewModel.WindowsItems.FirstOrDefault(i => i.OperationId == opId)
            ?? _gameModeViewModel.SecurityItems.FirstOrDefault(i => i.OperationId == opId)
            ?? _gameModeViewModel.CpuVendorItems.FirstOrDefault(i => i.OperationId == opId);
    }

    private void ShowRegistryInfo(Models.OptimizationToggleModel toggle)
    {
        var path = toggle.RegistryPath ?? string.Empty;
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
            null,
            toggle.OptimizedValue ?? toggle.ValuePreview,
            toggle.Title,
            toggle.Description,
            valueName,
            toggle.DefaultValue,
            toggle.ValueType);
        
        dialog.Owner = Window.GetWindow(this);
        dialog.ShowDialog();
    }

    private void ShowOperationInfo(Models.OptimizationToggleModel toggle)
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
    }

    private void ShowError(string title, string message)
    {
        InfoDialog.Show(Window.GetWindow(this), title, message, IconGlyphs.Error);
    }

    private void ShowWarning(string title, string message)
    {
        InfoDialog.Show(Window.GetWindow(this), title, message, IconGlyphs.Warning);
    }

    private void ShowSuccess(string title, string message)
    {
        InfoDialog.Show(Window.GetWindow(this), title, message, IconGlyphs.Success);
    }

    private void ShowInfo(string title, string message)
    {
        InfoDialog.Show(Window.GetWindow(this), title, message, IconGlyphs.Info);
    }

    #endregion
}

public enum StrategyType
{
    Safe,
    Balanced,
    Extreme
}
