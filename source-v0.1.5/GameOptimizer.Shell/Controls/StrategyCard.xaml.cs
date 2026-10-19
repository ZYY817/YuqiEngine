using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Controls;

public partial class StrategyCard : UserControl
{
    public static readonly DependencyProperty StrategyTypeProperty =
        DependencyProperty.Register(nameof(StrategyType), typeof(StrategyType), typeof(StrategyCard),
            new PropertyMetadata(StrategyType.Safe, OnStrategyTypeChanged));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(StrategyCard),
            new PropertyMetadata(false, OnIsSelectedChanged));

    public static readonly RoutedEvent SelectedEvent =
        EventManager.RegisterRoutedEvent(nameof(Selected), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(StrategyCard));

    public StrategyType StrategyType { get => (StrategyType)GetValue(StrategyTypeProperty); set => SetValue(StrategyTypeProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public event RoutedEventHandler Selected { add => AddHandler(SelectedEvent, value); remove => RemoveHandler(SelectedEvent, value); }

    public StrategyCard()
    {
        InitializeComponent();
        UpdateVisuals();
    }

    private static void OnStrategyTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => (d as StrategyCard)?.UpdateVisuals();
    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => (d as StrategyCard)?.UpdateSelectionVisuals();

    private void UpdateVisuals()
    {
        var loc = LocalizationService.Instance;
        var (icon, title, modeLabel, desc, ledColor) = StrategyType switch
        {
            StrategyType.Safe => (IconGlyphs.Shield, loc.Get("Controls.SafeMode"), "SAFE MODE", loc.Get("Controls.SafeModeDesc"), Color.FromRgb(0x4A, 0xDE, 0x80)),
            StrategyType.Balanced => (IconGlyphs.Power, loc.Get("Controls.BalancedMode"), "BALANCED", loc.Get("Controls.BalancedModeDesc"), Color.FromRgb(0x60, 0xA5, 0xFA)),
            StrategyType.Extreme => (IconGlyphs.RiskHigh, loc.Get("Controls.ExtremeMode"), "EXTREME", loc.Get("Controls.ExtremeModeDesc"), Color.FromRgb(0xF4, 0x3F, 0x5E)),
            _ => (IconGlyphs.Shield, loc.Get("Controls.SafeMode"), "SAFE MODE", loc.Get("Controls.SafeModeDesc"), Color.FromRgb(0x4A, 0xDE, 0x80))
        };
        IconText.Text = icon;
        TitleText.Text = title;
        ModeLabel.Text = modeLabel;
        DescText.Text = desc;
        LedIndicator.Fill = new SolidColorBrush(ledColor);
        if (LedIndicator.Effect is DropShadowEffect ledGlow) ledGlow.Color = ledColor;
        UpdateSelectionVisuals();
    }

    private void UpdateSelectionVisuals()
    {
        if (IsSelected)
        {
            var (borderColor, glowColor1, glowColor2) = StrategyType switch
            {
                StrategyType.Safe => (Color.FromRgb(0x4A, 0xDE, 0x80), Color.FromArgb(0x50, 0x4A, 0xDE, 0x80), Color.FromArgb(0x00, 0x4A, 0xDE, 0x80)),
                StrategyType.Balanced => (Color.FromRgb(0x60, 0xA5, 0xFA), Color.FromArgb(0x50, 0x60, 0xA5, 0xFA), Color.FromArgb(0x00, 0x60, 0xA5, 0xFA)),
                StrategyType.Extreme => (Color.FromRgb(0xF4, 0x3F, 0x5E), Color.FromArgb(0x60, 0xF4, 0x3F, 0x5E), Color.FromArgb(0x00, 0x8B, 0x5C, 0xF6)),
                _ => (Color.FromRgb(0x4A, 0xDE, 0x80), Color.FromArgb(0x50, 0x4A, 0xDE, 0x80), Color.FromArgb(0x00, 0x4A, 0xDE, 0x80))
            };
            CardBorder.BorderBrush = new SolidColorBrush(borderColor);
            CardBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, borderColor.R, borderColor.G, borderColor.B));
            GlowBorder.Background = new RadialGradientBrush(glowColor1, glowColor2);
            GlowBorder.Opacity = 1;
            if (CardBorder.Effect is DropShadowEffect shadow) { shadow.Color = borderColor; shadow.BlurRadius = 30; shadow.Opacity = 0.4; }
        }
        else
        {
            CardBorder.BorderBrush = ThemeColors.WhiteAlpha15Brush;
            CardBorder.Background = ThemeColors.WhiteAlpha08Brush;
            GlowBorder.Opacity = 0;
            if (CardBorder.Effect is DropShadowEffect shadow) { shadow.Color = Colors.Black; shadow.BlurRadius = 20; shadow.Opacity = 0.5; }
        }
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e) => RaiseEvent(new RoutedEventArgs(SelectedEvent, this));

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (!IsSelected)
        {
            CardBorder.Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            CardBorder.BorderBrush = ThemeColors.WhiteAlpha25Brush;
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => UpdateSelectionVisuals();
}

public enum StrategyType { Safe, Balanced, Extreme }