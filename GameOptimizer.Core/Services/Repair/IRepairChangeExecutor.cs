using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Repair;

public static class RepairChangeCapabilityIds
{
    public const string ExistingGameModeEnable = "gaming.mode.enable";
    public const string GameModeAutoEnable = "repair.game-mode.auto-enable";
    public const string HardwareAccelerationReset = "repair.hardware-acceleration.reset";
    public const string FeedbackReset = "privacy.feedback.frequency-zero";
    public const string DpiReset = "repair.display.dpi-reset";
    public const string VsyncReset = "repair.graphics.vsync-reset";
    public const string AdvertisingId = "privacy.disable_advertising_id";
    public const string GameDvrEnabled = "gaming.game-dvr.enabled";
    public const string GameDvrAppCapture = "gaming.game-dvr.app-capture";
    public const string LocationConsent = "privacy.location.consent";
    public const string WebcamConsent = "privacy.deny_app_access_to_camera";
    public const string MicrophoneConsent = "privacy.microphone.consent";
    public const string HagsEnable = "gpu.enable_hags";
    public const string DiagnosticDataReset = "repair.privacy.diagnostic-data-reset";
    public const string FastStartupEnable = "repair.power.fast-startup-enable";
    public const string TelemetryServiceReset = "repair.platform.telemetry-service-reset";
    public const string MemoryCompressionEnable = "repair.platform.memory-compression-enable";
    public const string HibernationEnable = "repair.platform.hibernation-enable";
    public const string PowerPlansReset = "repair.platform.power-plans-reset";
    public const string HighPerformancePlanRestore = "repair.platform.high-performance-plan-restore";
    public const string UltimatePerformancePlanRestore = "repair.platform.ultimate-performance-plan-restore";
    public const string SleepPolicyReset = "repair.platform.sleep-policy-reset";
    public const string CoreParkingReset = "repair.platform.core-parking-reset";
    public const string CpuFrequencyReset = "repair.platform.cpu-frequency-reset";
    public const string BatteryReportGenerate = "repair.platform.battery-report-generate";
    public const string PowerTroubleshooterLaunch = "repair.platform.power-troubleshooter-launch";
    public const string ServicesReset = "repair.service.critical-reset";
    public const string BitsEnsureRunning = "repair.service.bits.ensure-running";
    public const string CryptSvcEnsureRunning = "repair.service.cryptsvc.ensure-running";
    public const string AppInfoEnsureRunning = "repair.service.appinfo.ensure-running";
    public const string ThemesEnsureRunning = "repair.service.themes.ensure-running";
    public const string AudioServiceEnsureRunning = "repair.service.audiosrv.ensure-running";
    public const string ShellHardwareDetectionEnsureRunning = "repair.service.shellhwdetection.ensure-running";
    public const string PlugPlayEnsureRunning = "repair.service.plugplay.ensure-running";
    public const string NetworkListEnsureRunning = "repair.service.netprofm.ensure-running";
    public const string NetworkLocationEnsureRunning = "repair.service.nlasvc.ensure-running";
    public const string RpcSsEnsureRunning = "repair.service.rpcss.ensure-running";
}

public sealed record RepairChangeStep(
    string CapabilityId,
    string Title,
    string ConfirmationSummary,
    string TargetPayload = "apply",
    TargetScopeKind ScopeKind = TargetScopeKind.CurrentUser);

public sealed record RepairChangeBinding(
    string RepairItemId,
    IReadOnlyList<RepairChangeStep> Steps);

public sealed record RepairChangeOperationReceipt(
    string CapabilityId,
    Guid ReceiptId,
    Guid? RecoveryId,
    ChangeOutcome Outcome,
    bool MutationOccurred);

public sealed record RepairChangeReceipt(
    Guid ReceiptId,
    string RepairItemId,
    IReadOnlyList<RepairChangeOperationReceipt> Operations,
    bool CompensationAttempted,
    DateTimeOffset CompletedAtUtc);

public sealed record RepairRecoveryRequest(
    string RepairItemId,
    RepairChangeReceipt Receipt);

public interface IRepairChangeExecutor
{
    bool IsManaged(string repairItemId);

    Task<RepairResult> ExecuteAsync(
        RepairItem item,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken);

    Task<RepairResult> RestoreAsync(
        RepairRecoveryRequest request,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken);
}

internal sealed class UnavailableRepairChangeExecutor : IRepairChangeExecutor
{
    private static readonly HashSet<string> ManagedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "repair.game_mode",
        "repair.hardware_acceleration",
        "repair.feedback",
        "repair.dpi_reset",
        "repair.vsync",
        "repair.advertising_id",
        "repair.game_dvr",
        "repair.privacy_settings",
        "repair.hags",
        "repair.diagnostic_data",
        "repair.fast_startup",
        "repair.telemetry",
        "repair.memory_compression",
        "repair.hibernation",
        "repair.power_plan",
        "repair.high_performance",
        "repair.ultimate_performance",
        "repair.sleep",
        "repair.core_parking",
        "repair.cpu_frequency",
        "repair.power",
        "repair.battery_report",
        "repair.power_troubleshoot",
        "repair.services_reset",
        "repair.bits",
        "repair.cryptsvc",
        "repair.appinfo",
        "repair.themes",
        "repair.audio_service",
        "repair.shellhwdetection",
        "repair.plugplay",
        "repair.netprofm",
        "repair.nlasvc",
        "repair.rpcss"
    };

    public static UnavailableRepairChangeExecutor Instance { get; } = new();

    public bool IsManaged(string repairItemId) =>
        ManagedIds.Contains(repairItemId)
        || SystemMaintenanceRepairCatalog.Contains(repairItemId)
        || BootMemoryRepairCatalog.Contains(repairItemId)
        || NetworkRepairCatalog.Contains(repairItemId)
        || CpuSchedulingRepairCatalog.Contains(repairItemId)
        || FileShellRepairCatalog.Contains(repairItemId)
        || GraphicsDisplayRepairCatalog.Contains(repairItemId)
        || ResidualRepairCatalog.Contains(repairItemId);

    public Task<RepairResult> ExecuteAsync(
        RepairItem item,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(RepairResult.Blocked(
            "统一修复协调器尚未接入，已安全阻断该修复项。",
            "repair.executor-unavailable",
            availability: RepairExecutionAvailability.FailClosed));

    public Task<RepairResult> RestoreAsync(
        RepairRecoveryRequest request,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(RepairResult.Blocked(
            "统一修复协调器尚未接入，无法验证恢复回执。",
            "repair.restore-executor-unavailable",
            availability: RepairExecutionAvailability.FailClosed));
}
