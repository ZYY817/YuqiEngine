// File: GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.CoreMatrix.cs
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.ViewModels;

public partial class CpuCoreManagementViewModel
{
    // === 核心矩阵数据 ===
    public ObservableCollection<SelectableCoreInfo> AllCores { get; } = new();
    public ObservableCollection<CoreGroupViewModel> CoreGroups { get; } = new();
    public ObservableCollection<SelectableCoreInfo> SelectedCoreOptions { get; } = new();
    public ObservableCollection<CoreStrategyItem> CoreStrategies { get; } = new();
    public ObservableCollection<OptimizationPreset> OptimizationPresets { get; } = new();

    [ObservableProperty] private AccelMode _currentMode = AccelMode.Auto;
    [ObservableProperty] private SelectableCoreInfo? _preferredCore;
    [ObservableProperty] private string _preferredCoreStrategy = "auto";

    internal bool _isBatchUpdating;

    public int SelectedCount => AllCores.Count(c => c.IsSelected);

    partial void OnCurrentModeChanged(AccelMode value) => ApplyModeSelection(value);

    // === 核心矩阵构建 ===
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

        if (CpuTopology.Architecture == CpuArchitecture.IntelHybrid)
            BuildIntelHybridGroups();
        else if (CpuTopology.CcdCount > 1)
            BuildMultiCcdGroups();
        else
            BuildStandardGroups();

