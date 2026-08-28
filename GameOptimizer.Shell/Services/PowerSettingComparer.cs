// File: GameOptimizer.Shell/Services/PowerSettingComparer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 电源参数对比服务
/// </summary>
public class PowerSettingComparer
{
    private readonly PowerPlanEditorService _editorService = PowerPlanEditorService.CreateFailClosedCompatibility();
    private readonly LocalizationService _localization = LocalizationService.Instance;

    /// <summary>
    /// 对比当前电源参数与场景预设参数
    /// </summary>
    public async Task<List<PowerSettingComparison>> CompareSettingsAsync(Dictionary<string, int> scenarioSettings)
    {
        var currentSettings = await GetCurrentSettingsAsync();
        var comparisons = new List<PowerSettingComparison>();

        foreach (var (key, newValue) in scenarioSettings)
        {
            var currentValue = currentSettings.GetValueOrDefault(key, -1);
            var settingInfo = GetSettingMetadata(key);

            comparisons.Add(new PowerSettingComparison
            {
                SettingKey = key,
                DisplayName = _localization.Get($"PowerSetting.{key}.Name"),
                Category = settingInfo.Category,
                CurrentValue = currentValue,
                NewValue = newValue,
                CurrentDisplayValue = FormatValue(currentValue, settingInfo.Unit, key),
                NewDisplayValue = FormatValue(newValue, settingInfo.Unit, key),
                Direction = GetChangeDirection(currentValue, newValue)
            });
        }

        return comparisons
            .OrderBy(c => GetCategoryOrder(c.Category))
            .ThenBy(c => c.DisplayName)
            .ToList();
    }

    /// <summary>
    /// 获取当前电源计划的所有参数
    /// </summary>
    private async Task<Dictionary<string, int>> GetCurrentSettingsAsync()
    {
        var settings = new Dictionary<string, int>();

        try
        {
            // 处理器设置
            settings["proc_min_state"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorMinState) ?? 5;

            settings["proc_max_state"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorMaxState) ?? 100;

            settings["core_parking_min"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.CoreParkingMin) ?? 0;

            settings["boost_mode"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorBoostMode) ?? 1;

            // 低延迟设置
            settings["perf_increase_threshold"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorPerfIncreaseThreshold) ?? 10;

            settings["idle_demote_threshold"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorIdleDemoteThreshold) ?? 40;

            settings["perf_time_check"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorPerfTimeCheck) ?? 15;

            // Intel 混合架构设置
            settings["hetero_policy"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.HeteroPolicy) ?? -1;

            settings["class1_eff_threshold"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.Class1EffThreshold) ?? -1;

            settings["class1_perf_level"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.Class1PerfLevel) ?? -1;

            // 系统设置
            settings["cooling_policy"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.CoolingPolicy) ?? 1;

            // 硬件设置
            settings["pcie_aspm"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.PciExpressSubgroup,
                PowerPlanEditorService.PowerGuids.PcieLinkStatePowerManagement) ?? 1;

            settings["usb_selective_suspend"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.UsbSubgroup,
                PowerPlanEditorService.PowerGuids.UsbSelectiveSuspend) ?? 0;

            // 节能设置
            settings["disk_turn_off"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.HardDiskSubgroup,
                PowerPlanEditorService.PowerGuids.HardDiskTurnOff) ?? 20;

            settings["display_turn_off"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.DisplaySubgroup,
                PowerPlanEditorService.PowerGuids.DisplayTurnOff) ?? 10;

            settings["sleep_after"] = await GetSettingValueAsync(
                PowerPlanEditorService.PowerGuids.SleepSubgroup,
                PowerPlanEditorService.PowerGuids.SleepAfter) ?? 30;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PowerSettingComparer] GetCurrentSettings error: {ex.Message}");
        }

