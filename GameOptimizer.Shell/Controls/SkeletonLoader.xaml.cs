using System.Windows;
using System.Windows.Controls;

namespace GameOptimizer.Shell.Controls;

public partial class SkeletonLoader : UserControl
{
    public static readonly DependencyProperty SkeletonWidthProperty =
        DependencyProperty.Register(nameof(SkeletonWidth), typeof(double), typeof(SkeletonLoader),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty SkeletonHeightProperty =
        DependencyProperty.Register(nameof(SkeletonHeight), typeof(double), typeof(SkeletonLoader),
            new PropertyMetadata(20.0));

    public static readonly DependencyProperty SkeletonRadiusProperty =
        DependencyProperty.Register(nameof(SkeletonRadius), typeof(CornerRadius), typeof(SkeletonLoader),
            new PropertyMetadata(new CornerRadius(6)));

    public double SkeletonWidth
    {
        get => (double)GetValue(SkeletonWidthProperty);
        set => SetValue(SkeletonWidthProperty, value);
    }

    public double SkeletonHeight
    {
        get => (double)GetValue(SkeletonHeightProperty);
        set => SetValue(SkeletonHeightProperty, value);
    }

    public CornerRadius SkeletonRadius
    {
        get => (CornerRadius)GetValue(SkeletonRadiusProperty);
        set => SetValue(SkeletonRadiusProperty, value);
    }

    public SkeletonLoader()
    {
        InitializeComponent();
    }
}
