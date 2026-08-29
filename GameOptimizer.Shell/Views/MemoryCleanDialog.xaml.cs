using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.Memory;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public enum CleanMode { SmartBoost, StandbyList, WorkingSet }

public class ProcessGroupItem : INotifyPropertyChanged
{
    private bool _isSelected;
    
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public ProcessCategory Category { get; set; }
    public int InstanceCount { get; set; }
    public long TotalMemoryBytes { get; set; }
    public System.Collections.Generic.List<int> Pids { get; set; } = new();
    public bool IsResponding { get; set; } = true;
    
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }
    
    public bool CanSelect => Category != ProcessCategory.SystemCritical && Category != ProcessCategory.Whitelisted;
    public string InstanceText => InstanceCount > 1 ? $"×{InstanceCount}" : "1";
    public string MemoryText => TotalMemoryBytes >= 1024 * 1024 * 1024 
        ? $"{TotalMemoryBytes / (1024.0 * 1024 * 1024):F1} GB"
        : $"{TotalMemoryBytes / (1024.0 * 1024):F0} MB";
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    
    public string CategoryText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return Category switch
            {
                ProcessCategory.SafeToClean => loc.Get("MemClean.SafeToClean"),
                ProcessCategory.CautionClean => loc.Get("MemClean.Caution"),
                ProcessCategory.RecommendKeep => loc.Get("MemClean.Keep"),
                ProcessCategory.SystemCritical => loc.Get("MemClean.System"),
                ProcessCategory.Whitelisted => loc.Get("MemClean.Whitelist"),
                _ => ""
            };
        }
    }
    
    public Brush CategoryFg => Category switch
    {
        ProcessCategory.SafeToClean => ThemeColors.MemSafeBrush,
        ProcessCategory.CautionClean => ThemeColors.MemCautionBrush,
        ProcessCategory.RecommendKeep => ThemeColors.MemKeepBrush,
        ProcessCategory.SystemCritical => ThemeColors.MemCriticalBrush,
        ProcessCategory.Whitelisted => ThemeColors.MemWhitelistBrush,
        _ => ThemeColors.Zinc400Brush
    };
    
    public Brush CategoryBg => Category switch
    {
        ProcessCategory.SafeToClean => ThemeColors.MemSafeBgBrush,
        ProcessCategory.CautionClean => ThemeColors.MemCautionBgBrush,
        ProcessCategory.RecommendKeep => ThemeColors.MemKeepBgBrush,
        ProcessCategory.SystemCritical => ThemeColors.MemCriticalBgBrush,
        ProcessCategory.Whitelisted => ThemeColors.MemWhitelistBgBrush,
        _ => Brushes.Transparent
    };
    
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) 
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
    public void NotifyCategoryChanged() => OnPropertyChanged(nameof(CategoryText));
}

public partial class MemoryCleanDialog : Window
{
    private readonly CleanMode _mode;
    private readonly IMemoryCleanerService _cleanerService;
    private readonly IProcessCleanerService _processService;
    private readonly IMemoryCleaningOwner _memoryCleaningOwner;
    private readonly ObservableCollection<ProcessGroupItem> _processGroups = new();
    private MemoryStatus _beforeStatus = null!;
    private string _mutationBlockedReason = "请选择一个正在运行的应用。";
    private CancellationTokenSource? _cleanCts;
    private bool _isCleaning;

