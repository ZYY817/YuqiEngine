namespace GameOptimizer.Core.Services;

public enum OperationResultCode
{
    Success,
    PartialSuccess,
    Failed,
    Cancelled,
    TimedOut,
    PermissionDenied,
    NotSupported,
    AlreadyApplied
}

public record OperationResult
{
    public OperationResultCode Code { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }

    public bool IsSuccess => Code is OperationResultCode.Success or OperationResultCode.AlreadyApplied;
    public bool IsPartialSuccess => Code == OperationResultCode.PartialSuccess;
    public bool IsFailed => Code is OperationResultCode.Failed or OperationResultCode.TimedOut or OperationResultCode.PermissionDenied;

    public static OperationResult Success(string message = "操作成功", TimeSpan duration = default) =>
        new() { Code = OperationResultCode.Success, Message = message, Duration = duration };

    public static OperationResult AlreadyApplied(string message = "已是目标状态") =>
        new() { Code = OperationResultCode.AlreadyApplied, Message = message };

    public static OperationResult Failed(string message, Exception? ex = null, string? detail = null) =>
        new() { Code = OperationResultCode.Failed, Message = message, Exception = ex, Detail = detail ?? ex?.Message };

    public static OperationResult TimedOut(string message = "操作超时") =>
        new() { Code = OperationResultCode.TimedOut, Message = message };

    public static OperationResult PermissionDenied(string? message = null) =>
        new()
        {
            Code = OperationResultCode.PermissionDenied,
            // Why: 默认文案必须由 ElevationGate 按真实提权状态生成，已提权时不得再写「需要管理员」。
            Message = message ?? ElevationGate.DescribeWriteDenial("系统写入")
        };

    public static OperationResult NotSupported(string message = "当前系统不支持此操作") =>
        new() { Code = OperationResultCode.NotSupported, Message = message };

    public static OperationResult Cancelled(string message = "操作已取消") =>
        new() { Code = OperationResultCode.Cancelled, Message = message };

    public static OperationResult PartialSuccess(string message, int successCount, int totalCount) =>
        new() { Code = OperationResultCode.PartialSuccess, Message = message, Detail = $"成功 {successCount}/{totalCount}" };
}

public record OperationResult<T> : OperationResult
{
    public T? Data { get; init; }

    public static OperationResult<T> Success(T data, string message = "操作成功", TimeSpan duration = default) =>
        new() { Code = OperationResultCode.Success, Message = message, Data = data, Duration = duration };

    public static OperationResult<T> Fail(string message, Exception? ex = null, string? detail = null) =>
        new() { Code = OperationResultCode.Failed, Message = message, Exception = ex, Detail = detail ?? ex?.Message };
}
