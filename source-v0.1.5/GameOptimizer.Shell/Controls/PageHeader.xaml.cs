using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameOptimizer.Shell.Controls;

public partial class PageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty IconDataProperty =
        DependencyProperty.Register(nameof(IconData), typeof(Geometry), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowIconProperty =
        DependencyProperty.Register(nameof(ShowIcon), typeof(bool), typeof(PageHeader), new PropertyMetadata(false));

    // Kept for source compatibility; the foundation renders a flat semantic icon.
    public static readonly DependencyProperty IconGradientStartProperty =
        DependencyProperty.Register(nameof(IconGradientStart), typeof(Color), typeof(PageHeader), new PropertyMetadata(Colors.Transparent));

    public static readonly DependencyProperty IconGradientEndProperty =
        DependencyProperty.Register(nameof(IconGradientEnd), typeof(Color), typeof(PageHeader), new PropertyMetadata(Colors.Transparent));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty VisualStateProperty =
        DependencyProperty.Register(nameof(VisualState), typeof(ControlVisualState), typeof(PageHeader), new PropertyMetadata(ControlVisualState.Default));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public Geometry? IconData { get => (Geometry?)GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    public bool ShowIcon { get => (bool)GetValue(ShowIconProperty); set => SetValue(ShowIconProperty, value); }
    public Color IconGradientStart { get => (Color)GetValue(IconGradientStartProperty); set => SetValue(IconGradientStartProperty, value); }
    public Color IconGradientEnd { get => (Color)GetValue(IconGradientEndProperty); set => SetValue(IconGradientEndProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public ControlVisualState VisualState { get => (ControlVisualState)GetValue(VisualStateProperty); set => SetValue(VisualStateProperty, value); }

    public PageHeader()
    {
        InitializeComponent();
        SetResourceReference(IconGradientStartProperty, "Theme.Accent.Blue");
        SetResourceReference(IconGradientEndProperty, "Theme.Accent.Purple");
    }
}
