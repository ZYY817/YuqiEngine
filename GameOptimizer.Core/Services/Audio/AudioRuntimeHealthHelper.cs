using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Core.Services.Audio;

internal readonly record struct AudioRuntimeHealthResult(bool IsHealthy, bool RecoveryAttempted, string Message);

internal static class AudioRuntimeHealthHelper
{
    private const string EndpointBuilderServiceName = "AudioEndpointBuilder";
    private const string AudioServiceName = "Audiosrv";

    public static async Task<AudioRuntimeHealthResult> EnsureHealthyAsync(
        bool attemptRecovery,
        CancellationToken cancellationToken)
    {
        if (await IsCoreHealthyAsync().ConfigureAwait(false))
        {
            return new AudioRuntimeHealthResult(true, false, "Windows audio core is healthy.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new AudioRuntimeHealthResult(
            false,
            false,
            attemptRecovery
                ? "Audio service mutation requires the typed Device Repair Owner and is currently blocked."
                : "Windows audio core is unhealthy.");
    }

    public static async Task<bool> IsCoreHealthyAsync()
    {
        var endpointExists = await RepairHelper.ServiceExistsAsync(EndpointBuilderServiceName).ConfigureAwait(false);
        var audioExists = await RepairHelper.ServiceExistsAsync(AudioServiceName).ConfigureAwait(false);
        if (!endpointExists && !audioExists)
        {
            return true;
        }

        var endpointOk = !endpointExists || await RepairHelper.IsServiceRunningAsync(EndpointBuilderServiceName).ConfigureAwait(false);
        var audioOk = !audioExists || await RepairHelper.IsServiceRunningAsync(AudioServiceName).ConfigureAwait(false);
        return endpointOk && audioOk;
    }

    public static async Task<bool> IsServiceHealthyAsync(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (!await RepairHelper.ServiceExistsAsync(serviceName).ConfigureAwait(false))
        {
            return true;
        }

        return await RepairHelper.IsServiceRunningAsync(serviceName).ConfigureAwait(false);
    }

    public static Task<bool> RestartCoreServicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public static Task<bool> EnsureServiceRunningAsync(string serviceName, string startupMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(startupMode);
        return Task.FromResult(false);
    }
}
