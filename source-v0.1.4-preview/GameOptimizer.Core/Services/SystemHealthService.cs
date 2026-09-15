using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Services;

public sealed class SystemHealthService : ISystemHealthService
{
    private readonly ILogger<SystemHealthService> _logger;
    private const long MinDiskSpaceBytes = 500 * 1024 * 1024; // 500MB
    private const double DiskWarningThreshold = 90.0;
    private const double DiskCriticalThreshold = 95.0;
    private const double MemoryWarningThreshold = 85.0;
    private const double MemoryCriticalThreshold = 95.0;
    private const double CpuWarningThreshold = 80.0;
    private const double CpuCriticalThreshold = 95.0;
    private const double TempWarningThreshold = 80.0;
    private const double TempCriticalThreshold = 95.0;

    public SystemHealthService(ILogger<SystemHealthService> logger)
    {
        _logger = logger;
    }

    public async Task<SystemHealthReport> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var recommendations = new List<string>();

        var diskHealth = await CheckDiskHealthAsync(warnings, recommendations);
        var memoryHealth = CheckMemoryHealth(warnings, recommendations);
        var cpuHealth = await CheckCpuHealthAsync(warnings, recommendations, cancellationToken);

        var overallStatus = DetermineOverallStatus(diskHealth.Status, memoryHealth.Status, cpuHealth.Status);

        return new SystemHealthReport
        {
            OverallStatus = overallStatus,
            DiskHealth = diskHealth,
            MemoryHealth = memoryHealth,
            CpuHealth = cpuHealth,
            Warnings = warnings,
            Recommendations = recommendations
        };
    }

    public bool CanProceedWithOptimization(SystemHealthReport report)
    {
        if (report.OverallStatus == HealthStatus.Critical)
        {
            _logger.LogWarning("[SystemHealth] Critical health status, optimization not recommended");
            return false;
        }
        return true;
    }

    private Task<DiskHealthInfo> CheckDiskHealthAsync(List<string> warnings, List<string> recommendations)
    {
        try
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var driveInfo = new DriveInfo(systemDrive);
            var available = driveInfo.AvailableFreeSpace;
            var total = driveInfo.TotalSize;
            var usagePercent = (1 - (double)available / total) * 100;

            var status = usagePercent switch
            {
                >= DiskCriticalThreshold => HealthStatus.Critical,
                >= DiskWarningThreshold => HealthStatus.Warning,
                _ => HealthStatus.Healthy
            };

            if (available < MinDiskSpaceBytes)
            {
                status = HealthStatus.Critical;
                warnings.Add($"System disk space critically low: {FormatBytes(available)} available");
                recommendations.Add("Free up disk space before proceeding with optimization");
            }
            else if (status == HealthStatus.Warning)
            {
                warnings.Add($"System disk usage high: {usagePercent:F1}%");
            }

            return Task.FromResult(new DiskHealthInfo
            {
                AvailableBytes = available,
                TotalBytes = total,
                Status = status
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SystemHealth] Failed to check disk health");
            return Task.FromResult(new DiskHealthInfo { Status = HealthStatus.Warning });
        }
    }

    private MemoryHealthInfo CheckMemoryHealth(List<string> warnings, List<string> recommendations)
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            var totalMemory = gcInfo.TotalAvailableMemoryBytes;
            
            // Why: 先检查类别是否存在，避免在无 Memory 计数器的环境上抛 InvalidOperationException
            if (!PerformanceCounterCategory.Exists("Memory"))
            {
                _logger.LogWarning("[SystemHealth] 'Memory' performance counter category not found");
                return new MemoryHealthInfo { Status = HealthStatus.Warning };
            }
            
            using var counter = new PerformanceCounter("Memory", "Available Bytes", true);
            var available = (long)counter.NextValue();
            var usagePercent = (1 - (double)available / totalMemory) * 100;

            var status = usagePercent switch
            {
                >= MemoryCriticalThreshold => HealthStatus.Critical,
                >= MemoryWarningThreshold => HealthStatus.Warning,
                _ => HealthStatus.Healthy
            };

            if (status == HealthStatus.Critical)
            {
                warnings.Add($"Memory usage critical: {usagePercent:F1}%");
                recommendations.Add("Close unnecessary applications to free memory");
            }
            else if (status == HealthStatus.Warning)
            {
                warnings.Add($"Memory usage high: {usagePercent:F1}%");
            }

            return new MemoryHealthInfo
            {
                AvailableBytes = available,
                TotalBytes = totalMemory,
                Status = status
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SystemHealth] Failed to check memory health");
            return new MemoryHealthInfo { Status = HealthStatus.Warning };
        }
    }

    private async Task<CpuHealthInfo> CheckCpuHealthAsync(List<string> warnings, List<string> recommendations, CancellationToken cancellationToken)
    {
        try
        {
            // Why: 先检查类别是否存在，避免在无 Processor 计数器的环境上抛 InvalidOperationException
            if (!PerformanceCounterCategory.Exists("Processor"))
            {
                _logger.LogWarning("[SystemHealth] 'Processor' performance counter category not found");
                return new CpuHealthInfo { UsagePercent = 0, Temperature = null, Status = HealthStatus.Warning };
            }
            
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            cpuCounter.NextValue();
            await Task.Delay(100, cancellationToken);
            var cpuUsage = cpuCounter.NextValue();

            var status = cpuUsage switch
            {
                >= (float)CpuCriticalThreshold => HealthStatus.Critical,
                >= (float)CpuWarningThreshold => HealthStatus.Warning,
                _ => HealthStatus.Healthy
            };

            if (status == HealthStatus.Critical)
            {
                warnings.Add($"CPU usage critical: {cpuUsage:F1}%");
                recommendations.Add("Wait for CPU usage to decrease before optimization");
            }
            else if (status == HealthStatus.Warning)
            {
                warnings.Add($"CPU usage high: {cpuUsage:F1}%");
            }

            return new CpuHealthInfo
            {
                UsagePercent = cpuUsage,
                Temperature = null,
                Status = status
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SystemHealth] Failed to check CPU health");
            return new CpuHealthInfo { Status = HealthStatus.Healthy };
        }
    }

    private static HealthStatus DetermineOverallStatus(params HealthStatus[] statuses)
    {
        if (statuses.Any(s => s == HealthStatus.Critical))
            return HealthStatus.Critical;
        if (statuses.Any(s => s == HealthStatus.Warning))
            return HealthStatus.Warning;
        return HealthStatus.Healthy;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
