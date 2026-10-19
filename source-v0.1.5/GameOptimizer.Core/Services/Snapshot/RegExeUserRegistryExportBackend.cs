using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Services.Snapshot;

/// <summary>
/// Exports only the current user's HKCU hive. It never imports or mutates the registry.
/// The process boundary keeps registry serialization out of the UI and makes timeout
/// and cancellation enforceable.
/// </summary>
public sealed class RegExeUserRegistryExportBackend : IUserRegistryExportBackend
{
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(2);

    public async Task<UserRegistryExportResult> ExportCurrentUserAsync(string destinationPath, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new UserRegistryExportResult(false, 0, "当前平台不是 Windows，无法导出当前用户注册表。");
        if (string.IsNullOrWhiteSpace(destinationPath))
            return new UserRegistryExportResult(false, 0, "注册表导出目标为空。");

        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
            return new UserRegistryExportResult(false, 0, "注册表导出目录无效。");

        Directory.CreateDirectory(directory);
        var regPath = Path.Combine(Environment.SystemDirectory, "reg.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = regPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("export");
        startInfo.ArgumentList.Add("HKCU");
        startInfo.ArgumentList.Add(destinationPath);
        startInfo.ArgumentList.Add("/y");

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                return new UserRegistryExportResult(false, 0, "无法启动注册表导出程序。");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ExportTimeout);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            if (process.ExitCode != 0 || !File.Exists(destinationPath))
                return new UserRegistryExportResult(false, 0, "当前用户注册表导出失败。");

            var fileInfo = new FileInfo(destinationPath);
            if (fileInfo.Length == 0)
                return new UserRegistryExportResult(false, 0, "当前用户注册表导出结果为空。");
            return new UserRegistryExportResult(true, fileInfo.Length);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryTerminate(process);
            return new UserRegistryExportResult(false, 0, "注册表导出已取消。");
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            return new UserRegistryExportResult(false, 0, "注册表导出超时。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            TryTerminate(process);
            return new UserRegistryExportResult(false, 0, "当前用户注册表导出不可用。");
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Export is already fail-closed; never leak process errors to the UI.
        }
    }
}
