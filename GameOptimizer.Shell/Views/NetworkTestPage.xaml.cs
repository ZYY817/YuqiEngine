using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Controls;
using GameOptimizer.Shell.Services;
using GameOptimizer.Core.Services.NetworkReset;

namespace GameOptimizer.Shell.Views;

public partial class NetworkTestPage : UserControl
{
    private INetworkResetOwner? _networkResetOwner;
    private readonly INetworkSpeedTestService _speedTestService;
    private IReadOnlyList<NetworkSpeedTestNodeOption>? _speedNodeOptions;

    private bool _isRunning;
    private bool _preserveAcceptancePresentation;
    private CancellationTokenSource? _cts;

    public NetworkTestPage() : this(null, new NetworkSpeedTestService())
    {
    }

    public NetworkTestPage(INetworkResetOwner? networkResetOwner)
        : this(networkResetOwner, new NetworkSpeedTestService())
    {
    }

    public NetworkTestPage(
        INetworkResetOwner? networkResetOwner,
        INetworkSpeedTestService speedTestService)
    {
        _networkResetOwner = networkResetOwner;
        _speedTestService = speedTestService ?? throw new ArgumentNullException(nameof(speedTestService));
        PageDiagnosticService.Log("NetworkTestPage", "Constructor called");
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal void AttachNetworkResetOwner(INetworkResetOwner networkResetOwner)
    {
        ArgumentNullException.ThrowIfNull(networkResetOwner);
        if (_networkResetOwner is not null && !ReferenceEquals(_networkResetOwner, networkResetOwner))
            throw new InvalidOperationException("NetworkTestPage 已绑定另一 Network Reset Owner，拒绝替换。");
        _networkResetOwner = networkResetOwner;
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _dnsFlushCts?.Cancel();
            _dnsFlushCts?.Dispose();
            _dnsFlushCts = null;
        }
        catch { }
        
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
        _ = RefreshNetworkMetadataAsync();
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        // Page Header
        if (FindName("PageHeader") is ToolTopbar header)
        {
            header.Title = loc.CurrentLanguage == "en-US" ? "Network Test" : "网络测试";
            header.Subtitle = loc.CurrentLanguage == "en-US"
                ? "Sonar Pulse · Map latency between this PC and test nodes"
                : "Sonar Pulse · 测绘你与世界之间的延迟";
        }
        
        // Buttons
        if (FindName("BackupNetworkBtn") is Button backupBtn) backupBtn.Content = loc.Get("Network.BackupConfig");
        if (FindName("FlushDnsBtn") is Button flushBtn) flushBtn.Content = loc.Get("Network.FlushDnsBtn");
        if (FindName("ResetNetworkBtn") is Button resetBtn) resetBtn.Content = loc.Get("Network.ResetNetworkBtn");
        
        // Stats Labels
        if (FindName("PacketLossLabel") is TextBlock lossLabel) lossLabel.Text = loc.Get("Network.PacketLossLabel");
        if (FindName("JitterLabel") is TextBlock jitterLabel) jitterLabel.Text = loc.Get("Network.JitterLabel");
        if (FindName("MinLatencyLabel") is TextBlock minLabel) minLabel.Text = loc.Get("Network.MinLatencyLabel");
        if (!_isRunning && !_preserveAcceptancePresentation && FindName("ConnectionStatus") is TextBlock connStatus)
            connStatus.Text = "等待开始";
        if (!_isRunning && !_preserveAcceptancePresentation)
            SetRunTestButtonText(loc.Get("Network.StartTest"));
        
        if (!_isRunning && !_preserveAcceptancePresentation && FindName("NoResultText") is TextBlock noResult)
            noResult.Text = "尚未开始测试";
        
    }

