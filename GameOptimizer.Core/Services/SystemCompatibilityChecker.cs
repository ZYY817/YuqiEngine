// File: GameOptimizer.Core/Services/SystemCompatibilityChecker.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using GameOptimizer.Core.Diagnostics;
using Microsoft.Win32;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 系统兼容性检查器 - 检查操作系统、服务、注册表等兼容性
/// </summary>
public class SystemCompatibilityChecker
{
    private static readonly Lazy<SystemInfo> _cachedSystemInfo = new(GetCurrentSystemInfo);
    
    /// <summary>
    /// 获取当前系统信息（缓存）
    /// </summary>
    public static SystemInfo CurrentSystem => _cachedSystemInfo.Value;
    
    /// <summary>
    /// 检查 Windows 版本是否满足最低要求
    /// </summary>
    public static bool IsWindowsVersionSupported(int minBuild)
    {
        return CurrentSystem.WindowsBuild >= minBuild;
    }
    
    /// <summary>
    /// 检查服务是否存在且可用
    /// </summary>
    public static bool IsServiceAvailable(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            _ = service.Status; // 触发查询，如果服务不存在会抛出异常
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查服务是否正在运行
    /// </summary>
    public static bool IsServiceRunning(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            return service.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查服务启动类型
    /// </summary>
    public static ServiceStartMode? GetServiceStartMode(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            return service.StartType;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// 检查注册表键是否存在
    /// </summary>
    public static bool IsRegistryKeyAvailable(string keyPath, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            return key != null;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查注册表值是否存在
    /// </summary>
    public static bool IsRegistryValueAvailable(string keyPath, string valueName, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null) return false;
            
            var value = key.GetValue(valueName);
            return value != null;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查 PowerShell 命令是否可用
    /// </summary>
    public static bool IsPowerShellCmdletAvailable(string cmdlet)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Get-Command {cmdlet} -ErrorAction SilentlyContinue\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return false;
            
            // Why: 同步 ReadToEnd 在缓冲区满时死锁，改为异步读取后同步等待
            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }
            var output = outputTask.GetAwaiter().GetResult();
            
            return !string.IsNullOrWhiteSpace(output) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查硬件特性是否支持（通过 WMI）
    /// </summary>
    public static bool IsHardwareFeatureSupported(string feature)
    {
        try
        {
            return feature.ToLowerInvariant() switch
            {
                "virtualization" => CheckVirtualizationSupport(),
                "tpm" => CheckTpmSupport(),
                "secureboot" => CheckSecureBootSupport(),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查电源服务健康状态
    /// </summary>
    public static PowerServiceHealth CheckPowerServiceHealth()
    {
        var health = new PowerServiceHealth();
        
        try
        {
            // 检查 Power 服务
            health.PowerServiceExists = IsServiceAvailable("Power");
            health.PowerServiceRunning = IsServiceRunning("Power");
            health.PowerServiceStartMode = GetServiceStartMode("Power");
            
            // 检查电源计划是否可访问
            health.PowerSchemesAccessible = CheckPowerSchemesAccessible();
            
            // 检查 powercfg.exe 是否可用
            health.PowerCfgAvailable = CheckPowerCfgAvailable();
            
            health.IsHealthy = health.PowerServiceExists && 
                               health.PowerServiceRunning && 
                               health.PowerSchemesAccessible &&
                               health.PowerCfgAvailable;
        }
        catch (Exception ex)
        {
            health.ErrorMessage = ex.Message;
        }
        
        return health;
    }
    
    /// <summary>
    /// 检查网络适配器是否可用
    /// </summary>
    public static bool IsNetworkAdapterAvailable(string adapterName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter WHERE NetEnabled=true");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? string.Empty;
                if (name.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 执行完整的兼容性检查
    /// </summary>
    public static CompatibilityInfo CheckCompatibility(SystemRequirement requirement)
    {
        var missingDeps = new List<string>();
        var isCompatible = true;
        var reasons = new List<string>();
        
        // 检查 Windows 版本
        if (requirement.MinWindowsBuild.HasValue)
        {
            if (CurrentSystem.WindowsBuild < requirement.MinWindowsBuild.Value)
            {
                isCompatible = false;
                reasons.Add($"需要 Windows Build {requirement.MinWindowsBuild.Value} 或更高版本");
            }
        }
        
        // 检查必需的服务
        if (requirement.RequiredServices != null)
        {
            foreach (var service in requirement.RequiredServices)
            {
                if (!IsServiceAvailable(service))
                {
                    isCompatible = false;
                    missingDeps.Add($"服务: {service}");
                    reasons.Add($"缺少必需的服务: {service}");
                }
            }
        }
        
        // 检查必需的注册表键
        if (requirement.RequiredRegistryKeys != null)
        {
            foreach (var regKey in requirement.RequiredRegistryKeys)
            {
                if (!IsRegistryKeyAvailable(regKey))
                {
                    isCompatible = false;
                    missingDeps.Add($"注册表键: {regKey}");
                    reasons.Add($"缺少必需的注册表键: {regKey}");
                }
            }
        }
        
        // 检查必需的硬件特性
        if (requirement.RequiredHardwareFeatures != null)
        {
            foreach (var feature in requirement.RequiredHardwareFeatures)
            {
                if (!IsHardwareFeatureSupported(feature))
                {
                    isCompatible = false;
                    missingDeps.Add($"硬件特性: {feature}");
                    reasons.Add($"不支持必需的硬件特性: {feature}");
                }
            }
        }
        
        return new CompatibilityInfo
        {
            IsCompatible = isCompatible,
            Reason = isCompatible ? "系统兼容" : string.Join("; ", reasons),
            MinimumRequirement = requirement,
            CurrentSystem = CurrentSystem,
            MissingDependencies = missingDeps.Count > 0 ? missingDeps : null
        };
    }
    
    // ==================== 私有辅助方法 ====================
    
    private static SystemInfo GetCurrentSystemInfo()
    {
        var info = new SystemInfo
        {
            WindowsBuild = Environment.OSVersion.Version.Build,
            WindowsVersion = GetWindowsVersionName(),
            IsAdminMode = WindowsElevation.IsElevated
        };
        
        return info;
    }
    
    private static string GetWindowsVersionName()
    {
        var build = Environment.OSVersion.Version.Build;
        return build switch
        {
            >= 22000 => "Windows 11",
            >= 19041 => "Windows 10 (2004+)",
            >= 18362 => "Windows 10 (1903)",
            >= 17763 => "Windows 10 (1809)",
            >= 17134 => "Windows 10 (1803)",
            >= 16299 => "Windows 10 (1709)",
            >= 15063 => "Windows 10 (1703)",
            >= 14393 => "Windows 10 (1607)",
            >= 10586 => "Windows 10 (1511)",
            >= 10240 => "Windows 10 (RTM)",
            >= 9600 => "Windows 8.1",
            >= 9200 => "Windows 8",
            >= 7601 => "Windows 7 SP1",
            >= 7600 => "Windows 7",
            _ => $"Windows (Build {build})"
        };
    }
    
    private static bool IsAdministrator() => WindowsElevation.IsElevated;
    
    private static bool CheckVirtualizationSupport()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            foreach (var obj in searcher.Get())
            {
                // Why: WMI 可能返回 bool/string "True"/int 1，直接 (bool) 会 InvalidCastException
                var virtEnabled = obj["VirtualizationFirmwareEnabled"];
                if (virtEnabled is null) continue;
                try { if (Convert.ToBoolean(virtEnabled)) return true; }
                catch { /* 类型转换失败，视为不支持 */ }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    private static bool CheckTpmSupport()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2\\Security\\MicrosoftTpm", "SELECT * FROM Win32_Tpm");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);
            return searcher.Get().Count > 0;
        }
        catch
        {
            return false;
        }
    }
    
    private static bool CheckSecureBootSupport()
    {
        try
        {
            return IsRegistryValueAvailable(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        }
        catch
        {
            return false;
        }
    }
    
    private static bool CheckPowerSchemesAccessible()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/list",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return false;
            
            // Why: 同步 ReadToEnd 在缓冲区满时死锁，改为异步读取后同步等待
            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }
            var output = outputTask.GetAwaiter().GetResult();
            
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }
    
    private static bool CheckPowerCfgAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/?",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return false;
            
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 电源服务健康状态
/// </summary>
public record PowerServiceHealth
{
    public bool PowerServiceExists { get; set; }
    public bool PowerServiceRunning { get; set; }
    public ServiceStartMode? PowerServiceStartMode { get; set; }
    public bool PowerSchemesAccessible { get; set; }
    public bool PowerCfgAvailable { get; set; }
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    
    public string GetDiagnosticMessage()
    {
        if (IsHealthy) return "电源服务正常";
        
        var issues = new List<string>();
        
        if (!PowerServiceExists)
            issues.Add("Power 服务不存在");
        else if (!PowerServiceRunning)
            issues.Add("Power 服务未运行");
        else if (PowerServiceStartMode == ServiceStartMode.Disabled)
            issues.Add("Power 服务被禁用");
        
        if (!PowerSchemesAccessible)
            issues.Add("无法访问电源计划");
        
        if (!PowerCfgAvailable)
            issues.Add("powercfg.exe 不可用");
        
        if (!string.IsNullOrEmpty(ErrorMessage))
            issues.Add($"错误: {ErrorMessage}");
        
        return string.Join("; ", issues);
    }
}
