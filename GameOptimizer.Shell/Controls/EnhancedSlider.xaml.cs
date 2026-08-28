using System;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Controls;

public enum SliderRiskLevel { Low, Medium, High }
public enum SliderZoneMode { LowIsBetter, HighIsBetter, MidIsBetter, Custom }

/// <summary>
/// A token-based value editor.  It deliberately uses the application Slider style
/// instead of owning a second, gradient-heavy slider implementation.
/// </summary>
public partial class EnhancedSlider : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EnhancedSlider), new PropertyMetadata(string.Empty, OnPresentationChanged));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(EnhancedSlider), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(EnhancedSlider),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(EnhancedSlider), new PropertyMetadata(0d));
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(EnhancedSlider), new PropertyMetadata(100d));
    public static readonly DependencyProperty IsSnapToTickProperty =
        DependencyProperty.Register(nameof(IsSnapToTick), typeof(bool), typeof(EnhancedSlider), new PropertyMetadata(false));
    public static readonly DependencyProperty TickFrequencyProperty =
        DependencyProperty.Register(nameof(TickFrequency), typeof(double), typeof(EnhancedSlider), new PropertyMetadata(1d));
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(EnhancedSlider), new PropertyMetadata("%", OnPresentationChanged));
    public static readonly DependencyProperty RecommendedValueProperty =
        DependencyProperty.Register(nameof(RecommendedValue), typeof(double?), typeof(EnhancedSlider), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty ZoneModeProperty =
        DependencyProperty.Register(nameof(ZoneMode), typeof(SliderZoneMode), typeof(EnhancedSlider), new PropertyMetadata(SliderZoneMode.HighIsBetter));
    public static readonly DependencyProperty RiskLevelProperty =
        DependencyProperty.Register(nameof(RiskLevel), typeof(SliderRiskLevel), typeof(EnhancedSlider), new PropertyMetadata(SliderRiskLevel.Low, OnPresentationChanged));
    public static readonly DependencyProperty DetailedTooltipProperty =
        DependencyProperty.Register(nameof(DetailedTooltip), typeof(string), typeof(EnhancedSlider), new PropertyMetadata(null));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool IsSnapToTick { get => (bool)GetValue(IsSnapToTickProperty); set => SetValue(IsSnapToTickProperty, value); }
    public double TickFrequency { get => (double)GetValue(TickFrequencyProperty); set => SetValue(TickFrequencyProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double? RecommendedValue { get => (double?)GetValue(RecommendedValueProperty); set => SetValue(RecommendedValueProperty, value); }
    public SliderZoneMode ZoneMode { get => (SliderZoneMode)GetValue(ZoneModeProperty); set => SetValue(ZoneModeProperty, value); }
    public SliderRiskLevel RiskLevel { get => (SliderRiskLevel)GetValue(RiskLevelProperty); set => SetValue(RiskLevelProperty, value); }
    public string? DetailedTooltip { get => (string?)GetValue(DetailedTooltipProperty); set => SetValue(DetailedTooltipProperty, value); }

    private double? _originalValue;
    private bool _isInitialized;

    public event EventHandler<double>? ValueChanged;

    public EnhancedSlider()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _isInitialized = true;
            UpdatePresentation();
        };
    }

    public bool IsModified => _originalValue.HasValue && Math.Abs(_originalValue.Value - Value) > 0.01;

    public void SetOriginalValue(double value)
    {
        _originalValue = value;
        UpdatePresentation();
    }

    public void ClearOriginalValue()
    {
        _originalValue = null;
        UpdatePresentation();
    }

    public void ApplyRecommendedValue()
    {
        if (RecommendedValue.HasValue)
            Value = RecommendedValue.Value;
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not EnhancedSlider slider || !slider._isInitialized)
            return;

        slider.UpdatePresentation();
        slider.ValueChanged?.Invoke(slider, (double)args.NewValue);
    }

    private static void OnPresentationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        if (dependencyObject is EnhancedSlider slider && slider._isInitialized)
            slider.UpdatePresentation();
    }

    private void MainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (Math.Abs(Value - args.NewValue) > 0.0001)
            Value = args.NewValue;
    }

    private void UpdatePresentation()
    {
        if (!IsLoaded)
            return;

        TxtCurrentValue.Text = FormatValue(Value);
        var isModified = IsModified;
        TxtOriginalValue.Text = isModified ? FormatValue(_originalValue!.Value) : string.Empty;
        TxtOriginalValue.Visibility = isModified ? Visibility.Visible : Visibility.Collapsed;
        TxtArrow.Visibility = isModified ? Visibility.Visible : Visibility.Collapsed;

        RecommendBadge.Visibility = RecommendedValue.HasValue ? Visibility.Visible : Visibility.Collapsed;
        TxtRecommend.Text = RecommendedValue.HasValue
            ? LocalizationService.Instance.Get("Controls.Recommend", FormatValue(RecommendedValue.Value))
            : string.Empty;

        RiskBadge.Visibility = RiskLevel == SliderRiskLevel.Low ? Visibility.Collapsed : Visibility.Visible;
        TxtRisk.Text = RiskLevel switch
        {
            SliderRiskLevel.Medium => "可能影响体验",
            SliderRiskLevel.High => "需要明确确认",
            _ => string.Empty
        };
    }

    private string FormatValue(double value)
    {
        var display = Math.Abs(value - Math.Round(value)) < 0.001
            ? Math.Round(value).ToString("0")
            : value.ToString("0.##");
        return string.IsNullOrWhiteSpace(Unit) ? display : $"{display} {Unit}";
    }
}
