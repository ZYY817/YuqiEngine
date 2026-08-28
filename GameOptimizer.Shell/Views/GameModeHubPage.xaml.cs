using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.Models;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;
namespace GameOptimizer.Shell.Views;
public partial class GameModeHubPage : UserControl
{
    private readonly DispatcherTimer _monitorTimer;
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private DashboardViewModel? _viewModel;
    private bool _isInitialized;
    
    // 存储选中状态
    private readonly HashSet<string> _selectedItems = new();
    // 存储已生效状态
    private readonly HashSet<string> _activeItems = new();
    
    // 风险级别分类
    private static readonly HashSet<string> SafeOperations = new()
    {
        "basic.disable_mouse_acceleration", "peripheral.mouse_precision", "peripheral.keyboard.delay",
        "peripheral.keyboard.speed", "peripheral.keyboard_response", "gaming.mode.enable",
        "gaming.dvr.disable", "gaming.windowed_optimization", "network.disable_nagle", "network.set_congestion_ctcp",
        "fps.disable_fullscreen_optimizations", "gpu.enable_hags", "system.boost_foreground_priority",
        "network.optimize_rss", "network.optimize_checksum_offload", "cpu.system_responsiveness",
        "accessibility.disable_sticky_keys", "accessibility.disable_filter_keys", "accessibility.disable_toggle_keys",
        "gpu.nvidia_low_latency", "audio.disable_enhancements", "audio.exclusive_mode_priority",
        "power.disable_usb_suspend"
    };
    
    private static readonly HashSet<string> MediumOperations = new()
    {
        "gpu.force_high_performance_gpu", "privacy.disable_background_apps", "basic.disable_visual_effects",
        "peripheral.mouse_polling", "network.disable_tcp_auto_tuning", "cpu.force_high_timer_resolution",
        "device.disable_system_timer", "device.disable_umbus", "device.disable_composite_bus",
        "cpu.disable_core_parking", "gpu.disable_mpo", "memory.disable_compression", "memory.disable_superfetch",
        "power.disable_aspm", "power.processor_min_state_100", "power.perf_increase_threshold",
        "power.idle_demote_threshold", "power.perf_time_check", "power.class1_efficiency_threshold",
        "power.class1_perf_level", "power.disable_throttle"
    };
    
    private static readonly HashSet<string> HighRiskOperations = new()
    {
        "gpu.set_sfio_priority", "cpu.configure_hpet", "cpu.use_platform_tick", "network.disable_interrupt_moderation",
        "cpu.disable_dynamic_tick", "power.hetero_thread_scheduling"
    };
    // 卡片与操作ID映射
    private readonly Dictionary<string, (Border Card, Border Indicator, Ellipse Dot, Border ActiveTag)> _cardMap = new();
    public GameModeHubPage(IHardwareDetectionService hardwareDetectionService)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        InitializeComponent();
        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _monitorTimer.Tick += MonitorTimer_Tick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _viewModel = DataContext as DashboardViewModel;
        
        // 初始化卡片映射
        InitializeCardMap();
        
        // 同步已生效状态
        SyncActiveStates();
        
        // 应用默认筛选（平衡模式）
        ApplyFilter("balanced");
        
        // 硬件感知：根据 CPU 厂商更新 HPET 卡片提示
        _ = UpdateHardwareAwareHintsAsync();
        
