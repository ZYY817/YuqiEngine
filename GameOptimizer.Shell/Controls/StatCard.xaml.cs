using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameOptimizer.Shell.Controls;

public partial class StatCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(StatCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatCard), new PropertyMetadata("0"));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(StatCard), new PropertyMetadata(null));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(StatCard), new PropertyMetadata(null));

    public static readonly DependencyProperty IconDataProperty =
        DependencyProperty.Register(nameof(IconData), typeof(Geometry), typeof(StatCard), new PropertyMetadata(null));

    public static readonly DependencyProperty IconBrushProperty =
        DependencyProperty.Register(nameof(IconBrush), typeof(Brush), typeof(StatCard),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(77, 102, 255))));

    public static readonly DependencyProperty IconBackgroundProperty =
        DependencyProperty.Register(nameof(IconBackground), typeof(Color), typeof(StatCard),
            new PropertyMetadata(Color.FromRgb(30, 42, 74)));

    public static readonly DependencyProperty ShowIconProperty =
        DependencyProperty.Register(nameof(ShowIcon), typeof(bool), typeof(StatCard), new PropertyMetadata(true));

    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(nameof(Badge), typeof(object), typeof(StatCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowTrendProperty =
        DependencyProperty.Register(nameof(ShowTrend), typeof(bool), typeof(StatCard), new PropertyMetadata(false));

    public static readonly DependencyProperty TrendDirectionProperty =
        DependencyProperty.Register(nameof(TrendDirection), typeof(TrendType), typeof(StatCard), new PropertyMetadata(TrendType.None));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Geometry? IconData
    {
        get => (Geometry?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public Brush IconBrush
    {
        get => (Brush)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public Color IconBackground
    {
        get => (Color)GetValue(IconBackgroundProperty);
        set => SetValue(IconBackgroundProperty, value);
    }

    public bool ShowIcon
    {
        get => (bool)GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    public object? Badge
    {
        get => GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public bool ShowTrend
    {
        get => (bool)GetValue(ShowTrendProperty);
        set => SetValue(ShowTrendProperty, value);
    }

    public TrendType TrendDirection
    {
        get => (TrendType)GetValue(TrendDirectionProperty);
        set => SetValue(TrendDirectionProperty, value);
    }

    public StatCard()
    {
        InitializeComponent();
    }
}

public enum TrendType
{
    None,
    Up,
    Down,
    Stable
}
