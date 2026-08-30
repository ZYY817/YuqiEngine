using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class FirstProtectionPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _statePath;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public FirstProtectionPreferenceStore()
        : this(Path.Combine(AppPaths.ConfigDir, "first_protection_preference.json"))
    {
    }

    public FirstProtectionPreferenceStore(string statePath)
    {
        _statePath = string.IsNullOrWhiteSpace(statePath)
            ? throw new ArgumentException("A state path is required.", nameof(statePath))
            : Path.GetFullPath(statePath);
    }

    public async Task<FirstProtectionPreference> LoadAsync(CancellationToken ct = default)
    {
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_statePath))
            {
                return new FirstProtectionPreference();
            }

            var json = await File.ReadAllTextAsync(_statePath, ct).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<FirstProtectionPreference>(json, JsonOptions);
            return state ?? Corrupted("首次保护偏好为空，已重新提示用户选择。");
        }
        catch (JsonException)
        {
            return Corrupted("首次保护偏好文件损坏，未影响现有安全基线。");
        }
        catch (IOException)
        {
            return Corrupted("首次保护偏好暂时无法读取，未影响现有安全基线。");
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SaveAsync(FirstProtectionPreference state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_statePath)
                ?? throw new InvalidOperationException("The first-protection state directory is unavailable.");
            Directory.CreateDirectory(directory);
            state.LastUpdatedUtc = DateTime.UtcNow;
            var tempPath = $"{_statePath}.{Guid.NewGuid():N}.tmp";
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            File.Move(tempPath, _statePath, overwrite: true);
        }
        finally
        {
            _sync.Release();
        }
    }

    public Task DeferAsync(string? failureReason = null, CancellationToken ct = default) =>
        SaveAsync(new FirstProtectionPreference
        {
            Status = FirstProtectionPreferenceStatus.UserDeferred,
            OnboardingRevision = FirstProtectionPreference.CurrentOnboardingRevision,
            IntroShownUtc = DateTime.UtcNow,
            LastFailureReason = failureReason
        }, ct);

    public Task RecordFailureAsync(string reason, CancellationToken ct = default) =>
        SaveAsync(new FirstProtectionPreference
        {
            Status = FirstProtectionPreferenceStatus.BackupFailed,
            OnboardingRevision = FirstProtectionPreference.CurrentOnboardingRevision,
            IntroShownUtc = DateTime.UtcNow,
            LastFailureReason = string.IsNullOrWhiteSpace(reason) ? "首次保护备份未完成。" : reason
        }, ct);

    public Task RecordCompletedAsync(CancellationToken ct = default) =>
        SaveAsync(new FirstProtectionPreference
        {
            Status = FirstProtectionPreferenceStatus.BackupCompleted,
            OnboardingRevision = FirstProtectionPreference.CurrentOnboardingRevision,
            IntroShownUtc = DateTime.UtcNow,
            BackupCompletedUtc = DateTime.UtcNow
        }, ct);

    private static FirstProtectionPreference Corrupted(string reason) => new()
    {
        Status = FirstProtectionPreferenceStatus.Corrupted,
        OnboardingRevision = FirstProtectionPreference.CurrentOnboardingRevision,
        LastFailureReason = reason
    };
}
