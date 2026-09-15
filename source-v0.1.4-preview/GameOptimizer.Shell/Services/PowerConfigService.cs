using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public class PowerConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<bool> ExportAsync(string filePath, Dictionary<string, int> settings, HardwareInfo hardware)
    {
        try
        {
            // 检查磁盘空间
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !SystemGuard.HasSufficientDiskSpace(dir, 512 * 1024))
            {
                return false;
            }

            var config = new PowerConfig
            {
                Version = "1.0",
                ExportTime = DateTime.Now,
                HardwareInfo = $"{hardware.CpuVendor} | {hardware.DeviceType} | {(hardware.IsIntelHybrid ? "大小核" : "标准")}",
                Settings = settings
            };

            var json = JsonSerializer.Serialize(config, JsonOptions);
            await SafeFileWriter.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(PowerConfig? Config, string? Error)> ImportAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return (null, "文件不存在");

            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var config = JsonSerializer.Deserialize<PowerConfig>(json, JsonOptions);

            if (config == null)
                return (null, "配置文件格式错误");

            if (config.Settings == null || config.Settings.Count == 0)
                return (null, "配置文件中没有设置项");

            return (config, null);
        }
        catch (JsonException)
        {
            return (null, "JSON 格式错误");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public (List<string> Compatible, List<string> Incompatible) ValidateSettings(
        PowerConfig config, HardwareInfo hardware)
    {
        var compatible = new List<string>();
        var incompatible = new List<string>();

        foreach (var kvp in config.Settings)
        {
            var def = PowerSettingsRegistry.GetById(kvp.Key);
            if (def == null)
            {
                incompatible.Add($"{kvp.Key}: 未知设置项");
                continue;
            }

            var isCompatible = def.CpuFilter switch
            {
                CpuVendorFilter.All => true,
                CpuVendorFilter.IntelOnly => hardware.CpuVendor == CpuVendor.Intel,
                CpuVendorFilter.IntelHybridOnly => hardware.CpuVendor == CpuVendor.Intel && hardware.IsIntelHybrid,
                CpuVendorFilter.AmdOnly => hardware.CpuVendor == CpuVendor.AMD,
                _ => true
            };

            if (isCompatible)
                compatible.Add(def.Name);
            else
                incompatible.Add($"{def.Name}: {LocalizationService.Instance.Get("PowerConfig.IncompatibleCpu")}");
        }

        return (compatible, incompatible);
    }
}
