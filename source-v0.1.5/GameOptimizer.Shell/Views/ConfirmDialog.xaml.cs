using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Views;

public partial class ConfirmDialog : Window
{
    public bool IsConfirmed { get; private set; }

    public ConfirmDialog(string title, string message, string icon = "\uE897", string? confirmText = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = icon;
        _customConfirmText = confirmText;
        
        Loaded += OnLoaded;
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
    
    private readonly string? _customConfirmText;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        CancelBtn.ApplyTemplate();
        if (CancelBtn.Template.FindName("CancelBtnText", CancelBtn) is TextBlock cancelText)
            cancelText.Text = loc.Get("Dialog.Cancel");
        
        ConfirmBtn.ApplyTemplate();
        if (ConfirmBtn.Template.FindName("ConfirmBtnText", ConfirmBtn) is TextBlock confirmText)
            confirmText.Text = _customConfirmText ?? loc.Get("Dialog.Ok");
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
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsConfirmed = false;
            Close();
            e.Handled = true;
        }
    }

    public static bool Show(Window? owner, string title, string message, string icon = "\uE897")
    {
        // Why: 确保对话框在UI线程显示，避免跨线程异常
        if (owner != null && !owner.Dispatcher.CheckAccess())
        {
            return owner.Dispatcher.Invoke(() => Show(owner, title, message, icon));
        }
        
        var dialog = new ConfirmDialog(title, message, icon);
        if (owner != null)
        {
            dialog.Owner = owner;
            // Why: Owner 窗口可能失焦，先拉回前台防止 ShowDialog 弹到后面锁死 UI
            owner.Activate();
        }
        dialog.ShowDialog();
        return dialog.IsConfirmed;
    }
}
