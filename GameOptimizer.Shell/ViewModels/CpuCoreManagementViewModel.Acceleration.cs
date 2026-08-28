// File: GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.Acceleration.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.ProcessTuning;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.ViewModels;

public partial class CpuCoreManagementViewModel
{
    // === 实时加速状态 ===
    [ObservableProperty] private ProcessItem? _selectedProcess;
    [ObservableProperty] private bool _isAccelerating;
    [ObservableProperty] private string _elapsedTime = "00:00:00";
    [ObservableProperty] private bool _canStartAccelerate;
    [ObservableProperty] private bool _hasNoProcesses;
    [ObservableProperty] private string _processListMessage = string.Empty;

    public ObservableCollection<ProcessItem> ProcessList { get; } = new();

    private DispatcherTimer? _refreshTimer;
    private int _acceleratedProcessId;
    private Guid? _activeReceiptId;
    private DateTime _startTime;

    partial void OnSelectedProcessChanged(ProcessItem? value)
    {
        UpdateCanStartAccelerate();
        OnPropertyChanged(nameof(ConfigurationPreviewText));
    }
    partial void OnIsAcceleratingChanged(bool value) => UpdateCanStartAccelerate();

    private void UpdateCanStartAccelerate()
    {
        CanStartAccelerate = SelectedProcess != null
                             && AllCores.Any(c => c.IsSelected)
                             && IsAffinitySupported
                             && !IsAccelerating;
    }

    public string ConfigurationPreviewText
    {
        get
        {
            if (SelectedProcess is null)
                return "请选择一个正在运行的进程。";

            var selected = AllCores.Where(core => core.IsSelected)
                .Select(core => core.Index)
                .OrderBy(index => index)
                .ToArray();
            if (selected.Length == 0)
                return "请选择至少一个逻辑核心后再继续。";

            return $"将为 {SelectedProcess.Name}.exe（PID {SelectedProcess.Id}）配置 {selected.Length} 个逻辑核心：{string.Join(", ", selected)}。执行前会重新核验进程 identity，并要求显式确认。";
        }
    }

