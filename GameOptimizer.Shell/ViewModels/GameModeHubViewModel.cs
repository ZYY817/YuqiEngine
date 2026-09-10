using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举
using GameOptimizer.Core.Optimization;
using GameOptimizer.Shell.Helpers;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Theming;

namespace GameOptimizer.Shell.ViewModels;

public partial class GameModeHubViewModel : ObservableObject, IDisposable
{
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly List<GameModeOptimizationItem> _allItems = new();
    private bool _disposed;

    public GameModeHubViewModel(
        DashboardViewModel dashboardViewModel,
        IHardwareDetectionService hardwareDetectionService)
    {
        _dashboardViewModel = dashboardViewModel ?? throw new ArgumentNullException(nameof(dashboardViewModel));
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        InitializeOptimizationItems();
    }

    public ObservableCollection<GameModeOptimizationItem> FpsItems { get; } = new();
    public ObservableCollection<GameModeOptimizationItem> InputItems { get; } = new();
    public ObservableCollection<GameModeOptimizationItem> NetworkItems { get; } = new();
    public ObservableCollection<GameModeOptimizationItem> SecurityItems { get; } = new();
    public ObservableCollection<GameModeOptimizationItem> StorageItems { get; } = new();
    public ObservableCollection<GameModeOptimizationItem> WindowsItems { get; } = new();
    public ObservableCollection<GameModeOptimizationItem> CpuVendorItems { get; } = new();

    [ObservableProperty]
    private string filterMode = "balanced";

    public int SelectedCount => FpsItems.Count(i => i.IsSelected) 
                              + InputItems.Count(i => i.IsSelected) 
                              + NetworkItems.Count(i => i.IsSelected)
                              + SecurityItems.Count(i => i.IsSelected)
                              + StorageItems.Count(i => i.IsSelected)
                              + WindowsItems.Count(i => i.IsSelected)
                              + CpuVendorItems.Count(i => i.IsSelected);

