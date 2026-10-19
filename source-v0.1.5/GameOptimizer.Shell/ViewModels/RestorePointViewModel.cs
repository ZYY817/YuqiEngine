using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.Snapshot;
using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Shell.ViewModels;

public enum RestorePointPageState
{
    Loading,
    Empty,
    Ready,
    Partial,
    Error,
    Unauthorized,
    Recovering,
    InterruptedManual,
    RebootRequired
}

public enum RestoreTimelineFilter
{
    All,
    SystemRestorePoints,
    SoftwareRecovery
}

public partial class RestorePointViewModel : ObservableObject
{
    private readonly ISystemRestorePointOwner _owner;
    private readonly ISystemRestorePointConfirmationPrompt _confirmationPrompt;
    private readonly IBaselineCatalogService? _baselineCatalogService;
    private readonly ISnapshotService? _snapshotService;
    private readonly TimeProvider _timeProvider;
    private readonly ISystemRestoreManualLauncher _manualLauncher;
    private readonly ISystemRestorePointStartupReconciliation? _startupReconciliation;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _operationCancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateRestorePoint))]
    private string newRestorePointDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestorePointStatus))]
    [NotifyPropertyChangedFor(nameof(RestorePointStatusDisplay))]
    private string restorePointStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateRestorePoint))]
    private bool isCreatingRestorePoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRestorePoints))]
    private bool isLoadingRestorePoints;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateRestorePoint))]
    [NotifyPropertyChangedFor(nameof(CanCancelCurrentOperation))]
    private bool isOperationBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoveryAttention))]
    private string recoveryAttentionText = string.Empty;

    private SystemRestorePointWorkflowResult? _recoveryAttention;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotAdministrator))]
    [NotifyPropertyChangedFor(nameof(CanCreateRestorePoint))]
    [NotifyPropertyChangedFor(nameof(LastRestorePointTimeText))]
    [NotifyPropertyChangedFor(nameof(IsSystemRestoreDisabled))]
    [NotifyPropertyChangedFor(nameof(ProtectionStatusText))]
    [NotifyPropertyChangedFor(nameof(RestorePointCountText))]
    [NotifyPropertyChangedFor(nameof(NextCreateTimeHint))]
    [NotifyPropertyChangedFor(nameof(Has24HourLimit))]
    private SystemRestorePointProbe? systemRestoreStatus;

    [ObservableProperty]
    private string softwareRecoverySummary = "正在读取软件级恢复状态...";

    [ObservableProperty]
    private string softwareRecoveryDescription = "系统还原点负责整个 Windows 状态恢复，软件级恢复负责本软件的配置和优化结果恢复。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSoftwareRecoveryBackup))]
    [NotifyPropertyChangedFor(nameof(FirstRunBaselineBadgeText))]
    private bool hasFirstRunBaseline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSoftwareRecoveryBackup))]
    [NotifyPropertyChangedFor(nameof(RegularSnapshotCountText))]
    private int regularSnapshotCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStateText))]
    private RestorePointPageState pageState = RestorePointPageState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllTimelineSelected))]
    [NotifyPropertyChangedFor(nameof(IsSystemTimelineSelected))]
    [NotifyPropertyChangedFor(nameof(IsSoftwareTimelineSelected))]
    [NotifyPropertyChangedFor(nameof(HasSystemTimelineEntries))]
    [NotifyPropertyChangedFor(nameof(HasSoftwareTimelineEntry))]
    [NotifyPropertyChangedFor(nameof(HasNoTimelineEntries))]
    [NotifyPropertyChangedFor(nameof(TimelineTitle))]
    [NotifyPropertyChangedFor(nameof(TimelineDescription))]
    [NotifyPropertyChangedFor(nameof(TimelineEmptyTitle))]
    [NotifyPropertyChangedFor(nameof(TimelineEmptyDescription))]
    private RestoreTimelineFilter timelineFilter = RestoreTimelineFilter.All;

    public RestorePointViewModel(
        ISystemRestorePointOwner owner,
        ISystemRestorePointConfirmationPrompt confirmationPrompt,
        IBaselineCatalogService? baselineCatalogService = null,
        ISnapshotService? snapshotService = null,
        TimeProvider? timeProvider = null,
        ISystemRestoreManualLauncher? manualLauncher = null,
        ISystemRestorePointStartupReconciliation? startupReconciliation = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _confirmationPrompt = confirmationPrompt ?? throw new ArgumentNullException(nameof(confirmationPrompt));
        _baselineCatalogService = baselineCatalogService;
        _snapshotService = snapshotService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _manualLauncher = manualLauncher ?? new InstructionOnlyManualLauncher();
        _startupReconciliation = startupReconciliation;
        RestorePoints.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoRestorePoints));
            OnPropertyChanged(nameof(HasRestorePoints));
            OnPropertyChanged(nameof(RestorePointCountText));
            RefreshTimelinePresentation();
        };
    }

    public ObservableCollection<RestorePointInfo> RestorePoints { get; } = new();
    public bool HasRestorePointStatus => !string.IsNullOrEmpty(RestorePointStatus);

    // Keep the raw platform response for diagnostics, but never make an untranslated
    // Windows probe message the primary text in the Chinese maintenance workspace.
    public string RestorePointStatusDisplay => ContainsUnlocalizedDiagnostic(RestorePointStatus)
        ? "Windows 未返回可验证的系统保护状态；已保留诊断记录，请刷新或在“系统保护”中人工核对。"
        : RestorePointStatus;
    public bool HasNoRestorePoints => !IsLoadingRestorePoints && RestorePoints.Count == 0;
    public bool HasRestorePoints => !IsLoadingRestorePoints && RestorePoints.Count > 0;
    public bool IsNotAdministrator => SystemRestoreStatus is { IsAdministrator: false };
    public bool IsSystemRestoreDisabled => SystemRestoreStatus is not null
        && SystemRestoreStatus.ProtectionState != SystemProtectionState.Enabled;
    public SystemRestorePointCreateCapability CreateCapability =>
        SystemRestorePointCapabilities.EvaluateCreate(SystemRestoreStatus, _timeProvider.GetUtcNow());
    public bool CanCreateRestorePoint => !IsCreatingRestorePoint && !IsOperationBusy && CreateCapability.IsAvailable;
    public string CreateAvailabilityText => BuildCreateAvailabilityText();
    public bool CanCancelCurrentOperation => IsOperationBusy;
    public bool HasRecoveryAttention => !string.IsNullOrWhiteSpace(RecoveryAttentionText);
    public bool HasSoftwareRecoveryBackup => HasFirstRunBaseline || RegularSnapshotCount > 0;
    public bool IsAllTimelineSelected => TimelineFilter == RestoreTimelineFilter.All;
    public bool IsSystemTimelineSelected => TimelineFilter == RestoreTimelineFilter.SystemRestorePoints;
    public bool IsSoftwareTimelineSelected => TimelineFilter == RestoreTimelineFilter.SoftwareRecovery;
    public bool HasSystemTimelineEntries => HasRestorePoints && TimelineFilter != RestoreTimelineFilter.SoftwareRecovery;
    public bool HasSoftwareTimelineEntry => HasSoftwareRecoveryBackup && TimelineFilter != RestoreTimelineFilter.SystemRestorePoints;
    public bool HasNoTimelineEntries => !IsLoadingRestorePoints && !HasSystemTimelineEntries && !HasSoftwareTimelineEntry;
    public string TimelineTitle => TimelineFilter switch
    {
        RestoreTimelineFilter.SystemRestorePoints => "系统还原点时间线",
        RestoreTimelineFilter.SoftwareRecovery => "软件恢复记录",
        _ => "恢复时间线"
    };
    public string TimelineDescription => TimelineFilter switch
    {
        RestoreTimelineFilter.SystemRestorePoints => "仅显示当前系统接口真实返回的 Windows 系统还原点。",
        RestoreTimelineFilter.SoftwareRecovery => "软件级恢复由备份中心管理；此处只显示已读取到的摘要与入口。",
        _ => "系统还原点与软件级恢复状态均按应用当前可读取范围展示。"
    };
    public string TimelineEmptyTitle => TimelineFilter switch
    {
        RestoreTimelineFilter.SystemRestorePoints => "暂无可用系统还原点",
        RestoreTimelineFilter.SoftwareRecovery => "暂无软件恢复记录",
        _ => "暂无可用恢复记录"
    };
    public string TimelineEmptyDescription => TimelineFilter switch
    {
        RestoreTimelineFilter.SystemRestorePoints => "当前没有读取到系统还原点，或状态尚未完成加载。",
        RestoreTimelineFilter.SoftwareRecovery => "当前没有已接入的首次安全基线或普通快照。",
        _ => "当前没有读取到系统还原点、首次安全基线或普通快照。"
    };
    public bool IsRestoreExecutionPermanentlyBlocked => true;
    public string RestoreExecutionBlockReason => "系统还原执行保持阻断：当前系统接口只有创建和探测能力，恢复需要重启并由用户在 Windows 恢复界面确认。";
    public string PageStateText => PageState switch
    {
        RestorePointPageState.Loading => "加载中",
        RestorePointPageState.Empty => "暂无还原点",
        RestorePointPageState.Ready => "已就绪",
        RestorePointPageState.Partial => "部分可用",
        RestorePointPageState.Error => "读取失败",
        RestorePointPageState.Unauthorized => "需要权限",
        RestorePointPageState.Recovering => "恢复处理中",
        RestorePointPageState.InterruptedManual => "需要人工介入",
        RestorePointPageState.RebootRequired => "需要重启",
        _ => "状态未知"
    };
    public string SoftwareRecoveryTitle => "软件级恢复";
    public string FirstRunBaselineBadgeText => HasFirstRunBaseline ? "首次基线 已建立" : "首次基线 未建立";
    public string RegularSnapshotCountText => $"普通快照 {RegularSnapshotCount} 份";
    public bool Has24HourLimit => !string.IsNullOrEmpty(NextCreateTimeHint);
    public string RestorePointCountText => LocalizationService.Instance.Get("Restore.PointsFormat", RestorePoints.Count);

    partial void OnTimelineFilterChanged(RestoreTimelineFilter value) => RefreshTimelinePresentation();

    partial void OnHasFirstRunBaselineChanged(bool value) => RefreshTimelinePresentation();

    partial void OnRegularSnapshotCountChanged(int value) => RefreshTimelinePresentation();

    partial void OnIsLoadingRestorePointsChanged(bool value) => RefreshTimelinePresentation();

    partial void OnSystemRestoreStatusChanged(SystemRestorePointProbe? value)
    {
        OnPropertyChanged(nameof(CreateCapability));
        OnPropertyChanged(nameof(CreateAvailabilityText));
    }

    private void RefreshTimelinePresentation()
    {
        OnPropertyChanged(nameof(HasSystemTimelineEntries));
        OnPropertyChanged(nameof(HasSoftwareTimelineEntry));
        OnPropertyChanged(nameof(HasNoTimelineEntries));
        OnPropertyChanged(nameof(TimelineTitle));
        OnPropertyChanged(nameof(TimelineDescription));
        OnPropertyChanged(nameof(TimelineEmptyTitle));
        OnPropertyChanged(nameof(TimelineEmptyDescription));
    }

    [RelayCommand]
    private void SelectTimelineFilter(string? filter)
    {
        TimelineFilter = filter switch
        {
            "System" => RestoreTimelineFilter.SystemRestorePoints,
            "Software" => RestoreTimelineFilter.SoftwareRecovery,
            _ => RestoreTimelineFilter.All
        };
    }

    public string LastRestorePointTimeText
    {
        get
        {
            var latest = SystemRestoreStatus?.RestorePoints
                .OrderByDescending(point => point.CreatedAtUtc)
                .FirstOrDefault();
            if (latest is null)
            {
                return LocalizationService.Instance.Get("Restore.None");
            }

            var difference = _timeProvider.GetLocalNow() - latest.CreatedAtUtc.ToLocalTime();
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
            return latest.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
    }

    public string NextCreateTimeHint
    {
        get
        {
            var next = SystemRestoreStatus?.NextCreationAllowedAtUtc;
            if (next is null || next <= _timeProvider.GetUtcNow())
            {
                return string.Empty;
            }

            var remaining = next.Value - _timeProvider.GetUtcNow();
            var local = next.Value.ToLocalTime();
            return remaining.TotalHours >= 1
                ? LocalizationService.Instance.Get("Restore.NextCreateTime", local.ToString("HH:mm"), (int)remaining.TotalHours)
                : LocalizationService.Instance.Get("Restore.NextCreateTimeMinutes", local.ToString("HH:mm"), Math.Max(1, (int)remaining.TotalMinutes));
        }
    }

    public string ProtectionStatusText
    {
        get
        {
            if (IsNotAdministrator)
            {
                return LocalizationService.Instance.Get("Restore.NeedAdmin");
            }
            return SystemRestoreStatus?.ProtectionState switch
            {
                SystemProtectionState.Enabled => LocalizationService.Instance.Get("Restore.Protected"),
                SystemProtectionState.PolicyDisabled => "系统保护已被策略禁用",
                SystemProtectionState.Disabled => LocalizationService.Instance.Get("Restore.Disabled"),
                _ => "系统保护状态无法权威确认"
            };
        }
    }

    public async Task InitializeAsync()
    {
        await RefreshOwnerStateAsync(showToast: false);
        await LoadSoftwareRecoverySummaryAsync();
        LoadStartupReconciliationState();
    }

    public void CancelCurrentOperation() => _operationCancellation?.Cancel();

    public async Task ReevaluateCapabilityAsync()
    {
        OnPropertyChanged(nameof(CreateCapability));
        OnPropertyChanged(nameof(CreateAvailabilityText));
        OnPropertyChanged(nameof(CanCreateRestorePoint));
        OnPropertyChanged(nameof(NextCreateTimeHint));
        if (!IsOperationBusy)
        {
            await RefreshOwnerStateAsync(showToast: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelCurrentOperation))]
    private void CancelCurrentOperationFromUi()
    {
        CancelCurrentOperation();
        RestorePointStatus = "正在取消当前读取/准备操作；若写入已开始，系统状态仍将独立回读确认。";
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        if (!await TryEnterOperationAsync("已有系统还原点操作正在进行，请等待或取消后重试。"))
        {
            return;
        }

        using var operation = BeginOperation();
        var cancellationToken = operation.Token;

        try
        {
            var description = string.IsNullOrWhiteSpace(NewRestorePointDescription)
                ? LocalizationService.Instance.Get("Restore.DefaultDescription", _timeProvider.GetLocalNow().ToString("yyyy-MM-dd HH:mm"))
                : NewRestorePointDescription.Trim();
            var prepared = await _owner.PrepareCreateAsync(
                new SystemRestorePointCreateRequest(description, "restore-point-page"), cancellationToken);
            SystemRestoreStatus = prepared.Probe ?? SystemRestoreStatus;
            if (!prepared.Success || prepared.CreatePlan is null)
            {
                PageState = prepared.Status == SystemRestorePointWorkflowStatus.PermissionDenied
                    ? RestorePointPageState.Unauthorized
                    : RestorePointPageState.Error;
                RestorePointStatus = prepared.Message;
                return;
            }

            if (!await _confirmationPrompt.ConfirmCreateAsync(prepared.CreatePlan, cancellationToken))
            {
                RestorePointStatus = "已取消创建，未执行任何系统写入。";
                return;
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
                RestorePointStatus = "确认已过期或与当前计划不一致，请重新准备。";
                return;
            }

            IsCreatingRestorePoint = true;
            PageState = RestorePointPageState.Recovering;
            RestorePointStatus = LocalizationService.Instance.Get("Restore.Creating");
            var result = await _owner.ExecuteCreateAsync(prepared.CreatePlan, confirmation, cancellationToken);
            RestorePointStatus = result.Success
                ? "还原点已创建并通过独立身份回读。"
                : result.Message;
            PageState = result.Success
                ? RestorePointPageState.Ready
                : RestorePointPageState.Error;
            if (result.Success)
            {
                NewRestorePointDescription = string.Empty;
            }
            await RefreshOwnerStateCoreAsync(showToast: false, cancellationToken);
            await LoadSoftwareRecoverySummaryAsync();
        }
        catch (OperationCanceledException)
        {
            PageState = RestorePointPageState.Partial;
            RestorePointStatus = "操作已取消；若平台写入曾开始，请刷新并核对 Windows 系统保护中的记录。";
        }
        finally
        {
            IsCreatingRestorePoint = false;
            EndOperation();
        }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync() => await RefreshOwnerStateAsync(showToast: true);

    [RelayCommand]
    private void OpenBackupManager()
    {
        if (Application.Current?.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage("backup-manager");
            RestorePointStatus = "已切换到备份中心。";
            return;
        }
        RestorePointStatus = "未找到备份中心入口，请从左侧导航手动进入。";
    }

    [RelayCommand]
    private void OpenSystemRestoreWizard() => ApplyManualLaunch(_manualLauncher.OpenRecoveryWizard());

    [RelayCommand]
    private void OpenSystemRestoreSettings() => ApplyManualLaunch(_manualLauncher.OpenProtectionSettings());

    [RelayCommand]
    private Task EnableSystemRestoreAsync()
    {
        // Enabling System Protection is a machine-level change and must remain an
        // explicit Windows-owned action. Open the official settings surface instead
        // of presenting a dead button or attempting an unconfirmed write.
        var launch = _manualLauncher.OpenProtectionSettings();
        ApplyManualLaunch(launch);
        if (launch.Success)
        {
            RestorePointStatus = LocalizationService.Instance.Get("Restore.SettingsOpened");
        }
        return Task.CompletedTask;
    }

    private void SetManualRestoreState(string message)
    {
        PageState = RestorePointPageState.InterruptedManual;
        RestorePointStatus = message;
    }

    private void ApplyManualLaunch(SystemRestoreManualLaunchResult result)
    {
        PageState = result.Success ? RestorePointPageState.Partial : RestorePointPageState.Error;
        RestorePointStatus = result.Message;
    }

    private async Task RefreshOwnerStateAsync(bool showToast)
    {
        if (!await TryEnterOperationAsync("已有系统还原点操作正在进行；未启动重复刷新。"))
        {
            return;
        }
        using var operation = BeginOperation();
        try
        {
            await RefreshOwnerStateCoreAsync(showToast, operation.Token);
        }
        catch (OperationCanceledException)
        {
            PageState = RestorePointPageState.Partial;
            RestorePointStatus = "状态刷新已取消。";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RefreshOwnerStateCoreAsync(bool showToast, CancellationToken cancellationToken)
    {
        IsLoadingRestorePoints = true;
        PageState = RestorePointPageState.Loading;
        try
        {
            var result = await _owner.InspectAsync(cancellationToken);
            SystemRestoreStatus = result.Probe;
            RestorePoints.Clear();
            foreach (var point in result.Probe?.RestorePoints.OrderByDescending(item => item.CreatedAtUtc)
                         ?? Enumerable.Empty<SystemRestorePointIdentity>())
            {
                RestorePoints.Add(SystemRestoreService.Map(point));
            }
            if (!result.Success)
            {
                PageState = result.Status switch
                {
                    SystemRestorePointWorkflowStatus.Cancelled => RestorePointPageState.Partial,
                    SystemRestorePointWorkflowStatus.PermissionDenied => RestorePointPageState.Unauthorized,
                    _ => RestorePointPageState.Error
                };
                RestorePointStatus = result.Message;
            }
            else
            {
                PageState = RestorePoints.Count == 0
                    ? RestorePointPageState.Empty
                    : IsSystemRestoreDisabled
                        ? RestorePointPageState.Partial
                        : RestorePointPageState.Ready;
                if (showToast)
                {
                    ToastService.Instance.Success(
                        LocalizationService.Instance.Get("Restore.RefreshDoneTitle"),
                        LocalizationService.Instance.Get("Restore.RefreshDoneMsg"));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PageState = RestorePointPageState.Error;
            RestorePointStatus = $"读取系统还原点失败：{ex.Message}";
        }
        finally
        {
            IsLoadingRestorePoints = false;
            OnPropertyChanged(nameof(HasNoRestorePoints));
            OnPropertyChanged(nameof(HasRestorePoints));
            OnPropertyChanged(nameof(RestorePointCountText));
        }
    }

    private async Task<bool> TryEnterOperationAsync(string busyMessage)
    {
        if (await _operationGate.WaitAsync(0))
        {
            return true;
        }
        RestorePointStatus = busyMessage;
        return false;
    }

    private CancellationTokenSource BeginOperation()
    {
        _operationCancellation = new CancellationTokenSource();
        IsOperationBusy = true;
        CancelCurrentOperationFromUiCommand.NotifyCanExecuteChanged();
        return _operationCancellation;
    }

    private void EndOperation()
    {
        _operationCancellation = null;
        IsOperationBusy = false;
        CancelCurrentOperationFromUiCommand.NotifyCanExecuteChanged();
        _operationGate.Release();
    }

    private void LoadStartupReconciliationState()
    {
        var result = _startupReconciliation?.LastResult;
        _recoveryAttention = result?.Results.FirstOrDefault(item =>
            item.ManualInterventionRequired || item.Changed is null);
        RecoveryAttentionText = _recoveryAttention is null
            ? string.Empty
            : $"检测到未能权威判定的创建记录：{_recoveryAttention.Message}";
    }

    [RelayCommand]
    private async Task RecheckRecoveryEvidenceAsync()
    {
        if (_recoveryAttention?.Receipt is not { } receipt)
        {
            RestorePointStatus = "当前没有可重新核验的持久化创建凭据。";
            return;
        }
        var result = await _owner.ReconcileInterruptedCreateAsync(receipt.PlanId);
        _recoveryAttention = result.ManualInterventionRequired || result.Changed is null ? result : null;
        RecoveryAttentionText = _recoveryAttention is null
            ? ""
            : $"重新核验后仍需人工处理：{result.Message}";
        RestorePointStatus = result.Success
            ? "中断记录已通过权威回读核验。"
            : result.Message;
        await RefreshOwnerStateAsync(showToast: false);
    }

    [RelayCommand]
    private async Task AcknowledgeUnknownRecoveryAsync()
    {
        if (_recoveryAttention?.Receipt is not { ClaimId: { } claimId } receipt)
        {
            RestorePointStatus = "当前凭据缺少可验证的计划/claim 身份，不能确认处置。";
            return;
        }
        var result = await _owner.ResolveInterruptedCreateAsync(new(
            receipt.PlanId,
            claimId,
            SystemRestorePointManualResolutionDisposition.AcknowledgeUnknown,
            "User acknowledged that the mutation remains unknown after reviewing Windows System Protection."));
        RecoveryAttentionText = $"已记录人工知情确认；系统变化仍保持 Unknown：{result.Message}";
        RestorePointStatus = result.Message;
        PageState = RestorePointPageState.InterruptedManual;
    }

    private async Task LoadSoftwareRecoverySummaryAsync()
    {
        try
        {
            var latestBaseline = await GetLatestBaselineAsync();
            var snapshotCount = await GetSnapshotCountAsync();
            HasFirstRunBaseline = latestBaseline is not null;
            RegularSnapshotCount = snapshotCount;
            SoftwareRecoverySummary = BuildSoftwareRecoverySummary(latestBaseline, snapshotCount);
            SoftwareRecoveryDescription = BuildSoftwareRecoveryDescription(latestBaseline, snapshotCount, SystemRestoreStatus);
        }
        catch (Exception ex)
        {
            HasFirstRunBaseline = false;
            RegularSnapshotCount = 0;
            SoftwareRecoverySummary = "软件级恢复状态暂时无法读取";
            SoftwareRecoveryDescription = $"备份中心状态获取失败：{ex.Message}";
        }
    }

    private async Task<BaselineCatalogEntry?> GetLatestBaselineAsync()
    {
        if (_baselineCatalogService is null)
        {
            return null;
        }
        return (await _baselineCatalogService.GetEntriesAsync())
            .Where(entry => string.Equals(entry.PackageType, "BaselineFirstRun", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CreatedUtc)
            .FirstOrDefault();
    }

    private async Task<int> GetSnapshotCountAsync() =>
        _snapshotService is null ? 0 : (await _snapshotService.GetSnapshotsAsync()).Count;

    private static string BuildSoftwareRecoverySummary(BaselineCatalogEntry? baseline, int snapshotCount)
    {
        if (baseline is not null && snapshotCount > 0)
        {
            return $"已为本软件保留 1 份首次安全基线和 {snapshotCount} 份普通快照";
        }
        if (baseline is not null)
        {
            return "已建立首次安全基线，可恢复首次进入软件前的关键状态";
        }
        return snapshotCount > 0
            ? $"已保留 {snapshotCount} 份普通快照，可恢复本软件设置和优化结果"
            : "软件级恢复尚未建立";
    }

    private static string BuildSoftwareRecoveryDescription(
        BaselineCatalogEntry? baseline,
        int snapshotCount,
        SystemRestorePointProbe? restoreStatus)
    {
        var baselineText = baseline is null
            ? "首次安全基线还没有接入备份中心。"
            : $"首次安全基线创建于 {baseline.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}。";
        var snapshotText = snapshotCount > 0
            ? $"当前已有 {snapshotCount} 份普通快照。"
            : "当前还没有普通快照。";
        var systemText = restoreStatus?.ProtectionState switch
        {
            SystemProtectionState.Enabled when restoreStatus.RestorePoints.Count > 0 =>
                $"系统级保护已开启，最近还原点时间：{restoreStatus.RestorePoints.Max(point => point.CreatedAtUtc).ToLocalTime():yyyy-MM-dd HH:mm}。",
            SystemProtectionState.Enabled => "系统级保护已开启，但没有可用还原点。",
            SystemProtectionState.Disabled or SystemProtectionState.PolicyDisabled => "系统级保护未开启。",
            _ => "系统保护状态无法权威确认。"
        };
        return $"{baselineText}{snapshotText}{systemText} 软件级恢复请进入备份中心；系统还原执行在本应用内固定阻断。";
    }

    private string BuildCreateAvailabilityText()
    {
        if (SystemRestoreStatus is null)
        {
            return "正在检测系统还原点创建能力。";
        }
        if (CreateCapability.IsAvailable)
        {
            return RestorePoints.Count == 0
                ? "当前没有读取到还原点；所有安全条件均已满足，可以创建第一个还原点。"
                : "系统支持、保护状态、权限、冷却时间与独立 writer 均已通过，可以创建还原点。";
        }
        if (SystemRestoreStatus.NextCreationAllowedAtUtc is { } next && next > _timeProvider.GetUtcNow())
        {
            var remaining = next - _timeProvider.GetUtcNow();
            return $"Windows 创建冷却中，约剩 {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} 分钟；到期后会自动重新评估。";
        }
        return CreateCapability.Status switch
        {
            SystemRestorePointWorkflowStatus.PermissionDenied =>
                GameOptimizer.Core.Services.ElevationGate.DescribeWriteDenial("创建系统还原点"),
            SystemRestorePointWorkflowStatus.ProtectionDisabled => "系统盘保护已关闭。请打开“系统保护”人工启用后刷新。",
            SystemRestorePointWorkflowStatus.PolicyDenied when SystemRestoreStatus.ProtectionState == SystemProtectionState.PolicyDisabled =>
                "系统还原被组策略禁用。请联系管理员调整策略后刷新。",
            SystemRestorePointWorkflowStatus.PolicyDenied =>
                "缺少可终止的独立创建 writer，当前构建不能安全创建还原点。",
            SystemRestorePointWorkflowStatus.Unsupported when !SystemRestoreStatus.IsWindows =>
                "当前系统不支持 Windows 系统还原。",
            SystemRestorePointWorkflowStatus.Unsupported when !SystemRestoreStatus.IsCreationApiAvailable =>
                "Windows SRSetRestorePointW API 不可用；请修复系统组件后刷新。",
            SystemRestorePointWorkflowStatus.Unsupported => "当前 Windows 版本不支持此创建链。",
            _ => "创建条件暂无法由 Windows 权威确认；请刷新后重试，或打开“系统保护”人工核对。"
        };
    }

    private static bool ContainsUnlocalizedDiagnostic(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Any(char.IsAsciiLetter);

    private sealed class InstructionOnlyManualLauncher : ISystemRestoreManualLauncher
    {
        public SystemRestoreManualLaunchResult OpenProtectionSettings() => new(false,
            "无法自动打开系统保护。请在 Windows 搜索中输入“创建还原点”，打开后仅人工检查系统盘保护状态。");

        public SystemRestoreManualLaunchResult OpenRecoveryWizard() => new(false,
            "无法自动打开恢复向导。请在 Windows 搜索中输入“恢复”，进入“打开系统还原”，并由 Windows 完成后续确认。");
    }
}
