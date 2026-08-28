using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using GameOptimizer.Shell.Models;
using GameOptimizer.Core.PowerManagement;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 电源计划编辑器服务 - 用于读取和修改电源计划参数
/// </summary>
[SupportedOSPlatform("windows")]
public interface IPowerPlanEditorService
{
    string? LastBlockReason { get; }
    bool IsTypedOwnerAvailable { get; }
    PowerPlanInfo? GetActivePowerPlan();
    IReadOnlyList<PowerPlanInfo> GetAllPowerPlans();
    string GetActivePlanGuid();
    bool SetActivePlan(string guidString);
    IReadOnlyList<PowerSettingInfo> GetPowerSettings();
    int? GetPowerSettingValue(Guid subgroupGuid, Guid settingGuid, bool isAc);
    int? GetPowerSettingValue(Guid subgroupGuid, Guid settingGuid, bool isAc, Guid planGuid);
    bool SetPowerSettingValue(Guid subgroupGuid, Guid settingGuid, int value, bool isAc);
    bool SetPowerSettingValueBoth(Guid subgroupGuid, Guid settingGuid, int value);
    bool IsBuiltInPlan(string guidString);
    bool DeletePowerPlan(string guidString);
}

/// <summary>
/// 电源设置信息
/// </summary>
public record PowerSettingInfo(
    string Id,
    string Name,
    string Description,
    Guid SubgroupGuid,
    Guid SettingGuid,
    int MinValue,
    int MaxValue,
    int DefaultValue,
    string Unit,
    Models.PowerSettingCategory Category);

[SupportedOSPlatform("windows")]
public class PowerPlanEditorService : IPowerPlanEditorService
{
    private readonly IPowerManagementOwner? _owner;
    public string? LastBlockReason { get; private set; }
    public bool IsTypedOwnerAvailable => _owner is not null;
    // 常用电源设置 GUID
    public static class PowerGuids
    {
        // 子组 GUID
        public static readonly Guid ProcessorSubgroup = new("54533251-82BE-4824-96C1-47B60B740D00");
        public static readonly Guid HardDiskSubgroup = new("0012EE47-9041-4B5D-9B77-535FBA8B1442");
        public static readonly Guid SleepSubgroup = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
        public static readonly Guid UsbSubgroup = new("2A737441-1930-4402-8D77-B2BEBBA308A3");
        public static readonly Guid DisplaySubgroup = new("7516B95F-F776-4464-8C53-06167F40CC99");

        // 处理器设置
        public static readonly Guid ProcessorMinState = new("893DEE8E-2BEF-41E0-89C6-B55D0929964C");
        public static readonly Guid ProcessorMaxState = new("BC5038F7-23E0-4960-96DA-33ABAF5935EC");
        public static readonly Guid CoreParkingMin = new("0CC5B647-C1DF-4637-891A-DEC35C318583");
        public static readonly Guid CoreParkingMax = new("EA062031-0E34-4FF1-9B6D-EB1059334028");
        public static readonly Guid ProcessorBoostMode = new("BE337238-0D82-4146-A960-4F3749D470C7");
        public static readonly Guid ProcessorBoostPolicy = new("45BCC044-D885-43E2-8605-EE0EC6E96B59");
        
        // 高级处理器设置
        public static readonly Guid ProcessorPerfIncreaseThreshold = new("06cadf0e-64ed-448a-8927-ce7bf90eb35d");
        public static readonly Guid ProcessorIdleDemoteThreshold = new("4b92d758-5a24-4851-a470-815d78aee119");
        public static readonly Guid ProcessorPerfTimeCheck = new("4d2b0152-7d5c-498b-88e2-34345392a2c5");
        public static readonly Guid ProcessorThrottlePolicy = new("3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb");
        