    private void InitializeOptimizationItems()
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        
        // 帧率提升
        var fpsCategory = loc.Get("GameMode.Category.FpsBoost");
        FpsItems.Add(new("gpu.enable_hags", fpsCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.FpsPlus3to5")));
        FpsItems.Add(new("gpu.force_high_performance_gpu", fpsCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.FpsPlus5to10")));
        FpsItems.Add(new("gpu.set_sfio_priority", fpsCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.LatencyMinus1ms")));
        FpsItems.Add(new("fps.disable_fullscreen_optimizations", fpsCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.LatencyMinus2ms")));
        FpsItems.Add(new("gaming.mode.enable", fpsCategory, OperationRiskLevel.Safe));
        FpsItems.Add(new("gaming.dvr.disable", fpsCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.FpsPlus2to3")));
        FpsItems.Add(new("gaming.windowed_optimization", fpsCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.WindowModeOpt")));
        FpsItems.Add(new("privacy.disable_background_apps", fpsCategory, OperationRiskLevel.Balanced));
        FpsItems.Add(new("basic.disable_visual_effects", fpsCategory, OperationRiskLevel.Safe));
        FpsItems.Add(new("system.boost_foreground_priority", fpsCategory, OperationRiskLevel.Safe));

        // 输入延迟（排除外设操作，外设优化需在专门页面手动配置）
        var inputCategory = loc.Get("GameMode.Category.InputLatency");
        // 已移除：basic.disable_mouse_acceleration - 影响鼠标手感
        // 已移除：peripheral.mouse_precision - 影响鼠标手感
        // 已移除：peripheral.mouse_polling - 影响鼠标手感
        // 已移除：peripheral.keyboard.delay - 影响键盘手感
        // 已移除：peripheral.keyboard.speed - 影响键盘手感
        // 已移除：peripheral.keyboard_response - 影响键盘手感
        InputItems.Add(new("cpu.force_high_timer_resolution", inputCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.LatencyMinus0_5ms")));
        InputItems.Add(new("cpu.configure_hpet", inputCategory, OperationRiskLevel.Extreme, loc.Get("GameMode.Effect.LatencyMinus1to3ms")));
        InputItems.Add(new("cpu.use_platform_tick", inputCategory, OperationRiskLevel.Extreme, loc.Get("GameMode.Effect.LatencyMinus1to3ms")));
        InputItems.Add(new("cpu.system_responsiveness", inputCategory, OperationRiskLevel.Safe));
        InputItems.Add(new("device.disable_system_timer", inputCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.LatencyMinus0_5ms")));
        InputItems.Add(new("device.disable_umbus", inputCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.LatencyMinus0_2ms")));
        InputItems.Add(new("device.disable_composite_bus", inputCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.LatencyMinus0_2ms")));
        InputItems.Add(new("cpu.disable_dynamic_tick", inputCategory, OperationRiskLevel.Extreme, loc.Get("GameMode.Effect.LatencyMinus1to2ms")));
        InputItems.Add(new("cpu.disable_core_parking", inputCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.LatencyMinus0_5ms")));
        InputItems.Add(new("input.disable_pointer_trails", inputCategory, OperationRiskLevel.Safe));
        InputItems.Add(new("input.disable_snap_to", inputCategory, OperationRiskLevel.Safe));
        InputItems.Add(new("input.disable_touch_feedback", inputCategory, OperationRiskLevel.Safe));
        InputItems.Add(new("input.optimize_usb_polling", inputCategory, OperationRiskLevel.Safe));
        InputItems.Add(new("input.optimize_raw_input_buffer", inputCategory, OperationRiskLevel.Safe));

        // 网络延迟
        var networkCategory = loc.Get("GameMode.Category.NetworkLatency");
        NetworkItems.Add(new("network.disable_nagle", networkCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.LatencyMinus5to15ms")));
        NetworkItems.Add(new("network.disable_tcp_auto_tuning", networkCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.LatencyMinus2ms")));
        NetworkItems.Add(new("network.set_congestion_ctcp", networkCategory, OperationRiskLevel.Safe));
        NetworkItems.Add(new("network.disable_interrupt_moderation", networkCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.LatencyMinus1ms")));
        NetworkItems.Add(new("network.optimize_rss", networkCategory, OperationRiskLevel.Safe));
        NetworkItems.Add(new("network.optimize_checksum_offload", networkCategory, OperationRiskLevel.Safe));
        NetworkItems.Add(new("network.optimize_afd", networkCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.ThroughputPlus")));
        NetworkItems.Add(new("network.disable_qos_scheduler", networkCategory, OperationRiskLevel.Safe, loc.Get("GameMode.Effect.BandwidthPlus20")));
        NetworkItems.Add(new("network.disable_throttling_complete", networkCategory, OperationRiskLevel.Safe));
        NetworkItems.Add(new("network.disable_delivery_optimization", networkCategory, OperationRiskLevel.Safe));
        NetworkItems.Add(new("network.disable_netbios", networkCategory, OperationRiskLevel.Balanced));
        NetworkItems.Add(new("network.disable_lmhosts", networkCategory, OperationRiskLevel.Safe));

        // 安全保护不能作为游戏性能优化被关闭；保留旧 ID 仅用于解释迁移状态。
        var securityCategory = loc.Get("GameMode.Category.SecurityMitigation");
        SecurityItems.Add(new("security.disable_vbs", securityCategory, OperationRiskLevel.Extreme));
        SecurityItems.Add(new("security.disable_hvci", securityCategory, OperationRiskLevel.Extreme));
        SecurityItems.Add(new("security.disable_cfg", securityCategory, OperationRiskLevel.Extreme));
        SecurityItems.Add(new("security.disable_defender_realtime", securityCategory, OperationRiskLevel.Extreme));
        SecurityItems.Add(new("security.disable_smartscreen", securityCategory, OperationRiskLevel.Balanced));
        SecurityItems.Add(new("security.disable_dep", securityCategory, OperationRiskLevel.Extreme));
        SecurityItems.Add(new("security.disable_sehop", securityCategory, OperationRiskLevel.Extreme));
        foreach (var item in SecurityItems)
        {
            item.IsCompatible = false;
            item.IncompatibleReason = "产品安全策略禁止为了性能关闭系统保护；该入口固定 PolicyDenied。";
        }

        // 存储优化
        var storageCategory = loc.Get("GameMode.Category.StorageOptimization");
        StorageItems.Add(new("storage.disable_8dot3", storageCategory, OperationRiskLevel.Balanced));
        StorageItems.Add(new("storage.optimize_io_page_lock", storageCategory, OperationRiskLevel.Balanced));
        StorageItems.Add(new("storage.clear_standby_list", storageCategory, OperationRiskLevel.Safe));
        StorageItems.Add(new("storage.disable_ntfs_encryption", storageCategory, OperationRiskLevel.Balanced));

        // Windows 功能优化
        var windowsCategory = loc.Get("GameMode.Category.WindowsFeature");
        WindowsItems.Add(new("windows.disable_tips", windowsCategory, OperationRiskLevel.Safe));
        WindowsItems.Add(new("windows.disable_suggested_apps", windowsCategory, OperationRiskLevel.Safe));
        WindowsItems.Add(new("windows.disable_clipboard_history", windowsCategory, OperationRiskLevel.Safe));
        WindowsItems.Add(new("windows.disable_focus_assist", windowsCategory, OperationRiskLevel.Safe));
        WindowsItems.Add(new("windows.disable_timeline", windowsCategory, OperationRiskLevel.Safe));
        WindowsItems.Add(new("gpu.disable_dwm_animations", windowsCategory, OperationRiskLevel.Safe));
        WindowsItems.Add(new("gpu.disable_game_dvr_complete", windowsCategory, OperationRiskLevel.Safe));
        WindowsItems.Add(new("gpu.disable_xbox_game_monitoring", windowsCategory, OperationRiskLevel.Safe));

        // CPU 厂商专属优化
        var intelCategory = loc.Get("GameMode.Category.IntelExclusive");
        var nvidiaCategory = loc.Get("GameMode.Category.NvidiaExclusive");
        var amdCategory = loc.Get("GameMode.Category.AmdExclusive");
        var cpuAdvCategory = loc.Get("GameMode.Category.CpuAdvanced");
        var gpuAdvCategory = loc.Get("GameMode.Category.GpuAdvanced");
        
        // Intel 专属优化
        CpuVendorItems.Add(new("intel.speed_shift", intelCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.Intel6thGen")));
        CpuVendorItems.Add(new("intel.turbo_boost_max", intelCategory, OperationRiskLevel.Balanced));
        CpuVendorItems.Add(new("intel.disable_cstates", intelCategory, OperationRiskLevel.Extreme));
        // NVIDIA 专属优化
        CpuVendorItems.Add(new("gpu.nvidia_low_latency", nvidiaCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.NvidiaGpu")));
        // AMD 专属优化
        CpuVendorItems.Add(new("amd.radeon_anti_lag", amdCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.AmdGpu")));
        CpuVendorItems.Add(new("amd.precision_boost", amdCategory, OperationRiskLevel.Balanced, loc.Get("GameMode.Effect.AmdRyzen")));
        CpuVendorItems.Add(new("amd.disable_cool_n_quiet", amdCategory, OperationRiskLevel.Extreme));
        CpuVendorItems.Add(new("gpu.amd_anti_lag", amdCategory, OperationRiskLevel.Balanced));
        // CPU 高级优化
        CpuVendorItems.Add(new("cpu.disable_pstates", cpuAdvCategory, OperationRiskLevel.Extreme));
        CpuVendorItems.Add(new("cpu.disable_tsc_sync", cpuAdvCategory, OperationRiskLevel.Extreme));
        CpuVendorItems.Add(new("cpu.adjust_csrss_priority", cpuAdvCategory, OperationRiskLevel.Extreme));
        // GPU 深度优化
        CpuVendorItems.Add(new("gpu.disable_vrr", gpuAdvCategory, OperationRiskLevel.Balanced));
        CpuVendorItems.Add(new("gpu.clear_shader_cache", gpuAdvCategory, OperationRiskLevel.Safe));

        // 订阅选择变化
        var allItems = FpsItems.Concat(InputItems).Concat(NetworkItems)
            .Concat(SecurityItems).Concat(StorageItems).Concat(WindowsItems).Concat(CpuVendorItems);
        foreach (var item in allItems)
        {
            _allItems.Add(item);
            item.PropertyChanged += OnOptimizationItemPropertyChanged;
        }
    }

    private void OnOptimizationItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameModeOptimizationItem.IsSelected))
            OnPropertyChanged(nameof(SelectedCount));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 取消订阅所有 PropertyChanged 事件
        foreach (var item in _allItems)
            item.PropertyChanged -= OnOptimizationItemPropertyChanged;
        _allItems.Clear();
        GC.SuppressFinalize(this);
    }

    public async Task UpdateHardwareHintsAsync()
    {
        try
        {
            var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
            var hwInfo = await _hardwareDetectionService.GetOrDetectAsync();
            var green = AppBrushes.Success;
            var yellow = AppBrushes.Warning;
            var red = AppBrushes.Error;
            var gray = AppBrushes.TextMuted;

            // CPU/GPU 专属优化 - 根据厂商标记兼容性
            foreach (var item in CpuVendorItems)
            {
                // Intel CPU 专属优化
                if (item.OperationId.StartsWith("intel."))
                {
                    if (hwInfo.CpuVendor == CpuVendor.Intel)
                    {
                        item.HardwareHint = loc.Get("GameMode.IntelCpuOk");
                        item.HardwareHintBackground = green;
                        item.IsCompatible = true;
                    }
                    else
                    {
                        item.HardwareHint = loc.Get("GameMode.NotIntelCpu");
                        item.HardwareHintBackground = gray;
                        item.IsCompatible = false;
                        item.IncompatibleReason = loc.Get("GameMode.IntelCpuOnly");
                    }
                }
                // NVIDIA GPU 专属优化 - 使用 HasNvidiaGpu 支持多显卡场景
                else if (item.OperationId == "gpu.nvidia_low_latency")
                {
                    if (hwInfo.HasNvidiaGpu)
                    {
                        item.HardwareHint = hwInfo.HasAmdGpu || hwInfo.HasIntelGpu 
                            ? loc.Get("GameMode.NvidiaMultiGpu") : loc.Get("GameMode.NvidiaOk");
                        item.HardwareHintBackground = green;
                        item.IsCompatible = true;
                    }
                    else
                    {
                        item.HardwareHint = loc.Get("GameMode.NotNvidiaGpu");
                        item.HardwareHintBackground = gray;
                        item.IsCompatible = false;
                        item.IncompatibleReason = loc.Get("GameMode.NvidiaGpuOnly");
                    }
                }
                // AMD 专属优化（区分 CPU 和 GPU）
                else if (item.OperationId.StartsWith("amd.") || item.OperationId == "gpu.amd_anti_lag")
                {
                    bool isAmdGpuOpt = item.OperationId.Contains("radeon") || item.OperationId == "gpu.amd_anti_lag";
                    if (isAmdGpuOpt)
                    {
                        // AMD GPU 专属 - 使用 HasAmdGpu 支持多显卡场景
                        if (hwInfo.HasAmdGpu)
                        {
                            item.HardwareHint = hwInfo.HasNvidiaGpu || hwInfo.HasIntelGpu 
                                ? loc.Get("GameMode.AmdMultiGpu") : loc.Get("GameMode.AmdGpuOk");
                            item.HardwareHintBackground = green;
                            item.IsCompatible = true;
                        }
                        else
                        {
                            item.HardwareHint = loc.Get("GameMode.NotAmdGpu");
                            item.HardwareHintBackground = gray;
                            item.IsCompatible = false;
                            item.IncompatibleReason = loc.Get("GameMode.AmdGpuOnly");
                        }
                    }
                    else
                    {
                        // AMD CPU 专属 - 不兼容则灰显不可选
                        if (hwInfo.CpuVendor == CpuVendor.AMD)
                        {
                            item.HardwareHint = hwInfo.IsAmdX3D ? loc.Get("GameMode.AmdX3dOk") : loc.Get("GameMode.AmdCpuOk");
                            item.HardwareHintBackground = green;
                            item.IsCompatible = true;
                        }
                        else
                        {
                            item.HardwareHint = loc.Get("GameMode.NotAmdCpu");
                            item.HardwareHintBackground = gray;
                            item.IsCompatible = false;
                            item.IncompatibleReason = loc.Get("GameMode.AmdCpuOnly");
                        }
                    }
                }
                // GPU 通用深度优化（VRR、Shader缓存等）- 所有显卡可用
                else if (item.OperationId.StartsWith("gpu."))
                {
                    item.IsCompatible = true;
                    if (hwInfo.GpuVendor == GpuVendor.NVIDIA)
                    {
                        item.HardwareHint = loc.Get("GameMode.NvidiaOkSimple");
                        item.HardwareHintBackground = green;
                    }
                    else if (hwInfo.GpuVendor == GpuVendor.AMD)
                    {
                        item.HardwareHint = loc.Get("GameMode.AmdOkSimple");
                        item.HardwareHintBackground = green;
                    }
                }
                // CPU 通用高级优化 - 所有 CPU 可用但标注推荐
                else if (item.OperationId.StartsWith("cpu."))
                {
                    item.IsCompatible = true;
                    if (hwInfo.CpuVendor == CpuVendor.AMD)
                    {
                        item.HardwareHint = hwInfo.IsAmdX3D ? loc.Get("GameMode.AmdX3dOkSimple") : loc.Get("GameMode.AmdOkSimple");
                        item.HardwareHintBackground = green;
                    }
                    else if (hwInfo.CpuVendor == CpuVendor.Intel)
                    {
                        item.HardwareHint = loc.Get("GameMode.IntelOkSimple");
                        item.HardwareHintBackground = green;
                    }
                }
            }

            // FPS 优化中的 GPU 相关项 - 根据多GPU情况标注
            foreach (var item in FpsItems)
            {
                if (item.OperationId.StartsWith("gpu."))
                {
                    item.IsCompatible = true;
                    
                    // HAGS 特殊处理：NVIDIA RTX 效果最佳
                    if (item.OperationId == "gpu.enable_hags")
                    {
                        if (hwInfo.HasNvidiaGpu)
                        {
                            item.HardwareHint = loc.Get("GameMode.NvidiaRtxRecommend");
                            item.HardwareHintBackground = green;
                        }
                        else if (hwInfo.HasAmdGpu)
                        {
                            item.HardwareHint = loc.Get("GameMode.AmdOkAverage");
                            item.HardwareHintBackground = yellow;
                        }
                        else
                        {
                            item.HardwareHint = loc.Get("GameMode.IgpuNotRecommend");
                            item.HardwareHintBackground = red;
                        }
                    }
                    else
                    {
                        // 其他 GPU 优化：根据主显卡标注
                        if (hwInfo.GpuVendor == GpuVendor.NVIDIA)
                        {
                            item.HardwareHint = hwInfo.HasAmdGpu ? loc.Get("GameMode.NvidiaMultiGpuSimple") : loc.Get("GameMode.NvidiaOkSimple");
                            item.HardwareHintBackground = green;
                        }
                        else if (hwInfo.GpuVendor == GpuVendor.AMD)
                        {
                            item.HardwareHint = hwInfo.HasNvidiaGpu ? loc.Get("GameMode.AmdMultiGpuSimple") : loc.Get("GameMode.AmdOkSimple");
                            item.HardwareHintBackground = green;
                        }
                        else if (hwInfo.GpuVendor == GpuVendor.Intel)
                        {
                            item.HardwareHint = loc.Get("GameMode.IntelIgpu");
                            item.HardwareHintBackground = yellow;
                        }
                    }
                }
            }

            // 输入延迟优化 - HPET/计时器相关（CPU 通用但有推荐，不限制选择）
            var hpetItem = InputItems.FirstOrDefault(i => i.OperationId == "cpu.configure_hpet");
            var platformTickItem = InputItems.FirstOrDefault(i => i.OperationId == "cpu.use_platform_tick");
            var sysTimerItem = InputItems.FirstOrDefault(i => i.OperationId == "device.disable_system_timer");
            var coreParkingItem = InputItems.FirstOrDefault(i => i.OperationId == "cpu.disable_core_parking");
            var dynamicTickItem = InputItems.FirstOrDefault(i => i.OperationId == "cpu.disable_dynamic_tick");

            if (hwInfo.CpuVendor == CpuVendor.AMD)
            {
                // AMD 平台推荐禁用 HPET 和启用平台计时器
                if (hpetItem != null) { hpetItem.HardwareHint = hwInfo.IsAmdX3D ? loc.Get("GameMode.AmdX3dRecommend") : loc.Get("GameMode.AmdRecommend"); hpetItem.HardwareHintBackground = green; hpetItem.IsCompatible = true; }
                if (platformTickItem != null) { platformTickItem.HardwareHint = hwInfo.IsAmdX3D ? loc.Get("GameMode.AmdX3dRecommend") : loc.Get("GameMode.AmdRecommend"); platformTickItem.HardwareHintBackground = green; platformTickItem.IsCompatible = true; }
                if (sysTimerItem != null) { sysTimerItem.HardwareHint = loc.Get("GameMode.AmdOkSimple"); sysTimerItem.HardwareHintBackground = green; sysTimerItem.IsCompatible = true; }
                if (coreParkingItem != null) 
                { 
                    coreParkingItem.IsCompatible = true;
                    if (hwInfo.IsAmdX3D) { coreParkingItem.HardwareHint = loc.Get("GameMode.X3dCaution"); coreParkingItem.HardwareHintBackground = yellow; }
                    else { coreParkingItem.HardwareHint = loc.Get("GameMode.AmdOkSimple"); coreParkingItem.HardwareHintBackground = green; }
                }
                if (dynamicTickItem != null) { dynamicTickItem.HardwareHint = loc.Get("GameMode.AmdOkSimple"); dynamicTickItem.HardwareHintBackground = green; dynamicTickItem.IsCompatible = true; }
            }
            else if (hwInfo.CpuVendor == CpuVendor.Intel)
            {
                // Intel 平台不推荐禁用 HPET（用红色标注但仍可选择）
                if (hpetItem != null) { hpetItem.HardwareHint = loc.Get("GameMode.IntelNotRecommend"); hpetItem.HardwareHintBackground = red; hpetItem.IsCompatible = true; }
                if (platformTickItem != null) { platformTickItem.HardwareHint = loc.Get("GameMode.IntelNotRecommend"); platformTickItem.HardwareHintBackground = red; platformTickItem.IsCompatible = true; }
                if (sysTimerItem != null) { sysTimerItem.HardwareHint = loc.Get("GameMode.IntelCaution"); sysTimerItem.HardwareHintBackground = yellow; sysTimerItem.IsCompatible = true; }
                if (coreParkingItem != null) { coreParkingItem.HardwareHint = loc.Get("GameMode.IntelOkSimple"); coreParkingItem.HardwareHintBackground = green; coreParkingItem.IsCompatible = true; }
                if (dynamicTickItem != null) { dynamicTickItem.HardwareHint = loc.Get("GameMode.IntelOkSimple"); dynamicTickItem.HardwareHintBackground = green; dynamicTickItem.IsCompatible = true; }
            }

            Debug.WriteLine($"[GameModeHub] Hardware: CPU={hwInfo.CpuVendor}, GPU={hwInfo.GpuVendor}, " +
                $"X3D={hwInfo.IsAmdX3D}, NVIDIA={hwInfo.HasNvidiaGpu}, AMD={hwInfo.HasAmdGpu}, Intel={hwInfo.HasIntelGpu}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameModeHub] UpdateHardwareHints error: {ex.Message}");
        }
    }

    public void ApplyFilter(string mode)
    {
        FilterMode = mode;
        var allItems = FpsItems.Concat(InputItems).Concat(NetworkItems)
            .Concat(SecurityItems).Concat(StorageItems).Concat(WindowsItems).Concat(CpuVendorItems);

        foreach (var item in allItems)
        {
            item.IsSelected = CheckStateConflict.ComputePresetSelected(mode, item.RiskLevel, item.IsCompatible);
        }
    }

    public void SelectAllInCategory(string category)
    {
        var items = category switch
        {
            "fps" => FpsItems,
            "input" => InputItems,
            "network" => NetworkItems,
            "security" => SecurityItems,
            "storage" => StorageItems,
            "windows" => WindowsItems,
            "cpuvendor" => CpuVendorItems,
            _ => Enumerable.Empty<GameModeOptimizationItem>()
        };
        foreach (var item in items)
        {
            if (item.IsCompatible)
                item.IsSelected = true;
        }
    }

    public void RefreshAllLocalizedTexts()
    {
        var allItems = FpsItems.Concat(InputItems).Concat(NetworkItems)
            .Concat(SecurityItems).Concat(StorageItems).Concat(WindowsItems).Concat(CpuVendorItems);
        foreach (var item in allItems)
        {
            item.RefreshLocalizedTexts();
        }
    }

    public void DeselectAllInCategory(string category)
    {
        var items = category switch
        {
            "fps" => FpsItems,
            "input" => InputItems,
            "network" => NetworkItems,
            "security" => SecurityItems,
            "storage" => StorageItems,
            "windows" => WindowsItems,
            "cpuvendor" => CpuVendorItems,
            _ => Enumerable.Empty<GameModeOptimizationItem>()
        };
        foreach (var item in items) item.IsSelected = false;
    }

    public IEnumerable<string> GetSelectedOperationIds()
    {
        return FpsItems.Concat(InputItems).Concat(NetworkItems)
            .Concat(SecurityItems).Concat(StorageItems).Concat(WindowsItems).Concat(CpuVendorItems)
            .Where(i => i.IsSelected)
            .Select(i => i.OperationId);
    }

    public Task SyncAppliedStatesAsync()
    {
        var allItems = FpsItems.Concat(InputItems).Concat(NetworkItems)
            .Concat(SecurityItems).Concat(StorageItems).Concat(WindowsItems).Concat(CpuVendorItems);
            
        foreach (var module in _dashboardViewModel.OptimizationModules)
        {
            foreach (var toggle in module.Toggles)
            {
                if (string.IsNullOrEmpty(toggle.OperationId)) continue;
                
                var item = allItems.FirstOrDefault(i => i.OperationId == toggle.OperationId);
                
                if (item != null)
                {
                    item.IsApplied = toggle.IsApplied;
                    if (toggle.IsApplied && item.IsCompatible)
                        item.IsSelected = true;
                }
            }
        }
        return Task.CompletedTask;
    }
}
