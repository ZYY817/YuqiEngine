using System.Windows;
using System.Windows.Controls;

namespace GameOptimizer.Shell.Controls;

public partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata("状态"));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(BadgeStatus), typeof(StatusBadge), new PropertyMetadata(BadgeStatus.Info));

    public static readonly DependencyProperty ShowDotProperty =
        DependencyProperty.Register(nameof(ShowDot), typeof(bool), typeof(StatusBadge), new PropertyMetadata(true));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public BadgeStatus Status { get => (BadgeStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public bool ShowDot { get => (bool)GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }

    public StatusBadge() => InitializeComponent();
}

public enum BadgeStatus
{
    Info,
    Success,
    Warning,
    Error,
    Loading,
    Unauthorized,
    Recovering
}
