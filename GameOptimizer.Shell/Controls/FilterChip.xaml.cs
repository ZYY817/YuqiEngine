using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameOptimizer.Shell.Controls;

public partial class FilterChip : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(FilterChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(FilterChip),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IconDataProperty =
        DependencyProperty.Register(nameof(IconData), typeof(Geometry), typeof(FilterChip), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowIconProperty =
        DependencyProperty.Register(nameof(ShowIcon), typeof(bool), typeof(FilterChip), new PropertyMetadata(false));

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int), typeof(FilterChip), new PropertyMetadata(0));

    public static readonly DependencyProperty ShowCountProperty =
        DependencyProperty.Register(nameof(ShowCount), typeof(bool), typeof(FilterChip), new PropertyMetadata(false));

    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FilterChip));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public Geometry? IconData
    {
        get => (Geometry?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public bool ShowIcon
    {
        get => (bool)GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public bool ShowCount
    {
        get => (bool)GetValue(ShowCountProperty);
        set => SetValue(ShowCountProperty, value);
    }

    public event RoutedEventHandler SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    public FilterChip()
    {
        InitializeComponent();
    }

    private void ChipToggle_Changed(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent, this));
    }
}