        // 异类线程调度策略 (Intel 大小核)
        public static readonly Guid HeteroThreadSchedulingPolicy = new("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
        public static readonly Guid HeteroShortThreadSchedulingPolicy = new("bae08b81-2d5e-4688-ad6a-13243356654b");
        
        // 第一类处理器设置 (大核/P-Core)
        public static readonly Guid ProcessorClass1EfficiencyThreshold = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        public static readonly Guid ProcessorClass1PerfLevel = new("619b7505-003b-4e82-b7a6-4dd29c300971");
        
        // 系统冷却策略
        public static readonly Guid CoolingPolicy = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");
        
        // 别名 (用于兼容性)
        public static readonly Guid HeteroPolicy = HeteroThreadSchedulingPolicy;
        public static readonly Guid Class1EffThreshold = ProcessorClass1EfficiencyThreshold;
        public static readonly Guid Class1PerfLevel = ProcessorClass1PerfLevel;
        
        // PCIe 设置
        public static readonly Guid PciExpressSubgroup = new("501a4d13-42af-4429-9fd1-a8218c268e20");
        public static readonly Guid PcieLinkStatePowerManagement = new("ee12f906-d166-11d0-87cf-00a0c9110001");

        // 硬盘设置
        public static readonly Guid HardDiskTurnOff = new("6738E2C4-E8A5-4A42-B16A-E040E769756E");

        // 睡眠设置
        public static readonly Guid SleepAfter = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
        public static readonly Guid HibernateAfter = new("9D7815A6-7EE4-497E-8888-515A05F02364");

        // USB 设置
        public static readonly Guid UsbSelectiveSuspend = new("48E6B7A6-50F5-4782-A5D4-53BB8F07E226");

        // 显示设置
        public static readonly Guid DisplayTurnOff = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");
    }

    private readonly List<PowerSettingInfo> _knownSettings;

    private PowerPlanEditorService()
    {
        _knownSettings = BuildKnownSettings();
        LastBlockReason = "Power typed owner is not connected. Legacy powercfg execution is blocked.";
    }

    public PowerPlanEditorService(IPowerManagementOwner owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _knownSettings = BuildKnownSettings();
    }

    private static List<PowerSettingInfo> BuildKnownSettings()
    {
        return new List<PowerSettingInfo>
        {
            new("proc.min_state", "处理器最小状态", "处理器的最低性能状态百分比",
                PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorMinState, 0, 100, 5, "%", Models.PowerSettingCategory.Processor),
            new("proc.max_state", "处理器最大状态", "处理器的最高性能状态百分比",
                PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorMaxState, 0, 100, 100, "%", Models.PowerSettingCategory.Processor),
            new("proc.core_parking_min", "Core Parking 最小核心", "保持活动的最小处理器核心百分比",
                PowerGuids.ProcessorSubgroup, PowerGuids.CoreParkingMin, 0, 100, 100, "%", Models.PowerSettingCategory.Processor),
            new("proc.core_parking_max", "Core Parking 最大核心", "允许活动的最大处理器核心百分比",
                PowerGuids.ProcessorSubgroup, PowerGuids.CoreParkingMax, 0, 100, 100, "%", Models.PowerSettingCategory.Processor),
            new("proc.boost_mode", "处理器性能提升模式", "0=禁用, 1=启用, 2=激进, 3=高效激进",
                PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorBoostMode, 0, 3, 2, "", Models.PowerSettingCategory.Processor),
            new("disk.turn_off", "硬盘休眠时间", "硬盘空闲多久后关闭",
                PowerGuids.HardDiskSubgroup, PowerGuids.HardDiskTurnOff, 0, 9999, 20, "分钟", Models.PowerSettingCategory.HardDisk),
            new("sleep.after", "系统睡眠时间", "系统空闲多久后进入睡眠",
                PowerGuids.SleepSubgroup, PowerGuids.SleepAfter, 0, 9999, 30, "分钟", Models.PowerSettingCategory.Sleep),
            new("sleep.hibernate", "休眠时间", "系统空闲多久后进入休眠",
                PowerGuids.SleepSubgroup, PowerGuids.HibernateAfter, 0, 9999, 180, "分钟", Models.PowerSettingCategory.Sleep),
            new("usb.selective_suspend", "USB 选择性暂停", "禁用可解决 USB 设备断连问题",
                PowerGuids.UsbSubgroup, PowerGuids.UsbSelectiveSuspend, 0, 1, 1, "", Models.PowerSettingCategory.USB),
            new("display.turn_off", "显示器关闭时间", "显示器空闲多久后关闭",
                PowerGuids.DisplaySubgroup, PowerGuids.DisplayTurnOff, 0, 9999, 10, "分钟", Models.PowerSettingCategory.Display)
        };
    }

