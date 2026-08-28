using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class OperationInfoDialog : Window
{
    private readonly string? _manualPath;
    private readonly OperationType _operationType;

    public OperationInfoDialog(
        string title,
        string? description,
        OperationType operationType,
        string? targetName,
        string? targetValue,
        string? manualPath,
        string? manualSteps)
    {
        InitializeComponent();
        
        var loc = LocalizationService.Instance;
        _manualPath = manualPath;
        _operationType = operationType;
        
        DialogTitle.Text = title;
        OptimizationTitle.Text = title;
        OptimizationDesc.Text = description ?? loc.Get("OperationInfo.NoDescription");
        if (string.IsNullOrEmpty(description))
            OptimizationDesc.Visibility = Visibility.Collapsed;
        
        var (icon, typeName, badgeBgBrush, badgeFgBrush) = GetTypeInfo(operationType);
        TypeIcon.Text = icon;
        TypeBadgeIcon.Text = icon;
        TypeBadgeText.Text = typeName;
        TypeBadge.Background = badgeBgBrush;
        TypeBadgeIcon.Foreground = badgeFgBrush;
        TypeBadgeText.Foreground = badgeFgBrush;
        
        TargetNameText.Text = targetName ?? "-";
        TargetValueText.Text = targetValue ?? "-";
        
        if (!string.IsNullOrEmpty(manualPath))
        {
            ManualPathText.Text = manualPath;
            ManualPathSection.Visibility = Visibility.Visible;
            ManualPathLabel.Visibility = Visibility.Visible;
            OpenBtn.Visibility = Visibility.Visible;
        }
        else
        {
            ManualPathSection.Visibility = Visibility.Collapsed;
            ManualPathLabel.Visibility = Visibility.Collapsed;
            OpenBtn.Visibility = Visibility.Collapsed;
        }
        
        ManualStepsText.Text = manualSteps ?? GetDefaultSteps(operationType, targetName, targetValue);
        
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
        LblTargetInfo.Text = Constants.IconGlyphs.Target + " " + loc.Get("OperationInfo.TargetInfo");
        LblTargetName.Text = loc.Get("OperationInfo.TargetName") + ":";
        LblTargetValue.Text = loc.Get("OperationInfo.TargetValue") + ":";
        ManualPathLabel.Text = Constants.IconGlyphs.Pin + " " + loc.Get("OperationInfo.ManualPath");
        CopyBtnText.Text = loc.Get("OperationInfo.CopyCommand");
        LblManualSteps.Text = loc.Get("OperationInfo.ManualSteps");
        
        ActionBtn.ApplyTemplate();
        if (ActionBtn.Template.FindName("ActionText", ActionBtn) is System.Windows.Controls.TextBlock actionTb)
            actionTb.Text = loc.Get("OperationInfo.OpenTool");
        
        CloseBtn.ApplyTemplate();
        if (CloseBtn.Template.FindName("CloseBtnText", CloseBtn) is System.Windows.Controls.TextBlock closeTb)
            closeTb.Text = loc.Get("Common.Close");
    }

    private static (string Icon, string TypeName, SolidColorBrush BgBrush, SolidColorBrush FgBrush) GetTypeInfo(OperationType type)
    {
        var loc = LocalizationService.Instance;
        return type switch
        {
            OperationType.Service => (Constants.IconGlyphs.Service, loc.Get("OperationInfo.TypeService"), Constants.ThemeColors.OpServiceBgBrush, Constants.ThemeColors.OpServiceFgBrush),
            OperationType.ScheduledTask => (Constants.IconGlyphs.Task, loc.Get("OperationInfo.TypeTask"), Constants.ThemeColors.OpTaskBgBrush, Constants.ThemeColors.OpTaskFgBrush),
            OperationType.PowerPlan => (Constants.IconGlyphs.Power, loc.Get("OperationInfo.TypePower"), Constants.ThemeColors.OpPowerBgBrush, Constants.ThemeColors.OpPowerFgBrush),
            OperationType.Command => (Constants.IconGlyphs.Desktop, loc.Get("OperationInfo.TypeCommand"), Constants.ThemeColors.OpCommandBgBrush, Constants.ThemeColors.OpCommandFgBrush),
            OperationType.SystemApi => (Constants.IconGlyphs.Cpu, loc.Get("OperationInfo.TypeApi"), Constants.ThemeColors.OpApiBgBrush, Constants.ThemeColors.OpApiFgBrush),
            OperationType.Registry => (Constants.IconGlyphs.Registry, loc.Get("OperationInfo.TypeRegistry"), Constants.ThemeColors.OpRegistryBgBrush, Constants.ThemeColors.OpRegistryFgBrush),
            _ => (Constants.IconGlyphs.Settings, loc.Get("OperationInfo.TypeSystem"), Constants.ThemeColors.OpDefaultBgBrush, Constants.ThemeColors.OpDefaultFgBrush)
        };
    }

    private static string GetDefaultSteps(OperationType type, string? targetName, string? targetValue)
    {
        var loc = LocalizationService.Instance;
        return type switch
        {
            OperationType.Service => loc.Get("OperationInfo.StepsService"),
            OperationType.ScheduledTask => loc.Get("OperationInfo.StepsTask"),
            OperationType.PowerPlan => loc.Get("OperationInfo.StepsPower"),
            OperationType.Command => loc.Get("OperationInfo.StepsCommand"),
            OperationType.SystemApi => loc.Get("OperationInfo.StepsApi"),
            _ => loc.Get("OperationInfo.StepsDefault")
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void CopyPath_Click(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_manualPath)) return;
        
        var loc = LocalizationService.Instance;
        try
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(_manualPath, true);
                    CopyBtnText.Text = loc.Get("Common.Copied");
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                    timer.Tick += (s, args) => { CopyBtnText.Text = loc.Get("OperationInfo.CopyCommand"); timer.Stop(); };
                    timer.Start();
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OperationInfoDialog] Copy to clipboard failed: {ex.Message}");
        }
    }

    private void OpenPath_Click(object sender, MouseButtonEventArgs e) => OpenManualPath();

    private void Action_Click(object sender, RoutedEventArgs e) => OpenManualPath();

    private void OpenManualPath()
    {
        if (string.IsNullOrEmpty(_manualPath)) return;
        
        var loc = LocalizationService.Instance;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _manualPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            InfoDialog.Show(this, loc.Get("Dialog.Error"), loc.Get("OperationInfo.CannotOpen", ex.Message), Constants.IconGlyphs.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public static void Show(Window? owner, string title, string? description, OperationType operationType,
        string? targetName = null, string? targetValue = null, string? manualPath = null, string? manualSteps = null)
    {
        var dialog = new OperationInfoDialog(title, description, operationType, targetName, targetValue, manualPath, manualSteps);
        if (owner != null) dialog.Owner = owner;
        dialog.ShowDialog();
    }
}
