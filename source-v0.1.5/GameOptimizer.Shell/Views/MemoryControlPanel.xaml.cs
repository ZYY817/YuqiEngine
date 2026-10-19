// File: GameOptimizer.Shell/Views/MemoryControlPanel.xaml.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.Memory;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views.Controls;

public partial class MemoryControlPanel : UserControl
{
    private const double RingDashUnits = 45.6;
    private DashboardViewModel? _viewModel;
    private DispatcherTimer? _memoryTimer;
    private readonly IMemoryCleanerService _memoryCleanerService;
    private readonly IMemoryCleaningOwner _memoryCleaningOwner;
    private readonly IProcessCleanerService _processCleanerService;
    private bool _refreshInProgress;
    private bool _isIntegratedIntoPageScroll;
    private string _mutationBlockedReason = "正在读取可操作的应用。";
    private MemoryHeroActionState _actionState = MemoryHeroActionState.Checking;

    private enum MemoryHeroActionState
    {
        Checking,
        Ready,
        Executing,
        Success,
        Failed,
        Denied,
        Unavailable
    }

    public MemoryControlPanel(
        IMemoryCleanerService memoryCleanerService,
        IMemoryCleaningOwner memoryCleaningOwner,
        IProcessCleanerService processCleanerService)
    {
        _memoryCleanerService = memoryCleanerService ?? throw new ArgumentNullException(nameof(memoryCleanerService));
        _memoryCleaningOwner = memoryCleaningOwner ?? throw new ArgumentNullException(nameof(memoryCleaningOwner));
        _processCleanerService = processCleanerService ?? throw new ArgumentNullException(nameof(processCleanerService));
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        ApplyMutationBoundary();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        IntegrateIntoPageScroll();
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
        StartMemoryMonitor();
        await RefreshReadOnlyDataAsync();
        await RefreshMutationBoundaryAsync();
    }

