using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Controls;

public partial class OptimizationCardControl : UserControl
{
    public OptimizationCardControl()
    {
        System.Diagnostics.Debug.WriteLine("[OptimizationCardControl] Constructor start");
        try
        {
            InitializeComponent();
            System.Diagnostics.Debug.WriteLine("[OptimizationCardControl] InitializeComponent completed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OptimizationCardControl] Init FAILED: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    public static readonly RoutedEvent CardClickedEvent = EventManager.RegisterRoutedEvent(
        "CardClicked", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OptimizationCardControl));

    public event RoutedEventHandler CardClicked
    {
        add => AddHandler(CardClickedEvent, value);
        remove => RemoveHandler(CardClickedEvent, value);
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GameModeOptimizationItem item)
        {
            item.IsSelected = !item.IsSelected;
            RaiseEvent(new RoutedEventArgs(CardClickedEvent, this));
        }
    }
}
