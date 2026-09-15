// File: GameOptimizer.Shell/Controls/ModernProgressBar.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace GameOptimizer.Shell.Controls
{
    public partial class ModernProgressBar : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(ModernProgressBar),
                new PropertyMetadata(0.0, OnValueChanged));

        public static readonly DependencyProperty IsIndeterminateProperty =
            DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(ModernProgressBar),
                new PropertyMetadata(false, OnIsIndeterminateChanged));

        private Storyboard? _pulseAnimation;
        private Storyboard? _flowAnimation;
        private Storyboard? _indeterminateAnimation;

        public ModernProgressBar()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public bool IsIndeterminate
        {
            get => (bool)GetValue(IsIndeterminateProperty);
            set => SetValue(IsIndeterminateProperty, value);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _pulseAnimation = (Storyboard)Resources["PulseAnimation"];
            _flowAnimation = (Storyboard)Resources["FlowAnimation"];
            
            if (!IsIndeterminate)
            {
                _pulseAnimation?.Begin();
                _flowAnimation?.Begin();
            }
            
            UpdateProgressWidth();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _pulseAnimation?.Stop();
            _flowAnimation?.Stop();
            _indeterminateAnimation?.Stop();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ModernProgressBar progressBar)
            {
                progressBar.UpdateProgressWidth();
            }
        }

        private static void OnIsIndeterminateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ModernProgressBar progressBar)
            {
                progressBar.UpdateIndeterminateState();
            }
        }

        private void UpdateProgressWidth()
        {
            if (ProgressFill == null || TrackBorder == null) return;

            // Why: 布局完成前 ActualWidth 为 NaN，DoubleAnimation(To=NaN) 会在每个渲染帧抛
            // AnimationException 并打满 UI 线程（表现为死机）。此时跳过动画，等布局后由
            // SizeChanged/Loaded 再补一次。
            if (double.IsNaN(TrackBorder.ActualWidth) || TrackBorder.ActualWidth <= 0) return;

            var percentage = Math.Max(0, Math.Min(100, Value));
            var targetWidth = TrackBorder.ActualWidth * (percentage / 100.0);

            var animation = new DoubleAnimation
            {
                // Why: 显式 From=0 双保险——即使 Width 基值意外回到 NaN 也不产生 NaN origin。
                From = 0,
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            ProgressFill.BeginAnimation(WidthProperty, animation);
        }

        private void UpdateIndeterminateState()
        {
            if (IsIndeterminate)
            {
                ProgressFill.Visibility = Visibility.Collapsed;
                IndeterminateBorder.Visibility = Visibility.Visible;

                _pulseAnimation?.Stop();
                _flowAnimation?.Stop();

                // Why: 同 UpdateProgressWidth——布局前 ActualWidth 为 NaN 会导致动画逐帧抛异常。
                if (double.IsNaN(ActualWidth) || ActualWidth <= 0) return;
                // 创建不确定进度动画
                _indeterminateAnimation = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
                var animation = new DoubleAnimation
                {
                    From = -ActualWidth,
                    To = ActualWidth * 2,
                    Duration = TimeSpan.FromSeconds(1.5)
                };
                Storyboard.SetTarget(animation, IndeterminateBorder);
                Storyboard.SetTargetProperty(animation, new PropertyPath("(Canvas.Left)"));
                _indeterminateAnimation.Children.Add(animation);
                _indeterminateAnimation.Begin();
            }
            else
            {
                ProgressFill.Visibility = Visibility.Visible;
                IndeterminateBorder.Visibility = Visibility.Collapsed;
                
                _indeterminateAnimation?.Stop();
                _pulseAnimation?.Begin();
                _flowAnimation?.Begin();
                
                UpdateProgressWidth();
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (!IsIndeterminate)
            {
                UpdateProgressWidth();
            }
        }
    }
}
