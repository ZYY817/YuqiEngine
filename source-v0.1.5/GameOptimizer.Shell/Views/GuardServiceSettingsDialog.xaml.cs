// File: GameOptimizer.Shell/Views/GuardServiceSettingsDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Views;

public partial class GuardServiceSettingsDialog : Window
{
    private readonly OptimizationGuardService _guardService;
    private List<string> _protectedOperations;

    public int MonitorIntervalSeconds { get; private set; }
    public bool ShowNotifications { get; private set; }
    public List<string> ProtectedOperations => _protectedOperations;

    public GuardServiceSettingsDialog(OptimizationGuardService guardService, AppSettings settings)
    {
        InitializeComponent();
        
        _guardService = guardService;
        _protectedOperations = new List<string>(settings.GuardProtectedOperations);
        
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        
        Title = loc.Get("Guard.Settings.Title") ?? "守护服务设置";
        TitleText.Text = loc.Get("Guard.Settings.Title") ?? "守护服务设置";
        
        IntervalLabel.Text = loc.Get("Guard.Settings.Interval") ?? "监控间隔";
        IntervalHint.Text = loc.Get("Guard.Settings.IntervalHint") ?? "建议：30-60秒可平衡性能与保护效果";
        
        NotificationLabel.Text = loc.Get("Guard.Settings.Notification") ?? "显示通知";
        NotificationDesc.Text = loc.Get("Guard.Settings.NotificationDesc") ?? "检测到优化被恢复时显示桌面通知";
        
        ProtectedListLabel.Text = loc.Get("Guard.Settings.ProtectedList") ?? "保护列表";
        EmptyListHint.Text = loc.Get("Guard.Settings.EmptyList") ?? "当前没有保护项";
        
        CancelText.Text = loc.Get("Common.Cancel") ?? "取消";
        SaveText.Text = loc.Get("Common.Save") ?? "保存";
        
        // 加载当前设置
        IntervalSlider.Value = settings.GuardMonitorIntervalSeconds;
        ShowNotificationsCheckbox.IsChecked = settings.GuardShowNotifications;
        
        UpdateProtectedList();
    }

    private void UpdateProtectedList()
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        
        ProtectedItemsList.ItemsSource = _protectedOperations;
        ProtectedCountLabel.Text = $"{_protectedOperations.Count} " + (loc.Get("Common.Items") ?? "项");
        EmptyListHint.Visibility = _protectedOperations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var value = (int)IntervalSlider.Value;
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        IntervalValueText.Text = $"{value} " + (loc.Get("Common.Seconds") ?? "秒");
    }

    private void RemoveProtectedItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string opId) return;
        
        _protectedOperations.Remove(opId);
        UpdateProtectedList();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        MonitorIntervalSeconds = (int)IntervalSlider.Value;
        ShowNotifications = ShowNotificationsCheckbox.IsChecked == true;
        
        DialogResult = true;
        Close();
    }

    public static bool? Show(Window owner, OptimizationGuardService guardService, AppSettings settings)
    {
        var dialog = new GuardServiceSettingsDialog(guardService, settings)
        {
            Owner = owner
        };
        return dialog.ShowDialog();
    }
}
