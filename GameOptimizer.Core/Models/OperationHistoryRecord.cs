namespace GameOptimizer.Core.Models;

public enum OperationAction
{
    Apply,
    Restore
}

public record OperationHistoryRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string ModuleKey { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public string OperationTitle { get; init; } = string.Empty;
    public OperationAction Action { get; init; }
    public bool Success { get; init; }
    public bool Changed { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorDetail { get; init; }
    public TimeSpan Duration { get; init; }
    public Guid? ReceiptId { get; init; }
    public Guid? RecoveryId { get; init; }
    public string? CapabilityId { get; init; }
    public string? CapabilityVersion { get; init; }
    public string? ResourceKey { get; init; }
    public string? ScopeKind { get; init; }
    public string? ScopeIdentity { get; init; }
    public string? RecoveryIntegrityHash { get; init; }
    public string? RecoveryState { get; init; }
    public string? ReasonCode { get; init; }
}

public record OperationHistorySummary
{
    public DateTime Date { get; init; }
    public int TotalOperations { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public int ApplyCount { get; init; }
    public int RestoreCount { get; init; }
}
