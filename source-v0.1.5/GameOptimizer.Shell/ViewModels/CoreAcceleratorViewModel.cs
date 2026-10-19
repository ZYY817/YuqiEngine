// File: GameOptimizer.Shell/ViewModels/CoreAcceleratorViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.ProcessTuning;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.ViewModels;

public enum AccelMode
{
    Auto,
    Manual,
    PowerSave
}

public partial class CoreAcceleratorViewModel : ObservableObject, IDisposable
{
    private readonly CpuTopologyService _topologyService;
    private readonly IProcessTuningOwner _processTuningOwner;
    private readonly CorePresetService _presetService;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _refreshTimer;
    private DateTime _startTime;
    private int _acceleratedProcessId;
    private Guid? _activeReceiptId;
    
    [ObservableProperty] private CpuTopology _cpuTopology = new();
    [ObservableProperty] private ProcessItem? _selectedProcess;
    [ObservableProperty] private AccelMode _currentMode = AccelMode.Auto;
    [ObservableProperty] private SelectableCoreInfo? _preferredCore;
    [ObservableProperty] private bool _isAccelerating;
    [ObservableProperty] private string _elapsedTime = "00:00:00";
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string _preferredCoreStrategy = "auto";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _loadingText = "";
    [ObservableProperty] private bool _hasNoProcesses;
    [ObservableProperty] private bool _canStartAccelerate;
    
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private bool _isDisposed;
    private bool _isBatchUpdating;
    
    public ObservableCollection<ProcessItem> ProcessList { get; } = new();
    public ObservableCollection<SelectableCoreInfo> AllCores { get; } = new();
    public ObservableCollection<CoreGroupViewModel> CoreGroups { get; } = new();
    public ObservableCollection<SelectableCoreInfo> SelectedCoreOptions { get; } = new();
    public ObservableCollection<CoreStrategyItem> CoreStrategies { get; } = new();
    public ObservableCollection<OptimizationPreset> OptimizationPresets { get; } = new();
    
    public string CpuDisplayName => CpuTopology.CpuName;
    public string ArchLabel => CpuTopology.Architecture switch
    {
        CpuArchitecture.AmdX3D => "3D V-Cache",
        CpuArchitecture.AmdMultiCcd => $"{CpuTopology.CcdCount} CCD",
        CpuArchitecture.IntelHybrid => "P+E Core",
        _ => "Standard"
    };
    public string CoreCountLabel => $"{CpuTopology.PhysicalCores}C/{CpuTopology.LogicalCores}T";
    public int SelectedCount => AllCores.Count(c => c.IsSelected);
    
    public string SmartTip
    {
        get
        {
            var loc = LocalizationService.Instance;
            return CpuTopology.Architecture switch
            {
                CpuArchitecture.AmdX3D => loc.Get("CoreAccel.SmartTipX3D"),
                CpuArchitecture.AmdMultiCcd => loc.Get("CoreAccel.SmartTipMultiCcd", CpuTopology.CcdCount),
                CpuArchitecture.IntelHybrid => loc.Get("CoreAccel.SmartTipHybrid"),
                _ => loc.Get("CoreAccel.SmartTipStandard")
            };
        }
    }
    
    public CoreAcceleratorViewModel(CpuTopologyService topologyService, IProcessTuningOwner processTuningOwner)
    {
        _topologyService = topologyService ?? throw new ArgumentNullException(nameof(topologyService));
        _processTuningOwner = processTuningOwner ?? throw new ArgumentNullException(nameof(processTuningOwner));
        _presetService = new CorePresetService();
        _dispatcher = Dispatcher.CurrentDispatcher;
        _statusText = _loc.Get("CoreAccel.StatusReady");
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            LoadingText = _loc.Get("CoreAccel.LoadingDetecting");
            
            try
            {
                await Task.Run(() => 
                { 
                    CpuTopology = _topologyService.GetTopology(); 
                }).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                ToastService.Instance.Warning(_loc.Get("CoreAccel.DetectTimeout"), _loc.Get("CoreAccel.DetectTimeoutDesc"));
                CpuTopology = new CpuTopology { LogicalCores = Environment.ProcessorCount, PhysicalCores = Environment.ProcessorCount / 2 };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CoreAcceleratorViewModel] CPU检测失败: {ex.Message}");
                CpuTopology = new CpuTopology { LogicalCores = Environment.ProcessorCount, PhysicalCores = Environment.ProcessorCount / 2 };
            }
            
