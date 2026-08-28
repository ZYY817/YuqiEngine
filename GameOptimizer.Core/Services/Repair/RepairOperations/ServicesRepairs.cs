// File: GameOptimizer.Core/Services/Repair/RepairOperations/ServicesRepairs.cs
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class ServicesRepairs
{
    private static LocalizationService Loc => LocalizationService.Instance;

    public static IEnumerable<RepairItem> GetAll()
    {
        yield return new RepairItem
        {
            Id = "repair.services_reset",
            Name = "重置关键服务",
            NameKey = "Repair_ServicesReset_Name",
            Description = "重置 Windows 关键服务到默认启动类型",
            DescriptionKey = "Repair_ServicesReset_Desc",
            Icon = "⚙️",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = ResetCriticalServicesAsync
        };

        yield return new RepairItem
        {
            Id = "repair.bits",
            Name = "修复 BITS 服务",
            NameKey = "Repair_Bits_Name",
            Description = "修复后台智能传输服务，解决更新下载问题",
            DescriptionKey = "Repair_Bits_Desc",
            Icon = "📥",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckBitsServiceHealthAsync,
            RepairAsync = RepairBitsAsync
        };

        yield return new RepairItem
        {
            Id = "repair.cryptsvc",
            Name = "修复加密服务",
            NameKey = "Repair_CryptSvc_Name",
            Description = "修复加密服务，解决证书和签名问题",
            DescriptionKey = "Repair_CryptSvc_Desc",
            Icon = "🔐",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckCryptSvcHealthAsync,
            RepairAsync = RepairCryptSvcAsync
        };

        yield return new RepairItem
        {
            Id = "repair.appinfo",
            Name = "修复应用程序信息服务",
            NameKey = "Repair_AppInfo_Name",
            Description = "修复 UAC 和应用程序启动问题",
            DescriptionKey = "Repair_AppInfo_Desc",
            Icon = "📱",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckAppInfoServiceHealthAsync,
            RepairAsync = RepairAppInfoAsync
        };

        yield return new RepairItem
        {
            Id = "repair.themes",
            Name = "修复主题服务",
            NameKey = "Repair_Themes_Name",
            Description = "修复 Windows 主题和外观问题",
            DescriptionKey = "Repair_Themes_Desc",
            Icon = "🎨",
            Category = RepairCategory.Display,
            CheckHealthAsync = CheckThemesServiceHealthAsync,
            RepairAsync = RepairThemesAsync
        };

        yield return new RepairItem
        {
            Id = "repair.shellhwdetection",
            Name = "修复硬件检测服务",
            NameKey = "Repair_ShellHwDetection_Name",
            Description = "修复自动播放和硬件检测问题",
            DescriptionKey = "Repair_ShellHwDetection_Desc",
            Icon = "🔍",
            Category = RepairCategory.Peripheral,
            CheckHealthAsync = CheckShellHwDetectionServiceHealthAsync,
            RepairAsync = RepairShellHwDetectionAsync
        };

        yield return new RepairItem
        {
            Id = "repair.plugplay",
            Name = "修复即插即用服务",
            NameKey = "Repair_PlugPlay_Name",
            Description = "修复设备识别和驱动安装问题",
            DescriptionKey = "Repair_PlugPlay_Desc",
            Icon = "🔌",
            Category = RepairCategory.Peripheral,
            CheckHealthAsync = CheckPlugPlayServiceHealthAsync,
            RepairAsync = RepairPlugPlayAsync
        };

        yield return new RepairItem
        {
            Id = "repair.netprofm",
            Name = "修复网络列表服务",
            NameKey = "Repair_NetProfm_Name",
            Description = "修复网络识别和连接问题",
            DescriptionKey = "Repair_NetProfm_Desc",
            Icon = "🌐",
            Category = RepairCategory.Network,
            CheckHealthAsync = CheckNetProfmServiceHealthAsync,
            RepairAsync = RepairNetProfmAsync
        };

        yield return new RepairItem
        {
            Id = "repair.nlasvc",
            Name = "修复网络位置感知",
            NameKey = "Repair_NlaSvc_Name",
            Description = "修复网络类型识别问题",
            DescriptionKey = "Repair_NlaSvc_Desc",
            Icon = "📶",
            Category = RepairCategory.Network,
            CheckHealthAsync = CheckNlaSvcHealthAsync,
            RepairAsync = RepairNlaSvcAsync
        };

        yield return new RepairItem
        {
            Id = "repair.rpcss",
            Name = "修复 RPC 服务",
            NameKey = "Repair_Rpcss_Name",
            Description = "修复远程过程调用服务",
            DescriptionKey = "Repair_Rpcss_Desc",
            Icon = "🔗",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = CheckRpcssServiceHealthAsync,
            RepairAsync = RepairRpcssAsync
        };
    }

    private static async Task<bool> CheckBitsServiceHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("bits"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query bits", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static async Task<bool> CheckCryptSvcHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("cryptsvc"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query cryptsvc", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static async Task<bool> CheckAppInfoServiceHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("appinfo"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query appinfo", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static async Task<bool> CheckThemesServiceHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("Themes"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query Themes", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static async Task<bool> CheckShellHwDetectionServiceHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("ShellHWDetection"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query ShellHWDetection", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static async Task<bool> CheckPlugPlayServiceHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("PlugPlay"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query PlugPlay", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static async Task<bool> CheckNetProfmServiceHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("netprofm"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query netprofm", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static async Task<bool> CheckNlaSvcHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("NlaSvc"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query NlaSvc", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static async Task<bool> CheckRpcssServiceHealthAsync()
    {
        try
        {
            if (!await RepairHelper.ServiceExistsAsync("RpcSs"))
                return true;
            var result = await RepairHelper.RunCommandAsync("sc", "query RpcSs", 5000);
            return result.Contains("RUNNING");
        }
        catch { return true; }
    }

    private static Task<RepairResult> ResetCriticalServicesAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.services_reset");

    private static Task<RepairResult> RepairBitsAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.bits");

    private static Task<RepairResult> RepairCryptSvcAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.cryptsvc");

    private static Task<RepairResult> RepairAppInfoAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.appinfo");

    private static Task<RepairResult> RepairThemesAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.themes");

    private static Task<RepairResult> RepairShellHwDetectionAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.shellhwdetection");

    private static Task<RepairResult> RepairPlugPlayAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.plugplay");

    private static Task<RepairResult> RepairNetProfmAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.netprofm");

    private static Task<RepairResult> RepairNlaSvcAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.nlasvc");

    private static Task<RepairResult> RepairRpcssAsync(IProgress<RepairProgress>? progress) =>
        CentralServiceOwnerRequired("repair.rpcss");

    private static Task<RepairResult> CentralServiceOwnerRequired(string repairId) =>
        Task.FromResult(RepairResult.Failed(
            $"{repairId} 必须通过统一服务 Owner 执行；旧服务命令入口已安全禁用。"));
}
