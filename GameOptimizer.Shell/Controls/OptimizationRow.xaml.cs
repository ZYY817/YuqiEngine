using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Controls;

public enum RowStateIndicator { None, Applied, Pending }

public partial class OptimizationRow : UserControl
{
    public static readonly DependencyProperty OperationIdProperty = DependencyProperty.Register(nameof(OperationId), typeof(string), typeof(OptimizationRow), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(OptimizationRow), new PropertyMetadata(null, (d, e) => (d as OptimizationRow)!.TitleText.Text = (string)e.NewValue));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(OptimizationRow), new PropertyMetadata("", OnDescriptionChanged));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(OptimizationRow), new PropertyMetadata(IconGlyphs.Power, (d, e) => (d as OptimizationRow)!.IconText.Text = (string)e.NewValue));
    public static readonly DependencyProperty IsExtremeProperty = DependencyProperty.Register(nameof(IsExtreme), typeof(bool), typeof(OptimizationRow), new PropertyMetadata(false, (d, e) => (d as OptimizationRow)!.RiskBadge.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(OptimizationRow), new PropertyMetadata(false, OnIsCheckedChanged));
    public static readonly DependencyProperty IsCompatibleProperty = DependencyProperty.Register(nameof(IsCompatible), typeof(bool), typeof(OptimizationRow), new PropertyMetadata(true, OnIsCompatibleChanged));
    public static readonly DependencyProperty UnavailableReasonProperty = DependencyProperty.Register(nameof(UnavailableReason), typeof(string), typeof(OptimizationRow), new PropertyMetadata("", OnUnavailableReasonChanged));
    public static readonly DependencyProperty StateIndicatorProperty = DependencyProperty.Register(nameof(StateIndicator), typeof(RowStateIndicator), typeof(OptimizationRow), new PropertyMetadata(RowStateIndicator.None, OnStateIndicatorChanged));
    public static readonly RoutedEvent CheckedChangedEvent = EventManager.RegisterRoutedEvent(nameof(CheckedChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OptimizationRow));
    public static readonly RoutedEvent DetailsClickedEvent = EventManager.RegisterRoutedEvent(nameof(DetailsClicked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OptimizationRow));

    public string OperationId { get => (string)GetValue(OperationIdProperty); set => SetValue(OperationIdProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool IsExtreme { get => (bool)GetValue(IsExtremeProperty); set => SetValue(IsExtremeProperty, value); }
    public bool IsChecked { get => (bool)GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
    public bool IsCompatible { get => (bool)GetValue(IsCompatibleProperty); set => SetValue(IsCompatibleProperty, value); }
    public string UnavailableReason { get => (string)GetValue(UnavailableReasonProperty); set => SetValue(UnavailableReasonProperty, value); }
    public RowStateIndicator StateIndicator { get => (RowStateIndicator)GetValue(StateIndicatorProperty); set => SetValue(StateIndicatorProperty, value); }
    public event RoutedEventHandler CheckedChanged { add => AddHandler(CheckedChangedEvent, value); remove => RemoveHandler(CheckedChangedEvent, value); }
    public event RoutedEventHandler DetailsClicked { add => AddHandler(DetailsClickedEvent, value); remove => RemoveHandler(DetailsClickedEvent, value); }

    public OptimizationRow() => InitializeComponent();

    // Programmatic IsChecked changes must not re-enter Checked/Unchecked. Esports mode used to
    // bounce applied-but-incompatible rows until the UI thread overflowed the stack guard page.
    private bool _synchronizingCheckState;

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OptimizationRow row)
        {
            var desc = (string)e.NewValue;
            row.InfoBtn.ToolTip = string.IsNullOrEmpty(desc) ? null : desc;
            row.InfoBtn.Visibility = string.IsNullOrEmpty(desc) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not OptimizationRow row || row._synchronizingCheckState) return;
        row._synchronizingCheckState = true;
        try
        {
            row.RowToggle.IsChecked = (bool)e.NewValue;
        }
        finally
        {
            row._synchronizingCheckState = false;
        }
    }
    private static void OnIsCompatibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) 
    { 
        if (d is OptimizationRow row) 
        { 
            var isCompatible = (bool)e.NewValue;
            row.RowToggle.IsEnabled = isCompatible;
            row.Opacity = isCompatible ? 1.0 : 0.72;
            row.UpdateAvailabilityPresentation();
        } 
    }

    private static void OnUnavailableReasonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OptimizationRow row)
        {
            row.UpdateAvailabilityPresentation();
        }
    }

    private void UpdateAvailabilityPresentation()
    {
        var showReason = !IsCompatible && !string.IsNullOrWhiteSpace(UnavailableReason);
        UnavailableReasonText.Text = showReason ? UnavailableReason : string.Empty;
        UnavailableReasonText.Visibility = showReason ? Visibility.Visible : Visibility.Collapsed;
        RowToggle.ToolTip = showReason ? UnavailableReason : null;
    }
    
    private static void OnStateIndicatorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OptimizationRow row)
        {
            var state = (RowStateIndicator)e.NewValue;
            row.UpdateStateIndicatorUI(state);
        }
    }
    
    private void UpdateStateIndicatorUI(RowStateIndicator state)
    {
        var loc = LocalizationService.Instance;
        switch (state)
        {
            case RowStateIndicator.Applied:
                StateBadge.Visibility = Visibility.Visible;
                StateBadgeText.Text = loc.Get("Controls.Applied");
                StateBadge.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x4A, 0xDE, 0x80));
                StateBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                break;
            case RowStateIndicator.Pending:
                StateBadge.Visibility = Visibility.Visible;
                StateBadgeText.Text = loc.Get("Controls.Pending");
                StateBadge.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x63, 0x66, 0xF1));
                StateBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
                break;
            default:
                StateBadge.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private bool _isHighlighted;
    private void OnToggleChecked(object sender, RoutedEventArgs e)
    {
        if (_synchronizingCheckState) return;
        _synchronizingCheckState = true;
        try
        {
            IsChecked = true;
        }
        finally
        {
            _synchronizingCheckState = false;
        }

        RaiseEvent(new RoutedEventArgs(CheckedChangedEvent, this));
    }

    private void OnToggleUnchecked(object sender, RoutedEventArgs e)
    {
        if (_synchronizingCheckState) return;
        _synchronizingCheckState = true;
        try
        {
            IsChecked = false;
        }
        finally
        {
            _synchronizingCheckState = false;
        }

        RaiseEvent(new RoutedEventArgs(CheckedChangedEvent, this));
    }
    private void OnDetailsClick(object sender, MouseButtonEventArgs e) { RaiseEvent(new RoutedEventArgs(DetailsClickedEvent, this)); }
    private void OnMouseEnter(object sender, MouseEventArgs e) { if (IsCompatible && !_isHighlighted) RowBorder.Background = new SolidColorBrush(Color.FromArgb(0x12, 0x63, 0x66, 0xF1)); }
    private void OnMouseLeave(object sender, MouseEventArgs e) { if (!_isHighlighted) RowBorder.Background = new SolidColorBrush(Colors.Transparent); }

    public void SetHighlight()
    {
        _isHighlighted = true;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x25, 0x63, 0x66, 0xF1), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x08, 0x63, 0x66, 0xF1), 0.7));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        RowBorder.Background = brush;
        RowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x63, 0x66, 0xF1));
        RowBorder.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    public void ClearHighlight()
    {
        _isHighlighted = false;
        RowBorder.Background = new SolidColorBrush(Colors.Transparent);
        RowBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
        RowBorder.BorderThickness = new Thickness(0);
    }
}