    /// <summary>
    /// The memory hero is injected above the shared module list.  Keeping only the
    /// list inside the shared ScrollViewer made the hero consume fixed page space at
    /// small window heights.  For the memory route, move both sections under the
    /// same existing ScrollViewer so the page has one continuous vertical scroll.
    /// </summary>
    private void IntegrateIntoPageScroll()
    {
        if (_isIntegratedIntoPageScroll)
            return;

        var host = FindAncestor<FrameworkElement>(this);
        while (host is not null && host.FindName("ToggleListScrollViewer") is not ScrollViewer)
            host = FindAncestor<FrameworkElement>(host);

        if (host?.FindName("ToggleListScrollViewer") is not ScrollViewer pageScrollViewer ||
            host.FindName("GroupedItemsControl") is not ItemsControl groupedItems ||
            host.FindName("ModuleExtensionContainer") is not ContentPresenter extensionContainer ||
            !ReferenceEquals(extensionContainer.Content, this) ||
            !ReferenceEquals(pageScrollViewer.Content, groupedItems))
        {
            return;
        }

        // Detach both existing children before assigning the unified scroll content.
        extensionContainer.Content = null;
        extensionContainer.Visibility = Visibility.Collapsed;
        pageScrollViewer.Content = null;

        var continuousContent = new StackPanel
        {
            Name = "MemoryContinuousPageContent"
        };
        continuousContent.Children.Add(this);
        groupedItems.Margin = new Thickness(0, 20, 0, 112);
        continuousContent.Children.Add(groupedItems);
        pageScrollViewer.Content = continuousContent;

        _isIntegratedIntoPageScroll = true;
        Debug.WriteLine("[MemoryControlPanel] Integrated memory overview and module list into one page scroll viewer.");
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopMemoryMonitor();
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _viewModel = DataContext as DashboardViewModel;

    private async void OnLanguageChanged(object? sender, string language)
    {
        await Dispatcher.InvokeAsync(UpdateLocalizedTexts);
        await RefreshMutationBoundaryAsync();
    }

    private bool IsEnglish => LocalizationService.Instance.CurrentLanguage == "en-US";

    private void UpdateLocalizedTexts()
    {
        MemoryHeroEyebrow.Text = IsEnglish ? "MEMORY USAGE" : "内存占用";
        MemoryHeroTitle.Text = IsEnglish ? "Choose an app using more memory" : "选择一个占用内存较多的应用";
        MemoryHeroSubtitle.Text = IsEnglish ? "Choose a running app before deciding whether to release some of its memory" : "选择后可请求释放该应用当前占用的一部分内存";
        AvailableLabel.Text = IsEnglish ? "Available" : "可用";
        UsedLabel.Text = IsEnglish ? "Used" : "已用";
        TotalLabel.Text = IsEnglish ? "Total" : "总计";
        TopProcessTitle.Text = IsEnglish ? "Apps using the most memory" : "占用较高的应用";
        CleanJunkBtn.Content = IsEnglish ? "Open system cleanup" : "转到系统清理";
        MemoryUnavailableTitle.Text = IsEnglish ? "Memory status unavailable" : "无法读取内存状态";
        MemoryUnavailableHint.Text = IsEnglish ? "Windows memory data is temporarily unavailable. Try again later." : "系统内存接口暂不可用，请稍后重试";
        TopProcessEmptyText.Text = IsEnglish ? "Process memory usage is temporarily unavailable" : "暂时无法读取进程内存占用";

        SmartBoostBtn.ApplyTemplate();
        if (SmartBoostBtn.Template.FindName("MemBoostBtnText", SmartBoostBtn) is TextBlock boostText)
            boostText.Text = _actionState == MemoryHeroActionState.Executing
                ? (IsEnglish ? "Releasing..." : "释放中…")
                : (IsEnglish ? "Choose an app" : "选择应用");
    }

    private void StartMemoryMonitor()
    {
        if (_memoryTimer != null) return;
        _memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _memoryTimer.Tick += MemoryTimer_Tick;
        _memoryTimer.Start();
    }

    private async void MemoryTimer_Tick(object? sender, EventArgs e) => await RefreshReadOnlyDataAsync();

    private void StopMemoryMonitor()
    {
        if (_memoryTimer == null) return;
        _memoryTimer.Stop();
        _memoryTimer.Tick -= MemoryTimer_Tick;
        _memoryTimer = null;
    }

    private async Task RefreshReadOnlyDataAsync()
    {
        if (_refreshInProgress) return;
        _refreshInProgress = true;
        try
        {
            if (IsAcceptanceUnavailableProbe())
                throw new InvalidOperationException("Acceptance probe: memory data unavailable");

            var result = await Task.Run(() =>
            {
                var status = _memoryCleanerService.GetCurrentStatus();
                var groups = _processCleanerService.GetProcessGroups();
                return (status, Top: groups.Take(5).ToList());
            });

            ApplyMemoryStatus(result.status);
            ApplyTopProcesses(result.Top);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoryControlPanel] Read-only refresh failed: {ex.Message}");
            ShowUnavailableState();
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private static bool IsAcceptanceUnavailableProbe() =>
        string.Equals(Environment.GetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_MODE"), "1", StringComparison.Ordinal) &&
        (string.Equals(Environment.GetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_MEMORY_UNAVAILABLE"), "1", StringComparison.Ordinal) ||
         Environment.GetCommandLineArgs().Any(argument =>
             argument.Equals("--acceptance-memory-unavailable", StringComparison.OrdinalIgnoreCase)));

    private void ApplyMemoryStatus(MemoryStatus status)
    {
        if (status.TotalGB <= 0)
            throw new InvalidOperationException("Windows returned an empty physical-memory snapshot");

        MemoryUnavailablePanel.Visibility = Visibility.Collapsed;
        MemoryUsageVisual.Visibility = Visibility.Visible;
        MemoryHeroContent.Visibility = Visibility.Visible;

        MemoryPercentText.Text = status.LoadPercent.ToString();
        MemoryRatioText.Text = $"{status.UsedGB:F1} / {status.TotalGB:F0} GB";
        AvailableValueText.Text = $"{status.AvailableGB:F1} GB";
        UsedValueText.Text = $"{status.UsedGB:F1} GB";
        TotalValueText.Text = $"{status.TotalGB:F0} GB";
        MemoryHealthText.Text = status.LoadPercent switch
        {
            < 70 => IsEnglish ? "Healthy" : "运行良好",
            < 85 => IsEnglish ? "Moderate load" : "负载适中",
            _ => IsEnglish ? "High load" : "负载较高"
        };
        MemoryHealthText.Foreground = status.LoadPercent < 85
            ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
            : new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));

        var progress = Math.Clamp(status.LoadPercent / 100.0, 0, 1);
        MemoryUsageRing.StrokeDashArray = new DoubleCollection { RingDashUnits * progress, RingDashUnits };
    }

    private void ApplyTopProcesses(IReadOnlyList<ProcessGroup> groups)
    {
        TopProcessItemsPanel.Children.Clear();
        if (groups.Count == 0)
        {
            TopProcessItemsPanel.Visibility = Visibility.Collapsed;
            TopProcessEmptyPanel.Visibility = Visibility.Visible;
            TopProcessStateText.Text = IsEnglish ? "No readable process data" : "没有可读取的进程数据";
            return;
        }

        TopProcessEmptyPanel.Visibility = Visibility.Collapsed;
        TopProcessItemsPanel.Visibility = Visibility.Visible;
        TopProcessStateText.Text = IsEnglish ? "Live usage" : "实时占用";
        var maximum = Math.Max(1, groups.Max(group => group.TotalMemoryBytes));
        for (var index = 0; index < groups.Count; index++)
            TopProcessItemsPanel.Children.Add(CreateTopProcessItem(index + 1, groups[index], maximum));
    }

    private UIElement CreateTopProcessItem(int rank, ProcessGroup group, long maximum)
    {
        var panel = new StackPanel();
        var title = new TextBlock
        {
            Text = $"{rank}   {group.DisplayName}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11,
            Foreground = (Brush)FindResource("Theme.Brush.TextPrimary")
        };
        var bar = new ProgressBar
        {
            Height = 3,
            Margin = new Thickness(0, 7, 0, 6),
            Maximum = maximum,
            Value = group.TotalMemoryBytes,
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x32, 0x3B))
        };
        var value = new TextBlock
        {
            Text = FormatBytes(group.TotalMemoryBytes),
            FontSize = 10,
            Foreground = (Brush)FindResource("Theme.Brush.TextMuted")
        };
        panel.Children.Add(title);
        panel.Children.Add(bar);
        panel.Children.Add(value);
        return new Border
        {
            Margin = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(10, 9, 10, 9),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x72, 0x12, 0x1C, 0x24)),
            Child = panel
        };
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):F2} GB"
        : $"{bytes / (1024d * 1024):F0} MB";

    private void ShowUnavailableState()
    {
        MemoryUsageVisual.Visibility = Visibility.Hidden;
        MemoryHeroContent.Visibility = Visibility.Hidden;
        MemoryUnavailablePanel.Visibility = Visibility.Visible;
        TopProcessItemsPanel.Children.Clear();
        TopProcessItemsPanel.Visibility = Visibility.Collapsed;
        TopProcessEmptyPanel.Visibility = Visibility.Visible;
        TopProcessStateText.Text = IsEnglish ? "Unavailable" : "数据不可用";
    }

    private async void SmartBoost_Click(object sender, RoutedEventArgs e)
    {
        if (IsAcceptanceActionPreview(out _))
            return;
        var dialog = new MemoryCleanDialog(
            CleanMode.WorkingSet,
            _memoryCleanerService,
            _processCleanerService,
            _memoryCleaningOwner)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
        await RefreshReadOnlyDataAsync();
    }

    private async Task RefreshMutationBoundaryAsync()
    {
        if (IsAcceptanceActionPreview(out var preview))
        {
            ApplyAcceptanceActionPreview(preview);
            return;
        }

        _mutationBlockedReason = IsEnglish
            ? "Choose one running app. We will confirm it is still the same app before starting; this action cannot be undone."
            : "请选择一个正在运行的应用。开始前会确认它仍是同一个应用；此操作不能撤销。";
        ApplyActionState(MemoryHeroActionState.Ready, IsEnglish ? "Choose an app" : "选择应用", _mutationBlockedReason);
    }

    private void ApplyMutationBoundary()
    {
        SmartBoostBtn.IsEnabled = false;
        SmartBoostBtn.ToolTip = _mutationBlockedReason;
        MemoryMutationStatusText.Text = _mutationBlockedReason;
    }

    private void ApplyPreparationResult(MemoryCleaningResult result)
    {
        switch (result.Kind)
        {
            case MemoryCleaningResultKind.Ready:
                ApplyActionState(
                    MemoryHeroActionState.Ready,
                    IsEnglish ? "Ready to continue" : "可以继续",
                    GetUserFacingResultDetail(result));
                break;
            case MemoryCleaningResultKind.Denied:
                ApplyActionState(
                    MemoryHeroActionState.Denied,
                    IsEnglish ? "Windows did not allow this" : "Windows 不允许此操作",
                    GetUserFacingResultDetail(result));
                break;
            case MemoryCleaningResultKind.Failed:
                ApplyActionState(
                    MemoryHeroActionState.Failed,
                    IsEnglish ? "Cannot continue safely" : "暂时无法安全继续",
                    GetUserFacingResultDetail(result));
                break;
            default:
                ApplyActionState(
                    MemoryHeroActionState.Unavailable,
                    IsEnglish ? "Unavailable" : "暂不可用",
                    GetUserFacingResultDetail(result));
                break;
        }
    }

    private void ApplyActionResult(MemoryCleaningResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.Message)
            ? (IsEnglish ? "No result details were returned." : "执行器未返回结果详情。")
            : result.Message;

        switch (result.Kind)
        {
            case MemoryCleaningResultKind.SucceededVerified:
            case MemoryCleaningResultKind.NoChange:
                ApplyActionState(
                    MemoryHeroActionState.Success,
                    result.Kind == MemoryCleaningResultKind.NoChange
                        ? (IsEnglish ? "No cleanup needed" : "无需清理")
                        : (IsEnglish ? "Finished" : "操作已完成"),
                    GetUserFacingResultDetail(result, detail));
                break;
            case MemoryCleaningResultKind.Denied:
            case MemoryCleaningResultKind.Cancelled:
                ApplyActionState(
                    MemoryHeroActionState.Denied,
                    result.Kind == MemoryCleaningResultKind.Cancelled
                        ? (IsEnglish ? "Cleanup cancelled" : "清理已取消")
                        : (IsEnglish ? "Permission denied" : "权限不足"),
                    GetUserFacingResultDetail(result, detail));
                break;
            case MemoryCleaningResultKind.ReadOnly:
            case MemoryCleaningResultKind.FailClosed:
                ApplyActionState(
                    MemoryHeroActionState.Unavailable,
                    IsEnglish ? "Unavailable" : "暂不可用",
                    GetUserFacingResultDetail(result, detail));
                break;
            default:
                ApplyActionState(
                    MemoryHeroActionState.Failed,
                    IsEnglish ? "Not completed" : "操作未完成",
                    GetUserFacingResultDetail(result, detail));
                break;
        }
    }

    private void ApplyActionState(MemoryHeroActionState state, string title, string detail)
    {
        _actionState = state;
        SmartBoostBtn.ApplyTemplate();
        var buttonText = SmartBoostBtn.Template.FindName("MemBoostBtnText", SmartBoostBtn) as TextBlock;
        var icon = SmartBoostBtn.Template.FindName("CtaIcon", SmartBoostBtn) as FrameworkElement;
        var spinner = SmartBoostBtn.Template.FindName("CtaSpinner", SmartBoostBtn) as FrameworkElement;
        var rotation = SmartBoostBtn.Template.FindName("CtaSpinnerRotation", SmartBoostBtn) as RotateTransform;

        SmartBoostBtn.IsEnabled = state is MemoryHeroActionState.Ready or MemoryHeroActionState.Success or MemoryHeroActionState.Failed;
        SmartBoostBtn.ToolTip = detail;
        MemoryActionStateTitle.Text = title;
        MemoryMutationStatusText.Text = detail;

        var (iconText, accent, badge) = state switch
        {
            MemoryHeroActionState.Success => ("✓", Color.FromRgb(0x4A, 0xDE, 0x80), Color.FromArgb(0x34, 0x22, 0xC5, 0x5E)),
            MemoryHeroActionState.Failed => ("!", Color.FromRgb(0xFB, 0x71, 0x85), Color.FromArgb(0x34, 0xFB, 0x71, 0x85)),
            MemoryHeroActionState.Denied => ("!", Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromArgb(0x34, 0xFB, 0xBF, 0x24)),
            MemoryHeroActionState.Unavailable => ("×", Color.FromRgb(0x94, 0xA3, 0xB8), Color.FromArgb(0x34, 0x94, 0xA3, 0xB8)),
            _ => ("i", Color.FromRgb(0x86, 0xEF, 0xAC), Color.FromArgb(0x28, 0x34, 0xD3, 0x99))
        };
        MemoryActionStateIcon.Text = iconText;
        MemoryActionStateIcon.Foreground = new SolidColorBrush(accent);
        MemoryActionStateIconBadge.Background = new SolidColorBrush(badge);
        MemoryActionStatePanel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B));

        if (buttonText is not null)
            buttonText.Text = state == MemoryHeroActionState.Executing
                ? (IsEnglish ? "Releasing..." : "释放中…")
                : (IsEnglish ? "Choose an app" : "选择应用");
        if (icon is not null)
            icon.Visibility = state == MemoryHeroActionState.Executing ? Visibility.Collapsed : Visibility.Visible;
        if (spinner is not null)
            spinner.Visibility = state == MemoryHeroActionState.Executing ? Visibility.Visible : Visibility.Collapsed;

        if (rotation is not null)
        {
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            if (state == MemoryHeroActionState.Executing && SystemParameters.ClientAreaAnimation)
            {
                rotation.BeginAnimation(
                    RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    });
            }
        }
    }

    private string GetUserFacingResultDetail(MemoryCleaningResult result, string? fallback = null) => result.Kind switch
    {
        MemoryCleaningResultKind.SucceededVerified => IsEnglish
            ? "Finished. The app was not closed and no files were removed."
            : "操作已完成。应用没有被关闭，也没有删除任何文件。",
        MemoryCleaningResultKind.NoChange => IsEnglish
            ? "There was no memory to release. The app remains running."
            : "没有需要释放的内存，应用仍保持运行。",
        MemoryCleaningResultKind.Denied => IsEnglish
            ? GameOptimizer.Core.Services.ElevationGate.DescribeWriteDenial("change this app")
            : GameOptimizer.Core.Services.ElevationGate.DescribeWriteDenial("修改该应用"),
        MemoryCleaningResultKind.ReadOnly or MemoryCleaningResultKind.FailClosed => IsEnglish
            ? "This action is not available safely right now, so no app was changed."
            : "当前无法安全完成此操作，因此没有修改任何应用。",
        _ => IsEnglish
            ? "The action was not completed. No app was changed."
            : "操作没有完成，应用没有被修改。"
    };

    private static bool IsAcceptanceActionPreview(out string state)
    {
        state = string.Empty;
        if (!string.Equals(Environment.GetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_MODE"), "1", StringComparison.Ordinal))
            return false;

        var argument = Environment.GetCommandLineArgs().FirstOrDefault(value =>
            value.StartsWith("--acceptance-memory-action-state=", StringComparison.OrdinalIgnoreCase));
        if (argument is null)
            return false;

        state = argument.Split('=', 2)[1].Trim().ToLowerInvariant();
        return state.Length > 0;
    }

    private void ApplyAcceptanceActionPreview(string preview)
    {
        var state = preview switch
        {
            "ready" => MemoryHeroActionState.Ready,
            "executing" => MemoryHeroActionState.Executing,
            "success" => MemoryHeroActionState.Success,
            "failed" => MemoryHeroActionState.Failed,
            "denied" => MemoryHeroActionState.Denied,
            _ => MemoryHeroActionState.Unavailable
        };
        var title = IsEnglish ? $"Acceptance preview: {preview}" : $"验收预览：{preview}";
        var detail = IsEnglish
            ? "Visual state only. No cleanup command was invoked."
            : "仅渲染视觉状态，未调用任何清理命令。";
        ApplyActionState(state, title, detail);
        SmartBoostBtn.IsEnabled = false;
    }

    private void CleanJunk_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel?.NavigateToSystemCleanupCommand.Execute(null); }
        catch (Exception ex) { Debug.WriteLine($"[MemoryControlPanel] Navigation failed: {ex.Message}"); }
    }
}
