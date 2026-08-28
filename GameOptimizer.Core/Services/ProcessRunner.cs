using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GameOptimizer.Core.Services;

public sealed class ProcessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private const int MaxOutputLength = 1024 * 1024; // 1MB 最大输出限制

    public record ProcessResult(int ExitCode, string Output, string Error, bool TimedOut);

    private static readonly HashSet<string> AllowedPowerShellCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-", "Set-ItemProperty", "Remove-ItemProperty", "New-ItemProperty",
        "Get-Service", "Set-Service", "Stop-Service", "Start-Service",
        "Get-Process", "Stop-Process", "Get-ScheduledTask", "Disable-ScheduledTask",
        "Enable-ScheduledTask", "Get-NetAdapter", "Set-NetAdapterAdvancedProperty",
        "Get-WindowsOptionalFeature",
        "Enable-MMAgent", "Disable-MMAgent", "Get-MMAgent", "Confirm-SecureBootUEFI",
        "Enable-WindowsOptionalFeature", "Disable-WindowsOptionalFeature",
        "bcdedit", "powercfg", "netsh", "sfc", "DISM", "reg"
    };

    private static readonly Regex DangerousPatterns = new(
        @"(Invoke-Expression|iex|Invoke-WebRequest|iwr|Start-Process|cmd\s*/c|" +
        @"DownloadString|DownloadFile|Net\.WebClient|Reflection\.Assembly|" +
        @"Remove-Item\s+-Recurse.*-Force|rm\s+-rf|del\s+/s|Format-Volume|" +
        @"Clear-Disk|Initialize-Disk|\$env:)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ValidatePowerShellCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        if (DangerousPatterns.IsMatch(command)) return false;
        var firstWord = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return AllowedPowerShellCommands.Any(allowed => 
            firstWord.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
            command.Contains(allowed, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        Encoding? outputEncoding = null)
    {
        Guard.NotNullOrWhiteSpace(fileName, nameof(fileName));
        var actualTimeout = timeout ?? DefaultTimeout;
        var actualEncoding = outputEncoding ?? Encoding.UTF8;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputTruncated = false;
        var errorTruncated = false;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = actualEncoding,
                StandardErrorEncoding = actualEncoding
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) => 
        { 
            if (e.Data != null && !outputTruncated)
            {
                if (stdout.Length + e.Data.Length > MaxOutputLength)
                {
                    outputTruncated = true;
                    stdout.AppendLine("[输出已截断]");
                }
                else
                {
                    stdout.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) => 
        { 
            if (e.Data != null && !errorTruncated)
            {
                if (stderr.Length + e.Data.Length > MaxOutputLength)
                {
                    errorTruncated = true;
                    stderr.AppendLine("[输出已截断]");
                }
                else
                {
                    stderr.AppendLine(e.Data);
                }
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(actualTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                TryKillProcess(process);
                return new ProcessResult(-1, stdout.ToString(), "操作超时", true);
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, false);
        }
    }

    public static async Task<ProcessResult> RunPowerShellAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        bool bypassWhitelist = false)
    {
        if (!bypassWhitelist && !ValidatePowerShellCommand(command))
        {
            return new ProcessResult(-1, string.Empty, "命令未通过安全白名单验证", false);
        }
        
        var bytes = Encoding.Unicode.GetBytes(command);
        var encodedCommand = Convert.ToBase64String(bytes);
        return await RunAsync(
            "powershell",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ProcessResult> RunCmdAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync("cmd", $"/c {command}", timeout, cancellationToken).ConfigureAwait(false);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessRunner] TryKillProcess error: {ex.Message}");
        }
    }
}
