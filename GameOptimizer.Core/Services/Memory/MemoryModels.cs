using System.Runtime.Versioning;

namespace GameOptimizer.Core.Services.Memory;

[SupportedOSPlatform("windows")]
public record MemoryStatus(
    double UsedGB,
    double TotalGB,
    double AvailableGB,
    int LoadPercent,
    long StandbyBytes
);

[SupportedOSPlatform("windows")]
public record CleanResult(
    bool Success,
    long FreedBytes,
    string Message
)
{
    public double FreedMB => FreedBytes / (1024.0 * 1024);
    public double FreedGB => FreedBytes / (1024.0 * 1024 * 1024);
}
