// File: GameOptimizer.Shell/Views/RestoreDetailDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Views;

public partial class RestoreDetailDialog : Window
{
    private readonly List<RestoreDetailGroup> _allGroups;
    private readonly RestoreOptionsDialog.RestoreOptionType _optionType;

    public RestoreDetailDialog(List<RestoreDetailGroup> groups, RestoreOptionsDialog.RestoreOptionType optionType)
    {
        InitializeComponent();
        _allGroups = groups;
        _optionType = optionType;
        
        Loaded += OnLoaded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
        GroupsList.ItemsSource = _allGroups;
        UpdateEmptyState();
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        TitleText.Text = loc.Get("RestoreDetail.Title");
        
        var totalCount = _allGroups.Sum(g => g.Items.Count(i => i.WillBeRestored));
        SubtitleText.Text = loc.Get("RestoreDetail.TotalCount", totalCount);
        
        SearchPlaceholder.Text = loc.Get("RestoreDetail.SearchPlaceholder");
        HintText.Text = loc.Get("RestoreDetail.RestartHint");
        EmptyStateText.Text = loc.Get("RestoreDetail.NoSearchResults");
        
        foreach (var group in _allGroups)
        {
            group.UpdateAppliedCountText();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(query))
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
                    var isMatch = item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  item.OperationId.Contains(query, StringComparison.OrdinalIgnoreCase);
                    item.IsVisible = isMatch;
                    
                    if (isMatch)
                        hasVisibleItems = true;
                }
                
                group.IsVisible = hasVisibleItems;
                
                if (hasVisibleItems)
                    group.IsExpanded = true;
            }
        }
        
        UpdateEmptyState();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
    }

    private void UpdateEmptyState()
    {
        var hasVisibleGroups = _allGroups.Any(g => g.IsVisible);
        EmptyState.Visibility = hasVisibleGroups ? Visibility.Collapsed : Visibility.Visible;
        GroupsList.Visibility = hasVisibleGroups ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(this, new RoutedEventArgs());
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    public static bool? Show(Window owner, List<RestoreDetailGroup> groups, RestoreOptionsDialog.RestoreOptionType optionType)
    {
        if (groups == null || groups.Count == 0)
            return true;
        
        var dialog = new RestoreDetailDialog(groups, optionType)
        {
            Owner = owner
        };
        
        return dialog.ShowDialog();
    }
}
