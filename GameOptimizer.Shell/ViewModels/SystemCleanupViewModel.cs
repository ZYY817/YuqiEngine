// File: GameOptimizer.Shell/ViewModels/SystemCleanupViewModel.cs
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Events;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.SystemCleanup;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.ViewModels;

public partial class SystemCleanupViewModel : ObservableObject, IDisposable
{
    private readonly ISystemCleanupService _cleanupService;
    private readonly IEventBus? _eventBus;
    private readonly ICleanupQuarantineManager _quarantineManager;
    private readonly IActiveOperationRegistry? _activeOperations;
    private readonly Func<string, string, bool>? _restoreConfirmation;
    private readonly Dictionary<string, CleanupPreview> _previews = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private static LocalizationService Loc => LocalizationService.Instance;

    public ObservableCollection<CleanupItem> CleanupItems { get; } = new();
    public ObservableCollection<CleanupCategoryGroup> CategoryGroups { get; } = new();

    // The category strip is a real filter, not a decorative row of labels.
    // "All" is intentionally the single source of truth for the unfiltered state.
    [ObservableProperty] private string _selectedCleanupCategory = "All";
    public IEnumerable<CleanupItem> FilteredCleanupItems => SelectedCleanupCategory == "All"
        ? CleanupItems
        : CleanupItems.Where(item => string.Equals(item.Category.ToString(), SelectedCleanupCategory, StringComparison.Ordinal));

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private bool _isRestoring;
    [ObservableProperty] private bool _hasScanned;
    [ObservableProperty] private int _scanProgress;
    [ObservableProperty] private int _cleanProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _currentItemName = string.Empty;
    [ObservableProperty] private int _skippedDirectoryCount;
    [ObservableProperty] private int _skippedFileCount;
    [ObservableProperty] private int _partialPreviewCount;
    
    [ObservableProperty] private long _diskTotal;
    [ObservableProperty] private long _diskUsed;
    [ObservableProperty] private long _diskFree;
    [ObservableProperty] private double _diskUsagePercent;

    public bool IsWorking => IsScanning || IsCleaning || IsRestoring;
    public bool CanScan => !IsWorking;
    public bool CanClean => !IsWorking && HasScanned && SelectedCount > 0;

    public long TotalSize => CleanupItems.Where(i => i.IsSelected).Sum(i => i.Size);
    public string TotalSizeText => FormatSize(TotalSize);
    public int SelectedCount => CleanupItems.Count(i => i.IsSelected);
    public int TotalFileCount => CleanupItems.Where(i => i.IsSelected).Sum(i => i.FileCount);
    
    public string DiskTotalText => FormatSize(DiskTotal);
    public string DiskUsedText => FormatSize(DiskUsed);
    public string DiskFreeText => FormatSize(DiskFree);

    public string MainButtonIcon => IsScanning ? "\uE895" : IsCleaning ? "\uE74D" : HasScanned ? "\uE74D" : "\uE721";
    public string MainButtonText
    {
        get
        {
            if (IsScanning) return Loc.Get("Cleanup.Scanning");
            if (IsCleaning) return Loc.Get("Cleanup.CleaningStatus");
            if (HasScanned) return Loc.Get("Cleanup.CleanNow");
            return Loc.Get("Cleanup.OneClickScan");
        }
    }
    public string ProgressText => IsScanning ? $"{ScanProgress}%" : IsCleaning ? $"{CleanProgress}%" : "";
    public int ScannerProgressPercent => IsScanning ? ScanProgress : IsCleaning ? CleanProgress : HasScanned ? 100 : 0;
    public Geometry ScannerProgressArc => CreateScannerProgressArc(ScannerProgressPercent);

    public string VisualStateLabel => IsScanning ? Loc.Get("Cleanup.Scanning") : IsCleaning ? Loc.Get("Cleanup.CleaningStatus") : HasScanned ? Loc.Get("Cleanup.Recoverable") : Loc.Get("Cleanup.Title");
    public string VisualPrimaryValue => IsScanning ? ScanProgress.ToString() : IsCleaning ? CleanProgress.ToString() : HasScanned ? TotalSizeText : Loc.Get("Cleanup.StartScan");
    public string VisualPrimaryUnit => IsScanning || IsCleaning ? "%" : HasScanned ? string.Empty : "";
    // The orb mirrors the legacy Web state machine but clips long names in the view.
    // Keeping the actual catalog item here makes scanning visibly tied to real work.
    public string VisualStateHint => IsScanning
        ? string.IsNullOrWhiteSpace(CurrentItemName) ? Loc.Get("Cleanup.Preparing") : CurrentItemName
        : IsCleaning
            ? string.IsNullOrWhiteSpace(CurrentItemName) ? Loc.Get("Cleanup.Processing") : CurrentItemName
            : HasScanned
                ? Loc.Get("Cleanup.FilesClickClean", TotalFileCount)
                : Loc.Get("Cleanup.ScanHint");

