// File: GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.CpuAffinity;
using GameOptimizer.Core.Services.ProcessTuning;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.ViewModels;

/// <summary>
/// 统一 CPU 核心管理 ViewModel — 主文件
/// partial class 拆分: CoreMatrix / Acceleration / Profiles
/// </summary>
public partial class CpuCoreManagementViewModel : ObservableObject, IDisposable
{
    // === 依赖注入 ===
    private readonly CpuTopologyService _topologyService;
    private readonly CorePresetService _presetService;
    private readonly IProcessAffinityService _profileService;
    private readonly IProcessTuningOwner _processTuningOwner;
    private readonly Dispatcher _dispatcher;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    // === 共享状态 ===
    [ObservableProperty] private CpuTopology _cpuTopology = new();
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _loadingText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _activeTab; // 0=实时加速, 1=配置文件

    // === Tab 切换时的核心矩阵状态隔离 ===
    private List<int> _accelerationCoreSelection = new();
    [ObservableProperty] private bool _isMatrixReadOnly;
    [ObservableProperty] private bool _isAffinitySupported;
    [ObservableProperty] private string _affinitySupportMessage = string.Empty;

    // === 并发冲突追踪 ===
    private string? _acceleratedProcessName;
    private bool _isDisposed;

    // === 计算属性 ===
    /// <summary>
    /// The UI must never turn a failed topology probe into a fictional processor.
    /// A real name and at least one discovered logical processor are required before
    /// the editor can be presented as available.
    /// </summary>
    public bool HasUsableTopology =>
        !string.IsNullOrWhiteSpace(CpuTopology?.CpuName)
        && CpuTopology.LogicalCores > 0
        && CpuTopology.Cores.Count > 0;

    public string CpuDisplayName => HasUsableTopology
        ? CpuTopology.CpuName
        : _loc.Get("CoreMgmt.CpuUnavailable");

    public string ArchLabel => !HasUsableTopology
        ? _loc.Get("CoreMgmt.TopologyUnavailable")
        : CpuTopology.Architecture switch
    {
        CpuArchitecture.AmdX3D => "3D V-Cache",
        CpuArchitecture.AmdMultiCcd => $"{CpuTopology.CcdCount} CCD",
        CpuArchitecture.IntelHybrid => "P+E Core",
        _ => "Standard"
    };

    public string CoreCountLabel => HasUsableTopology
        ? $"{CpuTopology.PhysicalCores}C/{CpuTopology.LogicalCores}T"
        : _loc.Get("CoreMgmt.TopologyUnavailable");

    // Why: 教育性指南文本，根据 CPU 架构告诉用户"你的 CPU 是什么结构"，
    //      让用户自己决定怎么绑定，不做推荐，不承担结果责任
    public string GuideText
    {
        get
        {
            if (CpuTopology == null) return "";
            return CpuTopology.Architecture switch
            {
                CpuArchitecture.AmdX3D => _loc.Get("CoreMgmt.Guide.X3D"),
                CpuArchitecture.AmdMultiCcd => _loc.Get("CoreMgmt.Guide.MultiCcd", CpuTopology.CcdCount),
                CpuArchitecture.IntelHybrid => _loc.Get("CoreMgmt.Guide.Hybrid"),
                _ => _loc.Get("CoreMgmt.Guide.Standard")
            };
        }
    }

