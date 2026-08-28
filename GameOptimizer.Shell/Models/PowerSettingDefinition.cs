using System;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Models;

public enum PowerSettingType { Slider, Toggle, Select }
public enum RiskLevel { Safe, Medium, High }
public enum CpuVendorFilter { All, IntelOnly, IntelHybridOnly, AmdOnly }
public enum PowerSettingCategory { LowLatency, Processor, ProcessorAdvanced, IntelHybrid, Amd, System, Network, Display, HardDisk, Sleep, USB, Other }

public record PowerSettingDefinition
{
    private static LocalizationService Loc => LocalizationService.Instance;
    
    public string Id { get; init; } = "";
    
    // 本地化属性 - 运行时从LocalizationService获取
    public string LocalizedName => Loc.Get($"PowerSetting.{Id}.Name");
    public string LocalizedDescription => Loc.Get($"PowerSetting.{Id}.Desc");
    public string? LocalizedWarning
    {
        get
        {
            var key = $"PowerSetting.{Id}.Warning";
            var val = Loc.Get(key);
            return val == key ? null : val;
        }
    }
    public string[]? LocalizedSelectOptions
    {
        get
        {
            var key = $"PowerSetting.{Id}.Options";
            var val = Loc.Get(key);
            if (val == key) return null;
            return val.Split('|');
        }
    }
    public string LocalizedUnit
    {
        get
        {
            var key = $"PowerSetting.{Id}.Unit";
            var val = Loc.Get(key);
            return val == key ? "" : val;
        }
    }
    
    // 保留原始属性用于内部逻辑（如果需要）
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? WarningText { get; init; }
    public Guid SubgroupGuid { get; init; }
    public Guid SettingGuid { get; init; }
    public PowerSettingType Type { get; init; }
    public int MinValue { get; init; }
    public int MaxValue { get; init; }
    public int DefaultValue { get; init; }
    public int GamingValue { get; init; }
    public string Unit { get; init; } = "";
    public RiskLevel Risk { get; init; }
    public CpuVendorFilter CpuFilter { get; init; } = CpuVendorFilter.All;
    public PowerSettingCategory Category { get; init; }
    public string[]? SelectOptions { get; init; }
}
