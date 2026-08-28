using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Optimization.Operations;

namespace GameOptimizer.Core.Services;

public enum PowerSettingGuardDecision
{
    Allowed,
    SkippedUnsupported,
    BlockedRisk
}

public sealed class PowerSettingGuardResult
{
    public PowerSettingGuardDecision Decision { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool IsAllowed => Decision == PowerSettingGuardDecision.Allowed;
}

public sealed record PowerHardwareFacts(
    CpuTopology Topology,
    bool IsPortable,
    bool HasBattery,
    bool IsOnAcPower,
    bool IsModernStandby,
    bool IsOemOrPolicyRestricted,
    bool IsDetectionReliable);

public interface IPowerHardwareFactsProvider
{
    PowerHardwareFacts GetCurrent();
}

public interface IPowerSettingSupportProbe
{
    Task<(int? Ac, int? Dc)> ReadAsync(Guid scheme, Guid subgroup, Guid setting, CancellationToken cancellationToken);
}

public sealed class PowerCfgSettingSupportProbe : IPowerSettingSupportProbe
{
    public Task<(int? Ac, int? Dc)> ReadAsync(Guid scheme, Guid subgroup, Guid setting, CancellationToken cancellationToken) =>
        PowerCfgUtility.QuerySettingValueAsync(scheme, subgroup, setting, cancellationToken);
}

public sealed class PowerSettingCapabilityGuard
{
    private static readonly Guid IntelIdleDisableSetting = new("5D76A2CA-E8C0-402F-A133-2158492D58AD");

    private readonly CpuTopologyService _cpuTopologyService = new();
    private readonly IPowerHardwareFactsProvider? _factsProvider;
    private readonly IPowerSettingSupportProbe _supportProbe;
    private CpuTopology? _cachedTopology;
    private DevicePowerContext? _cachedDeviceContext;

    public PowerSettingCapabilityGuard(
        IPowerHardwareFactsProvider? factsProvider = null,
        IPowerSettingSupportProbe? supportProbe = null)
    {
        _factsProvider = factsProvider;
        _supportProbe = supportProbe ?? new PowerCfgSettingSupportProbe();
    }

    public void Refresh()
    {
        _cachedTopology = null;
        _cachedDeviceContext = null;
    }

