using Microsoft.Win32;

namespace GameOptimizer.Core.Services;

public enum WindowsBuild
{
    Unknown = 0,
    Win10_1507 = 10240,
    Win10_1511 = 10586,
    Win10_1607 = 14393,
    Win10_1703 = 15063,
    Win10_1709 = 16299,
    Win10_1803 = 17134,
    Win10_1809 = 17763,
    Win10_1903 = 18362,
    Win10_1909 = 18363,
    Win10_2004 = 19041,
    Win10_20H2 = 19042,
    Win10_21H1 = 19043,
    Win10_21H2 = 19044,
    Win10_22H2 = 19045,
    Win11_21H2 = 22000,
    Win11_22H2 = 22621,
    Win11_23H2 = 22631,
    Win11_24H2 = 26100,
    Win11_25H2 = 26200,
    Win11_26H1 = 28000
}

public record WindowsVersionInfo
{
    public int MajorVersion { get; init; }
    public int MinorVersion { get; init; }
    public int BuildNumber { get; init; }
    public string DisplayVersion { get; init; } = string.Empty;
    public string EditionId { get; init; } = string.Empty;
    public bool IsWindows11 => BuildNumber >= 22000;
    public bool IsWindows10 => BuildNumber >= 10240 && BuildNumber < 22000;
    public bool IsServer { get; init; }
    public WindowsBuild KnownBuild { get; init; }

    public bool IsAtLeast(WindowsBuild build) => BuildNumber >= (int)build;
    public bool IsExactly(WindowsBuild build) => BuildNumber == (int)build;
}

public sealed class WindowsVersionService
{
    private static WindowsVersionInfo? _cached;
    private static readonly object _lock = new();

    public static WindowsVersionInfo Current
    {
        get
        {
            if (_cached != null) return _cached;
            lock (_lock)
            {
                _cached ??= Detect();
            }
            return _cached;
        }
    }

    private static WindowsVersionInfo Detect()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key == null) return new WindowsVersionInfo();

            var buildStr = key.GetValue("CurrentBuildNumber")?.ToString() ?? "0";
            int.TryParse(buildStr, out var buildNumber);

            var majorStr = key.GetValue("CurrentMajorVersionNumber")?.ToString() ?? "10";
            int.TryParse(majorStr, out var major);

            var displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? 
                                 key.GetValue("ReleaseId")?.ToString() ?? "";
            var editionId = key.GetValue("EditionID")?.ToString() ?? "";
            var productName = key.GetValue("ProductName")?.ToString() ?? "";
            var isServer = productName.Contains("Server", StringComparison.OrdinalIgnoreCase);

            var knownBuild = buildNumber switch
            {
                >= 28000 => WindowsBuild.Win11_26H1,
                >= 26200 => WindowsBuild.Win11_25H2,
                >= 26100 => WindowsBuild.Win11_24H2,
                >= 22631 => WindowsBuild.Win11_23H2,
                >= 22621 => WindowsBuild.Win11_22H2,
                >= 22000 => WindowsBuild.Win11_21H2,
                >= 19045 => WindowsBuild.Win10_22H2,
                >= 19044 => WindowsBuild.Win10_21H2,
                >= 19043 => WindowsBuild.Win10_21H1,
                >= 19042 => WindowsBuild.Win10_20H2,
                >= 19041 => WindowsBuild.Win10_2004,
                >= 18363 => WindowsBuild.Win10_1909,
                >= 18362 => WindowsBuild.Win10_1903,
                >= 17763 => WindowsBuild.Win10_1809,
                >= 17134 => WindowsBuild.Win10_1803,
                >= 16299 => WindowsBuild.Win10_1709,
                >= 15063 => WindowsBuild.Win10_1703,
                >= 14393 => WindowsBuild.Win10_1607,
                >= 10586 => WindowsBuild.Win10_1511,
                >= 10240 => WindowsBuild.Win10_1507,
                _ => WindowsBuild.Unknown
            };

            return new WindowsVersionInfo
            {
                MajorVersion = major,
                MinorVersion = 0,
                BuildNumber = buildNumber,
                DisplayVersion = displayVersion,
                EditionId = editionId,
                IsServer = isServer,
                KnownBuild = knownBuild
            };
        }
        catch
        {
            return new WindowsVersionInfo();
        }
    }

    public static bool SupportsFeature(string featureId)
    {
        if (string.IsNullOrWhiteSpace(featureId)) return false;
        var version = Current;
        return featureId switch
        {
            "HAGS" => version.IsAtLeast(WindowsBuild.Win10_2004),
            "GameMode" => version.IsAtLeast(WindowsBuild.Win10_1703),
            "UltimatePerformance" => version.IsAtLeast(WindowsBuild.Win10_1803),
            "DynamicTick" => true,
            "CoreParking" => true,
            "MemoryCompression" => version.IsAtLeast(WindowsBuild.Win10_1507),
            "WSL2" => version.IsAtLeast(WindowsBuild.Win10_1903),
            "WinGet" => version.IsAtLeast(WindowsBuild.Win10_1809),
            "WidgetsBoard" => version.IsWindows11,
            "SnapLayouts" => version.IsWindows11,
            "AndroidApps" => version.IsAtLeast(WindowsBuild.Win11_22H2),
            _ => true
        };
    }

    public static string GetFriendlyName()
    {
        var v = Current;
        var baseName = v.IsWindows11 ? "Windows 11" : "Windows 10";
        return $"{baseName} {v.DisplayVersion} (Build {v.BuildNumber})";
    }
}
