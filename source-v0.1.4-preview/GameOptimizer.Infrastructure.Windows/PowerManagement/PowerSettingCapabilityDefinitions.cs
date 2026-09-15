// File: GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSettingCapabilityDefinitions.cs
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

/// <summary>
/// Frozen definitions for the 16 power-setting + 1 scheme-switch catalog migrations.  Each
/// capability owns exactly one <c>power:setting:*</c> (or <c>power:scheme:active</c>) resource
/// key.  None of these keys overlap with the machine-global
/// <c>power:machine:display-timeout-and-active-scheme</c> resource owned by PowerManagementOwner,
/// and none of these capabilities issue SETACTIVE — the change lands on the already-active scheme.
/// </summary>
public static class PowerSettingCapabilityDefinitions
{
    private const string Version = "1.0.0";

    // IntelIdleDisableSetting is intentionally local to this migration; it is not exposed by
    // PowerGuids because the real guard keeps it private for fail-closed reasoning.
    public static readonly Guid IntelIdleDisableSetting = new("5D76A2CA-E8C0-402F-A133-2158492D58AD");

    // The built-in High Performance scheme GUID (verified in PowerPlanOperation decompilation).
    public static readonly Guid HighPerformanceScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public static IReadOnlyList<PowerSettingChangeCapabilityDefinition> All => new[]
    {
        ProcessorMinState100,
        PerfIncreaseThreshold,
        IdleDemoteThreshold,
        PerfTimeCheck,
        Class1EfficiencyThreshold,
        Class1PerfLevel,
        DisableThrottle,
        HeteroThreadScheduling,
        DisableUsbSuspend,
        PeripheralUsbSuspend,
        DisableAspm,
        DisableCoreParking,
        ProcessorBoostMode,
        DisableBoostMode,
        DisablePStates,
        DisableCStates
    };

    public static PowerSettingChangeCapabilityDefinition ProcessorMinState100 { get; } = New(
        "power.processor_min_state_100",
        "power:setting:processor-min-state",
        ChangeRisk.MediumRebootOrExperience,
        Target("min-state", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorMinState, 100, null));

    public static PowerSettingChangeCapabilityDefinition PerfIncreaseThreshold { get; } = New(
        "power.perf_increase_threshold",
        "power:setting:perf-increase-threshold",
        ChangeRisk.MediumRebootOrExperience,
        Target("perf-increase-threshold", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorPerfIncreaseThreshold, 1, null));

    public static PowerSettingChangeCapabilityDefinition IdleDemoteThreshold { get; } = New(
        "power.idle_demote_threshold",
        "power:setting:idle-demote-threshold",
        ChangeRisk.MediumRebootOrExperience,
        Target("idle-demote-threshold", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorIdleDemoteThreshold, 100, null));

    public static PowerSettingChangeCapabilityDefinition PerfTimeCheck { get; } = New(
        "power.perf_time_check",
        "power:setting:perf-time-check",
        ChangeRisk.MediumRebootOrExperience,
        Target("perf-time-check", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorPerfTimeCheck, 5000, null));

    public static PowerSettingChangeCapabilityDefinition Class1EfficiencyThreshold { get; } = New(
        "power.class1_efficiency_threshold",
        "power:setting:class1-efficiency-threshold",
        ChangeRisk.MediumRebootOrExperience,
        Target("class1-efficiency-threshold", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorClass1EfficiencyThreshold, 1, null));

    public static PowerSettingChangeCapabilityDefinition Class1PerfLevel { get; } = New(
        "power.class1_perf_level",
        "power:setting:class1-perf-level",
        ChangeRisk.MediumRebootOrExperience,
        Target("class1-perf-level", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorClass1PerfLevel, 100, null));

    public static PowerSettingChangeCapabilityDefinition DisableThrottle { get; } = New(
        "power.disable_throttle",
        "power:setting:throttle-policy",
        ChangeRisk.HighSystem,
        Target("throttle-policy", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorThrottlePolicy, 0, null));

