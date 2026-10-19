// File: GameOptimizer.Core/Services/GpuInventoryService.cs
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

public interface IGpuInventoryService
{
    GpuInventorySnapshot GetSnapshot(bool forceRefresh = false);
    void InvalidateCache();
}

/// <summary>
/// 唯一的 GPU 硬件清单来源。它只枚举当前会话中 WMI 可见的适配器；
/// 不把软件/远程适配器伪装成物理独显，也不将 WMI 属性当作 GPU Engine LUID。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GpuInventoryService : IGpuInventoryService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private GpuInventorySnapshot _snapshot = GpuInventorySnapshot.Empty;

    public GpuInventorySnapshot GetSnapshot(bool forceRefresh = false)
    {
        lock (_gate)
        {
            if (!forceRefresh && _snapshot.CapturedAtUtc != DateTimeOffset.MinValue &&
                DateTimeOffset.UtcNow - _snapshot.CapturedAtUtc < CacheDuration)
            {
                return _snapshot;
            }

            _snapshot = Detect();
            return _snapshot;
        }
    }

    public void InvalidateCache()
    {
        lock (_gate)
            _snapshot = GpuInventorySnapshot.Empty;
    }

    private static GpuInventorySnapshot Detect()
    {
        var adapters = new List<GpuAdapterInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterCompatibility, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);

            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var pnpDeviceId = item["PNPDeviceID"]?.ToString()?.Trim() ?? string.Empty;
                var compatibility = item["AdapterCompatibility"]?.ToString()?.Trim() ?? string.Empty;
                var vendor = DetectVendor(pnpDeviceId, compatibility, name);
                var isVirtualOrSoftware = IsVirtualOrSoftware(name, compatibility, pnpDeviceId);
                var (kind, classificationSource) = Classify(name, vendor, isVirtualOrSoftware);
                var stableKey = !string.IsNullOrWhiteSpace(pnpDeviceId)
                    ? pnpDeviceId
                    : $"wmi:{name}|{compatibility}";

                adapters.Add(new GpuAdapterInfo(
                    stableKey,
                    name,
                    vendor,
                    kind,
                    classificationSource,
                    ReadAdapterMemoryMb(item["AdapterRAM"]),
                    item["DriverVersion"]?.ToString()?.Trim() ?? "Unknown",
                    pnpDeviceId,
                    isVirtualOrSoftware));
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[GpuInventoryService] WMI enumeration failed: {exception.Message}");
        }

        IReadOnlyList<DxgiGpuAdapter> dxgiAdapters = DxgiGpuAdapterDiscovery.TryEnumerate();
        for (int index = 0; index < adapters.Count; index++)
        {
            GpuAdapterInfo adapter = adapters[index];
            DxgiGpuAdapter? dxgi = dxgiAdapters.FirstOrDefault(candidate =>
                candidate.VendorId == VendorIdFrom(adapter.PnpDeviceId) &&
                candidate.DeviceId == DeviceIdFrom(adapter.PnpDeviceId) &&
                string.Equals(candidate.Name, adapter.Name, StringComparison.OrdinalIgnoreCase));
            if (dxgi is not null)
                adapters[index] = adapter with { DxgiLuid = dxgi.Luid };
        }

        return new GpuInventorySnapshot(adapters, DateTimeOffset.UtcNow);
    }

    private static uint VendorIdFrom(string pnpDeviceId) => ReadPnpHexComponent(pnpDeviceId, "VEN_");

    private static uint DeviceIdFrom(string pnpDeviceId) => ReadPnpHexComponent(pnpDeviceId, "DEV_");

    private static uint ReadPnpHexComponent(string value, string marker)
    {
        int start = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || start + marker.Length + 4 > value.Length)
            return 0;
        return uint.TryParse(value.AsSpan(start + marker.Length, 4), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out uint component)
            ? component
            : 0;
    }

    private static long ReadAdapterMemoryMb(object? rawValue)
    {
        try
        {
            var raw = rawValue switch
            {
                uint unsigned => (long)unsigned,
                int signed when signed < 0 => unchecked((uint)signed),
                _ => Convert.ToInt64(rawValue ?? 0)
            };
            return Math.Max(0, raw) / 1024 / 1024;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static GpuAdapterVendor DetectVendor(string pnpDeviceId, string compatibility, string name)
    {
        var evidence = $"{pnpDeviceId} {compatibility} {name}".ToUpperInvariant();
        if (evidence.Contains("VEN_10DE") || evidence.Contains("NVIDIA")) return GpuAdapterVendor.Nvidia;
        if (evidence.Contains("VEN_1002") || evidence.Contains("AMD") || evidence.Contains("RADEON")) return GpuAdapterVendor.Amd;
        if (evidence.Contains("VEN_8086") || evidence.Contains("INTEL")) return GpuAdapterVendor.Intel;
        return GpuAdapterVendor.Unknown;
    }

    private static bool IsVirtualOrSoftware(string name, string compatibility, string pnpDeviceId)
    {
        // Why: Name/AdapterCompatibility 随系统语言本地化（中文为"Microsoft 基本显示适配器"），
        // PNPDeviceID 是稳定协议标识符——SWD\/ROOT\ 总线或缺 PCI\VEN_ 即软件/虚拟设备，与语言无关。
        var upperId = pnpDeviceId.ToUpperInvariant();
        if (upperId.StartsWith("SWD\\", StringComparison.Ordinal)
            || upperId.StartsWith("ROOT\\", StringComparison.Ordinal)
            || (upperId.Length > 0 && !upperId.Contains("PCI\\VEN_", StringComparison.Ordinal)))
        {
            return true;
        }

        var evidence = $"{name} {compatibility}".ToUpperInvariant();
        return evidence.Contains("MICROSOFT BASIC") || evidence.Contains("BASIC RENDER") ||
               evidence.Contains("REMOTE") || evidence.Contains("VIRTUAL") || evidence.Contains("HYPER-V");
    }

    private static (GpuAdapterKind Kind, string Source) Classify(string name, GpuAdapterVendor vendor, bool isVirtualOrSoftware)
    {
        if (isVirtualOrSoftware)
            return (GpuAdapterKind.SoftwareOrVirtual, "适配器名称表明软件、远程或虚拟设备");

        var normalizedName = name.ToUpperInvariant();
        if (vendor == GpuAdapterVendor.Intel && !normalizedName.Contains("ARC"))
            return (GpuAdapterKind.Integrated, "Intel 非 Arc 图形产品名称");
        if (vendor == GpuAdapterVendor.Nvidia || (vendor == GpuAdapterVendor.Intel && normalizedName.Contains("ARC")))
            return (GpuAdapterKind.Discrete, "厂商产品线识别");

        // AMD 同时生产集显和独显；没有可验证的统一 WMI 类型字段时宁可保留未知。
        return (GpuAdapterKind.Unknown, "当前 WMI 字段无法可靠区分集显与独显");
    }
}
