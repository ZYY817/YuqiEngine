namespace GameOptimizer.Core.Models;

public sealed record SystemResourceSnapshot(
    double CpuUsage,
    double GpuUsage,
    double MemoryUsageGb,
    double MemoryTotalGb,
    double NetworkLatencyMs,
    double TemperatureGpu,
    double TemperatureCpu,
    double DiskUsedGb = 0,
    double DiskTotalGb = 0,
    double DpcLatencyUs = 0);
