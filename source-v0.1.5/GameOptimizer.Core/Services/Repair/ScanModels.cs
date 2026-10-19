namespace GameOptimizer.Core.Services.Repair;

public enum IssueSeverity
{
    Info,       // 信息提示
    Warning,    // 警告
    Error,      // 错误
    Critical    // 严重
}

public enum ScanCategory
{
    Network,        // 网络
    Audio,          // 音频
    Bluetooth,      // 蓝牙
    Display,        // 显示
    Peripheral,     // 外设
    Storage,        // 存储
    System,         // 系统
    Optimization    // 优化状态
}

public record ScanIssue
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public ScanCategory Category { get; init; }
    public IssueSeverity Severity { get; init; } = IssueSeverity.Warning;
    public string? SuggestedRepairId { get; init; }
    public DateTime DetectedAt { get; init; } = DateTime.Now;
}

public record SystemScanResult
{
    public IReadOnlyList<ScanIssue> Issues { get; init; } = [];
    public int TotalScanned { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public bool HasCriticalIssues => Issues.Any(i => i.Severity == IssueSeverity.Critical);
    public bool HasErrors => Issues.Any(i => i.Severity >= IssueSeverity.Error);
}

public record ScanProgressInfo
{
    public string CurrentTask { get; init; } = string.Empty;
    public int CompletedTasks { get; init; }
    public int TotalTasks { get; init; }
    public int IssuesFound { get; init; }
}
