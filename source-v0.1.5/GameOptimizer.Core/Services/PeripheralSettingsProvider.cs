// File: GameOptimizer.Core/Services/PeripheralSettingsProvider.cs
using System;
using System.IO;
using System.Text.Json;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 外设高级设置提供器 - 统一管理配置读取，避免代码重复
/// </summary>
public static class PeripheralSettingsProvider
{
    private static string SettingsPath => AppPaths.PeripheralSettingsFilePath;

    private static PeripheralSettings? _cachedSettings;
    private static DateTime _lastReadTime;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 获取外设设置，带缓存机制
    /// </summary>
    public static PeripheralSettings? GetSettings()
    {
        if (_cachedSettings != null && DateTime.Now - _lastReadTime < CacheExpiration)
        {
            return _cachedSettings;
        }

        try
        {
            if (!File.Exists(SettingsPath))
            {
                _cachedSettings = null;
                return null;
            }

            var json = File.ReadAllText(SettingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _cachedSettings = null;
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("PeripheralSettings", out var peripheralSettings))
            {
                _cachedSettings = null;
                return null;
            }

            _cachedSettings = new PeripheralSettings
            {
                MouseSpeed = peripheralSettings.TryGetProperty("MouseSpeed", out var ms) ? ms.GetInt32() : 0,
                MouseThreshold1 = peripheralSettings.TryGetProperty("MouseThreshold1", out var mt1) ? mt1.GetInt32() : 0,
                MouseThreshold2 = peripheralSettings.TryGetProperty("MouseThreshold2", out var mt2) ? mt2.GetInt32() : 0,
                KeyboardDelay = peripheralSettings.TryGetProperty("KeyboardDelay", out var kd) ? kd.GetString() ?? "0" : "0",
                KeyboardSpeed = peripheralSettings.TryGetProperty("KeyboardSpeed", out var ks) ? ks.GetString() ?? "31" : "31",
                MouseDataQueueSize = peripheralSettings.TryGetProperty("MouseDataQueueSize", out var mq) ? mq.GetInt32() : 200,
                KeyboardDataQueueSize = peripheralSettings.TryGetProperty("KeyboardDataQueueSize", out var kq) ? kq.GetInt32() : 200
            };

            _lastReadTime = DateTime.Now;
            return _cachedSettings;
        }
        catch (JsonException)
        {
            // 配置文件格式错误，返回null使用默认值
            _cachedSettings = null;
            return null;
        }
        catch (IOException)
        {
            // 文件读取失败，返回null使用默认值
            _cachedSettings = null;
            return null;
        }
        catch
        {
            // 任何其他异常都降级到默认值
            _cachedSettings = null;
            return null;
        }
    }

    /// <summary>
    /// 使缓存失效，强制下次重新读取
    /// </summary>
    public static void InvalidateCache()
    {
        _cachedSettings = null;
    }
}

/// <summary>
/// 外设设置数据模型
/// </summary>
public sealed class PeripheralSettings
{
    public int MouseSpeed { get; set; }
    public int MouseThreshold1 { get; set; }
    public int MouseThreshold2 { get; set; }
    public string KeyboardDelay { get; set; } = "0";
    public string KeyboardSpeed { get; set; } = "31";
    public int MouseDataQueueSize { get; set; }
    public int KeyboardDataQueueSize { get; set; }
}
