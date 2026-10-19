using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.ScheduledTasks;

namespace GameOptimizer.Infrastructure.Windows.Services;

public sealed class WindowsScheduledTaskAccessor : IScheduledTaskAccessor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    // Why: 控制台工具（schtasks/pnputil 等）按系统 OEM 代码页输出字节，
    // 与区域无关的 ASCII 内容各代码页解码一致，本地化错误文本也按 OEM 解才正确。
    private static readonly Encoding ConsoleEncoding = GetConsoleEncoding();

    private static Encoding GetConsoleEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    public async Task<ScheduledTaskPlatformAssessment> ProbeAsync(string taskPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTaskPath(taskPath);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return new ScheduledTaskPlatformAssessment(false, false, false, "os.unsupported", "需要 Windows 10 2004 或更高版本。");

        var exists = await TaskExistsAsync(taskPath, cancellationToken).ConfigureAwait(false);
        var administrator = IsAdministrator();
        return new ScheduledTaskPlatformAssessment(
            exists,
            administrator,
            ManagedByPolicy: false,
            exists
                ? administrator ? "applicable" : "permission.administrator-required"
                : "task.not-found",
            exists
                ? administrator ? "计划任务存在且当前进程已提升。" : "修改计划任务需要管理员权限。"
                : "当前系统不存在该计划任务。");
    }

    public async Task<ScheduledTaskStateSnapshot> ReadAsync(string taskPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTaskPath(taskPath);
        // Why: schtasks 声明 encoding="UTF-16" 但实际按控制台代码页/UTF-8 吐字节；
        // 用 UTF-16 解码会把每两个 ASCII 字节拼成一个汉字，ExtractXml 永远失败。
        // UTF-8 优先（覆盖现代系统的实际输出），找不到 <?xml 再按 UTF-16 重试兼容旧行为。
        var result = await ProcessRunner.RunAsync(
            "schtasks.exe",
            $"/QUERY /TN \"{taskPath}\" /XML",
            CommandTimeout,
            cancellationToken,
            ConsoleEncoding).ConfigureAwait(false);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            return new ScheduledTaskStateSnapshot(taskPath, false, false, "Unknown", string.Empty);

        var xml = ExtractXml(result.Output);
        if (xml is null)
        {
            var utf16Result = await ProcessRunner.RunAsync(
                "schtasks.exe",
                $"/QUERY /TN \"{taskPath}\" /XML",
                CommandTimeout,
                cancellationToken,
                Encoding.Unicode).ConfigureAwait(false);
            if (utf16Result.ExitCode == 0 && !string.IsNullOrWhiteSpace(utf16Result.Output))
            {
                xml = ExtractXml(utf16Result.Output);
                result = utf16Result;
            }
        }
        if (xml is null)
            return new ScheduledTaskStateSnapshot(taskPath, false, false, "Unknown", result.Output);

        var enabled = ParseEnabled(xml) ?? true;
        return new ScheduledTaskStateSnapshot(taskPath, true, enabled, enabled ? "Enabled" : "Disabled", xml);
    }

    public async Task<ScheduledTaskMutationResult> ApplyAsync(string taskPath, bool enabled, CancellationToken cancellationToken)
    {
        ValidateTaskPath(taskPath);
        var before = await ReadAsync(taskPath, cancellationToken).ConfigureAwait(false);
        if (!before.Exists)
            return new ScheduledTaskMutationResult(false, false, "task.not-found", "计划任务不存在。");
        if (before.Enabled == enabled)
            return new ScheduledTaskMutationResult(true, false, "no-change", "计划任务状态已符合目标。");

        var verb = enabled ? "/ENABLE" : "/DISABLE";
        var result = await ProcessRunner.RunAsync(
            "schtasks.exe",
            $"/CHANGE /TN \"{taskPath}\" {verb}",
            CommandTimeout,
            cancellationToken,
            ConsoleEncoding).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return new ScheduledTaskMutationResult(false, false, "schtasks.failed", (result.Error ?? result.Output).Trim());

        var after = await ReadAsync(taskPath, cancellationToken).ConfigureAwait(false);
        return new ScheduledTaskMutationResult(
            after.Exists && after.Enabled == enabled,
            true,
            after.Exists && after.Enabled == enabled ? "ok" : "verification.mismatch",
            "计划任务状态已切换。");
    }

    public async Task<ScheduledTaskMutationResult> RestoreAsync(ScheduledTaskStateSnapshot original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ValidateTaskPath(original.TaskPath);
        if (!original.Exists)
            return new ScheduledTaskMutationResult(true, false, "no-op", "任务原本不存在，恢复无需操作。");

        var before = await ReadAsync(original.TaskPath, cancellationToken).ConfigureAwait(false);
        if (!before.Exists)
            return new ScheduledTaskMutationResult(false, false, "task.not-found", "计划任务已不存在，拒绝盲目重建。");
        if (before.Enabled == original.Enabled)
            return new ScheduledTaskMutationResult(true, false, "no-change", "计划任务状态已等于原始快照。");

        return await ApplyAsync(original.TaskPath, original.Enabled, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TaskExistsAsync(string taskPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            "schtasks.exe",
            $"/QUERY /TN \"{taskPath}\"",
            CommandTimeout,
            cancellationToken,
            ConsoleEncoding).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static string? ExtractXml(string output)
    {
        var start = output.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;
        var end = output.LastIndexOf("</Task>", StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            return null;
        end += "</Task>".Length;
        return output[start..end];
    }

    private static bool? ParseEnabled(string xml)
    {
        try
        {
            var document = new XmlDocument();
            document.LoadXml(xml);
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
            var node = document.SelectSingleNode("//t:Settings/t:Enabled", manager);
            if (node is null)
                return null;
            return node.InnerText.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;

    private static void ValidateTaskPath(string taskPath)
    {
        if (string.IsNullOrWhiteSpace(taskPath))
            throw new ArgumentException("任务路径不能为空。", nameof(taskPath));
        if (!taskPath.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("任务不在固定系统任务白名单中，已阻断。");
    }
}
