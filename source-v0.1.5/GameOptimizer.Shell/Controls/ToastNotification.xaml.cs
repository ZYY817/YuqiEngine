// File: GameOptimizer.Shell/Controls/ToastNotification.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GameOptimizer.Shell.Controls;

public enum ToastType { Info, Success, Warning, Error }

public partial class ToastNotification : UserControl
{
    // CTRL-023: 自动消失定时器
    private DispatcherTimer? _autoHideTimer;
    
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ToastNotification), new PropertyMetadata(""));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(ToastNotification), new PropertyMetadata("", OnMessageChanged));

    public static readonly DependencyProperty ToastTypeProperty =
        DependencyProperty.Register(nameof(ToastType), typeof(ToastType), typeof(ToastNotification), new PropertyMetadata(ToastType.Info));

    public static readonly DependencyProperty HasMessageProperty =
        DependencyProperty.Register(nameof(HasMessage), typeof(bool), typeof(ToastNotification), new PropertyMetadata(false));

    // CTRL-023: 自动消失时间（毫秒），0表示不自动消失
    public static readonly DependencyProperty AutoHideDurationProperty =
        DependencyProperty.Register(nameof(AutoHideDuration), typeof(int), typeof(ToastNotification), new PropertyMetadata(3000));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public ToastType ToastType { get => (ToastType)GetValue(ToastTypeProperty); set => SetValue(ToastTypeProperty, value); }
    public bool HasMessage { get => (bool)GetValue(HasMessageProperty); set => SetValue(HasMessageProperty, value); }
    public int AutoHideDuration { get => (int)GetValue(AutoHideDurationProperty); set => SetValue(AutoHideDurationProperty, value); }

    public event EventHandler? Closed;

    public ToastNotification()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoHideTimer();
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotification toast)
            toast.HasMessage = !string.IsNullOrEmpty(e.NewValue as string);
    }

    public void Show()
    {
        var storyboard = (Storyboard)FindResource("SlideIn");
        storyboard.Begin();
        
        // CTRL-023: 启动自动消失定时器
        StartAutoHideTimer();
    }

    public void Hide()
    {
        StopAutoHideTimer();
        
        var storyboard = (Storyboard)FindResource("SlideOut");
        // CTRL-022: 使用局部变量避免重复订阅，并在完成后取消订阅
        EventHandler? completedHandler = null;
        completedHandler = (s, e) =>
        {
            storyboard.Completed -= completedHandler;
            Closed?.Invoke(this, EventArgs.Empty);
        };
        storyboard.Completed += completedHandler;
        storyboard.Begin();
    }
    
    // CTRL-023: 自动消失机制
    private void StartAutoHideTimer()
    {
        StopAutoHideTimer();
        
        if (AutoHideDuration <= 0) return;
        
        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AutoHideDuration)
        };
        _autoHideTimer.Tick += OnAutoHideTimerTick;
        _autoHideTimer.Start();
    }
    
    private void StopAutoHideTimer()
    {
        if (_autoHideTimer == null) return;
        _autoHideTimer.Stop();
        _autoHideTimer.Tick -= OnAutoHideTimerTick;
        _autoHideTimer = null;
    }
    
    private void OnAutoHideTimerTick(object? sender, EventArgs e)
    {
        Hide();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();
}
