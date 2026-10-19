namespace GameOptimizer.Core.Services.Audio;

public record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault,
    int SampleRate,
    int Channels,
    int BitsPerSample
);

public record AudioLatencyInfo(
    AudioDeviceInfo Device,
    double CurrentPeriodUs,
    double MinimumPeriodUs,
    double RoundTripLatencyUs,
    int BufferFrames,
    string ShareMode,
    DateTime MeasuredAt
)
{
    public double OptimizationPotentialPercent => 
        CurrentPeriodUs > 0 ? Math.Max(0, (CurrentPeriodUs - MinimumPeriodUs) / CurrentPeriodUs * 100) : 0;
    
    public LatencyLevel Level => RoundTripLatencyUs switch
    {
        < 5000 => LatencyLevel.VeryLow,
        < 10000 => LatencyLevel.Low,
        < 20000 => LatencyLevel.Medium,
        _ => LatencyLevel.High
    };
}

public enum LatencyLevel
{
    VeryLow,
    Low,
    Medium,
    High
}
