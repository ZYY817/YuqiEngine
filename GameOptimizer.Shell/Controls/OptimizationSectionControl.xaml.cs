using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Controls;

public partial class OptimizationSectionControl : UserControl
{
    public OptimizationSectionControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(OptimizationSectionControl), new PropertyMetadata("优化区域"));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(OptimizationSectionControl), new PropertyMetadata("\xE7FC"));

    public static readonly DependencyProperty IconBackgroundProperty = DependencyProperty.Register(
        nameof(IconBackground), typeof(Brush), typeof(OptimizationSectionControl), 
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x4D, 0x66, 0xFF))));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(OptimizationSectionControl), new PropertyMetadata(null));

    public new static readonly DependencyProperty MaxHeightProperty = DependencyProperty.Register(
        nameof(MaxHeight), typeof(double), typeof(OptimizationSectionControl), new PropertyMetadata(520.0));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Brush IconBackground { get => (Brush)GetValue(IconBackgroundProperty); set => SetValue(IconBackgroundProperty, value); }
    public IEnumerable ItemsSource { get => (IEnumerable)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public new double MaxHeight { get => (double)GetValue(MaxHeightProperty); set => SetValue(MaxHeightProperty, value); }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsSource == null) return;
        foreach (var item in ItemsSource)
        {
            if (item is GameModeOptimizationItem opt) opt.IsSelected = true;
        }
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsSource == null) return;
        foreach (var item in ItemsSource)
        {
            if (item is GameModeOptimizationItem opt) opt.IsSelected = false;
        }
    }
}
