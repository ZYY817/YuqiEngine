// File: GameOptimizer.Shell/Controls/HardwareDnaCard.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GameOptimizer.Shell.Services;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Controls;

public partial class HardwareDnaCard : UserControl
{
    public static readonly DependencyProperty HardwareTypeProperty =
        DependencyProperty.Register(nameof(HardwareType), typeof(HardwareCardType), typeof(HardwareDnaCard),
            new PropertyMetadata(HardwareCardType.IntelCpu, OnHardwareTypeChanged));

    public static readonly DependencyProperty ModelNameProperty =
        DependencyProperty.Register(nameof(ModelName), typeof(string), typeof(HardwareDnaCard),
            new PropertyMetadata(null, OnModelNameChanged));

    public static readonly DependencyProperty OptimizedCountProperty =
        DependencyProperty.Register(nameof(OptimizedCount), typeof(int), typeof(HardwareDnaCard),
            new PropertyMetadata(0, OnOptimizedCountChanged));

    public static readonly DependencyProperty FeatureTagProperty =
        DependencyProperty.Register(nameof(FeatureTag), typeof(string), typeof(HardwareDnaCard),
            new PropertyMetadata(null, OnFeatureTagChanged));

    public HardwareCardType HardwareType { get => (HardwareCardType)GetValue(HardwareTypeProperty); set => SetValue(HardwareTypeProperty, value); }
    public string ModelName { get => (string)GetValue(ModelNameProperty); set => SetValue(ModelNameProperty, value); }
    public int OptimizedCount { get => (int)GetValue(OptimizedCountProperty); set => SetValue(OptimizedCountProperty, value); }
    public string? FeatureTag { get => (string?)GetValue(FeatureTagProperty); set => SetValue(FeatureTagProperty, value); }

    public HardwareDnaCard()
    {
        InitializeComponent();
        UpdateVisuals();
        
        // CTRL-010: 订阅语言变更事件
        Loaded += (s, e) => LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (s, e) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
    
    // CTRL-010: 语言变更时刷新本地化文本
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            UpdateModelText();
            UpdateStatusText();
        });
    }

    private static void OnHardwareTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => (d as HardwareDnaCard)?.UpdateVisuals();
    private static void OnModelNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => (d as HardwareDnaCard)?.UpdateModelText();
    private static void OnOptimizedCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => (d as HardwareDnaCard)?.UpdateStatusText();
    private static void OnFeatureTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => (d as HardwareDnaCard)?.UpdateFeatureTag();

    private void UpdateVisuals()
    {
        var (logo, vendor, brandColor) = HardwareType switch
        {
            HardwareCardType.IntelCpu => ("I", "INTEL", Color.FromRgb(0x00, 0x68, 0xB5)),
            HardwareCardType.AmdCpu => ("A", "AMD", Color.FromRgb(0xED, 0x1C, 0x24)),
            HardwareCardType.NvidiaGpu => ("N", "NVIDIA", Color.FromRgb(0x76, 0xB9, 0x00)),
            HardwareCardType.AmdGpu => ("R", "RADEON", Color.FromRgb(0xED, 0x1C, 0x24)),
            _ => ("?", "UNKNOWN", Color.FromRgb(0x71, 0x71, 0x7A))
        };
        LogoText.Text = logo;
        VendorText.Text = vendor;
        LogoGrad1.Color = Color.FromArgb(0x50, brandColor.R, brandColor.G, brandColor.B);
        LogoGrad2.Color = Color.FromArgb(0x20, brandColor.R, brandColor.G, brandColor.B);
        UpdateModelText();
        UpdateStatusText();
        UpdateFeatureTag();
    }

    private void UpdateModelText()
    {
        var loc = LocalizationService.Instance;
        ModelText.Text = string.IsNullOrEmpty(ModelName) ? loc.Get("Controls.NotDetected") : ModelName;
    }

    private void UpdateStatusText()
    {
        var loc = LocalizationService.Instance;
        var hasOptimized = OptimizedCount > 0;
        StatusText.Text = hasOptimized ? loc.Get("Controls.Optimized", OptimizedCount) : loc.Get("Controls.ToOptimize");
        var statusColor = hasOptimized ? Color.FromRgb(0x4A, 0xDE, 0x80) : Color.FromRgb(0x71, 0x71, 0x7A);
        StatusText.Foreground = new SolidColorBrush(statusColor);
        StatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x15, statusColor.R, statusColor.G, statusColor.B));
        StatusBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, statusColor.R, statusColor.G, statusColor.B));
        StatusLed.Fill = new SolidColorBrush(statusColor);
        if (StatusLed.Effect is DropShadowEffect glow) glow.Color = statusColor;
    }

    private void UpdateFeatureTag()
    {
        FeatureBadge.Visibility = string.IsNullOrEmpty(FeatureTag) ? Visibility.Collapsed : Visibility.Visible;
        FeatureText.Text = FeatureTag ?? "";
    }
}

public enum HardwareCardType { IntelCpu, AmdCpu, NvidiaGpu, AmdGpu }