// File: GameOptimizer.Shell/Services/CommandResultPresentation.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// The Shell-facing interpretation of an already authoritative Core result.
/// This is presentation-only: it never infers or stores system state.
/// </summary>
public enum CommandResultStatus
{
    Succeeded,
    NoChange,
    Failed,
    Cancelled,
    PermissionDenied,
    Unsupported,
    NotApplicable,
    Blocked,
    ReadOnly,
    RequiresRecovery,
    RequiresReboot
}

public sealed record CommandResultPresentation(
    CommandResultStatus Status,
    bool IsSuccess,
    bool RequiresReboot,
    string Message)
{
    public string StatusText => Status.ToString();

    // Raw provider and exception details are retained in Message for diagnostics. UI must not
    // expose them directly because they may contain internal English identifiers or exception text.
    public string DisplayText => GetLocalizedUserMessage(Status, LocalizationService.Instance.CurrentLanguage == "en-US");

    private static string GetLocalizedUserMessage(CommandResultStatus status, bool isEnglish) => status switch
    {
        CommandResultStatus.Succeeded => isEnglish ? "Operation completed and verified." : "操作已完成并验证。",
        CommandResultStatus.NoChange => isEnglish ? "No change was needed for the current system state." : "当前系统状态无需修改。",
        CommandResultStatus.Cancelled => isEnglish ? "Operation was cancelled. No completion result is reported." : "操作已取消，未报告完成结果。",
        CommandResultStatus.PermissionDenied => isEnglish ? "Administrator permission is required to continue." : "需要管理员权限才能继续。",
        CommandResultStatus.Unsupported => isEnglish ? "This feature is not supported on the current system." : "当前系统不支持此功能。",
        CommandResultStatus.NotApplicable => isEnglish ? "This feature does not apply to the current device or configuration." : "此功能不适用于当前设备或配置。",
        CommandResultStatus.Blocked => isEnglish
            ? "Windows blocked this write; it is not caused by missing administrator rights."
            : "Windows 阻止了本次写入；这不是缺少管理员权限造成的。",
        CommandResultStatus.ReadOnly => isEnglish ? "This setting is read-only on the current system." : "当前系统中的此设置为只读。",
        CommandResultStatus.RequiresRecovery => isEnglish ? "The result cannot be verified safely. Use recovery or recheck the system state." : "无法安全验证结果，请先恢复或重新检测系统状态。",
        CommandResultStatus.RequiresReboot => isEnglish ? "Operation completed; restart Windows for the change to take effect." : "操作已完成，重启 Windows 后生效。",
        _ => isEnglish ? "Operation was not completed. Please retry or view diagnostics." : "操作未完成，请重试或查看诊断信息。"
    };
}

/// <summary>
/// A lifetime gate for one Shell command family. It guards command re-entry only;
/// it is not a copy of the Core/system state.
/// </summary>
public sealed class CommandExecutionGate
{
    private int _busy;

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public bool TryEnter() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    public void Exit() => Volatile.Write(ref _busy, 0);
}

public static class CommandResultPresentationMapper
{
    public static CommandResultPresentation FromMissingReceipt(string operationName)
    {
        var name = string.IsNullOrWhiteSpace(operationName) ? "命令" : operationName;
        return new CommandResultPresentation(
            CommandResultStatus.RequiresRecovery,
            IsSuccess: false,
            RequiresReboot: false,
            $"{name}未返回逐项执行回执，未宣称成功；请通过统一恢复中心或模块回读确认当前状态。");
    }

    public static CommandResultPresentation FromOptimization(OperationExecutionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var message = CombineText(summary.Message, summary.Before?.Details, summary.After?.Details);
        var requiresReboot = summary.After?.RequiresReboot ?? summary.Before?.RequiresReboot ?? false;
        var requiresRecovery = summary.After?.RequiresRecovery ?? summary.Before?.RequiresRecovery ?? false;

        if (requiresRecovery)
        {
            return new CommandResultPresentation(
                CommandResultStatus.RequiresRecovery,
                IsSuccess: false,
                requiresReboot,
                message);
        }

        if (summary.Success)
        {
            return new CommandResultPresentation(
                requiresReboot
                    ? CommandResultStatus.RequiresReboot
                    : summary.Changed
                        ? CommandResultStatus.Succeeded
                        : CommandResultStatus.NoChange,
                IsSuccess: true,
                requiresReboot,
                message);
        }

        var status = ClassifyFailure(message, summary.Exception);
        return new CommandResultPresentation(status, IsSuccess: false, requiresReboot, message);
    }

    public static CommandResultPresentation FromRepair(RepairResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Success)
        {
            return new CommandResultPresentation(
                result.RequiresReboot
                    ? CommandResultStatus.RequiresReboot
                    : result.Code == RepairResultCode.NoChange
                        ? CommandResultStatus.NoChange
                        : CommandResultStatus.Succeeded,
                IsSuccess: true,
                result.RequiresReboot,
                result.Message);
        }

