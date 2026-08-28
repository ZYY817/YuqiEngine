namespace GameOptimizer.Core.Services.ScheduledTasks;

public static class ScheduledTaskCapabilityDefinitions
{
    public static IReadOnlyList<ScheduledTaskChangeCapabilityDefinition> All { get; } =
    [
        Disable("tasks.disable_app_experience", @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"),
        Disable("tasks.disable_auto_proxy_detection", @"\Microsoft\Windows\Autochk\Proxy"),
        Disable("tasks.disable_ceip", @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"),
        HighSystem("tasks.disable_cleanup_tasks", @"\Microsoft\Windows\Servicing\StartComponentCleanup"),
        Disable("tasks.disable_defragmentation", @"\Microsoft\Windows\Defrag\ScheduledDefrag"),
        Disable("tasks.disable_disk_diagnostic", @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"),
        Disable("tasks.disable_ms_store_update_tasks", @"\Microsoft\Windows\WindowsUpdate\Automatic App Update"),
        Disable("tasks.disable_ms_sync_tasks", @"\Microsoft\Windows\SyncCenter\SyncCenterScheduledSync"),
        Disable("tasks.disable_notification_tasks", @"\Microsoft\Windows\WindowsBackup\ConfigNotification"),
        HighSystem("tasks.disable_policy_updates", @"\Microsoft\Windows\WindowsUpdate\Scheduled Start"),
        Disable("tasks.disable_remote_management", @"\Microsoft\Windows\RemoteAssistance\RemoteAssistanceTask"),
        Disable("tasks.disable_store_features", @"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask"),
        Disable("tasks.disable_sync_features", @"\Microsoft\Windows\SettingSync\BackgroundUploadTask"),
        HighSystem("tasks.disable_windows_update", @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan"),
        Disable("tasks.disable_xbox_features", @"\Microsoft\XblGameSave\XblGameSaveTask"),
        Disable("tasks.disable_xbox_tasks", @"\Microsoft\XblGameSave\XblGameSaveTaskLogon")
    ];

    private static ScheduledTaskChangeCapabilityDefinition Disable(string capabilityId, string taskPath) =>
        new(capabilityId, "1.0.0", taskPath);

    private static ScheduledTaskChangeCapabilityDefinition HighSystem(string capabilityId, string taskPath) =>
        new(capabilityId, "1.0.0", taskPath, Risk: GameOptimizer.Core.Contracts.Changes.ChangeRisk.HighSystem);
}
