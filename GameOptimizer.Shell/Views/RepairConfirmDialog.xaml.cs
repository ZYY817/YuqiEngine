using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class RepairConfirmDialog : Window
{
    public bool IsConfirmed { get; private set; }

    public RepairConfirmDialog(RepairItem item, RepairConfirmationContext context)
    {
        InitializeComponent();
        
        var loc = LocalizationService.Instance;
        ItemIcon.Text = item.Icon;
        ItemName.Text = item.Name;
        ItemDesc.Text = item.Description;
        IconText.Text = item.Icon;
        
        if (context.IsHealthy)
        {
            StatusBorder.Background = ThemeColors.EmeraldAlpha20Brush;
            StatusIcon.Text = Constants.IconGlyphs.Success;
            StatusText.Text = loc.Get("RepairConfirm.StatusHealthy");
            StatusText.Foreground = ThemeColors.CyanBrush;
        }
        else
        {
            StatusBorder.Background = ThemeColors.WarningAlpha20Brush;
            StatusIcon.Text = Constants.IconGlyphs.Warning;
            StatusText.Text = loc.Get("RepairConfirm.StatusUnhealthy");
            StatusText.Foreground = ThemeColors.WarningLightBrush;
        }

        DetectionText.Text = context.DetectionSummary;
        ChangePlanText.Text = string.Join("\n", context.PlannedChanges.Select(change => $"• {change}"));
        RecoveryText.Text = context.RecoverySummary;
        
        RiskBorder.Visibility = item.IsRisky ? Visibility.Visible : Visibility.Collapsed;
        
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
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        DialogTitleText.Text = loc.Get("RepairConfirm.Title");
        RiskText.Text = loc.Get("RepairConfirm.RiskWarning");
        
        CancelBtn.ApplyTemplate();
        if (CancelBtn.Template.FindName("CancelBtnText", CancelBtn) is System.Windows.Controls.TextBlock cancelTb)
            cancelTb.Text = loc.Get("Common.Cancel");
        
        ConfirmBtn.ApplyTemplate();
        if (ConfirmBtn.Template.FindName("ConfirmBtnText", ConfirmBtn) is System.Windows.Controls.TextBlock confirmTb)
            confirmTb.Text = loc.Get("RepairConfirm.StartRepair");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    public static bool Show(Window? owner, RepairItem item, RepairConfirmationContext context)
    {
        var dialog = new RepairConfirmDialog(item, context);
        if (owner != null) dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.IsConfirmed;
    }
}

/// <summary>Facts that must be visible before a mutating repair starts.</summary>
public sealed record RepairConfirmationContext(
    bool IsHealthy,
    string DetectionSummary,
    IReadOnlyList<string> PlannedChanges,
    string RecoverySummary);
