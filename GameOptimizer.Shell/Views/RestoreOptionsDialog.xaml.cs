// File: GameOptimizer.Shell/Views/RestoreOptionsDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class RestoreOptionsDialog : Window
{
    public enum RestoreOptionType
    {
        None,
        RestoreWindowsDefaults
    }

    public RestoreOptionType SelectedOption { get; private set; } = RestoreOptionType.RestoreWindowsDefaults;

    public RestoreOptionsDialog()
    {
        InitializeComponent();
        
        Loaded += OnLoaded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
        SelectOption(RestoreOptionType.RestoreWindowsDefaults);
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        TitleText.Text = loc.Get("RestoreOptions.Title");
        
        Option2Title.Text = loc.Get("RestoreOptions.RestoreTitle");
        Option2Desc.Text = loc.Get("RestoreOptions.RestoreDesc");
        Option2Detail.Text = loc.Get("RestoreOptions.RestoreDetail");
        Option2Risk.Text = loc.Get("RestoreOptions.RiskMedium");
        
        CancelBtn.ToolTip = loc.Get("Common.Cancel");
        ConfirmBtn.ToolTip = loc.Get("Common.Confirm");
    }

    private void Option2_Click(object sender, MouseButtonEventArgs e)
    {
        SelectOption(RestoreOptionType.RestoreWindowsDefaults);
    }

    private void SelectOption(RestoreOptionType option)
    {
        SelectedOption = option;
        
        var selectedBrush = ThemeColors.AccentBrush;
        
        if (option == RestoreOptionType.RestoreWindowsDefaults)
        {
            Option2Border.BorderBrush = selectedBrush;
            Option2Border.BorderThickness = new Thickness(2);
            Option2Indicator.Visibility = Visibility.Visible;
        }
        
        ConfirmBtn.IsEnabled = true;
    }

    private void Option2_MouseEnter(object sender, MouseEventArgs e)
    {
        if (SelectedOption != RestoreOptionType.RestoreWindowsDefaults)
        {
            var hoverBrush = (SolidColorBrush)FindResource("Theme.Brush.CardBackgroundHover");
            Option2Border.Background = hoverBrush;
        }
    }

    private void Option2_MouseLeave(object sender, MouseEventArgs e)
    {
        if (SelectedOption != RestoreOptionType.RestoreWindowsDefaults)
        {
            var defaultBrush = (SolidColorBrush)FindResource("Theme.Brush.CardBackground");
            Option2Border.Background = defaultBrush;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedOption == RestoreOptionType.None) return;
        
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

    public static RestoreOptionType? Show(Window owner)
    {
        var dialog = new RestoreOptionsDialog
        {
            Owner = owner
        };
        
        var result = dialog.ShowDialog();
        return result == true ? dialog.SelectedOption : null;
    }
}
