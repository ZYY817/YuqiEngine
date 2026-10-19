using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services;

[SupportedOSPlatform("windows")]
public static class ServiceDependencyChecker
{
    private static LocalizationService Loc => LocalizationService.Instance;
    // P0-2: 关键系统服务 - 禁用这些服务可能导致系统不稳定
    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs",           // Remote Procedure Call - 核心服务
        "RpcEptMapper",    // RPC Endpoint Mapper
        "DcomLaunch",      // DCOM Server Process Launcher
        "LSM",             // Local Session Manager
        "PlugPlay",        // Plug and Play
        "Power",           // 电源服务
        "Winmgmt",         // WMI
        "EventLog",        // Windows Event Log
        "Schedule",        // Task Scheduler
        "SENS",            // System Event Notification Service
        "Themes",          // 主题服务（影响UI）
        "AudioSrv",        // Windows Audio
        "AudioEndpointBuilder",
        "Dhcp",            // DHCP Client
        "Dnscache",        // DNS Client
        "LanmanWorkstation", // Workstation
        "nsi",             // Network Store Interface Service
        "Netman",          // Network Connections
        "NlaSvc",          // Network Location Awareness
        "WlanSvc",         // WLAN AutoConfig
        "Wcmsvc",          // Windows Connection Manager
        "SecurityHealthService", // Windows Security Health Service
        "wscsvc",          // Security Center
        "BFE",             // Base Filtering Engine
        "MpsSvc",          // Windows Defender Firewall
        "EFS",             // Encrypting File System
        "AppXSvc",         // AppX Deployment Service
        "ClipSVC",         // Client License Service
        "InstallService",  // Microsoft Store Install Service
    };

    // 游戏相关服务 - 禁用可能影响游戏体验
    private static readonly HashSet<string> GamingRelatedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "BrokerInfrastructure", // Background Tasks Infrastructure
        "XblAuthManager",       // Xbox Live Auth Manager
        "XblGameSave",          // Xbox Live Game Save
        "XboxGipSvc",           // Xbox Accessory Management
        "XboxNetApiSvc",        // Xbox Live Networking
    };

    public static ServiceCheckResult CheckBeforeDisable(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return new ServiceCheckResult(false, Loc.Get("Service.InvalidName"));

        // 检查是否为关键服务
        if (CriticalServices.Contains(serviceName))
            return new ServiceCheckResult(false, Loc.Get("Service.CriticalWarning", serviceName));

        // 检查游戏相关服务
        if (GamingRelatedServices.Contains(serviceName))
            return new ServiceCheckResult(true, Loc.Get("Service.GamingRelatedWarning", serviceName), true);

        // 检查依赖链
        var (hasDependents, dependentNames) = GetRunningDependents(serviceName);
        if (hasDependents)
        {
            var depList = string.Join(", ", dependentNames.Take(5));
            if (dependentNames.Count > 5) depList += Loc.Get("Service.AndMoreServices", dependentNames.Count);
            return new ServiceCheckResult(true, Loc.Get("Service.DependentsWarning", serviceName, depList), true);
        }

        // 检查此服务依赖的服务
        var (hasDependencies, dependencyNames) = GetServiceDependencies(serviceName);
        if (hasDependencies && dependencyNames.Any(d => CriticalServices.Contains(d)))
        {
            return new ServiceCheckResult(true, Loc.Get("Service.DependsOnCritical"));
        }

        return new ServiceCheckResult(true, string.Empty);
    }

    private static (bool HasDependents, List<string> Names) GetRunningDependents(string serviceName)
    {
        var names = new List<string>();
        try
        {
            using var controller = new ServiceController(serviceName);
            var dependents = controller.DependentServices;
            if (dependents is null || dependents.Length == 0)
                return (false, names);

            foreach (var dep in dependents)
            {
                try
                {
                    if (dep.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                        names.Add(dep.DisplayName);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ServiceDependencyChecker] Error checking dependent {dep.ServiceName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceDependencyChecker] Error getting dependents for {serviceName}: {ex.Message}");
        }
        return (names.Count > 0, names);
    }

    private static (bool HasDependencies, List<string> Names) GetServiceDependencies(string serviceName)
    {
        var names = new List<string>();
        try
        {
            using var controller = new ServiceController(serviceName);
            var dependencies = controller.ServicesDependedOn;
            if (dependencies is null || dependencies.Length == 0)
                return (false, names);

            foreach (var dep in dependencies)
            {
                try { names.Add(dep.ServiceName); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ServiceDependencyChecker] Error getting dependency name: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceDependencyChecker] Error getting dependencies for {serviceName}: {ex.Message}");
        }
        return (names.Count > 0, names);
    }

    public static bool IsCriticalService(string serviceName) 
        => CriticalServices.Contains(serviceName);

    public static bool IsGamingRelatedService(string serviceName) 
        => GamingRelatedServices.Contains(serviceName);
}

public record ServiceCheckResult(bool CanProceed, string Message, bool IsWarning)
{
    public ServiceCheckResult(bool canProceed, string message) : this(canProceed, message, false) { }
}

