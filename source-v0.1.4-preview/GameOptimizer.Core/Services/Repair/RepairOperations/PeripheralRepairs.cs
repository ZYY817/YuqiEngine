using GameOptimizer.Core.Services.DeviceRepair;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class PeripheralRepairs
{
    public static IEnumerable<RepairItem> GetAll(IDeviceRepairOwner? owner = null)
    {
        owner ??= DeviceRepairOwnerFallback.Instance;
        yield return Create(owner, "repair.usb_reset", "检查 USB 设备", "Repair.UsbReset.Name", "只读枚举 USB 设备，不重置控制器。", "Repair.UsbReset.Desc", "🔌");
        yield return Create(owner, "repair.hid_service", "检查 HID 服务", "Repair.HidService.Name", "只读检查 HID 服务，不启停服务。", "Repair.HidService.Desc", "🖱️");
        yield return Create(owner, "repair.webcam", "检查摄像头", "Repair.Webcam.Name", "只读检查摄像头设备，不重扫或重启服务。", "Repair.Webcam.Desc", "📷");
        yield return Create(owner, "repair.touchpad", "检查触摸板", "Repair.Touchpad.Name", "只读检查触摸板设备，不重扫或重启 HID 服务。", "Repair.Touchpad.Desc", "🖐️");
        yield return Create(owner, "repair.printer", "检查打印服务", "Repair.Printer.Name", "只读检查打印服务，不影响打印队列。", "Repair.Printer.Desc", "🖨️");
    }

    private static RepairItem Create(
        IDeviceRepairOwner owner,
        string id,
        string name,
        string nameKey,
        string description,
        string descriptionKey,
        string icon) => new()
    {
        Id = id,
        Name = name,
        NameKey = nameKey,
        Description = description,
        DescriptionKey = descriptionKey,
        Icon = icon,
        Category = RepairCategory.Peripheral,
        CheckHealthAsync = async () => (await owner.DiagnoseAsync(id, CancellationToken.None).ConfigureAwait(false)).IsHealthy,
        RepairAsync = progress => owner.RepairAsync(id, progress, CancellationToken.None)
    };
}
