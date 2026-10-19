// File: GameOptimizer.Core/Services/ProcessPriority/RegistryPriorityService.cs
using System;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GameOptimizer.Core.Services.ProcessPriority;

/// <summary>
/// 注册表进程优先级服务实现
/// 通过 HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options 设置优先级
/// </summary>
public class RegistryPriorityService : IRegistryPriorityService
{
    private const string IFEO_BASE_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string PERF_OPTIONS_KEY = "PerfOptions";
    private const string CPU_PRIORITY_VALUE = "CpuPriorityClass";
    
    private readonly ILogger<RegistryPriorityService> _logger;

    public RegistryPriorityService(ILogger<RegistryPriorityService> logger)
    {
        _logger = logger;
    }

    public bool HasAdminPrivileges() => WindowsElevation.IsElevated;

    public OperationResult SetProcessPriority(string executableName, CpuPriorityLevel priority)
    {
        _ = executableName;
        _ = priority;
        return OperationResult.Failed("IFEO persistent priority is blocked: it is a separate resource and has no verified durable owner yet.");
#if false
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return OperationResult.Failed("Executable name cannot be empty");
        }

        if (!HasAdminPrivileges())
        {
            return OperationResult.Failed("Administrator privileges required to modify registry");
        }

        try
        {
            // 【P0-002 修复】：使用 SafeRegistryHelper 安全访问注册表
            var (baseKey, baseError) = SafeRegistryHelper.OpenKeyWritable(Registry.LocalMachine, IFEO_BASE_PATH);
            if (baseKey == null)
            {
                _logger.LogError("[RegistryPriorityService] Failed to open IFEO base key: {Error}", baseError);
                return OperationResult.Failed(baseError ?? "Failed to access registry base key");
            }

            using (baseKey)
            {
                var (exeKey, exeError) = SafeRegistryHelper.CreateOrOpenKey(baseKey, executableName);
                if (exeKey == null)
                {
                    _logger.LogError("[RegistryPriorityService] Failed to create executable key: {ExeName}, Error: {Error}", executableName, exeError);
                    return OperationResult.Failed(exeError ?? $"Failed to create registry key for {executableName}");
                }

                using (exeKey)
                {
                    var (perfKey, perfError) = SafeRegistryHelper.CreateOrOpenKey(exeKey, PERF_OPTIONS_KEY);
                    if (perfKey == null)
                    {
                        _logger.LogError("[RegistryPriorityService] Failed to create PerfOptions key: {Error}", perfError);
                        return OperationResult.Failed(perfError ?? "Failed to create PerfOptions key");
                    }

                    using (perfKey)
                    {
                        perfKey.SetValue(CPU_PRIORITY_VALUE, (int)priority, RegistryValueKind.DWord);
                        
                        _logger.LogInformation(
                            "[RegistryPriorityService] Set priority to {Priority} for {ExeName}",
                            priority, executableName);

                        return OperationResult.Success($"Successfully set priority to {priority} for {executableName}");
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "[RegistryPriorityService] Access denied when setting priority");
            return OperationResult.Failed("Access denied. Please run as administrator.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RegistryPriorityService] Failed to set priority for {ExeName}", executableName);
            return OperationResult.Failed($"Failed to set priority: {ex.Message}");
        }
#endif
    }

    public OperationResult RemoveProcessPriority(string executableName)
    {
        _ = executableName;
        return OperationResult.Failed("IFEO persistent priority is blocked: it is a separate resource and has no verified durable owner yet.");
#if false
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return OperationResult.Failed("Executable name cannot be empty");
        }

        if (!HasAdminPrivileges())
        {
            return OperationResult.Failed("Administrator privileges required to modify registry");
        }

        try
        {
            // 【P0-002 修复】：使用 SafeRegistryHelper 安全访问注册表
            var (baseKey, baseError) = SafeRegistryHelper.OpenKeyWritable(Registry.LocalMachine, IFEO_BASE_PATH);
            if (baseKey == null)
            {
                return OperationResult.Failed(baseError ?? "Failed to access registry base key");
            }

            using (baseKey)
            {
                var (exeKey, exeError) = SafeRegistryHelper.OpenKeyWritable(baseKey, executableName);
                if (exeKey == null)
                {
                    _logger.LogDebug("[RegistryPriorityService] No registry key found for {ExeName}", executableName);
                    return OperationResult.Success("No configuration found to remove");
                }

                using (exeKey)
                {
                    try
                    {
                        exeKey.DeleteSubKeyTree(PERF_OPTIONS_KEY);
                        _logger.LogInformation("[RegistryPriorityService] Removed PerfOptions for {ExeName}", executableName);
                    }
                    catch (ArgumentException)
                    {
                        _logger.LogDebug("[RegistryPriorityService] PerfOptions key not found for {ExeName}", executableName);
                    }

                    var subKeyCount = exeKey.SubKeyCount;
                    var valueCount = exeKey.ValueCount;
                    
                    if (subKeyCount == 0 && valueCount == 0)
                    {
                        exeKey.Close();
                        baseKey.DeleteSubKey(executableName);
                        _logger.LogInformation("[RegistryPriorityService] Removed empty executable key: {ExeName}", executableName);
                    }
                }
            }

            return OperationResult.Success($"Successfully removed priority configuration for {executableName}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "[RegistryPriorityService] Access denied when removing priority");
            return OperationResult.Failed("Access denied. Please run as administrator.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RegistryPriorityService] Failed to remove priority for {ExeName}", executableName);
            return OperationResult.Failed($"Failed to remove priority: {ex.Message}");
        }
#endif
    }

    public bool IsConfigured(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return false;
        }

        try
        {
            var keyPath = $@"{IFEO_BASE_PATH}\{executableName}\{PERF_OPTIONS_KEY}";
            
            // 【P0-002 修复】：使用 SafeRegistryHelper 安全访问注册表
            var (key, error) = SafeRegistryHelper.OpenKey(Registry.LocalMachine, keyPath);
            if (key == null)
            {
                return false;
            }

            using (key)
            {
                var value = key.GetValue(CPU_PRIORITY_VALUE);
                return value != null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[RegistryPriorityService] Failed to check configuration for {ExeName}", executableName);
            return false;
        }
    }

    public CpuPriorityLevel? GetCurrentPriority(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        try
        {
            var keyPath = $@"{IFEO_BASE_PATH}\{executableName}\{PERF_OPTIONS_KEY}";
            
            // 【P0-002 修复】：使用 SafeRegistryHelper 安全访问注册表
            var (key, error) = SafeRegistryHelper.OpenKey(Registry.LocalMachine, keyPath);
            if (key == null)
            {
                return null;
            }

            using (key)
            {
                var value = key.GetValue(CPU_PRIORITY_VALUE);
                if (value is int intValue && Enum.IsDefined(typeof(CpuPriorityLevel), intValue))
                {
                    return (CpuPriorityLevel)intValue;
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[RegistryPriorityService] Failed to get priority for {ExeName}", executableName);
            return null;
        }
    }
}
