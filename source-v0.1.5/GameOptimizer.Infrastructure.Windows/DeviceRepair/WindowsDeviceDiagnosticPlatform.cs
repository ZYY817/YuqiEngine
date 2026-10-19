using System.Management;
using System.ServiceProcess;
using GameOptimizer.Core.Services.DeviceRepair;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Infrastructure.Windows.DeviceRepair;

public sealed class WindowsDeviceDiagnosticPlatform : IDeviceDiagnosticPlatform
{
    public Task<DeviceDiagnosticSnapshot> InspectAsync(
        DeviceRepairDefinition definition,
        CancellationToken cancellationToken) =>
        definition.DiagnosticKind switch
        {
            DeviceDiagnosticTargetKind.WindowsService => InspectServiceAsync(definition, cancellationToken),
            DeviceDiagnosticTargetKind.PnpClass => InspectPnpAsync(definition, byClass: true, cancellationToken),
            DeviceDiagnosticTargetKind.PnpName => InspectPnpAsync(definition, byClass: false, cancellationToken),
            _ => Task.FromResult(DeviceDiagnosticSnapshot.Failed(definition.RepairItemId, "不支持的诊断目标类型。"))
        };

    private static Task<DeviceDiagnosticSnapshot> InspectServiceAsync(
        DeviceRepairDefinition definition,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var controller = new ServiceController(definition.DiagnosticTarget);
            var status = controller.Status;
            return new DeviceDiagnosticSnapshot(
                definition.RepairItemId,
                true,
                status == ServiceControllerStatus.Running,
                [$"service:{controller.ServiceName}"],
                null,
                $"服务 {controller.ServiceName} 状态为 {status}。");
        }
        catch (InvalidOperationException ex)
        {
            return DeviceDiagnosticSnapshot.Failed(definition.RepairItemId, $"无法读取服务状态：{ex.Message}");
        }
    }, cancellationToken);

    private static Task<DeviceDiagnosticSnapshot> InspectPnpAsync(
        DeviceRepairDefinition definition,
        bool byClass,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var property = byClass ? "PNPClass" : "Name";
        var escaped = EscapeWql(definition.DiagnosticTarget);
        var predicate = byClass ? $"{property} = '{escaped}'" : $"{property} LIKE '%{escaped}%'";
        var identities = new List<string>();
        var healthy = 0;
        using var searcher = new ManagementObjectSearcher(
            $"SELECT DeviceID, Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE {predicate}");
        foreach (ManagementObject item in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = item["DeviceID"]?.ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            identities.Add(id);
            if (Convert.ToInt32(item["ConfigManagerErrorCode"] ?? 1) == 0)
            {
                healthy++;
            }
        }

        return new DeviceDiagnosticSnapshot(
            definition.RepairItemId,
            identities.Count > 0,
            identities.Count > 0 && healthy == identities.Count,
            identities,
            null,
            identities.Count == 0
                ? $"未找到匹配 {definition.DiagnosticTarget} 的设备。"
                : $"找到 {identities.Count} 个设备，其中 {healthy} 个状态正常。");
    }, cancellationToken);

    private static string EscapeWql(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}

public static class DeviceRepairServiceCollectionExtensions
{
    public static IServiceCollection AddDeviceRepairWindowsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(value => value.ServiceType == typeof(IDeviceRepairOwner)))
        {
            throw new InvalidOperationException("IDeviceRepairOwner 已注册，拒绝创建第二个设备修复写入边界。");
        }

        services.AddSingleton<IDeviceDiagnosticPlatform, WindowsDeviceDiagnosticPlatform>();
        services.AddSingleton<IDeviceRepairOwner, DeviceRepairOwner>();
        return services;
    }
}
