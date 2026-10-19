using System.Windows;
using System.Windows.Controls;

namespace GameOptimizer.Shell.Controls;

public partial class ModernToggle : UserControl
{
    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(ModernToggle),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ModernToggle), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowLabelProperty =
        DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(ModernToggle), new PropertyMetadata(true));

    public static readonly RoutedEvent ToggledEvent =
        EventManager.RegisterRoutedEvent(nameof(Toggled), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ModernToggle));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool ShowLabel
    {
        get => (bool)GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    public event RoutedEventHandler Toggled
    {
        add => AddHandler(ToggledEvent, value);
        remove => RemoveHandler(ToggledEvent, value);
    }

    public ModernToggle()
    {
        InitializeComponent();
    }

    private void InternalToggle_Changed(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ToggledEvent, this));
    }
}
