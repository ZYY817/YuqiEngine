// File: GameOptimizer.Shell/Services/PowerPlan/PowerSettingGuidMapper.cs
using System;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Services.PowerPlan;

/// <summary>
/// 电源参数 GUID 映射器 - 将参数键映射到 Windows GUID
/// </summary>
public class PowerSettingGuidMapper
{
    /// <summary>
    /// 获取参数对应的 GUID (子组 + 设置)
    /// </summary>
    public (Guid Subgroup, Guid Setting) GetGuids(string settingKey)
    {
        return settingKey switch
        {
            "proc_min_state" => (
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorMinState),
            "proc_max_state" => (
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorMaxState),
            "core_parking_min" => (
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.CoreParkingMin),
            "boost_mode" => (
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorBoostMode),
            "perf_increase_threshold" => (
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorPerfIncreaseThreshold),
            "idle_demote_threshold" => (
                PowerPlanEditorService.PowerGuids.ProcessorSubgroup,
                PowerPlanEditorService.PowerGuids.ProcessorIdleDemoteThreshold),
            "pcie_aspm" => (
                PowerPlanEditorService.PowerGuids.PciExpressSubgroup,
                PowerPlanEditorService.PowerGuids.PcieLinkStatePowerManagement),
            "usb_selective_suspend" => (
                PowerPlanEditorService.PowerGuids.UsbSubgroup,
                PowerPlanEditorService.PowerGuids.UsbSelectiveSuspend),
            "disk_turn_off" => (
                PowerPlanEditorService.PowerGuids.HardDiskSubgroup,
                PowerPlanEditorService.PowerGuids.HardDiskTurnOff),
            "display_turn_off" => (
                PowerPlanEditorService.PowerGuids.DisplaySubgroup,
                PowerPlanEditorService.PowerGuids.DisplayTurnOff),
            "sleep_after" => (
                PowerPlanEditorService.PowerGuids.SleepSubgroup,
                PowerPlanEditorService.PowerGuids.SleepAfter),
            _ => (Guid.Empty, Guid.Empty)
        };
    }
}
