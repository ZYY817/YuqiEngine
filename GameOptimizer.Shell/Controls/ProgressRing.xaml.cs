using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameOptimizer.Shell.Controls;

public partial class ProgressRing : UserControl
{
    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(ProgressRing),
            new PropertyMetadata(40.0, OnSizeChanged));

    public static readonly DependencyProperty StrokeWidthProperty =
        DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(ProgressRing),
            new PropertyMetadata(4.0, OnSizeChanged));

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(ProgressRing),
            new PropertyMetadata(true, OnModeChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(ProgressRing),
            new PropertyMetadata(0.0, OnProgressChanged));

    public static readonly DependencyProperty RingBrushProperty =
        DependencyProperty.Register(nameof(RingBrush), typeof(Brush), typeof(ProgressRing),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(77, 102, 255))));

    public static readonly DependencyProperty CenterContentProperty =
        DependencyProperty.Register(nameof(CenterContent), typeof(object), typeof(ProgressRing), new PropertyMetadata(null));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush RingBrush
    {
        get => (Brush)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public ProgressRing()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateArc();
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressRing ring) ring.UpdateArc();
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressRing ring) ring.UpdateArc();
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressRing ring && !ring.IsIndeterminate) ring.UpdateArc();
    }

    private void UpdateArc()
    {
        var radius = (Size - StrokeWidth) / 2;
        var center = Size / 2;
        var angle = IsIndeterminate ? 270 : Progress * 3.6;
        angle = Math.Min(angle, 359.9);

        var startAngle = -90;
        var endAngle = startAngle + angle;

        var startRad = startAngle * Math.PI / 180;
        var endRad = endAngle * Math.PI / 180;

        var startX = center + radius * Math.Cos(startRad);
        var startY = center + radius * Math.Sin(startRad);
        var endX = center + radius * Math.Cos(endRad);
        var endY = center + radius * Math.Sin(endRad);

        var largeArc = angle > 180;

        var pathData = $"M {startX:F2},{startY:F2} A {radius:F2},{radius:F2} 0 {(largeArc ? 1 : 0)} 1 {endX:F2},{endY:F2}";
        
        try
        {
            ProgressArc.Data = Geometry.Parse(pathData);
        }
        catch (Exception ex)
        {
            // CTRL-017: 记录解析错误而非静默吞没
            System.Diagnostics.Debug.WriteLine($"[ProgressRing] Geometry parse error: {ex.Message}, pathData: {pathData}");
        }
    }
}
