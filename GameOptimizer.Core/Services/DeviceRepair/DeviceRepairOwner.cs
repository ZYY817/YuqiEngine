using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Core.Services.DeviceRepair;

public enum DeviceDiagnosticTargetKind
{
    WindowsService,
    PnpClass,
    PnpName
}

public sealed record DeviceRepairDefinition(
    string RepairItemId,
    string CapabilityId,
    DeviceDiagnosticTargetKind DiagnosticKind,
    string DiagnosticTarget,
    string BlockReason);

public sealed record DeviceDiagnosticSnapshot(
    string RepairItemId,
    bool IsApplicable,
    bool IsHealthy,
    IReadOnlyList<string> ResourceIdentities,
    string? DefaultResourceIdentity,
    string Message)
{
    public static DeviceDiagnosticSnapshot Failed(string repairItemId, string message) =>
        new(repairItemId, false, false, [], null, message);
}

public interface IDeviceDiagnosticPlatform
{
    Task<DeviceDiagnosticSnapshot> InspectAsync(
        DeviceRepairDefinition definition,
        CancellationToken cancellationToken);
}

public interface IDeviceRepairOwner
{
    Task<DeviceDiagnosticSnapshot> DiagnoseAsync(string repairItemId, CancellationToken cancellationToken);
    Task<GameOptimizer.Core.Services.Repair.RepairResult> RepairAsync(
        string repairItemId,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken);
}

public static class DeviceRepairCatalog
{
    private static readonly IReadOnlyDictionary<string, DeviceRepairDefinition> Definitions =
        CreateDefinitions().ToDictionary(value => value.RepairItemId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<DeviceRepairDefinition> All => Definitions.Values.ToArray();

    public static bool TryGet(string repairItemId, out DeviceRepairDefinition definition) =>
        Definitions.TryGetValue(repairItemId, out definition!);

    private static IEnumerable<DeviceRepairDefinition> CreateDefinitions()
    {
        yield return Service("repair.audio_service", "audio.service.core", "Audiosrv", "音频服务启停与启动类型缺少完整快照和恢复 Owner。");
        yield return Service("repair.audio_endpoint", "audio.service.endpoint", "AudioEndpointBuilder", "音频端点服务重启缺少完整快照和恢复 Owner。");
        yield return Service("repair.audio_reset", "audio.settings.reset", "Audiosrv", "音频注册表、服务和设备状态的批量重置不可证明完整恢复。");
        yield return PnpClass("repair.audio_driver", "audio.device.rescan", "AudioEndpoint", "PnP 全局重扫可能改变设备拓扑，缺少设备级快照与恢复回执。");
        yield return Service("repair.bluetooth_service", "device.bluetooth.service", "bthserv", "蓝牙服务组启停缺少启动类型和依赖服务的完整恢复材料。");
        yield return PnpClass("repair.bluetooth_adapter", "device.bluetooth.adapter", "Bluetooth", "设备禁用/启用缺少稳定设备身份、热拔插和驱动恢复契约。");
        yield return Service("repair.bluetooth_cache", "device.bluetooth.cache", "bthserv", "蓝牙配对状态属于用户数据，禁止以重启服务冒充清理或修复成功。");
        yield return PnpClass("repair.bluetooth_rescan", "device.bluetooth.rescan", "Bluetooth", "PnP 全局重扫缺少可逆快照与设备拓扑回读。");
        yield return PnpClass("repair.usb_reset", "device.usb.rescan", "USB", "USB 控制器与设备拓扑重扫缺少逐设备恢复材料。");
        yield return Service("repair.hid_service", "device.hid.service", "hidserv", "HID 服务启停缺少依赖项、启动类型与恢复验证。");
        yield return PnpClass("repair.webcam", "device.camera.repair", "Camera", "摄像头设备和 FrameServer 组合修复缺少设备身份及服务恢复契约。");
        yield return PnpName("repair.touchpad", "device.touchpad.repair", "touchpad", "触摸板设备差异大，缺少稳定设备身份和驱动恢复契约。");
        yield return Service("repair.printer", "device.printer.spooler", "Spooler", "打印后台服务重启可能影响队列，缺少队列与服务完整恢复材料。");
    }

    private static DeviceRepairDefinition Service(string id, string capability, string service, string reason) =>
        new(id, capability, DeviceDiagnosticTargetKind.WindowsService, service, reason);

    private static DeviceRepairDefinition PnpClass(string id, string capability, string deviceClass, string reason) =>
        new(id, capability, DeviceDiagnosticTargetKind.PnpClass, deviceClass, reason);

    private static DeviceRepairDefinition PnpName(string id, string capability, string name, string reason) =>
        new(id, capability, DeviceDiagnosticTargetKind.PnpName, name, reason);
}

public sealed class DeviceRepairOwner : IDeviceRepairOwner
{
    private readonly IDeviceDiagnosticPlatform _platform;

    public DeviceRepairOwner(IDeviceDiagnosticPlatform platform) =>
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public async Task<DeviceDiagnosticSnapshot> DiagnoseAsync(
        string repairItemId,
        CancellationToken cancellationToken)
    {
        if (!DeviceRepairCatalog.TryGet(repairItemId, out var definition))
        {
            return DeviceDiagnosticSnapshot.Failed(repairItemId, "未知设备修复项，已拒绝诊断与写入。");
        }

        try
        {
            return await _platform.InspectAsync(definition, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DeviceDiagnosticSnapshot.Failed(repairItemId, $"只读设备诊断失败：{ex.Message}");
        }
    }

    public async Task<GameOptimizer.Core.Services.Repair.RepairResult> RepairAsync(
        string repairItemId,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!DeviceRepairCatalog.TryGet(repairItemId, out var definition))
        {
            return GameOptimizer.Core.Services.Repair.RepairResult.Failed("未知设备修复项，已拒绝执行。");
        }

        progress?.Report(new RepairProgress { Message = "正在执行只读设备诊断……", PercentComplete = 20 });
        var snapshot = await DiagnoseAsync(repairItemId, cancellationToken).ConfigureAwait(false);
        progress?.Report(new RepairProgress { Message = "设备写入已由中央安全边界阻断。", PercentComplete = 100 });

        var diagnostic = snapshot.IsApplicable
            ? $"只读诊断：{snapshot.Message}；资源数={snapshot.ResourceIdentities.Count}。"
            : $"只读诊断不可用：{snapshot.Message}。";
        return GameOptimizer.Core.Services.Repair.RepairResult.Failed(
            $"{definition.BlockReason} 当前仅开放只读诊断，不会修改设备、驱动、服务或注册表。",
            null,
            diagnostic);
    }
}

internal sealed class UnavailableDeviceDiagnosticPlatform : IDeviceDiagnosticPlatform
{
    public Task<DeviceDiagnosticSnapshot> InspectAsync(
        DeviceRepairDefinition definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DeviceDiagnosticSnapshot.Failed(
            definition.RepairItemId,
            "设备只读平台未接入。"));
    }
}

public static class DeviceRepairOwnerFallback
{
    public static IDeviceRepairOwner Instance { get; } =
        new DeviceRepairOwner(new UnavailableDeviceDiagnosticPlatform());
}
