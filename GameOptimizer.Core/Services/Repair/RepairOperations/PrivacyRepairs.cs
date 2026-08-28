// File: GameOptimizer.Core/Services/Repair/RepairOperations/PrivacyRepairs.cs
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.ApplicationRepair;
using GameOptimizer.Core.Services.MaintenanceDiagnostics;

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

    private static async Task<bool> CheckTelemetryServiceHealthAsync()
    {
        try
        {
            var result = await RepairHelper.RunCommandAsync("sc", "query DiagTrack", 5000);
            return result.Contains("RUNNING") || result.Contains("STOPPED");
        }
        catch { return false; }
    }

    private static async Task<RepairResult> ResetTelemetryAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingTelemetry"), PercentComplete = 10 });
        try
        {
            await RepairHelper.RunCommandAsync("sc", "config DiagTrack start= auto");
            await RepairHelper.RunCommandAsync("net", "start DiagTrack");
            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return RepairResult.Succeeded(Loc.Get("Repair.TelemetryReset"));
        }
        catch (Exception ex) { return RepairResult.Failed(Loc.Get("Repair.ResetTelemetryFailed"), ex); }
    }

    private static async Task<RepairResult> ResetPrivacySettingsAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingPrivacySettings"), PercentComplete = 10 });
        try
        {
            var script = @"
                $privacyKeys = @(
                    'HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location',
                    'HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam',
                    'HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone'
                )
                foreach ($key in $privacyKeys) {
                    if (Test-Path $key) {
                        Set-ItemProperty -Path $key -Name 'Value' -Value 'Allow' -ErrorAction SilentlyContinue
                    }
                }
            ";
            await RepairHelper.RunCommandAsync("powershell.exe", $"-ExecutionPolicy Bypass -Command \"{script}\"");
            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return RepairResult.Succeeded(Loc.Get("Repair.PrivacySettingsReset"));
        }
        catch (Exception ex) { return RepairResult.Failed(Loc.Get("Repair.ResetPrivacySettingsFailed"), ex); }
    }

    private static async Task<RepairResult> ResetAdvertisingIdAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingAdvertisingId"), PercentComplete = 10 });
        try
        {
            var script = @"
                Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' -Name 'Enabled' -Value 1 -Type DWord -Force
                Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' -Name 'Id' -ErrorAction SilentlyContinue
            ";
            await RepairHelper.RunCommandAsync("powershell.exe", $"-ExecutionPolicy Bypass -Command \"{script}\"");
            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return RepairResult.Succeeded(Loc.Get("Repair.AdvertisingIdReset"));
        }
        catch (Exception ex) { return RepairResult.Failed(Loc.Get("Repair.ResetAdvertisingIdFailed"), ex); }
    }

    private static async Task<RepairResult> ResetDiagnosticDataAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingDiagnosticData"), PercentComplete = 10 });
        try
        {
            var script = @"
                Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection' -Name 'AllowTelemetry' -Value 3 -Type DWord -Force
            ";
            await RepairHelper.RunCommandAsync("powershell.exe", $"-ExecutionPolicy Bypass -Command \"{script}\"");
            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return RepairResult.Succeeded(Loc.Get("Repair.DiagnosticDataReset"));
        }
        catch (Exception ex) { return RepairResult.Failed(Loc.Get("Repair.ResetDiagnosticDataFailed"), ex); }
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

    private static async Task<RepairResult> ResetFeedbackAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = Loc.Get("Repair.ResettingFeedback"), PercentComplete = 10 });
        try
        {
            var script = @"
                Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Siuf\Rules' -Name 'NumberOfSIUFInPeriod' -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue
            ";
            await RepairHelper.RunCommandAsync("powershell.exe", $"-ExecutionPolicy Bypass -Command \"{script}\"");
            progress?.Report(new RepairProgress { Message = Loc.Get("Repair.Complete"), PercentComplete = 100 });
            return RepairResult.Succeeded(Loc.Get("Repair.FeedbackReset"));
        }
        catch (Exception ex) { return RepairResult.Failed(Loc.Get("Repair.ResetFeedbackFailed"), ex); }
    }
}
