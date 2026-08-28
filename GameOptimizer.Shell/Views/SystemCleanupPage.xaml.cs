using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.SystemCleanup;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class SystemCleanupPage : UserControl
{
    private readonly SystemCleanupViewModel _viewModel;

    public SystemCleanupPage(SystemCleanupViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        PageDiagnosticService.Log("SystemCleanupPage", "Constructor called");
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            PageDiagnosticService.Log("SystemCleanupPage", "OnLoaded - Reusing injected ViewModel");
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            UpdateLocalizedTexts();
            PageDiagnosticService.LogLoaded("SystemCleanupPage", true, $"Cleanup catalog loaded: {_viewModel.CleanupItems.Count} items");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("SystemCleanupPage", "Failed to create ViewModel", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

        // The page and its view model are both navigation singletons.  Clearing or disposing
        // the DataContext on a visibility switch leaves the cached page visually alive but empty
        // when it is shown again.
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        if (FindName("PageTitle") is TextBlock title)
            title.Text = loc.Get("Cleanup.Title");
        VisiblePageTitle.Text = loc.Get("Cleanup.Title");
        PageSubtitle.Text = loc.Get("Cleanup.Subtitle");
        SystemDiskLabel.Text = loc.Get("Cleanup.SystemDisk");
        AllCategoryButton.Content = loc.Get("Cleanup.Category.All");
        SystemCategoryButton.Content = loc.Get("Cleanup.Category.System");
        UserCategoryButton.Content = loc.Get("Cleanup.Category.User");
        GameCategoryButton.Content = loc.Get("Cleanup.Category.Game");
        BrowserCategoryButton.Content = loc.Get("Cleanup.Category.Browser");
        CleanupSafetyHint.Text = loc.Get("Cleanup.SafetyHint");
        // The visual shell uses binding-driven state text; do not overwrite it on language refresh.
        if (FindName("DiskTotalLabel") is TextBlock diskTotal)
            diskTotal.Text = loc.Get("Cleanup.DiskTotal");
        if (FindName("DiskUsedLabel") is TextBlock diskUsed)
            diskUsed.Text = loc.Get("Cleanup.DiskUsed");
        if (FindName("DiskFreeLabel") is TextBlock diskFree)
            diskFree.Text = loc.Get("Cleanup.DiskFree");
        if (FindName("SelectAllText") is TextBlock selectAll)
            selectAll.Text = loc.Get("Cleanup.SelectAll");
        if (FindName("DeselectAllText") is TextBlock deselectAll)
            deselectAll.Text = loc.Get("Cleanup.DeselectAll");
        if (FindName("SelectSafeOnlyText") is TextBlock selectSafe)
            selectSafe.Text = loc.Get("Cleanup.SelectSafeOnly");
        if (FindName("RescanText") is TextBlock rescan)
            rescan.Text = loc.Get("Cleanup.Rescan");
        if (FindName("RestoreQuarantineText") is TextBlock restoreQuarantine)
            restoreQuarantine.Text = loc.Get("Cleanup.RestoreQuarantine");
        if (FindName("SelectedLabel") is TextBlock selected)
            selected.Text = loc.Get("Cleanup.Selected");
        if (FindName("ItemsLabel") is TextBlock items)
            items.Text = loc.Get("Cleanup.Items");
        if (FindName("TotalLabel") is TextBlock total)
            total.Text = loc.Get("Cleanup.Total");
        if (FindName("FilesLabel") is TextBlock files)
            files.Text = loc.Get("Cleanup.Files");
        if (FindName("CancelText") is TextBlock cancel)
            cancel.Text = loc.Get("Common.Cancel");
        if (FindName("CleanNowText") is TextBlock cleanNow)
            cleanNow.Text = loc.Get("Cleanup.CleanNow");
    }
    
    private void ViewDetails_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.Tag is not CleanupItem item) return;
        
        try
        {
            var dialog = new CleanupFileListDialog(item)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("SystemCleanupPage", "Failed to show details dialog", ex);
            var loc = LocalizationService.Instance;
            ToastService.Instance.Error(loc.Get("Cleanup.Title"), loc.Get("Common.OperationFailed"));
        }
    }

    private async void ScanCore_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsWorking)
            return;

        try
        {
            PageDiagnosticService.Log("SystemCleanupPage", "Scanner core clicked.");
            await _viewModel.RunScannerActionAsync();
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("SystemCleanupPage", "Scanner action failed", ex);
            ToastService.Instance.Error("系统清理", "扫描未能完成，请查看日志后重试。");
        }
    }
}
