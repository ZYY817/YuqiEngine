using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public sealed record RegistryValueSetCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string SubKeyPath,
    IReadOnlyList<RegistryTypedTargetValue> Targets,
    ChangeRisk Risk,
    bool RequiresExplicitConfirmation = false,
    bool RequiresRestart = false,
    IReadOnlyList<RegistryTypedTargetConstraint>? Constraints = null,
    RegistryDwordHive Hive = RegistryDwordHive.CurrentUser);

public static class RegistryValueSetCapabilityDefinitions
{
    public static RegistryValueSetCapabilityDefinition KeyboardResponse { get; } = new(
        "peripheral.keyboard_response",
        "1.0.0",
        @"Control Panel\Keyboard",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.String("KeyboardDelay", "0"),
            RegistryTypedTargetValue.String("KeyboardSpeed", "31")
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("KeyboardDelay", RegistryScalarValueKind.String, 0, 3),
            new RegistryTypedTargetConstraint("KeyboardSpeed", RegistryScalarValueKind.String, 0, 31)
        ]));

    public static RegistryValueSetCapabilityDefinition MousePrecision { get; } = new(
        "peripheral.mouse_precision",
        "1.0.0",
        @"Control Panel\Mouse",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.String("MouseSpeed", "0"),
            RegistryTypedTargetValue.String("MouseThreshold1", "0"),
            RegistryTypedTargetValue.String("MouseThreshold2", "0")
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("MouseSpeed", RegistryScalarValueKind.String, 0, 2),
            new RegistryTypedTargetConstraint("MouseThreshold1", RegistryScalarValueKind.String, 0, 100),
            new RegistryTypedTargetConstraint("MouseThreshold2", RegistryScalarValueKind.String, 0, 100)
        ]));

    public static RegistryValueSetCapabilityDefinition DisableTouchFeedback { get; } = new(
        "input.disable_touch_feedback",
        "1.0.0",
        @"Control Panel\Cursors",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("ContactVisualization", 0),
            RegistryTypedTargetValue.DWord("GestureVisualization", 0)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("ContactVisualization", RegistryScalarValueKind.DWord, 0, 1),
            new RegistryTypedTargetConstraint("GestureVisualization", RegistryScalarValueKind.DWord, 0, 1)
        ]));

    public static RegistryValueSetCapabilityDefinition DisableHandwritingCollection { get; } = new(
        "privacy.disable_handwriting_collection",
        "1.0.0",
        @"SOFTWARE\Microsoft\InputPersonalization",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("RestrictImplicitInkCollection", 1),
            RegistryTypedTargetValue.DWord("RestrictImplicitTextCollection", 1)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("RestrictImplicitInkCollection", RegistryScalarValueKind.DWord, 0, 1),
            new RegistryTypedTargetConstraint("RestrictImplicitTextCollection", RegistryScalarValueKind.DWord, 0, 1)
        ]));

    public static RegistryValueSetCapabilityDefinition DisableAdvertisingId { get; } = new(
        "privacy.disable_advertising_id",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("Enabled", 0),
            RegistryTypedTargetValue.String("Id", "00000000-0000-0000-0000-000000000000")
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("Enabled", RegistryScalarValueKind.DWord, 0, 1),
            new RegistryTypedTargetConstraint("Id", RegistryScalarValueKind.String)
        ]));

    public static RegistryValueSetCapabilityDefinition DisableWindowsTips { get; } = new(
        "windows.disable_tips",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("SoftLandingEnabled", 0),
            RegistryTypedTargetValue.DWord("SubscribedContent-338389Enabled", 0)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("SoftLandingEnabled", RegistryScalarValueKind.DWord, 0, 1),
            new RegistryTypedTargetConstraint("SubscribedContent-338389Enabled", RegistryScalarValueKind.DWord, 0, 1)
        ]));

    public static RegistryValueSetCapabilityDefinition DisableSuggestedApps { get; } = new(
        "windows.disable_suggested_apps",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("OemPreInstalledAppsEnabled", 0),
            RegistryTypedTargetValue.DWord("PreInstalledAppsEnabled", 0),
            RegistryTypedTargetValue.DWord("PreInstalledAppsEverEnabled", 0),
            RegistryTypedTargetValue.DWord("SilentInstalledAppsEnabled", 0)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("OemPreInstalledAppsEnabled", RegistryScalarValueKind.DWord, 0, 1),
            new RegistryTypedTargetConstraint("PreInstalledAppsEnabled", RegistryScalarValueKind.DWord, 0, 1),
            new RegistryTypedTargetConstraint("PreInstalledAppsEverEnabled", RegistryScalarValueKind.DWord, 0, 1),
            new RegistryTypedTargetConstraint("SilentInstalledAppsEnabled", RegistryScalarValueKind.DWord, 0, 1)
        ]));

    public static RegistryValueSetCapabilityDefinition DisableFullscreenOptimizations { get; } = new(
        "fps.disable_fullscreen_optimizations",
        "1.0.0",
        @"System\GameConfigStore",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("GameDVR_FSEBehavior", 2),
            RegistryTypedTargetValue.DWord("GameDVR_FSEBehaviorMode", 2)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("GameDVR_FSEBehavior", RegistryScalarValueKind.DWord, 0, 2),
            new RegistryTypedTargetConstraint("GameDVR_FSEBehaviorMode", RegistryScalarValueKind.DWord, 0, 2)
        ]));

    public static RegistryValueSetCapabilityDefinition OptimizeDns { get; } = new(
        "network.optimize_dns",
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("MaxCacheTtl", 900),
            RegistryTypedTargetValue.DWord("MaxNegativeCacheTtl", 30),
            RegistryTypedTargetValue.DWord("NegativeSOACacheTime", 0),
            RegistryTypedTargetValue.DWord("NetFailureCacheTime", 0),
            RegistryTypedTargetValue.DWord("ServerPriorityTimeLimit", 0)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: true,
        Hive: RegistryDwordHive.LocalMachine);

    public static RegistryValueSetCapabilityDefinition DisableCloudContent { get; } = new(
        "privacy.disable_cloud_content",
        "1.0.0",
        @"SOFTWARE\Policies\Microsoft\Windows\CloudContent",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("DisableWindowsConsumerFeatures", 1),
            RegistryTypedTargetValue.DWord("DisableWindowsSpotlightFeatures", 1),
            RegistryTypedTargetValue.DWord("DisableSoftLanding", 1)
        ]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    public static RegistryValueSetCapabilityDefinition DisableAppSync { get; } = new(
        "privacy.disable_app_sync",
        "1.0.0",
        @"SOFTWARE\Policies\Microsoft\Windows\SettingSync",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("DisableSettingSync", 1),
            RegistryTypedTargetValue.DWord("DisableSettingSyncUserOverride", 1)
        ]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    public static RegistryValueSetCapabilityDefinition DisableTimeline { get; } = new(
        "windows.disable_timeline",
        "1.0.0",
        @"SOFTWARE\Policies\Microsoft\Windows\System",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("EnableActivityFeed", 0),
            RegistryTypedTargetValue.DWord("PublishUserActivities", 0),
            RegistryTypedTargetValue.DWord("UploadUserActivities", 0)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    public static RegistryValueSetCapabilityDefinition OptimizeAfdParameters { get; } = new(
        "network.optimize_afd",
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Services\AFD\Parameters",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("DefaultReceiveWindow", 65535),
            RegistryTypedTargetValue.DWord("DefaultSendWindow", 65535)
        ]),
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    public static RegistryValueSetCapabilityDefinition DisableNetworkThrottling { get; } = new(
        "network.disable_throttling_complete",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)),
            RegistryTypedTargetValue.DWord("SystemResponsiveness", 0)
        ]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    // 备注：本项把 Audio 多媒体任务的 Priority/Affinity/Scheduling Category/SFIO Priority 写为
    // 6/0/High/High 以降低音频处理延迟；只改变多媒体任务调度参数，不删除系统保护。
    // 写入前捕获原值，恢复时按原值回写。
    public static RegistryValueSetCapabilityDefinition OptimizeSampleRate { get; } = new(
        "audio.optimize_sample_rate",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("Priority", 6),
            RegistryTypedTargetValue.DWord("Affinity", 0),
            RegistryTypedTargetValue.String("Scheduling Category", "High"),
            RegistryTypedTargetValue.String("SFIO Priority", "High")
        ]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    // 备注：本项把 DWM 的 EnableAeroPeek/AlwaysHibernateThumbnails 写为 0 以关闭桌面动画；
    // 只影响当前用户 DWM 设置，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static RegistryValueSetCapabilityDefinition DisableDwmAnimations { get; } = new(
        "gpu.disable_dwm_animations",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\DWM",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("EnableAeroPeek", 0),
            RegistryTypedTargetValue.DWord("AlwaysHibernateThumbnails", 0)
        ]),
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        Constraints: Array.AsReadOnly(
        [
            new RegistryTypedTargetConstraint("EnableAeroPeek", RegistryScalarValueKind.DWord, 0, 1),
            new RegistryTypedTargetConstraint("AlwaysHibernateThumbnails", RegistryScalarValueKind.DWord, 0, 1)
        ]));

    // 备注：本项把内核 Memory Management 的 FeatureSettingsOverride/FeatureSettingsOverrideMask 写为 3
    // 以关闭 Spectre/Meltdown 缓解；这是官方内核缓解位掩码。写入前捕获原值，恢复时按原值回写；
    // 缺失值恢复为删除。需重启生效。高风险，标红 + 显式确认。
    public static RegistryValueSetCapabilityDefinition DisableSpectreMeltdown { get; } = new(
        "cpu.disable_spectre_meltdown",
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
        Array.AsReadOnly(
        [
            RegistryTypedTargetValue.DWord("FeatureSettingsOverride", 3),
            RegistryTypedTargetValue.DWord("FeatureSettingsOverrideMask", 3)
        ]),
        ChangeRisk.HighSystem,
        RequiresExplicitConfirmation: true,
        RequiresRestart: true,
        Hive: RegistryDwordHive.LocalMachine);
}
