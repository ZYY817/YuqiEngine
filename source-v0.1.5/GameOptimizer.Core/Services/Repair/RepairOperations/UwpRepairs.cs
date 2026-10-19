using GameOptimizer.Core.Services.ApplicationRepair;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class UwpRepairs
{
    public static IEnumerable<RepairItem> GetAll(IApplicationPackageRepairOwner? owner = null)
    {
        owner ??= UnavailableApplicationPackageRepairOwner.Instance;
        yield return Create("repair.store", "Microsoft Store",
            "检查当前用户的 Store 包；重注册与缓存重置在安全恢复链完成前保持阻断。",
            "🛍️", ApplicationPackageRepairTarget.MicrosoftStore, owner);
        yield return Create("repair.uwp_apps", "UWP 应用",
            "批量 UWP 重注册无法绑定单一包身份与恢复材料，当前保持阻断。",
            "📫", ApplicationPackageRepairTarget.AllUwpApplications, owner);
        yield return Create("repair.calculator", "计算器",
            "检查当前用户的计算器包；重注册在安全恢复链完成前保持阻断。",
            "🧮", ApplicationPackageRepairTarget.Calculator, owner);
        yield return Create("repair.photos", "照片",
            "检查当前用户的照片包；重注册在安全恢复链完成前保持阻断。",
            "🖼️", ApplicationPackageRepairTarget.Photos, owner);
    }

    private static RepairItem Create(
        string id,
        string name,
        string description,
        string icon,
        ApplicationPackageRepairTarget target,
        IApplicationPackageRepairOwner owner) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        Icon = icon,
        Category = RepairCategory.UwpApps,
        IsRisky = true,
        CheckHealthAsync = async () =>
        {
            var result = await owner.DiagnoseAsync(target, CancellationToken.None).ConfigureAwait(false);
            return result.Success && result.IsApplicable && result.Status == "Healthy";
        },
        RepairAsync = async progress =>
        {
            progress?.Report(new RepairProgress { Message = "正在验证应用包身份与安全边界...", PercentComplete = 20 });
            var result = await owner.PrepareAsync(target, ApplicationPackageRepairAction.Reregister, CancellationToken.None)
                .ConfigureAwait(false);
            return RepairResult.Failed(result.Message, detail: result.Status);
        }
    };
}
