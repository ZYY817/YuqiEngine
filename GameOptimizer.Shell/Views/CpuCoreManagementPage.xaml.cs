// File: GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml.cs
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Controls;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class CpuCoreManagementPage : UserControl
{
    private bool _isInitialized;
    private bool? _usesCompactWorkspace;
    private ICollectionView? _processListView;

    public CpuCoreManagementPage(CpuCoreManagementViewModel viewModel)
    {
        PageDiagnosticService.Log("CpuCoreManagementPage", "Constructor called");
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnPageSizeChanged;
        DataContext = viewModel;
        _isInitialized = true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            if (DataContext is CpuCoreManagementViewModel viewModel)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            UpdateLocalizedTexts();
            ConfigureProcessListView();
            ShowLiveWorkspace();
            UpdateWorkspaceLayout();

            if (!_isInitialized)
                throw new InvalidOperationException("CpuCoreManagementPage requires DI construction.");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("CpuCoreManagementPage", "OnLoaded failed", ex);
        }
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        if (DataContext is CpuCoreManagementViewModel viewModel)
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        try
        {
            // Existing lifecycle behavior: leaving this page restores an active affinity receipt.
            if (DataContext is CpuCoreManagementViewModel vm)
                await vm.DeactivateAsync();
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("CpuCoreManagementPage", "Receipt restore on unload failed", ex);
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateWorkspaceLayout();

    // Both workspaces use the same responsive rule but are never layered. Each pane owns its own scroller.
    private void UpdateWorkspaceLayout()
    {
        if (ActualWidth <= 0)
            return;

        // The page lives beside the persistent navigation rail.  At a 1200 DIP
        // application window its own content area is only about 930 DIP wide,
        // which is still sufficient for the 5:8 process/matrix workbench.
        // Stacking at 1180 collapsed each independently scrolling pane to half
        // height and made the process chooser effectively unusable.
        var compact = ActualWidth < 900;
        if (_usesCompactWorkspace == compact)
            return;

        _usesCompactWorkspace = compact;
        ArrangeWorkspace(LiveWorkspaceGrid, ProcessWorkspace, LiveCoreWorkspace, compact);
        ArrangeWorkspace(ProfileWorkspaceGrid, ProfileListWorkspace, ProfilePreviewWorkspace, compact);
    }

    private static void ArrangeWorkspace(
        Grid workspace,
        FrameworkElement leftPane,
        FrameworkElement rightPane,
        bool compact)
    {
        workspace.ColumnDefinitions.Clear();
        workspace.RowDefinitions.Clear();

        if (compact)
        {
            workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(leftPane, 0);
            Grid.SetColumn(leftPane, 0);
            Grid.SetRow(rightPane, 2);
            Grid.SetColumn(rightPane, 0);
            return;
        }

        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Star) });

        Grid.SetRow(leftPane, 0);
        Grid.SetColumn(leftPane, 0);
        Grid.SetRow(rightPane, 0);
        Grid.SetColumn(rightPane, 2);
    }

    private void ProfilesButton_Click(object sender, RoutedEventArgs e) => ShowProfileWorkspace();

    private void BackToLiveButton_Click(object sender, RoutedEventArgs e) => ShowLiveWorkspace();

    private void ShowLiveWorkspace()
    {
        if (DataContext is CpuCoreManagementViewModel vm)
            vm.ActiveTab = 0;

        LiveWorkspace.Visibility = Visibility.Visible;
        ProfileWorkspace.Visibility = Visibility.Collapsed;
        LiveCommandBar.Visibility = Visibility.Visible;
        ProfileCommandBar.Visibility = Visibility.Collapsed;
    }

    private void ShowProfileWorkspace()
    {
        if (DataContext is CpuCoreManagementViewModel vm)
            vm.ActiveTab = 1;

        LiveWorkspace.Visibility = Visibility.Collapsed;
        ProfileWorkspace.Visibility = Visibility.Visible;
        LiveCommandBar.Visibility = Visibility.Collapsed;
        ProfileCommandBar.Visibility = Visibility.Visible;
        UpdateProfilePreviewState();
    }

    private void OnLanguageChanged(object? sender, string e) => _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CpuCoreManagementViewModel.ConfigurationPreviewText))
            _ = Dispatcher.InvokeAsync(UpdateTargetPreview);

        if (e.PropertyName == nameof(CpuCoreManagementViewModel.SelectedProcess) &&
            (ProcessFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "selected")
        {
            _processListView?.Refresh();
        }

        if (e.PropertyName == nameof(CpuCoreManagementViewModel.SelectedProfile) ||
            e.PropertyName == nameof(CpuCoreManagementViewModel.HasUsableTopology))
        {
            _ = Dispatcher.InvokeAsync(UpdateProfilePreviewState);
        }
    }

    // A profile preview must never borrow the real-time default core selection.
    // This only controls visibility; profile data and affinity commands remain in the ViewModel.
    private void UpdateProfilePreviewState()
    {
        if (DataContext is not CpuCoreManagementViewModel viewModel)
            return;

        var hasProfile = viewModel.SelectedProfile is not null;
        ProfileCorePreview.Visibility = hasProfile && viewModel.HasUsableTopology
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProfileTopologyUnavailable.Visibility = hasProfile && !viewModel.HasUsableTopology
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoProfileSelectedState.Visibility = hasProfile ? Visibility.Collapsed : Visibility.Visible;
        ProfileActions.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;
    }

    // Search/filter affect only the CollectionView, never ProcessList, SelectedProcess, or affinity execution.
    private void ConfigureProcessListView()
    {
        if (DataContext is not CpuCoreManagementViewModel viewModel)
            return;

        _processListView = CollectionViewSource.GetDefaultView(viewModel.ProcessList);
        _processListView.Filter = FilterProcess;
        _processListView.Refresh();
    }

    private bool FilterProcess(object candidate)
    {
        if (candidate is not ProcessItem process)
            return false;

        var search = ProcessSearchBox?.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(search) &&
            !process.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
            !process.WindowTitle.Contains(search, StringComparison.OrdinalIgnoreCase) &&
            !process.Id.ToString().Contains(search, StringComparison.Ordinal))
        {
            return false;
        }

        return (ProcessFilterBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "window" => !string.IsNullOrWhiteSpace(process.WindowTitle),
            "selected" => DataContext is CpuCoreManagementViewModel vm && process.IsSameInstance(vm.SelectedProcess),
            _ => true
        };
    }

    private void ProcessSearchBox_TextChanged(object sender, TextChangedEventArgs e) => _processListView?.Refresh();

    private void ProcessFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => _processListView?.Refresh();

    private void UpdateTargetPreview()
    {
        if (DataContext is CpuCoreManagementViewModel viewModel)
            GuideLabel.Text = viewModel.ConfigurationPreviewText;
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        CpuInfoLabel.Text = loc.Get("CoreMgmt.CpuInfoLabel");
        CoreTopologyTitle.Text = loc.Get("CoreAccel.CoreTopology");
        SelectedLabel.Text = loc.Get("CoreAccel.Selected") + " ";
        SelectAllBtn.Text = loc.Get("CoreAccel.SelectAll");
        ClearAllBtn.Text = loc.Get("CoreAccel.ClearAll");
        InvertBtn.Text = loc.Get("CoreAccel.Invert");
        RestoreDefaultBtn.Text = loc.Get("CoreMgmt.RestoreDefault");
        RefreshBtnText.Text = loc.Get("CoreAccel.Refresh");
        StopBtnText.Text = loc.Get("CoreAccel.StopBtn");
        StartBtnText.Text = loc.Get("CoreAccel.StartAccel");
        AddProgramBtn.Text = loc.Get("CpuAffinity.AddProgram");
        NoConfigState.Title = loc.Get("CpuAffinity.NoConfig");
        NoConfigState.Description = loc.Get("CoreMgmt.NoConfigHint");
        NoConfigState.ActionText = loc.Get("CpuAffinity.AddProgram");
        UpdateTargetPreview();
    }
}
