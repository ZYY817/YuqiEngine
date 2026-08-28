using Microsoft.Win32;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class SystemFrameworkRepairs
{
    public static IEnumerable<RepairItem> GetAll()
    {
        yield return new RepairItem
        {
            Id = "repair.dotnet_framework",
            Name = ".NET Framework",
            Description = "重新注册 .NET 程序集，修复框架问题",
            Icon = "🔷",
            Category = RepairCategory.SystemFramework,
            CheckHealthAsync = CheckDotNetHealthAsync,
            RepairAsync = RepairDotNetAsync
        };

        yield return new RepairItem
        {
            Id = "repair.vcredist",
            Name = "Visual C++ 运行库",
            Description = "检测并修复 Visual C++ Redistributables",
            Icon = "📦",
            Category = RepairCategory.SystemFramework,
            CheckHealthAsync = RepairHelper.CheckVcRedistInstalledAsync,
            RepairAsync = RepairVcRedistAsync
        };

        yield return new RepairItem
        {
            Id = "repair.directx",
            Name = "DirectX",
            Description = "重置 DirectX 设置，修复游戏图形问题",
            Icon = "🎮",
            Category = RepairCategory.SystemFramework,
            CheckHealthAsync = RepairHelper.CheckDirectXHealthAsync,
            RepairAsync = RepairDirectXAsync
        };

        yield return new RepairItem
        {
            Id = "repair.activex",
            Name = "ActiveX 控件",
            Description = "重新注册 ActiveX 组件",
            Icon = "🔧",
            Category = RepairCategory.SystemFramework,
            CheckHealthAsync = CheckActiveXHealthAsync,
            RepairAsync = RepairActiveXAsync
        };

        yield return new RepairItem
        {
            Id = "repair.com_plus",
            Name = "COM+ 应用程序",
            Description = "修复 COM+ 目录和组件",
            Icon = "⚙️",
            Category = RepairCategory.SystemFramework,
            CheckHealthAsync = () => RepairHelper.IsServiceRunningAsync("COMSysApp"),
            RepairAsync = RepairComPlusAsync
        };
    }

    private static Task<bool> CheckDotNetHealthAsync()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            var release = key?.GetValue("Release") as int?;
            return Task.FromResult(release >= 528040);
        }
        catch { return Task.FromResult(false); }
    }

    private static async Task<RepairResult> RepairDotNetAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = "正在修复 .NET Framework...", PercentComplete = 10 });
        try
        {
            await RepairHelper.RunCommandAsync("cmd.exe", "/c cd /d %windir%\\Microsoft.NET\\Framework64\\v4.0.30319 && aspnet_regiis.exe -i");
            progress?.Report(new RepairProgress { Message = "修复完成", PercentComplete = 100 });
            return RepairResult.Succeeded(".NET Framework 修复完成");
        }
        catch (Exception ex)
        {
            return RepairResult.Failed("修复 .NET Framework 失败", ex);
        }
    }

    private static Task<bool> CheckActiveXHealthAsync()
    {
        try
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var requiredDlls = new[] { "actxprxy.dll", "oleaut32.dll" };
            foreach (var dll in requiredDlls)
            {
                if (!File.Exists(Path.Combine(systemDir, dll)))
                    return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
        catch { return Task.FromResult(false); }
    }

    private static Task<RepairResult> RepairVcRedistAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = "Visual C++ 运行库需要手动安装", PercentComplete = 100 });
        return Task.FromResult(RepairResult.Succeeded("请从 Microsoft 官网下载最新的 Visual C++ Redistributable"));
    }

    private static async Task<RepairResult> RepairDirectXAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = "正在重置 DirectX 设置...", PercentComplete = 10 });
        try
        {
            await RepairHelper.RunCommandAsync("cmd.exe", "/c regsvr32 /s ddraw.dll");
            await RepairHelper.RunCommandAsync("cmd.exe", "/c regsvr32 /s dsound.dll");
            progress?.Report(new RepairProgress { Message = "修复完成", PercentComplete = 100 });
            return RepairResult.Succeeded("DirectX 设置已重置");
        }
        catch (Exception ex)
        {
            return RepairResult.Failed("修复 DirectX 失败", ex);
        }
    }

    private static async Task<RepairResult> RepairActiveXAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = "正在重新注册 ActiveX 组件...", PercentComplete = 10 });
        try
        {
            await RepairHelper.RunCommandAsync("cmd.exe", "/c regsvr32 /s actxprxy.dll");
            await RepairHelper.RunCommandAsync("cmd.exe", "/c regsvr32 /s oleaut32.dll");
            progress?.Report(new RepairProgress { Message = "修复完成", PercentComplete = 100 });
            return RepairResult.Succeeded("ActiveX 组件已重新注册");
        }
        catch (Exception ex)
        {
            return RepairResult.Failed("修复 ActiveX 失败", ex);
        }
    }

    private static async Task<RepairResult> RepairComPlusAsync(IProgress<RepairProgress>? progress)
    {
        progress?.Report(new RepairProgress { Message = "正在修复 COM+ 组件...", PercentComplete = 10 });
        try
        {
            await RepairHelper.RunCommandAsync("net", "stop COMSysApp");
            await RepairHelper.RunCommandAsync("net", "start COMSysApp");
            progress?.Report(new RepairProgress { Message = "修复完成", PercentComplete = 100 });
            return RepairResult.Succeeded("COM+ 组件已修复");
        }
        catch (Exception ex)
        {
            return RepairResult.Failed("修复 COM+ 失败", ex);
        }
    }
}
