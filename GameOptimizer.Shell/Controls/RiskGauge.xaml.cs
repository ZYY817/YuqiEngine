using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Controls;

public partial class RiskGauge : UserControl
{
    public static readonly DependencyProperty RiskValueProperty =
        DependencyProperty.Register(nameof(RiskValue), typeof(int), typeof(RiskGauge),
            new PropertyMetadata(0, OnRiskValueChanged));

    public static readonly DependencyProperty ModifiedCountProperty =
        DependencyProperty.Register(nameof(ModifiedCount), typeof(int), typeof(RiskGauge),
            new PropertyMetadata(0, OnModifiedCountChanged));

    public int RiskValue { get => (int)GetValue(RiskValueProperty); set => SetValue(RiskValueProperty, value); }
    public int ModifiedCount { get => (int)GetValue(ModifiedCountProperty); set => SetValue(ModifiedCountProperty, value); }

    private bool _isInitialized;

    public RiskGauge()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        UpdateLocalizedTexts();
        UpdateDisplay();
        UpdateModifiedCount();
    }

    private void UpdateLocalizedTexts()
    {
        TxtTitle.Text = LocalizationService.Instance.Get("PowerEditor.RiskAssessment");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isInitialized = true;
        UpdateLocalizedTexts();
        UpdateDisplay();
        UpdateModifiedCount();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInitialized) UpdateIndicatorPosition();
    }

    private static void OnRiskValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RiskGauge gauge && gauge._isInitialized)
            gauge.UpdateDisplay();
    }

    private static void OnModifiedCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RiskGauge gauge && gauge._isInitialized)
            gauge.UpdateModifiedCount();
    }

    private void UpdateDisplay()
    {
        var loc = LocalizationService.Instance;
        var risk = Math.Clamp(RiskValue, 0, 100);
        var hasModifications = ModifiedCount > 0;
        
        // 根据是否有修改来决定显示模式
        if (!hasModifications && risk == 0)
        {
            // 初始状态：友好提示
            TxtInitialState.Visibility = Visibility.Visible;
            TxtInitialState.Text = loc.Get("Controls.DefaultSettings");
            RiskValuePanel.Visibility = Visibility.Collapsed;
            TxtRiskLevel.Text = loc.Get("Controls.LevelSafe");
            TxtRiskLevel.Foreground = ThemeColors.EmeraldBrush;
            TxtIcon.Text = IconGlyphs.Success;
            return;
        }
        
        // 有修改时显示风险值
        TxtInitialState.Visibility = Visibility.Collapsed;
        RiskValuePanel.Visibility = Visibility.Visible;
        TxtRiskValue.Text = risk.ToString();
        
        // 更新颜色和文字 - 使用更友好的描述
        Color color;
        string levelText;
        string icon;
        
        if (risk < 20)
        {
            color = ThemeColors.Emerald; // 绿色
            levelText = loc.Get("Controls.LevelSafe");
            icon = IconGlyphs.Success;
        }
        else if (risk < 40)
        {
            color = Color.FromRgb(0x84, 0xCC, 0x16); // 黄绿色
            levelText = loc.Get("Controls.LevelLight");
            icon = IconGlyphs.RiskLow;
        }
        else if (risk < 60)
        {
            color = Color.FromRgb(0xF5, 0x9E, 0x0B); // 黄色
            levelText = loc.Get("Controls.LevelPerf");
            icon = IconGlyphs.Power;
        }
        else if (risk < 80)
        {
            color = Color.FromRgb(0xF9, 0x73, 0x16); // 橙色
            levelText = loc.Get("Controls.LevelHigh");
            icon = IconGlyphs.RiskMedium;
        }
        else
        {
            color = Color.FromRgb(0xEF, 0x44, 0x44); // 红色
            levelText = loc.Get("Controls.LevelExtreme");
            icon = IconGlyphs.RiskHigh;
        }
        
        TxtRiskLevel.Text = levelText;
        TxtRiskLevel.Foreground = new SolidColorBrush(color);
        TxtRiskValue.Foreground = new SolidColorBrush(color);
        TxtIcon.Text = icon;
        
        UpdateIndicatorPosition();
    }

    private void UpdateIndicatorPosition()
    {
        var parent = Indicator.Parent as Canvas;
        if (parent == null) return;
        
        var ratio = Math.Clamp(RiskValue / 100.0, 0, 1);
        var maxWidth = parent.ActualWidth > 0 ? parent.ActualWidth - 4 : 200;
        var targetX = ratio * maxWidth;
        
        // 防止 NaN 导致动画崩溃
        if (double.IsNaN(targetX) || double.IsInfinity(targetX))
            targetX = 0;
        
        // 获取当前位置，如果是 NaN 则使用 0
        var currentLeft = Canvas.GetLeft(Indicator);
        if (double.IsNaN(currentLeft))
        {
            Canvas.SetLeft(Indicator, 0);
            currentLeft = 0;
        }
        
        var animation = new DoubleAnimation(currentLeft, targetX, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        Indicator.BeginAnimation(Canvas.LeftProperty, animation);
    }

    private void UpdateModifiedCount()
    {
        if (ModifiedCount > 0)
        {
            var loc = LocalizationService.Instance;
            TxtModifiedCount.Text = loc.Get("Controls.ModifiedCount", ModifiedCount);
            ModifiedCountBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ModifiedCountBadge.Visibility = Visibility.Collapsed;
        }
    }

    public void AnimateRiskChange(int newValue)
    {
        var animation = new Int32Animation(RiskValue, newValue, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        animation.CurrentTimeInvalidated += (s, e) =>
        {
            if (s is AnimationClock clock && clock.CurrentProgress.HasValue)
            {
                var progress = clock.CurrentProgress.Value;
                var currentValue = (int)(RiskValue + (newValue - RiskValue) * progress);
                TxtRiskValue.Text = currentValue.ToString();
            }
        };
        
        BeginAnimation(RiskValueProperty, animation);
    }
}
