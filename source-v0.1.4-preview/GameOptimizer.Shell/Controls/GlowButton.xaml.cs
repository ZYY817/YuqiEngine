using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GameOptimizer.Shell.Controls;

public partial class GlowButton : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(GlowButton), new PropertyMetadata("Button"));

    public static readonly DependencyProperty IconDataProperty =
        DependencyProperty.Register(nameof(IconData), typeof(Geometry), typeof(GlowButton), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowIconProperty =
        DependencyProperty.Register(nameof(ShowIcon), typeof(bool), typeof(GlowButton), new PropertyMetadata(false));

    public static readonly DependencyProperty ButtonBackgroundProperty =
        DependencyProperty.Register(nameof(ButtonBackground), typeof(Brush), typeof(GlowButton), new PropertyMetadata(null, OnButtonBackgroundChanged));

    public static readonly DependencyProperty ButtonPaddingProperty =
        DependencyProperty.Register(nameof(ButtonPadding), typeof(Thickness), typeof(GlowButton), new PropertyMetadata(new Thickness(20, 12, 20, 12)));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(GlowButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(GlowButton), new PropertyMetadata(null));

    public static readonly DependencyProperty GlowColorProperty =
        DependencyProperty.Register(nameof(GlowColor), typeof(Color), typeof(GlowButton), new PropertyMetadata(Color.FromRgb(77, 102, 255)));

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(ButtonVariant), typeof(GlowButton), new PropertyMetadata(ButtonVariant.Primary, OnVariantChanged));

    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GlowButton));

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
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

    public Brush? ButtonBackground
    {
        get => (Brush?)GetValue(ButtonBackgroundProperty);
        set => SetValue(ButtonBackgroundProperty, value);
    }

    public Thickness ButtonPadding
    {
        get => (Thickness)GetValue(ButtonPaddingProperty);
        set => SetValue(ButtonPaddingProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public Color GlowColor
    {
        get => (Color)GetValue(GlowColorProperty);
        set => SetValue(GlowColorProperty, value);
    }

    public ButtonVariant Variant
    {
        get => (ButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public GlowButton()
    {
        InitializeComponent();
        ApplyVariant();
    }

    private static void OnButtonBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Custom background overrides variant
    }

    private static void OnVariantChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GlowButton button)
            button.ApplyVariant();
    }

    private void ApplyVariant()
    {
        if (ButtonBackground != null) return;

        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        
        switch (Variant)
        {
            case ButtonVariant.Primary:
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(77, 102, 255), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(139, 92, 246), 1));
                GlowColor = Color.FromRgb(77, 102, 255);
                break;
            case ButtonVariant.Success:
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(16, 185, 129), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(52, 211, 153), 1));
                GlowColor = Color.FromRgb(16, 185, 129);
                break;
            case ButtonVariant.Warning:
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(245, 158, 11), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(251, 191, 36), 1));
                GlowColor = Color.FromRgb(245, 158, 11);
                break;
            case ButtonVariant.Danger:
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(239, 68, 68), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(248, 113, 113), 1));
                GlowColor = Color.FromRgb(239, 68, 68);
                break;
        }
        
        ButtonBackground = gradient;
    }

    private void InternalButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }
}

public enum ButtonVariant
{
    Primary,
    Success,
    Warning,
    Danger
}