        _isInitialized = true;
        _monitorTimer.Start();
        UpdatePerformanceMonitor();
    }
    
    private async Task UpdateHardwareAwareHintsAsync()
    {
        try
        {
            var hwInfo = await _hardwareDetectionService.GetOrDetectAsync();
            
            // 根据 CPU 厂商更新 HPET 和 System Timer 卡片的风险提示
            if (hwInfo.CpuVendor == CpuVendor.AMD)
            {
                // AMD CPU 推荐禁用 HPET
                HpetRiskTag.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                HpetRiskText.Text = "推荐";
                HpetDescText.Text = "AMD CPU 推荐禁用，可降低 DPC 延迟";
                HpetHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                HpetHardwareHintText.Text = hwInfo.IsAmdX3D ? "AMD X3D ✓" : "AMD ✓";
                HpetHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                HpetHardwareHint.Visibility = Visibility.Visible;
                
                // AMD CPU 强烈推荐启用平台计时器
                PlatformTickRiskTag.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                PlatformTickRiskText.Text = "强烈推荐";
                PlatformTickDescText.Text = "AMD CPU 强烈推荐，可解决随机抽帧问题";
                PlatformTickHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                PlatformTickHardwareHintText.Text = hwInfo.IsAmdX3D ? "AMD X3D ✓" : "AMD ✓";
                PlatformTickHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                PlatformTickHardwareHint.Visibility = Visibility.Visible;
                
                // AMD CPU 推荐禁用 System Timer
                SysTimerRiskTag.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                SysTimerRiskText.Text = "推荐";
                SysTimerDescText.Text = "AMD CPU 推荐禁用，可降低系统延迟";
                SysTimerHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                SysTimerHardwareHintText.Text = "AMD ✓";
                SysTimerHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                SysTimerHardwareHint.Visibility = Visibility.Visible;
            }
            else if (hwInfo.CpuVendor == CpuVendor.Intel)
            {
                // Intel CPU 不推荐禁用 HPET
                HpetRiskTag.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                HpetRiskText.Text = "不推荐";
                HpetDescText.Text = "Intel CPU 不建议禁用，可能导致系统不稳定";
                HpetHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                HpetHardwareHintText.Text = "Intel ✗";
                HpetHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                HpetHardwareHint.Visibility = Visibility.Visible;
                
                // Intel CPU 不推荐启用平台计时器
                PlatformTickRiskTag.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                PlatformTickRiskText.Text = "不推荐";
                PlatformTickDescText.Text = "Intel CPU 不建议启用，可能导致时钟异常";
                PlatformTickHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                PlatformTickHardwareHintText.Text = "Intel ✗";
                PlatformTickHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                PlatformTickHardwareHint.Visibility = Visibility.Visible;
                
                // Intel CPU 谨慎禁用 System Timer
                SysTimerRiskTag.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                SysTimerRiskText.Text = "谨慎";
                SysTimerDescText.Text = "Intel CPU 需谨慎，可能影响系统稳定性";
                SysTimerHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                SysTimerHardwareHintText.Text = "Intel ⚠";
                SysTimerHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                SysTimerHardwareHint.Visibility = Visibility.Visible;
            }
            
            // Dynamic Tick - 所有 CPU 都推荐
            DynamicTickHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            DynamicTickHardwareHintText.Text = hwInfo.CpuVendor == CpuVendor.AMD ? "AMD ✓" : "Intel ✓";
            DynamicTickHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
            DynamicTickHardwareHint.Visibility = Visibility.Visible;
            
            // Core Parking - AMD X3D 需谨慎
            if (hwInfo.IsAmdX3D)
            {
                CoreParkingHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                CoreParkingHardwareHintText.Text = "X3D ⚠";
                CoreParkingHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                CoreParkingHardwareHint.Visibility = Visibility.Visible;
            }
            
            // 异类线程调度策略 - 根据 CPU 厂商推荐
            if (hwInfo.CpuVendor == CpuVendor.Intel)
            {
                // Intel 大小核 CPU 推荐「首选高性能处理器」
                HeteroHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                HeteroHardwareHintText.Text = "Intel ✓";
                HeteroHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                HeteroHardwareHint.Visibility = Visibility.Visible;
                HeteroDescText.Text = "Intel 推荐：首选高性能处理器（大核优先）";
                HeteroRiskTag.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                HeteroRiskText.Text = "推荐";
            }
            else if (hwInfo.CpuVendor == CpuVendor.AMD)
            {
                // AMD CPU 推荐「所有处理器」
                HeteroHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                HeteroHardwareHintText.Text = "AMD ✓";
                HeteroHardwareHintText.Foreground = new SolidColorBrush(Colors.White);
                HeteroHardwareHint.Visibility = Visibility.Visible;
                HeteroDescText.Text = "AMD 推荐：所有处理器（无大小核）";
                HeteroRiskTag.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                HeteroRiskText.Text = "推荐";
            }
            
            // 电源高级优化区域硬件提示
            PowerHardwareHint.Background = new SolidColorBrush(Color.FromRgb(0x14, 0xB8, 0xA6));
            PowerHardwareHintText.Text = hwInfo.CpuVendor == CpuVendor.Intel ? "Intel 优化" : "AMD 优化";
            PowerHardwareHint.Visibility = Visibility.Visible;
            
            Debug.WriteLine($"[GameModeHub] Hardware detected: {hwInfo.CpuVendor}, X3D: {hwInfo.IsAmdX3D}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameModeHub] UpdateHardwareAwareHints error: {ex.Message}");
        }
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => _monitorTimer.Stop();
    private void InitializeCardMap()
    {
        // 帧率提升
        _cardMap["gpu.enable_hags"] = (CardHags, IndicatorHags, DotHags, TagHagsActive);
        _cardMap["gpu.force_high_performance_gpu"] = (CardForceGpu, IndicatorForceGpu, DotForceGpu, TagForceGpuActive);
        _cardMap["gpu.set_sfio_priority"] = (CardSfio, IndicatorSfio, DotSfio, TagSfioActive);
        _cardMap["fps.disable_fullscreen_optimizations"] = (CardFullscreen, IndicatorFullscreen, DotFullscreen, TagFullscreenActive);
        _cardMap["gaming.mode.enable"] = (CardGameMode, IndicatorGameMode, DotGameMode, TagGameModeActive);
        _cardMap["gaming.dvr.disable"] = (CardDvr, IndicatorDvr, DotDvr, TagDvrActive);
        _cardMap["gaming.windowed_optimization"] = (CardWindowedOpt, IndicatorWindowedOpt, DotWindowedOpt, TagWindowedOptActive);
        _cardMap["privacy.disable_background_apps"] = (CardBgApps, IndicatorBgApps, DotBgApps, TagBgAppsActive);
        _cardMap["basic.disable_visual_effects"] = (CardVisual, IndicatorVisual, DotVisual, TagVisualActive);
        _cardMap["system.boost_foreground_priority"] = (CardForeground, IndicatorForeground, DotForeground, TagForegroundActive);
        
        // 输入延迟
        _cardMap["basic.disable_mouse_acceleration"] = (CardMouseAccel, IndicatorMouseAccel, DotMouseAccel, TagMouseAccelActive);
        _cardMap["peripheral.mouse_precision"] = (CardMousePrecision, IndicatorMousePrecision, DotMousePrecision, TagMousePrecisionActive);
        _cardMap["peripheral.mouse_polling"] = (CardMousePolling, IndicatorMousePolling, DotMousePolling, TagMousePollingActive);
        _cardMap["peripheral.keyboard.delay"] = (CardKbDelay, IndicatorKbDelay, DotKbDelay, TagKbDelayActive);
        _cardMap["peripheral.keyboard.speed"] = (CardKbSpeed, IndicatorKbSpeed, DotKbSpeed, TagKbSpeedActive);
        _cardMap["peripheral.keyboard_response"] = (CardKbResponse, IndicatorKbResponse, DotKbResponse, TagKbResponseActive);
        _cardMap["cpu.force_high_timer_resolution"] = (CardTimer, IndicatorTimer, DotTimer, TagTimerActive);
        _cardMap["cpu.configure_hpet"] = (CardHpet, IndicatorHpet, DotHpet, TagHpetActive);
        _cardMap["cpu.use_platform_tick"] = (CardPlatformTick, IndicatorPlatformTick, DotPlatformTick, TagPlatformTickActive);
        _cardMap["cpu.system_responsiveness"] = (CardSysResp, IndicatorSysResp, DotSysResp, TagSysRespActive);
        _cardMap["device.disable_system_timer"] = (CardSysTimer, IndicatorSysTimer, DotSysTimer, TagSysTimerActive);
        _cardMap["device.disable_umbus"] = (CardUmbus, IndicatorUmbus, DotUmbus, TagUmbusActive);
        _cardMap["device.disable_composite_bus"] = (CardCompBus, IndicatorCompBus, DotCompBus, TagCompBusActive);
        _cardMap["cpu.disable_dynamic_tick"] = (CardDynamicTick, IndicatorDynamicTick, DotDynamicTick, TagDynamicTickActive);
        _cardMap["cpu.disable_core_parking"] = (CardCoreParking, IndicatorCoreParking, DotCoreParking, TagCoreParkingActive);
        
        // 网络延迟
        _cardMap["network.disable_nagle"] = (CardNagle, IndicatorNagle, DotNagle, TagNagleActive);
        _cardMap["network.disable_tcp_auto_tuning"] = (CardTcpTuning, IndicatorTcpTuning, DotTcpTuning, TagTcpTuningActive);
        _cardMap["network.set_congestion_ctcp"] = (CardCtcp, IndicatorCtcp, DotCtcp, TagCtcpActive);
        _cardMap["network.disable_interrupt_moderation"] = (CardInterrupt, IndicatorInterrupt, DotInterrupt, TagInterruptActive);
        _cardMap["network.optimize_rss"] = (CardRss, IndicatorRss, DotRss, TagRssActive);
        _cardMap["network.optimize_checksum_offload"] = (CardChecksum, IndicatorChecksum, DotChecksum, TagChecksumActive);
        
        // 辅助功能
        _cardMap["accessibility.disable_sticky_keys"] = (CardStickyKeys, IndicatorStickyKeys, DotStickyKeys, TagStickyKeysActive);
        _cardMap["accessibility.disable_filter_keys"] = (CardFilterKeys, IndicatorFilterKeys, DotFilterKeys, TagFilterKeysActive);
        _cardMap["accessibility.disable_toggle_keys"] = (CardToggleKeys, IndicatorToggleKeys, DotToggleKeys, TagToggleKeysActive);
        
        // GPU 高级优化
        _cardMap["gpu.nvidia_low_latency"] = (CardNvidiaLowLatency, IndicatorNvidiaLowLatency, DotNvidiaLowLatency, TagNvidiaLowLatencyActive);
        _cardMap["gpu.disable_mpo"] = (CardDisableMpo, IndicatorDisableMpo, DotDisableMpo, TagDisableMpoActive);
        
        // 内存优化
        _cardMap["memory.disable_compression"] = (CardMemoryCompression, IndicatorMemoryCompression, DotMemoryCompression, TagMemoryCompressionActive);
        _cardMap["memory.disable_superfetch"] = (CardSuperFetch, IndicatorSuperFetch, DotSuperFetch, TagSuperFetchActive);
        
        // 音频优化
        _cardMap["audio.disable_enhancements"] = (CardAudioEnhancements, IndicatorAudioEnhancements, DotAudioEnhancements, TagAudioEnhancementsActive);
        _cardMap["audio.exclusive_mode_priority"] = (CardAudioExclusive, IndicatorAudioExclusive, DotAudioExclusive, TagAudioExclusiveActive);
        
        // 电源高级优化
        _cardMap["power.disable_usb_suspend"] = (CardUsbSuspend, IndicatorUsbSuspend, DotUsbSuspend, TagUsbSuspendActive);
        _cardMap["power.disable_aspm"] = (CardAspm, IndicatorAspm, DotAspm, TagAspmActive);
        _cardMap["power.processor_min_state_100"] = (CardProcMinState, IndicatorProcMinState, DotProcMinState, TagProcMinStateActive);
        _cardMap["power.perf_increase_threshold"] = (CardPerfThreshold, IndicatorPerfThreshold, DotPerfThreshold, TagPerfThresholdActive);
        _cardMap["power.idle_demote_threshold"] = (CardIdleDemote, IndicatorIdleDemote, DotIdleDemote, TagIdleDemoteActive);
        _cardMap["power.perf_time_check"] = (CardPerfTimeCheck, IndicatorPerfTimeCheck, DotPerfTimeCheck, TagPerfTimeCheckActive);
        _cardMap["power.hetero_thread_scheduling"] = (CardHeteroThread, IndicatorHeteroThread, DotHeteroThread, TagHeteroThreadActive);
        _cardMap["power.class1_efficiency_threshold"] = (CardClass1Efficiency, IndicatorClass1Efficiency, DotClass1Efficiency, TagClass1EfficiencyActive);
        _cardMap["power.class1_perf_level"] = (CardClass1PerfLevel, IndicatorClass1PerfLevel, DotClass1PerfLevel, TagClass1PerfLevelActive);
        _cardMap["power.disable_throttle"] = (CardDisableThrottle, IndicatorDisableThrottle, DotDisableThrottle, TagDisableThrottleActive);
    }
    private void SyncActiveStates()
    {
        if (_viewModel == null) return;
        
        _activeItems.Clear();
        foreach (var module in _viewModel.OptimizationModules)
        {
            foreach (var toggle in module.Toggles)
            {
                if (toggle.IsApplied && !string.IsNullOrEmpty(toggle.OperationId))
                {
                    _activeItems.Add(toggle.OperationId);
                }
            }
        }
        
        // 更新UI显示已生效状态
        foreach (var kvp in _cardMap)
        {
            var isActive = _activeItems.Contains(kvp.Key);
            kvp.Value.ActiveTag.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            
            // 已生效的项自动选中
            if (isActive && !_selectedItems.Contains(kvp.Key))
            {
                _selectedItems.Add(kvp.Key);
                UpdateCardVisual(kvp.Key, true);
            }
        }
    }
    private void MonitorTimer_Tick(object? sender, EventArgs e) => UpdatePerformanceMonitor();
    private void UpdatePerformanceMonitor()
    {
        // 性能监控已移除，此方法保留以兼容定时器
    }
    // 卡片点击事件 - 切换选中状态
    private void OptCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border card || card.Tag is not string opId) return;
        
        if (_selectedItems.Contains(opId))
        {
            _selectedItems.Remove(opId);
            UpdateCardVisual(opId, false);
        }
        else
        {
            _selectedItems.Add(opId);
            UpdateCardVisual(opId, true);
        }
    }
    private void UpdateCardVisual(string opId, bool isSelected)
    {
        if (!_cardMap.TryGetValue(opId, out var components)) return;
        
        var (card, indicator, dot, _) = components;
        
        if (isSelected)
        {
            // 选中状态：紫色填充圆点 + 卡片边框高亮
            dot.Visibility = Visibility.Visible;
            indicator.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4D, 0x66, 0xFF));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4D, 0x66, 0xFF));
            card.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x25, 0x40));
        }
        else
        {
            // 未选中状态：空心圆 + 默认边框
            dot.Visibility = Visibility.Collapsed;
            indicator.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x65));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x42));
            card.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x30));
        }
    }
    // 筛选按钮事件
    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        
        if (FilterAll.IsChecked == true) ApplyFilter("all");
        else if (FilterPerformance.IsChecked == true) ApplyFilter("performance");
        else if (FilterBalanced.IsChecked == true) ApplyFilter("balanced");
        else if (FilterSafe.IsChecked == true) ApplyFilter("safe");
    }
    private void ApplyFilter(string mode)
    {
        _selectedItems.Clear();
        
        foreach (var opId in _cardMap.Keys)
        {
            bool shouldSelect = mode switch
            {
                "all" => true,
                "performance" => true, // 全部选中
                "balanced" => SafeOperations.Contains(opId) || MediumOperations.Contains(opId),
                "safe" => SafeOperations.Contains(opId),
                _ => false
            };
            
            if (shouldSelect)
            {
                _selectedItems.Add(opId);
            }
            
            UpdateCardVisual(opId, shouldSelect);
        }
    }
    // 应用选中项
    private async void ApplySelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItems.Count == 0)
        {
            ShowMessage("请先选择要应用的优化项", "提示", MessageBoxImage.Information);
            return;
        }

        // 高风险操作二次确认
        var highRiskOps = _selectedItems.Where(op => HighRiskOperations.Contains(op)).ToList();
        if (highRiskOps.Count > 0)
        {
            var riskDetails = new StringBuilder();
            riskDetails.AppendLine("⚠️ 检测到以下高风险操作：\n");
            foreach (var op in highRiskOps)
            {
                var name = GetOperationName(op);
                var risk = GetHighRiskWarning(op);
                riskDetails.AppendLine($"• {name}");
                riskDetails.AppendLine($"  风险：{risk}\n");
            }
            riskDetails.AppendLine("这些操作可能导致：");
            riskDetails.AppendLine("• 系统不稳定或蓝屏");
            riskDetails.AppendLine("• 部分硬件功能异常");
            riskDetails.AppendLine("• 需要重装系统才能恢复\n");
            riskDetails.AppendLine("强烈建议：先创建系统还原点！\n");
            riskDetails.AppendLine("确定要继续吗？");

            var riskResult = MessageBox.Show(
                riskDetails.ToString(),
                "⚠️ 高风险操作警告",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (riskResult != MessageBoxResult.Yes) return;

            // 二次确认
            var confirmResult = MessageBox.Show(
                "请再次确认：您已了解上述风险，并愿意承担可能的后果？\n\n点击「是」将立即执行这些高风险操作。",
                "最终确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Exclamation);
            
            if (confirmResult != MessageBoxResult.Yes) return;
        }
        else
        {
            // 普通操作确认
            var message = $"确定要应用选中的 {_selectedItems.Count} 项优化吗？\n\n应用后将修改系统设置，部分设置可能需要重启才能生效。";
            var result = MessageBox.Show(message, "确认应用", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }
        // 禁用按钮防止重复点击
        ApplyBtn.IsEnabled = false;
        ApplyBtn.Content = "应用中...";
        try
        {
            // 同步到 ViewModel 并真正执行优化
            if (_viewModel != null)
            {
                // 按模块分组操作ID
                var moduleOperations = new Dictionary<string, List<string>>();
                foreach (var opId in _selectedItems)
                {
                    foreach (var module in _viewModel.OptimizationModules)
                    {
                        var toggle = module.Toggles.FirstOrDefault(t => t.OperationId == opId);
                        if (toggle != null)
                        {
                            toggle.IsEnabled = true;
                            if (!moduleOperations.ContainsKey(module.Key))
                            {
                                moduleOperations[module.Key] = new List<string>();
                            }
                            moduleOperations[module.Key].Add(opId);
                            break;
                        }
                    }
                }
                // 调用 ViewModel 的应用命令执行真正的优化
                int totalSuccess = 0, totalFailed = 0;
                var allFailedOps = new List<string>();
                
                foreach (var kvp in moduleOperations)
                {
                    var module = _viewModel.OptimizationModules.FirstOrDefault(m => m.Key == kvp.Key);
                    if (module != null && module.SupportsExecution)
                    {
                        var (success, failed, failedOps) = await ApplyModuleOperationsAsync(kvp.Key, kvp.Value);
                        totalSuccess += success;
                        totalFailed += failed;
                        allFailedOps.AddRange(failedOps);
                    }
                }
                // 刷新模块状态
                await _viewModel.RefreshModuleStatusesAsync();
            
                // 更新已生效状态（只更新成功的）
                foreach (var opId in _selectedItems.Where(op => !allFailedOps.Contains(op)))
                {
                    _activeItems.Add(opId);
                    if (_cardMap.TryGetValue(opId, out var components))
                    {
                        components.ActiveTag.Visibility = Visibility.Visible;
                    }
                }
                
                // 显示详细结果
                if (totalFailed > 0)
                {
                    var failedNames = allFailedOps.Take(3).Select(GetOperationName);
                    var failedList = string.Join("\n", failedNames);
                    if (allFailedOps.Count > 3) failedList += $"\n...等 {allFailedOps.Count} 项";
                    
                    ShowMessage($"应用完成\n\n✓ 成功：{totalSuccess} 项\n✗ 失败：{totalFailed} 项\n\n失败项目：\n{failedList}\n\n可能原因：权限不足或系统限制\n建议：以管理员身份运行程序后重试", 
                        "部分应用成功", MessageBoxImage.Warning);
                }
                else
                {
                    ShowMessage($"已成功应用 {totalSuccess} 项优化设置\n\n部分设置可能需要重启才能完全生效。", "应用成功", MessageBoxImage.Information);
                }
            }
            else
            {
                ShowMessage($"已成功应用 {_selectedItems.Count} 项优化设置\n\n部分设置可能需要重启才能完全生效。", "应用成功", MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"应用优化时出错: {ex.Message}", "错误", MessageBoxImage.Error);
        }
        finally
        {
            ApplyBtn.IsEnabled = true;
            ApplyBtn.Content = "应用勾选优化";
        }
    }
    private async Task<(int success, int failed, List<string> failedOps)> ApplyModuleOperationsAsync(string moduleKey, List<string> operationIds)
    {
        if (_viewModel == null) return (0, operationIds.Count, operationIds);
        
        try
        {
            var result = await _viewModel.ApplyOperationsAsync(moduleKey, operationIds);
            var successCount = result.Operations.Count(o => o.Success);
            var failedCount = result.Operations.Count(o => !o.Success);
            var failedOps = result.Operations.Where(o => !o.Success).Select(o => o.OperationId).ToList();
            return (successCount, failedCount, failedOps);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameModeHub] ApplyModuleOperationsAsync error: {ex.Message}");
            return (0, operationIds.Count, operationIds);
        }
    }
    // 统一由备份管理页显示经过校验的快照、首次基线与不可恢复项。
    private void OpenBackupManager_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current?.MainWindow is GameOptimizer.Shell.MainWindow mainWindow)
        {
            mainWindow.NavigateToPage("backup-manager");
        }
    }
    private void SyncToViewModel(string operationId, bool isEnabled)
    {
        if (_viewModel == null) return;
        foreach (var module in _viewModel.OptimizationModules)
        {
            var toggle = module.Toggles.FirstOrDefault(t => t.OperationId == operationId);
            if (toggle != null) { toggle.IsEnabled = isEnabled; break; }
        }
    }
    // 全选/全不选 - 帧率提升
    private void FpsSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var fpsOps = new[] { "gpu.enable_hags", "gpu.force_high_performance_gpu", "gpu.set_sfio_priority",
            "fps.disable_fullscreen_optimizations", "gaming.mode.enable", "gaming.dvr.disable",
            "privacy.disable_background_apps", "basic.disable_visual_effects", "system.boost_foreground_priority" };
        
        foreach (var op in fpsOps)
        {
            _selectedItems.Add(op);
            UpdateCardVisual(op, true);
        }
    }
    private void FpsDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        var fpsOps = new[] { "gpu.enable_hags", "gpu.force_high_performance_gpu", "gpu.set_sfio_priority",
            "fps.disable_fullscreen_optimizations", "gaming.mode.enable", "gaming.dvr.disable",
            "privacy.disable_background_apps", "basic.disable_visual_effects", "system.boost_foreground_priority" };
        
        foreach (var op in fpsOps)
        {
            _selectedItems.Remove(op);
            UpdateCardVisual(op, false);
        }
    }
    // 全选/全不选 - 输入延迟
    private void InputSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var inputOps = new[] { "basic.disable_mouse_acceleration", "peripheral.mouse_precision", "peripheral.mouse_polling",
            "peripheral.keyboard.delay", "peripheral.keyboard.speed", "peripheral.keyboard_response",
            "cpu.force_high_timer_resolution", "cpu.configure_hpet", "cpu.use_platform_tick", "cpu.system_responsiveness",
            "device.disable_system_timer", "device.disable_umbus", "device.disable_composite_bus",
            "cpu.disable_dynamic_tick", "cpu.disable_core_parking" };
        
        foreach (var op in inputOps)
        {
            _selectedItems.Add(op);
            UpdateCardVisual(op, true);
        }
    }
    private void InputDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        var inputOps = new[] { "basic.disable_mouse_acceleration", "peripheral.mouse_precision", "peripheral.mouse_polling",
            "peripheral.keyboard.delay", "peripheral.keyboard.speed", "peripheral.keyboard_response",
            "cpu.force_high_timer_resolution", "cpu.configure_hpet", "cpu.use_platform_tick", "cpu.system_responsiveness",
            "device.disable_system_timer", "device.disable_umbus", "device.disable_composite_bus",
            "cpu.disable_dynamic_tick", "cpu.disable_core_parking" };
        
        foreach (var op in inputOps)
        {
            _selectedItems.Remove(op);
            UpdateCardVisual(op, false);
        }
    }
    // 全选/全不选 - 网络延迟
    private void NetworkSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var netOps = new[] { "network.disable_nagle", "network.disable_tcp_auto_tuning", "network.set_congestion_ctcp",
            "network.disable_interrupt_moderation", "network.optimize_rss", "network.optimize_checksum_offload" };
        
        foreach (var op in netOps)
        {
            _selectedItems.Add(op);
            UpdateCardVisual(op, true);
        }
    }
    private void NetworkDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        var netOps = new[] { "network.disable_nagle", "network.disable_tcp_auto_tuning", "network.set_congestion_ctcp",
            "network.disable_interrupt_moderation", "network.optimize_rss", "network.optimize_checksum_offload" };
        
        foreach (var op in netOps)
        {
            _selectedItems.Remove(op);
            UpdateCardVisual(op, false);
        }
    }
    
    // 全选/全不选 - 电源高级优化
    private void PowerSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var powerOps = new[] { "power.disable_usb_suspend", "power.disable_aspm", "power.processor_min_state_100",
            "power.perf_increase_threshold", "power.idle_demote_threshold", "power.perf_time_check", "power.hetero_thread_scheduling",
            "power.class1_efficiency_threshold", "power.class1_perf_level", "power.disable_throttle" };
        
        foreach (var op in powerOps)
        {
            _selectedItems.Add(op);
            UpdateCardVisual(op, true);
        }
    }
    
    private void PowerDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        var powerOps = new[] { "power.disable_usb_suspend", "power.disable_aspm", "power.processor_min_state_100",
            "power.perf_increase_threshold", "power.idle_demote_threshold", "power.perf_time_check", "power.hetero_thread_scheduling",
            "power.class1_efficiency_threshold", "power.class1_perf_level", "power.disable_throttle" };
        
        foreach (var op in powerOps)
        {
            _selectedItems.Remove(op);
            UpdateCardVisual(op, false);
        }
    }
    
    // 快捷操作
    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var decision = await UnavailableFileShellSafetyOwner.Instance.EvaluateAsync(
            new FileShellSafetyRequest("game-mode.clear-cache", FileShellMutationKind.QuarantineFiles, System.IO.Path.GetTempPath()),
            CancellationToken.None);
        ShowMessage($"{decision.Status}: {decision.Message}\n\n请使用具备预览、隔离清单与恢复功能的系统清理页面。",
            "安全阻断", MessageBoxImage.Warning);
    }
    private async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要刷新 DNS 缓存吗？\n\n操作说明：此操作将清除 DNS 解析缓存，可以解决部分网络连接问题。\n\n风险提示：首次访问网站可能会稍慢，因为需要重新解析域名。",
            "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result != MessageBoxResult.Yes) return;
        
        await Task.CompletedTask;
        ShowMessage("此旧入口尚未接入 Network Reset Owner，已安全阻断；请使用网络测试页的受控 DNS 维护入口。", "安全阻断", MessageBoxImage.Warning);
    }
    private void FreeMemory_Click(object sender, RoutedEventArgs e)
    {
        ShowMessage(
            "系统级待机内存清理未接入受控 Owner，已安全阻断。此操作不会启动外部进程、调用 PowerShell、执行 GC 或修改系统状态。",
            "安全阻断",
            MessageBoxImage.Warning);
    }
    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        var decision = await UnavailableFileShellSafetyOwner.Instance.EvaluateAsync(
            new FileShellSafetyRequest("game-mode.restart-explorer", FileShellMutationKind.ExplorerRestart),
            CancellationToken.None);
        ShowMessage($"{decision.Status}: {decision.Message}\n\n本版本不会终止或重启 Explorer。",
            "安全阻断", MessageBoxImage.Warning);
    }
    private static void ShowMessage(string message, string title, MessageBoxImage icon)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, icon);
    }

    private static string GetOperationName(string opId)
    {
        return opId switch
        {
            "gpu.enable_hags" => "显卡硬件加速调度",
            "gpu.force_high_performance_gpu" => "全局优先独显",
            "gpu.set_sfio_priority" => "游戏读写优先",
            "fps.disable_fullscreen_optimizations" => "禁用全屏优化",
            "gaming.mode.enable" => "Windows游戏模式",
            "gaming.dvr.disable" => "关闭后台录制",
            "gaming.windowed_optimization" => "窗口化游戏优化",
            "privacy.disable_background_apps" => "禁用后台应用",
            "basic.disable_visual_effects" => "禁用视觉效果",
            "system.boost_foreground_priority" => "前台程序优先",
            "basic.disable_mouse_acceleration" => "关闭鼠标加速",
            "peripheral.mouse_precision" => "鼠标精准度",
            "peripheral.mouse_polling" => "鼠标轮询率",
            "peripheral.keyboard.delay" => "键盘延迟",
            "peripheral.keyboard.speed" => "键盘速度",
            "peripheral.keyboard_response" => "键盘响应",
            "cpu.force_high_timer_resolution" => "高精度计时器",
            "cpu.configure_hpet" => "HPET配置",
            "cpu.use_platform_tick" => "启用平台计时器",
            "cpu.system_responsiveness" => "系统响应优化",
            "cpu.disable_dynamic_tick" => "禁用动态时钟",
            "cpu.disable_core_parking" => "禁用核心停车",
            "device.disable_system_timer" => "禁用系统计时器",
            "device.disable_umbus" => "禁用UMBus枚举器",
            "device.disable_composite_bus" => "禁用复合总线枚举器",
            "network.disable_nagle" => "关闭数据包合并",
            "network.disable_tcp_auto_tuning" => "稳定网络延迟",
            "network.set_congestion_ctcp" => "优化网络拥堵",
            "network.disable_interrupt_moderation" => "极速网络响应",
            "network.optimize_rss" => "多核网络加速",
            "network.optimize_checksum_offload" => "网络校验优化",
            "memory.disable_compression" => "禁用内存压缩",
            "memory.disable_superfetch" => "禁用SuperFetch",
            "audio.disable_enhancements" => "禁用音频增强",
            "audio.exclusive_mode_priority" => "音频独占模式优先",
            "accessibility.disable_sticky_keys" => "禁用粘滞键",
            "accessibility.disable_filter_keys" => "禁用筛选键",
            "accessibility.disable_toggle_keys" => "禁用切换键",
            "gpu.nvidia_low_latency" => "NVIDIA低延迟模式",
            "gpu.disable_mpo" => "禁用多平面覆盖",
            "power.disable_usb_suspend" => "禁用USB选择性暂停",
            "power.disable_aspm" => "禁用PCIe节能(ASPM)",
            "power.processor_min_state_100" => "处理器最小状态100%",
            "power.perf_increase_threshold" => "处理器性能提升阈值",
            "power.idle_demote_threshold" => "处理器闲置降级阈值",
            "power.perf_time_check" => "处理器性能时间检查",
            "power.hetero_thread_scheduling" => "异类线程调度策略",
            "power.class1_efficiency_threshold" => "大核效率阈值",
            "power.class1_perf_level" => "大核性能级别",
            "power.disable_throttle" => "禁止CPU节流",
            _ => opId
        };
    }

    private static string GetHighRiskWarning(string opId)
    {
        return opId switch
        {
            "gpu.set_sfio_priority" => "修改磁盘I/O优先级，可能导致系统文件读写异常",
            "cpu.configure_hpet" => "修改高精度事件计时器，可能导致系统时钟异常或蓝屏",
            "cpu.use_platform_tick" => "修改系统时钟源为 TSC，Intel CPU 可能导致时钟异常（AMD CPU 推荐）",
            "cpu.disable_dynamic_tick" => "修改BCD启动配置，可能导致系统无法启动或时钟异常",
            "network.disable_interrupt_moderation" => "禁用网卡中断调节，可能导致CPU占用过高或网络不稳定",
            "power.hetero_thread_scheduling" => "修改CPU线程调度策略，选错可能导致性能下降（Intel选大核优先，AMD选所有处理器）",
            _ => "此操作涉及底层系统设置，可能影响系统稳定性"
        };
    }
}
