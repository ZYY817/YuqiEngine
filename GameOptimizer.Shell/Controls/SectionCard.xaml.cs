using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameOptimizer.Shell.Controls;

public partial class SectionCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(SectionCard), new PropertyMetadata(null));

    public static readonly DependencyProperty IconDataProperty =
        DependencyProperty.Register(nameof(IconData), typeof(Geometry), typeof(SectionCard), new PropertyMetadata(null));

    public static readonly DependencyProperty IconBrushProperty =
        DependencyProperty.Register(nameof(IconBrush), typeof(Brush), typeof(SectionCard), new PropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty IconBackgroundProperty =
        DependencyProperty.Register(nameof(IconBackground), typeof(Color), typeof(SectionCard), new PropertyMetadata(Colors.Transparent));

    public static readonly DependencyProperty ShowIconProperty =
        DependencyProperty.Register(nameof(ShowIcon), typeof(bool), typeof(SectionCard), new PropertyMetadata(true));

    public static readonly DependencyProperty HasHeaderProperty =
        DependencyProperty.Register(nameof(HasHeader), typeof(bool), typeof(SectionCard), new PropertyMetadata(true));

    public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(SectionCard), new PropertyMetadata(null));

    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(object), typeof(SectionCard), new PropertyMetadata(null));

    public static readonly DependencyProperty CardPaddingProperty =
        DependencyProperty.Register(nameof(CardPadding), typeof(Thickness), typeof(SectionCard), new PropertyMetadata(new Thickness(16)));

    public static readonly DependencyProperty VisualStateProperty =
        DependencyProperty.Register(nameof(VisualState), typeof(ControlVisualState), typeof(SectionCard), new PropertyMetadata(ControlVisualState.Default));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public Geometry? IconData { get => (Geometry?)GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    public Brush IconBrush { get => (Brush)GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }
    public Color IconBackground { get => (Color)GetValue(IconBackgroundProperty); set => SetValue(IconBackgroundProperty, value); }
    public bool ShowIcon { get => (bool)GetValue(ShowIconProperty); set => SetValue(ShowIconProperty, value); }
    public bool HasHeader { get => (bool)GetValue(HasHeaderProperty); set => SetValue(HasHeaderProperty, value); }
    public object? HeaderContent { get => GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }
    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    public Thickness CardPadding { get => (Thickness)GetValue(CardPaddingProperty); set => SetValue(CardPaddingProperty, value); }
    public ControlVisualState VisualState { get => (ControlVisualState)GetValue(VisualStateProperty); set => SetValue(VisualStateProperty, value); }

    public SectionCard()
    {
        InitializeComponent();
        SetResourceReference(IconBrushProperty, "Theme.Brush.ControlAccent");
        SetResourceReference(IconBackgroundProperty, "Theme.Icon.Background");
        SetResourceReference(CardPaddingProperty, "Token.Padding.Card");
    }
}
