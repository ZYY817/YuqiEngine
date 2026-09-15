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
    private bool _isDetailsExpanded;
    private int? _selectedProcessId;
    
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public ProcessCategory Category { get; set; }
    public int InstanceCount { get; set; }
    public long TotalMemoryBytes { get; set; }
    public System.Collections.Generic.List<int> Pids { get; set; } = new();
    public bool IsResponding { get; set; } = true;
    public ObservableCollection<ProcessInstanceItem> Instances { get; } = new();
    
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        set
        {
            if (_isDetailsExpanded == value) return;
            _isDetailsExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailsToggleText));
        }
    }

    public int? SelectedProcessId
    {
        get => _selectedProcessId;
        set
        {
            if (_selectedProcessId == value) return;
            _selectedProcessId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionText));
        }
    }
    
    public bool CanSelect => Category != ProcessCategory.SystemCritical && Category != ProcessCategory.Whitelisted;
    public string InstanceText => InstanceCount > 1 ? $"×{InstanceCount}" : "1";
    public string DetailsToggleText => IsDetailsExpanded ? "收起实例" : $"查看 {InstanceCount} 个实例";
    public string SelectionText => SelectedProcessId.HasValue ? "将只处理这个实例" : $"将处理全部 {InstanceCount} 个实例";
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

public sealed class ProcessInstanceItem
{
    public required ProcessGroupItem Parent { get; init; }
    public required int ProcessId { get; init; }
    public required int Index { get; init; }
    public string DetailText => $"实例 {Index} · 进程编号 {ProcessId}";
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
                var processIds = group.Pids.Where(id => id > 4).Distinct().ToList();
                if (processIds.Count == 0)
                    continue;

                var item = new ProcessGroupItem
                {
                    Name = group.Name,
                    DisplayName = group.DisplayName,
                    Description = string.IsNullOrWhiteSpace(group.Description)
                        ? $"{processIds.Count} 个实例已合并显示"
                        : $"{group.Description} · {processIds.Count} 个实例已合并显示",
                    Category = group.Category,
                    InstanceCount = processIds.Count,
                    TotalMemoryBytes = group.TotalMemoryBytes,
                    Pids = processIds,
                    IsResponding = group.IsResponding,
                    IsSelected = false
                };
                for (var index = 0; index < processIds.Count; index++)
                {
                    item.Instances.Add(new ProcessInstanceItem
                    {
                        Parent = item,
                        ProcessId = processIds[index],
                        Index = index + 1
                    });
                }

                item.PropertyChanged += OnProcessItemPropertyChanged;
                _processGroups.Add(item);
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
        var selected = _processGroups.SingleOrDefault(p => p.IsSelected && p.CanSelect);
        var targetProcessIds = selected is null ? new List<int>() : GetSelectedProcessIds(selected);
        var count = targetProcessIds.Count;
        var totalMB = selected is null
            ? 0
            : selected.TotalMemoryBytes / (1024.0 * 1024) * count / Math.Max(1, selected.InstanceCount);
        
        SelectedCountText.Text = selected is null
            ? loc.Get("MemClean.SelectedCount", 0)
            : selected.SelectedProcessId.HasValue
                ? $"已选：{selected.DisplayName} 的一个实例"
                : $"已选：{selected.DisplayName} 的 {count} 个实例";
        SelectedMemoryText.Text = totalMB >= 1024 
            ? loc.Get("MemClean.AboutGB", totalMB / 1024)
            : loc.Get("MemClean.AboutMB", (int)totalMB);
        
