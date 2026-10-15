// File: GameOptimizer.Shell/Controls/GuardServicePanel.xaml.cs
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Controls;

public partial class GuardServicePanel : UserControl
{
    private OptimizationGuardService? _guardService;
    private bool _isExpanded;
    private bool _isGuardEventsSubscribed;
    
    public event EventHandler? ToggleServiceRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ViewListRequested;
    public event EventHandler? CheckNowRequested;

    public GuardServicePanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void AttachGuardService(OptimizationGuardService guardService)
    {
        ArgumentNullException.ThrowIfNull(guardService);
        if (ReferenceEquals(_guardService, guardService))
            return;

        UnsubscribeGuardEvents();
        _guardService = guardService;
        if (IsLoaded)
            SubscribeGuardEvents();
        UpdateUI();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeGuardEvents();
        UpdateUI();
        UpdateLocalizedTexts();
        GameOptimizer.Core.Localization.LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        GameOptimizer.Core.Localization.LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeGuardEvents();
        GameOptimizer.Core.Localization.LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void SubscribeGuardEvents()
    {
        if (_guardService is null || _isGuardEventsSubscribed)
            return;

        _guardService.StatusChanged += OnGuardStatusChanged;
        _guardService.OperationsReverted += OnOperationsReverted;
        _guardService.OperationsReapplied += OnOperationsReapplied;
        _isGuardEventsSubscribed = true;
    }

    private void UnsubscribeGuardEvents()
    {
        if (_guardService is null || !_isGuardEventsSubscribed)
            return;

        _guardService.StatusChanged -= OnGuardStatusChanged;
        _guardService.OperationsReverted -= OnOperationsReverted;
        _guardService.OperationsReapplied -= OnOperationsReapplied;
        _isGuardEventsSubscribed = false;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        Dispatcher.Invoke(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        TitleText.Text = loc.Get("Guard.Title") ?? "优化守护服务";
        
        if (_guardService != null)
        {
            StatusText.Text = _guardService.IsEnabled 
                ? (_guardService.IsPaused ? (loc.Get("Guard.Status.Paused") ?? "已暂停") : (loc.Get("Guard.Status.Enabled") ?? "运行中"))
                : (loc.Get("Guard.Status.Disabled") ?? "已禁用");
        }
        
        ToggleBtnText.Text = _guardService?.IsEnabled == true 
            ? (loc.Get("Guard.Action.Disable") ?? "禁用守护")
            : (loc.Get("Guard.Action.Enable") ?? "启用守护");
    }

    public async void UpdateUI()
    {
        if (_guardService == null) return;
        
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        
        // 更新状态文本
        StatusText.Text = _guardService.IsEnabled 
            ? (_guardService.IsPaused ? (loc.Get("Guard.Status.Paused") ?? "已暂停") : (loc.Get("Guard.Status.Enabled") ?? "运行中"))
            : (loc.Get("Guard.Status.Disabled") ?? "已禁用");
        
        // 更新按钮文本
        ToggleBtnText.Text = _guardService.IsEnabled 
            ? (loc.Get("Guard.Action.Disable") ?? "禁用守护")
            : (loc.Get("Guard.Action.Enable") ?? "启用守护");
        
        // 更新保护项数量
        // Why: async void 的异常会直达 UI 线程崩溃进程，必须就地捕获。
        try
        {
            var protectedOps = await _guardService.GetProtectedOperationsAsync();
            ProtectedCountText.Text = protectedOps.Count.ToString();
        }
        catch (Exception ex)
        {
            Services.PageDiagnosticService.LogError("GuardServicePanel", "读取守护保护项失败", ex);
            ProtectedCountText.Text = "-";
        }
        
        // 更新最后检查时间
        if (_guardService.IsEnabled && _guardService.LastCheckTime != default)
        {
            var elapsed = DateTime.Now - _guardService.LastCheckTime;
            if (elapsed.TotalMinutes < 1)
                LastCheckText.Text = loc.Get("Guard.LastCheck.JustNow") ?? "刚刚";
            else if (elapsed.TotalMinutes < 60)
                LastCheckText.Text = $"{(int)elapsed.TotalMinutes} " + (loc.Get("Common.MinutesAgo") ?? "分钟前");
            else if (elapsed.TotalHours < 24)
                LastCheckText.Text = $"{(int)elapsed.TotalHours} " + (loc.Get("Common.HoursAgo") ?? "小时前");
            else
                LastCheckText.Text = $"{(int)elapsed.TotalDays} " + (loc.Get("Common.DaysAgo") ?? "天前");
        }
        else
        {
            LastCheckText.Text = loc.Get("Guard.LastCheck.Never") ?? "从未";
        }
        
        // 更新"立即检查"按钮状态
        CheckNowBtn.IsEnabled = _guardService.IsEnabled && !_guardService.IsPaused;
        CheckNowBtn.Opacity = CheckNowBtn.IsEnabled ? 1.0 : 0.5;
    }

    private void OnGuardStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(UpdateUI);
    }

    private void OnOperationsReverted(object? sender, GuardServiceEventArgs e)
    {
        // 事件由 EsportsModePage 处理（显示 Toast）
    }

    private void OnOperationsReapplied(object? sender, GuardServiceEventArgs e)
    {
        // 事件由 EsportsModePage 处理（显示 Toast）
    }

    private void ToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        ExpandedContent.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandIcon.Text = _isExpanded ? "\uE70E" : "\uE70D"; // ChevronUp : ChevronDown
    }

    private void ToggleService_Click(object sender, RoutedEventArgs e)
    {
        ToggleServiceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ViewList_Click(object sender, RoutedEventArgs e)
    {
        ViewListRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckNowRequested?.Invoke(this, EventArgs.Empty);
    }
}
