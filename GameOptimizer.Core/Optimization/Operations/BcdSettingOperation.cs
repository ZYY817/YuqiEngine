// File: GameOptimizer.Core/Optimization/Operations/BcdSettingOperation.cs
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class BcdSettingOperation : IOptimizationOperation
{
    private record BcdSnapshot(string? PreviousValue);

    public BcdSettingOperation(
        string id,
        string title,
        string description,
        string category,
        string elementName,
        string? desiredValue,
        string? desiredDisplay = null,
        string? queryIdentifier = "{current}",
        string? modifyIdentifier = "{current}")
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        ElementName = elementName;
        DesiredValue = desiredValue;
        DesiredDisplay = desiredDisplay ?? (desiredValue ?? "Not set");
        QueryIdentifier = queryIdentifier;
        ModifyIdentifier = modifyIdentifier;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public string ElementName { get; }
    public string? DesiredValue { get; }
    public string DesiredDisplay { get; }
    public string? QueryIdentifier { get; }
    public string? ModifyIdentifier { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;
    public bool RequiresExplicitConfirmation => true;
    public bool RequiresRestorePoint => true;
    public string? ImpactWarning => "⚠️ BCD修改可能导致系统无法启动！请务必先创建系统还原点。";

    public OperationType OperationType => OperationType.Command;
    public string? ManualPath => "cmd.exe";
    public string? TargetName => $"BCD 启动配置: {ElementName}";
    public string? TargetValue => DesiredDisplay;
    public string? ManualSteps =>
        $"⚠️ BCD modifications may prevent system boot. Create a restore point first.\n\n" +
        "1. Press Win+X and open Terminal (Admin)\n" +
        $"2. View current setting: bcdedit {BuildEnumArguments()}\n" +
        $"3. Set new value: bcdedit {BuildMutationPreview(DesiredValue)}\n" +
        $"4. Restore later using the saved snapshot or: bcdedit {BuildMutationPreview(null)}\n" +
        "5. Restart the computer to verify the result";

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var currentState = await ReadCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        if (!currentState.Success)
        {
            return new OperationStatus(false, currentState.Message);
        }

        var compliant = AreValuesEquivalent(currentState.Value, DesiredValue);
        var details = currentState.Value is null ? "当前未设置" : $"当前值：{currentState.Value}";
        if (!compliant)
        {
            details += " (BCD修改需要重启系统才能生效)";
        }

        return new OperationStatus(compliant, details);
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        if (await IsSecureBootEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return new OperationInvokeResult(false, false, "安全启动(Secure Boot)已启用，无法修改BCD。请在BIOS中禁用安全启动后重试");
        }

        var currentState = await ReadCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        if (!currentState.Success)
        {
            return new OperationInvokeResult(false, false, currentState.Message);
        }

        if (AreValuesEquivalent(currentState.Value, DesiredValue))
        {
            return new OperationInvokeResult(true, false, DesiredValue is null ? "值已移除" : "值已符合目标");
        }

        var snapshot = new BcdSnapshot(currentState.Value);
        await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

        var mutationResult = await ApplyValueAsync(DesiredValue, cancellationToken).ConfigureAwait(false);
        if (!mutationResult.Success)
        {
            return await BuildApplyFailureResultAsync(context, snapshot, mutationResult.Message, cancellationToken).ConfigureAwait(false);
        }

        var verifyResult = await VerifyValueAsync(DesiredValue, cancellationToken).ConfigureAwait(false);
        if (!verifyResult.Success)
        {
            return await BuildApplyFailureResultAsync(context, snapshot, verifyResult.Message, cancellationToken).ConfigureAwait(false);
        }

        return new OperationInvokeResult(
            true,
            true,
            DesiredValue is null
                ? "⚠️ 已移除BCD值（建议重启前创建还原点）"
                : $"⚠️ 已设置为 {DesiredDisplay}（BCD修改需重启生效，建议先创建还原点）");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        if (await IsSecureBootEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return new OperationInvokeResult(false, false, "安全启动(Secure Boot)已启用，无法修改BCD。请在BIOS中禁用安全启动后重试");
        }

        var snapshot = await context.LoadSnapshotAsync<BcdSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new OperationInvokeResult(false, false, "没有可恢复的快照");
        }

        var currentState = await ReadCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        if (!currentState.Success)
        {
            return new OperationInvokeResult(false, false, currentState.Message);
        }

        if (AreValuesEquivalent(currentState.Value, snapshot.PreviousValue))
        {
            return new OperationInvokeResult(true, false, "已是原值");
        }

        var restoreResult = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!restoreResult.Success)
        {
            return new OperationInvokeResult(false, false, restoreResult.Message);
        }

        return new OperationInvokeResult(
            true,
            true,
            snapshot.PreviousValue is null
                ? "已移除该值"
                : $"已恢复为 {snapshot.PreviousValue}");
    }

    private async Task<OperationInvokeResult> BuildApplyFailureResultAsync(
        OperationExecutionContext context,
        BcdSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollbackResult = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        var message = rollbackResult.Success
            ? failureMessage + "\nOriginal BCD value remained intact or was restored."
            : failureMessage + "\nAutomatic rollback failed: " + rollbackResult.Message;
        return new OperationInvokeResult(false, false, message);
    }

    private async Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        BcdSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var originalState = await VerifyValueAsync(snapshot.PreviousValue, cancellationToken).ConfigureAwait(false);
        if (originalState.Success)
        {
            await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
            return (true, "Original BCD value remained intact.");
        }

        var rollbackResult = await RestoreSnapshotInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!rollbackResult.Success)
        {
            return rollbackResult;
        }

        await TryDeleteSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
        return (true, "Original BCD value restored.");
    }

    private async Task<(bool Success, string Message)> RestoreSnapshotInternalAsync(BcdSnapshot snapshot, CancellationToken cancellationToken)
    {
        var mutationResult = await ApplyValueAsync(snapshot.PreviousValue, cancellationToken).ConfigureAwait(false);
        if (!mutationResult.Success)
        {
            return mutationResult;
        }

        return await VerifyValueAsync(snapshot.PreviousValue, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Success, string? Value, string Message)> ReadCurrentStateAsync(CancellationToken cancellationToken)
    {
        var result = await RunBcdEditAsync(BuildEnumArguments(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return (false, null, BuildCommandFailureMessage("读取 BCD 失败: ", result.StdErr, result.StdOut));
        }

        return (true, ParseCurrentValue(result.StdOut), string.Empty);
    }

    private async Task<(bool Success, string Message)> ApplyValueAsync(string? value, CancellationToken cancellationToken)
    {
        var arguments = value is null ? BuildDeleteArguments() : BuildSetArguments(value);
        var result = await RunBcdEditAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return (false, BuildCommandFailureMessage(value is null ? "删除失败: " : "写入失败: ", result.StdErr, result.StdOut));
        }

        return (true, string.Empty);
    }

    private async Task<(bool Success, string Message)> VerifyValueAsync(string? expectedValue, CancellationToken cancellationToken)
    {
        var currentState = await ReadCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        if (!currentState.Success)
        {
            return (false, currentState.Message);
        }

        if (AreValuesEquivalent(currentState.Value, expectedValue))
        {
            return (true, string.Empty);
        }

        var actualDisplay = currentState.Value ?? "<未设置>";
        var expectedDisplay = expectedValue ?? "<未设置>";
        return (false, $"BCD 值验证失败：实际 {actualDisplay}，期望 {expectedDisplay}，已回滚或需手动处理");
    }

    private string? ParseCurrentValue(string output)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith(ElementName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Length == ElementName.Length)
            {
                return string.Empty;
            }

            var value = line[(ElementName.Length)..].Trim();
            return value.Length == 0 ? string.Empty : value;
        }

        return null;
    }

    private bool AreValuesEquivalent(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildEnumArguments()
    {
        return QueryIdentifier is null ? "/ENUM" : $"/ENUM {QueryIdentifier}";
    }

    private string BuildSetArguments(string value)
    {
        return ModifyIdentifier is null
            ? $"/SET {ElementName} {value}"
            : $"/SET {ModifyIdentifier} {ElementName} {value}";
    }

    private string BuildDeleteArguments()
    {
        return ModifyIdentifier is null
            ? $"/DELETEVALUE {ElementName}"
            : $"/DELETEVALUE {ModifyIdentifier} {ElementName}";
    }

    private string BuildMutationPreview(string? value)
    {
        return value is null ? BuildDeleteArguments() : BuildSetArguments(value);
    }

    private static string BuildCommandFailureMessage(string prefix, string stderr, string stdout)
    {
        var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        details = string.IsNullOrWhiteSpace(details) ? "bcdedit 无输出" : details.Trim();
        return prefix + details;
    }

    private Task TryDeleteSnapshotAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        return context.TryDeleteSnapshotAsync(Id, cancellationToken);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunBcdEditAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bcdedit",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Unicode,
                StandardErrorEncoding = Encoding.Unicode
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return (-1, string.Empty, "无法启动 bcdedit.exe");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProcessTimeouts.StandardCommand);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return (-1, stdout.ToString(), "bcdedit 命令执行超时");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task<bool> IsSecureBootEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunPowerShellAsync("Confirm-SecureBootUEFI", cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 && result.StdOut.Contains("True", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunPowerShellAsync(
        string command,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Unicode,
                StandardErrorEncoding = Encoding.Unicode
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return (-1, string.Empty, "无法启动 powershell.exe");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProcessTimeouts.StandardCommand);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return (-1, stdout.ToString(), "PowerShell 命令执行超时");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
