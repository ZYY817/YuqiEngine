// File: GameOptimizer.Shell/Views/RevertResultDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Views;

public partial class RevertResultDialog : Window
{
    private List<RevertResultGroup> _allGroups = new();
    private RestoreOperationType _operationType;

    public RevertResultDialog()
    {
        InitializeComponent();
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() => UpdateLocalizedTexts());
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        TitleText.Text = _operationType == RestoreOperationType.RevertOptimizations
            ? loc.Get("RevertResult.Title")
            : loc.Get("RestoreResult.Title");
        
        SearchPlaceholder.Text = loc.Get("RestoreDetail.SearchPlaceholder");
        EmptyStateText.Text = loc.Get("RestoreDetail.EmptyState");
        
        if (CloseBtn.Template.FindName("CloseBtnText", CloseBtn) is TextBlock closeBtnText)
        {
            closeBtnText.Text = loc.Get("Common.Close");
        }
        
        if (_allGroups.Count > 0)
        {
            var totalSuccess = _allGroups.Sum(g => g.SuccessCount);
            var totalFailed = _allGroups.Sum(g => g.FailedCount);
            
            if (totalFailed > 0)
            {
                SubtitleText.Text = _operationType == RestoreOperationType.RevertOptimizations
                    ? loc.Get("RevertResult.Summary", totalSuccess, totalFailed)
                    : loc.Get("RestoreResult.Summary", totalSuccess, totalFailed);
            }
            else
            {
                SubtitleText.Text = _operationType == RestoreOperationType.RevertOptimizations
                    ? loc.Get("RevertResult.SuccessOnly", totalSuccess)
                    : loc.Get("RestoreResult.SuccessOnly", totalSuccess);
            }
        }
    }

    public void SetResults(List<RevertResultGroup> groups, RestoreOperationType operationType)
    {
        _allGroups = groups;
        _operationType = operationType;
        
        var totalSuccess = groups.Sum(g => g.SuccessCount);
        var totalFailed = groups.Sum(g => g.FailedCount);
        
        var loc = LocalizationService.Instance;
        if (totalFailed > 0)
        {
            SubtitleText.Text = operationType == RestoreOperationType.RevertOptimizations
                ? loc.Get("RevertResult.Summary", totalSuccess, totalFailed)
                : loc.Get("RestoreResult.Summary", totalSuccess, totalFailed);
        }
        else
        {
            SubtitleText.Text = operationType == RestoreOperationType.RevertOptimizations
                ? loc.Get("RevertResult.SuccessOnly", totalSuccess)
                : loc.Get("RestoreResult.SuccessOnly", totalSuccess);
        }
        
        GroupsList.ItemsSource = _allGroups;
        UpdateVisibility();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchBox.Text.Trim().ToLower();
        
        if (string.IsNullOrEmpty(searchText))
        {
            foreach (var group in _allGroups)
            {
                group.IsVisible = true;
                foreach (var item in group.Items)
                {
                    item.IsVisible = true;
                }
            }
        }
        else
        {
            foreach (var group in _allGroups)
            {
                var hasVisibleItems = false;
                foreach (var item in group.Items)
                {
                    item.IsVisible = item.Title.ToLower().Contains(searchText);
                    if (item.IsVisible) hasVisibleItems = true;
                }
                group.IsVisible = hasVisibleItems;
            }
        }
        
        UpdateVisibility();
        GroupsList.Items.Refresh();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
    }

    private void UpdateVisibility()
    {
        var hasVisibleGroups = _allGroups.Any(g => g.IsVisible);
        EmptyState.Visibility = hasVisibleGroups ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            Close_Click(sender, e);
        }
    }

    public static void Show(Window owner, List<RevertResultGroup> groups, RestoreOperationType operationType)
    {
        // Why: Owner 窗口可能失焦（如 ProgressDialog 刚关闭），先拉回前台防止 ShowDialog 弹到后面锁死 UI
        owner.Activate();
        var dialog = new RevertResultDialog
        {
            Owner = owner
        };
        dialog.SetResults(groups, operationType);
        dialog.ShowDialog();
    }
}