    public Models.PowerPlanInfo? GetActivePowerPlan()
    {
        if (_owner is null)
        {
            LastBlockReason = "Power typed owner is unavailable; active scheme was not guessed from localized command output.";
            return null;
        }
        try
        {
            var state = _owner.InspectAsync(null, CancellationToken.None).GetAwaiter().GetResult();
            var active = state.Schemes.Single(item => item.Id == state.ActiveSchemeId);
            LastBlockReason = null;
            return new Models.PowerPlanInfo(active.Id, active.Name, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerPlanEditor] GetActivePowerPlan error: {ex.Message}");
        }
        return null;
    }

    public IReadOnlyList<Models.PowerPlanInfo> GetAllPowerPlans()
    {
        if (_owner is null)
        {
            LastBlockReason = "Power typed owner is unavailable; scheme enumeration is blocked.";
            return Array.Empty<Models.PowerPlanInfo>();
        }
        try
        {
            var state = _owner.InspectAsync(null, CancellationToken.None).GetAwaiter().GetResult();
            LastBlockReason = null;
            return state.Schemes.Select(item => new Models.PowerPlanInfo(item.Id, item.Name, item.Id == state.ActiveSchemeId)).ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerPlanEditor] GetAllPowerPlans error: {ex.Message}");
        }
        return Array.Empty<Models.PowerPlanInfo>();
    }

    public string GetActivePlanGuid()
    {
        var plan = GetActivePowerPlan();
        return plan?.Guid ?? "";
    }

    public bool SetActivePlan(string guidString)
    {
        LastBlockReason = "Scheme-only switching has no typed prepare/confirmation/baseline/recovery contract and is blocked.";
        return false;
    }

    public IReadOnlyList<PowerSettingInfo> GetPowerSettings() => _knownSettings;

    public int? GetPowerSettingValue(Guid subgroupGuid, Guid settingGuid, bool isAc)
    {
        var activePlan = GetActivePowerPlan();
        if (activePlan == null) return null;
        return GetPowerSettingValue(subgroupGuid, settingGuid, isAc, System.Guid.Parse(activePlan.Guid));
    }

    // Why: 避免每次查询都启动powercfg获取active plan，支持批量查询时复用plan guid
    public int? GetPowerSettingValue(Guid subgroupGuid, Guid settingGuid, bool isAc, Guid planGuid)
    {
        if (_owner is null)
        {
            LastBlockReason = "Power typed owner is unavailable; setting read is blocked.";
            return null;
        }
        if (subgroupGuid != PowerManagementGuids.DisplaySubgroup || settingGuid != PowerManagementGuids.DisplayIdleTimeout)
        {
            LastBlockReason = "This setting family has not been migrated to the typed Power owner and is blocked.";
            return null;
        }
        try
        {
            var state = _owner.InspectAsync(planGuid, CancellationToken.None).GetAwaiter().GetResult();
            LastBlockReason = null;
            var seconds = isAc ? state.DisplayTimeout.AcSeconds : state.DisplayTimeout.DcSeconds;
            return seconds.HasValue ? checked((int)seconds.Value) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerPlanEditor] GetPowerSettingValue error: {ex.Message}");
        }
        return null;
    }

    public bool SetPowerSettingValue(Guid subgroupGuid, Guid settingGuid, int value, bool isAc)
    {
        LastBlockReason = "Legacy synchronous writes cannot provide the typed confirmation, baseline gate, drift check, or durable receipt and are blocked.";
        return false;
    }

    public bool SetPowerSettingValueBoth(Guid subgroupGuid, Guid settingGuid, int value)
    {
        var acResult = SetPowerSettingValue(subgroupGuid, settingGuid, value, true);
        var dcResult = SetPowerSettingValue(subgroupGuid, settingGuid, value, false);
        return acResult && dcResult;
    }

    // Windows 内置电源计划 GUID（不可删除）
    private static readonly HashSet<string> BuiltInPlanGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "381b4222-f694-41f0-9685-ff5bb260df2e", // 平衡
        "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", // 高性能
        "a1841308-3541-4fab-bc81-f71556f20b4a", // 节能
        "e9a42b02-d5df-448d-aa00-03f14749eb61", // 卓越性能
    };

    public bool IsBuiltInPlan(string guidString)
    {
        return BuiltInPlanGuids.Contains(guidString);
    }

    public bool DeletePowerPlan(string guidString)
    {
        LastBlockReason = "Power scheme deletion is intentionally unsupported because reversible semantics are not proven.";
        return false;
    }

    internal static PowerPlanEditorService CreateFailClosedCompatibility() => new();
}