    partial void OnIsScanningChanged(bool value) => NotifyVisualStateChanged();
    partial void OnIsCleaningChanged(bool value) => NotifyVisualStateChanged();
    partial void OnIsRestoringChanged(bool value) => NotifyVisualStateChanged();
    partial void OnHasScannedChanged(bool value) => NotifyVisualStateChanged();
    partial void OnScanProgressChanged(int value) => NotifyVisualStateChanged();
    partial void OnCleanProgressChanged(int value) => NotifyVisualStateChanged();
    partial void OnCurrentItemNameChanged(string value) => NotifyVisualStateChanged();

    private void NotifyVisualStateChanged()
    {
        OnPropertyChanged(nameof(VisualStateLabel));
        OnPropertyChanged(nameof(VisualPrimaryValue));
        OnPropertyChanged(nameof(VisualPrimaryUnit));
        OnPropertyChanged(nameof(VisualStateHint));
        OnPropertyChanged(nameof(ScannerProgressPercent));
        OnPropertyChanged(nameof(ScannerProgressArc));
    }

    private static Geometry CreateScannerProgressArc(int progress)
    {
        progress = Math.Clamp(progress, 0, 100);
        const double center = 112;
        const double radius = 101;
        if (progress == 0) return Geometry.Empty;
        if (progress == 100) return new EllipseGeometry(new Point(center, center), radius, radius);

        const double radiansPerDegree = Math.PI / 180d;
        double endAngle = (-90d + progress * 3.6d) * radiansPerDegree;
        var end = new Point(center + radius * Math.Cos(endAngle), center + radius * Math.Sin(endAngle));
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, progress > 50, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }

    partial void OnSelectedCleanupCategoryChanged(string value) =>
        OnPropertyChanged(nameof(FilteredCleanupItems));

