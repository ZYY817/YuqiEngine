using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public static class RepairRegistryCapabilityDefinitions
{
    public static RegistryDwordCapabilityDefinition GameModeAutoEnable { get; } = new(
        RepairChangeCapabilityIds.GameModeAutoEnable,
        "1.0.0",
        @"SOFTWARE\Microsoft\GameBar",
        "AutoGameModeEnabled",
        1,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    public static RegistryDwordCapabilityDefinition HardwareAccelerationReset { get; } = new(
        RepairChangeCapabilityIds.HardwareAccelerationReset,
        "1.0.0",
        @"Software\Microsoft\Avalon.Graphics",
        "DisableHWAcceleration",
        0,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    public static RegistryDwordCapabilityDefinition FeedbackReset { get; } = new(
        RepairChangeCapabilityIds.FeedbackReset,
        "1.0.0",
        @"Software\Microsoft\Siuf\Rules",
        "NumberOfSIUFInPeriod",
        0,
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true);

    // Why: 该修复能力的语义是"启用 Game DVR"，默认 apply 目标必须与名称一致为 1；
    // 设为 0 会让默认目标反向（修复=禁用）。
    public static RegistryDwordCapabilityDefinition GameDvrEnabled { get; } = new(
        RepairChangeCapabilityIds.GameDvrEnabled,
        "1.0.0",
        @"System\GameConfigStore",
        "GameDVR_Enabled",
        1,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    // 同上：修复语义为"启用游戏捕获"，默认 apply 目标为 1。
    public static RegistryDwordCapabilityDefinition GameDvrAppCapture { get; } = new(
        RepairChangeCapabilityIds.GameDvrAppCapture,
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR",
        "AppCaptureEnabled",
        1,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    public static RegistryValueDeleteCapabilityDefinition DpiReset { get; } = new(
        RepairChangeCapabilityIds.DpiReset,
        "1.0.0",
        @"Control Panel\Desktop",
        Array.AsReadOnly(["LogPixels", "Win8DpiScaling"]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: true);

    public static RegistryValueDeleteCapabilityDefinition VsyncReset { get; } = new(
        RepairChangeCapabilityIds.VsyncReset,
        "1.0.0",
        @"Software\Microsoft\DirectX",
        Array.AsReadOnly(["ForceVSync"]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true);

    public static RegistryDwordCapabilityDefinition HagsEnable { get; } = new(
        RepairChangeCapabilityIds.HagsEnable,
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
        "HwSchMode",
        2,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine,
        RequiresRestart: true);

    public static RegistryDwordCapabilityDefinition DiagnosticDataReset { get; } = new(
        RepairChangeCapabilityIds.DiagnosticDataReset,
        "1.0.0",
        @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
        "AllowTelemetry",
        3,
        ChangeRisk.HighSystem,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    public static RegistryDwordCapabilityDefinition FastStartupEnable { get; } = new(
        RepairChangeCapabilityIds.FastStartupEnable,
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
        "HiberbootEnabled",
        1,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine,
        RequiresRestart: true);
}
