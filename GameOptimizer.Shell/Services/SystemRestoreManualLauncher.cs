using System.Diagnostics;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services;

public sealed record SystemRestoreManualLaunchResult(bool Success, string Message);

public interface ISystemRestoreManualLauncher
{
    SystemRestoreManualLaunchResult OpenProtectionSettings();
    SystemRestoreManualLaunchResult OpenRecoveryWizard();
}

public sealed class WindowsSystemRestoreManualLauncher : ISystemRestoreManualLauncher
{
    public SystemRestoreManualLaunchResult OpenProtectionSettings() => Launch(
        "SystemPropertiesProtection.exe",
        "已打开 Windows“系统保护”。本应用不会自动更改保护配置。",
        "无法打开 Windows“系统保护”");

    public SystemRestoreManualLaunchResult OpenRecoveryWizard() => Launch(
        "rstrui.exe",
        "已打开 Windows 系统还原向导。后续选择与确认完全由 Windows 处理。",
        "无法打开 Windows 系统还原向导");

    private static SystemRestoreManualLaunchResult Launch(
        string fileName,
        string successMessage,
        string failurePrefix)
    {
        var (process, error) = SafeProcessHelper.StartProcess(new ProcessStartInfo(fileName)
        {
            UseShellExecute = true
        });
        process?.Dispose();
        return process is not null
            ? new(true, successMessage)
            : new(false, $"{failurePrefix}：{error ?? "系统未返回可用进程。"}");
    }
}