    public SystemCleanupViewModel(
        ISystemCleanupService cleanupService,
        IEventBus? eventBus = null,
        ICleanupQuarantineManager? quarantineManager = null,
        IActiveOperationRegistry? activeOperations = null,
        Func<string, string, bool>? restoreConfirmation = null)
    {
        _cleanupService = cleanupService;
        _eventBus = eventBus;
        _quarantineManager = quarantineManager ?? new CleanupQuarantineManager();
        _activeOperations = activeOperations;
        _restoreConfirmation = restoreConfirmation;
        LoadDiskInfo();
        
        IReadOnlyList<CleanupItem> catalog = _cleanupService.GetCleanupItems();
        // A blank catalog makes the page unusable even though the built-in, allowlisted
        // cleanup definitions are available. Fall back only to that same production catalog;
        // this never invents files, expands the allowlist, or bypasses preview/confirmation.
        if (catalog.Count == 0)
        {
            Debug.WriteLine("[SystemCleanupVM] Injected cleanup catalog was empty; using the built-in allowlist catalog.");
            catalog = new SystemCleanupService().GetCleanupItems();
        }

        foreach (var item in catalog)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CleanupItem.IsSelected))
                {
                    OnPropertyChanged(nameof(TotalSize));
                    OnPropertyChanged(nameof(TotalSizeText));
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(TotalFileCount));
                    OnPropertyChanged(nameof(CanClean));
                    NotifyVisualStateChanged();
                    UpdateCategoryGroups();
                }
            };
            CleanupItems.Add(item);
        }

        BuildCategoryGroups();
        StatusText = CleanupItems.Count > 0
            ? Loc.Get("Cleanup.ClickToScan")
            : "未加载到清理目录，已阻止执行。";
    }
    
    private void LoadDiskInfo()
    {
        try
        {
            var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
            DiskTotal = systemDrive.TotalSize;
            DiskFree = systemDrive.AvailableFreeSpace;
            DiskUsed = DiskTotal - DiskFree;
            DiskUsagePercent = (double)DiskUsed / DiskTotal * 100;
            
            OnPropertyChanged(nameof(DiskTotalText));
            OnPropertyChanged(nameof(DiskUsedText));
            OnPropertyChanged(nameof(DiskFreeText));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemCleanupVM] Failed to update disk info: {ex.Message}");
        }
    }
    
    private void BuildCategoryGroups()
    {
        CategoryGroups.Clear();
        var groups = CleanupItems.GroupBy(i => i.Category);
        foreach (var g in groups)
        {
            CategoryGroups.Add(new CleanupCategoryGroup
            {
                Category = g.Key,
                Name = g.Key switch
                {
                    CleanupCategory.System => Loc.Get("Cleanup.CategorySystem"),
                    CleanupCategory.User => Loc.Get("Cleanup.CategoryUser"),
                    CleanupCategory.Game => Loc.Get("Cleanup.CategoryGame"),
                    CleanupCategory.Browser => Loc.Get("Cleanup.CategoryBrowser"),
                    _ => Loc.Get("Cleanup.CategoryOther")
                },
                Items = new ObservableCollection<CleanupItem>(g)
            });
        }
    }
    
    private void UpdateCategoryGroups()
    {
        foreach (var group in CategoryGroups)
        {
            group.NotifyChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        PageDiagnosticService.Log("SystemCleanup", $"Scan requested for {CleanupItems.Count} allowlisted cleanup categories.");
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsScanning = true;
        HasScanned = false;
        InvalidatePreviews();
        _previews.Clear();
        ScanProgress = 0;
        SkippedDirectoryCount = 0;
        SkippedFileCount = 0;
        PartialPreviewCount = 0;
        StatusText = Loc.Get("Cleanup.ScanningStatus");
        NotifyStateChanged();

        int current = 0;
        int total = CleanupItems.Count;
        bool cancelled = false;

        try
        {
            foreach (var item in CleanupItems)
            {
                token.ThrowIfCancellationRequested();

                item.State = CleanupState.Scanning;
                item.Size = 0;
                item.FileCount = 0;
                CurrentItemName = item.Name;
                StatusText = $"正在扫描 ({current + 1}/{total})：{item.Name}。扫描只统计可清理文件，不会删除任何内容。";

                try
                {
                    int itemIndex = current + 1;
                    IProgress<CleanupScanProgress> progress = new Progress<CleanupScanProgress>(scan =>
                    {
                        if (!IsScanning || !string.Equals(scan.ItemId, item.Id, StringComparison.Ordinal))
                        {
                            return;
                        }

                        StatusText = $"正在扫描 ({itemIndex}/{total})：{item.Name} · 已发现 {scan.FilesDiscovered:N0} 个文件 · 已用时 {Math.Max(1, (int)Math.Ceiling(scan.Elapsed.TotalSeconds))} 秒。可随时取消。";
                    });
                    CleanupPreview preview = await _cleanupService.CreatePreviewAsync(item, progress, token);
                    _previews[item.Id] = preview;
                    item.Size = preview.TotalBytes;
                    item.FileCount = preview.Entries.Count;
                    SkippedDirectoryCount += preview.SkippedDirectoryCount;
                    SkippedFileCount += preview.SkippedFileCount;
                    if (preview.IsPartial)
                    {
                        PartialPreviewCount++;
                        item.ErrorMessage = $"已快速结束扫描：{preview.PartialReason} 已找到的 {preview.Entries.Count:N0} 个文件可安全清理；未扫描部分不会被处理。";
                    }
                    else
                    {
                        item.ErrorMessage = string.Empty;
                    }
                    item.State = CleanupState.Idle;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    item.State = CleanupState.Idle;
                    throw;
                }
                catch (Exception ex)
                {
                    item.State = CleanupState.Error;
                    item.ErrorMessage = ex.Message;
                    PageDiagnosticService.Log("SystemCleanup", $"Preview failed for {item.Id}: {ex.GetType().Name}: {ex.Message}");
                }

                current++;
                ScanProgress = total == 0 ? 100 : (int)((double)current / total * 100);
                await Task.Delay(120, token);
            }

            HasScanned = _previews.Count == CleanupItems.Count;
            StatusText = Loc.Get("Cleanup.ScanComplete", TotalFileCount, TotalSizeText);
            if (SkippedDirectoryCount > 0 || SkippedFileCount > 0)
                StatusText += $"；已跳过 {SkippedDirectoryCount} 个无权限或不可访问目录、{SkippedFileCount} 个不可读取文件。";
            if (PartialPreviewCount > 0)
                StatusText += $"；{PartialPreviewCount} 个目录为避免卡顿已快速结束，未扫描部分不会被清理。";
            PageDiagnosticService.Log("SystemCleanup", $"Scan completed: {TotalFileCount} files, {TotalSizeText}, previews={_previews.Count}, skippedDirectories={SkippedDirectoryCount}, skippedFiles={SkippedFileCount}, partialPreviews={PartialPreviewCount}.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            cancelled = true;
            HasScanned = false;
            StatusText = Loc.Get("Cleanup.Cancelled");
            PageDiagnosticService.Log("SystemCleanup", $"Scan cancelled after {current}/{total} categories; partial previews invalidated.");
        }
        finally
        {
            if (cancelled)
            {
                InvalidatePreviews();
                _previews.Clear();
                ScanProgress = 0;
            }

            foreach (var item in CleanupItems.Where(item => item.State == CleanupState.Scanning))
                item.State = CleanupState.Idle;

            IsScanning = false;
            CurrentItemName = string.Empty;
            _cts?.Dispose();
            _cts = null;
            NotifyStateChanged();
            OnPropertyChanged(nameof(TotalSize));
            OnPropertyChanged(nameof(TotalSizeText));
            OnPropertyChanged(nameof(TotalFileCount));
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        // 统一 Elevation 门禁：未提权才阻断并给出可操作指引；已提权永不提示「以管理员身份运行」。
        var elevation = GameOptimizer.Core.Services.ElevationGate.Evaluate(
            "系统清理",
            operationRequiresAdministrator: true);
        if (elevation.NeedsElevation)
        {
            StatusText = GameOptimizer.Core.Services.ElevationGate.ShortLabel(elevation.Attribution);
            var message = elevation.UserMessage;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Views.InfoDialog.Show(Application.Current.MainWindow,
                    Loc.Get("Common.AdminRequired"),
                    message,
                    Constants.IconGlyphs.Lock);
            });
            return;
        }
        
        var selectedItems = CleanupItems.Where(i => i.IsSelected && i.Size > 0).ToList();
        if (selectedItems.Count == 0)
        {
            ToastService.Instance.Warning(Loc.Get("Activity.SystemCleanup"), Loc.Get("Cleanup.NoItemsToClean"));
            return;
        }
        
        if (selectedItems.Any(item => !_previews.ContainsKey(item.Id)))
        {
            HasScanned = false;
            StatusText = "清理预览已失效，请重新扫描。";
            NotifyStateChanged();
            return;
        }

        IReadOnlyDictionary<string, CleanupPreviewConfirmation>? confirmations =
            Views.CleanupConfirmDialog.Show(
                Application.Current.MainWindow,
                selectedItems,
                _previews);
        if (confirmations is null) return;

        _cts = new CancellationTokenSource();
        using var activeLease = _activeOperations?.Begin(
            "system-cleanup.clean",
            "系统清理",
            ActiveOperationRisk.Mutating,
            ActiveOperationClosePolicy.CancelAndDrain,
            () => _cts?.Cancel());
        IsCleaning = true;
        CleanProgress = 0;
        StatusText = Loc.Get("Cleanup.CleaningStatus");
        NotifyStateChanged();

        IProgress<(int current, int total, string name)> progress = new Progress<(int current, int total, string name)>(p =>
        {
            CleanProgress = (int)((double)p.current / p.total * 100);
            CurrentItemName = p.name;
            StatusText = Loc.Get("Cleanup.Cleaning", p.current, p.total, p.name);
        });

        try
        {
            var result = new CleanupResult();
            for (int index = 0; index < selectedItems.Count; index++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                CleanupItem item = selectedItems[index];
                progress.Report((index + 1, selectedItems.Count, item.Name));
                CleanupResult current = await _cleanupService.CleanPreviewAsync(
                    item,
                    _previews[item.Id],
                    confirmations[item.Id],
                    _cts.Token);
                result.TotalCleaned += current.TotalCleaned;
                result.FilesDeleted += current.FilesDeleted;
                result.FilesFailed += current.FilesFailed;
                result.Errors.AddRange(current.Errors);
                result.FileResults.AddRange(current.FileResults);
                bool itemWasCancelled = current.FileResults.Any(IsCancellationOutcome);
                item.State = current.Errors.Count == 0 && !itemWasCancelled
                    ? CleanupState.Done
                    : CleanupState.Error;
                if (itemWasCancelled)
                    item.ErrorMessage = "清理已取消；未完成的文件未被删除，已隔离的文件可恢复。";
            }

            bool wasCancelled = result.FileResults.Any(IsCancellationOutcome);
            if (wasCancelled)
            {
                StatusText = result.FilesDeleted > 0
                    ? $"清理已中断：已安全隔离 {result.FilesDeleted} 个文件（{FormatSize(result.TotalCleaned)}）；未完成文件未被删除，可通过“恢复隔离文件”还原已隔离内容。"
                    : "清理已取消；未完成文件未被删除。";
                ToastService.Instance.Warning(Loc.Get("Activity.SystemCleanup"), StatusText);
            }
            else
            {
                StatusText = Loc.Get("Cleanup.Complete", result.FilesDeleted, FormatSize(result.TotalCleaned));
                if (result.FilesFailed > 0)
                    StatusText += Loc.Get("Cleanup.FailedCount", result.FilesFailed);

                if (result.Errors.Count == 0)
                    ToastService.Instance.Success(Loc.Get("Activity.SystemCleanup"), Loc.Get("Cleanup.Released", FormatSize(result.TotalCleaned)));
                else
                    ToastService.Instance.Warning(Loc.Get("Activity.SystemCleanup"), StatusText);
            }
            if (result.FilesDeleted > 0)
                ActivityService.Instance.LogCleanup(result.FilesDeleted, result.TotalCleaned);
            
            // VM-022: 发布清理完成事件，通知仪表盘刷新磁盘使用
            if (_eventBus != null && result.FilesDeleted > 0)
            {
                Debug.WriteLine($"[SystemCleanupVM] Publishing CleanupCompletedEvent: {result.TotalCleaned} bytes, {result.FilesDeleted} files");
                await _eventBus.PublishAsync(new CleanupCompletedEvent(result.TotalCleaned, result.FilesDeleted));
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.Get("Cleanup.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = Loc.Get("Cleanup.Failed", ex.Message);
            ToastService.Instance.Error(Loc.Get("Activity.SystemCleanup"), Loc.Get("Cleanup.Error"));
        }
        finally
        {
            IsCleaning = false;
            HasScanned = false;
            InvalidatePreviews();
            _previews.Clear();
            CurrentItemName = string.Empty;
            NotifyStateChanged();

            OnPropertyChanged(nameof(TotalSize));
            OnPropertyChanged(nameof(TotalSizeText));
            OnPropertyChanged(nameof(TotalFileCount));
            
            LoadDiskInfo();
        }
    }

    [RelayCommand]
    private async Task MainActionAsync()
    {
        if (IsWorking) return;
        
        if (HasScanned && SelectedCount > 0)
            await CleanAsync();
        else
            await ScanAsync();
    }

    /// <summary>Explicit page entry point for the scanner hit target.</summary>
    public Task RunScannerActionAsync() => MainActionAsync();

    [RelayCommand]
    private void Cancel()
    {
        // Capture before signalling cancellation: Task.Delay and similar awaited work may
        // complete its cancellation continuation synchronously and clear IsRestoring before
        // this method resumes. The recovery message must not be overwritten in that race.
        var wasRestoring = IsRestoring;
        _cts?.Cancel();
        if (wasRestoring)
        {
            // A restore can already have moved a quarantined item when the user
            // cancels.  Tell the user about the durable reconciliation boundary
            // immediately instead of briefly presenting this as an ordinary
            // cleanup cancellation while the async operation drains.
            StatusText = "正在取消隔离恢复；恢复清单将在操作结束后重新核对。";
            return;
        }

        StatusText = IsScanning ? Loc.Get("Cleanup.CancellingScan") : Loc.Get("Cleanup.CancellingClean");
        
        foreach (var item in CleanupItems)
        {
            if (item.State == CleanupState.Scanning || item.State == CleanupState.Cleaning)
                item.State = CleanupState.Idle;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in CleanupItems)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in CleanupItems)
            item.IsSelected = false;
    }
    
    [RelayCommand]
    private void SelectSafeOnly()
    {
        foreach (var item in CleanupItems)
            item.IsSelected = item.Risk == RiskLevel.Safe;
    }

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        SelectedCleanupCategory = category is "System" or "User" or "Game" or "Browser"
            ? category
            : "All";
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task RestoreQuarantineAsync()
    {
        CleanupQuarantinePreview preview;
        try
        {
            preview = await _quarantineManager.CreateRestorePreviewAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"读取隔离清单失败：{ex.Message}";
            return;
        }

        if (preview.Entries.Count == 0)
        {
            StatusText = "当前没有可恢复的隔离文件。";
            return;
        }

        const string title = "恢复隔离文件";
        string message = $"将按原路径恢复 {preview.Entries.Count} 个文件（{FormatSize(preview.TotalBytes)}）。已有同名文件时会安全阻断，不会覆盖。";
        bool confirmed = _restoreConfirmation?.Invoke(title, message) ?? Views.ConfirmDialog.Show(
            Application.Current.MainWindow, title, message, Constants.IconGlyphs.Restore);
        if (!confirmed) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        using var activeLease = _activeOperations?.Begin(
            "system-cleanup.restore-quarantine",
            "恢复清理隔离文件",
            ActiveOperationRisk.Mutating,
            ActiveOperationClosePolicy.CancelAndDrain,
            () => _cts?.Cancel());
        IsRestoring = true;
        StatusText = "正在恢复隔离文件…";
        NotifyStateChanged();
        try
        {
            CleanupQuarantineConfirmation confirmation = CleanupQuarantineConfirmation.Create(
                preview, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
            CleanupQuarantineResult result = await _quarantineManager.RestoreAsync(preview, confirmation, token);
            StatusText = $"隔离恢复完成：成功 {result.Succeeded} 项，阻断或失败 {result.Failed} 项。";
            if (result.Failed == 0)
                ToastService.Instance.Success("系统清理", StatusText);
            else
                ToastService.Instance.Warning("系统清理", StatusText);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusText = "隔离恢复已中断；未确认完成的项目将由恢复清单在下次操作时重新核对。";
            activeLease?.UpdateHandoff(new ActiveOperationHandoff([], StatusText));
        }
        catch (Exception ex)
        {
            StatusText = $"隔离恢复失败：{ex.Message}";
            ToastService.Instance.Error("系统清理", StatusText);
        }
        finally
        {
            IsRestoring = false;
            _cts?.Dispose();
            _cts = null;
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(MainButtonIcon));
        OnPropertyChanged(nameof(MainButtonText));
        OnPropertyChanged(nameof(ProgressText));
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        RestoreQuarantineCommand.NotifyCanExecuteChanged();
    }
    
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // 取消订阅所有 CleanupItem 的 PropertyChanged 事件
        foreach (var item in CleanupItems)
        {
            item.PropertyChanged -= (_, e) =>
            {
                if (e.PropertyName == nameof(CleanupItem.IsSelected))
                {
                    OnPropertyChanged(nameof(TotalSize));
                    OnPropertyChanged(nameof(TotalSizeText));
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(TotalFileCount));
                    OnPropertyChanged(nameof(CanClean));
                    UpdateCategoryGroups();
                }
            };
        }
        
        _cts?.Cancel();
        InvalidatePreviews();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }

    private void InvalidatePreviews()
    {
        foreach (CleanupPreview preview in _previews.Values)
        {
            _cleanupService.InvalidatePreview(preview);
        }
    }

    private static bool IsCancellationOutcome(CleanupFileResult result) =>
        result.Outcome is CleanupFileOutcome.Cancelled or CleanupFileOutcome.CancelledAfterPartialQuarantine;

    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return LocalizationService.Instance.Get("Cleanup.Unknown");
        if (bytes == 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F2} {units[i]}";
    }
}

public class CleanupCategoryGroup : ObservableObject
{
    public CleanupCategory Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public ObservableCollection<CleanupItem> Items { get; set; } = new();
    
    public int SelectedCount => Items.Count(i => i.IsSelected);
    public long TotalSize => Items.Where(i => i.IsSelected).Sum(i => i.Size);
    
    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(TotalSize));
    }
}
