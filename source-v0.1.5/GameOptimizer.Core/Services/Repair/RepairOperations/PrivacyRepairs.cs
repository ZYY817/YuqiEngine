// File: GameOptimizer.Core/Services/Repair/RepairOperations/PrivacyRepairs.cs
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.ApplicationRepair;
using GameOptimizer.Core.Services.MaintenanceDiagnostics;
using Microsoft.Win32;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class PrivacyRepairs
{
    private static LocalizationService Loc => LocalizationService.Instance;

    public static IEnumerable<RepairItem> GetAll(
        IApplicationPackageRepairOwner? applicationRepairOwner = null,
        IMaintenanceDiagnosticOwner? maintenanceOwner = null)
    {
        applicationRepairOwner ??= UnavailableApplicationPackageRepairOwner.Instance;
        maintenanceOwner ??= MaintenanceDiagnosticOwnerFallback.Instance;
        yield return new RepairItem
        {
            Id = "repair.telemetry",
            Name = "重置遥测设置",
            NameKey = "Repair_Telemetry_Name",
            Description = "重置 Windows 遥测设置到默认值",
            DescriptionKey = "Repair_Telemetry_Desc",
            Icon = "📡",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckTelemetryServiceHealthAsync,
            RepairAsync = ResetTelemetryAsync
        };

        yield return new RepairItem
        {
            Id = "repair.privacy_settings",
            Name = "重置隐私设置",
            NameKey = "Repair_PrivacySettings_Name",
            Description = "重置所有应用权限到默认值",
            DescriptionKey = "Repair_PrivacySettings_Desc",
            Icon = "🔒",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetPrivacySettingsAsync
        };

        yield return new RepairItem
        {
            Id = "repair.advertising_id",
            Name = "重置广告 ID",
            NameKey = "Repair_AdvertisingId_Name",
            Description = "重置 Windows 广告标识符",
            DescriptionKey = "Repair_AdvertisingId_Desc",
            Icon = "🎯",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetAdvertisingIdAsync
        };

        yield return new RepairItem
        {
            Id = "repair.location_service",
            Name = "修复位置服务",
            NameKey = "Repair_LocationService_Name",
            Description = "重置位置服务设置，修复定位不工作问题",
            DescriptionKey = "Repair_LocationService_Desc",
            Icon = "📍",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = async () =>
                (await maintenanceOwner.DiagnoseAsync("repair.location_service", CancellationToken.None)
                    .ConfigureAwait(false)).IsHealthy == true,
            RepairAsync = progress => maintenanceOwner.RepairAsync(
                "repair.location_service",
                progress,
                CancellationToken.None)
        };

        yield return new RepairItem
        {
            Id = "repair.diagnostic_data",
            Name = "重置诊断数据设置",
            NameKey = "Repair_DiagnosticData_Name",
            Description = "重置诊断数据收集设置",
            DescriptionKey = "Repair_DiagnosticData_Desc",
            Icon = "📊",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetDiagnosticDataAsync
        };

        yield return new RepairItem
        {
            Id = "repair.activity_history",
            Name = "清理活动历史",
            NameKey = "Repair_ActivityHistory_Name",
            Description = "清理 Windows 活动历史记录",
            DescriptionKey = "Repair_ActivityHistory_Desc",
            Icon = "📜",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => Task.FromResult(RepairResult.Failed(
                "repair.activity_history 尚未接入唯一 typed Owner，已安全阻断；不会修改活动历史策略。"))
        };

        yield return new RepairItem
        {
            Id = "repair.cortana",
            Name = "重置 Cortana",
            NameKey = "Repair_Cortana_Name",
            Description = "重置 Cortana 设置到默认值",
            DescriptionKey = "Repair_Cortana_Desc",
            Icon = "🎤",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = progress => ResetCortanaAsync(applicationRepairOwner, progress)
        };

        yield return new RepairItem
        {
            Id = "repair.feedback",
            Name = "重置反馈设置",
            NameKey = "Repair_Feedback_Name",
            Description = "重置 Windows 反馈频率设置",
            DescriptionKey = "Repair_Feedback_Desc",
            Icon = "💬",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetFeedbackAsync
        };
    }

    // Why: ServiceController 走 SCM API，sc query 输出随系统语言本地化，文本匹配不可靠。
    private static Task<bool> CheckTelemetryServiceHealthAsync() =>
        RepairHelper.ServiceExistsAsync("DiagTrack");

    private static async Task<RepairResult> ResetTelemetryAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingTelemetry"), PercentComplete = 10 });
        try
        {
            var result = await RepairHelper.EnableServiceAsync("DiagTrack", "DiagTrack", null);
            if (!result.Success)
                return RepairResult.Failed(Loc.Get("Repair.ResetTelemetryFailed"), detail: result.Message);
            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return RepairResult.Succeeded(Loc.Get("Repair.TelemetryReset"));
        }
        catch (Exception ex) { return RepairResult.Failed(Loc.Get("Repair.ResetTelemetryFailed"), ex); }
    }

    // Why: PowerShell Set-ItemProperty 非终止错误退出码仍为 0，失败会假报成功；
    // 注册表直接写入异常真实抛出，与语言无关。
    private static Task<RepairResult> ResetPrivacySettingsAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingPrivacySettings"), PercentComplete = 10 });
        try
        {
            var privacyKeys = new[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone"
            };
            foreach (var path in privacyKeys)
            {
                using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
                key?.SetValue("Value", "Allow", RegistryValueKind.String);
            }

            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return Task.FromResult(RepairResult.Succeeded(Loc.Get("Repair.PrivacySettingsReset")));
        }
        catch (Exception ex) { return Task.FromResult(RepairResult.Failed(Loc.Get("Repair.ResetPrivacySettingsFailed"), ex)); }
    }

    private static Task<RepairResult> ResetAdvertisingIdAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingAdvertisingId"), PercentComplete = 10 });
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", writable: true);
            key.SetValue("Enabled", 1, RegistryValueKind.DWord);
            key.DeleteValue("Id", throwOnMissingValue: false);

            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return Task.FromResult(RepairResult.Succeeded(Loc.Get("Repair.AdvertisingIdReset")));
        }
        catch (Exception ex) { return Task.FromResult(RepairResult.Failed(Loc.Get("Repair.ResetAdvertisingIdFailed"), ex)); }
    }

    private static Task<RepairResult> ResetDiagnosticDataAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingDiagnosticData"), PercentComplete = 10 });
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", writable: true);
            key.SetValue("AllowTelemetry", 3, RegistryValueKind.DWord);

            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return Task.FromResult(RepairResult.Succeeded(Loc.Get("Repair.DiagnosticDataReset")));
        }
        catch (Exception ex) { return Task.FromResult(RepairResult.Failed(Loc.Get("Repair.ResetDiagnosticDataFailed"), ex)); }
    }

    private static async Task<RepairResult> ResetCortanaAsync(
        IApplicationPackageRepairOwner owner,
        IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingCortana"), PercentComplete = 10 });
        var result = await owner.PrepareAsync(
            ApplicationPackageRepairTarget.Cortana,
            ApplicationPackageRepairAction.Reset,
            CancellationToken.None).ConfigureAwait(false);
        return RepairResult.Failed(result.Message, detail: result.Status);
    }

    private static Task<RepairResult> ResetFeedbackAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingFeedback"), PercentComplete = 10 });
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Siuf\Rules", writable: true);
            key.SetValue("NumberOfSIUFInPeriod", 0, RegistryValueKind.DWord);

            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return Task.FromResult(RepairResult.Succeeded(Loc.Get("Repair.FeedbackReset")));
        }
        catch (Exception ex) { return Task.FromResult(RepairResult.Failed(Loc.Get("Repair.ResetFeedbackFailed"), ex)); }
    }
}
