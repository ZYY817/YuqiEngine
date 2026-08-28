using System.Windows;

namespace GameOptimizer.Shell.Controls;

/// <summary>
/// Shared presentation states for controls that can represent an operation
/// without coupling the visual layer to a page or a business service.
/// </summary>
public enum ControlVisualState
{
    Default,
    Success,
    Loading,
    Error,
    Unauthorized,
    Recovering
}

public static class ControlStateProperties
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(ControlVisualState),
            typeof(ControlStateProperties),
            new FrameworkPropertyMetadata(ControlVisualState.Default, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetState(DependencyObject element, ControlVisualState value) =>
        element.SetValue(StateProperty, value);

    public static ControlVisualState GetState(DependencyObject element) =>
        (ControlVisualState)element.GetValue(StateProperty);
}
