// File: GameOptimizer.Shell/Views/InfoDialog.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Views;

public partial class InfoDialog : Window
{
    public InfoDialog(string title, string message, string icon = "ℹ")
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = icon;
        
        Loaded += OnLoaded;
        KeyDown += Window_KeyDown;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    protected override void OnClosed(System.EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
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
        OkBtn.ApplyTemplate();
        if (OkBtn.Template.FindName("OkBtnText", OkBtn) is TextBlock okText)
            okText.Text = loc.Get("Dialog.Ok");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public static void Show(Window? owner, string title, string message, string icon = "ℹ")
    {
        // Why: 确保对话框在UI线程显示，避免跨线程异常
        if (owner != null && !owner.Dispatcher.CheckAccess())
        {
            owner.Dispatcher.Invoke(() => Show(owner, title, message, icon));
            return;
        }
        
        var dialog = new InfoDialog(title, message, icon);
        if (owner != null)
        {
            dialog.Owner = owner;
            // Why: Owner 窗口可能失焦（如 ProgressDialog 刚关闭），先拉回前台防止 ShowDialog 弹到后面锁死 UI
            owner.Activate();
        }
        dialog.ShowDialog();
    }
}
