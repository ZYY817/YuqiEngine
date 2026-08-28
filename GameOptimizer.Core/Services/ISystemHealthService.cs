namespace GameOptimizer.Core.Services;

public enum HealthStatus
{
    Healthy,
    Warning,
    Critical
}

public record SystemHealthReport
{
    public HealthStatus OverallStatus { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.Now;
    public DiskHealthInfo DiskHealth { get; init; } = new();
    public MemoryHealthInfo MemoryHealth { get; init; } = new();
    public CpuHealthInfo CpuHealth { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
}

public record DiskHealthInfo
{
    public long AvailableBytes { get; init; }
    public long TotalBytes { get; init; }
    public double UsagePercent => TotalBytes > 0 ? (1 - (double)AvailableBytes / TotalBytes) * 100 : 0;
    public HealthStatus Status { get; init; }
}

public record MemoryHealthInfo
{
    public long AvailableBytes { get; init; }
    public long TotalBytes { get; init; }
    public double UsagePercent => TotalBytes > 0 ? (1 - (double)AvailableBytes / TotalBytes) * 100 : 0;
    public HealthStatus Status { get; init; }
}

public record CpuHealthInfo
{
    public double UsagePercent { get; init; }
    public double? Temperature { get; init; }
    public HealthStatus Status { get; init; }
}

public interface ISystemHealthService
{
    Task<SystemHealthReport> CheckHealthAsync(CancellationToken cancellationToken = default);
    bool CanProceedWithOptimization(SystemHealthReport report);
}
