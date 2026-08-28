// File: GameOptimizer.Shell/ViewModels/AppxCleanupViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Services.Appx;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.ViewModels;

public enum AppxCleanupPageState
{
    Loading,
    Empty,
    Ready,
    Error,
    Unauthorized
}

/// <summary>列表条目：RecoveryClass 决定恢复徽章（StagedRegister=绿/StoreOnly=红），Removable=false 时禁勾。</summary>
public partial class AppxCleanupItemViewModel : ObservableObject
{
    public AppxCleanupItemViewModel(AppxCatalogEntry entry)
    {
        Entry = entry;
    }

    public AppxCatalogEntry Entry { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Entry.DisplayName) ? Entry.FamilyName : Entry.DisplayName;
    public string Publisher => Entry.PublisherDisplayName;
    public string Version => Entry.Version;
    public string FamilyName => Entry.FamilyName;
    public bool Removable => Entry.Removable;
    public string NotRemovableReason => Entry.NotRemovableReason;

    /// <summary>恢复徽章语义：StagedRegister=可自动恢复（绿）；StoreOnly=仅 Store 重装（红，卸载前需二次强调）。</summary>
    public bool IsStagedRegister => Entry.RecoveryClass == AppxRecoveryClass.StagedRegister;
    public bool IsStoreOnly => !IsStagedRegister;
    public string RecoveryBadgeText => IsStagedRegister ? "可自动恢复" : "仅 Store 重装";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        // 拒绝项（受保护/收件箱）不可勾选：任何外部赋值都会被强制回退。
        if (value && !Removable)
            IsSelected = false;
    }
}

/// <summary>确认请求明细：逐项标注恢复能力，StoreOnly 类额外强调无法自动恢复。</summary>
public sealed record AppxCleanupConfirmationRequest(
    string Title,
    string Message,
    IReadOnlyList<string> Consequences);

public partial class AppxCleanupViewModel : ObservableObject
{
    private readonly IAppxRemovalOwner _owner;
    private readonly IAppxUserRemovalService _removalService;
    private readonly Func<AppxCleanupConfirmationRequest, bool>? _confirmationPrompt;
    private readonly IActiveOperationRegistry? _activeOperations;
    private CancellationTokenSource? _cts;

    public AppxCleanupViewModel(
        IAppxRemovalOwner owner,
        IAppxUserRemovalService removalService,
        Func<AppxCleanupConfirmationRequest, bool>? confirmationPrompt = null,
        IActiveOperationRegistry? activeOperations = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _removalService = removalService ?? throw new ArgumentNullException(nameof(removalService));
        _confirmationPrompt = confirmationPrompt;
        _activeOperations = activeOperations;
    }

