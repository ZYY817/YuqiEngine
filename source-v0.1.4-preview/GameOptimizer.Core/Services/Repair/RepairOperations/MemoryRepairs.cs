// File: GameOptimizer.Core/Services/Repair/RepairOperations/MemoryRepairs.cs
using GameOptimizer.Core.Services.MaintenanceDiagnostics;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class MemoryRepairs
{
    public static IEnumerable<RepairItem> GetAll(IMaintenanceDiagnosticOwner? maintenanceOwner = null)
    {
        maintenanceOwner ??= MaintenanceDiagnosticOwnerFallback.Instance;
        yield return new RepairItem
        {
            Id = "repair.memory_service",
            Name = "修复内存管理服务",
            NameKey = "Repair_MemoryService_Name",
            Description = "重启 SysMain 服务，修复内存管理异常问题",
            DescriptionKey = "Repair_MemoryService_Desc",
            Icon = "🧠",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => IsHealthyAsync(maintenanceOwner, "repair.memory_service"),
            RepairAsync = progress => maintenanceOwner.RepairAsync("repair.memory_service", progress, CancellationToken.None)
        };

        yield return new RepairItem
        {
            Id = "repair.virtual_memory",
            Name = "重置虚拟内存",
            NameKey = "Repair_VirtualMemory_Name",
            Description = "重置页面文件设置为系统管理",
            DescriptionKey = "Repair_VirtualMemory_Desc",
            Icon = "💾",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => BootMemoryOwnerRequired("repair.virtual_memory")
        };

        yield return new RepairItem
        {
            Id = "repair.memory_compression",
            Name = "修复内存压缩",
            NameKey = "Repair_MemoryCompression_Name",
            Description = "重置内存压缩设置，修复内存占用异常问题",
            DescriptionKey = "Repair_MemoryCompression_Desc",
            Icon = "📦",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => BootMemoryOwnerRequired("repair.memory_compression")
        };

        yield return new RepairItem
        {
            Id = "repair.standby_list",
            Name = "清理待机内存",
            NameKey = "Repair_StandbyList_Name",
            Description = "清理系统待机列表，释放缓存内存",
            DescriptionKey = "Repair_StandbyList_Desc",
            Icon = "🧹",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => BootMemoryOwnerRequired("repair.standby_list")
        };

        yield return new RepairItem
        {
            Id = "repair.working_set",
            Name = "清理进程工作集",
            NameKey = "Repair_WorkingSet_Name",
            Description = "清理所有进程的工作集内存",
            DescriptionKey = "Repair_WorkingSet_Desc",
            Icon = "🔄",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => BootMemoryOwnerRequired("repair.working_set")
        };

        yield return new RepairItem
        {
            Id = "repair.superfetch",
            Name = "修复 Superfetch",
            NameKey = "Repair_Superfetch_Name",
            Description = "重置 Superfetch/SysMain 服务",
            DescriptionKey = "Repair_Superfetch_Desc",
            Icon = "⚡",
            Category = RepairCategory.SystemCore,
            CheckHealthAsync = () => IsHealthyAsync(maintenanceOwner, "repair.superfetch"),
            RepairAsync = progress => maintenanceOwner.RepairAsync("repair.superfetch", progress, CancellationToken.None)
        };
    }

    private static async Task<bool> IsHealthyAsync(IMaintenanceDiagnosticOwner owner, string repairItemId)
    {
        var snapshot = await owner.DiagnoseAsync(repairItemId, CancellationToken.None).ConfigureAwait(false);
        return snapshot.IsHealthy == true;
    }

    private static Task<RepairResult> BootMemoryOwnerRequired(string repairId) =>
        Task.FromResult(RepairResult.Failed(
            $"{repairId} 必须通过统一 Boot/Memory Owner 执行；正式平台适配与精确恢复未验证，已安全阻断。"));

}
