using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 验证结果
/// </summary>
public record ValidationResult
{
    public string SettingId { get; init; } = "";
    public string SettingName { get; init; } = "";
    public int ExpectedValue { get; init; }
    public int? ActualValue { get; init; }
    public bool IsSuccess { get; init; }
    public string? FailureReason { get; init; }
}

/// <summary>
/// 验证报告
/// </summary>
public record ValidationReport
{
    public int TotalSettings { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }
    public List<ValidationResult> Results { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public DateTime Timestamp { get; init; } = DateTime.Now;
    
    public bool AllSuccess => FailedCount == 0;
    public double SuccessRate => TotalSettings > 0 ? (double)SuccessCount / TotalSettings * 100 : 0;
}

/// <summary>
/// 电源设置验证服务 - 验证设置是否成功应用
/// </summary>
[SupportedOSPlatform("windows")]
public class PowerSettingValidator
{
    private readonly PowerPlanEditorService _powerService = PowerPlanEditorService.CreateFailClosedCompatibility();

    /// <summary>
    /// 验证单个设置是否生效
    /// </summary>
    public async Task<ValidationResult> ValidateSettingAsync(string settingId, int expectedValue)
    {
        var loc = LocalizationService.Instance;
        var def = PowerSettingsRegistry.GetById(settingId);
        if (def == null)
        {
            return new ValidationResult
            {
                SettingId = settingId,
                SettingName = settingId,
                ExpectedValue = expectedValue,
                IsSuccess = false,
                FailureReason = loc.Get("PowerValidator.UnknownSettingId")
            };
        }

        return await ValidateSettingAsync(def, expectedValue);
    }

    /// <summary>
    /// 验证单个设置是否生效
    /// </summary>
    public async Task<ValidationResult> ValidateSettingAsync(PowerSettingDefinition def, int expectedValue)
    {
        var loc = LocalizationService.Instance;
        try
        {
            var actualValue = await Task.Run(() => 
                _powerService.GetPowerSettingValue(def.SubgroupGuid, def.SettingGuid, true));

            if (!actualValue.HasValue)
            {
                return new ValidationResult
                {
                    SettingId = def.Id,
                    SettingName = def.Name,
                    ExpectedValue = expectedValue,
                    ActualValue = null,
                    IsSuccess = false,
                    FailureReason = loc.Get("PowerValidator.CannotReadValue")
                };
            }

            var isSuccess = actualValue.Value == expectedValue;
            return new ValidationResult
            {
                SettingId = def.Id,
                SettingName = def.Name,
                ExpectedValue = expectedValue,
                ActualValue = actualValue.Value,
                IsSuccess = isSuccess,
                FailureReason = isSuccess ? null : loc.Get("PowerValidator.ValueMismatch", expectedValue, actualValue.Value)
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Validator] ValidateSetting error: {ex.Message}");
            return new ValidationResult
            {
                SettingId = def.Id,
                SettingName = def.Name,
                ExpectedValue = expectedValue,
                IsSuccess = false,
                FailureReason = ex.Message
            };
        }
    }

    /// <summary>
    /// 批量验证设置
    /// </summary>
    public async Task<ValidationReport> ValidateSettingsAsync(Dictionary<string, int> settings)
    {
        var loc = LocalizationService.Instance;
        var results = new List<ValidationResult>();
        var warnings = new List<string>();
        var skipped = 0;

        foreach (var kvp in settings)
        {
            var def = PowerSettingsRegistry.GetById(kvp.Key);
            if (def == null)
            {
                skipped++;
                continue;
            }

            var result = await ValidateSettingAsync(def, kvp.Value);
            results.Add(result);

            // 检查高风险设置
            if (result.IsSuccess && def.Risk == RiskLevel.High)
            {
                warnings.Add(loc.Get("PowerValidator.HighRiskApplied", def.Name));
            }
        }

        return new ValidationReport
        {
            TotalSettings = settings.Count,
            SuccessCount = results.Count(r => r.IsSuccess),
            FailedCount = results.Count(r => !r.IsSuccess),
            SkippedCount = skipped,
            Results = results,
            Warnings = warnings
        };
    }

    /// <summary>
    /// 检查设置兼容性（应用前检查）
    /// </summary>
    public List<string> CheckCompatibility(Dictionary<string, int> settings, HardwareInfo hardware)
    {
        var loc = LocalizationService.Instance;
        var issues = new List<string>();

        foreach (var kvp in settings)
        {
            var def = PowerSettingsRegistry.GetById(kvp.Key);
            if (def == null) continue;

            // CPU 厂商过滤
            var compatible = def.CpuFilter switch
            {
                CpuVendorFilter.IntelOnly => hardware.CpuVendor == CpuVendor.Intel,
                CpuVendorFilter.IntelHybridOnly => hardware.CpuVendor == CpuVendor.Intel && hardware.IsIntelHybrid,
                CpuVendorFilter.AmdOnly => hardware.CpuVendor == CpuVendor.AMD,
                _ => true
            };

            if (!compatible)
            {
                issues.Add(loc.Get("PowerValidator.IncompatibleCpu", def.Name, hardware.CpuVendor));
            }

            // 笔记本电池模式警告
            if (hardware.DeviceType == DeviceType.Laptop && !hardware.IsOnACPower)
            {
                if (def.Risk == RiskLevel.High || def.Risk == RiskLevel.Medium)
                {
                    issues.Add(loc.Get("PowerValidator.BatteryWarning", def.Name));
                }
            }

            // 高风险设置警告
            if (def.Risk == RiskLevel.High && kvp.Value == def.GamingValue)
            {
                issues.Add(loc.Get("PowerValidator.HighRiskWarning", def.Name, def.WarningText ?? loc.Get("PowerValidator.EnsureCooling")));
            }
        }

        return issues;
    }

    /// <summary>
    /// 生成推荐设置（根据硬件自动调整）
    /// </summary>
    public Dictionary<string, int> GenerateRecommendedSettings(HardwareInfo hardware)
    {
        var settings = new Dictionary<string, int>();
        var allSettings = PowerSettingsRegistry.AllSettings;

        foreach (var def in allSettings)
        {
            // CPU 厂商过滤
            var compatible = def.CpuFilter switch
            {
                CpuVendorFilter.IntelOnly => hardware.CpuVendor == CpuVendor.Intel,
                CpuVendorFilter.IntelHybridOnly => hardware.CpuVendor == CpuVendor.Intel && hardware.IsIntelHybrid,
                CpuVendorFilter.AmdOnly => hardware.CpuVendor == CpuVendor.AMD,
                _ => true
            };
            if (!compatible) continue;

            // 根据设备类型和电源状态选择值
            int value;
            if (hardware.DeviceType == DeviceType.Laptop)
            {
                if (hardware.IsOnACPower)
                {
                    // 笔记本插电：中等性能
                    value = def.Risk == RiskLevel.High ? def.DefaultValue : def.GamingValue;
                }
                else
                {
                    // 笔记本电池：保守设置
                    value = def.DefaultValue;
                }
            }
            else
            {
                // 台式机：游戏性能
                value = def.GamingValue;
            }

            settings[def.Id] = value;
        }

        // AMD X3D 特殊处理：不禁用 Core Parking
        if (hardware.IsAmdX3D && settings.ContainsKey("core_parking_min"))
        {
            settings["core_parking_min"] = 50; // X3D 需要 Core Parking 来正确调度
        }

        return settings;
    }
}
