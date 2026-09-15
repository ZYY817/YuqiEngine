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
        // 判定收敛到唯一权威实现（令牌 TOKEN_ELEVATION + 管理员组 SID）。
        IsAdmin = GameOptimizer.Core.Services.WindowsElevation.IsElevated;
    }

    /// <summary>
    /// 权限提示文案：必须基于真实提权状态。已提权时不得再让用户"以管理员身份运行"，
    /// 而要说明当前真实状态（含判定原因码），否则用户会去重试一个不可能有效的动作。
    /// </summary>
    public static string BuildPermissionHint(bool isElevated, bool isAdministratorAccount, string? reasonCode, string? detail)
    {
        var elevation = $"{reasonCode} - {detail}";

        if (isElevated)
        {
            return $"当前进程令牌已提权（管理员模式）：无需再以管理员身份运行，若仍失败请按提示里的具体原因处理。\n提权判定 / Elevation: {elevation}";
        }

        return isAdministratorAccount
            ? $"当前账户在管理员组内，但进程令牌未提权（UAC 筛选令牌），需要以管理员身份重新启动。\n提权判定 / Elevation: {elevation}"
            : $"当前账户不在管理员组内，需要管理员账户或管理员凭据。\n提权判定 / Elevation: {elevation}";
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Why: 门禁与提示都读真实提权状态，不能沿用构造函数期的旧快照。
            CheckAdminPrivilege();
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
                    var elevation = GameOptimizer.Core.Services.WindowsElevation.Current;
                    ToastService.Instance.Warning(
                        _loc.Get("CoreAccel.PermissionHint"),
                        _loc.Get("CoreAccel.PermissionHintDesc") + "\n\n" + BuildPermissionHint(
                            IsAdmin,
                            GameOptimizer.Core.Services.PermissionChecker.IsAdministratorAccount(),
                            elevation.ReasonCode,
                            elevation.Detail));
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
