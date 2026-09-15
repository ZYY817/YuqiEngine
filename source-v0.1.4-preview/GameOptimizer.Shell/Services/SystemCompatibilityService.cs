using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 系统兼容性检测服务
/// 检测当前Windows版本，判断各优化项的兼容性
/// </summary>
public interface ISystemCompatibilityService
{
    WindowsVersion CurrentVersion { get; }
    string VersionDisplayName { get; }
    bool IsWindows10OrLater { get; }
    bool IsWindows11OrLater { get; }
    CompatibilityResult CheckOperation(string operationId);
    IReadOnlyList<CompatibilityWarning> GetAllWarnings();
}

public enum WindowsVersion
{
    Unknown,
    Windows7,
    Windows8,
    Windows81,
    Windows10_1507,
    Windows10_1607,
    Windows10_1703,
    Windows10_1709,
    Windows10_1803,
    Windows10_1809,
    Windows10_1903,
    Windows10_1909,
    Windows10_2004,
    Windows10_20H2,
    Windows10_21H1,
    Windows10_21H2,
    Windows10_22H2,
    Windows11_21H2,
    Windows11_22H2,
    Windows11_23H2,
    Windows11_24H2
}

public record CompatibilityResult(
    bool IsCompatible,
    string? WarningMessage = null,
    string? Suggestion = null);

public record CompatibilityWarning(
    string OperationId,
    string OperationName,
    string Message,
    string Suggestion);

public class SystemCompatibilityService : ISystemCompatibilityService
{
    private readonly List<CompatibilityWarning> _warnings = new();

    public WindowsVersion CurrentVersion { get; }
    public string VersionDisplayName { get; }
    public bool IsWindows10OrLater => CurrentVersion >= WindowsVersion.Windows10_1507;
    public bool IsWindows11OrLater => CurrentVersion >= WindowsVersion.Windows11_21H2;

    public SystemCompatibilityService()
    {
        (CurrentVersion, VersionDisplayName) = DetectWindowsVersion();
        BuildWarnings();
    }

    private static (WindowsVersion, string) DetectWindowsVersion()
    {
        var loc = LocalizationService.Instance;
        try
        {
            var registryReader = WindowsRegistryInfoReader.Instance;
            var keyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var buildStr = registryReader.Read("HKLM", keyPath, "CurrentBuildNumber").Value?.ToString() ?? "0";
            var displayVersion = registryReader.Read("HKLM", keyPath, "DisplayVersion").Value?.ToString() ?? "";
            var productName = registryReader.Read("HKLM", keyPath, "ProductName").Value?.ToString() ?? "Windows";

            if (!int.TryParse(buildStr, out var build)) build = 0;

            var version = build switch
            {
                >= 26100 => WindowsVersion.Windows11_24H2,
                >= 22631 => WindowsVersion.Windows11_23H2,
                >= 22621 => WindowsVersion.Windows11_22H2,
                >= 22000 => WindowsVersion.Windows11_21H2,
                >= 19045 => WindowsVersion.Windows10_22H2,
                >= 19044 => WindowsVersion.Windows10_21H2,
                >= 19043 => WindowsVersion.Windows10_21H1,
                >= 19042 => WindowsVersion.Windows10_20H2,
                >= 19041 => WindowsVersion.Windows10_2004,
                >= 18363 => WindowsVersion.Windows10_1909,
                >= 18362 => WindowsVersion.Windows10_1903,
                >= 17763 => WindowsVersion.Windows10_1809,
                >= 17134 => WindowsVersion.Windows10_1803,
                >= 16299 => WindowsVersion.Windows10_1709,
                >= 15063 => WindowsVersion.Windows10_1703,
                >= 14393 => WindowsVersion.Windows10_1607,
                >= 10240 => WindowsVersion.Windows10_1507,
                >= 9600 => WindowsVersion.Windows81,
                >= 9200 => WindowsVersion.Windows8,
                >= 7601 => WindowsVersion.Windows7,
                _ => WindowsVersion.Unknown
            };

            var displayName = version switch
            {
                WindowsVersion.Windows11_24H2 => "Windows 11 24H2",
                WindowsVersion.Windows11_23H2 => "Windows 11 23H2",
                WindowsVersion.Windows11_22H2 => "Windows 11 22H2",
                WindowsVersion.Windows11_21H2 => "Windows 11 21H2",
                WindowsVersion.Windows10_22H2 => "Windows 10 22H2",
                WindowsVersion.Windows10_21H2 => "Windows 10 21H2",
                WindowsVersion.Windows10_21H1 => "Windows 10 21H1",
                WindowsVersion.Windows10_20H2 => "Windows 10 20H2",
                WindowsVersion.Windows10_2004 => "Windows 10 2004",
                WindowsVersion.Windows10_1909 => "Windows 10 1909",
                WindowsVersion.Windows10_1903 => "Windows 10 1903",
                WindowsVersion.Windows10_1809 => "Windows 10 1809",
                WindowsVersion.Windows10_1803 => "Windows 10 1803",
                WindowsVersion.Windows10_1709 => "Windows 10 1709",
                WindowsVersion.Windows10_1703 => "Windows 10 1703",
                WindowsVersion.Windows10_1607 => "Windows 10 1607",
                WindowsVersion.Windows10_1507 => "Windows 10 1507",
                WindowsVersion.Windows81 => "Windows 8.1",
                WindowsVersion.Windows8 => "Windows 8",
                WindowsVersion.Windows7 => "Windows 7",
                _ => productName
            };

            return (version, $"{displayName} (Build {build})");
        }
        catch
        {
            return (WindowsVersion.Unknown, LocalizationService.Instance.Get("Compat.DetectFailed"));
        }
    }