        UpdatePreferredOptions();
    }

    private void BuildIntelHybridGroups()
    {
        var loc = LocalizationService.Instance;
        var pCores = AllCores.Where(c => c.Type == CoreType.PerformanceCore).ToList();
        var pCoreSmt = AllCores.Where(c => c.Type == CoreType.Smt).ToList();
        var eCores = AllCores.Where(c => c.Type == CoreType.EfficiencyCore).ToList();

        if (pCores.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupPCore"), pCores));
        if (pCoreSmt.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupPCoreSmt"), pCoreSmt));
        if (eCores.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupECore"), eCores));
    }

    private void BuildMultiCcdGroups()
    {
        for (int i = 0; i < CpuTopology.CcdCount; i++)
        {
            var ccdCores = AllCores.Where(c => c.CcdIndex == i && c.Type != CoreType.Smt).ToList();
            var ccdSmt = AllCores.Where(c => c.CcdIndex == i && c.Type == CoreType.Smt).ToList();
            var hasVCache = CpuTopology.Ccds.FirstOrDefault(c => c.CcdIndex == i)?.HasVCache == true;
            var label = hasVCache ? $"CCD {i} (V-Cache)" : $"CCD {i}";

            if (ccdCores.Any()) CoreGroups.Add(new CoreGroupViewModel(label, ccdCores));
            if (ccdSmt.Any()) CoreGroups.Add(new CoreGroupViewModel($"CCD {i} SMT", ccdSmt));
        }
    }

    private void BuildStandardGroups()
    {
        var loc = LocalizationService.Instance;
        var physical = AllCores.Where(c => c.Type == CoreType.Physical).ToList();
        var smt = AllCores.Where(c => c.Type == CoreType.Smt).ToList();

        if (physical.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupPhysical"), physical));
        if (smt.Any()) CoreGroups.Add(new CoreGroupViewModel(loc.Get("CoreAccel.GroupSmt"), smt));
    }

    private void OnCoreSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isBatchUpdating) return;
        if (e.PropertyName != nameof(SelectableCoreInfo.IsSelected)) return;

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ConfigurationPreviewText));
        UpdatePreferredOptions();
        if (CurrentMode != AccelMode.Manual) CurrentMode = AccelMode.Manual;
    }

    private void UpdatePreferredOptions()
    {
        var current = PreferredCore?.Index;
        SelectedCoreOptions.Clear();
        foreach (var c in AllCores.Where(c => c.IsSelected))
            SelectedCoreOptions.Add(c);

        PreferredCore = current.HasValue
            ? SelectedCoreOptions.FirstOrDefault(c => c.Index == current) ?? SelectedCoreOptions.FirstOrDefault()
            : SelectedCoreOptions.FirstOrDefault();

        OnPropertyChanged(nameof(ConfigurationPreviewText));
        UpdateCanStartAccelerate();
    }

    // === 选择操作 ===
    [RelayCommand]
    private void SelectAll()
    {
        if (IsMatrixReadOnly) return;
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
        if (IsMatrixReadOnly) return;
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
        if (IsMatrixReadOnly) return;
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = !c.IsSelected;
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
        CurrentMode = AccelMode.Manual;
    }

    [RelayCommand]
    private void SelectGroup(CoreGroupViewModel? group)
    {
        if (IsMatrixReadOnly || group == null) return;
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = false;
        foreach (var c in group.Cores) c.IsSelected = true;
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
        CurrentMode = AccelMode.Manual;
    }

    // === 模式切换 ===
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
                foreach (var c in AllCores.Where(c => c.Type == CoreType.EfficiencyCore))
                    c.IsSelected = true;
                if (!AllCores.Any(c => c.IsSelected))
                    foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical).Take(CpuTopology.PhysicalCores / 2))
                        c.IsSelected = true;
                break;
        }

        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
    }

    private void SelectRecommended()
    {
        // 型号和线程数不能证明 P/E/CCD 映射。可靠拓扑提供器接入前，
        // 默认仅选择所有可表达逻辑处理器，不宣称性能推荐。
        foreach (var c in AllCores.Where(c => c.Index is >= 0 and < 63))
            c.IsSelected = true;
    }

    // === 预设系统 ===
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

        var allCount = AllCores.Count;
        var physicalOnly = AllCores.Count(c => c.Type != CoreType.Smt);

        string perfDesc = CpuTopology.Architecture == CpuArchitecture.IntelHybrid
            ? _loc.Get("CoreAccel.OptPresetPerfDescPCore", AllCores.Count(c => c.Type == CoreType.PerformanceCore))
            : _loc.Get("CoreAccel.OptPresetPerfDescPhysical", physicalOnly);

        OptimizationPresets.Add(new OptimizationPreset("perf", "\uE7FC", _loc.Get("CoreAccel.OptPresetPerf"), perfDesc, _loc.Get("CoreAccel.CoreCount", physicalOnly), true));
        OptimizationPresets.Add(new OptimizationPreset("all", "\uE8B2", _loc.Get("CoreAccel.OptPresetAll"), _loc.Get("CoreAccel.OptPresetAllDesc", allCount), _loc.Get("CoreAccel.ThreadCount", allCount), true));
        OptimizationPresets.Add(new OptimizationPreset("lowlat", "\uE945", _loc.Get("CoreAccel.OptPresetLowLat"), _loc.Get("CoreAccel.OptPresetLowLatDesc", physicalOnly), _loc.Get("CoreAccel.CoreCount", physicalOnly), true));

        LoadUserPresets();
    }

    private void LoadUserPresets()
    {
        try
        {
            foreach (var preset in _presetService.GetUserPresets())
            {
                OptimizationPresets.Add(new OptimizationPreset(
                    preset.Id, preset.Icon, preset.Name, preset.Description,
                    $"{preset.SelectedCores.Count}核", false));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuCoreManagement][LoadUserPresets] {ex.Message}");
        }
    }

    private void LoadLastConfig()
    {
        try
        {
            var lastConfig = _presetService.GetLastUsedConfig();
            if (lastConfig == null) return;

            foreach (var c in AllCores)
                c.IsSelected = lastConfig.SelectedCores.Contains(c.Index);
            if (lastConfig.PreferredCore.HasValue)
                PreferredCore = AllCores.FirstOrDefault(c => c.Index == lastConfig.PreferredCore);
            UpdatePreferredOptions();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuCoreManagement][LoadLastConfig] {ex.Message}");
        }
    }

    private void SaveLastConfig()
    {
        try
        {
            var selected = AllCores.Where(c => c.IsSelected).Select(c => c.Index).ToList();
            if (selected.Count == 0) return;
            _presetService.SaveLastUsedConfig(new CorePreset
            {
                SelectedCores = selected,
                PreferredCore = PreferredCore?.Index
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuCoreManagement][SaveLastConfig] {ex.Message}");
        }
    }

    [RelayCommand]
    private void ApplyOptimizationPreset(string? presetKey)
    {
        if (IsMatrixReadOnly || string.IsNullOrEmpty(presetKey)) return;

        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = false;

        switch (presetKey)
        {
            case "perf":
                SelectForGaming();
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
                ApplyUserPreset(presetKey);
                break;
        }

        _isBatchUpdating = false;
        CurrentMode = AccelMode.Manual;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
    }

    private void ApplyUserPreset(string presetKey)
    {
        var userPreset = _presetService.GetUserPresets().FirstOrDefault(p => p.Id == presetKey);
        if (userPreset == null) return;

        foreach (var c in AllCores.Where(c => userPreset.SelectedCores.Contains(c.Index)))
            c.IsSelected = true;
        if (userPreset.PreferredCore.HasValue)
            PreferredCore = AllCores.FirstOrDefault(c => c.Index == userPreset.PreferredCore);
        ToastService.Instance.Success(userPreset.Name, _loc.Get("CoreAccel.PresetLoaded"));
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
                if (vCcd != null)
                    foreach (var c in AllCores.Where(c => c.CcdIndex == vCcd.CcdIndex && c.Type != CoreType.Smt))
                        c.IsSelected = true;
                break;
            case CpuArchitecture.AmdMultiCcd:
                foreach (var c in AllCores.Where(c => c.CcdIndex == 0 && c.Type != CoreType.Smt)) c.IsSelected = true;
                break;
            default:
                foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical)) c.IsSelected = true;
                break;
        }
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
    private void DeletePreset(string? presetId)
    {
        if (string.IsNullOrEmpty(presetId)) return;
        var preset = _presetService.GetUserPresets().FirstOrDefault(p => p.Id == presetId);
        if (preset == null) return;

        _presetService.DeletePreset(presetId);
        OptimizationPresets.Clear();
        BuildOptimizationPresets();
        ToastService.Instance.Info(_loc.Get("CoreAccel.PresetDeleted"), _loc.Get("CoreAccel.PresetDeletedDesc", preset.Name));
    }

    [RelayCommand]
    private void ApplyCoreStrategy(string? strategy)
    {
        if (IsMatrixReadOnly || string.IsNullOrEmpty(strategy)) return;

        PreferredCoreStrategy = strategy;
        _isBatchUpdating = true;
        foreach (var c in AllCores) c.IsSelected = false;

        switch (strategy)
        {
            case "auto": SelectRecommended(); break;
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
                if (vCcd != null)
                    foreach (var c in AllCores.Where(c => c.CcdIndex == vCcd.CcdIndex && c.Type != CoreType.Smt))
                        c.IsSelected = true;
                break;
            case "ccd0":
                foreach (var c in AllCores.Where(c => c.CcdIndex == 0 && c.Type != CoreType.Smt)) c.IsSelected = true;
                break;
            case "lowlatency":
                foreach (var c in AllCores.Where(c => c.Type == CoreType.Physical || c.Type == CoreType.PerformanceCore))
                    c.IsSelected = true;
                break;
        }

        _isBatchUpdating = false;
        CurrentMode = AccelMode.Manual;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();

        var item = CoreStrategies.FirstOrDefault(s => s.Key == strategy);
        if (item != null)
            ToastService.Instance.Info(_loc.Get("CoreAccel.StrategyApplied"), item.Name);
    }
}
