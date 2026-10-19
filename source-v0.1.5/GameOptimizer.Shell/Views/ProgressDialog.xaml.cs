// File: GameOptimizer.Shell/Views/ProgressDialog.xaml.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Views
{
    public partial class ProgressDialog : Window, INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _subtitle = string.Empty;
        private string _message = string.Empty;
        private double _progressPercentage;
        private bool _isIndeterminate = true;
        private string _progressText = string.Empty;
        private string _currentOperationTitle = string.Empty;
        private string _currentOperationCategory = string.Empty;
        private string _categoryIcon = "\uE946";
        private string _estimatedTimeText = string.Empty;
        private string _tipText = string.Empty;
        private bool _showCurrentOperation;
        private bool _showEstimatedTime;
        private bool _showMessage = true;
        private bool _showCancelButton;
        private bool _showSubtitle;
        private bool _showPercentage = true;
        private bool _showTip;
        private CancellationTokenSource? _cancellationTokenSource;
        
        private readonly Stopwatch _stopwatch = new();
        private int _totalItems;
        private int _completedItems;
        private DateTime _startTime;

        public ProgressDialog()
        {
            InitializeComponent();
            DataContext = this;
            
            UpdateLocalizedTexts();
            
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            Unloaded += (s, e) =>
            {
                LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            };
            
            Loaded += OnLoaded;
        }
        
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _startTime = DateTime.Now;
            _stopwatch.Start();
            
            // 同时播放淡入和滑入动画以获得最佳视觉效果
            var fadeIn = (Storyboard)Resources["FadeInAnimation"];
            var slideIn = (Storyboard)Resources["SlideInAnimation"];
            
            fadeIn?.Begin(this);
            slideIn?.Begin(this);
        }
        
        private void OnLanguageChanged(object? sender, string newLanguage)
        {
            _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
        }
        
        private void UpdateLocalizedTexts()
        {
            var loc = LocalizationService.Instance;
            CurrentOperationLabel = loc.Get("Progress.CurrentOperation");
            EstimatedTimeLabel = loc.Get("Progress.EstimatedTime");
            CancelButtonText = loc.Get("Progress.Cancel");
            OnPropertyChanged(nameof(CurrentOperationLabel));
            OnPropertyChanged(nameof(EstimatedTimeLabel));
            OnPropertyChanged(nameof(CancelButtonText));
        }

        #region Properties

        public new string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string Subtitle
        {
            get => _subtitle;
            set { _subtitle = value; OnPropertyChanged(); }
        }

        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(); }
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set
            {
                _progressPercentage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressPercentageText));
            }
        }

        public string ProgressPercentageText => $"{ProgressPercentage:F0}%";

        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set { _isIndeterminate = value; OnPropertyChanged(); }
        }

        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        public string CurrentOperationTitle
        {
            get => _currentOperationTitle;
            set
            {
                _currentOperationTitle = value;
                OnPropertyChanged();
                AnimateOperationChange();
            }
        }

        public string CurrentOperationCategory
        {
            get => _currentOperationCategory;
            set { _currentOperationCategory = value; OnPropertyChanged(); }
        }

        public string CategoryIcon
        {
            get => _categoryIcon;
            set { _categoryIcon = value; OnPropertyChanged(); }
        }

        public string EstimatedTimeText
        {
            get => _estimatedTimeText;
            set { _estimatedTimeText = value; OnPropertyChanged(); }
        }

        public string TipText
        {
            get => _tipText;
            set { _tipText = value; OnPropertyChanged(); }
        }

        public bool ShowCurrentOperation
        {
            get => _showCurrentOperation;
            set { _showCurrentOperation = value; OnPropertyChanged(); }
        }

        public bool ShowEstimatedTime
        {
            get => _showEstimatedTime;
            set { _showEstimatedTime = value; OnPropertyChanged(); }
        }

        public bool ShowMessage
        {
            get => _showMessage;
            set { _showMessage = value; OnPropertyChanged(); }
        }

        public bool ShowCancelButton
        {
            get => _showCancelButton;
            set { _showCancelButton = value; OnPropertyChanged(); }
        }

        public bool ShowSubtitle
        {
            get => _showSubtitle;
            set { _showSubtitle = value; OnPropertyChanged(); }
        }

        public bool ShowPercentage
        {
            get => _showPercentage;
            set { _showPercentage = value; OnPropertyChanged(); }
        }

        public bool ShowTip
        {
            get => _showTip;
            set { _showTip = value; OnPropertyChanged(); }
        }

        public string CurrentOperationLabel { get; private set; } = string.Empty;
        public string EstimatedTimeLabel { get; private set; } = string.Empty;
        public string CancelButtonText { get; private set; } = string.Empty;

        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        #endregion

        #region ShowAsync Static Method

        /// <summary>
        /// Shows a progress dialog and executes an operation with progress reporting.
        /// Requirements: 14.1, 14.2, 14.3, 14.5, 14.6
        /// </summary>
        public static async Task<TResult> ShowAsync<TResult>(
            Window owner,
            string title,
            Func<IProgress<OperationProgress>, CancellationToken, Task<TResult>> operation)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrEmpty(title)) throw new ArgumentException("Title cannot be null or empty", nameof(title));
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            ProgressDialog? dialog = null;
            var dialogCreated = new TaskCompletionSource<ProgressDialog>();
            
            await owner.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    dialog = new ProgressDialog
                    {
                        Owner = owner,
                        Title = title,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    
                    dialog.EnableCancellation();
                    dialog.Show();
                    
                    dialogCreated.SetResult(dialog);
                }
                catch (Exception ex)
                {
                    dialogCreated.SetException(ex);
                }
            });

            dialog = await dialogCreated.Task;
            var cts = dialog._cancellationTokenSource!;

            try
            {
                var progress = new Progress<OperationProgress>(p =>
                {
                    dialog.Dispatcher.InvokeAsync(() => dialog.UpdateProgressFromReport(p));
                });

                var result = await Task.Run(async () => await operation(progress, cts.Token), cts.Token);

                await dialog.Dispatcher.InvokeAsync(() =>
                {
                    if (dialog.IsVisible) dialog.Close();
                });

                return result;
            }
            catch (OperationCanceledException)
            {
                await dialog.Dispatcher.InvokeAsync(() =>
                {
                    if (dialog.IsVisible) dialog.Close();
                });
                throw;
            }
            catch (Exception)
            {
                await dialog.Dispatcher.InvokeAsync(() =>
                {
                    if (dialog.IsVisible) dialog.Close();
                });
                throw;
            }
        }

        private void UpdateProgressFromReport(OperationProgress progress)
        {
            if (progress.TotalCount > 0)
            {
                var completed = progress.CurrentIndex + 1;
                ProgressPercentage = (double)completed / progress.TotalCount * 100;
                ProgressText = LocalizationService.Instance.Get("Progress.ItemsCompleted", completed, progress.TotalCount);
                IsIndeterminate = false;
                ShowPercentage = true;

                if (completed > 0 && _stopwatch.IsRunning)
                {
                    var elapsed = _stopwatch.Elapsed;
                    var avgTimePerItem = elapsed.TotalSeconds / completed;
                    var remainingItems = progress.TotalCount - completed;
                    var estimatedSeconds = avgTimePerItem * remainingItems;

                    EstimatedTimeText = FormatTimeSpan(TimeSpan.FromSeconds(estimatedSeconds));
                    ShowEstimatedTime = true;
                }
            }

            if (!string.IsNullOrEmpty(progress.CurrentItemName))
            {
                CurrentOperationTitle = progress.CurrentItemName;
                ShowCurrentOperation = true;
            }

            switch (progress.Status)
            {
                case BatchOperationStatus.Starting:
                    IsIndeterminate = true;
                    ShowPercentage = false;
                    break;
                case BatchOperationStatus.Processing:
                    IsIndeterminate = false;
                    ShowPercentage = true;
                    break;
                case BatchOperationStatus.Completed:
                    ProgressPercentage = 100;
                    IsIndeterminate = false;
                    ShowPercentage = true;
                    break;
                case BatchOperationStatus.Failed:
                    break;
            }
        }

        #endregion

        #region Public Methods

        public void EnableCancellation()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            ShowCancelButton = true;
        }

        public void UpdateProgress(int completed, int total, string? currentOperation = null)
        {
            _completedItems = completed;
            _totalItems = total;
            
            if (total > 0)
            {
                ProgressPercentage = (double)completed / total * 100;
                ProgressText = LocalizationService.Instance.Get("Progress.ItemsCompleted", completed, total);
                IsIndeterminate = false;
                ShowPercentage = true;
                
                // 计算预计剩余时间
                if (completed > 0 && _stopwatch.IsRunning)
                {
                    var elapsed = _stopwatch.Elapsed;
                    var avgTimePerItem = elapsed.TotalSeconds / completed;
                    var remainingItems = total - completed;
                    var estimatedSeconds = avgTimePerItem * remainingItems;
                    
                    EstimatedTimeText = FormatTimeSpan(TimeSpan.FromSeconds(estimatedSeconds));
                    ShowEstimatedTime = true;
                }
            }
            
            if (!string.IsNullOrEmpty(currentOperation))
            {
                CurrentOperationTitle = currentOperation;
                ShowCurrentOperation = true;
            }
        }

        public void SetCategoryIcon(string category)
        {
            CategoryIcon = category switch
            {
                "Network" or "网络" => "\uE968",
                "CPU" or "处理器" => "\uE950",
                "GPU" or "显卡" => "\uE7F8",
                "Memory" or "内存" => "\uE7C0",
                "Storage" or "存储" => "\uEDA2",
                "System" or "系统" => "\uE770",
                "Gaming" or "游戏" => "\uE7FC",
                "Power" or "电源" => "\uE945",
                _ => "\uE946"
            };
        }

        public void SetTip(string tip)
        {
            TipText = tip;
            ShowTip = !string.IsNullOrEmpty(tip);
        }

        #endregion

        #region Private Methods

        private void AnimateOperationChange()
        {
            // 播放滑入动画
            var slideIn = (Storyboard)Resources["SlideInAnimation"];
            slideIn?.Begin(this);
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            var loc = LocalizationService.Instance;
            if (timeSpan.TotalSeconds < 60)
                return loc.Get("Progress.Seconds", (int)timeSpan.TotalSeconds);
            if (timeSpan.TotalMinutes < 60)
                return loc.Get("Progress.MinSec", (int)timeSpan.TotalMinutes, timeSpan.Seconds);
            return loc.Get("Progress.HourMin", (int)timeSpan.TotalHours, timeSpan.Minutes);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            CancelButton.IsEnabled = false;
            
            var loc = LocalizationService.Instance;
            Message = loc.Get("Operation.Cancelling");
            ShowMessage = true;
            ShowCurrentOperation = false;
            ShowEstimatedTime = false;
            ShowTip = false;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
