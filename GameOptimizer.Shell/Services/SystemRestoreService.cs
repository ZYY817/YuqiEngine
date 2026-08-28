using GameOptimizer.Core.Localization;
using GameOptimizer.Core.SystemRestore;

namespace GameOptimizer.Shell.Services;

public interface ISystemRestoreService
{
    Task<bool> CreateRestorePointAsync(string description, CancellationToken ct = default);
    Task<bool> DeleteRestorePointAsync(int sequenceNumber, CancellationToken ct = default);
    bool IsSystemRestoreEnabled();
    Task<SystemRestoreStatus> GetSystemRestoreStatusAsync(CancellationToken ct = default);
    Task<List<RestorePointInfo>> GetAllRestorePointsAsync(CancellationToken ct = default);
    RestorePointInfo? GetLatestRestorePoint();
    void OpenSystemRestoreWizard();
    void OpenSystemRestoreSettings();
    Task<bool> EnableSystemRestoreAsync(CancellationToken ct = default);
    string LastError { get; }
    (bool CanCreate, string Reason) CanCreateRestorePoint();
}

public sealed class SystemRestoreStatus
{
    public bool IsEnabled { get; set; }
    public bool IsAdministrator { get; set; }
    public string SystemDrive { get; set; } = string.Empty;
    public double UsedSpaceGb { get; set; }
    public double MaxSpaceGb { get; set; }
    public int RestorePointCount { get; set; }
    public DateTime? LastRestorePointTime { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public int DiskPercentAllocated { get; set; }
    public double TotalDiskSizeGb { get; set; }
    public DateTimeOffset? NextCreationAllowedAtUtc { get; set; }

    public string SpaceUsageText => MaxSpaceGb > 0
        ? LocalizationService.Instance.Get("Restore.SpaceAllocated", MaxSpaceGb, DiskPercentAllocated)
        : LocalizationService.Instance.Get("Restore.NotConfigured");
}

public sealed class RestorePointInfo
{
    public SystemRestorePointIdentity? Identity { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
    public int SequenceNumber { get; set; }
    public int RestorePointType { get; set; }
    public string VolumeIdentity { get; set; } = string.Empty;

    public bool IsSystemCheckpoint => RestorePointType is 0 or 1 or 10 or 13;
    public string CheckpointTypeText => LocalizationService.Instance.Get(
        IsSystemCheckpoint ? "Restore.SystemAuto" : "Restore.ManualCreate");
    public string RestoreButtonText => LocalizationService.Instance.Get("Restore.Restore");
    public string DeleteButtonText => LocalizationService.Instance.Get("Restore.Delete");

    public string TypeDescription => RestorePointType switch
    {
        0 => LocalizationService.Instance.Get("Restore.Type.AppInstall"),
        1 => LocalizationService.Instance.Get("Restore.Type.AppUninstall"),
        10 => LocalizationService.Instance.Get("Restore.Type.DriverInstall"),
        12 => LocalizationService.Instance.Get("Restore.Type.SystemChange"),
        13 => LocalizationService.Instance.Get("Restore.Type.CancelOperation"),
        _ => LocalizationService.Instance.Get("Restore.Type.Manual")
    };

    public string TimeAgo
    {
        get
        {
            var difference = DateTime.Now - CreationTime;
            if (difference.TotalMinutes < 60)
            {
                return LocalizationService.Instance.Get("Restore.MinutesAgo", (int)difference.TotalMinutes);
            }
            if (difference.TotalHours < 24)
            {
                return LocalizationService.Instance.Get("Restore.HoursAgo", (int)difference.TotalHours);
            }
            if (difference.TotalDays < 7)
            {
                return LocalizationService.Instance.Get("Restore.DaysAgo", (int)difference.TotalDays);
            }
            return CreationTime.ToString("yyyy-MM-dd HH:mm");
        }
    }
}

/// <summary>
/// Compatibility facade for legacy non-page callers. All state and creation flow are owned by
/// <see cref="ISystemRestorePointOwner"/>; unsupported delete/enable/restore intents fail closed.
/// </summary>
public sealed class SystemRestoreService : ISystemRestoreService
{
    private readonly ISystemRestorePointOwner _owner;
    private readonly ISystemRestorePointConfirmationPrompt _confirmationPrompt;
    private readonly TimeProvider _timeProvider;
    private readonly ISystemRestoreManualLauncher _manualLauncher;
    private SystemRestorePointProbe? _lastProbe;
    private RestorePointInfo? _lastPoint;

    public SystemRestoreService(
        ISystemRestorePointOwner owner,
        ISystemRestorePointConfirmationPrompt confirmationPrompt,
        TimeProvider? timeProvider = null,
        ISystemRestoreManualLauncher? manualLauncher = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _confirmationPrompt = confirmationPrompt ?? throw new ArgumentNullException(nameof(confirmationPrompt));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _manualLauncher = manualLauncher ?? new UnsupportedManualLauncher();
    }

    public string LastError { get; private set; } = string.Empty;

