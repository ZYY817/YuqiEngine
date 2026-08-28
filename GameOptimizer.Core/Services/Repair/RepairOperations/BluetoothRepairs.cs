using GameOptimizer.Core.Services.DeviceRepair;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class BluetoothRepairs
{
    public static IEnumerable<RepairItem> GetAll(IDeviceRepairOwner? owner = null)
    {
        owner ??= DeviceRepairOwnerFallback.Instance;
        yield return Create(owner, "repair.bluetooth_service", "检查蓝牙服务", "Repair.BluetoothService.Name", "只读检查蓝牙支持服务。", "Repair.BluetoothService.Desc", "📶");
        yield return Create(owner, "repair.bluetooth_adapter", "检查蓝牙适配器", "Repair.BluetoothAdapter.Name", "只读检查蓝牙适配器状态。", "Repair.BluetoothAdapter.Desc", "📱");
        yield return Create(owner, "repair.bluetooth_cache", "检查蓝牙配对状态", "Repair.BluetoothCache.Name", "不删除用户配对数据，仅检查服务状态。", "Repair.BluetoothCache.Desc", "🗂️", true);
        yield return Create(owner, "repair.bluetooth_rescan", "检查蓝牙设备", "Repair.BluetoothRescan.Name", "只读枚举蓝牙设备，不触发全局重扫。", "Repair.BluetoothRescan.Desc", "🔎");
    }

    private static RepairItem Create(
        IDeviceRepairOwner owner,
        string id,
        string name,
        string nameKey,
        string description,
        string descriptionKey,
        string icon,
        bool risky = false) => new()
    {
        Id = id,
        Name = name,
        NameKey = nameKey,
        Description = description,
        DescriptionKey = descriptionKey,
        Icon = icon,
        Category = RepairCategory.Bluetooth,
        IsRisky = risky,
        CheckHealthAsync = async () => (await owner.DiagnoseAsync(id, CancellationToken.None).ConfigureAwait(false)).IsHealthy,
        RepairAsync = progress => owner.RepairAsync(id, progress, CancellationToken.None)
    };
}
