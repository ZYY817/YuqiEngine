using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services;

public sealed class RetryPolicy
{
    private static LocalizationService Loc => LocalizationService.Instance;
    
    public int MaxRetries { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public double BackoffMultiplier { get; init; } = 2.0;
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan MaxTotalDuration { get; init; } = TimeSpan.FromMinutes(2);

    public static RetryPolicy Default { get; } = new();
    public static RetryPolicy Fast { get; } = new() { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(200), MaxTotalDuration = TimeSpan.FromSeconds(30) };
    public static RetryPolicy Aggressive { get; } = new() { MaxRetries = 5, InitialDelay = TimeSpan.FromSeconds(1), MaxTotalDuration = TimeSpan.FromMinutes(5) };

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool>? shouldRetry = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(operation, nameof(operation));
        shouldRetry ??= DefaultShouldRetry;
        var delay = InitialDelay;
        Exception? lastException = null;
        var startTime = DateTime.UtcNow;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (DateTime.UtcNow - startTime > MaxTotalDuration)
            {
                throw new TimeoutException(Loc.Get("Retry.TotalTimeExceeded", MaxTotalDuration.TotalSeconds));
            }

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxRetries && shouldRetry(ex))
            {
                lastException = ex;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * BackoffMultiplier, MaxDelay.TotalMilliseconds));
            }
        }

        throw lastException ?? new InvalidOperationException(Loc.Get("Retry.Failed"));
    }

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        Func<Exception, bool>? shouldRetry = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return true;
        }, shouldRetry, cancellationToken).ConfigureAwait(false);
    }

    private static bool DefaultShouldRetry(Exception ex)
    {
        return ex is TimeoutException
            or System.IO.IOException
            or System.Net.Http.HttpRequestException
            or System.Management.ManagementException
            or UnauthorizedAccessException;
    }
}