            // Why: await Task.Run 之后续体可能在后台线程，Invoke 同步阻塞有死锁风险。
            //      InvokeAsync 异步投递，await 确保 UI 操作完成后再继续。
            await _dispatcher.InvokeAsync(() =>
            {
                BuildCoreGroups();
                BuildCoreStrategies();
                ApplyModeSelection(AccelMode.Auto);
                LoadLastConfig();
                NotifyAll();
                IsLoading = false;
                
                // 权限、保护进程和拓扑限制必须由 ProcessTuningOwner 的 Probe 判定。
                // 不能用“是否管理员”替代目标进程的实际可操作性。
            });
            await LoadProcessListAsync();
            StartRefreshTimer();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoreAcceleratorVM][InitializeAsync] {ex.Message}");
            IsLoading = false;
        }
    }
    
    private void LoadLastConfig()
    {
        try
        {
            var lastConfig = _presetService.GetLastUsedConfig();
            if (lastConfig != null)
            {
                foreach (var c in AllCores) c.IsSelected = lastConfig.SelectedCores.Contains(c.Index);
                if (lastConfig.PreferredCore.HasValue)
                    PreferredCore = AllCores.FirstOrDefault(c => c.Index == lastConfig.PreferredCore);
                UpdatePreferredOptions();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoreAcceleratorVM] Failed to load last config: {ex.Message}");
        }
    }
    
    private void SaveLastConfig()
    {
        try
        {
            var selected = AllCores.Where(c => c.IsSelected).Select(c => c.Index).ToList();
            if (selected.Count > 0)
            {
                _presetService.SaveLastUsedConfig(new CorePreset
                {
                    SelectedCores = selected,
                    PreferredCore = PreferredCore?.Index
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoreAcceleratorVM] Failed to save config: {ex.Message}");
        }
    }
    
    private void BuildCoreStrategies()
    {
        CoreStrategies.Clear();
        CoreStrategies.Add(new CoreStrategyItem("auto", "\uE8BA " + _loc.Get("CoreAccel.StrategyAuto"), _loc.Get("CoreAccel.StrategyAutoDesc")));
        CoreStrategies.Add(new CoreStrategyItem("first", "\uE945 " + _loc.Get("CoreAccel.StrategyFirst"), _loc.Get("CoreAccel.StrategyFirstDesc")));
        CoreStrategies.Add(new CoreStrategyItem("fastest", "\uE9F5 " + _loc.Get("CoreAccel.StrategyFastest"), _loc.Get("CoreAccel.StrategyFastestDesc")));
        
        if (CpuTopology.Architecture == CpuArchitecture.IntelHybrid)
        {
            CoreStrategies.Add(new CoreStrategyItem("pcore", "\uE9D9 " + _loc.Get("CoreAccel.StrategyPCore"), _loc.Get("CoreAccel.StrategyPCoreDesc")));
            CoreStrategies.Add(new CoreStrategyItem("ecore", "\uEC48 " + _loc.Get("CoreAccel.StrategyECore"), _loc.Get("CoreAccel.StrategyECoreDesc")));
        }
        else if (CpuTopology.Architecture == CpuArchitecture.AmdX3D)
        {
            CoreStrategies.Add(new CoreStrategyItem("vcache", "\uE950 " + _loc.Get("CoreAccel.StrategyVCache"), _loc.Get("CoreAccel.StrategyVCacheDesc")));
        }
        else if (CpuTopology.CcdCount > 1)
        {
            CoreStrategies.Add(new CoreStrategyItem("ccd0", "\uE7B8 " + _loc.Get("CoreAccel.StrategyCcd0"), _loc.Get("CoreAccel.StrategyCcd0Desc")));
        }
        
        CoreStrategies.Add(new CoreStrategyItem("lowlatency", "\uE916 " + _loc.Get("CoreAccel.StrategyLowLatency"), _loc.Get("CoreAccel.StrategyLowLatencyDesc")));
        PreferredCoreStrategy = "auto";
        
        BuildOptimizationPresets();
    }
    
    private void BuildOptimizationPresets()
    {
        OptimizationPresets.Clear();
        
        var perfCores = AllCores.Count(c => c.Type == CoreType.PerformanceCore || c.Type == CoreType.Physical);
        var allCores = AllCores.Count;
        var physicalOnly = AllCores.Count(c => c.Type != CoreType.Smt);
        
        string perfDesc = CpuTopology.Architecture == CpuArchitecture.IntelHybrid 
            ? _loc.Get("CoreAccel.OptPresetPerfDescPCore", AllCores.Count(c => c.Type == CoreType.PerformanceCore))
            : _loc.Get("CoreAccel.OptPresetPerfDescPhysical", physicalOnly);
            
        OptimizationPresets.Add(new OptimizationPreset("perf", "\uE7FC", _loc.Get("CoreAccel.OptPresetPerf"), perfDesc, _loc.Get("CoreAccel.CoreCount", physicalOnly), true));
        OptimizationPresets.Add(new OptimizationPreset("all", "\uE8B2", _loc.Get("CoreAccel.OptPresetAll"), _loc.Get("CoreAccel.OptPresetAllDesc", allCores), _loc.Get("CoreAccel.ThreadCount", allCores), true));
        OptimizationPresets.Add(new OptimizationPreset("lowlat", "\uE945", _loc.Get("CoreAccel.OptPresetLowLat"), _loc.Get("CoreAccel.OptPresetLowLatDesc", physicalOnly), _loc.Get("CoreAccel.CoreCount", physicalOnly), true));
        
        LoadUserPresets();
    }
    
    private void LoadUserPresets()
    {
        foreach (var preset in _presetService.GetUserPresets())
        {
            OptimizationPresets.Add(new OptimizationPreset(
                preset.Id, preset.Icon, preset.Name, preset.Description, 
                $"{preset.SelectedCores.Count}核", false));
        }
    }
    
    [RelayCommand]
    private void ApplyOptimizationPreset(string presetKey)
    {
        foreach (var c in AllCores) c.IsSelected = false;
        
        switch (presetKey)
        {
            case "perf":
                if (CpuTopology.Architecture == CpuArchitecture.IntelHybrid)
                    foreach (var c in AllCores.Where(c => c.Type == CoreType.PerformanceCore)) c.IsSelected = true;
                else
                    foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical || c.Type == CoreType.PerformanceCore)) c.IsSelected = true;
                ToastService.Instance.Success(_loc.Get("CoreAccel.PresetGameMode"), _loc.Get("CoreAccel.PresetGameModeDesc"));
                break;
                
            case "all":
                foreach (var c in AllCores) c.IsSelected = true;
                ToastService.Instance.Success(_loc.Get("CoreAccel.PresetAllCore"), _loc.Get("CoreAccel.PresetAllCoreDesc"));
                break;
                
            case "lowlat":
                foreach (var c in AllCores.Where(c => c.Type != CoreType.Smt)) c.IsSelected = true;
                ToastService.Instance.Success(_loc.Get("CoreAccel.PresetLowLatency"), _loc.Get("CoreAccel.PresetLowLatencyDesc"));
                break;
                
            default:
                var userPreset = _presetService.GetUserPresets().FirstOrDefault(p => p.Id == presetKey);
                if (userPreset != null)
                {
                    foreach (var c in AllCores.Where(c => userPreset.SelectedCores.Contains(c.Index))) 
                        c.IsSelected = true;
                    if (userPreset.PreferredCore.HasValue)
                        PreferredCore = AllCores.FirstOrDefault(c => c.Index == userPreset.PreferredCore);
                    ToastService.Instance.Success(userPreset.Name, _loc.Get("CoreAccel.PresetLoaded"));
                }
                break;
        }
        
        CurrentMode = AccelMode.Manual;
        UpdatePreferredOptions();
    }
    
    [RelayCommand]
    private void SaveCurrentAsPreset()
    {
        var selected = AllCores.Where(c => c.IsSelected).Select(c => c.Index).ToList();
        if (selected.Count == 0)
        {
            ToastService.Instance.Warning(_loc.Get("CoreAccel.CannotSave"), _loc.Get("CoreAccel.SelectCoreFirst"));
            return;
        }
        
        var presetCount = _presetService.GetUserPresets().Count + 1;
        var preset = new CorePreset
        {
            Name = _loc.Get("CoreAccel.MyConfig", presetCount),
            Icon = "\uE74E",
            Description = _loc.Get("CoreAccel.CustomCoreConfig", selected.Count),
            SelectedCores = selected,
            PreferredCore = PreferredCore?.Index
        };
        
        _presetService.SavePreset(preset);
        
        OptimizationPresets.Clear();
        BuildOptimizationPresets();
        
        ToastService.Instance.Success(_loc.Get("CoreAccel.PresetSaved"), _loc.Get("CoreAccel.PresetSavedDesc", preset.Name));
    }
    
    [RelayCommand]
    private void DeletePreset(string presetId)
    {
        var preset = _presetService.GetUserPresets().FirstOrDefault(p => p.Id == presetId);
        if (preset == null) return;
        
        _presetService.DeletePreset(presetId);
        
        OptimizationPresets.Clear();
        BuildOptimizationPresets();
        
        ToastService.Instance.Info(_loc.Get("CoreAccel.PresetDeleted"), _loc.Get("CoreAccel.PresetDeletedDesc", preset.Name));
    }

    private void BuildCoreGroups()
    {
        AllCores.Clear();
        CoreGroups.Clear();
        
        foreach (var core in CpuTopology.Cores)
        {
            var selectable = new SelectableCoreInfo(core);
            selectable.PropertyChanged += OnCoreSelectionChanged;
            AllCores.Add(selectable);
        }
        
        // 按类型分组
        if (CpuTopology.Architecture == CpuArchitecture.IntelHybrid)
        {
            var pCores = AllCores.Where(c => c.Type == CoreType.PerformanceCore).ToList();
            var pCoreSmt = AllCores.Where(c => c.Type == CoreType.Smt).ToList();
            var eCores = AllCores.Where(c => c.Type == CoreType.EfficiencyCore).ToList();
            
            var loc = LocalizationService.Instance;
            if (pCores.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupPCore"), pCores));
            if (pCoreSmt.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupPCoreSmt"), pCoreSmt));
            if (eCores.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupECore"), eCores));
        }
        else if (CpuTopology.CcdCount > 1)
        {
            for (int i = 0; i < CpuTopology.CcdCount; i++)
            {
                var ccdCores = AllCores.Where(c => c.CcdIndex == i && c.Type != CoreType.Smt).ToList();
                var ccdSmt = AllCores.Where(c => c.CcdIndex == i && c.Type == CoreType.Smt).ToList();
                var label = CpuTopology.Ccds.FirstOrDefault(c => c.CcdIndex == i)?.HasVCache == true 
                    ? $"CCD {i} (V-Cache)" : $"CCD {i}";
                if (ccdCores.Any()) CoreGroups.Add(new CoreGroupViewModel(label, ccdCores));
                if (ccdSmt.Any()) CoreGroups.Add(new CoreGroupViewModel($"CCD {i} SMT", ccdSmt));
            }
        }
        else
        {
            var loc = LocalizationService.Instance;
            var physical = AllCores.Where(c => c.Type == CoreType.Physical).ToList();
            var smt = AllCores.Where(c => c.Type == CoreType.Smt).ToList();
            if (physical.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupPhysical"), physical));
            if (smt.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupSmt"), smt));
        }
        
        UpdatePreferredOptions();
    }
    
    private void OnCoreSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isBatchUpdating) return;
        if (e.PropertyName == nameof(SelectableCoreInfo.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            UpdatePreferredOptions();
            if (CurrentMode != AccelMode.Manual) CurrentMode = AccelMode.Manual;
        }
    }
    
    private void UpdatePreferredOptions()
    {
        var current = PreferredCore?.Index;
        SelectedCoreOptions.Clear();
        foreach (var c in AllCores.Where(c => c.IsSelected)) SelectedCoreOptions.Add(c);
        
        if (current.HasValue)
        {
            var stillSelected = SelectedCoreOptions.FirstOrDefault(c => c.Index == current);
            PreferredCore = stillSelected ?? SelectedCoreOptions.FirstOrDefault();
        }
        else
        {
            PreferredCore = SelectedCoreOptions.FirstOrDefault();
        }
        
        UpdateCanStartAccelerate();
    }
    
    private void UpdateCanStartAccelerate()
    {
        CanStartAccelerate = SelectedProcess != null 
                             && AllCores.Any(c => c.IsSelected) 
                             && !IsAccelerating;
    }
    
    // Why: Process.GetProcesses() + MainWindowHandle 遍历是跨进程 IPC，在多进程环境下可能耗时 1-2 秒，必须离开 UI 线程
    private async Task LoadProcessListAsync()
    {
        var currentId = SelectedProcess?.Id;
        
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
                    Name = p.Name,
                    WindowTitle = p.WindowTitle
                })
                .ToList();
            
            ProcessList.Clear();
            foreach (var p in items) ProcessList.Add(p);
            
            HasNoProcesses = ProcessList.Count == 0;
            
            if (currentId.HasValue)
            {
                var stillExists = ProcessList.FirstOrDefault(p => p.Id == currentId);
                if (stillExists != null)
                    SelectedProcess = stillExists;
                else
                {
                    SelectedProcess = null;
                    if (IsAccelerating)
                    {
                        ToastService.Instance.Warning(_loc.Get("CoreAccel.ProcessExited"), _loc.Get("CoreAccel.ProcessExitedDesc"));
                        StopAccelerateInternal(showToast: false);
                    }
                }
            }
            
            if (ProcessList.Count == 0)
            {
                StatusText = scan.DiagnosticMessage ?? _loc.Get("CoreAccel.NoWindowProcess");
            }

            if (!scan.Success && !string.IsNullOrWhiteSpace(scan.DiagnosticMessage))
            {
                ToastService.Instance.Warning("进程列表获取受限", scan.DiagnosticMessage);
            }
            
            UpdateCanStartAccelerate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoreAcceleratorViewModel] 加载进程列表失败: {ex.Message}");
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
    
    private void NotifyAll()
    {
        OnPropertyChanged(nameof(CpuDisplayName));
        OnPropertyChanged(nameof(ArchLabel));
        OnPropertyChanged(nameof(CoreCountLabel));
        OnPropertyChanged(nameof(SmartTip));
        OnPropertyChanged(nameof(SelectedCount));
    }
    
    partial void OnCurrentModeChanged(AccelMode value) => ApplyModeSelection(value);
    
    partial void OnSelectedProcessChanged(ProcessItem? value) => UpdateCanStartAccelerate();
    
    partial void OnIsAcceleratingChanged(bool value) => UpdateCanStartAccelerate();
    
    private void ApplyModeSelection(AccelMode mode)
    {
        if (mode == AccelMode.Manual) return;
        
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = false;
        
        switch (mode)
        {
            case AccelMode.Auto:
                SelectRecommended();
                break;
            case AccelMode.PowerSave:
                foreach (var c in AllCores.Where(c => c.Type == CoreType.EfficiencyCore)) c.IsSelected = true;
                if (!AllCores.Any(c => c.IsSelected))
                    foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical).Take(CpuTopology.PhysicalCores / 2)) c.IsSelected = true;
                break;
        }
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
    }
    
    private void SelectRecommended()
    {
        switch (CpuTopology.Architecture)
        {
            case CpuArchitecture.AmdX3D:
                var vCcd = CpuTopology.Ccds.FirstOrDefault(c => c.HasVCache);
                if (vCcd != null) foreach (var c in AllCores.Where(c => c.CcdIndex == vCcd.CcdIndex && c.Type != CoreType.Smt)) c.IsSelected = true;
                break;
            case CpuArchitecture.AmdMultiCcd:
                foreach (var c in AllCores.Where(c => c.CcdIndex == 0 && c.Type != CoreType.Smt)) c.IsSelected = true;
                break;
            case CpuArchitecture.IntelHybrid:
                foreach (var c in AllCores.Where(c => c.Type == CoreType.PerformanceCore)) c.IsSelected = true;
                break;
            default:
                foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical)) c.IsSelected = true;
                break;
        }
    }

    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        await LoadProcessListAsync();
        ToastService.Instance.Success(_loc.Get("CoreAccel.Refreshed"), _loc.Get("CoreAccel.FoundProcesses", ProcessList.Count));
    }
    
    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = false;
        
        switch (preset)
        {
            case "game":
                SelectForGaming();
                ToastService.Instance.Success(_loc.Get("CoreAccel.GameModeTitle"), _loc.Get("CoreAccel.GameModeApplied"));
                break;
            case "stream":
                SelectForStreaming();
                ToastService.Instance.Success(_loc.Get("CoreAccel.StreamModeTitle"), _loc.Get("CoreAccel.StreamModeApplied"));
                break;
            case "render":
                SelectForRendering();
                ToastService.Instance.Success(_loc.Get("CoreAccel.RenderModeTitle"), _loc.Get("CoreAccel.RenderModeApplied"));
                break;
        }
        
        CurrentMode = AccelMode.Manual;
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
    }
    
    private void SelectForGaming()
    {
        switch (CpuTopology.Architecture)
        {
            case CpuArchitecture.IntelHybrid:
                foreach (var c in AllCores.Where(c => c.Type == CoreType.PerformanceCore)) c.IsSelected = true;
                break;
            case CpuArchitecture.AmdX3D:
                var vCcd = CpuTopology.Ccds.FirstOrDefault(c => c.HasVCache);
                if (vCcd != null) foreach (var c in AllCores.Where(c => c.CcdIndex == vCcd.CcdIndex && c.Type != CoreType.Smt)) c.IsSelected = true;
                break;
            case CpuArchitecture.AmdMultiCcd:
                foreach (var c in AllCores.Where(c => c.CcdIndex == 0 && c.Type != CoreType.Smt)) c.IsSelected = true;
                break;
            default:
                foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical)) c.IsSelected = true;
                break;
        }
    }
    
    private void SelectForStreaming()
    {
        switch (CpuTopology.Architecture)
        {
            case CpuArchitecture.IntelHybrid:
                foreach (var c in AllCores.Where(c => c.Type == CoreType.PerformanceCore).Take(4)) c.IsSelected = true;
                foreach (var c in AllCores.Where(c => c.Type == CoreType.EfficiencyCore).Take(4)) c.IsSelected = true;
                break;
            default:
                var half = CpuTopology.PhysicalCores / 2;
                foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical).Take(half)) c.IsSelected = true;
                break;
        }
    }
    
    private void SelectForRendering()
    {
        foreach (var c in AllCores) c.IsSelected = true;
    }
    
    [RelayCommand]
    private void ApplyCoreStrategy(string strategy)
    {
        PreferredCoreStrategy = strategy;
        ApplyStrategySelection(strategy);
        var item = CoreStrategies.FirstOrDefault(s => s.Key == strategy);
        if (item != null) ToastService.Instance.Info(_loc.Get("CoreAccel.StrategyApplied"), item.Name);
    }
    
    private void ApplyStrategySelection(string strategy)
    {
        foreach (var c in AllCores) c.IsSelected = false;
        
        switch (strategy)
        {
            case "auto":
                SelectRecommended();
                break;
            case "first":
                var first = AllCores.FirstOrDefault(c => c.Type != CoreType.Smt);
                if (first != null) first.IsSelected = true;
                break;
            case "fastest":
                var fastest = AllCores.FirstOrDefault(c => c.Type == CoreType.PerformanceCore) 
                              ?? AllCores.FirstOrDefault(c => c.Type == CoreType.Physical);
                if (fastest != null) fastest.IsSelected = true;
                break;
            case "pcore":
                foreach (var c in AllCores.Where(c => c.Type == CoreType.PerformanceCore)) c.IsSelected = true;
                break;
            case "ecore":
                foreach (var c in AllCores.Where(c => c.Type == CoreType.EfficiencyCore)) c.IsSelected = true;
                break;
            case "vcache":
                var vCcd = CpuTopology.Ccds.FirstOrDefault(c => c.HasVCache);
                if (vCcd != null) foreach (var c in AllCores.Where(c => c.CcdIndex == vCcd.CcdIndex && c.Type != CoreType.Smt)) c.IsSelected = true;
                break;
            case "ccd0":
                foreach (var c in AllCores.Where(c => c.CcdIndex == 0 && c.Type != CoreType.Smt)) c.IsSelected = true;
                break;
            case "lowlatency":
                foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical || c.Type == CoreType.PerformanceCore)) c.IsSelected = true;
                break;
        }
        
        CurrentMode = AccelMode.Manual;
        UpdatePreferredOptions();
    }
    
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
        var affinityMask = BuildAffinityMask(selected);
        if (affinityMask <= 0)
        {
            ToastService.Instance.Warning(_loc.Get("CoreAccel.SelectCoreHint"), _loc.Get("CoreAccel.SelectCoreHintDesc"));
            return;
        }
        
        try
        {
            var result = await _processTuningOwner.RunInteractiveAsync(
                ProcessTuningRequest.Affinity(targetPid, affinityMask));
            if (!result.IsTerminalSuccess || result.Receipt is null)
            {
                ToastService.Instance.Error(_loc.Get("CoreAccel.AccelFailed"), result.Message);
                if (result.Kind == ProcessTuningResultKind.NotApplicable)
                {
                    await LoadProcessListAsync();
                }
                return;
            }
            
            _acceleratedProcessId = targetPid;
            _activeReceiptId = result.Receipt.ReceiptId;
            
            IsAccelerating = true;
            _startTime = DateTime.Now;
            StatusText = _loc.Get("CoreAccel.Accelerating", SelectedProcess.Name);
            SaveLastConfig();
            
            ToastService.Instance.Success(_loc.Get("CoreAccel.AccelSuccess"), _loc.Get("CoreAccel.LockedToCores", SelectedProcess.Name, selected.Count));
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error(_loc.Get("CoreAccel.AccelFailed"), ex.Message);
            Debug.WriteLine($"[CoreAcceleratorVM] StartAccelerate failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task StopAccelerateAsync()
    {
        var restored = await RestoreProcessTuningAsync();
        if (!restored && _activeReceiptId is not null)
        {
            ToastService.Instance.Error(_loc.Get("CoreAccel.AccelFailed"), StatusText);
            return;
        }

        IsAccelerating = false;
        StatusText = _loc.Get("CoreAccel.StatusReady");
        ElapsedTime = "00:00:00";
        _acceleratedProcessId = 0;
        
        ToastService.Instance.Info(_loc.Get("CoreAccel.StoppedTitle"), _loc.Get("CoreAccel.LockReleased"));
    }
    
    [RelayCommand]
    private async Task RestoreDefaultAsync()
    {
        if (_acceleratedProcessId > 0)
        {
            var restored = await RestoreProcessTuningAsync();
            if (restored)
            {
                ToastService.Instance.Success(_loc.Get("CoreAccel.RestoredTitle"), _loc.Get("CoreAccel.RestoredDefault"));
            }
        }
        
        foreach (var c in AllCores) c.IsSelected = false;
        SelectRecommended();
        UpdatePreferredOptions();
        CurrentMode = AccelMode.Auto;
    }
    
    private async Task<bool> RestoreProcessTuningAsync()
    {
        if (_activeReceiptId is null)
        {
            return false;
        }

        var result = await _processTuningOwner.RestoreAsync(new GameOptimizer.Core.Contracts.Changes.RestoreRequest(_activeReceiptId.Value));
        if (result.IsTerminalSuccess)
        {
            _activeReceiptId = null;
            return true;
        }

        StatusText = result.Message;
        if (result.Kind == ProcessTuningResultKind.NotApplicable)
        {
            _activeReceiptId = null;
            _acceleratedProcessId = 0;
            return true;
        }
        return false;
    }

    public async Task DeactivateAsync()
    {
        if (_activeReceiptId is null)
        {
            return;
        }

        if (!await RestoreProcessTuningAsync())
        {
            return;
        }

        IsAccelerating = false;
        ElapsedTime = "00:00:00";
        _acceleratedProcessId = 0;
        StatusText = _loc.Get("CoreAccel.StatusReady");
    }
    
    private async void HandleProcessExited()
    {
        try
        {
            IsAccelerating = false;
            StatusText = _loc.Get("CoreAccel.GameExited");
            ElapsedTime = "00:00:00";
            _acceleratedProcessId = 0;
            
            ToastService.Instance.Info(_loc.Get("CoreAccel.GameExitedTitle"), _loc.Get("CoreAccel.LockAutoReleased"));
            await LoadProcessListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoreAcceleratorVM][HandleProcessExited] {ex.Message}");
        }
    }
    
    private void StopAccelerateInternal(bool showToast = true)
    {
        IsAccelerating = false;
        StatusText = _activeReceiptId is null
            ? _loc.Get("CoreAccel.StatusReady")
            : "目标进程已不在当前窗口列表中；未执行猜测性恢复，durable receipt 保留待核对。";
        ElapsedTime = "00:00:00";
        _acceleratedProcessId = 0;
        
        if (showToast && _activeReceiptId is null)
            ToastService.Instance.Info(_loc.Get("CoreAccel.StoppedTitle"), _loc.Get("CoreAccel.LockReleased"));
    }

    private static long BuildAffinityMask(IEnumerable<int> selectedCores)
    {
        long mask = 0;
        foreach (var index in selectedCores)
        {
            if (index is < 0 or >= 63)
            {
                return 0;
            }

            mask |= 1L << index;
        }
        return mask;
    }
    
    [RelayCommand]
    private void SetMode(string mode)
    {
        if (Enum.TryParse<AccelMode>(mode, out var m)) CurrentMode = m;
    }
    
    [RelayCommand]
    private void SelectGroup(CoreGroupViewModel group)
    {
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = false;
        foreach (var c in group.Cores) c.IsSelected = true;
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
        CurrentMode = AccelMode.Manual;
    }
    
    [RelayCommand]
    private void SelectAll()
    {
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = true;
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
        CurrentMode = AccelMode.Manual;
    }
    
    [RelayCommand]
    private void ClearAll()
    {
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = false;
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
        CurrentMode = AccelMode.Manual;
    }
    
    [RelayCommand]
    private void InvertSelection()
    {
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = !c.IsSelected;
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
        CurrentMode = AccelMode.Manual;
    }
    
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        
        _refreshTimer?.Stop();
        _refreshTimer = null;
        
        foreach (var core in AllCores)
        {
            core.PropertyChanged -= OnCoreSelectionChanged;
        }
        
        GC.SuppressFinalize(this);
    }
    
    ~CoreAcceleratorViewModel()
    {
        Dispose();
    }
}

