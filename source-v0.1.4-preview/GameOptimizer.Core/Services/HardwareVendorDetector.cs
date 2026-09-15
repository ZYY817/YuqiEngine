// File: GameOptimizer.Core/Services/HardwareVendorDetector.cs
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

/// <summary>
/// Centralized hardware vendor detection to keep CPU/GPU related modules consistent.
/// </summary>
public static class HardwareVendorDetector
{
    private static readonly string[] AmdManufacturerMarkers =
    {
        "AUTHENTICAMD",
        "ADVANCED MICRO DEVICES",
        "AMD"
    };

    private static readonly string[] IntelManufacturerMarkers =
    {
        "GENUINEINTEL",
        "INTEL"
    };

    private static readonly string[] AmdCpuNameMarkers =
    {
        "AMD",
        "RYZEN",
        "EPYC",
        "THREADRIPPER",
        "ATHLON"
    };

    private static readonly string[] IntelCpuNameMarkers =
    {
        "INTEL",
        "XEON",
        "PENTIUM",
        "CELERON",
        "CORE ULTRA",
        "CORE(TM)",
        "CORE I3",
        "CORE I5",
        "CORE I7",
        "CORE I9",
        "I3-",
        "I5-",
        "I7-",
        "I9-"
    };

    public static CpuVendor DetectCpuVendor(string? manufacturer, string? cpuName)
    {
        var manufacturerUpper = (manufacturer ?? string.Empty).Trim().ToUpperInvariant();
        var cpuNameUpper = (cpuName ?? string.Empty).Trim().ToUpperInvariant();

        if (ContainsAny(manufacturerUpper, AmdManufacturerMarkers))
            return CpuVendor.AMD;

        if (ContainsAny(manufacturerUpper, IntelManufacturerMarkers))
            return CpuVendor.Intel;

        var hasAmdMarkers = ContainsAny(cpuNameUpper, AmdCpuNameMarkers);
        var hasIntelMarkers = ContainsAny(cpuNameUpper, IntelCpuNameMarkers);

        if (hasAmdMarkers && !hasIntelMarkers)
            return CpuVendor.AMD;

        if (hasIntelMarkers && !hasAmdMarkers)
            return CpuVendor.Intel;

        if (hasAmdMarkers)
            return CpuVendor.AMD;

        if (hasIntelMarkers)
            return CpuVendor.Intel;

        return CpuVendor.Unknown;
    }

    private static bool ContainsAny(string source, string[] markers)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        foreach (var marker in markers)
        {
            if (source.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
