// File: GameOptimizer.Core/Models/GpuInventoryModels.cs
namespace GameOptimizer.Core.Models;

/// <summary>
/// GPU 厂商。该枚举只表示已识别的硬件厂商，不从负载或显存大小反推。
/// </summary>
public enum GpuAdapterVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel
}

/// <summary>
/// 图形适配器类型。Unknown 必须被保留，不能以名称或适配器顺序强行归类。
/// </summary>
public enum GpuAdapterKind
{
    Unknown,
    Integrated,
    Discrete,
    SoftwareOrVirtual
}

/// <summary>
/// 来自 Win32_VideoController 的当前会话可见 GPU 清单项。
/// PNP 设备 ID 是稳定硬件键；WMI 不提供 DXGI LUID，因此它不能单独用于负载归属。
/// </summary>
public sealed record GpuAdapterInfo(
    string StableKey,
    string Name,
    GpuAdapterVendor Vendor,
    GpuAdapterKind Kind,
    string ClassificationSource,
    long DedicatedMemoryMb,
    string DriverVersion,
    string PnpDeviceId,
    bool IsVirtualOrSoftware,
    string? DxgiLuid = null);

public sealed record GpuInventorySnapshot(IReadOnlyList<GpuAdapterInfo> Adapters, DateTimeOffset CapturedAtUtc)
{
    public static readonly GpuInventorySnapshot Empty = new(Array.Empty<GpuAdapterInfo>(), DateTimeOffset.MinValue);

    public bool HasVendor(GpuAdapterVendor vendor) => Adapters.Any(adapter => adapter.Vendor == vendor);

    public GpuAdapterInfo? PreferredAdapter => Adapters
        .OrderBy(adapter => adapter.Kind == GpuAdapterKind.Discrete ? 0 : adapter.Kind == GpuAdapterKind.Integrated ? 1 : 2)
        .ThenByDescending(adapter => adapter.DedicatedMemoryMb)
        .FirstOrDefault();
}

/// <summary>
/// GPU Engine 负载对应的硬件归属。只有 LUID 与硬件清单有同一可信键时，Adapter 才可设置。
/// </summary>
public sealed record GpuLoadAttribution(
    double UtilizationPercent,
    string? AdapterStableKey,
    string? AdapterName,
    GpuAdapterKind? AdapterKind,
    string Source,
    string Detail)
{
    public bool IsMapped => !string.IsNullOrWhiteSpace(AdapterStableKey);

    public static GpuLoadAttribution Unavailable(string source, string detail) =>
        new(0, null, null, null, source, detail);

    public static GpuLoadAttribution Unmapped(double utilizationPercent, string source, string detail) =>
        new(Math.Clamp(utilizationPercent, 0, 100), null, null, null, source, detail);
}