        if (_mode == CleanMode.WorkingSet)
        {
            ConfirmBtn.IsEnabled = _memoryCleaningOwner is not null
                && selected is not null
                && targetProcessIds.Count > 0
                && !_isCleaning;
            var selectionGuidance = selected switch
            {
                null => "请选择一个应用；默认不会操作任何程序。",
                { SelectedProcessId: not null } => $"将只处理“{selected.DisplayName}”的一个实例。不会关闭应用，但此操作不能撤销。",
                _ => $"将依次处理“{selected.DisplayName}”的 {targetProcessIds.Count} 个实例。不会关闭应用，但此操作不能撤销。"
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

        var selected = _processGroups.SingleOrDefault(item => item.IsSelected && item.CanSelect);
        if (selected is null)
        {
            UpdateSelectedCount();
            return;
        }

        var processIds = GetSelectedProcessIds(selected);
        if (processIds.Count == 0)
        {
            UpdateSelectedCount();
            return;
        }

        var confirmed = DangerousOperationDialog.Show(
            this,
            "确认释放应用占用的内存",
            processIds.Count == 1
                ? $"准备处理“{selected.DisplayName}”的一个实例。\n\n不会关闭该应用。"
                : $"准备依次处理“{selected.DisplayName}”的 {processIds.Count} 个实例。\n\n不会关闭该应用。",
            new[]
            {
                "每个实例开始前都会确认它仍在运行；已退出或发生变化的实例会被跳过。",
                "不会结束进程、关闭应用或删除任何文件。",
                "每个实例完成后都会读取一次结果；这个操作不能撤销。"
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
            var succeeded = 0;
            var skipped = 0;
            var failures = new List<string>();
            using var confirmationScope = BatchChangeConfirmationScope.Begin();
            for (var index = 0; index < processIds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CleanProgressBar.Value = (index + 0.15) / processIds.Count * 100;
                ProgressStatusText.Text = $"正在处理第 {index + 1} / {processIds.Count} 个实例…";
                ProgressDetailText.Text = $"正在检查目标 PID：{processIds[index]} 是否仍在运行，并确认可以继续。";
                var target = await _memoryCleaningOwner.CaptureWorkingSetTargetAsync(processIds[index], cancellationToken);
                if (!target.Succeeded || target.Request is null)
                {
                    skipped++;
                    failures.Add(DescribeWorkingSetFailure(processIds[index], target.ReasonCode, target.Message));
                    PageDiagnosticService.Log("MemoryClean", $"PID {processIds[index]} was not captured: {target.ReasonCode}.");
                    continue;
                }

                ProgressDetailText.Text = "正在释放该实例当前占用的一部分内存。";
                var result = await _memoryCleaningOwner.RunInteractiveAsync(target.Request, cancellationToken);
                if (result.IsTerminalSuccess)
                {
                    succeeded++;
                    PageDiagnosticService.Log("MemoryClean", $"PID {processIds[index]} completed: {result.ReasonCode}.");
                }
                else
                {
                    skipped++;
                    failures.Add(DescribeWorkingSetFailure(processIds[index], result.ReasonCode, result.Message));
                    PageDiagnosticService.Log("MemoryClean", $"PID {processIds[index]} was not modified: {result.ReasonCode}.");
                }
                CleanProgressBar.Value = (index + 1d) / processIds.Count * 100;
            }

            ShowWorkingSetResult(succeeded, processIds.Count, skipped, BuildWorkingSetResultDetail(failures));
        }
        catch (OperationCanceledException)
        {
            ShowWorkingSetResult(0, processIds.Count, processIds.Count, "用户已取消；未确认或未开始的操作没有执行。");
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

    private static List<int> GetSelectedProcessIds(ProcessGroupItem group) => group.SelectedProcessId is { } processId
        ? [processId]
        : group.Pids.Where(id => id > 4).Distinct().ToList();

    private void SelectGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { DataContext: ProcessGroupItem group })
            SelectTarget(group, null);
    }

    private void SelectOneInstance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ProcessInstanceItem instance })
            SelectTarget(instance.Parent, instance.ProcessId);
    }

    private void ToggleInstanceDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ProcessGroupItem group })
            group.IsDetailsExpanded = !group.IsDetailsExpanded;
    }

    private void SelectTarget(ProcessGroupItem target, int? processId)
    {
        foreach (var item in _processGroups)
        {
            if (!ReferenceEquals(item, target))
            {
                item.IsSelected = false;
                item.SelectedProcessId = null;
            }
        }

        target.SelectedProcessId = processId;
        target.IsSelected = true;
        UpdateSelectedCount();
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

    private void ShowWorkingSetResult(int succeeded, int total, int skipped, string? detail = null)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        ProcessListBorder.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        var allSucceeded = succeeded == total && total > 0;
        BeforeMemoryText.Text = $"{succeeded} / {total}";
        AfterMemoryText.Text = allSucceeded ? "已完成" : "部分完成";
        FreedMemoryText.Text = allSucceeded
            ? $"已处理 {succeeded} 个实例，应用没有被关闭。"
            : succeeded > 0
                ? $"已处理 {succeeded} 个实例，另有 {skipped} 个未处理。"
                : "没有实例被处理，应用没有被修改。";
        WorkingSetResultDetailText.Text = string.IsNullOrWhiteSpace(detail) ? string.Empty : detail;
        WorkingSetResultDetailText.Visibility = string.IsNullOrWhiteSpace(detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        MemoryBlockedReasonText.Text = string.IsNullOrWhiteSpace(detail)
            ? allSucceeded
                ? "每个实例都已完成处理，应用没有被关闭。"
                : "部分实例已退出、发生变化或不允许处理，因此被跳过。"
            : detail;
    }

    private static string BuildWorkingSetResultDetail(IReadOnlyList<string> failures)
    {
        if (failures.Count == 0)
        {
            return string.Empty;
        }

        var displayed = failures.Take(8).ToList();
        var detail = "未处理原因：\n" + string.Join("\n", displayed);
        return failures.Count > displayed.Count
            ? detail + $"\n另有 {failures.Count - displayed.Count} 个实例未处理。"
            : detail;
    }

    private static string DescribeWorkingSetFailure(int processId, string reasonCode, string message)
    {
        var reason = reasonCode switch
        {
            "memory.process-permission-denied" => "Windows 不允许修改这个实例的内存（权限不足或进程受保护）。",
            "memory.process-access-denied" => "Windows 无法打开这个实例以释放内存。",
            "memory.process-identity-unavailable" => "无法安全核验这个实例的身份，已跳过。",
            "memory.process-identity-drift" => "这个实例在操作期间发生变化或已退出，已跳过。",
            "memory.process-exited" => "这个实例已经退出，已跳过。",
            "memory.confirmation-denied" => "未取得本次实例的确认，未执行操作。",
            "execution.reported-mutation-without-target" => "Windows 已接收请求，但无法确认内存已释放，未将其算作成功。",
            _ => string.IsNullOrWhiteSpace(message) ? "当前无法安全处理这个实例，已跳过。" : message
        };
        return $"PID {processId}：{reason}";
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Cancel_Click(sender, e);
    }
}