    private void SetRunTestButtonText(string text)
    {
        if (RunTestBtn.Template?.FindName("StartTestText", RunTestBtn) is TextBlock startText)
            startText.Text = text;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        InitializeSpeedNodeSelector();
        UpdateLocalizedTexts();
        RefreshNetworkBackupInventory();
        _ = RefreshNetworkMetadataAsync();
        PageDiagnosticService.LogLoaded("NetworkTestPage", true, $"{_speedTestService.BuiltInNodes.Count} speed profiles configured");
    }

    private async Task RefreshNetworkMetadataAsync()
    {
        if (FindName("PageHeader") is not ToolTopbar header)
            return;

        try
        {
            if (IsAcceptanceNetworkMetadataUnavailable())
                throw new NetworkInformationException();

            var adapter = await Task.Run(() => NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .Where(network => network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .OrderByDescending(HasDefaultGateway)
                .ThenBy(network => network.Name, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault());

            if (adapter is null)
            {
                header.StatusText = LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? "Connection unavailable"
                    : "当前连接不可用";
                header.StatusDetail = LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? "Node not tested"
                    : "节点未测试";
                ConnectionSummaryText.Text = "无可用网卡";
                LocalAddressText.Text = "--";
                GatewayText.Text = "--";
                LinkSpeedText.Text = "--";
                return;
            }

            var properties = adapter.GetIPProperties();
            var localAddress = properties.UnicastAddresses
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
            var gateway = properties.GatewayAddresses
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            var connection = $"{FormatInterfaceType(adapter.NetworkInterfaceType)} · {adapter.Name}";

            header.StatusText = connection;
            header.StatusDetail = LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "Node not tested"
                : "节点未测试";
            ConnectionSummaryText.Text = connection;
            LocalAddressText.Text = localAddress?.ToString() ?? "未分配";
            GatewayText.Text = gateway?.ToString() ?? "未检测到";
            LinkSpeedText.Text = adapter.Speed > 0
                ? $"{adapter.Speed / 1_000_000d:F0} Mbps"
                : "未知";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetworkTestPage] Connection metadata unavailable: {ex.Message}");
            header.StatusText = LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "Connection unavailable"
                : "当前连接不可用";
            header.StatusDetail = LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "Node not tested"
                : "节点未测试";
            ConnectionSummaryText.Text = "读取失败";
            LocalAddressText.Text = "--";
            GatewayText.Text = "--";
            LinkSpeedText.Text = "--";
        }
    }

    private static bool HasDefaultGateway(NetworkInterface adapter)
    {
        try { return adapter.GetIPProperties().GatewayAddresses.Count > 0; }
        catch { return false; }
    }

    private static string FormatInterfaceType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => "Ethernet",
        _ => type.ToString()
    };