    public CpuCoreManagementViewModel(
        CpuTopologyService topologyService,
        IProcessAffinityService profileService,
        IProcessTuningOwner processTuningOwner)
    {
        _topologyService = topologyService ?? throw new ArgumentNullException(nameof(topologyService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _processTuningOwner = processTuningOwner ?? throw new ArgumentNullException(nameof(processTuningOwner));
        _presetService = new CorePresetService();
        _dispatcher = Dispatcher.CurrentDispatcher;
        _statusText = _loc.Get("CoreAccel.StatusReady");
        CheckAdminPrivilege();
    }

    private void CheckAdminPrivilege()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            IsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            IsAdmin = false;
        }
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
                ToastService.Instance.Warning(
                    _loc.Get("CoreAccel.DetectTimeout"),
                    _loc.Get("CoreAccel.DetectTimeoutDesc"));
                CpuTopology = new CpuTopology
                {
                    CapabilityMessage = _loc.Get("CoreMgmt.TopologyReadFailed")
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CpuCoreManagement][InitializeAsync] CPU检测失败: {ex.Message}");
                CpuTopology = new CpuTopology
                {
                    CapabilityMessage = _loc.Get("CoreMgmt.TopologyReadFailed")
                };
            }

            await _dispatcher.InvokeAsync(() =>
            {
                BuildCoreGroups();
                IsAffinitySupported = HasUsableTopology
                    && CpuTopology.IsAffinityMaskSupported
                    && CpuTopology.Cores.All(c => c.Index is >= 0 and < 63);
                AffinitySupportMessage = !HasUsableTopology
                    ? _loc.Get("CoreMgmt.TopologyReadFailed")
                    : string.IsNullOrWhiteSpace(CpuTopology.CapabilityMessage)
                        ? _loc.Get("CoreMgmt.AffinityManualOnly")
                        : CpuTopology.CapabilityMessage;
                BuildCoreStrategies();
                ApplyModeSelection(AccelMode.Auto);
                LoadLastConfig();
                NotifyAll();
                IsLoading = false;

                if (!IsAdmin)
                {
                    ToastService.Instance.Warning(
                        _loc.Get("CoreAccel.PermissionHint"),
                        _loc.Get("CoreAccel.PermissionHintDesc"));
                }
            });

            await LoadProcessListAsync();
            StartRefreshTimer();
            await LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuCoreManagement][InitializeAsync] {ex.Message}");
            IsLoading = false;
        }
    }

    // === Tab 切换 ===
    partial void OnActiveTabChanged(int value)
    {
        if (value == 0)
        {
            IsMatrixReadOnly = false;
            RestoreAccelerationCoreSelection();
        }
        else
        {
            SaveAccelerationCoreSelection();
            IsMatrixReadOnly = true;
            ShowSelectedProfileCores();
        }
    }

    private void SaveAccelerationCoreSelection()
    {
        _accelerationCoreSelection = AllCores
            .Where(c => c.IsSelected)
            .Select(c => c.Index)
            .ToList();
    }

    private void RestoreAccelerationCoreSelection()
    {
        _isBatchUpdating = true;
        foreach (var c in AllCores)
            c.IsSelected = _accelerationCoreSelection.Contains(c.Index);
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
        UpdatePreferredOptions();
    }

    private void ShowSelectedProfileCores()
    {
        if (SelectedProfile == null)
        {
            _isBatchUpdating = true;
            foreach (var c in AllCores) c.IsSelected = false;
            _isBatchUpdating = false;
            OnPropertyChanged(nameof(SelectedCount));
            return;
        }
        ShowProfileCoresInMatrix(SelectedProfile.Profile.AssignedThreads);
    }

    private void ShowProfileCoresInMatrix(List<int>? threads)
    {
        if (threads == null) return;
        _isBatchUpdating = true;
        foreach (var c in AllCores)
            c.IsSelected = threads.Contains(c.Index);
        _isBatchUpdating = false;
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(CpuDisplayName));
        OnPropertyChanged(nameof(ArchLabel));
        OnPropertyChanged(nameof(CoreCountLabel));
        OnPropertyChanged(nameof(HasUsableTopology));
        OnPropertyChanged(nameof(GuideText));
        OnPropertyChanged(nameof(SelectedCount));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _refreshTimer?.Stop();
        _refreshTimer = null;

        foreach (var core in AllCores)
            core.PropertyChanged -= OnCoreSelectionChanged;

        GC.SuppressFinalize(this);
    }

    ~CpuCoreManagementViewModel() => Dispose();
}
