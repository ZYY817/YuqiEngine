// File: GameOptimizer.Core/Services/Repair/RepairModels.cs
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.Repair;

public enum RepairCategory
{
    Audio,
    Network,
    Bluetooth,
    Display,
    Peripheral,
    Storage,
    SystemCore,
    UwpApps,
    SystemFramework,
    OptimizationRevert
}

public enum RepairSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Runtime route of a static Repair item. A managed route only means that the
/// legacy delegate is no longer reachable; it does not make a fail-closed item
/// executable.
/// </summary>
public enum RepairExecutionAvailability
{
    WorkingBidirectional,
    ReadOnly,
    FailClosed,
    SafetyBlocked
}

public enum RepairResultCode
{
    Succeeded,
    NoChange,
    ReadOnly,
    Blocked,
    PermissionDenied,
    VersionUnsupported,
    NotApplicable,
    PolicyDenied,
    Cancelled,
    RecoveryRequired,
    Failed
}

public record RepairItem
{
    private static LocalizationService Loc => LocalizationService.Instance;
    
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? NameKey { get; init; }
    public string? DescriptionKey { get; init; }
    public string Icon { get; init; } = "🔧";
    public RepairCategory Category { get; init; }
    public RepairSeverity Severity { get; init; } = RepairSeverity.Info;
    public bool RequiresAdmin { get; init; } = true;
    public bool IsRisky { get; init; }
    public RepairExecutionIntent ExecutionIntent { get; init; } = RepairExecutionIntent.Apply;
    public string? RelatedOptimization { get; init; }
    public RepairExecutionAvailability Availability => RepairExecutionAvailabilityCatalog.Get(Id);
    public bool CanMutate => Availability == RepairExecutionAvailability.WorkingBidirectional;
    public required Func<Task<bool>> CheckHealthAsync { get; init; }
    public required Func<IProgress<RepairProgress>?, Task<RepairResult>> RepairAsync { get; init; }
    
    public string LocalizedName => NameKey != null ? Loc.Get(NameKey) : Name;
    public string LocalizedDescription => DescriptionKey != null ? Loc.Get(DescriptionKey) : Description;
}

public record RepairProgress
{
    public string Message { get; init; } = string.Empty;
    public int PercentComplete { get; init; }
    public string? CurrentStep { get; init; }
}

public record RepairResult
{
    private static LocalizationService Loc => LocalizationService.Instance;
    
