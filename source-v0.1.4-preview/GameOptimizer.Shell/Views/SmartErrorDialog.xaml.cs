// File: GameOptimizer.Shell/Views/SmartErrorDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class SmartErrorDialog : Window
{
    private DiagnosticResult? _diagnostic;
    private List<AutoRepairOption>? _repairOptions;
    private readonly AutoRepairService? _repairService;

    public SmartErrorDialog()
    {
        InitializeComponent();
        KeyDown += Window_KeyDown;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
    }

    public SmartErrorDialog(AutoRepairService repairService) : this()
    {
        _repairService = repairService;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        Title = loc.Get("Error.UI.Title");
        TitleText.Text = loc.Get("Error.UI.Title");
        CopyReportBtn.Content = loc.Get("Error.UI.CopyReport");
    }


    public void SetError(string operationTitle, string errorMessage, 
        DiagnosticResult? diagnostic = null, 
        List<AutoRepairOption>? repairOptions = null)
    {
        _diagnostic = diagnostic;
        _repairOptions = repairOptions;

        var loc = LocalizationService.Instance;
        TitleText.Text = $"{loc.Get("Error.DetailTitle")}: {operationTitle}";
        
        if (diagnostic != null)
        {
            ErrorSummary.Text = diagnostic.UserMessage ?? errorMessage;
            
            if (diagnostic.PossibleCauses?.Count > 0)
            {
                CausesSection.Visibility = Visibility.Visible;
                CausesList.ItemsSource = diagnostic.PossibleCauses;
            }
            
            if (diagnostic.Solutions?.Count > 0)
            {
                ManualStepsSection.Visibility = Visibility.Visible;
                var stepsWithNumbers = diagnostic.Solutions
                    .OrderBy(s => s.Order)
                    .Select((step, index) => new { StepNumber = (index + 1).ToString(), Description = step.Description })
                    .ToList();
                ManualStepsList.ItemsSource = stepsWithNumbers;
            }
            
            if (!string.IsNullOrEmpty(diagnostic.TechnicalDetails))
            {
                TechnicalSection.Visibility = Visibility.Visible;
                TechnicalDetails.Text = diagnostic.TechnicalDetails;
            }
        }
        else
        {
            ErrorSummary.Text = errorMessage;
        }
        
        if (repairOptions?.Count > 0)
        {
            RepairSection.Visibility = Visibility.Visible;
            RepairOptionsList.ItemsSource = repairOptions;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void RepairOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not AutoRepairOption option)
            return;

        if (_repairService == null)
        {
            var loc = LocalizationService.Instance;
            InfoDialog.Show(this, loc.Get("Dialog.Error"), 
                loc.Get("Error.RepairServiceUnavailable"), Constants.IconGlyphs.Warning);
            return;
        }

        btn.IsEnabled = false;
        var originalContent = btn.Content;
        
        try
        {
            var loadingPanel = new System.Windows.Controls.StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal 
            };
            var spinner = new System.Windows.Controls.TextBlock
            {
                Text = "\uE1CD",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var text = new System.Windows.Controls.TextBlock
            {
                Text = LocalizationService.Instance.Get("Error.Repairing"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            loadingPanel.Children.Add(spinner);
            loadingPanel.Children.Add(text);
            btn.Content = loadingPanel;

            var result = await _repairService.TryRepairAsync(option);
            
            if (result.IsSuccess)
            {
                ToastService.Instance.Success(
                    LocalizationService.Instance.Get("Error.RepairSuccess"),
                    LocalizationService.Instance.Get("Error.RepairSuccessMsg"));
                
                await System.Threading.Tasks.Task.Delay(500);
                DialogResult = true;
                Close();
            }
            else
            {
                ToastService.Instance.Error(
                    LocalizationService.Instance.Get("Error.RepairFailed"),
                    result.Message ?? LocalizationService.Instance.Get("Error.RepairFailedMsg"));
                btn.Content = originalContent;
                btn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartErrorDialog] Repair failed: {ex.Message}");
            ToastService.Instance.Error(
                LocalizationService.Instance.Get("Error.RepairFailed"),
                ex.Message);
            btn.Content = originalContent;
            btn.IsEnabled = true;
        }
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var sb = new StringBuilder();
        sb.AppendLine($"=== {loc.Get("Error.ErrorReport")} ===");
        sb.AppendLine($"{loc.Get("Error.GeneratedAt")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        
        sb.AppendLine($"[{loc.Get("Error.Summary")}]");
        sb.AppendLine(ErrorSummary.Text);
        sb.AppendLine();
        
        if (_diagnostic?.PossibleCauses?.Count > 0)
        {
            sb.AppendLine($"[{loc.Get("Error.PossibleCauses")}]");
            foreach (var cause in _diagnostic.PossibleCauses)
                sb.AppendLine($"  • {cause}");
            sb.AppendLine();
        }
        
        if (_diagnostic?.Solutions?.Count > 0)
        {
            sb.AppendLine($"[{loc.Get("Error.ManualSteps")}]");
            foreach (var solution in _diagnostic.Solutions.OrderBy(s => s.Order))
            {
                var prefix = solution.RequiresAdmin ? loc.Get("Error.RequiresAdminTag") : "";
                var suffix = solution.RequiresReboot ? loc.Get("Error.RequiresRebootTag") : "";
                sb.AppendLine($"  {solution.Order}. {prefix}{solution.Description}{suffix}");
                if (!string.IsNullOrEmpty(solution.Command))
                    sb.AppendLine($"     {loc.Get("Error.CommandLabel")}{solution.Command}");
            }
            sb.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(_diagnostic?.TechnicalDetails))
        {
            sb.AppendLine($"[{loc.Get("Error.TechnicalDetails")}]");
            sb.AppendLine(_diagnostic.TechnicalDetails);
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            ToastService.Instance.Success(
                loc.Get("Error.CopySuccess"),
                loc.Get("Error.CopySuccessMsg"));
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error(
                loc.Get("Error.CopyFailed"),
                ex.Message);
        }
    }
}