    public async Task<bool> CreateRestorePointAsync(string description, CancellationToken ct = default)
    {
        LastError = string.Empty;
        var prepared = await _owner.PrepareCreateAsync(
            new SystemRestorePointCreateRequest(description, "legacy-shell-facade"),
            ct).ConfigureAwait(false);
        if (!prepared.Success || prepared.CreatePlan is null)
        {
            LastError = prepared.Message;
            return false;
        }

        if (!await _confirmationPrompt.ConfirmCreateAsync(prepared.CreatePlan, ct).ConfigureAwait(false))
        {
            LastError = "Restore-point creation was not explicitly confirmed.";
            return false;
        }

        var confirmation = _owner.ConfirmCreate(
            prepared.CreatePlan,
            new SystemRestorePointConfirmationEvidence(
                prepared.CreatePlan.PlanId,
                prepared.CreatePlan.PlanHash,
                _timeProvider.GetUtcNow(),
                ExplicitlyConfirmed: true));
        if (confirmation is null)
        {
            LastError = "Restore-point confirmation did not match the frozen plan.";
            return false;
        }

        var result = await _owner.ExecuteCreateAsync(prepared.CreatePlan, confirmation, ct).ConfigureAwait(false);
        if (!result.Success || result.Receipt?.ReadBackVerified != true || result.Receipt.CreatedIdentity is null)
        {
            LastError = result.Message;
            return false;
        }

        _lastPoint = Map(result.Receipt.CreatedIdentity);
        return true;
    }

    public Task<bool> DeleteRestorePointAsync(int sequenceNumber, CancellationToken ct = default)
    {
        LastError = "Deleting restore points is not owned by the System Restore Point Owner and is fail-closed.";
        return Task.FromResult(false);
    }

    public bool IsSystemRestoreEnabled()
    {
        if (_lastProbe is null)
        {
            var result = _owner.InspectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            _lastProbe = result.Probe;
        }
        return _lastProbe?.ProtectionState == SystemProtectionState.Enabled;
    }

    public async Task<SystemRestoreStatus> GetSystemRestoreStatusAsync(CancellationToken ct = default)
    {
        var result = await _owner.InspectAsync(ct).ConfigureAwait(false);
        _lastProbe = result.Probe;
        LastError = result.Success ? string.Empty : result.Message;
        var points = result.Probe?.RestorePoints ?? Array.Empty<SystemRestorePointIdentity>();
        var latest = points.OrderByDescending(point => point.CreatedAtUtc).FirstOrDefault();
        return new SystemRestoreStatus
        {
            IsEnabled = result.Probe?.ProtectionState == SystemProtectionState.Enabled,
            IsAdministrator = result.Probe?.IsAdministrator == true,
            SystemDrive = result.Probe?.SystemVolumeIdentity ?? string.Empty,
            RestorePointCount = points.Count,
            LastRestorePointTime = latest?.CreatedAtUtc.LocalDateTime,
            NextCreationAllowedAtUtc = result.Probe?.NextCreationAllowedAtUtc,
            StatusMessage = result.Message
        };
    }

    public async Task<List<RestorePointInfo>> GetAllRestorePointsAsync(CancellationToken ct = default)
    {
        var result = await _owner.InspectAsync(ct).ConfigureAwait(false);
        _lastProbe = result.Probe;
        LastError = result.Success ? string.Empty : result.Message;
        var points = (result.Probe?.RestorePoints ?? Array.Empty<SystemRestorePointIdentity>())
            .OrderByDescending(point => point.CreatedAtUtc)
            .Select(Map)
            .ToList();
        _lastPoint = points.FirstOrDefault();
        return points;
    }

    public RestorePointInfo? GetLatestRestorePoint() => _lastPoint;

    public void OpenSystemRestoreWizard()
    {
        var result = _manualLauncher.OpenRecoveryWizard();
        LastError = result.Success ? string.Empty : result.Message;
    }

    public void OpenSystemRestoreSettings()
    {
        var result = _manualLauncher.OpenProtectionSettings();
        LastError = result.Success ? string.Empty : result.Message;
    }

    public Task<bool> EnableSystemRestoreAsync(CancellationToken ct = default)
    {
        LastError = "Enabling System Protection changes machine policy/state and is fail-closed.";
        return Task.FromResult(false);
    }

    public (bool CanCreate, string Reason) CanCreateRestorePoint()
    {
        var inspection = _owner.InspectAsync()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        _lastProbe = inspection.Probe;
        if (inspection.Probe is not { } probe)
        {
            return (false, inspection.Message);
        }
        var capability = SystemRestorePointCapabilities.EvaluateCreate(probe, _timeProvider.GetUtcNow());
        return (capability.IsAvailable, capability.Reason);
    }

    internal static RestorePointInfo Map(SystemRestorePointIdentity point) => new()
    {
        Identity = point,
        Description = point.Description,
        CreationTime = point.CreatedAtUtc.LocalDateTime,
        SequenceNumber = checked((int)point.SequenceNumber),
        RestorePointType = point.RestorePointType,
        VolumeIdentity = point.VolumeIdentity
    };

    private sealed class UnsupportedManualLauncher : ISystemRestoreManualLauncher
    {
        public SystemRestoreManualLaunchResult OpenProtectionSettings() =>
            new(false, "Windows System Protection must be opened manually.");

        public SystemRestoreManualLaunchResult OpenRecoveryWizard() =>
            new(false, "Windows System Restore must be opened manually.");
    }
}
