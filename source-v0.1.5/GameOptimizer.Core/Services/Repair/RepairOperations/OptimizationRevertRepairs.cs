// File: GameOptimizer.Core/Services/Repair/RepairOperations/OptimizationRevertRepairs.cs
using System.Threading;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using Microsoft.Win32;
namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class OptimizationRevertRepairs
{
    private static LocalizationService Loc => LocalizationService.Instance;

    public static IEnumerable<RepairItem> GetAll()
    {
        foreach (var item in GetBasicReverts()) yield return item;
        foreach (var item in GetServiceReverts()) yield return item;
        foreach (var item in GetSystemReverts()) yield return item;
        foreach (var item in GetNetworkReverts()) yield return item;
        foreach (var item in GetPrivacyReverts()) yield return item;
        foreach (var item in GetSecurityReverts()) yield return item;
    }

    #region Basic Reverts
    private static IEnumerable<RepairItem> GetBasicReverts()
    {
        yield return CreateRevert("security_center", "Windows 安全中心", "", "basic.disable_security_center",
            () => WindowsFeatureHealthHelper.IsSecurityCenterHealthyAsync(),
            async (p) =>
            {
                var health = await WindowsFeatureHealthHelper.EnsureSecurityCenterHealthyAsync(true, CancellationToken.None).ConfigureAwait(false);
                return health.IsHealthy
                    ? RepairResult.Succeeded("Windows 安全中心相关服务已恢复")
                    : RepairResult.Failed("Windows 安全中心相关服务仍异常", detail: health.Message);
            });

        yield return CreateRevert("firewall", "Windows 防火墙", "", "basic.disable_firewall",
            () => WindowsFeatureHealthHelper.IsFirewallHealthyAsync(),
            async (p) =>
            {
                var health = await WindowsFeatureHealthHelper.EnsureFirewallHealthyAsync(true, CancellationToken.None).ConfigureAwait(false);
                return health.IsHealthy
                    ? RepairResult.Succeeded("Windows 防火墙服务与规则已恢复")
                    : RepairResult.Failed("Windows 防火墙仍异常", detail: health.Message);
            });

        yield return CreateRevert("store_auto_update", "商店自动更新策略", "", "basic.disable_store_auto_update",
            () =>
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\WindowsStore");
                var current = key?.GetValue("AutoDownload");
                return Task.FromResult(current == null);
            },
            (p) =>
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\WindowsStore");
                key?.DeleteValue("AutoDownload", false);
                return Task.FromResult(RepairResult.Succeeded("已恢复商店自动更新策略为系统默认"));
            });

        yield return CreateRevert("game_bar", "Xbox Game Bar", "", "basic.disable_game_bar",
            () => {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
                return Task.FromResult((key?.GetValue("AppCaptureEnabled") as int?) != 0);
            },
            p => GraphicsRepairBoundary.RequireExistingOwnerAsync("repair.game_dvr", p));

        yield return CreateRevert("mouse_acceleration", "鼠标加速", "", "peripheral.mouse_precision",
            () =>
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                var mouseSpeed = key?.GetValue("MouseSpeed") as string;
                var threshold1 = key?.GetValue("MouseThreshold1") as string;
                var threshold2 = key?.GetValue("MouseThreshold2") as string;

                return Task.FromResult(
                    mouseSpeed == "1" &&
                    threshold1 == "6" &&
                    threshold2 == "10");
            },
            (p) =>
            {
                p?.Report(new RepairProgress { Message = Loc.Get("Repair.Revert.EnablingMouseAcceleration"), PercentComplete = 50 });
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
                key?.SetValue("MouseSpeed", "1", RegistryValueKind.String);
                key?.SetValue("MouseThreshold1", "6", RegistryValueKind.String);
                key?.SetValue("MouseThreshold2", "10", RegistryValueKind.String);
                return Task.FromResult(RepairResult.Succeeded(Loc.Get("Repair.Revert.MouseAccelerationEnabled")));
            });

        yield return CreateRevert("win_key_shortcuts", "Win 键快捷键", "", "peripheral.anti_mistouch",
            () =>
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
                var noWinKeysObj = key?.GetValue("NoWinKeys");
                var noWinKeys = noWinKeysObj == null ? 0 : Convert.ToInt32(noWinKeysObj);
                return Task.FromResult(noWinKeys != 1);
            },
            (p) =>
            {
                p?.Report(new RepairProgress { Message = "正在恢复 Win 键快捷键...", PercentComplete = 50 });
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
                key?.DeleteValue("NoWinKeys", false);
                key?.DeleteValue("DisabledHotkeys", false);
                return Task.FromResult(RepairResult.Succeeded("已恢复 Win 键快捷键（Win+V/Win+Shift+S/Win+R 等）。"));
            });
    }
    #endregion

    #region Service Reverts
    private static IEnumerable<RepairItem> GetServiceReverts()
    {
        yield return CreateRevert("print_spooler", "Print Spooler", "🖨️", "service.disable_spooler",
            () => RepairHelper.IsServiceRunningAsync("Spooler"),
            (p) => RepairHelper.EnableServiceAsync("Spooler", "Print Spooler", p));

        yield return CreateRevert("telemetry", "遥测服务", "📊", "service.disable_telemetry",
            () => RepairHelper.IsServiceRunningAsync("DiagTrack"),
            (p) => RepairHelper.EnableServiceAsync("DiagTrack", "遥测服务", p));

        yield return CreateRevert("wsearch", "Windows Search", "🔍", "service.disable_wsearch",
            () => RepairHelper.IsServiceRunningAsync("WSearch"),
            (p) => RepairHelper.EnableServiceAsync("WSearch", "Windows Search", p));

        yield return CreateRevert("sysmain", "SysMain/Superfetch", "⚡", "services.disable_sysmain_unique",
            () => RepairHelper.IsServiceRunningAsync("SysMain"),
            (p) => RepairHelper.EnableServiceAsync("SysMain", "SysMain", p));
    }
    #endregion

    #region System Reverts
    private static IEnumerable<RepairItem> GetSystemReverts()
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var hiberfilPath = Path.Combine(systemDrive, "hiberfil.sys");
        
        yield return CreateRevert("hibernation", "休眠", "💤", "system.disable_hibernation",
            () => Task.FromResult(File.Exists(hiberfilPath)),
            async (p) => {
                p?.Report(new RepairProgress { Message = Loc.Get("Repair.Revert.EnablingHibernation"), PercentComplete = 50 });
                var (code, _, err) = await RepairHelper.RunCommandResultAsync("powercfg", "/hibernate on");
                if (code != 0)
                    return RepairResult.Failed(Loc.Get("Repair.Revert.HibernationEnableFailed"),
                        detail: $"powercfg /hibernate on 退出码 {code}：{err.Trim()}");
                return RepairResult.Succeeded(Loc.Get("Repair.Revert.HibernationEnabled"));
            });

        yield return CreateRevert("prefetch", "Prefetch", "", "system.disable_prefetch",
            () => {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters");
                return Task.FromResult((key?.GetValue("EnablePrefetcher") as int?) == 3);
            },
            (p) => {
                p?.Report(new RepairProgress { Message = Loc.Get("Repair.Revert.EnablingPrefetch"), PercentComplete = 50 });
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters");
                key?.SetValue("EnablePrefetcher", 3, RegistryValueKind.DWord);
                return Task.FromResult(RepairResult.Succeeded(Loc.Get("Repair.Revert.PrefetchEnabled")));
            });

        yield return CreateRevert("ntfs_encryption", "NTFS 加密", "", "storage.disable_ntfs_encryption",
            () => Task.FromResult(false),
            p =>
            {
                p?.Report(new RepairProgress { Message = "旧 NTFS/EFS 恢复入口已安全阻断", PercentComplete = 100 });
                return Task.FromResult(RepairResult.Failed(
                    "NTFS/EFS 恢复不可用",
                    detail: "旧快照不包含密钥、卷、文件身份和 durable recovery receipt；未执行注册表、服务、卷或文件写入。"));
            },
            isRisky: true);
    }
    #endregion

    #region Network Reverts
    private static IEnumerable<RepairItem> GetNetworkReverts()
    {
        yield return CreateRevert("nagle", "Nagle 算法", "🌐", "network.disable_nagle",
            () => Task.FromResult(true),
            (p) => {
                p?.Report(new RepairProgress { Message = Loc.Get("Repair.Revert.RestoringNagle"), PercentComplete = 50 });
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
                if (key != null)
                    foreach (var subKeyName in key.GetSubKeyNames())
                        using (var subKey = key.OpenSubKey(subKeyName, true))
                        {
                            subKey?.DeleteValue("TcpAckFrequency", false);
                            subKey?.DeleteValue("TCPNoDelay", false);
                        }
                return Task.FromResult(RepairResult.Succeeded(Loc.Get("Repair.Revert.NagleRestored")));
            });

        yield return CreateRevert("tcp_auto_tuning", "TCP 自动调优", "📶", "network.disable_tcp_auto_tuning",
            () => Task.FromResult(true),
            async (p) => {
                p?.Report(new RepairProgress { Message = Loc.Get("Repair.Revert.RestoringTcpAutoTuning"), PercentComplete = 50 });
                var (code, _, err) = await RepairHelper.RunCommandResultAsync("netsh", "int tcp set global autotuninglevel=normal");
                if (code != 0)
                    return RepairResult.Failed(Loc.Get("Repair.Revert.TcpAutoTuningRestoreFailed"),
                        detail: $"netsh int tcp set global 退出码 {code}：{err.Trim()}");
                return RepairResult.Succeeded(Loc.Get("Repair.Revert.TcpAutoTuningRestored"));
            });
    }
    #endregion

    #region Privacy Reverts
    private static IEnumerable<RepairItem> GetPrivacyReverts()
    {
        yield return CreateRevert("advertising_id", "广告 ID", "", "privacy.disable_advertising_id",
            () => {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
                return Task.FromResult((key?.GetValue("Enabled") as int?) == 1);
            },
            (p) => {
                p?.Report(new RepairProgress { Message = Loc.Get("Repair.Revert.EnablingAdvertisingId"), PercentComplete = 50 });
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
                key?.SetValue("Enabled", 1, RegistryValueKind.DWord);
                return Task.FromResult(RepairResult.Succeeded(Loc.Get("Repair.Revert.AdvertisingIdEnabled")));
            });
    }
    #endregion

    #region Security Reverts
    private static IEnumerable<RepairItem> GetSecurityReverts()
    {
        yield return CreateRevert("smartscreen", "SmartScreen", "🛡️", "security.disable_smartscreen",
            () => Task.FromResult(false),
            (p) => {
                p?.Report(new RepairProgress { Message = "SmartScreen 恢复已安全阻断", PercentComplete = 100 });
                return Task.FromResult(RepairResult.Failed(
                    "SmartScreen 恢复不可用",
                    detail: "缺少可证明 Policy/Tamper/第三方安全产品适用性、完整快照和权威回读的 typed adapter；未执行注册表写入。"));
            }, isRisky: true);
    }
    #endregion

    #region Helper
    private static RepairItem CreateRevert(
        string id,
        string name,
        string icon,
        string relatedOptimizationId,
        Func<Task<bool>> checkFunc,
        Func<IProgress<RepairProgress>?, Task<RepairResult>> repairFunc,
        bool isRisky = false,
        string? nameKey = null,
        string? descKey = null)
    {
        return new RepairItem
        {
            ExecutionIntent = RepairExecutionIntent.Restore,
            Id = $"repair.revert.{id}",
            Name = $"还原: {name}",
            NameKey = nameKey ?? $"Repair_Revert_{ToPascalCase(id)}_Name",
            Description = $"将 {name} 恢复到 Windows 默认设置",
            DescriptionKey = descKey ?? $"Repair_Revert_{ToPascalCase(id)}_Desc",
            Category = RepairCategory.OptimizationRevert,
            Icon = icon,
            IsRisky = isRisky,
            RelatedOptimization = relatedOptimizationId,
            CheckHealthAsync = checkFunc,
            RepairAsync = repairFunc
        };
    }

    private static string ToPascalCase(string input)
    {
        return string.Join("", input.Split('_').Select(s => 
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1).ToLower()));
    }
    #endregion
}



