// File: GameOptimizer.Shell/Controls/RecentActivityPanel.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Controls;

public class ActivityItem : INotifyPropertyChanged
{
    private string? _cachedTimeAgo;
    private DateTime _lastTimeAgoUpdate = DateTime.MinValue;
    
    public string Icon { get; set; } = IconGlyphs.Power;
    public string TitleKey { get; set; } = "";
    public string DescriptionKey { get; set; } = "";
    public object[]? TitleArgs { get; set; }
    public object[]? DescriptionArgs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool? IsSuccess { get; set; }

    public string Title => string.IsNullOrEmpty(TitleKey) ? "" : LocalizationService.Instance.Get(TitleKey, TitleArgs ?? Array.Empty<object>());
    public string Description => string.IsNullOrEmpty(DescriptionKey) ? "" : LocalizationService.Instance.Get(DescriptionKey, DescriptionArgs ?? Array.Empty<object>());

    // CTRL-018: 缓存TimeAgo计算结果，每30秒更新一次
    public string TimeAgo
    {
        get
        {
            if (_cachedTimeAgo != null && (DateTime.Now - _lastTimeAgoUpdate).TotalSeconds < 30)
                return _cachedTimeAgo;
            
            var loc = LocalizationService.Instance;
            var span = DateTime.Now - Timestamp;
            
            _cachedTimeAgo = span.TotalSeconds < 60 ? loc.Get("Controls.JustNow")
                : span.TotalMinutes < 60 ? loc.Get("Controls.MinutesAgo", (int)span.TotalMinutes)
                : span.TotalHours < 24 ? loc.Get("Controls.HoursAgo", (int)span.TotalHours)
                : Timestamp.ToString("MM-dd HH:mm");
            
            _lastTimeAgoUpdate = DateTime.Now;
            return _cachedTimeAgo;
        }
    }
    
    public void RefreshTimeAgo()
    {
        _cachedTimeAgo = null;
        OnPropertyChanged(nameof(TimeAgo));
    }
    
    public void NotifyLanguageChanged()
    {
        _cachedTimeAgo = null; // 清除缓存以便重新本地化
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(TimeAgo));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class RecentActivityPanel : UserControl
{
    // CTRL-019: 活动列表上限可配置
    public static readonly DependencyProperty MaxActivitiesProperty =
        DependencyProperty.Register(nameof(MaxActivities), typeof(int), typeof(RecentActivityPanel), new PropertyMetadata(50));
    
    public static readonly DependencyProperty ActivitiesProperty =
        DependencyProperty.Register(nameof(Activities), typeof(ObservableCollection<ActivityItem>), 
            typeof(RecentActivityPanel), new PropertyMetadata(null, OnActivitiesChanged));

    public static readonly DependencyProperty HasActivitiesProperty =
        DependencyProperty.Register(nameof(HasActivities), typeof(bool), typeof(RecentActivityPanel), new PropertyMetadata(false));

    private DispatcherTimer? _refreshTimer;

    // CTRL-019: 可配置的最大活动数量
    public int MaxActivities
    {
        get => (int)GetValue(MaxActivitiesProperty);
        set => SetValue(MaxActivitiesProperty, value);
    }

    public ObservableCollection<ActivityItem> Activities
    {
        get => (ObservableCollection<ActivityItem>)GetValue(ActivitiesProperty);
        set => SetValue(ActivitiesProperty, value);
    }

    public bool HasActivities
    {
        get => (bool)GetValue(HasActivitiesProperty);
        set => SetValue(HasActivitiesProperty, value);
    }

    public event EventHandler? ClearRequested;

    public RecentActivityPanel()
    {
        InitializeComponent();
        Activities = new ObservableCollection<ActivityItem>();
        Activities.CollectionChanged += (s, e) => UpdateHasActivities();
        
        // CTRL-018: 定时刷新TimeAgo显示
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += (s, e) => RefreshAllTimeAgo();
        _refreshTimer.Start();
        
        Unloaded += (s, e) => { _refreshTimer?.Stop(); _refreshTimer = null; };
    }
    
    private void RefreshAllTimeAgo()
    {
        if (Activities == null) return;
        foreach (var item in Activities)
            item.RefreshTimeAgo();
    }

    private static void OnActivitiesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RecentActivityPanel panel)
        {
            if (e.OldValue is ObservableCollection<ActivityItem> oldCol)
                oldCol.CollectionChanged -= panel.Activities_CollectionChanged;
            if (e.NewValue is ObservableCollection<ActivityItem> newCol)
                newCol.CollectionChanged += panel.Activities_CollectionChanged;
            panel.UpdateHasActivities();
        }
    }

    private void Activities_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => UpdateHasActivities();

    private void UpdateHasActivities() => HasActivities = Activities?.Count > 0;

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        ClearRequested?.Invoke(this, EventArgs.Empty);
        Activities?.Clear();
    }

    public void AddActivity(string titleKey, string descriptionKey, string icon = "\uE945", bool? isSuccess = null, object[]? titleArgs = null, object[]? descArgs = null)
    {
        var item = new ActivityItem
        {
            TitleKey = titleKey,
            DescriptionKey = descriptionKey,
            TitleArgs = titleArgs,
            DescriptionArgs = descArgs,
            Icon = icon,
            IsSuccess = isSuccess,
            Timestamp = DateTime.Now
        };

        Dispatcher.InvokeAsync(() =>
        {
            Activities.Insert(0, item);
            // CTRL-019: 使用可配置的MaxActivities
            while (Activities.Count > MaxActivities)
                Activities.RemoveAt(Activities.Count - 1);
        });
    }
}
