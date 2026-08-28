// File: GameOptimizer.Shell/Controls/ToolTopbar.xaml.cs
using System.Windows;
using System.Windows.Controls;

namespace GameOptimizer.Shell.Controls;

public partial class ToolTopbar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ToolTopbar), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ToolTopbar), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(ToolTopbar), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconDataProperty =
        DependencyProperty.Register(nameof(IconData), typeof(string), typeof(ToolTopbar), new PropertyMetadata(string.Empty, OnIconDataChanged));
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(ToolTopbar), new PropertyMetadata(null));
    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(ToolTopbar), new PropertyMetadata(string.Empty, OnStatusChanged));
    public static readonly DependencyProperty StatusDetailProperty =
        DependencyProperty.Register(nameof(StatusDetail), typeof(string), typeof(ToolTopbar), new PropertyMetadata(string.Empty));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string IconGlyph { get => (string)GetValue(IconGlyphProperty); set => SetValue(IconGlyphProperty, value); }
    public string IconData { get => (string)GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public string StatusDetail { get => (string)GetValue(StatusDetailProperty); set => SetValue(StatusDetailProperty, value); }

    public ToolTopbar()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateIconPresentation();
    }

    private static void OnIconDataChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        if (source is ToolTopbar topbar)
            topbar.UpdateIconPresentation();
    }

    private void UpdateIconPresentation()
    {
        if (VectorIcon is null || GlyphIcon is null)
            return;

        var hasVectorIcon = !string.IsNullOrWhiteSpace(IconData);
        VectorIcon.Visibility = hasVectorIcon ? Visibility.Visible : Visibility.Collapsed;
        GlyphIcon.Visibility = hasVectorIcon ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnStatusChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        if (source is ToolTopbar topbar)
            topbar.StatusPill.Visibility = string.IsNullOrWhiteSpace(e.NewValue as string)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }
}
