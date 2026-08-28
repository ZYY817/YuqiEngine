using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Controls;

public partial class EnhancedToggle : UserControl
{
    #region 依赖属性
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EnhancedToggle), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(EnhancedToggle), new PropertyMetadata(""));

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(EnhancedToggle),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

    public static readonly DependencyProperty OnTextProperty =
        DependencyProperty.Register(nameof(OnText), typeof(string), typeof(EnhancedToggle), new PropertyMetadata(null));

    public static readonly DependencyProperty OffTextProperty =
        DependencyProperty.Register(nameof(OffText), typeof(string), typeof(EnhancedToggle), new PropertyMetadata(null));

    public static readonly DependencyProperty RecommendedValueProperty =
        DependencyProperty.Register(nameof(RecommendedValue), typeof(bool?), typeof(EnhancedToggle),
            new PropertyMetadata(null, OnRecommendedValueChanged));

    public static readonly DependencyProperty RiskLevelProperty =
        DependencyProperty.Register(nameof(RiskLevel), typeof(SliderRiskLevel), typeof(EnhancedToggle),
            new PropertyMetadata(SliderRiskLevel.Low, OnRiskLevelChanged));

    public static readonly DependencyProperty OnIsGoodProperty =
        DependencyProperty.Register(nameof(OnIsGood), typeof(bool), typeof(EnhancedToggle), new PropertyMetadata(true));

    public static readonly DependencyProperty DetailedTooltipProperty =
        DependencyProperty.Register(nameof(DetailedTooltip), typeof(string), typeof(EnhancedToggle),
            new PropertyMetadata(null, OnDetailedTooltipChanged));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public bool IsChecked { get => (bool)GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
    public string OnText { get => (string)GetValue(OnTextProperty); set => SetValue(OnTextProperty, value); }
    public string OffText { get => (string)GetValue(OffTextProperty); set => SetValue(OffTextProperty, value); }
    public bool? RecommendedValue { get => (bool?)GetValue(RecommendedValueProperty); set => SetValue(RecommendedValueProperty, value); }
    public SliderRiskLevel RiskLevel { get => (SliderRiskLevel)GetValue(RiskLevelProperty); set => SetValue(RiskLevelProperty, value); }
    public bool OnIsGood { get => (bool)GetValue(OnIsGoodProperty); set => SetValue(OnIsGoodProperty, value); }
    public string? DetailedTooltip { get => (string?)GetValue(DetailedTooltipProperty); set => SetValue(DetailedTooltipProperty, value); }
    #endregion

    private bool? _originalValue;
    private bool _isInitialized;

    public event EventHandler<bool>? Toggled;

    public EnhancedToggle()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isInitialized = true;
        UpdateDisplay();
        UpdateRecommendBadge();
        UpdateRiskBadge();
    }

    public void SetOriginalValue(bool value)
    {
        _originalValue = value;
        UpdateComparisonDisplay();
    }

    public void ClearOriginalValue()
    {
        _originalValue = null;
        TxtOriginalValue.Visibility = Visibility.Collapsed;
        TxtArrow.Visibility = Visibility.Collapsed;
        ModifiedBadge.Visibility = Visibility.Collapsed;
    }

    public bool IsModified => _originalValue.HasValue && _originalValue.Value != IsChecked;

    #region 事件处理
    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EnhancedToggle toggle && toggle._isInitialized)
        {
            toggle.UpdateDisplay();
            toggle.UpdateComparisonDisplay();
            toggle.Toggled?.Invoke(toggle, (bool)e.NewValue);
        }
    }

    private static void OnRecommendedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EnhancedToggle toggle && toggle._isInitialized)
            toggle.UpdateRecommendBadge();
    }

    private static void OnRiskLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EnhancedToggle toggle && toggle._isInitialized)
            toggle.UpdateRiskBadge();
    }

    private static void OnDetailedTooltipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EnhancedToggle toggle && toggle._isInitialized)
            toggle.UpdateTooltip();
    }

    private void Toggle_Click(object sender, MouseButtonEventArgs e)
    {
        IsChecked = !IsChecked;
    }
    #endregion

    #region UI 更新
    private void UpdateDisplay()
    {
        var loc = LocalizationService.Instance;
        var isOn = IsChecked;
        var isGood = OnIsGood ? isOn : !isOn;
        var onText = string.IsNullOrEmpty(OnText) ? loc.Get("Controls.Disabled") : OnText;
        var offText = string.IsNullOrEmpty(OffText) ? loc.Get("Controls.Enabled") : OffText;

        // 状态文字
        TxtStatus.Text = isOn ? onText : offText;
        TxtStatus.Foreground = new SolidColorBrush(isGood 
            ? Color.FromRgb(0x00, 0xC8, 0x53) 
            : Color.FromRgb(0xFF, 0x6B, 0x00));

        // 开关动画 - iOS 风格 (Green/Grey)
        var targetMargin = isOn ? new Thickness(22, 0, 0, 0) : new Thickness(2, 0, 0, 0);
        var targetColor = isOn 
            ? Color.FromRgb(0x34, 0xC7, 0x59)  // iOS Green
            : Color.FromRgb(0x39, 0x39, 0x3D); // iOS Grey

        var marginAnim = new ThicknessAnimation(targetMargin, TimeSpan.FromMilliseconds(150));
        var colorAnim = new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(150));

        ToggleThumb.BeginAnimation(MarginProperty, marginAnim);
        
        // 确保 Background 是可动画的 SolidColorBrush
        if (ToggleBorder.Background is not SolidColorBrush || ToggleBorder.Background.IsFrozen)
        {
            ToggleBorder.Background = new SolidColorBrush(targetColor);
        }
        else
        {
            ToggleBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
        }
    }

    private void UpdateComparisonDisplay()
    {
        if (!_originalValue.HasValue) return;

        var loc = LocalizationService.Instance;
        var isModified = _originalValue.Value != IsChecked;
        var onText = string.IsNullOrEmpty(OnText) ? loc.Get("Controls.Disabled") : OnText;
        var offText = string.IsNullOrEmpty(OffText) ? loc.Get("Controls.Enabled") : OffText;
        
        if (isModified)
        {
            TxtOriginalValue.Text = _originalValue.Value ? onText : offText;
            TxtOriginalValue.Visibility = Visibility.Visible;
            TxtArrow.Visibility = Visibility.Visible;
            ModifiedBadge.Visibility = Visibility.Visible;
        }
        else
        {
            TxtOriginalValue.Visibility = Visibility.Collapsed;
            TxtArrow.Visibility = Visibility.Collapsed;
            ModifiedBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateRecommendBadge()
    {
        if (!RecommendedValue.HasValue)
        {
            RecommendBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var loc = LocalizationService.Instance;
        var onText = string.IsNullOrEmpty(OnText) ? loc.Get("Controls.Disabled") : OnText;
        var offText = string.IsNullOrEmpty(OffText) ? loc.Get("Controls.Enabled") : OffText;
        var recommendText = RecommendedValue.Value ? onText : offText;
        TxtRecommend.Text = loc.Get("Controls.Recommend", recommendText);
        
        // 根据推荐值设置颜色
        var isGoodRecommend = OnIsGood ? RecommendedValue.Value : !RecommendedValue.Value;
        if (isGoodRecommend)
        {
            RecommendBadge.Background = ThemeColors.EmeraldAlpha33Brush;
            TxtRecommend.Foreground = ThemeColors.EmeraldBrush;
        }
        else
        {
            RecommendBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x6B, 0x00));
            TxtRecommend.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x00));
        }
        
        RecommendBadge.Visibility = Visibility.Visible;
    }

    private void UpdateRiskBadge()
    {
        var loc = LocalizationService.Instance;
        switch (RiskLevel)
        {
            case SliderRiskLevel.High:
                RiskBadge.Background = new LinearGradientBrush(
                    ThemeColors.DangerAlpha25Brush.Color,
                    ThemeColors.DangerAlpha15Brush.Color,
                    0);
                RiskBadge.BorderBrush = ThemeColors.DangerAlpha40Brush;
                RiskBadge.BorderThickness = new Thickness(1);
                TxtRisk.Text = loc.Get("Controls.NeedCooling");
                TxtRisk.Foreground = ThemeColors.DangerBrush;
                RiskBadge.ToolTip = loc.Get("Controls.NeedCoolingTip");
                RiskBadge.Visibility = Visibility.Visible;
                break;
            case SliderRiskLevel.Medium:
                RiskBadge.Background = new LinearGradientBrush(
                    ThemeColors.WarningAlpha25Brush.Color,
                    ThemeColors.WarningAlpha15Brush.Color,
                    0);
                RiskBadge.BorderBrush = ThemeColors.WarningAlpha40Brush;
                RiskBadge.BorderThickness = new Thickness(1);
                TxtRisk.Text = loc.Get("Controls.MayHeat");
                TxtRisk.Foreground = ThemeColors.WarningBrush;
                RiskBadge.ToolTip = loc.Get("Controls.MayHeatTip");
                RiskBadge.Visibility = Visibility.Visible;
                break;
            default:
                RiskBadge.Visibility = Visibility.Collapsed;
                break;
        }
    }
    #endregion

    private void UpdateTooltip()
    {
        if (!string.IsNullOrEmpty(DetailedTooltip))
            ToolTip = DetailedTooltip;
    }

    public void ApplyRecommendedValue()
    {
        if (RecommendedValue.HasValue)
            IsChecked = RecommendedValue.Value;
    }
}
