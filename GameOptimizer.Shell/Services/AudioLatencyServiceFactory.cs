using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services.Audio;

namespace GameOptimizer.Shell.Services;

public static class AudioLatencyServiceFactory
{
    private static IAudioLatencyService? _instance;
    private static readonly string LogPath = System.IO.Path.Combine(AppPaths.LogsDir, "audio_debug.log");
    
    private static void Log(string msg)
    {
        try
        {
            // P3-005: 使用 SafeDirectoryOperations 确保目录存在
            var dir = System.IO.Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(dir, out var error))
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioLatencyServiceFactory] Failed to create log directory: {error}");
                    return;
                }
            }
            
            System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioLatencyServiceFactory] Log write failed: {ex.Message}"); }
    }
    
    public static IAudioLatencyService Create()
    {
        return _instance ??= CreateInstance();
    }
    
    private static IAudioLatencyService CreateInstance()
    {
        Log("CreateInstance called");
        try
        {
            var asm = typeof(IAudioLatencyService).Assembly;
            Log($"Assembly: {asm.FullName}");
            Log($"Assembly Location: {AppContext.BaseDirectory}");
            
            // 列出所有 Audio 相关类型
            try
            {
                var allTypes = asm.GetTypes();
                Log($"Total types in assembly: {allTypes.Length}");
                var audioTypes = allTypes.Where(t => t.Namespace?.Contains("Audio") == true).ToList();
                foreach (var t in audioTypes)
                {
                    Log($"Found type: {t.FullName} (IsClass={t.IsClass}, IsInterface={t.IsInterface})");
                }
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                Log($"ReflectionTypeLoadException: {ex.Message}");
                foreach (var loaderEx in ex.LoaderExceptions ?? Array.Empty<Exception>())
                {
                    if (loaderEx != null)
                        Log($"  LoaderException: {loaderEx.Message}");
                }
                // 尝试使用已加载的类型
                var loadedTypes = ex.Types?.Where(t => t != null && t.Namespace?.Contains("Audio") == true).ToList();
                if (loadedTypes != null)
                {
                    foreach (var t in loadedTypes)
                    {
                        Log($"Loaded type: {t!.FullName}");
                    }
                    var serviceType = loadedTypes.FirstOrDefault(t => 
                        t != null && typeof(IAudioLatencyService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    if (serviceType != null)
                    {
                        Log($"Found service type via exception: {serviceType.FullName}");
                        var instance = Activator.CreateInstance(serviceType) as IAudioLatencyService;
                        if (instance != null) return instance;
                    }
                }
            }
            
            var type = asm.GetType("GameOptimizer.Core.Services.Audio.AudioLatencyService");
            Log($"Direct lookup: {type != null}");
            
            if (type != null)
            {
                var instance = Activator.CreateInstance(type) as IAudioLatencyService;
                Log($"Instance created: {instance != null}");
                if (instance != null) return instance;
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Log($"Inner: {ex.InnerException.Message}");
        }
        Log("Using FallbackAudioLatencyService");
        return new FallbackAudioLatencyService();
    }
}

internal sealed class FallbackAudioLatencyService : IAudioLatencyService
{
    public Task<AudioLatencyInfo?> GetDefaultDeviceLatencyAsync() => Task.FromResult<AudioLatencyInfo?>(null);
    public Task<IReadOnlyList<AudioLatencyInfo>> GetAllDevicesLatencyAsync() => Task.FromResult<IReadOnlyList<AudioLatencyInfo>>(Array.Empty<AudioLatencyInfo>());
    public Task<double> MeasureRoundTripLatencyAsync(int iterations = 5) => Task.FromResult(0.0);
}
