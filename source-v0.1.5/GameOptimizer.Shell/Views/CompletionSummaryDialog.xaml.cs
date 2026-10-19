// File: GameOptimizer.Shell/Views/CompletionSummaryDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Views;

/// <summary>
/// Displays detailed results after batch optimization operations complete.
/// Shows success count, failure list, and expandable error details.
/// </summary>
public partial class CompletionSummaryDialog : Window
{
    private readonly BatchResult _result;

    private CompletionSummaryDialog(BatchResult result)
    {
        // Guard: Defensive null check
        _result = result ?? throw new ArgumentNullException(nameof(result));
        
        InitializeComponent();
        InitializeContent();
        
        Loaded += OnLoaded;
        KeyDown += Window_KeyDown;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void InitializeContent()
    {
        // Guard: Handle edge case of no operations
        if (_result.SuccessCount == 0 && _result.FailedCount == 0)
        {
            TitleText.Text = "操作结果";
            SuccessText.Text = "未执行任何操作";
            SuccessSubText.Text = "未获得可展示的执行回执";
            return;
        }

        // Display success count. A mixed or failed batch is a result, not a completion.
        TitleText.Text = _result.FailedCount > 0 ? "操作结果" : "操作完成";
        SuccessText.Text = $"成功应用 {_result.SuccessCount} 项优化";
        
        // Update subtitle based on failure status
        if (_result.FailedCount == 0)
        {
            SuccessSubText.Text = "所有操作已完成";
        }
        else
        {
            SuccessSubText.Text = $"部分操作完成，{_result.FailedCount} 项失败";
        }

        // Show failure section if there are errors
        if (_result.FailedCount > 0 && _result.Errors != null && _result.Errors.Count > 0)
        {
            FailureSummaryPanel.Visibility = Visibility.Visible;
            FailureText.Text = $"失败 {_result.FailedCount} 项";
            
            ErrorDetailsExpander.Visibility = Visibility.Visible;
            ErrorList.ItemsSource = _result.Errors;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        // Update title
        TitleText.Text = _result.FailedCount > 0
            ? loc.Get("Dashboard.OperationResult")
            : loc.Get("Dialog.CompletionSummary.Title");
        
        // Update button text
        CloseBtn.ApplyTemplate();
        if (CloseBtn.Template.FindName("CloseBtnText", CloseBtn) is System.Windows.Controls.TextBlock closeText)
        {
            closeText.Text = loc.Get("Dialog.Close");
        }
        
        // Update view details text
        ViewDetailsText.Text = loc.Get("Dialog.CompletionSummary.ViewDetails");
        
        // Update success/failure texts with current counts
        if (_result.SuccessCount > 0 || _result.FailedCount > 0)
        {
            SuccessText.Text = string.Format(
                loc.Get("Dialog.CompletionSummary.SuccessFormat"), 
                _result.SuccessCount);
            
            if (_result.FailedCount > 0)
            {
                FailureText.Text = string.Format(
                    loc.Get("Dialog.CompletionSummary.FailedFormat"), 
                    _result.FailedCount);
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Displays the completion summary dialog with batch operation results.
    /// Thread-safe: can be called from any thread.
    /// </summary>
    /// <param name="owner">Owner window for modal display. Can be null.</param>
    /// <param name="result">Batch operation result containing success/failure counts and errors.</param>
    /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
    public static void Show(Window? owner, BatchResult result)
    {
        // Guard: Validate required parameter
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        // Thread safety: Ensure dialog is shown on UI thread
        if (owner != null && !owner.Dispatcher.CheckAccess())
        {
            owner.Dispatcher.Invoke(() => Show(owner, result));
            return;
        }

        var dialog = new CompletionSummaryDialog(result);
        
        if (owner != null)
        {
            dialog.Owner = owner;
            // Why: Activate owner to prevent dialog appearing behind other windows
            owner.Activate();
            owner.Focus();
        }
        
        // FIX: 确保对话框获得焦点，防止界面卡死
        dialog.Loaded += (s, e) => 
        {
            dialog.Activate();
            dialog.Focus();
        };
        
        dialog.ShowDialog();
    }
}
