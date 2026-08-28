namespace GameOptimizer.Core.Services.Audio;

public interface IAudioLatencyService
{
    Task<AudioLatencyInfo?> GetDefaultDeviceLatencyAsync();
    Task<IReadOnlyList<AudioLatencyInfo>> GetAllDevicesLatencyAsync();
    Task<double> MeasureRoundTripLatencyAsync(int iterations = 5);
}
