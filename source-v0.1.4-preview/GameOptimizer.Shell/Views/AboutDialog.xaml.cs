// File: GameOptimizer.Shell/Views/AboutDialog.xaml.cs
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class AboutDialog : Window
{
    private readonly bool _focusSupportSection;

    public AboutDialog()
        : this(focusSupportSection: false)
    {
    }

    public AboutDialog(bool focusSupportSection)
    {
        _focusSupportSection = focusSupportSection;
        InitializeComponent();
        var version = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)
            ?.InformationalVersion?.Split('+')[0];
        VersionText.Text = string.IsNullOrWhiteSpace(version) ? "" : $"v{version}";
        UpdateLocalizedTexts();
        Loaded += OnLoaded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_focusSupportSection) return;

        _ = Dispatcher.InvokeAsync(() =>
        {
            // “支持作者”入口不应只把区块勉强滚进可见范围；直接将它置于内容区顶部。
            var point = SupportSectionCard.TransformToAncestor(ContentScrollViewer)
                .Transform(new Point(0, 0));
            ContentScrollViewer.ScrollToVerticalOffset(ContentScrollViewer.VerticalOffset + point.Y - 12);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // DLG-001: 窗口关闭时取消订阅事件，防止内存泄漏
    private void OnClosed(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        DialogTitleText.Text = _focusSupportSection
            ? LocalizationService.Instance.CurrentLanguage == "en-US" ? "Support the author" : "支持作者"
            : LocalizationService.Instance.CurrentLanguage == "en-US" ? "About the author" : "作者介绍";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void FeedbackTencentButton_Click(object sender, RoutedEventArgs e)
        => OpenUrl("https://docs.qq.com/sheet/DU2dEdkRDdmFodnNn?tab=BB08J2");

    private void FeedbackGitHubButton_Click(object sender, RoutedEventArgs e)
        => OpenUrl("https://github.com/ZYY817/YuqiEngine/issues");

    private void AuthorDouyinButton_Click(object sender, RoutedEventArgs e)
        => OpenUrl("https://www.douyin.com/search/35071658?type=user");

    private void AuthorBilibiliButton_Click(object sender, RoutedEventArgs e)
        => OpenUrl("https://search.bilibili.com/all?keyword=%E6%99%BA%E5%AE%87%E3%80%81817");

    private void AuthorGitHubButton_Click(object sender, RoutedEventArgs e)
        => OpenUrl("https://github.com/ZYY817/YuqiEngine");

    private void WechatDonationImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShowDonationPreview("/Assets/donate_wechat.jpg", "微信赞赏");

    private void AlipayDonationImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShowDonationPreview("/Assets/donate_alipay.jpg", "支付宝赞赏");

    private void ShowDonationPreview(string resourcePath, string title)
    {
        var preview = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowState = WindowState.Maximized,
            ShowInTaskbar = false,
            Background = (Brush)(TryFindResource("Theme.Brush.DialogBackground") ?? Brushes.Black)
        };

        var image = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(64),
            SnapsToDevicePixels = true
        };
        try
        {
            image.Source = new BitmapImage(new Uri($"pack://application:,,,{resourcePath}", UriKind.Absolute));
        }
        catch (Exception ex)
        {
            InfoDialog.Show(this, "赞赏码无法加载", $"赞赏码资源无法加载，请检查安装文件是否完整。\n{ex.Message}", IconGlyphs.Warning);
            return;
        }
        var surface = new Border
        {
            Background = (Brush)(TryFindResource("Theme.Brush.DialogBackground") ?? Brushes.Black),
            BorderThickness = new Thickness(0),
            Child = image
        };

        var closeButton = new Button
        {
            Content = "退出全屏",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 20, 20, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Background = (Brush)(TryFindResource("Theme.Brush.ControlBackground") ?? Brushes.DimGray),
            Foreground = (Brush)(TryFindResource("Theme.Brush.TextPrimary") ?? Brushes.White),
            BorderBrush = (Brush)(TryFindResource("Theme.Brush.DialogBorder") ?? Brushes.Gray),
            BorderThickness = new Thickness(1)
        };
        closeButton.Click += (_, _) => preview.Close();

        var content = new Grid();
        content.Children.Add(surface);
        content.Children.Add(closeButton);
        preview.Content = content;
        preview.PreviewKeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Key.Escape) return;

            preview.Close();
            eventArgs.Handled = true;
        };
        preview.ShowDialog();
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            InfoDialog.Show(this, "打开失败", $"无法打开链接：{ex.Message}", IconGlyphs.Warning);
        }
    }
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }
}
