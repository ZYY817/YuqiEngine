using System;
using System.Collections.Generic;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

public sealed class DemoSystemInsightService : ISystemInsightService
{
    private readonly Random _random = new();

    public SystemResourceSnapshot GetCurrentSnapshot()
    {
        return new SystemResourceSnapshot(
            CpuUsage: Math.Round(45 + _random.NextDouble() * 20, 1),
            GpuUsage: Math.Round(67 + _random.NextDouble() * 15, 1),
            MemoryUsageGb: 12.3,
            MemoryTotalGb: 32,
            NetworkLatencyMs: Math.Round(18 + _random.NextDouble() * 6, 1),
            TemperatureGpu: Math.Round(62 + _random.NextDouble() * 8, 1),
            TemperatureCpu: Math.Round(58 + _random.NextDouble() * 6, 1),
            DiskUsedGb: 256,
            DiskTotalGb: 512,
            DpcLatencyUs: Math.Round(80 + _random.NextDouble() * 40, 1));
    }

    public DetectedGame? GetActiveGame() =>
        new("Starfield", "Starfield.exe", "Assets/Starfield.png", "starfield-default", "Optimized - 92%");

    public IReadOnlyList<OptimizationRecommendation> GetRecommendations() =>
        new List<OptimizationRecommendation>
        {
            new("Enable frame pacing", "Lock 90 FPS and balance CPU threads to reduce jitter.", "Stability +15%", RequiresRestart: false),
            new("Update GPU driver", "Install NVIDIA 552.58 Game Ready to patch crash loops.", "Crash fix", RequiresRestart: true),
            new("Purge cache", "Flush unused resource cache to free 1.4 GB of RAM.", "More headroom", RequiresRestart: false)
        };
}
