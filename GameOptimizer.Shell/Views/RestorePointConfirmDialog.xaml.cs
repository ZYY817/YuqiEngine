// File: GameOptimizer.Shell/Views/RestorePointConfirmDialog.xaml.cs
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class RestorePointConfirmDialog : Window
{
    public bool IsConfirmed { get; private set; }

    public RestorePointConfirmDialog()
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
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        // 更新静态UI文本
        DescriptionLabel.Text = loc.Get("RestoreConfirm.DescriptionLabel");
        InfoLabel.Text = loc.Get("RestoreConfirm.InfoLabel");
        
        // 取消按钮在ControlTemplate内，需要遍历查找
        UpdateCancelButtonText(loc.Get("Common.Cancel"));
    }
    
    private void UpdateCancelButtonText(string text)
    {
        var cancelBtn = FindVisualChild<Button>(this, b => b.Name != "ConfirmBtn");
        if (cancelBtn != null)
        {
            var textBlock = FindVisualChild<TextBlock>(cancelBtn, _ => true);
            if (textBlock != null)
            {
                textBlock.Text = text;
            }
        }
    }
    
    private static T? FindVisualChild<T>(DependencyObject parent, System.Func<T, bool> predicate) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && predicate(t))
                return t;
            var result = FindVisualChild<T>(child, predicate);
            if (result != null)
                return result;
        }
        return null;
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    private void AddInfoItem(string text, SolidColorBrush bulletBrush)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(new TextBlock 
        { 
            Text = "•", 
            Foreground = bulletBrush, 
            FontSize = 12, 
            Width = 16 
        });
        var textBlock = new TextBlock 
        { 
            Text = text, 
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
        sp.Children.Add(textBlock);
        InfoList.Children.Add(sp);
    }

    public static bool ShowCreateConfirm(Window? owner, string description)
    {
        var loc = LocalizationService.Instance;
        var dialog = new RestorePointConfirmDialog();
        if (owner != null) dialog.Owner = owner;

        dialog.TitleIcon.Text = Constants.IconGlyphs.Save;
        dialog.TitleText.Text = loc.Get("RestoreConfirm.CreateTitle");
        
        dialog.CardIcon.Text = Constants.IconGlyphs.Shield;
        dialog.CardTitle.Text = loc.Get("RestoreConfirm.CreateCardTitle");
        dialog.CardDescription.Text = loc.Get("RestoreConfirm.CreateCardDesc");
        
        dialog.DescriptionText.Text = description;
        dialog.DescriptionSection.Visibility = Visibility.Visible;
        
        dialog.InfoList.Children.Clear();
        dialog.AddInfoItem(loc.Get("RestoreConfirm.CreateInfo1"), ThemeColors.SuccessBrush);
        dialog.AddInfoItem(loc.Get("RestoreConfirm.CreateInfo2"), ThemeColors.SuccessBrush);
        dialog.AddInfoItem(loc.Get("RestoreConfirm.CreateInfo3"), ThemeColors.TextMutedBrush);
        dialog.AddInfoItem(loc.Get("RestoreConfirm.CreateInfo4"), ThemeColors.WarningBrush);
        
        dialog.TimeHintText.Text = loc.Get("RestoreConfirm.CreateTimeHint");
        dialog.FooterHint.Text = loc.Get("RestoreConfirm.CreateFooter");
        dialog.ConfirmBtn.Template = CreateGreenButtonTemplate(loc.Get("RestoreConfirm.ConfirmCreate"));

        dialog.ShowDialog();
        return dialog.IsConfirmed;
    }

    public static bool ShowRestoreConfirm(Window? owner, string pointDescription, string creationTime)
    {
        var loc = LocalizationService.Instance;
        var dialog = new RestorePointConfirmDialog();
        if (owner != null) dialog.Owner = owner;

        dialog.TitleIcon.Text = Constants.IconGlyphs.Refresh;
        dialog.TitleText.Text = loc.Get("RestoreConfirm.RestoreTitle");
        
        dialog.MainInfoCard.Background = new LinearGradientBrush(
            Color.FromArgb(0x10, ThemeColors.Warning.R, ThemeColors.Warning.G, ThemeColors.Warning.B),
            Color.FromArgb(0x08, 0x09, 0x09, 0x0B),
            45);
        dialog.MainInfoCard.BorderBrush = new LinearGradientBrush(
            Color.FromArgb(0x25, ThemeColors.Warning.R, ThemeColors.Warning.G, ThemeColors.Warning.B),
            Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF),
            45);
        
        var iconBorder = (Border)dialog.CardIcon.Parent;
        iconBorder.Background = new LinearGradientBrush(
            Color.FromArgb(0x20, ThemeColors.Warning.R, ThemeColors.Warning.G, ThemeColors.Warning.B),
            Color.FromArgb(0x10, ThemeColors.Warning.R, ThemeColors.Warning.G, ThemeColors.Warning.B),
            45);
        
        dialog.CardIcon.Text = Constants.IconGlyphs.Warning;
        dialog.CardTitle.Text = loc.Get("RestoreConfirm.RestoreCardTitle");
        dialog.CardDescription.Text = loc.Get("RestoreConfirm.RestoreCardDesc", pointDescription, creationTime);
        
        dialog.DescriptionSection.Visibility = Visibility.Collapsed;
        
        dialog.InfoList.Children.Clear();
        dialog.AddInfoItem(loc.Get("RestoreConfirm.RestoreInfo1"), ThemeColors.WarningBrush);
        dialog.AddInfoItem(loc.Get("RestoreConfirm.RestoreInfo2"), ThemeColors.DangerBrush);
        dialog.AddInfoItem(loc.Get("RestoreConfirm.RestoreInfo3"), ThemeColors.SuccessBrush);
        dialog.AddInfoItem(loc.Get("RestoreConfirm.RestoreInfo4"), ThemeColors.DangerBrush);
        
        dialog.TimeHintText.Text = loc.Get("RestoreConfirm.RestoreTimeHint");
        dialog.FooterHint.Text = loc.Get("RestoreConfirm.RestoreFooter");
        dialog.ConfirmBtn.Template = CreateOrangeButtonTemplate(loc.Get("RestoreConfirm.ConfirmRestore"));

        dialog.ShowDialog();
        return dialog.IsConfirmed;
    }

    private static ControlTemplate CreateGreenButtonTemplate(string buttonText)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(20, 10, 20, 10));
        border.SetValue(Border.BackgroundProperty, new LinearGradientBrush(
            ThemeColors.Success,
            ThemeColors.SuccessDark,
            0));

        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetValue(TextBlock.TextProperty, Constants.IconGlyphs.Save);
        icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        icon.SetValue(TextBlock.FontSizeProperty, 12.0);
        icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, buttonText);
        text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        text.SetValue(TextBlock.FontSizeProperty, 12.0);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 0, 0));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        sp.AppendChild(icon);
        sp.AppendChild(text);
        border.AppendChild(sp);
        template.VisualTree = border;

        var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty, new LinearGradientBrush(
            ThemeColors.SuccessLight,
            ThemeColors.Success,
            0), "Bd"));
        template.Triggers.Add(trigger);

        return template;
    }

    private static ControlTemplate CreateOrangeButtonTemplate(string buttonText)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(20, 10, 20, 10));
        border.SetValue(Border.BackgroundProperty, new LinearGradientBrush(
            ThemeColors.Warning,
            ThemeColors.WarningDark,
            0));

        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetValue(TextBlock.TextProperty, Constants.IconGlyphs.Refresh);
        icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        icon.SetValue(TextBlock.FontSizeProperty, 12.0);
        icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, buttonText);
        text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        text.SetValue(TextBlock.FontSizeProperty, 12.0);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 0, 0));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        sp.AppendChild(icon);
        sp.AppendChild(text);
        border.AppendChild(sp);
        template.VisualTree = border;

        var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty, new LinearGradientBrush(
            ThemeColors.WarningLight,
            ThemeColors.Warning,
            0), "Bd"));
        template.Triggers.Add(trigger);

        return template;
    }
}
