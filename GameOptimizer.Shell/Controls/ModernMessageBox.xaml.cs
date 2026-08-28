using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Controls;

public enum MessageBoxType { Info, Success, Warning, Error, Confirm }

public partial class ModernMessageBox : Window
{
    public bool Result { get; private set; }

    private ModernMessageBox()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }

    private void SetType(MessageBoxType type)
    {
        var loc = LocalizationService.Instance;
        // P1-020: 使用Segoe MDL2 Assets字体图标替代emoji
        // \uE73E = Checkmark, \uE7BA = Warning, \uE711 = Cancel/Error, \uE897 = Help, \uE946 = Info
        var (icon, color, title) = type switch
        {
            MessageBoxType.Success => ("\uE73E", Color.FromRgb(0x10, 0xB9, 0x81), loc.Get("Controls.Success")),
            MessageBoxType.Warning => ("\uE7BA", Color.FromRgb(0xF5, 0x9E, 0x0B), loc.Get("Controls.Warning")),
            MessageBoxType.Error => ("\uE711", Color.FromRgb(0xEF, 0x44, 0x44), loc.Get("Controls.Error")),
            MessageBoxType.Confirm => ("\uE897", Color.FromRgb(0x4D, 0x66, 0xFF), loc.Get("Controls.Confirm")),
            _ => ("\uE946", Color.FromRgb(0x4D, 0x66, 0xFF), loc.Get("Controls.Info"))
        };

        IconText.Text = icon;
        IconText.Foreground = new SolidColorBrush(color);
        IconBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
        TitleText.Text = title;
    }

    private void AddButtons(bool isConfirm)
    {
        var loc = LocalizationService.Instance;
        ButtonPanel.Children.Clear();

        if (isConfirm)
        {
            var cancelBtn = CreateButton(loc.Get("Controls.Cancel"), false, false);
            cancelBtn.Margin = new Thickness(0, 0, 10, 0);
            ButtonPanel.Children.Add(cancelBtn);

            var confirmBtn = CreateButton(loc.Get("Controls.Ok"), true, true);
            ButtonPanel.Children.Add(confirmBtn);
        }
        else
        {
            var okBtn = CreateButton(loc.Get("Controls.Ok"), true, true);
            ButtonPanel.Children.Add(okBtn);
        }
    }

    private Button CreateButton(string text, bool isPrimary, bool result)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(24, 10, 24, 10),
            Cursor = Cursors.Hand,
            FontSize = 13,
            FontWeight = FontWeights.Medium
        };

        if (isPrimary)
        {
            btn.Background = new SolidColorBrush(Color.FromRgb(0x4D, 0x66, 0xFF));
            btn.Foreground = Brushes.White;
        }
        else
        {
            btn.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x40));
            btn.Foreground = ThemeColors.MutedPurpleBrush;
        }

        btn.Template = CreateButtonTemplate(isPrimary);
        btn.Click += (s, e) => { Result = result; Close(); };

        return btn;
    }

    private static ControlTemplate CreateButtonTemplate(bool isPrimary)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.Name = "Bd";

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        template.VisualTree = border;

        var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(BackgroundProperty, 
            new SolidColorBrush(isPrimary ? Color.FromRgb(0x6B, 0x7F, 0xFF) : Color.FromRgb(0x2A, 0x35, 0x55)), "Bd"));
        template.Triggers.Add(hoverTrigger);

        return template;
    }

    public static void Show(string message, string? title = null, MessageBoxType type = MessageBoxType.Info, Window? owner = null)
    {
        var box = new ModernMessageBox();
        box.SetType(type);
        box.MessageText.Text = message;
        if (!string.IsNullOrEmpty(title)) box.TitleText.Text = title;
        box.AddButtons(false);
        box.Owner = owner ?? Application.Current.MainWindow;
        box.ShowDialog();
    }

    public static bool Confirm(string message, string? title = null, Window? owner = null)
    {
        var box = new ModernMessageBox();
        box.SetType(MessageBoxType.Confirm);
        box.MessageText.Text = message;
        if (!string.IsNullOrEmpty(title)) box.TitleText.Text = title;
        box.AddButtons(true);
        box.Owner = owner ?? Application.Current.MainWindow;
        box.ShowDialog();
        return box.Result;
    }

    public static void Success(string message, Window? owner = null)
    {
        var loc = LocalizationService.Instance;
        Show(message, loc.Get("Controls.Success"), MessageBoxType.Success, owner);
    }
    public static void Warning(string message, Window? owner = null)
    {
        var loc = LocalizationService.Instance;
        Show(message, loc.Get("Controls.Warning"), MessageBoxType.Warning, owner);
    }
    public static void Error(string message, Window? owner = null)
    {
        var loc = LocalizationService.Instance;
        Show(message, loc.Get("Controls.Error"), MessageBoxType.Error, owner);
    }
    public static void Info(string message, Window? owner = null)
    {
        var loc = LocalizationService.Instance;
        Show(message, loc.Get("Controls.Info"), MessageBoxType.Info, owner);
    }
}