public class ProcessItem
{
    public int Id { get; set; }
    public long CreationTimeUtcTicks { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string DisplayName => string.IsNullOrEmpty(WindowTitle) ? $"{Name}.exe" : $"{Name}.exe - {WindowTitle}";
    public string ShortDisplay => $"{Name}.exe";
    public bool IsSameInstance(ProcessItem? other) => other is not null
        && Id == other.Id
        && CreationTimeUtcTicks > 0
        && CreationTimeUtcTicks == other.CreationTimeUtcTicks;
}

public class CoreGroupViewModel
{
    public string GroupName { get; }
    public ObservableCollection<SelectableCoreInfo> Cores { get; }
    
    public CoreGroupViewModel(string name, IEnumerable<SelectableCoreInfo> cores)
    {
        GroupName = name;
        Cores = new ObservableCollection<SelectableCoreInfo>(cores);
    }
}

public class CoreStrategyItem
{
    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    
    public CoreStrategyItem(string key, string name, string desc)
    {
        Key = key;
        Name = name;
        Description = desc;
    }
}

public class OptimizationPreset
{
    public string Key { get; }
    public string Icon { get; }
    public string Name { get; }
    public string Description { get; }
    public string CoreInfo { get; }
    public bool IsBuiltIn { get; }
    
    public OptimizationPreset(string key, string icon, string name, string desc, string coreInfo, bool isBuiltIn = false)
    {
        Key = key;
        Icon = icon;
        Name = name;
        Description = desc;
        CoreInfo = coreInfo;
        IsBuiltIn = isBuiltIn;
    }
}