        var status = result.Code switch
        {
            RepairResultCode.Cancelled => CommandResultStatus.Cancelled,
            RepairResultCode.PermissionDenied => CommandResultStatus.PermissionDenied,
            RepairResultCode.VersionUnsupported => CommandResultStatus.Unsupported,
            RepairResultCode.NotApplicable => CommandResultStatus.NotApplicable,
            RepairResultCode.PolicyDenied => CommandResultStatus.Blocked,
            RepairResultCode.Blocked => ContainsAny(
                CombineText(result.Message, result.Detail),
                "recovery",
                "restore material",
                "恢复")
                ? CommandResultStatus.RequiresRecovery
                : CommandResultStatus.Blocked,
            RepairResultCode.ReadOnly => CommandResultStatus.ReadOnly,
            _ => ClassifyFailure(CombineText(result.Message, result.Detail), result.Exception)
        };

        return new CommandResultPresentation(status, IsSuccess: false, result.RequiresReboot, result.Message);
    }

    public static CommandResultPresentation FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var status = ClassifyFailure(exception.Message, exception);
        return new CommandResultPresentation(status, IsSuccess: false, RequiresReboot: false, exception.Message);
    }

    public static CommandResultPresentation SummarizeOptimization(
        IEnumerable<OperationExecutionSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var results = summaries.Select(FromOptimization).ToList();
        if (results.Count == 0)
        {
            return new CommandResultPresentation(CommandResultStatus.Failed, false, false, "No operation result was returned.");
        }

        var priority = new[]
        {
            CommandResultStatus.RequiresRecovery,
            CommandResultStatus.PermissionDenied,
            CommandResultStatus.Unsupported,
            CommandResultStatus.Cancelled,
            CommandResultStatus.Blocked,
            CommandResultStatus.NotApplicable,
            CommandResultStatus.ReadOnly,
            CommandResultStatus.Failed,
            CommandResultStatus.RequiresReboot,
            CommandResultStatus.Succeeded,
            CommandResultStatus.NoChange
        };

        var selected = priority
            .Select(status => results.FirstOrDefault(result => result.Status == status))
            .FirstOrDefault(result => result is not null);

        return selected ?? results[0];
    }

    private static CommandResultStatus ClassifyFailure(string? message, Exception? exception)
    {
        var text = CombineText(message, exception?.Message);

        if (exception is OperationCanceledException || ContainsAny(text, "cancel", "canceled", "cancelled", "取消"))
        {
            return CommandResultStatus.Cancelled;
        }

        // Why: 已提权时 UnauthorizedAccess/拒绝访问 不能再归因成「缺管理员」——那是系统拒绝。
        if (exception is UnauthorizedAccessException
            || ContainsAny(text, "permission", "access denied", "administrator", "admin required", "权限", "拒绝访问"))
        {
            return GameOptimizer.Core.Services.WindowsElevation.IsElevated
                ? CommandResultStatus.Blocked
                : CommandResultStatus.PermissionDenied;
        }

        if (exception is NotSupportedException or PlatformNotSupportedException
            || ContainsAny(text, "unsupported", "not supported", "os.unsupported", "version", "不支持", "版本"))
        {
            return CommandResultStatus.Unsupported;
        }

        if (ContainsAny(
            text,
            "rollback failed",
            "回滚失败",
            "requires recovery",
            "manual recovery",
            "需要恢复",
            "还原快照",
            "restore material",
            "restore snapshot",
            "current state is unknown",
            "restore was blocked"))
        {
            return CommandResultStatus.RequiresRecovery;
        }

        if (ContainsAny(text, "not applicable", "target missing", "not-found", "not installed", "不存在", "未安装"))
        {
            return CommandResultStatus.NotApplicable;
        }

        if (ContainsAny(text, "read-only", "只读"))
        {
            return CommandResultStatus.ReadOnly;
        }

        if (ContainsAny(text, "blocked", "fail-closed", "policy", "阻止", "策略"))
        {
            return CommandResultStatus.Blocked;
        }

        return CommandResultStatus.Failed;
    }

    private static bool ContainsAny(string text, params string[] candidates) =>
        candidates.Any(candidate => text.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string CombineText(params string?[] values) =>
        string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public static class LegacyRestoreEntryPresentation
{
    public const string RecoveryCenterRoute = "backup-manager";

    public static CommandResultPresentation Unavailable => new(
        CommandResultStatus.Unsupported,
        IsSuccess: false,
        RequiresReboot: false,
        "旧快照恢复入口已停用，请前往备份管理使用统一恢复流程；现有备份不会被删除。");
}
