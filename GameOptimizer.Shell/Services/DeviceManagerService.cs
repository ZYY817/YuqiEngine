using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 设备管理器服务 - 用于启用/禁用系统设备
/// </summary>
[SupportedOSPlatform("windows")]
public interface IDeviceManagerService
{
    IReadOnlyList<DeviceInfo> GetDevices(string className);
    DeviceInfo? FindDevice(string deviceName);
    bool EnableDevice(string deviceId);
    bool DisableDevice(string deviceId);
    bool IsDeviceEnabled(string deviceId);
}

/// <summary>
/// 设备信息
/// </summary>
public record DeviceInfo(
    string DeviceId,
    string Name,
    string Description,
    string ClassName,
    bool IsEnabled,
    string Status);

[SupportedOSPlatform("windows")]
public class DeviceManagerService : IDeviceManagerService
{
    // 常见的可优化设备名称（用于匹配）
    public static class KnownDevices
    {
        public const string HighPrecisionEventTimer = "High precision event timer";
        public const string SystemTimer = "System timer";
        public const string UMBusRootBusEnumerator = "UMBus Root Bus Enumerator";
        public const string CompositeBusEnumerator = "Composite Bus Enumerator";
    }

    public IReadOnlyList<DeviceInfo> GetDevices(string className)
    {
        var devices = new List<DeviceInfo>();
        try
        {
            if (!string.IsNullOrEmpty(className)
                && (className.Length > 64
                    || className.Any(character =>
                        !char.IsLetterOrDigit(character)
                        && character is not ('_' or '-' or ' '))))
            {
                return devices;
            }

            var query = string.IsNullOrEmpty(className)
                ? "SELECT * FROM Win32_PnPEntity"
                : $"SELECT * FROM Win32_PnPEntity WHERE PNPClass = '{className}'";

            using var searcher = new ManagementObjectSearcher(query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            foreach (var obj in searcher.Get())
            {
                var deviceId = obj["DeviceID"]?.ToString() ?? "";
                var name = obj["Name"]?.ToString() ?? "";
                var desc = obj["Description"]?.ToString() ?? "";
                var pnpClass = obj["PNPClass"]?.ToString() ?? "";
                var status = obj["Status"]?.ToString() ?? "";
                var configMgrErrorCode = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);
                
                // ConfigManagerErrorCode: 0 = 正常, 22 = 已禁用
                var isEnabled = configMgrErrorCode != 22;

                if (!string.IsNullOrEmpty(deviceId))
                {
                    devices.Add(new DeviceInfo(deviceId, name, desc, pnpClass, isEnabled, status));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceManager] GetDevices error: {ex.Message}");
        }
        return devices;
    }

    public DeviceInfo? FindDevice(string deviceName)
    {
        try
        {
            var query = $"SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%{EscapeWmiString(deviceName)}%'";
            using var searcher = new ManagementObjectSearcher(query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            foreach (var obj in searcher.Get())
            {
                var deviceId = obj["DeviceID"]?.ToString() ?? "";
                var name = obj["Name"]?.ToString() ?? "";
                var desc = obj["Description"]?.ToString() ?? "";
                var pnpClass = obj["PNPClass"]?.ToString() ?? "";
                var status = obj["Status"]?.ToString() ?? "";
                var configMgrErrorCode = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);
                var isEnabled = configMgrErrorCode != 22;

                if (!string.IsNullOrEmpty(deviceId))
                {
                    return new DeviceInfo(deviceId, name, desc, pnpClass, isEnabled, status);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceManager] FindDevice error: {ex.Message}");
        }
        return null;
    }

    public bool IsDeviceEnabled(string deviceId)
    {
        try
        {
            var escapedId = EscapeWmiString(deviceId);
            var query = $"SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID = '{escapedId}'";
            using var searcher = new ManagementObjectSearcher(query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            foreach (var obj in searcher.Get())
            {
                var errorCode = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);
                return errorCode != 22; // 22 = 已禁用
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceManager] IsDeviceEnabled error: {ex.Message}");
        }
        return false;
    }

    public bool EnableDevice(string deviceId)
    {
        return false;
    }

    public bool DisableDevice(string deviceId)
    {
        return false;
    }

    private static string EscapeWmiString(string input)
    {
        // WMI 查询中需要转义反斜杠
        return input.Replace("\\", "\\\\").Replace("'", "\\'");
    }

}
