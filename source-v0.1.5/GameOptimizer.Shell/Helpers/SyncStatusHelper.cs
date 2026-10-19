// File: GameOptimizer.Shell/Helpers/SyncStatusHelper.cs
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Threading;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Helpers;

public enum SyncState { Idle, Syncing, Completed, Failed }

public sealed class SyncStatusHelper : IDisposable
{
    private readonly Ellipse? _statusDot;
    private readonly TextBlock? _statusText;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _resetTimer;
    private bool _disposed;

    public SyncState CurrentState { get; private set; } = SyncState.Idle;

    public SyncStatusHelper(Ellipse? statusDot, TextBlock? statusText, Dispatcher dispatcher)
    {
        _statusDot = statusDot;
        _statusText = statusText;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void BeginSync()
    {
        if (_disposed) return;
        if (_statusDot == null && _statusText == null) return;

        StopResetTimer();
        CurrentState = SyncState.Syncing;

        var text = LocalizationService.Instance.Get("Sync.Status.Syncing");
        if (_statusText != null) _statusText.Text = text;

        StartPulseAnimation();
    }

    public void OnSyncCompleted()
    {
        if (_disposed) return;
        if (_statusDot == null && _statusText == null) return;

        StopPulseAnimation();
        CurrentState = SyncState.Completed;

        if (_statusDot != null) _statusDot.Fill = ThemeColors.SuccessLightBrush;
        var text = LocalizationService.Instance.Get("Sync.Status.Completed");
        if (_statusText != null) _statusText.Text = text;

        ScheduleResetToIdle(TimeSpan.FromSeconds(2));
    }

    public void OnSyncFailed()
    {
        if (_disposed) return;
        if (_statusDot == null && _statusText == null) return;

        StopPulseAnimation();
        CurrentState = SyncState.Failed;

        if (_statusDot != null) _statusDot.Fill = ThemeColors.DangerBrush;
        var text = LocalizationService.Instance.Get("Sync.Status.Failed");
        if (_statusText != null) _statusText.Text = text;

        ScheduleResetToIdle(TimeSpan.FromSeconds(3));
    }

    // Why: 语言切换时需要刷新当前状态对应的文本
    public void RefreshLocalizedText()
    {
        if (_disposed) return;
        var loc = LocalizationService.Instance;
        var key = CurrentState switch
        {
            SyncState.Idle => "Module.SystemReady",
            SyncState.Syncing => "Sync.Status.Syncing",
            SyncState.Completed => "Sync.Status.Completed",
            SyncState.Failed => "Sync.Status.Failed",
            _ => "Module.SystemReady"
        };
        if (_statusText != null) _statusText.Text = loc.Get(key);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopResetTimer();
        StopPulseAnimation();
    }

    private void ResetToIdle()
    {
        if (_disposed) return;
        CurrentState = SyncState.Idle;

        if (_statusDot != null) _statusDot.Fill = ThemeColors.SuccessLightBrush;
        var text = LocalizationService.Instance.Get("Module.SystemReady");
        if (_statusText != null) _statusText.Text = text;
    }

    private void ScheduleResetToIdle(TimeSpan delay)
    {
        StopResetTimer();
        _resetTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = delay
        };
        _resetTimer.Tick += (_, _) =>
        {
            _resetTimer?.Stop();
            ResetToIdle();
        };
        _resetTimer.Start();
    }

    private void StopResetTimer()
    {
        if (_resetTimer == null) return;
        _resetTimer.Stop();
        _resetTimer = null;
    }

    private void StartPulseAnimation()
    {
        if (_statusDot == null) return;

        // Why: 需要非冻结的 Brush 才能做动画，冻结的 Brush 不可变
        var animBrush = new SolidColorBrush(ThemeColors.Accent);
        _statusDot.Fill = animBrush;

        var pulse = new ColorAnimation
        {
            From = ThemeColors.Accent,
            To = ThemeColors.Info,
            Duration = TimeSpan.FromMilliseconds(800),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        animBrush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);
    }

    private void StopPulseAnimation()
    {
        if (_statusDot?.Fill is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        }
    }
}
