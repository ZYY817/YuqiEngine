using System;
using System.Collections.Generic;
using System.Linq;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

using PSC = PowerSettingCategory;

public static class PowerSettingsRegistry
{

    public static class Guids
    {
        public static readonly Guid Processor = new("54533251-82BE-4824-96C1-47B60B740D00");
        public static readonly Guid HardDisk = new("0012EE47-9041-4B5D-9B77-535FBA8B1442");
        public static readonly Guid Sleep = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
        public static readonly Guid Usb = new("2A737441-1930-4402-8D77-B2BEBBA308A3");
        public static readonly Guid Display = new("7516B95F-F776-4464-8C53-06167F40CC99");
        public static readonly Guid PciExpress = new("501a4d13-42af-4429-9fd1-a8218c268e20");
        public static readonly Guid Graphics = new("5fb4938d-1ee8-4b0f-9a3c-5036b0ab995c");
        public static readonly Guid NetworkAdapter = new("e73a048d-bf27-4f12-9731-8b2076e8891f");
        public static readonly Guid ProcMinState = new("893DEE8E-2BEF-41E0-89C6-B55D0929964C");
        public static readonly Guid ProcMaxState = new("BC5038F7-23E0-4960-96DA-33ABAF5935EC");
        public static readonly Guid CoreParkingMin = new("0CC5B647-C1DF-4637-891A-DEC35C318583");
        public static readonly Guid CoreParkingMax = new("EA062031-0E34-4FF1-9B6D-EB1059334028");
        public static readonly Guid BoostMode = new("BE337238-0D82-4146-A960-4F3749D470C7");
        public static readonly Guid PerfIncreaseThreshold = new("06cadf0e-64ed-448a-8927-ce7bf90eb35d");
        public static readonly Guid PerfDecreaseThreshold = new("12a0ab44-fe28-4fa9-b3bd-4b64f44960a6");
        public static readonly Guid IdleDemoteThreshold = new("4b92d758-5a24-4851-a470-815d78aee119");
        public static readonly Guid IdlePromoteThreshold = new("7b224883-b3cc-4d79-819f-8374152cbe7c");
        public static readonly Guid PerfTimeCheck = new("4d2b0152-7d5c-498b-88e2-34345392a2c5");
        public static readonly Guid ThrottlePolicy = new("3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb");
        public static readonly Guid IdleDisable = new("5d76a2ca-e8c0-402f-a133-2158492d58ad");
        public static readonly Guid AutonomousMode = new("8baa4a8a-14c6-4451-8e8b-14bdbd197537");
        public static readonly Guid HeteroPolicy = new("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
        public static readonly Guid HeteroShortPolicy = new("bae08b81-2d5e-4688-ad6a-13243356654b");
        public static readonly Guid Class1EffThreshold = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        public static readonly Guid Class1PerfLevel = new("619b7505-003b-4e82-b7a6-4dd29c300971");
        public static readonly Guid Class0EffThreshold = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");
        public static readonly Guid Class0PerfLevel = new("619b7505-003b-4e82-b7a6-4dd29c300972");
        public static readonly Guid PreferredParkingClass = new("2ddd5a84-5a71-437e-912a-db0b8c788732");
        public static readonly Guid CoreParkingConcurrency = new("2430ab6f-a520-44a2-9601-f7f23b5134b1");
        public static readonly Guid CoreParkingDistribution = new("4bdaf4e9-d103-46d7-a5f0-6280121616ef");
        public static readonly Guid PcieAspm = new("ee12f906-d166-11d0-87cf-00a0c9110001");
        public static readonly Guid UsbSelectiveSuspend = new("48E6B7A6-50F5-4782-A5D4-53BB8F07E226");
        public static readonly Guid UsbLinkPowerMgmt = new("d4e98f31-5ffe-4ce1-be31-1b38b384c009");
        public static readonly Guid DiskTurnOff = new("6738E2C4-E8A5-4A42-B16A-E040E769756E");
        public static readonly Guid SleepAfter = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
        public static readonly Guid HibernateAfter = new("9D7815A6-7EE4-497E-8888-515A05F02364");
        public static readonly Guid HybridSleep = new("94ac6d29-73ce-41a6-809f-6363ba21b47e");
        public static readonly Guid WakeTimers = new("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d");
        public static readonly Guid DisplayTurnOff = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");
        public static readonly Guid CoolingPolicy = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");
        public static readonly Guid GpuPowerPolicy = new("dd848b2a-8a5d-4451-9ae2-39cd41658f6c");
        public static readonly Guid NetworkPowerSaving = new("245d8541-3943-4422-b025-13a784f679b7");
    }
    private static readonly List<PowerSettingDefinition> _allSettings = new()
    {
        new() { Id = "perf_increase_threshold", Name = "性能提升阈值", Category = PSC.LowLatency,
            Description = "CPU使用率达到此阈值时触发睿频", SubgroupGuid = Guids.Processor, SettingGuid = Guids.PerfIncreaseThreshold,
            Type = PowerSettingType.Slider, MinValue = 1, MaxValue = 100, DefaultValue = 10, GamingValue = 1, Unit = "%", Risk = RiskLevel.Safe },
        new() { Id = "idle_demote_threshold", Name = "闲置降级阈值", Category = PSC.LowLatency,
            Description = "CPU使用率低于此阈值时降频", SubgroupGuid = Guids.Processor, SettingGuid = Guids.IdleDemoteThreshold,
            Type = PowerSettingType.Slider, MinValue = 1, MaxValue = 100, DefaultValue = 40, GamingValue = 100, Unit = "%", Risk = RiskLevel.Safe },
        new() { Id = "core_parking_min", Name = "Core Parking最小核心", Category = PSC.LowLatency,
            Description = "保持活动的最小核心百分比", SubgroupGuid = Guids.Processor, SettingGuid = Guids.CoreParkingMin,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 100, DefaultValue = 100, GamingValue = 100, Unit = "%", Risk = RiskLevel.Safe },
        new() { Id = "pcie_aspm", Name = "PCIe ASPM节能", Category = PSC.LowLatency,
            Description = "禁用可降低显卡和NVMe延迟", WarningText = "禁用后功耗略增", SubgroupGuid = Guids.PciExpress, SettingGuid = Guids.PcieAspm,
            Type = PowerSettingType.Select, MinValue = 0, MaxValue = 2, DefaultValue = 1, GamingValue = 0,
            SelectOptions = new[] { "关闭", "中等节能", "最大节能" }, Risk = RiskLevel.Safe },
        new() { Id = "usb_selective_suspend", Name = "USB选择性暂停", Category = PSC.LowLatency,
            Description = "禁用可解决USB设备断连问题", SubgroupGuid = Guids.Usb, SettingGuid = Guids.UsbSelectiveSuspend,
            Type = PowerSettingType.Toggle, MinValue = 0, MaxValue = 1, DefaultValue = 1, GamingValue = 0, Risk = RiskLevel.Safe },
        new() { Id = "proc_min_state", Name = "处理器最小状态", Category = PSC.Processor,
            Description = "CPU的最低性能状态", WarningText = "100%会导致CPU始终满频运行", SubgroupGuid = Guids.Processor, SettingGuid = Guids.ProcMinState,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 100, DefaultValue = 5, GamingValue = 100, Unit = "%", Risk = RiskLevel.Medium },
        new() { Id = "proc_max_state", Name = "处理器最大状态", Category = PSC.Processor,
            Description = "CPU的最高性能状态", SubgroupGuid = Guids.Processor, SettingGuid = Guids.ProcMaxState,
            Type = PowerSettingType.Slider, MinValue = 5, MaxValue = 100, DefaultValue = 100, GamingValue = 100, Unit = "%", Risk = RiskLevel.Safe },
        new() { Id = "boost_mode", Name = "处理器性能提升模式", Category = PSC.Processor,
            Description = "控制CPU睿频行为", SubgroupGuid = Guids.Processor, SettingGuid = Guids.BoostMode,
            Type = PowerSettingType.Select, MinValue = 0, MaxValue = 3, DefaultValue = 2, GamingValue = 2,
            SelectOptions = new[] { "禁用", "启用", "激进", "高效激进" }, Risk = RiskLevel.Safe },
        new() { Id = "core_parking_max", Name = "Core Parking最大核心", Category = PSC.Processor,
            Description = "允许活动的最大核心百分比", SubgroupGuid = Guids.Processor, SettingGuid = Guids.CoreParkingMax,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 100, DefaultValue = 100, GamingValue = 100, Unit = "%", Risk = RiskLevel.Safe },
        new() { Id = "throttle_policy", Name = "CPU节流保护", Category = PSC.Processor,
            Description = "禁用后CPU不会因温度限制降频", WarningText = "禁用需确保散热良好", SubgroupGuid = Guids.Processor, SettingGuid = Guids.ThrottlePolicy,
            Type = PowerSettingType.Toggle, MinValue = 0, MaxValue = 1, DefaultValue = 1, GamingValue = 0, Risk = RiskLevel.High },
        new() { Id = "perf_decrease_threshold", Name = "性能降低阈值", Category = PSC.ProcessorAdvanced,
            Description = "CPU使用率低于此阈值时降低频率", SubgroupGuid = Guids.Processor, SettingGuid = Guids.PerfDecreaseThreshold,
            Type = PowerSettingType.Slider, MinValue = 1, MaxValue = 100, DefaultValue = 10, GamingValue = 1, Unit = "%", Risk = RiskLevel.Safe },
        new() { Id = "idle_promote_threshold", Name = "闲置提升阈值", Category = PSC.ProcessorAdvanced,
            Description = "空闲时提升到更高C-State的阈值", SubgroupGuid = Guids.Processor, SettingGuid = Guids.IdlePromoteThreshold,
            Type = PowerSettingType.Slider, MinValue = 1, MaxValue = 100, DefaultValue = 60, GamingValue = 100, Unit = "%", Risk = RiskLevel.Safe },
        new() { Id = "perf_time_check", Name = "性能时间检查间隔", Category = PSC.ProcessorAdvanced,
            Description = "CPU频率调整检查间隔", SubgroupGuid = Guids.Processor, SettingGuid = Guids.PerfTimeCheck,
            Type = PowerSettingType.Slider, MinValue = 1, MaxValue = 5000, DefaultValue = 15, GamingValue = 5000, Unit = "ms", Risk = RiskLevel.Safe },
        new() { Id = "idle_disable", Name = "处理器空闲状态禁用", Category = PSC.ProcessorAdvanced,
            Description = "禁用C-States可降低延迟但增加功耗", WarningText = "禁用会显著增加功耗和发热", SubgroupGuid = Guids.Processor, SettingGuid = Guids.IdleDisable,
            Type = PowerSettingType.Toggle, MinValue = 0, MaxValue = 1, DefaultValue = 0, GamingValue = 1, Risk = RiskLevel.High },
        new() { Id = "autonomous_mode", Name = "处理器自主模式", Category = PSC.ProcessorAdvanced,
            Description = "Intel Speed Shift技术", SubgroupGuid = Guids.Processor, SettingGuid = Guids.AutonomousMode,
            Type = PowerSettingType.Toggle, MinValue = 0, MaxValue = 1, DefaultValue = 1, GamingValue = 1, Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.IntelOnly },
        new() { Id = "core_parking_concurrency", Name = "Core Parking并发阈值", Category = PSC.ProcessorAdvanced,
            Description = "并发负载触发核心唤醒的阈值", SubgroupGuid = Guids.Processor, SettingGuid = Guids.CoreParkingConcurrency,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 100, DefaultValue = 50, GamingValue = 0, Unit = "%", Risk = RiskLevel.Safe },        new() { Id = "hetero_policy", Name = "异类线程调度策略", Category = PSC.IntelHybrid,
            Description = "Intel大小核调度策略", SubgroupGuid = Guids.Processor, SettingGuid = Guids.HeteroPolicy,
            Type = PowerSettingType.Select, MinValue = 0, MaxValue = 5, DefaultValue = 5, GamingValue = 2,
            SelectOptions = new[] { "所有处理器", "首选高效", "首选高性能", "自动高效", "自动高性能", "自动" },
            Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.IntelHybridOnly },
        new() { Id = "hetero_short_policy", Name = "短线程调度策略", Category = PSC.IntelHybrid,
            Description = "短任务的调度策略", SubgroupGuid = Guids.Processor, SettingGuid = Guids.HeteroShortPolicy,
            Type = PowerSettingType.Select, MinValue = 0, MaxValue = 5, DefaultValue = 5, GamingValue = 2,
            SelectOptions = new[] { "所有处理器", "首选高效", "首选高性能", "自动高效", "自动高性能", "自动" },
            Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.IntelHybridOnly },
        new() { Id = "class1_eff_threshold", Name = "大核效率阈值", Category = PSC.IntelHybrid,
            Description = "大核响应负载变化的阈值", SubgroupGuid = Guids.Processor, SettingGuid = Guids.Class1EffThreshold,
            Type = PowerSettingType.Slider, MinValue = 1, MaxValue = 100, DefaultValue = 10, GamingValue = 1, Unit = "%", Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.IntelHybridOnly },
        new() { Id = "class1_perf_level", Name = "大核性能级别", Category = PSC.IntelHybrid,
            Description = "大核的最高性能级别", SubgroupGuid = Guids.Processor, SettingGuid = Guids.Class1PerfLevel,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 100, DefaultValue = 100, GamingValue = 100, Unit = "%", Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.IntelHybridOnly },
        new() { Id = "class0_eff_threshold", Name = "小核效率阈值", Category = PSC.IntelHybrid,
            Description = "小核响应负载变化的阈值", SubgroupGuid = Guids.Processor, SettingGuid = Guids.Class0EffThreshold,
            Type = PowerSettingType.Slider, MinValue = 1, MaxValue = 100, DefaultValue = 10, GamingValue = 50, Unit = "%", Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.IntelHybridOnly },
        new() { Id = "class0_perf_level", Name = "小核性能级别", Category = PSC.IntelHybrid,
            Description = "小核的最高性能级别", SubgroupGuid = Guids.Processor, SettingGuid = Guids.Class0PerfLevel,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 100, DefaultValue = 100, GamingValue = 100, Unit = "%", Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.IntelHybridOnly },
        new() { Id = "preferred_parking_class", Name = "首选核心停车类", Category = PSC.IntelHybrid,
            Description = "优先停车哪类核心", SubgroupGuid = Guids.Processor, SettingGuid = Guids.PreferredParkingClass,
            Type = PowerSettingType.Select, MinValue = 0, MaxValue = 1, DefaultValue = 0, GamingValue = 0,
            SelectOptions = new[] { "小核优先停车", "大核优先停车" }, Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.IntelHybridOnly },
        new() { Id = "amd_hetero_policy", Name = "异类线程调度策略", Category = PSC.Amd,
            Description = "AMD推荐选择所有处理器", SubgroupGuid = Guids.Processor, SettingGuid = Guids.HeteroPolicy,
            Type = PowerSettingType.Select, MinValue = 0, MaxValue = 5, DefaultValue = 5, GamingValue = 0,
            SelectOptions = new[] { "所有处理器", "首选高效", "首选高性能", "自动高效", "自动高性能", "自动" },
            Risk = RiskLevel.Safe, CpuFilter = CpuVendorFilter.AmdOnly },
        new() { Id = "disk_turn_off", Name = "硬盘休眠时间", Category = PSC.System,
            Description = "硬盘空闲多久后关闭", SubgroupGuid = Guids.HardDisk, SettingGuid = Guids.DiskTurnOff,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 60, DefaultValue = 20, GamingValue = 0, Unit = "分钟", Risk = RiskLevel.Safe },
        new() { Id = "sleep_after", Name = "系统睡眠时间", Category = PSC.System,
            Description = "系统空闲多久后进入睡眠", SubgroupGuid = Guids.Sleep, SettingGuid = Guids.SleepAfter,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 120, DefaultValue = 30, GamingValue = 0, Unit = "分钟", Risk = RiskLevel.Safe },
        new() { Id = "hibernate_after", Name = "休眠时间", Category = PSC.System,
            Description = "系统空闲多久后进入休眠", SubgroupGuid = Guids.Sleep, SettingGuid = Guids.HibernateAfter,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 360, DefaultValue = 180, GamingValue = 0, Unit = "分钟", Risk = RiskLevel.Safe },
        new() { Id = "hybrid_sleep", Name = "混合睡眠", Category = PSC.System,
            Description = "睡眠时同时写入休眠文件", SubgroupGuid = Guids.Sleep, SettingGuid = Guids.HybridSleep,
            Type = PowerSettingType.Toggle, MinValue = 0, MaxValue = 1, DefaultValue = 1, GamingValue = 0, Risk = RiskLevel.Safe },
        new() { Id = "wake_timers", Name = "唤醒定时器", Category = PSC.System,
            Description = "允许计划任务唤醒计算机", SubgroupGuid = Guids.Sleep, SettingGuid = Guids.WakeTimers,
            Type = PowerSettingType.Toggle, MinValue = 0, MaxValue = 1, DefaultValue = 1, GamingValue = 0, Risk = RiskLevel.Safe },
        new() { Id = "cooling_policy", Name = "系统冷却策略", Category = PSC.System,
            Description = "主动=风扇先转，被动=先降频", SubgroupGuid = Guids.Processor, SettingGuid = Guids.CoolingPolicy,
            Type = PowerSettingType.Select, MinValue = 0, MaxValue = 1, DefaultValue = 1, GamingValue = 1,
            SelectOptions = new[] { "被动", "主动" }, Risk = RiskLevel.Safe },
        new() { Id = "usb_link_power", Name = "USB链接电源管理", Category = PSC.System,
            Description = "USB 3.0链接电源管理", SubgroupGuid = Guids.Usb, SettingGuid = Guids.UsbLinkPowerMgmt,
            Type = PowerSettingType.Toggle, MinValue = 0, MaxValue = 1, DefaultValue = 1, GamingValue = 0, Risk = RiskLevel.Safe },
        new() { Id = "display_turn_off", Name = "显示器关闭时间", Category = PSC.Display,
            Description = "显示器空闲多久后关闭", SubgroupGuid = Guids.Display, SettingGuid = Guids.DisplayTurnOff,
            Type = PowerSettingType.Slider, MinValue = 0, MaxValue = 60, DefaultValue = 10, GamingValue = 0, Unit = "分钟", Risk = RiskLevel.Safe },
        new() { Id = "network_power_saving", Name = "网卡节能模式", Category = PSC.Network,
            Description = "禁用可降低网络延迟", SubgroupGuid = Guids.NetworkAdapter, SettingGuid = Guids.NetworkPowerSaving,
            Type = PowerSettingType.Toggle, MinValue = 0, MaxValue = 1, DefaultValue = 1, GamingValue = 0, Risk = RiskLevel.Safe },
        new() { Id = "gpu_power_policy", Name = "GPU首选电源策略", Category = PSC.Other,
            Description = "显卡电源策略", SubgroupGuid = Guids.Graphics, SettingGuid = Guids.GpuPowerPolicy,
            Type = PowerSettingType.Select, MinValue = 0, MaxValue = 2, DefaultValue = 0, GamingValue = 1,
            SelectOptions = new[] { "自动", "最高性能", "省电" }, Risk = RiskLevel.Safe },
    };

    public static IReadOnlyList<PowerSettingDefinition> AllSettings => _allSettings;

    public static IEnumerable<PowerSettingDefinition> GetByCategory(PowerSettingCategory category)
        => _allSettings.Where(s => s.Category == category);

    public static IEnumerable<PowerSettingDefinition> GetForCpu(CpuVendor vendor, bool isHybrid)
    {
        return _allSettings.Where(s => s.CpuFilter switch
        {
            CpuVendorFilter.All => true,
            CpuVendorFilter.IntelOnly => vendor == CpuVendor.Intel,
            CpuVendorFilter.IntelHybridOnly => vendor == CpuVendor.Intel && isHybrid,
            CpuVendorFilter.AmdOnly => vendor == CpuVendor.AMD,
            _ => true
        });
    }

    public static PowerSettingDefinition? GetById(string id)
        => _allSettings.FirstOrDefault(s => s.Id == id);
}