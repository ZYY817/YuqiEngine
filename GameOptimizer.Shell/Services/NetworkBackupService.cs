using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Shell.Services;

public static class NetworkBackupService
{
    private static readonly (string FileName, string DisplayName)[] ExpectedFiles =
    {
        ("ipconfig.txt", "IP 配置"),
        ("route_table.txt", "路由表"),
        ("dns_config.txt", "DNS 配置"),
        ("proxy_config.txt", "WinHTTP 代理"),
        ("adapter_config.txt", "网络适配器")
    };
    private static string BackupFolder => AppPaths.NetworkBackupsDir;

    public static string GetBackupFolder() => BackupFolder;

    public static async Task<(bool Success, string BackupPath, string Error)> BackupNetworkConfigAsync()
    {
        try
        {
            // 【P0-002 修复】：使用安全目录操作替代直接创建
            if (!SafeDirectoryOperations.TryCreateDirectory(BackupFolder, out var folderError))
            {
                var friendlyError = UserFriendlyErrorMapper.GetFriendlyMessage(
                    new IOException(folderError ?? "Unknown error"), LocalizationService.Instance.Get("NetBackup.CreateDirFailed", BackupFolder));
                return (false, string.Empty, friendlyError);
            }
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(BackupFolder, $"network_backup_{timestamp}");
            
            if (!SafeDirectoryOperations.TryCreateDirectory(backupPath, out var pathError))
            {
                var friendlyError = UserFriendlyErrorMapper.GetFriendlyMessage(
                    new IOException(pathError ?? "Unknown error"), LocalizationService.Instance.Get("NetBackup.CreateSubDirFailed", backupPath));
                return (false, string.Empty, friendlyError);
            }

            var loc = LocalizationService.Instance;
            var sb = new StringBuilder();
            sb.AppendLine($"{loc.Get("NetBackup.Header")} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            var ipConfig = await RunCommandAsync("ipconfig /all");
            GameOptimizer.Core.Services.SafeFileHelper.WriteAllText(Path.Combine(backupPath, "ipconfig.txt"), ipConfig);
            sb.AppendLine(loc.Get("NetBackup.IpConfigDone"));

            var routeTable = await RunCommandAsync("route print");
            GameOptimizer.Core.Services.SafeFileHelper.WriteAllText(Path.Combine(backupPath, "route_table.txt"), routeTable);
            sb.AppendLine(loc.Get("NetBackup.RouteTableDone"));

            var dnsConfig = await RunCommandAsync("netsh interface ip show dns");
            GameOptimizer.Core.Services.SafeFileHelper.WriteAllText(Path.Combine(backupPath, "dns_config.txt"), dnsConfig);
            sb.AppendLine(loc.Get("NetBackup.DnsConfigDone"));

            var proxyConfig = await RunCommandAsync("netsh winhttp show proxy");
            GameOptimizer.Core.Services.SafeFileHelper.WriteAllText(Path.Combine(backupPath, "proxy_config.txt"), proxyConfig);
            sb.AppendLine(loc.Get("NetBackup.ProxyConfigDone"));

            var adapterConfig = await RunCommandAsync("netsh interface show interface");
            GameOptimizer.Core.Services.SafeFileHelper.WriteAllText(Path.Combine(backupPath, "adapter_config.txt"), adapterConfig);
            sb.AppendLine(loc.Get("NetBackup.AdapterConfigDone"));

            sb.AppendLine();
            sb.AppendLine(loc.Get("NetBackup.BackupLocation", backupPath));
            GameOptimizer.Core.Services.SafeFileHelper.WriteAllText(Path.Combine(backupPath, "backup_summary.txt"), sb.ToString());

            return (true, backupPath, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    public static string[] GetBackupList()
    {
        if (!Directory.Exists(BackupFolder)) return Array.Empty<string>();
        return Directory.GetDirectories(BackupFolder, "network_backup_*");
    }

    public static IReadOnlyList<NetworkBackupSnapshot> GetBackupSnapshots()
    {
        if (!Directory.Exists(BackupFolder))
            return Array.Empty<NetworkBackupSnapshot>();

        return Directory.GetDirectories(BackupFolder, "network_backup_*")
            .Select(CreateSnapshot)
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ToArray();
    }

    private static NetworkBackupSnapshot CreateSnapshot(string backupPath)
    {
        var capturedItems = ExpectedFiles
            .Where(expected => File.Exists(Path.Combine(backupPath, expected.FileName)))
            .Select(expected => expected.DisplayName)
            .ToArray();
        var complete = capturedItems.Length == ExpectedFiles.Length &&
                       File.Exists(Path.Combine(backupPath, "backup_summary.txt"));
        var createdAt = Directory.GetCreationTimeUtc(backupPath);
        return new NetworkBackupSnapshot(
            backupPath,
            new DateTimeOffset(createdAt, TimeSpan.Zero).ToLocalTime(),
            capturedItems,
            complete,
            complete
                ? "已验证完整性；可供受支持的恢复流程读取"
                : "备份不完整；已禁止作为恢复来源");
    }

    public static string GetBackupInfo(string backupPath)
    {
        var summaryFile = Path.Combine(backupPath, "backup_summary.txt");
        if (File.Exists(summaryFile))
            return File.ReadAllText(summaryFile);
        
        var dirName = Path.GetFileName(backupPath);
        if (dirName.StartsWith("network_backup_") && dirName.Length > 15)
        {
            var dateStr = dirName.Substring(15);
            if (DateTime.TryParseExact(dateStr, "yyyyMMdd_HHmmss", null, 
                System.Globalization.DateTimeStyles.None, out var dt))
            {
                return LocalizationService.Instance.Get("NetBackup.BackupTime", dt.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        return LocalizationService.Instance.Get("NetBackup.BackupDir", dirName);
    }

    private static async Task<string> RunCommandAsync(string command)
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // Why: 非中文系统上 GBK 不可用，动态获取系统 OEM 代码页
                    StandardOutputEncoding = Encoding.GetEncoding(
                        System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
                };
                using var proc = Process.Start(psi);
                if (proc == null) return LocalizationService.Instance.Get("NetBackup.CannotRunCommand");
                // Why: 同步 ReadToEnd 在缓冲区满时死锁，必须先启动异步读取
                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(10000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* process may have already exited */ }
                }
                return outputTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return LocalizationService.Instance.Get("NetBackup.ExecutionFailed", ex.Message);
            }
        });
    }
}

public sealed record NetworkBackupSnapshot(
    string Path,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> CapturedItems,
    bool IsComplete,
    string Status);