    public MemoryCleanDialog(
        CleanMode mode,
        IMemoryCleanerService cleanerService,
        IProcessCleanerService processService,
        IMemoryCleaningOwner memoryCleaningOwner)
    {
        InitializeComponent();
        _mode = mode;
        _cleanerService = cleanerService ?? throw new ArgumentNullException(nameof(cleanerService));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _memoryCleaningOwner = memoryCleaningOwner ?? throw new ArgumentNullException(nameof(memoryCleaningOwner));
        ProcessList.ItemsSource = _processGroups;
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        
        InitializeDialog();
    }
    
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        await RefreshMutationBoundaryAsync();
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        
        // P2-MEM: 取消 PropertyChanged 事件订阅，防止内存泄漏
        foreach (var item in _processGroups)
            item.PropertyChanged -= OnProcessItemPropertyChanged;
    }
    
    private void OnProcessItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessGroupItem.IsSelected)
            && sender is ProcessGroupItem selected
            && selected.IsSelected)
        {
            foreach (var item in _processGroups)
            {
                if (!ReferenceEquals(item, selected) && item.IsSelected)
                    item.IsSelected = false;
            }
        }

        UpdateSelectedCount();
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            SetDialogTitle();
            UpdateLocalizedTexts();
            ApplyBlockedState(_mutationBlockedReason);
            foreach (var item in _processGroups)
                item.NotifyCategoryChanged();
        });
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        LblCurrentMemory.Text = loc.Get("MemClean.CurrentUsage");
        LblMemoryLoad.Text = loc.Get("MemClean.MemoryLoad");
        LblCleanable.Text = loc.Get("MemClean.SafeToClean");
        LblCaution.Text = loc.Get("MemClean.Caution");
        LblSystem.Text = loc.Get("MemClean.System");
        LblWhitelist.Text = loc.Get("MemClean.Whitelist");
        LblProcessName.Text = loc.Get("MemClean.ProcessName");
        LblCategory.Text = loc.Get("MemClean.Category");
        LblInstance.Text = loc.Get("MemClean.Instance");
        LblMemoryUsage.Text = loc.Get("MemClean.MemoryUsage");
        LblCleanComplete.Text = _mode == CleanMode.WorkingSet ? "操作结果" : loc.Get("MemClean.CleanComplete");
        LblBeforeClean.Text = _mode == CleanMode.WorkingSet ? "操作前" : loc.Get("MemClean.Before");
        LblAfterClean.Text = _mode == CleanMode.WorkingSet ? "操作后" : loc.Get("MemClean.After");
        CancelBtn.Content = loc.Get("Common.Cancel");
        ConfirmBtn.ApplyTemplate();
        if (ConfirmBtn.Template.FindName("StartCleanText", ConfirmBtn) is System.Windows.Controls.TextBlock actionText)
            actionText.Text = _mode == CleanMode.WorkingSet ? "继续" : "开始清理";
        
        UpdateSelectedCount();
    }

    private void InitializeDialog()
    {
        _beforeStatus = _cleanerService.GetCurrentStatus();
        UpdateMemoryDisplay(_beforeStatus);
        SetDialogTitle();
        UpdateLocalizedTexts();
        LoadProcessGroups();
        ApplyBlockedState(_mutationBlockedReason);
    }

    private void UpdateMemoryDisplay(MemoryStatus status)
    {
        CurrentUsedText.Text = $"{status.UsedGB:F1}";
        CurrentTotalText.Text = $"{status.TotalGB:F0}";
        CurrentPercentText.Text = $"{status.LoadPercent}";
    }

    private void SetDialogTitle()
    {
        var loc = LocalizationService.Instance;
        switch (_mode)
        {
            case CleanMode.SmartBoost:
                TitleText.Text = loc.Get("MemClean.TitleBoost");
                SubtitleText.Text = loc.Get("MemClean.SubtitleBoost");
                break;
            case CleanMode.StandbyList:
                TitleText.Text = loc.Get("MemClean.TitleStandby");
                SubtitleText.Text = loc.Get("MemClean.SubtitleStandby");
                break;
            case CleanMode.WorkingSet:
                TitleText.Text = "管理单个应用的内存";
                SubtitleText.Text = "选择一个正在运行的应用；不会默认操作任何程序。";
                break;
        }
    }

    private void LoadProcessGroups()
    {
        _processGroups.Clear();
        
        try
        {
            var groups = _processService.GetProcessGroups();
            foreach (var group in groups)
            {
                // The dialog is intentionally process-scoped: a grouped application
                // may contain several child processes, but the user must choose one
                // concrete PID before a non-reversible working-set release can run.
                foreach (var processId in group.Pids.Where(id => id > 4).Distinct())
                {
                    var item = new ProcessGroupItem
                    {
                        Name = group.Name,
                        DisplayName = group.Pids.Count > 1 ? $"{group.Name} (PID {processId})" : group.DisplayName,
                        Description = string.IsNullOrWhiteSpace(group.Description)
                            ? $"PID {processId}"
                            : $"{group.Description} · PID {processId}",
                        Category = group.Category,
                        InstanceCount = 1,
                        // The mutation decision is identity-bound to the PID. The
                        // displayed value is only a group-derived preview; final
                        // before/after values come from the platform readback.
                        TotalMemoryBytes = Math.Max(0, group.TotalMemoryBytes / Math.Max(1, group.Pids.Count)),
                        Pids = [processId],
                        IsResponding = group.IsResponding,
                        IsSelected = false
                    };
                    item.PropertyChanged += OnProcessItemPropertyChanged;
                    _processGroups.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoryCleanDialog] LoadProcessGroups error: {ex.Message}");
        }
        
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        var loc = LocalizationService.Instance;
        var selected = _processGroups.Where(p => p.IsSelected && p.CanSelect).ToList();
        var count = selected.Sum(p => p.InstanceCount);
        var totalMB = selected.Sum(p => p.TotalMemoryBytes) / (1024.0 * 1024);
        
        SelectedCountText.Text = loc.Get("MemClean.SelectedCount", count);
        SelectedMemoryText.Text = totalMB >= 1024 
            ? loc.Get("MemClean.AboutGB", totalMB / 1024)
            : loc.Get("MemClean.AboutMB", (int)totalMB);
        
        var selectedCount = _processGroups.Count(p => p.IsSelected && p.CanSelect);
        if (_mode == CleanMode.WorkingSet)
        {
            var targetProcessCount = selected.SelectMany(item => item.Pids).Distinct().Count();
            ConfirmBtn.IsEnabled = _memoryCleaningOwner is not null
                && selectedCount == 1
                && targetProcessCount == 1
                && !_isCleaning;
            var selectionGuidance = selectedCount switch
            {
                0 => "请选择一个进程；不会默认操作任何程序。",
                > 1 => "一次只能选择一个进程。",
                _ when targetProcessCount != 1 => "无法确认这个应用，不能继续。",
                _ => $"将确认“{selected[0].DisplayName}”仍在运行后，尝试释放它当前占用的一部分内存。不会关闭应用，但此操作不能撤销。"
            };
            MemoryBlockedReasonText.Text = selectionGuidance;
            SubtitleText.Text = selectionGuidance;
        }
    }

    private void AddWhitelist_Click(object sender, RoutedEventArgs e)
    {
        var selected = _processGroups.Where(p => p.IsSelected && p.CanSelect).ToList();
        foreach (var p in selected)
        {
            _processService.AddToWhitelist(p.Name);
        }
        LoadProcessGroups();
    }

    private void RemoveWhitelist_Click(object sender, RoutedEventArgs e)
    {
        // P0-021/P0-024: 添加确认对话框
        var whitelisted = _processGroups.Where(p => p.Category == ProcessCategory.Whitelisted).ToList();
        
        if (whitelisted.Count == 0)
        {
            var loc = LocalizationService.Instance;
            InfoDialog.Show(
                Window.GetWindow(this),
                loc.Get("Dialog.Info"),
                loc.Get("MemClean.NoWhitelistToRemove"),
                Constants.IconGlyphs.Lightbulb);
            return;
        }
        
        var confirmed = ConfirmDialog.Show(
            Window.GetWindow(this),
            LocalizationService.Instance.Get("MemClean.RemoveWhitelistConfirm"),
            LocalizationService.Instance.Get("MemClean.RemoveWhitelistDesc", whitelisted.Count),
            Constants.IconGlyphs.Question);
        
        if (!confirmed) return;
        
        foreach (var p in whitelisted)
        {
            _processService.RemoveFromWhitelist(p.Name);
        }
        LoadProcessGroups();
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_mode != CleanMode.WorkingSet || _memoryCleaningOwner is null)
        {
            ApplyBlockedState(_mutationBlockedReason);
            return;
        }

        var selected = _processGroups.Where(item => item.IsSelected && item.CanSelect).ToList();
        if (selected.Count != 1)
        {
            UpdateSelectedCount();
            return;
        }

        var processId = selected[0].Pids.SingleOrDefault(processId => processId > 4);
        if (processId <= 4 || selected[0].Pids.Count != 1)
        {
            UpdateSelectedCount();
            return;
        }

        var confirmed = DangerousOperationDialog.Show(
            this,
            "确认释放应用占用的内存",
            $"准备处理“{selected[0].DisplayName}”。\n\n不会关闭该应用。",
            new[]
            {
                "开始前会确认选择的仍是这个应用；如果应用已经退出或变成其他程序，本次不会继续。",
                "不会关闭应用、关闭窗口或删除任何文件。",
                "完成后会读取一次结果；这个操作不能撤销。"
            },
            confirmationKeyword: null,
            countdownSeconds: 3);
        if (!confirmed)
            return;

        _isCleaning = true;
        _cleanCts?.Dispose();
        _cleanCts = new CancellationTokenSource();
        var cancellationToken = _cleanCts.Token;
        ConfirmBtn.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        ProcessListBorder.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        CancelBtn.IsEnabled = true;
        try
        {
            ProgressStatusText.Text = "正在确认所选应用…";
            ProgressDetailText.Text = "正在检查它是否仍在运行，并确认可以继续。";
            var target = await _memoryCleaningOwner.CaptureWorkingSetTargetAsync(processId, cancellationToken);
            if (!target.Succeeded || target.Request is null)
            {
                ShowWorkingSetResult(null, target.Message);
                return;
            }

            // This second confirmation is issued by the central plan authority and
            // binds the token to the freshly captured identity and exact PID.
            ProgressStatusText.Text = "正在等待确认…";
            var result = await _memoryCleaningOwner.RunInteractiveAsync(target.Request, cancellationToken);
            ShowWorkingSetResult(result, result.Message);
        }
        catch (OperationCanceledException)
        {
            ShowWorkingSetResult(null, "用户已取消；未确认或未开始的操作没有执行。");
        }
        finally
        {
            _isCleaning = false;
            _cleanCts?.Dispose();
            _cleanCts = null;
            CancelBtn.IsEnabled = true;
            UpdateSelectedCount();
        }
    }

    private async Task RefreshMutationBoundaryAsync()
    {
        if (_memoryCleaningOwner is null)
        {
            ApplyBlockedState(
                "当前版本没有接入安全操作通道，因此不会修改任何应用。");
            return;
        }

        if (_mode == CleanMode.WorkingSet)
        {
            _mutationBlockedReason = "请选择一个正在运行的应用。开始前会再次确认它仍在运行；不会关闭应用，但此操作不能撤销。";
            UpdateSelectedCount();
            return;
        }

        var result = await _memoryCleaningOwner.PrepareAsync(
            MemoryCleaningRequest.ForStandbyList(Environment.MachineName));
        ApplyBlockedState(result.Message);
    }

    private void ApplyBlockedState(string reason)
    {
        _mutationBlockedReason = string.IsNullOrWhiteSpace(reason)
            ? "当前无法安全完成此操作，因此没有修改任何应用。"
            : reason;
        ConfirmBtn.IsEnabled = false;
        ConfirmBtn.ToolTip = _mutationBlockedReason;
        MemoryBlockedReasonText.Text = _mutationBlockedReason;
        SubtitleText.Text = _mutationBlockedReason;
        ProgressPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ProcessListBorder.Visibility = Visibility.Visible;
    }

    private void ShowWorkingSetResult(MemoryCleaningResult? result, string detail)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        ProcessListBorder.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        var succeeded = result is { IsTerminalSuccess: true };
        BeforeMemoryText.Text = succeeded ? "已完成" : "未执行";
        AfterMemoryText.Text = succeeded ? "已回读" : "已阻断";
        FreedMemoryText.Text = succeeded
            ? "操作已完成。应用没有被关闭。"
            : "操作没有完成，应用没有被修改。";
        MemoryBlockedReasonText.Text = GetUserFacingResultDetail(result, detail);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanCts is { IsCancellationRequested: false })
        {
            _cleanCts.Cancel();
            CancelBtn.IsEnabled = false;
            ProgressStatusText.Text = "正在取消操作…";
            ProgressDetailText.Text = "不会开始未确认的操作。";
            return;
        }

        DialogResult = false;
        Close();
    }

    private static string GetUserFacingResultDetail(MemoryCleaningResult? result, string? detail)
    {
        if (result is { IsTerminalSuccess: true })
            return "操作已完成。应用没有被关闭，也没有删除任何文件。";

        return result?.Kind switch
        {
            MemoryCleaningResultKind.Denied => "Windows 不允许修改这个应用。请换一个应用，或以管理员身份启动软件后重试。",
            MemoryCleaningResultKind.ReadOnly or MemoryCleaningResultKind.FailClosed => "当前无法安全完成此操作，因此没有修改任何应用。",
            _ when string.Equals(detail, "用户已取消；未确认或未开始的操作没有执行。", StringComparison.Ordinal) => detail,
            _ => "操作没有完成，应用没有被修改。"
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Cancel_Click(sender, e);
    }
}
