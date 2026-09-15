using System.Text;
using System.Text.RegularExpressions;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Bcd;

namespace GameOptimizer.Infrastructure.Windows.Services;

public sealed class WindowsBcdEditAccessor : IBcdEditAccessor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SecureBootProbeTimeout = TimeSpan.FromSeconds(10);

    public async Task<BcdPlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return new BcdPlatformAssessment(false, false, false, "os.unsupported", "需要 Windows 10 2004 或更高版本。");

        var administrator = IsAdministrator();
        var secureBoot = await IsSecureBootEnabledAsync(cancellationToken).ConfigureAwait(false);
        if (secureBoot is null)
            return new BcdPlatformAssessment(
                false,
                false,
                true,
                "secureboot.state-unknown",
                "无法确认安全启动状态（可能为旧版 BIOS 或固件拒绝查询），已阻断 BCD 写入。");
        if (secureBoot.Value)
            return new BcdPlatformAssessment(
                administrator,
                true,
                true,
                "secureboot.enabled",
                "安全启动(Secure Boot)已启用，修改 BCD 会导致系统无法启动。请在 BIOS 中禁用安全启动后重试。");

        return new BcdPlatformAssessment(
            administrator,
            false,
            true,
            administrator ? "applicable" : "permission.administrator-required",
            administrator
                ? "当前进程已提升且安全启动未启用，可读写 BCD 启动配置。"
                : "修改 BCD 启动配置需要管理员权限。");
    }

    public async Task<BcdStateSnapshot> ReadAsync(string elementName, CancellationToken cancellationToken)
    {
        ValidateElementName(elementName);
        var result = await ProcessRunner.RunAsync(
            "bcdedit",
            "/ENUM {current}",
            CommandTimeout,
            cancellationToken,
            Encoding.Unicode).ConfigureAwait(false);

        if (result.ExitCode != 0)
            return new BcdStateSnapshot(ReadSucceeded: false, null);

        return new BcdStateSnapshot(ReadSucceeded: true, ParseElementValue(result.Output, elementName));
    }

    public async Task<BcdMutationResult> ApplyAsync(string elementName, string? targetValue, CancellationToken cancellationToken)
    {
        ValidateElementName(elementName);
        var arguments = targetValue is null
            ? $"/DELETEVALUE {{current}} {elementName}"
            : $"/SET {{current}} {elementName} {targetValue}";
        var result = await ProcessRunner.RunAsync(
            "bcdedit",
            arguments,
            CommandTimeout,
            cancellationToken,
            Encoding.Unicode).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return new BcdMutationResult(
                false,
                false,
                "bcdedit.failed",
                (result.Error ?? result.Output).Trim());

        return new BcdMutationResult(true, true, "ok", "BCD 元素已写入。");
    }

    public async Task<BcdMutationResult> RestoreAsync(string elementName, BcdStateSnapshot original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ValidateElementName(elementName);
        if (!original.ReadSucceeded)
            return new BcdMutationResult(false, false, "restore.material-invalid", "原始 BCD 快照不可读，拒绝盲目恢复。");

        var current = await ReadAsync(elementName, cancellationToken).ConfigureAwait(false);
        if (!current.ReadSucceeded)
            return new BcdMutationResult(false, false, "bcdedit.failed", "恢复前读取 BCD 状态失败，未执行写入。");
        if (AreValuesEquivalent(current.ElementValue, original.ElementValue))
            return new BcdMutationResult(true, false, "no-change", "BCD 状态已等于原始快照。");

        return await ApplyAsync(elementName, original.ElementValue, cancellationToken).ConfigureAwait(false);
    }

    private static string? ParseElementValue(string output, string elementName)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith(elementName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.Length == elementName.Length)
                return string.Empty;

            var value = line[elementName.Length..].Trim();
            return value.Length == 0 ? string.Empty : value;
        }

        return null;
    }

    private static bool AreValuesEquivalent(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private async Task<bool?> IsSecureBootEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunPowerShellAsync(
                "Confirm-SecureBootUEFI",
                SecureBootProbeTimeout,
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return null;
            var output = result.Output.Trim();
            if (output.EndsWith("True", StringComparison.OrdinalIgnoreCase)) return true;
            if (output.EndsWith("False", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;

    private static void ValidateElementName(string elementName)
    {
        if (string.IsNullOrWhiteSpace(elementName))
            throw new ArgumentException("BCD 元素名不能为空。", nameof(elementName));
        if (!Regex.IsMatch(elementName, @"^[A-Za-z]+$"))
            throw new InvalidOperationException("BCD 元素名不在允许的字母白名单内，已阻断。");
    }
}