    public async Task<PowerSettingGuardResult> EvaluateAsync(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        int? targetAc,
        int? targetDc,
        string? operationId = null,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        if (subgroup != PowerGuids.ProcessorSubgroup)
        {
            return Allow();
        }

        var displayName = string.IsNullOrWhiteSpace(label)
            ? (string.IsNullOrWhiteSpace(operationId) ? setting.ToString("D") : operationId)
            : label;

        var facts = GetFactsSafe();
        var topology = facts.Topology;
        var device = new DevicePowerContext(facts.IsPortable, facts.HasBattery, facts.IsOnAcPower,
            facts.IsModernStandby, facts.IsDetectionReliable);

        if (topology.Vendor == CpuVendor.Unknown || string.IsNullOrWhiteSpace(topology.CpuName)
            || topology.CpuName.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return BlockRisk($"已拦截“{displayName}”：CPU 厂商或型号无法可靠识别，处理器电源设置按 fail-closed 处理。");
        }

        if (!device.IsDetectionReliable)
        {
            return BlockRisk($"已拦截“{displayName}”：机型、供电或 Modern Standby 状态检测不完整，处理器电源设置按 fail-closed 处理。");
        }

        if (device.IsModernStandby)
        {
            return BlockRisk($"已拦截“{displayName}”：Modern Standby/OEM 电源语义未经过该能力验证。");
        }

        if (facts.IsOemOrPolicyRestricted)
        {
            return BlockRisk($"已拦截“{displayName}”：检测到 OEM 或组织策略接管电源设置，禁止覆盖。");
        }

        if (topology.ProcessorGroupCount > 1 && IsTopologySensitive(setting, operationId))
        {
            return BlockRisk($"已拦截“{displayName}”：检测到 {topology.ProcessorGroupCount} 个 processor groups，当前能力不能可靠表达跨组拓扑策略。");
        }

        if (RequiresIntelHybrid(setting) && topology.Architecture != CpuArchitecture.IntelHybrid)
        {
            return BlockRisk($"已拦截“{displayName}”：该设置仅适用于 Intel 大小核处理器，当前 CPU 架构不支持。");
        }

        if (IsIntelHeteroPolicy(setting) && topology.Architecture != CpuArchitecture.IntelHybrid && topology.Vendor != CpuVendor.AMD)
        {
            return BlockRisk($"已拦截“{displayName}”：异类线程调度策略仅适用于 Intel 大小核或已明确暴露该项的 AMD 平台，当前 CPU 架构不适用。");
        }

        if (RequiresIntelCpu(setting, operationId) && topology.Vendor != CpuVendor.Intel)
        {
            return BlockRisk($"已拦截“{displayName}”：该功能仅适用于 Intel 处理器，当前 CPU 不是 Intel。");
        }

        if (RequiresAmdCpu(operationId) && topology.Vendor != CpuVendor.AMD)
        {
            return BlockRisk($"已拦截“{displayName}”：该功能仅适用于 AMD 处理器，当前 CPU 不是 AMD。");
        }

        var (currentAc, currentDc) = await _supportProbe.ReadAsync(scheme, subgroup, setting, cancellationToken).ConfigureAwait(false);
        if (currentAc is null && currentDc is null)
        {
            return SkipUnsupported($"已跳过“{displayName}”：当前电源计划未公开这个处理器设置，常见于 OEM 笔记本、Modern Standby 或受限电源方案。为了避免误写，已安全拦截。");
        }

        // 已通过机型、权限和目标设置的实际可读性检测后，100% 最小状态、核心停车、
        // Boost/P-State/C-State 等均是可回读的活动电源方案参数。它们不再被“风险”本身
        // 静态禁用：执行协调器会在写入前持久化原值、要求显式确认，并在写后验证或补偿。
        // 真正不适用（架构不匹配）、无法读取或 OEM/策略接管仍在上方阻断。
        return Allow();
    }

    private CpuTopology GetTopologySafe()
    {
        if (_cachedTopology != null)
        {
            return _cachedTopology;
        }

        try
        {
            _cachedTopology = _cpuTopologyService.GetTopology();
        }
        catch
        {
            _cachedTopology = new CpuTopology();
        }

        return _cachedTopology;
    }

    private DevicePowerContext GetDeviceContextSafe()
    {
        if (_cachedDeviceContext != null)
        {
            return _cachedDeviceContext;
        }

        _cachedDeviceContext = DetectDeviceContext();
        return _cachedDeviceContext;
    }

    private static DevicePowerContext DetectDeviceContext()
    {
        var (portableByChassis, chassisReliable) = DetectPortableByChassis();
        var (hasBattery, isOnAcPower, powerReliable) = DetectPowerStatus();
        var (isModernStandby, capabilitiesReliable) = DetectModernStandby();
        return new DevicePowerContext(
            IsPortable: portableByChassis || hasBattery,
            HasBattery: hasBattery,
            IsOnAcPower: isOnAcPower,
            IsModernStandby: isModernStandby,
            IsDetectionReliable: chassisReliable && powerReliable && capabilitiesReliable);
    }

    private PowerHardwareFacts GetFactsSafe()
    {
        if (_factsProvider is not null)
        {
            try { return _factsProvider.GetCurrent(); }
            catch
            {
                return new PowerHardwareFacts(new CpuTopology(), false, false, false, false, false, false);
            }
        }

        var topology = GetTopologySafe();
        var device = GetDeviceContextSafe();
        return new PowerHardwareFacts(topology, device.IsPortable, device.HasBattery, device.IsOnAcPower,
            device.IsModernStandby, IsPowerPolicyManaged(), device.IsDetectionReliable);
    }

