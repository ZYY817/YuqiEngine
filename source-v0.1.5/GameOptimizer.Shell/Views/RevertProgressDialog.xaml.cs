// File: GameOptimizer.Shell/Views/RevertProgressDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Media.Animation;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Views;

public partial class RevertProgressDialog : Window
{
    private readonly RevertProgressInfo _progressInfo;
    private RestoreOperationType _operationType;

    public RevertProgressDialog()
    {
        InitializeComponent();
        _progressInfo = new RevertProgressInfo();
        _progressInfo.PropertyChanged += OnProgressInfoChanged;
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() => UpdateLocalizedTexts());
    }

    public void SetOperationType(RestoreOperationType operationType)
    {
        _operationType = operationType;
        UpdateLocalizedTexts();
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        TitleText.Text = _operationType == RestoreOperationType.RevertOptimizations
            ? loc.Get("RevertProgress.Title")
            : loc.Get("RestoreProgress.Title");
        
        ProcessingLabel.Text = loc.Get("RevertProgress.ProcessingLabel");
        HintText.Text = loc.Get("RevertProgress.Hint");
    }

    private void OnProgressInfoChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Why: Invoke 是同步阻塞，UpdateProgress 从后台线程调用时会与 UI 线程互等死锁。
        //      InvokeAsync 异步投递，不阻塞调用线程。
        _ = Dispatcher.InvokeAsync(() =>
        {
            var loc = LocalizationService.Instance;
            
            if (e.PropertyName == nameof(RevertProgressInfo.CurrentModuleName))
            {
                CurrentModuleText.Text = _progressInfo.CurrentModuleName;
            }
            else if (e.PropertyName == nameof(RevertProgressInfo.CompletedCount) || 
                     e.PropertyName == nameof(RevertProgressInfo.TotalCount))
            {
                ProgressText.Text = loc.Get("RevertProgress.Completed", 
                    _progressInfo.CompletedCount, _progressInfo.TotalCount);
            }
            else if (e.PropertyName == nameof(RevertProgressInfo.ProgressPercentage))
            {
                PercentageText.Text = $"{_progressInfo.ProgressPercentage:F0}%";
                
                var targetWidth = ActualWidth > 0 ? (ActualWidth - 64) * (_progressInfo.ProgressPercentage / 100.0) : 0;
                
                var animation = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                ProgressBar.BeginAnimation(WidthProperty, animation);
            }
        });
    }

    public void UpdateProgress(string moduleName, int completed, int total)
    {
        _progressInfo.CurrentModuleName = moduleName;
        _progressInfo.CompletedCount = completed;
        _progressInfo.TotalCount = total;
        _progressInfo.ProgressPercentage = total > 0 ? (completed * 100.0 / total) : 0;
    }

    protected override void OnClosed(EventArgs e)
    {
        _progressInfo.PropertyChanged -= OnProgressInfoChanged;
        base.OnClosed(e);
    }
}