    // === 进程列表 ===
    private async Task LoadProcessListAsync()
    {
        var currentSelection = SelectedProcess;
        try
        {
            var scan = await Task.Run(() =>
                SafeProcessHelper.EnumerateWindowProcesses(
                    excludeByName: IsSystemProcess,
                    maxWindowTitleLength: 30));

            var items = scan.Processes
                .Select(p => new ProcessItem
                {
                    Id = p.Id,
                    CreationTimeUtcTicks = p.CreationTimeUtcTicks,
                    Name = p.Name,
                    WindowTitle = p.WindowTitle
                })
                .ToList();

            ProcessList.Clear();
            foreach (var p in items) ProcessList.Add(p);
            HasNoProcesses = ProcessList.Count == 0;
            ProcessListMessage = ProcessList.Count == 0
                ? scan.DiagnosticMessage ?? _loc.Get("CoreAccel.NoWindowProcess")
                : string.Empty;

            if (currentSelection is not null)
            {
                var stillExists = ProcessList.FirstOrDefault(p => p.IsSameInstance(currentSelection));
                if (stillExists != null)
                    SelectedProcess = stillExists;
                else
                {
                    SelectedProcess = null;
                    StatusText = "目标进程已退出或 PID 已被其他进程复用；请重新选择后再配置。";
                    if (IsAccelerating)
                    {
                        ToastService.Instance.Warning(
                            _loc.Get("CoreAccel.ProcessExited"),
                            _loc.Get("CoreAccel.ProcessExitedDesc"));
                        await StopAccelerateInternalAsync(showToast: false);
                    }
                }
            }

            if (ProcessList.Count == 0)
            {
                StatusText = ProcessListMessage;
            }

            if (!scan.Success && !string.IsNullOrWhiteSpace(scan.DiagnosticMessage))
            {
                ToastService.Instance.Warning("进程列表获取受限", scan.DiagnosticMessage);
            }

            UpdateCanStartAccelerate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuCoreManagement][LoadProcessList] {ex.Message}");
            HasNoProcesses = true;
            ProcessListMessage = _loc.Get("CoreMgmt.ProcessReadFailed");
            StatusText = ProcessListMessage;
        }
    }

    private static bool IsSystemProcess(string name)
    {
        var systemProcs = new[] { "explorer", "dwm", "csrss", "smss", "services", "lsass", "winlogon", "svchost", "System", "Idle", "Registry" };
        return systemProcs.Any(s => name.Equals(s, StringComparison.OrdinalIgnoreCase));
    }

    private static string TruncateTitle(string title, int maxLen)
    {
        if (string.IsNullOrEmpty(title)) return string.Empty;
        return title.Length <= maxLen ? title : title[..(maxLen - 3)] + "...";
    }

    private void StartRefreshTimer()
    {
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (s, e) => await LoadProcessListAsync();
        _refreshTimer.Start();
    }

    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        await LoadProcessListAsync();
        ToastService.Instance.Success(
            _loc.Get("CoreAccel.Refreshed"),
            _loc.Get("CoreAccel.FoundProcesses", ProcessList.Count));
    }

    // === 加速控制 ===
    [RelayCommand]
    private async Task StartAccelerateAsync()
    {
        if (SelectedProcess == null)
        {
            ToastService.Instance.Warning(_loc.Get("CoreAccel.SelectProgram"), _loc.Get("CoreAccel.SelectProgramDesc"));
            return;
        }

        var selected = AllCores.Where(c => c.IsSelected).Select(c => c.Index).ToList();
        if (selected.Count == 0)
        {
            ToastService.Instance.Warning(_loc.Get("CoreAccel.SelectCoreHint"), _loc.Get("CoreAccel.SelectCoreHintDesc"));
            return;
        }

        IsLoading = true;
        LoadingText = _loc.Get("CoreAccel.ApplyingOptimization");
        var targetPid = SelectedProcess.Id;
        try
        {
            long affinityMask = BuildAffinityMask(selected);
            if (affinityMask <= 0)
            {
                ToastService.Instance.Warning(_loc.Get("CoreAccel.SelectCoreHint"), _loc.Get("CoreAccel.SelectCoreHintDesc"));
                return;
            }
            var result = await _processTuningOwner.RunInteractiveAsync(
                ProcessTuningRequest.Affinity(targetPid, affinityMask));
            if (!result.IsTerminalSuccess || result.Receipt is null)
            {
                ToastService.Instance.Error(_loc.Get("CoreAccel.AccelFailed"), result.Message);
                if (result.Kind == ProcessTuningResultKind.NotApplicable)
                    await LoadProcessListAsync();
                return;
            }
            _acceleratedProcessId = targetPid;
            _activeReceiptId = result.Receipt.ReceiptId;
            _acceleratedProcessName = SelectedProcess.Name;

            IsAccelerating = true;
            _startTime = DateTime.Now;
            StatusText = _loc.Get("CoreAccel.Accelerating", SelectedProcess.Name);
            SaveLastConfig();

            ToastService.Instance.Success(
                _loc.Get("CoreAccel.AccelSuccess"),
                _loc.Get("CoreAccel.LockedToCores", SelectedProcess.Name, selected.Count));
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error(_loc.Get("CoreAccel.AccelFailed"), ex.Message);
            Debug.WriteLine($"[CpuCoreManagement][StartAccelerate] {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task StopAccelerateAsync()
    {
        await StopAccelerateInternalAsync(showToast: true);
    }

    private async Task StopAccelerateInternalAsync(bool showToast)
    {
        if (_activeReceiptId is not null)
        {
            var restored = await _processTuningOwner.RestoreAsync(
                new GameOptimizer.Core.Contracts.Changes.RestoreRequest(_activeReceiptId.Value));
            if (!restored.IsTerminalSuccess)
            {
                StatusText = restored.Message;
                if (restored.Kind == ProcessTuningResultKind.NotApplicable)
                {
                    _activeReceiptId = null;
                }
                else
                {
                    return;
                }
            }
            _activeReceiptId = null;
        }
        IsAccelerating = false;
        StatusText = _loc.Get("CoreAccel.StatusReady");
        ElapsedTime = "00:00:00";
        _acceleratedProcessId = 0;
        _acceleratedProcessName = null;

        if (showToast)
            ToastService.Instance.Info(_loc.Get("CoreAccel.StoppedTitle"), _loc.Get("CoreAccel.LockReleased"));

    }

    public Task DeactivateAsync() => StopAccelerateInternalAsync(showToast: false);

    [RelayCommand]
    private async Task RestoreDefaultAsync()
    {
        if (_activeReceiptId is not null)
        {
            await StopAccelerateInternalAsync(showToast: false);
            if (_activeReceiptId is not null)
                return;
            ToastService.Instance.Success(_loc.Get("CoreAccel.RestoredTitle"), _loc.Get("CoreAccel.RestoredDefault"));
        }

        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = false;
        SelectRecommended();
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
        CurrentMode = AccelMode.Auto;
    }

    private static long BuildAffinityMask(IEnumerable<int> selectedCores)
    {
        long mask = 0;
        foreach (int index in selectedCores)
        {
            if (index is < 0 or >= 63) return 0;
            mask |= 1L << index;
        }
        return mask;
    }
}
