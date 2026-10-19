using System.Management;
using GameOptimizer.Core.Services.MaintenanceDiagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Infrastructure.Windows.MaintenanceDiagnostics;

public sealed class WindowsMaintenanceDiagnosticPlatform : IMaintenanceDiagnosticPlatform
{
    public Task<MaintenanceDiagnosticSnapshot> InspectAsync(
        MaintenanceDiagnosticDefinition definition,
        CancellationToken cancellationToken) =>
        Task.Run(() => Inspect(definition, cancellationToken), cancellationToken);

    private static MaintenanceDiagnosticSnapshot Inspect(
        MaintenanceDiagnosticDefinition definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return definition.TargetKind switch
            {
                MaintenanceDiagnosticTargetKind.SystemVolume => InspectSystemVolume(definition, cancellationToken),
                MaintenanceDiagnosticTargetKind.WindowsService => InspectService(definition, cancellationToken),
                _ => MaintenanceDiagnosticSnapshot.Failed(
                    definition.RepairItemId,
                    MaintenanceDiagnosticOutcome.NotApplicable,
                    "不支持的维护诊断目标。")
            };
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                definition.RepairItemId,
                MaintenanceDiagnosticOutcome.PermissionDenied,
                $"WMI 只读查询被拒绝：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                definition.RepairItemId,
                MaintenanceDiagnosticOutcome.PermissionDenied,
                $"只读查询权限不足：{ex.Message}");
        }
        catch (ManagementException ex)
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                definition.RepairItemId,
                MaintenanceDiagnosticOutcome.Unknown,
                $"WMI 只读查询失败 ({ex.ErrorCode})：{ex.Message}");
        }
    }

    private static MaintenanceDiagnosticSnapshot InspectSystemVolume(
        MaintenanceDiagnosticDefinition definition,
        CancellationToken cancellationToken)
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(systemRoot))
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                definition.RepairItemId,
                MaintenanceDiagnosticOutcome.Unknown,
                "无法解析系统卷。") ;
        }

        var driveLetter = systemRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        using var searcher = new ManagementObjectSearcher(
            $"SELECT DeviceID, DriveLetter, DirtyBit, Status FROM Win32_Volume WHERE DriveLetter = '{EscapeWql(driveLetter)}'");
        using var results = searcher.Get();
        var volume = results.Cast<ManagementObject>().FirstOrDefault();
        cancellationToken.ThrowIfCancellationRequested();
        if (volume is null)
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                definition.RepairItemId,
                MaintenanceDiagnosticOutcome.NotApplicable,
                $"未找到系统卷 {driveLetter} 的 Win32_Volume 记录。") ;
        }

        var dirtyValue = volume["DirtyBit"];
        if (dirtyValue is null)
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                definition.RepairItemId,
                MaintenanceDiagnosticOutcome.Unknown,
                "系统卷未返回 DirtyBit，不能推断健康状态。") ;
        }

        var dirty = Convert.ToBoolean(dirtyValue);
        var identity = volume["DeviceID"]?.ToString() ?? driveLetter;
        var status = volume["Status"]?.ToString() ?? "Unknown";
        return new MaintenanceDiagnosticSnapshot(
            definition.RepairItemId,
            dirty ? MaintenanceDiagnosticOutcome.Degraded : MaintenanceDiagnosticOutcome.Healthy,
            !dirty,
            $"volume:{identity}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DriveLetter"] = driveLetter,
                ["DirtyBit"] = dirty.ToString(),
                ["Status"] = status
            },
            dirty
                ? "系统卷 DirtyBit 已设置；仅报告状态，不调度磁盘修复。"
                : "系统卷 DirtyBit 未设置；未执行 chkdsk 或任何写入。") ;
    }

    private static MaintenanceDiagnosticSnapshot InspectService(
        MaintenanceDiagnosticDefinition definition,
        CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT Name, State, StartMode, Status, ProcessId FROM Win32_Service WHERE Name = '{EscapeWql(definition.Target)}'");
        using var results = searcher.Get();
        var service = results.Cast<ManagementObject>().FirstOrDefault();
        cancellationToken.ThrowIfCancellationRequested();
        if (service is null)
        {
            return MaintenanceDiagnosticSnapshot.Failed(
                definition.RepairItemId,
                MaintenanceDiagnosticOutcome.NotApplicable,
                $"服务 {definition.Target} 在当前系统上不存在。") ;
        }

        var state = service["State"]?.ToString() ?? "Unknown";
        var startMode = service["StartMode"]?.ToString() ?? "Unknown";
        var status = service["Status"]?.ToString() ?? "Unknown";
        var processId = service["ProcessId"]?.ToString() ?? "0";
        return new MaintenanceDiagnosticSnapshot(
            definition.RepairItemId,
            MaintenanceDiagnosticOutcome.Observed,
            null,
            $"service:{definition.Target}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["State"] = state,
                ["StartMode"] = startMode,
                ["Status"] = status,
                ["ProcessId"] = processId
            },
            $"已读取服务 {definition.Target}：State={state}, StartMode={startMode}, Status={status}。该观测不能单独证明服务健康或应被重启。") ;
    }

    private static string EscapeWql(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}

public static class MaintenanceDiagnosticServiceCollectionExtensions
{
    public static IServiceCollection AddMaintenanceDiagnosticWindowsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(value => value.ServiceType == typeof(IMaintenanceDiagnosticOwner)))
        {
            throw new InvalidOperationException("IMaintenanceDiagnosticOwner 已注册，拒绝创建第二个维护诊断边界。");
        }

        services.AddSingleton<IMaintenanceDiagnosticPlatform, WindowsMaintenanceDiagnosticPlatform>();
        services.AddSingleton<IMaintenanceDiagnosticOwner, MaintenanceDiagnosticOwner>();
        return services;
    }
}
