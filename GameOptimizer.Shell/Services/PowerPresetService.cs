using System.Collections.Generic;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public static class PowerPresetService
{
    #region 通用预设（基础设置）
    private static Dictionary<string, int> GetBaseGamingSettings() => new()
    {
        ["perf_increase_threshold"] = 1,
        ["idle_demote_threshold"] = 100,
        ["core_parking_min"] = 100,
        ["pcie_aspm"] = 0,
        ["usb_selective_suspend"] = 0,
        ["proc_min_state"] = 100,
        ["proc_max_state"] = 100,
        ["boost_mode"] = 2,
        ["perf_time_check"] = 5000,
        ["disk_turn_off"] = 0,
        ["display_turn_off"] = 0,
        ["sleep_after"] = 0,
        ["cooling_policy"] = 1,
    };

    private static Dictionary<string, int> GetBaseCompetitiveSettings() => new()
    {
        ["perf_increase_threshold"] = 1,
        ["idle_demote_threshold"] = 100,
        ["perf_decrease_threshold"] = 1,
        ["idle_promote_threshold"] = 100,
        ["core_parking_min"] = 100,
        ["core_parking_max"] = 100,
        ["core_parking_concurrency"] = 0,
        ["pcie_aspm"] = 0,
        ["usb_selective_suspend"] = 0,
        ["usb_link_power"] = 0,
        ["proc_min_state"] = 100,
        ["proc_max_state"] = 100,
        ["boost_mode"] = 2,
        ["perf_time_check"] = 5000,
        ["throttle_policy"] = 0,
        ["idle_disable"] = 1,
        ["disk_turn_off"] = 0,
        ["display_turn_off"] = 0,
        ["sleep_after"] = 0,
        ["network_power_saving"] = 0,
        ["cooling_policy"] = 1,
    };

    private static Dictionary<string, int> GetBaseStreamingSettings() => new()
    {
        ["perf_increase_threshold"] = 5,
        ["idle_demote_threshold"] = 80,
        ["core_parking_min"] = 100,
        ["pcie_aspm"] = 0,
        ["usb_selective_suspend"] = 0,
        ["proc_min_state"] = 80,
        ["proc_max_state"] = 100,
        ["boost_mode"] = 1,
        ["perf_time_check"] = 1000,
        ["disk_turn_off"] = 0,
        ["display_turn_off"] = 0,
        ["sleep_after"] = 0,
        ["cooling_policy"] = 1,
    };

    private static Dictionary<string, int> GetBaseBalancedSettings() => new()
    {
        ["perf_increase_threshold"] = 10,
        ["idle_demote_threshold"] = 40,
        ["core_parking_min"] = 50,
        ["pcie_aspm"] = 1,
        ["usb_selective_suspend"] = 0,
        ["proc_min_state"] = 5,
        ["proc_max_state"] = 100,
        ["boost_mode"] = 1,
        ["perf_time_check"] = 15,
        ["disk_turn_off"] = 20,
        ["display_turn_off"] = 10,
        ["sleep_after"] = 30,
        ["cooling_policy"] = 1,
    };

    private static Dictionary<string, int> GetBasePowerSaverSettings() => new()
    {
        ["perf_increase_threshold"] = 30,
        ["idle_demote_threshold"] = 20,
        ["core_parking_min"] = 5,
        ["pcie_aspm"] = 2,
        ["usb_selective_suspend"] = 1,
        ["proc_min_state"] = 5,
        ["proc_max_state"] = 50,
        ["boost_mode"] = 0,
        ["perf_time_check"] = 15,
        ["disk_turn_off"] = 5,
        ["display_turn_off"] = 5,
        ["sleep_after"] = 15,
        ["cooling_policy"] = 0,
    };

    private static Dictionary<string, int> GetBaseIdleSettings() => new()
    {
        ["perf_increase_threshold"] = 50,
        ["idle_demote_threshold"] = 10,
        ["core_parking_min"] = 5,
        ["pcie_aspm"] = 2,
        ["usb_selective_suspend"] = 1,
        ["proc_min_state"] = 5,
        ["proc_max_state"] = 30,
        ["boost_mode"] = 0,
        ["perf_time_check"] = 15,
        ["disk_turn_off"] = 10,
        ["display_turn_off"] = 5,
        ["sleep_after"] = 0,
        ["hibernate_after"] = 0,
        ["cooling_policy"] = 0,
    };
    #endregion

    #region Windows 默认电源计划设置
    private static Dictionary<string, int> GetWindowsBalancedSettings() => new()
    {
        ["perf_increase_threshold"] = 10,
        ["idle_demote_threshold"] = 40,
        ["core_parking_min"] = 50,
        ["pcie_aspm"] = 1,
        ["usb_selective_suspend"] = 1,
        ["proc_min_state"] = 5,
        ["proc_max_state"] = 100,
        ["boost_mode"] = 1,
        ["perf_time_check"] = 15,
        ["disk_turn_off"] = 20,
        ["display_turn_off"] = 10,
        ["sleep_after"] = 30,
        ["cooling_policy"] = 1,
    };

    private static Dictionary<string, int> GetWindowsHighPerformanceSettings() => new()
    {
        ["perf_increase_threshold"] = 10,
        ["idle_demote_threshold"] = 40,
        ["core_parking_min"] = 100,
        ["pcie_aspm"] = 0,
        ["usb_selective_suspend"] = 0,
        ["proc_min_state"] = 100,
        ["proc_max_state"] = 100,
        ["boost_mode"] = 2,
        ["perf_time_check"] = 15,
        ["disk_turn_off"] = 0,
        ["display_turn_off"] = 15,
        ["sleep_after"] = 0,
        ["cooling_policy"] = 1,
    };

    private static Dictionary<string, int> GetWindowsPowerSaverSettings() => new()
    {
        ["perf_increase_threshold"] = 30,
        ["idle_demote_threshold"] = 20,
        ["core_parking_min"] = 5,
        ["pcie_aspm"] = 2,
        ["usb_selective_suspend"] = 1,
        ["proc_min_state"] = 5,
        ["proc_max_state"] = 50,
        ["boost_mode"] = 0,
        ["perf_time_check"] = 15,
        ["disk_turn_off"] = 5,
        ["display_turn_off"] = 5,
        ["sleep_after"] = 15,
        ["cooling_policy"] = 0,
    };
    #endregion

    #region Intel 专属参数
    private static void AddIntelHybridSettings(Dictionary<string, int> settings, bool isHighPerf)
    {
        // Intel 大小核专属设置
        settings["hetero_policy"] = isHighPerf ? 2 : 5; // 2=首选高性能, 5=自动
        settings["class1_eff_threshold"] = isHighPerf ? 1 : 10; // 大核响应阈值
        settings["class1_perf_level"] = 100; // 大核性能级别
    }
    #endregion

    #region AMD 专属参数
    private static void AddAmdSettings(Dictionary<string, int> settings, bool isHighPerf)
    {
        // AMD 专属设置
        settings["hetero_policy"] = 0; // AMD 推荐使用所有处理器
    }
    #endregion

    #region 默认预设（向后兼容）
    public static readonly PowerPreset Gaming = new()
    {
        Id = "gaming", NameKey = "PowerPreset.Gaming", Icon = "🎮",
        DescriptionKey = "PowerPreset.GamingDesc",
        Settings = GetBaseGamingSettings()
    };

    public static readonly PowerPreset Competitive = new()
    {
        Id = "competitive", NameKey = "PowerPreset.Competitive", Icon = "⚔️",
        DescriptionKey = "PowerPreset.CompetitiveDesc",
        Settings = GetBaseCompetitiveSettings()
    };

    public static readonly PowerPreset Streaming = new()
    {
        Id = "streaming", NameKey = "PowerPreset.Streaming", Icon = "📺",
        DescriptionKey = "PowerPreset.StreamingDesc",
        Settings = GetBaseStreamingSettings()
    };

    public static readonly PowerPreset Balanced = new()
    {
        Id = "balanced", NameKey = "PowerPreset.Balanced", Icon = "⚖️",
        DescriptionKey = "PowerPreset.BalancedDesc",
        Settings = GetBaseBalancedSettings()
    };

    public static readonly PowerPreset PowerSaver = new()
    {
        Id = "power_saver", NameKey = "PowerPreset.PowerSaver", Icon = "🔋",
        DescriptionKey = "PowerPreset.PowerSaverDesc",
        Settings = GetBasePowerSaverSettings()
    };

    public static readonly PowerPreset Idle = new()
    {
        Id = "idle", NameKey = "PowerPreset.Idle", Icon = "💤",
        DescriptionKey = "PowerPreset.IdleDesc",
        Settings = GetBaseIdleSettings()
    };

    // Windows 默认电源计划（用于恢复）
    public static readonly PowerPreset WindowsBalanced = new()
    {
        Id = "win_balanced", NameKey = "PowerPreset.WinBalanced", Icon = "⚖️",
        DescriptionKey = "PowerPreset.WinBalancedDesc",
        Settings = GetWindowsBalancedSettings()
    };

    public static readonly PowerPreset WindowsHighPerformance = new()
    {
        Id = "win_high_perf", NameKey = "PowerPreset.WinHighPerf", Icon = "🚀",
        DescriptionKey = "PowerPreset.WinHighPerfDesc",
        Settings = GetWindowsHighPerformanceSettings()
    };

    public static readonly PowerPreset WindowsPowerSaver = new()
    {
        Id = "win_power_saver", NameKey = "PowerPreset.WinPowerSaver", Icon = "🔋",
        DescriptionKey = "PowerPreset.WinPowerSaverDesc",
        Settings = GetWindowsPowerSaverSettings()
    };

    public static IReadOnlyList<PowerPreset> AllPresets { get; } = new[]
    {
        Gaming, Competitive, Streaming, Balanced, PowerSaver, Idle,
        WindowsBalanced, WindowsHighPerformance, WindowsPowerSaver
    };
    #endregion

    #region CPU 特定预设获取
    public static PowerPreset GetPresetForCpu(string presetId, CpuVendor vendor, bool isIntelHybrid = false)
    {
        var baseSettings = presetId switch
        {
            "gaming" => GetBaseGamingSettings(),
            "competitive" => GetBaseCompetitiveSettings(),
            "streaming" => GetBaseStreamingSettings(),
            "balanced" => GetBaseBalancedSettings(),
            "power_saver" => GetBasePowerSaverSettings(),
            "idle" => GetBaseIdleSettings(),
            _ => GetBaseBalancedSettings()
        };

        var basePreset = GetById(presetId) ?? Balanced;
        var isHighPerf = presetId is "gaming" or "competitive";

        // 根据 CPU 类型添加专属参数
        if (vendor == CpuVendor.Intel && isIntelHybrid)
        {
            AddIntelHybridSettings(baseSettings, isHighPerf);
        }
        else if (vendor == CpuVendor.AMD)
        {
            AddAmdSettings(baseSettings, isHighPerf);
        }

        return new PowerPreset
        {
            Id = basePreset.Id,
            NameKey = basePreset.NameKey,
            Icon = basePreset.Icon,
            DescriptionKey = basePreset.DescriptionKey,
            Settings = baseSettings
        };
    }

    public static PowerPreset? GetById(string id) => id switch
    {
        "gaming" => Gaming,
        "competitive" => Competitive,
        "streaming" => Streaming,
        "balanced" => Balanced,
        "power_saver" => PowerSaver,
        "idle" => Idle,
        "win_balanced" => WindowsBalanced,
        "win_high_perf" => WindowsHighPerformance,
        "win_power_saver" => WindowsPowerSaver,
        _ => null
    };
    #endregion
}