    public static PowerSettingChangeCapabilityDefinition HeteroThreadScheduling { get; } = New(
        "power.hetero_thread_scheduling",
        "power:setting:hetero-thread-scheduling",
        ChangeRisk.HighSystem,
        Target("hetero-thread-scheduling", PowerGuids.ProcessorSubgroup, PowerGuids.HeteroThreadSchedulingPolicy, 2, null));

    public static PowerSettingChangeCapabilityDefinition DisableUsbSuspend { get; } = New(
        "power.disable_usb_suspend",
        "power:setting:usb-selective-suspend",
        ChangeRisk.LowReversible,
        Target("usb-selective-suspend", PowerGuids.UsbSubgroup, PowerGuids.UsbSelectiveSuspend, 0, null));

    public static PowerSettingChangeCapabilityDefinition PeripheralUsbSuspend { get; } = New(
        "peripheral.usb.suspend",
        "power:setting:usb-selective-suspend",
        ChangeRisk.LowReversible,
        Target("usb-selective-suspend", PowerGuids.UsbSubgroup, PowerGuids.UsbSelectiveSuspend, 0, null));

    public static PowerSettingChangeCapabilityDefinition DisableAspm { get; } = New(
        "power.disable_aspm",
        "power:setting:pcie-aspm",
        ChangeRisk.MediumRebootOrExperience,
        Target("pcie-aspm", PowerGuids.PciExpressSubgroup, PowerGuids.PcieLinkStatePowerManagement, 0, null));

    public static PowerSettingChangeCapabilityDefinition DisableCoreParking { get; } = New(
        "cpu.disable_core_parking",
        "power:setting:core-parking",
        ChangeRisk.MediumRebootOrExperience,
        Target("core-parking-min", PowerGuids.ProcessorSubgroup, PowerGuids.CoreParkingMin, 100, null),
        Target("core-parking-max", PowerGuids.ProcessorSubgroup, PowerGuids.CoreParkingMax, 100, null));

    public static PowerSettingChangeCapabilityDefinition ProcessorBoostMode { get; } = New(
        "cpu.processor_boost_mode",
        "power:setting:processor-boost-mode",
        ChangeRisk.MediumRebootOrExperience,
        Target("processor-boost-mode", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorBoostMode, 2, null));

    public static PowerSettingChangeCapabilityDefinition DisableBoostMode { get; } = New(
        "cpu.disable_boost_mode",
        "power:setting:processor-boost-mode",
        ChangeRisk.MediumRebootOrExperience,
        Target("processor-boost-mode", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorBoostMode, 0, null));

    public static PowerSettingChangeCapabilityDefinition DisablePStates { get; } = New(
        "cpu.disable_pstates",
        "power:setting:processor-minmax-state",
        ChangeRisk.HighSystem,
        Target("processor-min-state", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorMinState, 100, null),
        Target("processor-max-state", PowerGuids.ProcessorSubgroup, PowerGuids.ProcessorMaxState, 100, null));

    public static PowerSettingChangeCapabilityDefinition DisableCStates { get; } = New(
        "intel.disable_cstates",
        "power:setting:intel-idle-disable",
        ChangeRisk.HighSystem,
        Target("intel-idle-disable", PowerGuids.ProcessorSubgroup, IntelIdleDisableSetting, 1, null));

    public static PowerSchemeChangeCapabilityDefinition EnableHighPerformanceMode { get; } = new(
        "services.enable_high_performance_mode",
        Version,
        GameOptimizer.Core.PowerManagement.PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource,
        HighPerformanceScheme,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: false);

    private static PowerSettingChangeCapabilityDefinition New(
        string capabilityId,
        string resourceKey,
        ChangeRisk risk,
        params PowerSettingTarget[] targets) =>
        new(capabilityId, Version, resourceKey, risk, targets, RequiresExplicitConfirmation: true, RequiresRestart: false);

    private static PowerSettingTarget Target(
        string settingId,
        Guid subgroup,
        Guid setting,
        int targetAc,
        int? targetDc) =>
        new(settingId, subgroup, setting, targetAc, targetDc);
}