    private static bool IsAcceptanceNetworkMetadataUnavailable() =>
        string.Equals(Environment.GetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_MODE"), "1", StringComparison.Ordinal) &&
        (string.Equals(
             Environment.GetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_NETWORK_METADATA_UNAVAILABLE"),
             "1",
             StringComparison.Ordinal) ||
         Environment.GetCommandLineArgs().Any(argument =>
             argument.Equals("--acceptance-network-metadata-unavailable", StringComparison.OrdinalIgnoreCase) ||
             argument.Equals("--acceptance-visual-mode=network-metadata-unavailable", StringComparison.OrdinalIgnoreCase) ||
             argument.Equals("--acceptance-action=network-metadata-unavailable", StringComparison.OrdinalIgnoreCase)));

    private async void RunTest_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _cts?.Cancel();
            return;
        }
        await RunNetworkTestAsync();
    }

    private void CancelTest_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning && _cts is { IsCancellationRequested: false })
        {
            _cts.Cancel();
            ProgressText.Text = "正在取消当前请求…";
            CancelTestBtn.IsEnabled = false;
        }
    }

    private void InitializeSpeedNodeSelector()
    {
        if (_speedNodeOptions is null)
        {
            var options = _speedTestService.BuiltInNodes
                .Select(node => new NetworkSpeedTestNodeOption(node, node.Name, node.Description, IsCustom: false))
                .ToList();
            options.Add(new NetworkSpeedTestNodeOption(
                Node: null,
                DisplayName: "自定义兼容节点",
                Description: "输入提供 /__down 与 /__up 的 HTTPS 基址",
                IsCustom: true));
            _speedNodeOptions = options;
        }

        if (NodeSelector.ItemsSource is null)
        {
            NodeSelector.ItemsSource = _speedNodeOptions;
            NodeSelector.SelectedIndex = 0;
        }
    }

    private void NodeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var option = NodeSelector.SelectedItem as NetworkSpeedTestNodeOption;
        CustomNodePanel.Visibility = option?.IsCustom == true ? Visibility.Visible : Visibility.Collapsed;
        CurrentNodeText.Text = option?.DisplayName ?? string.Empty;
        if (!_isRunning)
            ResetMeasurementPresentation("节点已选择；尚未开始测试。");
    }

    private void NodeSelector_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 公共 ComboBox 模板的内容层会截获空白区域命中；在控件边界统一打开下拉，
        // 同时保留已展开时的 ComboBoxItem 原生点击与键盘选择行为。
        if (sender is ComboBox selector && TryOpenNodeSelectorDropDown(selector))
            e.Handled = true;
    }

    internal bool TryOpenNodeSelectorDropDown(ComboBox? selector = null)
    {
        selector ??= NodeSelector;
        if (_isRunning || !selector.IsEnabled || selector.IsDropDownOpen)
            return false;

        InitializeSpeedNodeSelector();
        selector.Focus();
        selector.IsDropDownOpen = true;
        return selector.IsDropDownOpen;
    }

    private bool TryResolveSelectedSpeedNode(out NetworkSpeedTestNode? node)
    {
        node = null;
        if (NodeSelector.SelectedItem is not NetworkSpeedTestNodeOption option)
        {
            ShowFailureState("未选择测速节点", "请从下拉列表选择一个预设节点或自定义节点。", "选择节点后可重新开始。", "未选择节点");
            return false;
        }

        if (!option.IsCustom)
        {
            node = option.Node;
            return node is not null;
        }

        if (_speedTestService.TryCreateCustomNode(CustomNodeInput.Text, out node, out var error))
        {
            CustomNodeHint.Text = "已识别兼容基址；开始后会发起 HTTPS 下载与上传请求。";
            CustomNodeHint.Foreground = ThemeColors.SuccessLightBrush;
            return true;
        }

        CustomNodeHint.Text = error;
        CustomNodeHint.Foreground = ThemeColors.BrushFromColor(ThemeColors.Warning);
        ShowFailureState("自定义节点无效", error, "修正 HTTPS 基址后重试。", "节点格式错误");
        return false;
    }

    private void RefreshNetworkBackupInventory()
    {
        var snapshots = NetworkBackupService.GetBackupSnapshots();
        var entries = snapshots
            .Select(snapshot => new NetworkBackupListItem(
                snapshot.CreatedAt,
                snapshot.CapturedItems.Count == 0
                    ? "未发现可读取的网络配置文件"
                    : "已保存：" + string.Join("、", snapshot.CapturedItems),
                snapshot.Status))
            .ToArray();
        NetworkBackupItems.ItemsSource = entries;
        NetworkBackupSummary.Text = entries.Length == 0
            ? "尚无网络备份。创建备份会保存 IP、路由、DNS、代理和适配器状态；不完整备份不会被恢复流程使用。"
            : $"共 {entries.Length} 份备份。仅完整且经过验证的备份可供受支持的恢复流程读取。";
    }

    internal void BeginAcceptanceNetworkTest()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("YUQIENGINE_ACCEPTANCE_MODE"),
                "1",
                StringComparison.Ordinal))
            throw new InvalidOperationException("网络测试验收入口仅允许在隔离 acceptance UI 中运行。");

        _preserveAcceptancePresentation = true;
        InitializeSpeedNodeSelector();
        var state = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--acceptance-network-state=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1]
            .Trim()
            .ToLowerInvariant() ?? "running";
        ApplyAcceptanceVisualState(state);
    }

    private async Task RunNetworkTestAsync()
    {
        var loc = LocalizationService.Instance;
        if (!TryResolveSelectedSpeedNode(out var node) || node is null)
            return;

        _isRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var userCts = _cts;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(userCts.Token, timeoutCts.Token);
        var ct = runCts.Token;

        SetRunTestButtonText(loc.Get("Network.CancelTest"));
        NodeSelector.IsEnabled = false;
        CustomNodeInput.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        CancelTestBtn.IsEnabled = true;
        ResetMeasurementPresentation("测试进行中；最终延迟、最低值、抖动和丢包将在完整成功后显示。");
        NoResultText.Text = "测试进行中";
        RadarStatusText.Text = "进行中 · 可取消";
        ConnectionStatus.Text = "正在建立真实测量";
        CurrentNodeText.Text = node.Name;
        StartRadarAnimation();

        try
        {
            var progress = new Progress<NetworkSpeedTestProgress>(ApplySpeedTestProgress);
            var result = await _speedTestService.RunAsync(node, progress, ct);
            ShowCompletedResult(result, isAcceptanceEvidence: false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !userCts.IsCancellationRequested)
        {
            ShowFailureState(
                "测试超时",
                "45 秒内未完成全部阶段，已取消未完成请求且未生成最终结果。",
                "检查代理、防火墙或切换省流量档位后重试。",
                loc.Get("Network.Timeout"));
        }
        catch (OperationCanceledException)
        {
            ShowFailureState(
                "测试已取消",
                "当前下载或上传请求已收到取消信号；不保留不完整结果，也不会自动重试。",
                "可更换节点或档位后重新开始。",
                loc.Get("Network.TestCancelled"));
        }
        catch (NetworkSpeedTestException ex)
        {
            var (title, recovery, status) = ex.Kind switch
            {
                NetworkSpeedTestFailureKind.Offline => ("当前无网络", "连接 Wi-Fi 或网线，确认 Windows 显示已联网后重试。", "离线"),
                NetworkSpeedTestFailureKind.PermissionDenied => ("请求被策略拒绝", "检查代理、企业网络策略、防火墙或节点授权；应用不会尝试提权。", "权限或策略不足"),
                NetworkSpeedTestFailureKind.InvalidNode => ("节点配置无效", "选择内置节点，或修正自定义 HTTPS 基址后重试。", "节点无效"),
                _ => ("测速节点不可用", "切换节点或档位，并检查代理、证书与防火墙后重试。", "节点不可用")
            };
            ShowFailureState(title, ex.Message, recovery, status);
        }
        catch (Exception ex)
        {
            ShowFailureState(
                "测试失败",
                "发生未预期错误，未生成最终结果。",
                "可重新开始；若持续失败，请查看诊断日志中的 NetworkTestPage 记录。",
                loc.Get("Network.TestFailed"));
            PageDiagnosticService.LogError("NetworkTestPage", "Network test failed", ex);
        }
        finally
        {
            StopRadarAnimation();
            ProgressPanel.Visibility = Visibility.Collapsed;
            _isRunning = false;
            SetRunTestButtonText(loc.Get("Network.StartTest"));
            RunTestBtn.IsEnabled = true;
            NodeSelector.IsEnabled = true;
            CustomNodeInput.IsEnabled = true;
            userCts.Dispose();
            if (ReferenceEquals(_cts, userCts))
                _cts = null;
        }
    }

    private void ApplySpeedTestProgress(NetworkSpeedTestProgress progress)
    {
        TestProgress.Value = Math.Clamp(progress.Percent, 0, 100);
        ProgressText.Text = progress.Message;
        RadarStatusText.Text = progress.Phase switch
        {
            NetworkSpeedTestPhase.Preflight => "准备中 · 可取消",
            NetworkSpeedTestPhase.Latency => "延迟采样 · 可取消",
            NetworkSpeedTestPhase.Download => "下载测量 · 可取消",
            NetworkSpeedTestPhase.Upload => "上传测量 · 可取消",
            _ => "正在核对 · 可取消"
        };
        ConnectionStatus.Text = progress.Message;

        // A phase sample is not a completed measurement.  Keeping it out of the
        // metric cards prevents a cancelled or failed run from being mistaken for
        // a final download/upload result.  ShowCompletedResult is the single
        // place that reveals final metrics after every phase has completed.
    }

    private void ResetMeasurementPresentation(string status)
    {
        AvgLatency.Text = "--";
        MinLatency.Text = "--";
        Jitter.Text = "--";
        PacketLoss.Text = "--";
        DownloadSpeed.Text = "--";
        UploadSpeed.Text = "--";
        AvgLatencyPending.Visibility = Visibility.Visible;
        AvgLatencyValuePanel.Visibility = Visibility.Collapsed;
        DownloadPending.Visibility = Visibility.Visible;
        DownloadValuePanel.Visibility = Visibility.Collapsed;
        UploadPending.Visibility = Visibility.Visible;
        UploadValuePanel.Visibility = Visibility.Collapsed;
        StabilityPending.Visibility = Visibility.Visible;
        StabilityValuePanel.Visibility = Visibility.Collapsed;
        DnsPending.Visibility = Visibility.Visible;
        DnsValuePanel.Visibility = Visibility.Collapsed;
        ResultEvidencePanel.Visibility = Visibility.Collapsed;
        FailureRecoveryPanel.Visibility = Visibility.Collapsed;
        NoResultText.Text = status;
        TestProgress.Value = 0;
    }

    private void ShowCompletedResult(NetworkSpeedTestResult result, bool isAcceptanceEvidence)
    {
        AvgLatency.Text = result.AverageLatencyMs.ToString("F0");
        MinLatency.Text = result.MinimumLatencyMs.ToString("F0");
        Jitter.Text = result.JitterMs.ToString("F1");
        PacketLoss.Text = result.PacketLossPercent.ToString("F1");
        DnsResolution.Text = result.DnsResolutionMs.ToString("F1");
        NodeReachability.Text = result.NodeReachable ? "可达" : "不可达";
        DownloadSpeed.Text = result.DownloadMbps.ToString("F1");
        UploadSpeed.Text = result.UploadMbps.ToString("F1");
        AvgLatencyPending.Visibility = Visibility.Collapsed;
        AvgLatencyValuePanel.Visibility = Visibility.Visible;
        DownloadPending.Visibility = Visibility.Collapsed;
        DownloadValuePanel.Visibility = Visibility.Visible;
        UploadPending.Visibility = Visibility.Collapsed;
        UploadValuePanel.Visibility = Visibility.Visible;
        StabilityPending.Visibility = Visibility.Collapsed;
        StabilityValuePanel.Visibility = Visibility.Visible;
        DnsPending.Visibility = Visibility.Collapsed;
        DnsValuePanel.Visibility = Visibility.Visible;
        ResultEvidencePanel.Visibility = Visibility.Visible;
        FailureRecoveryPanel.Visibility = Visibility.Collapsed;
        NoResultText.Text = isAcceptanceEvidence ? "隔离视觉验收数据 · 非真实测速" : "完整测试成功";
        RadarStatusText.Text = isAcceptanceEvidence ? "验收示例 · 非真实数据" : "完成 · 已生成证据";
        ConnectionStatus.Text = isAcceptanceEvidence ? "仅用于界面验收" : "本轮测量完成";
        TestEvidenceText.Text =
            $"{(isAcceptanceEvidence ? "隔离验收示例；" : string.Empty)}节点：{result.Node.Name} · " +
            $"DNS {result.DnsResolutionMs:F1} ms · 节点{(result.NodeReachable ? "可达" : "不可达")} · HTTP RTT 样本 5 次 · 下载实收 {FormatMebibytes(result.DownloadedBytes):F1} MiB · " +
            $"上传合成数据 {FormatMebibytes(result.UploadedBytes):F1} MiB · 总耗时 {result.Duration.TotalSeconds:F1}s";
    }

    private void ShowFailureState(string title, string detail, string recovery, string status)
    {
        ResetMeasurementPresentation(status);
        FailureRecoveryPanel.Visibility = Visibility.Visible;
        FailureRecoveryTitle.Text = title;
        FailureRecoveryText.Text = detail + "\n恢复：" + recovery;
        RadarStatusText.Text = status;
        ConnectionStatus.Text = title;
        ProgressText.Text = status;
    }

    private void ApplyAcceptanceVisualState(string state)
    {
        var node = _speedTestService.BuiltInNodes[0];
        ResetMeasurementPresentation("隔离视觉验收 · 非真实测速结果");
        CurrentNodeText.Text = node.Name;

        switch (state)
        {
            case "dropdown":
                NoResultText.Text = "隔离验收：节点下拉列表已展开";
                NodeSelector.IsDropDownOpen = true;
                break;
            case "success":
                ShowCompletedResult(new NetworkSpeedTestResult(
                    node, 7.8, true, 28, 23, 3.4, 0, 86.4, 24.7,
                    node.DownloadBytes, node.UploadBytes, TimeSpan.FromSeconds(8.6), DateTimeOffset.UtcNow),
                    isAcceptanceEvidence: true);
                break;
            case "offline":
                ShowFailureState("当前无网络", "隔离验收：Windows 未检测到可用网络。", "连接网络后重新开始。", "离线");
                break;
            case "permission":
                ShowFailureState("请求被策略拒绝", "隔离验收：节点或网络策略返回拒绝。", "检查代理、防火墙或节点授权；应用不会提权。", "权限或策略不足");
                break;
            case "failed":
                ShowFailureState("测速节点不可用", "隔离验收：节点未返回有效响应。", "切换节点并检查代理、证书与防火墙。", "节点不可用");
                break;
            case "cancelled":
                ShowFailureState("测试已取消", "隔离验收：当前请求已取消，未生成最终结果。", "可重新开始。", "已取消");
                break;
            default:
                _isRunning = true;
                NodeSelector.IsEnabled = false;
                CustomNodeInput.IsEnabled = false;
                ProgressPanel.Visibility = Visibility.Visible;
                TestProgress.Value = 58;
                ProgressText.Text = "隔离验收：正在模拟下载阶段（不发起真实网络请求，未生成最终结果）…";
                NoResultText.Text = "测试进行中 · 最终数据尚未生成";
                RadarStatusText.Text = "下载测量 · 可取消";
                ConnectionStatus.Text = "隔离验收阶段 · 非真实测速";
                SetRunTestButtonText("取消测试");
                StartRadarAnimation();
                break;
        }
    }

    private static double FormatMebibytes(long bytes) => bytes / 1024d / 1024d;

    private void StartRadarAnimation()
    {
        if (Resources["RadarPulseStoryboard"] is Storyboard storyboard)
            storyboard.Begin(this, true);
    }

    private void StopRadarAnimation()
    {
        if (Resources["RadarPulseStoryboard"] is Storyboard storyboard)
            storyboard.Remove(this);

        PulseRingOuter.Opacity = 0;
        PulseRingInner.Opacity = 0;
        RadarSweep.Opacity = 0;
    }

    private void OptimizeNetwork_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var mainWindow = Window.GetWindow(this) as MainWindow;
        if (mainWindow is not null)
        {
            mainWindow.NavigateToPage("network-nav");
            ToastService.Instance.Info(loc.Get("Nav.Navigation"), loc.Get("Network.NavigatedToOptimize"));
        }
    }

    private bool _isBackingUp;
    
    private async void BackupNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (_isBackingUp) return;
        _isBackingUp = true;
        
        if (sender is Button btn) btn.IsEnabled = false;
        var loc = LocalizationService.Instance;
        try
        {
            var (success, path, error) = await NetworkBackupService.BackupNetworkConfigAsync();
            if (success)
            {
                RefreshNetworkBackupInventory();
                ToastService.Instance.Success(loc.Get("Network.BackupTitle"), loc.Get("Network.BackupSuccess"));
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Network.BackupComplete"), 
                    loc.Get("Network.BackupCompleteMsg", path), IconGlyphs.Check);
            }
            else
            {
                ToastService.Instance.Error(loc.Get("Network.BackupTitle"), loc.Get("Network.BackupFailedMsg", error));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetworkTest] Backup failed: {ex.Message}");
            ToastService.Instance.Error(loc.Get("Network.BackupTitle"), loc.Get("Network.BackupFailed", ex.Message));
        }
        finally
        {
            if (sender is Button btn2) btn2.IsEnabled = true;
            _isBackingUp = false;
        }
    }

    private bool _isFlushingDns;
    private CancellationTokenSource? _dnsFlushCts;

    private void SetDnsActionStatus(string title, string detail, bool showCancel)
    {
        DnsActionStatusPanel.Visibility = Visibility.Visible;
        DnsActionStatusText.Text = title;
        DnsActionReceiptText.Text = detail;
        CancelDnsBtn.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        CancelDnsBtn.IsEnabled = showCancel;
    }

    private void CancelDns_Click(object sender, RoutedEventArgs e)
    {
        if (_dnsFlushCts is not { IsCancellationRequested: false })
            return;

        _dnsFlushCts.Cancel();
        SetDnsActionStatus(
            "DNS 刷新：已请求取消",
            "不会自动重试；若命令已经启动，会等待当前命令退出后再报告最终回执。",
            showCancel: false);
    }
    
    private async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        if (_isFlushingDns) return;
        _isFlushingDns = true;
        
        var loc = LocalizationService.Instance;
        FlushDnsBtn.IsEnabled = false;
        _dnsFlushCts?.Dispose();
        _dnsFlushCts = new CancellationTokenSource();
        SetDnsActionStatus(
            "DNS 刷新：准备中",
            "正在检查权限、快照、确认与安全门禁；不会自动重试。",
            showCancel: true);
        try
        {
            if (_networkResetOwner is null)
            {
                ToastService.Instance.Warning(loc.Get("Network.FlushDns"), "Network Reset Owner 未接入，已安全阻断。");
                return;
            }
            SetDnsActionStatus(
                "DNS 刷新：执行中",
                "已通过确认后开始执行。可以请求取消；取消不会自动重试。",
                showCancel: true);
            var receipt = await _networkResetOwner.RunInteractiveAsync(
                NetworkResetProfile.DnsCache,
                _dnsFlushCts.Token);
            var success = receipt.Outcome == NetworkResetOutcome.SucceededVerified;
            
            if (success)
            {
                SetDnsActionStatus(
                    "DNS 刷新：命令已完成",
                    receipt.Message + " 最近回执：" + receipt.ReasonCode,
                    showCancel: false);
                ToastService.Instance.Success(loc.Get("Network.FlushDns"), receipt.Message);
            }
            else
            {
                SetDnsActionStatus(
                    "DNS 刷新：未完成",
                    receipt.Message + " 最近回执：" + receipt.ReasonCode + "。" + receipt.RecoveryInstructions,
                    showCancel: false);
                InfoDialog.Show(
                    Window.GetWindow(this),
                    loc.Get("Common.Failed"),
                    receipt.Message + "\n" + receipt.ReasonCode + "\n" + receipt.RecoveryInstructions,
                    IconGlyphs.Warning);
            }
        }
        catch (Exception ex)
        {
            SetDnsActionStatus(
                "DNS 刷新：执行失败",
                "未报告成功，也不会自动重试。" + ex.GetType().Name,
                showCancel: false);
            ToastService.Instance.Error(loc.Get("Network.FlushDns"), loc.Get("Network.DnsFailed", ex.Message));
        }
        finally
        {
            FlushDnsBtn.IsEnabled = true;
            _isFlushingDns = false;
            _dnsFlushCts?.Dispose();
            _dnsFlushCts = null;
        }
    }

    private bool _isResettingNetwork;

    private void SetNetworkResetRisk(string title, string detail)
    {
        NetworkResetRiskPanel.Visibility = Visibility.Visible;
        NetworkResetRiskTitle.Text = title;
        NetworkResetRiskDetail.Text = detail;
    }
    
    private async void ResetNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (_isResettingNetwork) return;
        
        var loc = LocalizationService.Instance;
        
        if (_networkResetOwner is null)
        {
            InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Failed"),
                "Network Reset Owner、首次安全基线或确认链尚未接入，已安全阻断。", Constants.IconGlyphs.Lock);
            return;
        }

        _isResettingNetwork = true;
        ResetNetworkBtn.IsEnabled = false;
        try
        {
            var preflight = await _networkResetOwner.ReadStatusAsync(NetworkResetProfile.Full);
            if (!preflight.IsSupported)
            {
                var findings = preflight.SafetyAssessment?.Findings ?? Array.Empty<NetworkResetSafetyFinding>();
                var details = findings.Count == 0
                    ? preflight.Message
                    : preflight.Message + Environment.NewLine
                        + string.Join(Environment.NewLine, findings.Select(finding => $"• {finding.Message}"));
                SetNetworkResetRisk("完整网络重置：已安全阻断", details);
                InfoDialog.Show(
                    Window.GetWindow(this),
                    "网络重置已阻断",
                    details,
                    Constants.IconGlyphs.Warning);
                return;
            }
            var receipt = await _networkResetOwner.RunInteractiveAsync(NetworkResetProfile.Full);
            if (receipt.Outcome == NetworkResetOutcome.SucceededVerified)
            {
                ToastService.Instance.Success(loc.Get("Network.ResetNetwork"), receipt.Message);
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Completed"),
                    receipt.Message, IconGlyphs.Lightbulb);
            }
            else if (receipt.Outcome == NetworkResetOutcome.AppliedRequiresRestartManualRecovery)
            {
                var restart = receipt.RebootRequired ? "需要重启：是" : "需要重启：否";
                var manual = receipt.ManualInterventionRequired ? "人工恢复：需要" : "人工恢复：不需要";
                var msg = receipt.Message + "\n\n" + restart + "\n" + manual + "\n"
                    + receipt.RecoveryDecision + "\n" + receipt.RecoveryInstructions + "\n"
                    + string.Join("\n", receipt.Steps.Select(step => $"{step.Command}: {step.ReasonCode}"));
                ToastService.Instance.Warning(loc.Get("Network.ResetNetwork"), receipt.Message);
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Completed"),
                    msg, IconGlyphs.Warning);
            }
            else
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Failed"), 
                    receipt.Message + "\n" + receipt.ReasonCode + "\n" + receipt.RecoveryInstructions,
                    IconGlyphs.Warning);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetworkTest] Reset network failed: {ex.Message}");
            ToastService.Instance.Error(loc.Get("Network.ResetNetwork"), loc.Get("Network.NeedAdmin"));
        }
        finally
        {
            ResetNetworkBtn.IsEnabled = true;
            _isResettingNetwork = false;
        }
    }

}

public sealed record NetworkSpeedTestNodeOption(
    NetworkSpeedTestNode? Node,
    string DisplayName,
    string Description,
    bool IsCustom);

internal sealed record NetworkBackupListItem(
    DateTimeOffset CreatedAt,
    string CapturedItemsText,
    string Status);
