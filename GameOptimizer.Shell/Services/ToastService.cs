using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameOptimizer.Shell.Controls;

namespace GameOptimizer.Shell.Services;

public class ToastService
{
    private static ToastService? _instance;
    public static ToastService Instance => _instance ??= new ToastService();

    private Panel? _container;
    private readonly object _lock = new();
    private readonly List<ToastNotification> _activeToasts = new();
    private const int MaxToasts = 5;
    private const double ToastSpacing = 8;

    public void Initialize(Panel container) => _container = container;

    public void Show(string title, string message = "", ToastType type = ToastType.Info, int durationMs = 3000)
    {
        if (_container == null) return;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            lock (_lock)
            {
                if (_activeToasts.Count >= MaxToasts)
                {
                    var oldest = _activeToasts[0];
                    oldest.Hide();
                }
            }

            var toast = new ToastNotification
            {
                Title = title,
                Message = message,
                ToastType = type,
                Margin = new Thickness(0, 0, 0, ToastSpacing),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };

            toast.Closed += (s, e) =>
            {
                _container.Children.Remove(toast);
                lock (_lock)
                {
                    _activeToasts.Remove(toast);
                }
            };

            _container.Children.Add(toast);
            lock (_lock)
            {
                _activeToasts.Add(toast);
            }
            toast.Show();

            if (durationMs > 0)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    bool contains;
                    lock (_lock)
                    {
                        contains = _activeToasts.Contains(toast);
                    }
                    if (contains)
                        toast.Hide();
                };
                timer.Start();
            }
        });
    }

    public void Success(string title, string message = "") => Show(title, message, ToastType.Success);
    public void Error(string title, string message = "") => Show(title, message, ToastType.Error, 5000);
    public void Warning(string title, string message = "") => Show(title, message, ToastType.Warning, 4000);
    public void Info(string title, string message = "") => Show(title, message, ToastType.Info);
}
