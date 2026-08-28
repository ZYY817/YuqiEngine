using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class OptimizationResultDialog : Window
{
    private List<ExecutionResultItem> _successItems = new();
    private List<ExecutionResultItem> _failedItems = new();
    private List<ExecutionResultItem> _skippedItems = new();
    private readonly AutoRepairService? _repairService;

    public event Action<string>? NavigateRequested;

    public OptimizationResultDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        KeyDown += Window_KeyDown;
        
        _repairService = null;
        
        // 订阅语言变化事件
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (s, e) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string newLanguage)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            UpdateLocalizedTexts();
            RefreshResultCounts();
        });
    }
    
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        TitleText.Text = loc.Get("Result.Title");
        SubtitleText.Text = loc.Get("Result.Subtitle");
        SuccessHeader.Text = loc.Get("Result.SuccessHeader");
        FailedHeader.Text = loc.Get("Result.FailedHeader");
        SkippedHeader.Text = loc.Get("Result.SkippedHeader");
        CopyReportBtn.Content = loc.Get("Result.CopyReport");
        CloseBtn.Content = loc.Get("Common.Close");
    }
    
    private void RefreshResultCounts()
    {
        var loc = LocalizationService.Instance;
        SuccessCountText.Text = loc.Get("Result.SuccessCount", _successItems.Count);
        FailedCountText.Text = loc.Get("Result.FailedCount", _failedItems.Count);
        SkippedCountText.Text = loc.Get("Result.SkippedCount", _skippedItems.Count);
    }

    public void SetResults(
        IEnumerable<ExecutionResultItem> successItems,
        IEnumerable<ExecutionResultItem> failedItems,
        IEnumerable<ExecutionResultItem> skippedItems)
    {
        _successItems = successItems.ToList();
        _failedItems = failedItems.ToList();
        _skippedItems = skippedItems.ToList();

        RefreshResultCounts();

        SuccessList.ItemsSource = _successItems;
        SuccessPanel.Visibility = _successItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        FailedList.ItemsSource = _failedItems;
        FailedPanel.Visibility = _failedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        SkippedList.ItemsSource = _skippedItems;
        SkippedPanel.Visibility = _skippedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var sb = new StringBuilder();
        sb.AppendLine($"=== {loc.Get("Result.Title")} ===");
        sb.AppendLine($"{loc.Get("Result.GeneratedAt")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine($"[{loc.Get("Result.Summary")}]");
        sb.AppendLine($"  [OK] {loc.Get("Result.SuccessCount", _successItems.Count)}");
        sb.AppendLine($"  [FAIL] {loc.Get("Result.FailedCount", _failedItems.Count)}");
        sb.AppendLine($"  [SKIP] {loc.Get("Result.SkippedCount", _skippedItems.Count)}");
        sb.AppendLine();

        if (_successItems.Count > 0)
        {
            sb.AppendLine($"[OK] {loc.Get("Result.SuccessHeader")}");
            foreach (var item in _successItems)
                sb.AppendLine($"  - {item.Title} [{item.ModuleName}]");
            sb.AppendLine();
        }

        if (_failedItems.Count > 0)
        {
            sb.AppendLine($"[FAIL] {loc.Get("Result.FailedHeader")}");
            foreach (var item in _failedItems)
            {
                sb.AppendLine($"  - {item.Title} [{item.ModuleName}]");
                if (!string.IsNullOrEmpty(item.ErrorMessage))
                    sb.AppendLine($"    {loc.Get("Result.Reason")}: {item.ErrorMessage}");
            }
            sb.AppendLine();
        }

        if (_skippedItems.Count > 0)
        {
            sb.AppendLine($"[SKIP] {loc.Get("Result.SkippedHeader")}");
            foreach (var item in _skippedItems)
            {
                sb.AppendLine($"  - {item.Title}");
                if (!string.IsNullOrEmpty(item.SkipReason))
                    sb.AppendLine($"    {item.SkipReason}");
            }
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            ToastService.Instance.Success(
                loc.Get("Result.CopySuccess"), 
                loc.Get("Result.CopySuccessMsg"));
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error(
                loc.Get("Result.CopyFailed"), 
                ex.Message);
        }
    }

    private void NavigateToConfig_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string target)
        {
            NavigateRequested?.Invoke(target);
            Close();
        }
    }

    private void ViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not ExecutionResultItem item)
            return;

        if (!item.HasDiagnostic && string.IsNullOrEmpty(item.ErrorMessage))
            return;

        var dialog = _repairService != null 
            ? new SmartErrorDialog(_repairService) 
            : new SmartErrorDialog();
        
        dialog.Owner = this;
        dialog.SetError(item.Title, item.ErrorMessage ?? LocalizationService.Instance.Get("Common.UnknownError"), item.Diagnostic, item.RepairOptions);
        
        var result = dialog.ShowDialog();
        if (result == true)
        {
            DialogResult = true;
            Close();
        }
    }

    private async void TryRepair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not ExecutionResultItem item)
            return;

        if (_repairService == null || item.RepairOptions == null || item.RepairOptions.Count == 0)
            return;

        var loc = LocalizationService.Instance;
        btn.IsEnabled = false;

        try
        {
            var progressDialog = new ProgressDialog
            {
                Owner = this,
                Title = loc.Get("Result.RepairingTitle"),
                Message = loc.Get("Result.RepairingMessage", item.Title)
            };
            progressDialog.Show();

            var repairResult = await _repairService.TryRepairAsync(item.RepairOptions.First());

            progressDialog.Close();

            if (repairResult.IsSuccess)
            {
                ToastService.Instance.Success(
                    loc.Get("Esports.Result.RepairSuccess"),
                    loc.Get("Result.RepairSuccessItem", item.Title));
                
                _failedItems.Remove(item);
                FailedList.ItemsSource = null;
                FailedList.ItemsSource = _failedItems;
                FailedPanel.Visibility = _failedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                RefreshResultCounts();
            }
            else
            {
                ToastService.Instance.Error(
                    loc.Get("Esports.Result.RepairFailed"),
                    repairResult.Message ?? loc.Get("Esports.Result.RepairFailed"));
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error(loc.Get("Esports.Result.RepairFailed"), ex.Message);
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }
}