    private static bool IsPowerPolicyManaged()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Power\PowerSettings", writable: false);
            return key is not null;
        }
        catch { return true; }
    }

    private static (bool IsPortable, bool IsReliable) DetectPortableByChassis()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (ManagementObject enclosure in searcher.Get())
            {
                if (enclosure["ChassisTypes"] is not Array rawTypes)
                {
                    continue;
                }

                foreach (var rawType in rawTypes)
                {
                    if (rawType == null)
                    {
                        continue;
                    }

                    var chassisType = Convert.ToInt32(rawType);
                    if (PortableChassisTypes.Contains(chassisType))
                    {
                        return (true, true);
                    }

                    if (DesktopChassisTypes.Contains(chassisType))
                    {
                        return (false, true);
                    }
                }
            }
        }
        catch
        {
            // Ignore and fall back to battery-based detection.
        }

        return (false, false);
    }

    private static (bool HasBattery, bool IsOnAcPower, bool IsReliable) DetectPowerStatus()
    {
        try
        {
            if (GetSystemPowerStatus(out var status))
            {
                var hasBattery = status.BatteryFlag != 255 && (status.BatteryFlag & 128) == 0;
                var isOnAcPower = status.ACLineStatus == 1;
                var acKnown = status.ACLineStatus is 0 or 1;
                return (hasBattery, isOnAcPower, acKnown);
            }
        }
        catch
        {
            // Ignore and fall back to defaults.
        }

        return (false, false, false);
    }

    private static (bool IsModernStandby, bool IsReliable) DetectModernStandby()
    {
        try
        {
            return GetPwrCapabilities(out var capabilities)
                ? (capabilities.AoAc != 0, true)
                : (false, false);
        }
        catch { return (false, false); }
    }

    private static bool IsTopologySensitive(Guid setting, string? operationId) =>
        RequiresIntelHybrid(setting)
        || IsIntelHeteroPolicy(setting)
        || setting == PowerGuids.CoreParkingMin
        || setting == PowerGuids.CoreParkingMax
        || operationId?.Contains("core_parking", StringComparison.OrdinalIgnoreCase) == true;


    private static bool IsIntelHeteroPolicy(Guid setting)
    {
        return setting == PowerGuids.HeteroThreadSchedulingPolicy
            || setting == PowerGuids.HeteroShortThreadSchedulingPolicy;
    }
    private static bool RequiresIntelHybrid(Guid setting)
    {
        return setting == PowerGuids.ProcessorClass1EfficiencyThreshold
            || setting == PowerGuids.ProcessorClass1PerfLevel;
    }

    private static bool RequiresIntelCpu(Guid setting, string? operationId)
    {
        return setting == IntelIdleDisableSetting
            || string.Equals(operationId, "intel.disable_cstates", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresAmdCpu(string? operationId)
    {
        return string.Equals(operationId, "amd.precision_boost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(operationId, "amd.disable_cool_n_quiet", StringComparison.OrdinalIgnoreCase);
    }

    private static PowerSettingGuardResult Allow() => new() { Decision = PowerSettingGuardDecision.Allowed };

    private static PowerSettingGuardResult SkipUnsupported(string reason) => new()
    {
        Decision = PowerSettingGuardDecision.SkippedUnsupported,
        Reason = reason
    };

    private static PowerSettingGuardResult BlockRisk(string reason) => new()
    {
        Decision = PowerSettingGuardDecision.BlockedRisk,
        Reason = reason
    };

    private sealed record DevicePowerContext(
        bool IsPortable,
        bool HasBattery,
        bool IsOnAcPower,
        bool IsModernStandby,
        bool IsDetectionReliable);

    private static readonly HashSet<int> PortableChassisTypes = new()
    {
        8, 9, 10, 11, 12, 14, 30, 31, 32
    };

    private static readonly HashSet<int> DesktopChassisTypes = new() { 3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 35 };

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [DllImport("powrprof.dll")]
    private static extern bool GetPwrCapabilities(out SystemPowerCapabilities systemPowerCapabilities);

    [StructLayout(LayoutKind.Explicit, Size = 76)]
    private struct SystemPowerCapabilities
    {
        [FieldOffset(20)] public byte AoAc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
