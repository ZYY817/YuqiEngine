using System.Management;
using System.Security.Principal;
using System.Text;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.DeviceRepair;

namespace GameOptimizer.Infrastructure.Windows.Services;

public sealed class WindowsDeviceStateAccessor : IDeviceStateAccessor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(8);

    public Task<DevicePlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return Task.FromResult(new DevicePlatformAssessment(false, "os.unsupported", "需要 Windows 10 2004 或更高版本。"));

        var administrator = IsAdministrator();
        return Task.FromResult(new DevicePlatformAssessment(
            administrator,
            administrator ? "applicable" : "permission.administrator-required",
            administrator
                ? "当前进程已提升，可通过 pnputil 切换设备状态。"
                : "禁用/启用设备需要管理员权限。"));
    }

    public async Task<DeviceStateSnapshot> ReadAsync(string deviceNamePattern, CancellationToken cancellationToken)
    {
        ValidatePattern(deviceNamePattern);
        var found = await Task.Run(() => QueryDevice(deviceNamePattern), cancellationToken).ConfigureAwait(false);
        return found ?? new DeviceStateSnapshot(Found: false, string.Empty, string.Empty, true);
    }

    public async Task<DeviceMutationResult> ApplyAsync(string deviceId, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (deviceId.Contains('"') || deviceId.Contains('\''))
            return new DeviceMutationResult(false, false, "device.id-invalid", "设备实例 ID 含非法字符，已阻断写入。");

        var verb = enabled ? "/enable-device" : "/disable-device";
        var result = await ProcessRunner.RunAsync(
            "pnputil",
            $"{verb} \"{deviceId}\"",
            CommandTimeout,
            cancellationToken,
            Encoding.Unicode).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var error = (result.Error ?? result.Output).Trim();
            var reason = error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                || error.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase)
                    ? "permission.denied" : "pnputil.failed";
            return new DeviceMutationResult(false, false, reason, error.Length == 0 ? "pnputil 无输出" : error);
        }

        return new DeviceMutationResult(true, true, "ok", "设备状态已写入。");
    }

    public async Task<DeviceMutationResult> RestoreAsync(DeviceStateSnapshot original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!original.Found || string.IsNullOrWhiteSpace(original.DeviceId))
            return new DeviceMutationResult(false, false, "restore.material-invalid", "原始设备快照缺失身份，拒绝盲目恢复。");

        var current = await ReadAsync(original.Name, cancellationToken).ConfigureAwait(false);
        if (!current.Found)
            return new DeviceMutationResult(false, false, "device.not-found", "目标设备已不存在，拒绝操作。");
        if (current.IsEnabled == original.IsEnabled && current.DeviceId == original.DeviceId)
            return new DeviceMutationResult(true, false, "no-change", "设备状态已等于原始快照。");

        return await ApplyAsync(original.DeviceId, original.IsEnabled, cancellationToken).ConfigureAwait(false);
    }

    private static DeviceStateSnapshot? QueryDevice(string deviceNamePattern)
    {
        try
        {
            var escaped = deviceNamePattern.Replace(@"\", @"\\").Replace("%", "[%]").Replace("_", "[_]").Replace("'", "''");
            using var searcher = new ManagementObjectSearcher(
                $"SELECT DeviceID, Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Name LIKE '%{escaped}%'")
            {
                Options = new System.Management.EnumerationOptions { Timeout = QueryTimeout }
            };
            foreach (var item in searcher.Get())
            {
                var deviceId = item["DeviceID"]?.ToString();
                var name = item["Name"]?.ToString();
                var errorCode = Convert.ToInt32(item["ConfigManagerErrorCode"] ?? 0);
                if (!string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(name))
                    return new DeviceStateSnapshot(true, deviceId, name, IsEnabled: errorCode != 22);
            }
        }
        catch (ManagementException)
        {
            // 查询失败按未找到处理，由上层 fail-closed。
        }

        return null;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void ValidatePattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (pattern.Contains('\''))
            throw new InvalidOperationException("设备匹配模式不在允许的字符范围内，已阻断查询。");
    }
}