        return settings;
    }

    private async Task<int?> GetSettingValueAsync(Guid subgroup, Guid setting)
    {
        return await Task.Run(() => _editorService.GetPowerSettingValue(subgroup, setting, true));
    }

    private ChangeDirection GetChangeDirection(int currentValue, int newValue)
    {
        if (currentValue == newValue) return ChangeDirection.NoChange;
        return newValue > currentValue ? ChangeDirection.Increase : ChangeDirection.Decrease;
    }

    private string FormatValue(int value, string unit, string key = "")
    {
        if (value < 0) return _localization.Get("Common.Unknown");

        return unit switch
        {
            "%" => $"{value}%",
            "min" => value == 0 ? _localization.Get("Common.Never") : $"{value} " + _localization.Get("Common.Minutes"),
            "ms" => $"{value} ms",
            "bool" => value == 0 ? _localization.Get("Common.Disabled") : _localization.Get("Common.Enabled"),
            "aspm" => value switch
            {
                0 => _localization.Get("PowerSetting.AspmOff"),
                1 => _localization.Get("PowerSetting.AspmModerate"),
                2 => _localization.Get("PowerSetting.AspmMaximum"),
                _ => value.ToString()
            },
            "hetero" => value switch
            {
                0 => _localization.Get("PowerSetting.HeteroPolicy.AllProcessors"),
                1 => _localization.Get("PowerSetting.HeteroPolicy.PreferEfficient"),
                2 => _localization.Get("PowerSetting.HeteroPolicy.PreferPerformance"),
                3 => _localization.Get("PowerSetting.HeteroPolicy.AutoEfficient"),
                4 => _localization.Get("PowerSetting.HeteroPolicy.AutoPerformance"),
                5 => _localization.Get("PowerSetting.HeteroPolicy.Auto"),
                _ => value.ToString()
            },
            "cooling" => value switch
            {
                0 => _localization.Get("PowerSetting.CoolingPolicy.Passive"),
                1 => _localization.Get("PowerSetting.CoolingPolicy.Active"),
                _ => value.ToString()
            },
            _ => value.ToString()
        };
    }

    private (string Category, string Unit) GetSettingMetadata(string key)
    {
        return key switch
        {
            "proc_min_state" or "proc_max_state" or "core_parking_min" => 
                (_localization.Get("PowerScenario.Category.Processor"), "%"),
            "perf_increase_threshold" or "idle_demote_threshold" => 
                (_localization.Get("PowerScenario.Category.LowLatency"), "%"),
            "perf_time_check" => 
                (_localization.Get("PowerScenario.Category.LowLatency"), "ms"),
            "hetero_policy" => 
                (_localization.Get("PowerScenario.Category.IntelHybrid"), "hetero"),
            "class1_eff_threshold" or "class1_perf_level" => 
                (_localization.Get("PowerScenario.Category.IntelHybrid"), "%"),
            "cooling_policy" => 
                (_localization.Get("PowerScenario.Category.System"), "cooling"),
            "pcie_aspm" => 
                (_localization.Get("PowerScenario.Category.Hardware"), "aspm"),
            "usb_selective_suspend" => 
                (_localization.Get("PowerScenario.Category.Hardware"), "bool"),
            "disk_turn_off" or "display_turn_off" or "sleep_after" => 
                (_localization.Get("PowerScenario.Category.PowerSaving"), "min"),
            "boost_mode" => 
                (_localization.Get("PowerScenario.Category.Processor"), ""),
            _ => (_localization.Get("PowerScenario.Category.Other"), "")
        };
    }

    private int GetCategoryOrder(string category)
    {
        var processorCat = _localization.Get("PowerScenario.Category.Processor");
        var lowLatencyCat = _localization.Get("PowerScenario.Category.LowLatency");
        var hardwareCat = _localization.Get("PowerScenario.Category.Hardware");
        var powerSavingCat = _localization.Get("PowerScenario.Category.PowerSaving");

        if (category == processorCat) return 1;
        if (category == lowLatencyCat) return 2;
        if (category == hardwareCat) return 3;
        if (category == powerSavingCat) return 4;
        return 5;
    }
}