    public bool Success { get; init; }
    public RepairResultCode Code { get; init; } = RepairResultCode.Failed;
    public RepairResultCode Status => Code;
    public RepairExecutionAvailability Availability { get; init; } = RepairExecutionAvailability.FailClosed;
    public string? ReasonCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public Exception? Exception { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public TimeSpan Duration { get; init; }
    public bool RequiresReboot { get; init; }
    public RepairChangeReceipt? ChangeReceipt { get; init; }

    public static RepairResult Succeeded(
        string message,
        bool requiresReboot = false,
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        new()
        {
            Success = true,
            Code = RepairResultCode.Succeeded,
            Availability = availability,
            ReasonCode = "succeeded",
            Message = message,
            RequiresReboot = requiresReboot
        };

    public static RepairResult NoChange(
        string message = "当前状态已经符合目标。",
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        new()
        {
            Success = true,
            Code = RepairResultCode.NoChange,
            Availability = availability,
            ReasonCode = "change.no-op",
            Message = message
        };

    public static RepairResult ReadOnly(
        string message,
        string? detail = null,
        string reasonCode = "repair.read-only") =>
        new()
        {
            Success = false,
            Code = RepairResultCode.ReadOnly,
            Availability = RepairExecutionAvailability.ReadOnly,
            ReasonCode = reasonCode,
            Message = message,
            Detail = detail
        };

    public static RepairResult Blocked(
        string message,
        string? reasonCode = "repair.blocked",
        string? detail = null,
        RepairExecutionAvailability availability = RepairExecutionAvailability.FailClosed) =>
        new()
        {
            Success = false,
            Code = RepairResultCode.Blocked,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            Detail = detail
        };

    public static RepairResult PermissionDenied(
        string message,
        string? reasonCode = "permission.denied",
        string? detail = null,
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        new()
        {
            Success = false,
            Code = RepairResultCode.PermissionDenied,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            Detail = detail
        };

    public static RepairResult VersionUnsupported(
        string message,
        string? reasonCode = "os.unsupported",
        string? detail = null,
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        new()
        {
            Success = false,
            Code = RepairResultCode.VersionUnsupported,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            Detail = detail
        };

    public static RepairResult NotApplicable(
        string message,
        string? reasonCode = "repair.not-applicable",
        string? detail = null,
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        new()
        {
            Success = false,
            Code = RepairResultCode.NotApplicable,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            Detail = detail
        };

    public static RepairResult PolicyDenied(
        string message,
        string? reasonCode = "policy.denied",
        string? detail = null,
        RepairExecutionAvailability availability = RepairExecutionAvailability.FailClosed) =>
        new()
        {
            Success = false,
            Code = RepairResultCode.PolicyDenied,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            Detail = detail
        };

    public static RepairResult Cancelled(
        string message,
        string? reasonCode = "cancelled",
        string? detail = null,
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        new()
        {
            Success = false,
            Code = RepairResultCode.Cancelled,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            Detail = detail
        };

    public static RepairResult RecoveryRequired(
        string message,
        string? reasonCode = "restore.recovery-required",
        string? detail = null,
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        new()
        {
            Success = false,
            Code = RepairResultCode.RecoveryRequired,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            Detail = detail
        };
    
    public static RepairResult SucceededDefault(bool requiresReboot = false) =>
        Succeeded(Loc.Get("Repair.SuccessDefault"), requiresReboot);
    
    public static RepairResult Failed(
        string message,
        Exception? ex = null,
        string? detail = null,
        string? reasonCode = "repair.failed",
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        new()
        {
            Success = false,
            Code = RepairResultCode.Failed,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            Exception = ex,
            Detail = detail ?? ex?.Message
        };

    public static RepairResult FromReasonCode(
        string message,
        string? reasonCode,
        string? detail = null,
        RepairExecutionAvailability availability = RepairExecutionAvailability.FailClosed)
    {
        var code = ClassifyReasonCode(reasonCode, message, detail);
        return code switch
        {
            RepairResultCode.PermissionDenied => PermissionDenied(message, reasonCode, detail, availability),
            RepairResultCode.VersionUnsupported => VersionUnsupported(message, reasonCode, detail, availability),
            RepairResultCode.NotApplicable => NotApplicable(message, reasonCode, detail, availability),
            RepairResultCode.PolicyDenied => PolicyDenied(message, reasonCode, detail, availability),
            RepairResultCode.Cancelled => Cancelled(message, reasonCode, detail, availability),
            RepairResultCode.RecoveryRequired => RecoveryRequired(message, reasonCode, detail, availability),
            RepairResultCode.ReadOnly => ReadOnly(message, detail, reasonCode ?? "repair.read-only"),
            RepairResultCode.NoChange => NoChange(message, availability),
            _ => Blocked(message, reasonCode, detail, availability)
        };
    }

    public static RepairResult FromException(
        string message,
        Exception exception,
        RepairExecutionAvailability availability = RepairExecutionAvailability.WorkingBidirectional) =>
        exception switch
        {
            OperationCanceledException => Cancelled(message, "cancelled", exception.Message, availability),
            UnauthorizedAccessException => PermissionDenied(message, "permission.denied", exception.Message, availability),
            PlatformNotSupportedException or NotSupportedException =>
                VersionUnsupported(message, "os.unsupported", exception.Message, availability),
            FileNotFoundException or DirectoryNotFoundException =>
                NotApplicable(message, "target.missing", exception.Message, availability),
            _ => Failed(message, exception, availability: availability)
        };

    private static RepairResultCode ClassifyReasonCode(
        string? reasonCode,
        string message,
        string? detail)
    {
        var text = string.Join(" ", reasonCode, message, detail).ToLowerInvariant();
        if (text.Contains("cancel", StringComparison.Ordinal)
            || text.Contains("取消", StringComparison.Ordinal))
            return RepairResultCode.Cancelled;
        if (text.Contains("permission", StringComparison.Ordinal)
            || text.Contains("access.denied", StringComparison.Ordinal)
            || text.Contains("administrator", StringComparison.Ordinal)
            || text.Contains("权限", StringComparison.Ordinal)
            || text.Contains("拒绝访问", StringComparison.Ordinal))
            return RepairResultCode.PermissionDenied;
        if (text.Contains("unsupported", StringComparison.Ordinal)
            || text.Contains("not.supported", StringComparison.Ordinal)
            || text.Contains("os.", StringComparison.Ordinal)
            || text.Contains("version", StringComparison.Ordinal)
            || text.Contains("版本", StringComparison.Ordinal))
            return RepairResultCode.VersionUnsupported;
        if (text.Contains("not.applicable", StringComparison.Ordinal)
            || text.Contains("scope.invalid", StringComparison.Ordinal)
            || text.Contains("target.missing", StringComparison.Ordinal)
            || text.Contains("not-found", StringComparison.Ordinal)
            || text.Contains("not installed", StringComparison.Ordinal)
            || text.Contains("不存在", StringComparison.Ordinal)
            || text.Contains("未安装", StringComparison.Ordinal))
            return RepairResultCode.NotApplicable;
        if (text.Contains("policy", StringComparison.Ordinal)
            || text.Contains("baseline", StringComparison.Ordinal)
            || text.Contains("策略", StringComparison.Ordinal)
            || text.Contains("基线", StringComparison.Ordinal))
            return RepairResultCode.PolicyDenied;
        if (text.Contains("recovery", StringComparison.Ordinal)
            || text.Contains("restore.", StringComparison.Ordinal)
            || text.Contains("恢复材料", StringComparison.Ordinal))
            return RepairResultCode.RecoveryRequired;
        if (text.Contains("read-only", StringComparison.Ordinal)
            || text.Contains("只读", StringComparison.Ordinal))
            return RepairResultCode.ReadOnly;
        if (text.Contains("no-op", StringComparison.Ordinal)
            || text.Contains("no change", StringComparison.Ordinal)
            || text.Contains("无变化", StringComparison.Ordinal))
            return RepairResultCode.NoChange;
        return RepairResultCode.Failed;
    }
}

public record ScanProgress
{
    public string CurrentItem { get; init; } = string.Empty;
    public int ScannedCount { get; init; }
    public int TotalCount { get; init; }
    public int IssuesFound { get; init; }
}

public record ScanResult
{
    public IReadOnlyList<RepairItem> Issues { get; init; } = [];
    public int TotalScanned { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public record RepairLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public required string RepairItemId { get; init; }
    public required string RepairItemName { get; init; }
    public RepairCategory Category { get; init; }
    public bool Success { get; init; }
    public RepairResultCode Code { get; init; } = RepairResultCode.Failed;
    public RepairExecutionAvailability Availability { get; init; } = RepairExecutionAvailability.FailClosed;
    public string? ReasonCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorDetail { get; init; }
    public TimeSpan Duration { get; init; }
}