    public ObservableCollection<AppxCleanupItemViewModel> Packages { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private AppxCleanupPageState _pageState = AppxCleanupPageState.Loading;

    [ObservableProperty]
    private bool _isRemoving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isEnumerating;

    [ObservableProperty]
    private string _statusText = "正在枚举本机已安装的预装应用…";

    [ObservableProperty]
    private string _currentItemName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveButtonText))]
    private int _progressCurrent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveButtonText))]
    private int _progressTotal;

    public bool IsEmpty => PageState == AppxCleanupPageState.Empty;
    public bool IsLoading => PageState == AppxCleanupPageState.Loading;
    public bool IsReady => PageState == AppxCleanupPageState.Ready;
    public bool IsError => PageState == AppxCleanupPageState.Error;
    public bool IsUnauthorized => PageState == AppxCleanupPageState.Unauthorized;
    public bool IsBusy => IsEnumerating || IsRemoving;
    public bool HasSelection => SelectedCount > 0;

    /// <summary>禁勾项不参与统计与按钮可用性。</summary>
    public int SelectedCount => Packages.Count(item => item.IsSelected && item.Removable);
    public int RemovableCount => Packages.Count(item => item.Removable);
    public bool HasStoreOnlySelected => Packages.Any(item => item.IsSelected && item.IsStoreOnly);

    public string RemoveButtonText => IsRemoving
        ? $"正在卸载 {ProgressCurrent}/{ProgressTotal}…"
        : $"卸载所选({SelectedCount})";

    public bool CanRemove => !IsRemoving && PageState == AppxCleanupPageState.Ready && HasSelection;

    /// <summary>页面首次加载入口；由 View 的 Loaded 触发，也可被测试直接调用。已加载过则跳过。</summary>
    public async Task InitializeAsync()
    {
        if (Packages.Count > 0 || PageState is not AppxCleanupPageState.Loading)
            return;
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    public async Task LoadAsync()
    {
        if (!CanLoad())
            return;

        var previousState = PageState;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsEnumerating = true;
        SetBusyState("正在枚举本机已安装的预装应用…");
        try
        {
            // Why: WindowsAppxRemovalOwner 的 Probe/Enumerate 是同步方法体，
            // 直接在 UI 线程调用会冻结界面（首次 WinRT 目录查询可能数秒）。
            var probe = await Task.Run(() => _owner.ProbeAsync(token), token).ConfigureAwait(true);
            if (!probe.HasRequiredPermission)
            {
                PageState = AppxCleanupPageState.Unauthorized;
                StatusText = probe.ReasonCode == "permission.administrator-required"
                    ? "需要以管理员身份运行本软件后才能管理预装应用。"
                    : probe.Details;
                return;
            }

            var entries = await Task.Run(() => _owner.EnumerateInstalledAsync(token), token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            ReplacePackages(entries);

            PageState = Packages.Count == 0
                ? AppxCleanupPageState.Empty
                : AppxCleanupPageState.Ready;
            StatusText = $"共 {Packages.Count} 个应用（可移除 {RemovableCount} 个）。请仔细确认后再勾选卸载。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            PageState = previousState == AppxCleanupPageState.Loading
                ? AppxCleanupPageState.Empty
                : previousState;
            StatusText = "已取消应用列表枚举；未执行任何卸载操作。";
        }
        catch (Exception ex)
        {
            PageState = AppxCleanupPageState.Error;
            StatusText = $"读取本机应用列表失败：{ex.Message}";
        }
        finally
        {
            IsEnumerating = false;
            _cts?.Dispose();
            _cts = null;
            NotifyStateChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    public async Task UninstallSelectedAsync()
    {
        var selected = Packages.Where(item => item.IsSelected && item.Removable).Select(item => item.Entry).ToList();
        if (selected.Count == 0 || IsRemoving || PageState != AppxCleanupPageState.Ready)
            return;

        var confirmed = RequestConfirmation(selected);
        if (!confirmed)
            return;

        _cts = new CancellationTokenSource();
        using var activeLease = _activeOperations?.Begin(
            "appx-cleanup.remove",
            "预装应用卸载",
            ActiveOperationRisk.Mutating,
            ActiveOperationClosePolicy.CancelAndDrain,
            () => _cts?.Cancel());
        IsRemoving = true;
        ProgressTotal = selected.Count;
        ProgressCurrent = 0;
        NotifyStateChanged();

        try
        {
            var progress = new Action<int, int, string>((current, total, name) =>
            {
                ProgressCurrent = current;
                CurrentItemName = name;
                StatusText = $"正在卸载（{current}/{total}）：{name}";
            });

            var result = await _removalService.RemoveAsync(selected, progress, _cts.Token).ConfigureAwait(true);
            if (result.Interrupted && result.Outcomes.Count > 0)
            {
                activeLease?.UpdateHandoff(new ActiveOperationHandoff(
                    result.Outcomes.Select(item => item.FamilyName).ToArray(),
                    "批量卸载已中断；请按刷新后的真实列表核对已完成项。"));
            }
            StatusText = BuildResultSummary(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消：剩余项未执行，已完成项保留。列表将刷新为当前真实状态。";
        }
        catch (Exception ex)
        {
            StatusText = $"批量卸载失败：{ex.Message}";
        }
        finally
        {
            IsRemoving = false;
            CurrentItemName = string.Empty;
            await RefreshListKeepingStatusAsync().ConfigureAwait(true);
            _cts?.Dispose();
            _cts = null;
            NotifyStateChanged();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanLoad() => !IsRemoving && !IsEnumerating;

    private void SetBusyState(string status)
    {
        PageState = AppxCleanupPageState.Loading;
        StatusText = status;
        NotifyStateChanged();
    }

    private void ReplacePackages(IReadOnlyList<AppxCatalogEntry> entries)
    {
        foreach (var item in Packages)
            item.PropertyChanged -= OnItemPropertyChanged;
        Packages.Clear();

        foreach (var entry in entries.OrderByDescending(item => item.Removable).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var item = new AppxCleanupItemViewModel(entry);
            item.PropertyChanged += OnItemPropertyChanged;
            Packages.Add(item);
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppxCleanupItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasStoreOnlySelected));
            OnPropertyChanged(nameof(RemoveButtonText));
            UninstallSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>执行完成后只刷新列表、不改写状态文本（结果摘要需要保留展示）。</summary>
    private async Task RefreshListKeepingStatusAsync()
    {
        try
        {
            var entries = await Task.Run(() => _owner.EnumerateInstalledAsync(CancellationToken.None)).ConfigureAwait(true);
            ReplacePackages(entries);
            if (PageState != AppxCleanupPageState.Error && PageState != AppxCleanupPageState.Unauthorized)
            {
                PageState = Packages.Count == 0 ? AppxCleanupPageState.Empty : AppxCleanupPageState.Ready;
            }
        }
        catch (Exception ex)
        {
            StatusText += $"（刷新列表失败：{ex.Message}）";
        }
    }

    private static string BuildResultSummary(AppxUserRemovalBatchResult result)
    {
        var removedNow = result.Outcomes.Count(item => item.Success && item.CompensationSucceeded != true);
        var failed = result.Outcomes.Count(item => !item.Success);
        var restored = result.Outcomes.Count(item => item.CompensationSucceeded == true);
        var manualOnly = result.Outcomes.Count(item => item.Success && item.CompensationSucceeded == false);

        var summary = result.Interrupted
            ? $"操作已中断：当前仍移除 {removedNow} 项"
            : $"卸载完成：成功 {removedNow} 项";
        if (failed > 0)
            summary += $"，失败/阻断 {failed} 项";
        if (restored > 0)
            summary += $"；因部分失败已自动恢复 {restored} 项";
        if (manualOnly > 0)
            summary += $"；其中 {manualOnly} 项无法自动恢复，如需还原请从 Microsoft Store 手动重装";
        if (result.Interrupted)
        {
            summary += "。未执行项保持不变";
            if (failed > 0)
                summary += $"；首个失败原因：{result.Outcomes.First(item => !item.Success).ReasonCode}";
        }
        return summary;
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(RemovableCount));
        OnPropertyChanged(nameof(HasStoreOnlySelected));
        OnPropertyChanged(nameof(RemoveButtonText));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsEmpty));
        UninstallSelectedCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRemovingChanged(bool value) => NotifyStateChanged();
    partial void OnIsEnumeratingChanged(bool value) => NotifyStateChanged();

    /// <summary>
    /// 显式确认门禁：未注入确认回调（纯 VM/测试环境）时 fail-closed 拒绝执行；
    /// StoreOnly 类包在明细中逐项强调"无法自动恢复"。
    /// </summary>
    private bool RequestConfirmation(IReadOnlyList<AppxCatalogEntry> selected)
    {
        if (_confirmationPrompt is null)
            return false;

        var consequences = selected.Select(item =>
        {
            var recovery = item.RecoveryClass == AppxRecoveryClass.StagedRegister
                ? "卸载后可从本机 Staged 载荷自动恢复"
                : "无法自动恢复！如需还原只能从 Microsoft Store 手动重装";
            return $"{item.DisplayName}（{item.PublisherDisplayName}）：{recovery}";
        }).ToList();

        var storeOnlyCount = selected.Count(item => item.RecoveryClass != AppxRecoveryClass.StagedRegister);
        var message = storeOnlyCount > 0
            ? $"即将为当前用户移除 {selected.Count} 个应用，其中 {storeOnlyCount} 个应用卸载后无法通过本机载荷自动恢复。请再次确认。"
            : $"即将为当前用户移除 {selected.Count} 个应用，移除后可从本机 Staged 载荷自动恢复。";

        return _confirmationPrompt(new AppxCleanupConfirmationRequest("AppX 卸载确认", message, consequences));
    }

    partial void OnPageStateChanged(AppxCleanupPageState value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsUnauthorized));
        NotifyStateChanged();
    }
}
