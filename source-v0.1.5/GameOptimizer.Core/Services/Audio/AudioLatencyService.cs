using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GameOptimizer.Core.Services.Audio;

public sealed class AudioLatencyService : IAudioLatencyService
{
    public Task<AudioLatencyInfo?> GetDefaultDeviceLatencyAsync()
    {
        return Task.Run(async () =>
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var roundTrip = await MeasureRoundTripLatencyAsync(3);
                return MeasureDeviceLatency(device, true, roundTrip);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioLatencyService] GetDefaultDeviceLatency failed: {ex.Message}");
                return null;
            }
        });
    }

    public Task<IReadOnlyList<AudioLatencyInfo>> GetAllDevicesLatencyAsync()
    {
        return Task.Run(async () =>
        {
            var results = new List<AudioLatencyInfo>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var defaultId = defaultDevice.ID;
                var roundTrip = await MeasureRoundTripLatencyAsync(3);
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in devices)
                {
                    try
                    {
                        var isDefault = device.ID == defaultId;
                        var info = MeasureDeviceLatency(device, isDefault, isDefault ? roundTrip : 0);
                        if (info != null) results.Add(info);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[AudioLatencyService] Failed to measure device {device.FriendlyName}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioLatencyService] GetAllDevicesLatency failed: {ex.Message}");
            }
            return (IReadOnlyList<AudioLatencyInfo>)results;
        });
    }

    public Task<double> MeasureRoundTripLatencyAsync(int iterations = 5)
    {
        return Task.Run(() =>
        {
            var latencies = new List<double>();
            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var latency = MeasureSingleRoundTrip();
                    if (latency > 0) latencies.Add(latency);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioLatencyService] RoundTrip iteration {i} failed: {ex.Message}");
                }
            }
            if (latencies.Count == 0) return 0.0;
            latencies.Sort();
            var trimmed = latencies.Skip(1).Take(Math.Max(1, latencies.Count - 2)).ToList();
            return trimmed.Average();
        });
    }

    private static double MeasureSingleRoundTrip()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sw = new Stopwatch();
            var format = renderDevice.AudioClient.MixFormat;
            using var waveOut = new WasapiOut(renderDevice, AudioClientShareMode.Shared, false, 10);
            // 使用极短的静音脉冲(5ms)，音量极低(0.01)，用户几乎听不到
            var pulseSignal = GenerateSilentPulse(format.SampleRate, format.Channels, 5);
            var provider = new RawSourceWaveStream(new MemoryStream(pulseSignal), format);
            sw.Start();
            waveOut.Init(provider);
            waveOut.Play();
            while (waveOut.PlaybackState == PlaybackState.Playing)
            {
                Thread.Sleep(1);
                if (sw.ElapsedMilliseconds > 200) break;
            }
            sw.Stop();
            var latencyUs = sw.Elapsed.TotalMicroseconds;
            var signalDurationUs = 5.0 * 1000;
            return Math.Max(0, latencyUs - signalDurationUs);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioLatencyService] MeasureSingleRoundTrip failed: {ex.Message}");
            return 0;
        }
    }

    private static byte[] GenerateSilentPulse(int sampleRate, int channels, int durationMs)
    {
        var samples = sampleRate * durationMs / 1000;
        var buffer = new byte[samples * channels * 4];
        // 生成完全静音的脉冲，只用于触发音频管道，不发出任何声音
        // 所有采样值都是0，完全静音
        return buffer;
    }

    private static AudioLatencyInfo? MeasureDeviceLatency(MMDevice device, bool isDefault, double roundTripUs)
    {
        try
        {
            var format = device.AudioClient.MixFormat;
            var defaultPeriod = device.AudioClient.DefaultDevicePeriod;
            var minimumPeriod = device.AudioClient.MinimumDevicePeriod;
            var currentPeriodUs = defaultPeriod / 10.0;
            var minimumPeriodUs = minimumPeriod / 10.0;
            var bufferFrames = (int)(format.SampleRate * currentPeriodUs / 1000000.0);
            var deviceInfo = new AudioDeviceInfo(
                device.ID,
                device.FriendlyName,
                isDefault,
                format.SampleRate,
                format.Channels,
                format.BitsPerSample
            );
            return new AudioLatencyInfo(
                deviceInfo,
                currentPeriodUs,
                minimumPeriodUs,
                roundTripUs,
                bufferFrames,
                "Shared",
                DateTime.Now
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioLatencyService] MeasureDeviceLatency failed: {ex.Message}");
            return null;
        }
    }
}
