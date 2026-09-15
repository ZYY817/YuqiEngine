using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.SystemServices;

namespace GameOptimizer.Infrastructure.Windows.Services;

public static class ServiceCapabilityDefinitions
{
    public static IReadOnlyList<ServiceChangeCapabilityDefinition> Optimization { get; } =
    [
        Disable("service.disable_spooler", "Spooler"),
        Disable("service.disable_wsearch", "WSearch"),
        Disable("service.disable_telemetry", "DiagTrack"),
        Disable("services.disable_fax", "Fax"),
        Disable("services.disable_maps_broker", "MapsBroker"),
        Disable("services.disable_remote_registry", "RemoteRegistry"),
        Disable("services.disable_wallet_service", "WalletService"),
        Disable("service.wer.disable", "WerSvc"),
        Disable("memory.disable_superfetch", "SysMain"),
        Disable("gpu.disable_xbox_game_monitoring", "xbgm"),
        Disable("services.disable_diagtrack", "dmwappushservice"),
        Disable("service.bluetooth.disable", "bthserv"),
        Disable("privacy.disable_upnp_sharing.ssdpsrv", "SSDPSRV"),
        Disable("privacy.disable_upnp_sharing.upnphost", "upnphost"),
        Disable("privacy.disable_sensor_services.sensordataservice", "SensorDataService"),
        Disable("privacy.disable_sensor_services.sensrsvc", "SensrSvc"),
        Disable("services.disable_xbox_services.xboxgipsvc", "XboxGipSvc"),
        Disable("services.disable_xbox_services.xblauthmanager", "XblAuthManager"),
        Disable("services.disable_xbox_services.xblgamesave", "XblGameSave"),
        Disable("services.disable_xbox_services.xboxnetapisvc", "XboxNetApiSvc"),
        Disable("services.disable_xbox_services.gamingservices", "GamingServices"),
        Disable("services.disable_xbox_services.gamingservicesnet", "GamingServicesNet"),
        // 高风险安全项：关闭防火墙/安全中心/Windows Update 服务（标红 + 显式确认，用户自行判断）。
        Disable("basic.disable_firewall", "MpsSvc"),
        Disable("basic.disable_security_center", "SecurityHealthService"),
        Disable("perf.wuauserv.disable", "wuauserv")
    ];

    /// <summary>修复目录中的服务能力（确保服务运行），与 Optimization 共同构成访问器白名单事实源。</summary>
    public static IReadOnlyList<ServiceChangeCapabilityDefinition> Repair { get; } =
    [
        new(
            RepairChangeCapabilityIds.AppInfoEnsureRunning,
            "1.0.0",
            "Appinfo",
            ServiceChangeAction.EnsureRunning,
            WindowsServiceStartType.Manual),
        new(
            RepairChangeCapabilityIds.ThemesEnsureRunning,
            "1.0.0",
            "Themes",
            ServiceChangeAction.EnsureRunning,
            WindowsServiceStartType.Automatic),
        new(
            RepairChangeCapabilityIds.AudioServiceEnsureRunning,
            "1.0.0",
            "Audiosrv",
            ServiceChangeAction.EnsureRunning)
    ];

    /// <summary>全部合法服务变更能力的服务名并集；WindowsServiceAccessor 白名单由此派生，禁止另立第二份清单。</summary>
    public static IEnumerable<string> AllServiceNames =>
        Optimization.Select(definition => definition.ServiceName)
            .Concat(Repair.Select(definition => definition.ServiceName));

    private static ServiceChangeCapabilityDefinition Disable(string capabilityId, string serviceName) =>
        new(
            capabilityId,
            "1.0.0",
            serviceName,
            ServiceChangeAction.EnsureStopped,
            WindowsServiceStartType.Disabled);
}
