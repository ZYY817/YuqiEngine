// File: GameOptimizer.Shell/Controls/GuideOverlayAdorner.cs
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Controls;

public class GuideOverlayAdorner : Adorner
{
    private FrameworkElement? _highlightTarget;
    private Rect _highlightRect;
    private readonly GuideTooltip _tooltip;
    private int _highlightPadding = 12;
    private GuideTooltipPosition _preferredPosition = GuideTooltipPosition.Auto;
    private bool _isTracking;
    private Window? _hostWindow;
    private bool _rootHandlersAttached;

    public GuideOverlayAdorner(UIElement adornedElement, GuideTooltip tooltip) : base(adornedElement)
    {
        _tooltip = tooltip;
        IsHitTestVisible = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _tooltip.UseLayoutRounding = true;
        _tooltip.SnapsToDevicePixels = true;

        AddVisualChild(_tooltip);
        AddLogicalChild(_tooltip);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _tooltip : throw new ArgumentOutOfRangeException(nameof(index));

    public void SetHighlightTarget(FrameworkElement? target, int padding = 12, GuideTooltipPosition position = GuideTooltipPosition.Auto)
    {
        if (!ReferenceEquals(_highlightTarget, target))
        {
            if (_highlightTarget != null)
            {
                _highlightTarget.LayoutUpdated -= OnTargetLayoutUpdated;
                _highlightTarget.SizeChanged -= OnTargetSizeChanged;
                _highlightTarget.IsVisibleChanged -= OnTargetVisibilityChanged;
            }

            _highlightTarget = target;

            if (_highlightTarget != null)
            {
                _highlightTarget.LayoutUpdated += OnTargetLayoutUpdated;
                _highlightTarget.SizeChanged += OnTargetSizeChanged;
                _highlightTarget.IsVisibleChanged += OnTargetVisibilityChanged;
            }
        }

        _highlightPadding = Math.Max(0, padding);
        _preferredPosition = position;

        if (_highlightTarget != null)
        {
            _highlightTarget.BringIntoView();
            StartTracking();
            AnimateToTarget();
            QueueRefreshAfterDelay(350);
        }
        else
        {
            _highlightRect = Rect.Empty;
            StopTracking();
        }

        InvalidateVisual();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void QueueRefreshAfterDelay(int delayMs)
    {
        if (_highlightTarget == null)
        {
            return;
        }

        _highlightTarget.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(delayMs);
            if (TryUpdateHighlightRect())
            {
                InvalidateVisual();
                InvalidateArrange();
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void StartTracking()
    {
        if (_isTracking)
        {
            return;
        }

        _isTracking = true;
        CompositionTarget.Rendering += OnRendering;
        AttachRootHandlers();
        AttachWindowHandlers();
    }

    public void StopTracking()
    {
        if (_isTracking)
        {
            _isTracking = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        DetachRootHandlers();
        DetachWindowHandlers();

        if (_highlightTarget != null)
        {
            _highlightTarget.LayoutUpdated -= OnTargetLayoutUpdated;
            _highlightTarget.SizeChanged -= OnTargetSizeChanged;
            _highlightTarget.IsVisibleChanged -= OnTargetVisibilityChanged;
            _highlightTarget = null;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_highlightTarget == null || !_highlightTarget.IsLoaded)
        {
            return;
        }

        if (TryUpdateHighlightRect())
        {
            InvalidateVisual();
            InvalidateArrange();
        }
    }

    private void OnTargetLayoutUpdated(object? sender, EventArgs e)
    {
        if (TryUpdateHighlightRect())
        {
            InvalidateVisual();
            InvalidateArrange();
        }
    }

    private void OnTargetSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (TryUpdateHighlightRect())
        {
            InvalidateVisual();
            InvalidateArrange();
        }
    }

    private void OnTargetVisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (TryUpdateHighlightRect())
        {
            InvalidateVisual();
            InvalidateArrange();
        }
    }

    private void OnRootLayoutUpdated(object? sender, EventArgs e)
    {
        if (_highlightTarget == null)
        {
            return;
        }

        if (TryUpdateHighlightRect())
        {
            InvalidateVisual();
            InvalidateArrange();
        }
    }

    private void OnRootMetricsChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_highlightTarget == null)
        {
            return;
        }

        if (TryUpdateHighlightRect())
        {
            InvalidateVisual();
            InvalidateArrange();
        }
    }

    private void AttachWindowHandlers()
    {
        if (AdornedElement is not DependencyObject element)
        {
            return;
        }

        var hostWindow = Window.GetWindow(element);
        if (hostWindow == null || ReferenceEquals(_hostWindow, hostWindow))
        {
            return;
        }

        DetachWindowHandlers();
        _hostWindow = hostWindow;
        _hostWindow.SizeChanged += OnWindowMetricsChanged;
        _hostWindow.StateChanged += OnWindowMetricsChanged;
        _hostWindow.LocationChanged += OnWindowLocationChanged;
    }

    private void AttachRootHandlers()
    {
        if (_rootHandlersAttached || AdornedElement is not FrameworkElement root)
        {
            return;
        }

        root.SizeChanged += OnRootMetricsChanged;
        root.LayoutUpdated += OnRootLayoutUpdated;
        _rootHandlersAttached = true;
    }

    private void DetachRootHandlers()
    {
        if (!_rootHandlersAttached || AdornedElement is not FrameworkElement root)
        {
            return;
        }

        root.SizeChanged -= OnRootMetricsChanged;
        root.LayoutUpdated -= OnRootLayoutUpdated;
        _rootHandlersAttached = false;
    }

    private void DetachWindowHandlers()
    {
        if (_hostWindow == null)
        {
            return;
        }

        _hostWindow.SizeChanged -= OnWindowMetricsChanged;
        _hostWindow.StateChanged -= OnWindowMetricsChanged;
        _hostWindow.LocationChanged -= OnWindowLocationChanged;
        _hostWindow = null;
    }

    private void OnWindowMetricsChanged(object? sender, EventArgs e)
    {
        if (TryUpdateHighlightRect())
        {
            InvalidateVisual();
            InvalidateArrange();
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (TryUpdateHighlightRect())
        {
            InvalidateVisual();
            InvalidateArrange();
        }
    }

    private bool TryUpdateHighlightRect()
    {
        var previous = _highlightRect;
        _highlightRect = CalculateHighlightRect();
        return !AreRectsSimilar(previous, _highlightRect);
    }

    private Rect CalculateHighlightRect()
    {
        if (_highlightTarget == null ||
            !_highlightTarget.IsLoaded ||
            !_highlightTarget.IsVisible ||
            _highlightTarget.ActualWidth < 1 ||
            _highlightTarget.ActualHeight < 1 ||
            AdornedElement is not Visual rootVisual)
        {
            return Rect.Empty;
        }

        try
        {
            var bounds = _highlightTarget.TransformToVisual(rootVisual).TransformBounds(
                new Rect(0, 0, _highlightTarget.ActualWidth, _highlightTarget.ActualHeight));

            if (bounds.Width <= 0 || bounds.Height <= 0 || double.IsNaN(bounds.X) || double.IsNaN(bounds.Y))
            {
                return Rect.Empty;
            }

            bounds.Inflate(_highlightPadding, _highlightPadding);
            var container = new Rect(0, 0, AdornedElement.RenderSize.Width, AdornedElement.RenderSize.Height);
            if (container.Width <= 0 || container.Height <= 0 || !container.IntersectsWith(bounds))
            {
                return Rect.Empty;
            }

            bounds = Rect.Intersect(bounds, container);
            return SnapRect(bounds);
        }
        catch
        {
            return Rect.Empty;
        }
    }

    private void AnimateToTarget()
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, animation);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var renderSize = AdornedElement.RenderSize;
        var fullRect = new Rect(renderSize);
        var overlayBrush = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0));

        if (_highlightTarget == null || _highlightRect.IsEmpty)
        {
            dc.DrawRectangle(overlayBrush, null, fullRect);
            return;
        }

        var fullGeometry = new RectangleGeometry(fullRect);
        var highlightGeometry = new RectangleGeometry(_highlightRect, 8, 8);
        var clipGeometry = Geometry.Combine(fullGeometry, highlightGeometry, GeometryCombineMode.Exclude, null);

        dc.PushClip(clipGeometry);
        dc.DrawRectangle(overlayBrush, null, fullRect);
        dc.Pop();

        var spotlightBrush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(70, 255, 255, 255), 0.0),
                new(Colors.Transparent, 1.0)
            }
        };
        dc.DrawRectangle(
            spotlightBrush,
            null,
            SnapRect(new Rect(
                _highlightRect.X - 20,
                _highlightRect.Y - 20,
                _highlightRect.Width + 40,
                _highlightRect.Height + 40)));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _tooltip.Measure(constraint);
        return constraint;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_highlightTarget != null)
        {
            TryUpdateHighlightRect();
        }

        var tooltipSize = _tooltip.DesiredSize;
        if (tooltipSize.Width <= 0 || tooltipSize.Height <= 0)
        {
            tooltipSize = new Size(360, 220);
        }

        var position = CalculateTooltipPosition(finalSize, tooltipSize);
        var snappedPosition = SnapPoint(position);
        var snappedSize = SnapSize(tooltipSize);
        _tooltip.Arrange(new Rect(snappedPosition, snappedSize));
        return finalSize;
    }

    private Point CalculateTooltipPosition(Size containerSize, Size tooltipSize)
    {
        if (_highlightRect.IsEmpty)
        {
            var center = new Point(
                (containerSize.Width - tooltipSize.Width) / 2,
                (containerSize.Height - tooltipSize.Height) / 2);
            return Clamp(center.X, center.Y, tooltipSize.Width, tooltipSize.Height, containerSize, 20);
        }

        var tw = tooltipSize.Width > 0 ? tooltipSize.Width : 340;
        var th = tooltipSize.Height > 0 ? tooltipSize.Height : 200;
        const double gap = 20;

        var spaceBelow = containerSize.Height - _highlightRect.Bottom;
        var spaceAbove = _highlightRect.Top;
        var spaceRight = containerSize.Width - _highlightRect.Right;
        var spaceLeft = _highlightRect.Left;

        double x;
        double y;

        switch (_preferredPosition)
        {
            case GuideTooltipPosition.Bottom when spaceBelow >= th + gap:
                x = _highlightRect.Left + (_highlightRect.Width - tw) / 2;
                y = _highlightRect.Bottom + gap;
                return Clamp(x, y, tw, th, containerSize, gap);

            case GuideTooltipPosition.Top when spaceAbove >= th + gap:
                x = _highlightRect.Left + (_highlightRect.Width - tw) / 2;
                y = _highlightRect.Top - th - gap;
                return Clamp(x, y, tw, th, containerSize, gap);

            case GuideTooltipPosition.Right when spaceRight >= tw + gap:
                x = _highlightRect.Right + gap;
                y = _highlightRect.Top + (_highlightRect.Height - th) / 2;
                return Clamp(x, y, tw, th, containerSize, gap);

            case GuideTooltipPosition.Left when spaceLeft >= tw + gap:
                x = _highlightRect.Left - tw - gap;
                y = _highlightRect.Top + (_highlightRect.Height - th) / 2;
                return Clamp(x, y, tw, th, containerSize, gap);
        }

        if (spaceBelow >= th + gap)
        {
            x = _highlightRect.Left + (_highlightRect.Width - tw) / 2;
            y = _highlightRect.Bottom + gap;
        }
        else if (spaceAbove >= th + gap)
        {
            x = _highlightRect.Left + (_highlightRect.Width - tw) / 2;
            y = _highlightRect.Top - th - gap;
        }
        else if (spaceRight >= tw + gap)
        {
            x = _highlightRect.Right + gap;
            y = _highlightRect.Top + (_highlightRect.Height - th) / 2;
        }
        else if (spaceLeft >= tw + gap)
        {
            x = _highlightRect.Left - tw - gap;
            y = _highlightRect.Top + (_highlightRect.Height - th) / 2;
        }
        else
        {
            x = (containerSize.Width - tw) / 2;
            y = (containerSize.Height - th) / 2;
        }

        return Clamp(x, y, tw, th, containerSize, gap);
    }

    private Point Clamp(double x, double y, double tw, double th, Size container, double gap)
    {
        if (container.Width <= tw + gap * 2)
        {
            x = (container.Width - tw) / 2;
        }
        else
        {
            x = Math.Max(gap, Math.Min(x, container.Width - tw - gap));
        }

        if (container.Height <= th + gap * 2)
        {
            y = (container.Height - th) / 2;
        }
        else
        {
            y = Math.Max(gap, Math.Min(y, container.Height - th - gap));
        }

        return SnapPoint(new Point(x, y));
    }

    private Point SnapPoint(Point point)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(
            SnapValue(point.X, dpi.DpiScaleX),
            SnapValue(point.Y, dpi.DpiScaleY));
    }

    private Size SnapSize(Size size)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Size(
            Math.Max(1, SnapValue(size.Width, dpi.DpiScaleX)),
            Math.Max(1, SnapValue(size.Height, dpi.DpiScaleY)));
    }

    private Rect SnapRect(Rect rect)
    {
        var topLeft = SnapPoint(rect.TopLeft);
        var bottomRight = SnapPoint(rect.BottomRight);
        var width = Math.Max(1, bottomRight.X - topLeft.X);
        var height = Math.Max(1, bottomRight.Y - topLeft.Y);
        return new Rect(topLeft, new Size(width, height));
    }

    private static double SnapValue(double value, double dpiScale)
    {
        if (dpiScale <= 0)
        {
            dpiScale = 1;
        }

        return Math.Round(value * dpiScale) / dpiScale;
    }

    private static bool AreRectsSimilar(Rect left, Rect right)
    {
        const double tolerance = 0.1;
        return Math.Abs(left.X - right.X) < tolerance &&
               Math.Abs(left.Y - right.Y) < tolerance &&
               Math.Abs(left.Width - right.Width) < tolerance &&
               Math.Abs(left.Height - right.Height) < tolerance;
    }
}