    private void BuildWarnings()
    {
        var loc = LocalizationService.Instance;
        
        // HAGS 仅 Win10 2004+ 支持
        if (CurrentVersion < WindowsVersion.Windows10_2004)
        {
            _warnings.Add(new CompatibilityWarning(
                "gpu.enable_hags",
                loc.Get("Compat.Hags.Name"),
                loc.Get("Compat.Hags.Message"),
                loc.Get("Compat.Hags.Suggestion")));
        }

        // 游戏模式 Win10 1703+ 支持
        if (CurrentVersion < WindowsVersion.Windows10_1703)
        {
            _warnings.Add(new CompatibilityWarning(
                "gaming.mode.enable",
                loc.Get("Compat.GameMode.Name"),
                loc.Get("Compat.GameMode.Message"),
                loc.Get("Compat.GameMode.Suggestion")));
        }

        // DVR 功能 Win10 1607+ 支持
        if (CurrentVersion < WindowsVersion.Windows10_1607)
        {
            _warnings.Add(new CompatibilityWarning(
                "gaming.dvr.disable",
                loc.Get("Compat.Dvr.Name"),
                loc.Get("Compat.Dvr.Message"),
                loc.Get("Compat.Dvr.Suggestion")));
        }

        // CTCP 拥塞控制 Win10+ 支持
        if (!IsWindows10OrLater)
        {
            _warnings.Add(new CompatibilityWarning(
                "network.set_congestion_ctcp",
                loc.Get("Compat.Ctcp.Name"),
                loc.Get("Compat.Ctcp.Message"),
                loc.Get("Compat.Ctcp.Suggestion")));
        }

        // Win11 特有警告
        if (IsWindows11OrLater)
        {
            _warnings.Add(new CompatibilityWarning(
                "basic.disable_visual_effects",
                loc.Get("Compat.VisualEffects.Name"),
                loc.Get("Compat.VisualEffects.Message"),
                loc.Get("Compat.VisualEffects.Suggestion")));
        }

        // 硬件相关警告 - 这些警告需要结合硬件检测结果动态判断
        // HPET 禁用警告（Intel CPU 不推荐）
        _warnings.Add(new CompatibilityWarning(
            "device.disable_hpet.intel",
            loc.Get("Compat.Hpet.Name"),
            loc.Get("Compat.Hpet.Message"),
            loc.Get("Compat.Hpet.Suggestion")));

        // Core Parking 警告（AMD X3D 需谨慎）
        _warnings.Add(new CompatibilityWarning(
            "cpu.disable_core_parking.x3d",
            loc.Get("Compat.CoreParking.Name"),
            loc.Get("Compat.CoreParking.Message"),
            loc.Get("Compat.CoreParking.Suggestion")));

        // UMBus 禁用警告
        _warnings.Add(new CompatibilityWarning(
            "device.disable_umbus",
            loc.Get("Compat.Umbus.Name"),
            loc.Get("Compat.Umbus.Message"),
            loc.Get("Compat.Umbus.Suggestion")));

        // 系统计时器禁用警告（Intel 需谨慎）
        _warnings.Add(new CompatibilityWarning(
            "device.disable_system_timer.intel",
            loc.Get("Compat.SystemTimer.Name"),
            loc.Get("Compat.SystemTimer.Message"),
            loc.Get("Compat.SystemTimer.Suggestion")));
    }

    public CompatibilityResult CheckOperation(string operationId)
    {
        var warning = _warnings.Find(w => w.OperationId == operationId);
        if (warning == null)
        {
            return new CompatibilityResult(true);
        }

        return new CompatibilityResult(false, warning.Message, warning.Suggestion);
    }

    public IReadOnlyList<CompatibilityWarning> GetAllWarnings() => _warnings.AsReadOnly();
}
