// File: GameOptimizer.Shell/ViewModels/RepairCenterViewModel.cs
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Events;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Utilities;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.ViewModels;

[SupportedOSPlatform("windows")]
public partial class RepairCenterViewModel : ObservableObject, IDisposable
{
    private readonly IRepairService _repairService;
    private readonly ISystemScanService _scanService;
    private readonly IEventBus? _eventBus;
    private readonly IActiveOperationRegistry? _activeOperations;
    private CancellationTokenSource? _repairCts;
    private readonly CommandExecutionGate _repairOperationGate = new();
    private bool _disposed;
    private readonly object _ctsLock = new();
    private LocalizationService Loc => LocalizationService.Instance;
    
    private static string ScanCachePath => AppPaths.ScanCacheFilePath;
    private const string FullRestoreActionId = "__full_restore__";
    private const string OpenRestoreWizardActionId = "__open_restore_wizard__";
    private const string ResetPowerDefaultsActionId = "__reset_power_defaults__";
    private const string OpenDeviceManagerActionId = "__open_device_manager__";
    private const string AdvancedScriptGuideActionId = "__advanced_script_guide__";
    private const string ScenarioCpuHighLoadActionId = "__scenario_cpu_high_load__";
    private const string ScenarioNetworkDropActionId = "__scenario_network_drop__";
    private const string ScenarioFpsDropActionId = "__scenario_fps_drop__";
    private const string ScenarioSystemCrashActionId = "__scenario_system_crash__";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGroups))]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGroups))]
    private RepairCategory? selectedCategory;

    [ObservableProperty] private bool isRepairing;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isAdmin;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private int scanProgress;
    [ObservableProperty] private string scanProgressText = string.Empty;
    [ObservableProperty] private bool hasScanResult;
    [ObservableProperty] private int scannedItemCount;
    [ObservableProperty] private int skippedScanItemCount;

    public ObservableCollection<RepairItemViewModel> AllItems { get; } = [];
    public ObservableCollection<DetectedIssueViewModel> PotentialIssues { get; } = [];
    public ObservableCollection<RepairItemViewModel> OptimizedItems { get; } = [];
    public ObservableCollection<RepairCategoryGroup> CategoryGroups { get; } = [];
    public ObservableCollection<QuickRepairActionViewModel> QuickRepairActions { get; } = [];
    public ObservableCollection<QuickRepairActionViewModel> EmergencyRecoveryActions { get; } = [];
    public ObservableCollection<QuickRepairActionViewModel> ScenarioRecoveryActions { get; } = [];
    public ObservableCollection<SystemRepairCategoryCardViewModel> SystemRepairCategories { get; } = [];
    public ObservableCollection<OptimizationRepairGroupViewModel> OptimizationRepairGroups { get; } = [];
    public ObservableCollection<RepairHistoryCardViewModel> RecentRepairs { get; } = [];

    public int TotalRepairItemsCount => AllItems.Count;
    public int PotentialIssueCount => PotentialIssues.Count;
    public int NormalItemCount => Math.Max(0, ScannedItemCount - PotentialIssues.Count - SkippedScanItemCount);
    public int RepairableIssueCount => PotentialIssues.Count(issue => issue.CanRepair);
    public int ReadOnlyIssueCount => PotentialIssues.Count(issue => issue.Availability == RepairExecutionAvailability.ReadOnly);
    public int SafetyBlockedIssueCount => PotentialIssues.Count(issue =>
        issue.Availability is RepairExecutionAvailability.SafetyBlocked or RepairExecutionAvailability.FailClosed);
    public bool HasNoDetectedIssues => HasScanResult && PotentialIssueCount == 0;
    public QuickRepairActionViewModel? FullRestoreAction =>
        EmergencyRecoveryActions.FirstOrDefault(action => action.ActionId == FullRestoreActionId);

    public IEnumerable<RepairCategoryGroup> FilteredGroups
    {
        get
        {
            var groups = CategoryGroups.AsEnumerable();
            
            if (SelectedCategory.HasValue)
                groups = groups.Where(g => g.Category == SelectedCategory.Value);

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                groups = groups.Select(g => new RepairCategoryGroup(
                    g.Category,
                    g.CategoryName,
                    g.CategoryIcon,
                    g.CategoryColor,
                    g.Items.Where(i => 
                        i.Item.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                        i.Item.Description.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                    ).ToList()
                )).Where(g => g.Items.Count > 0);
            }

            return groups;
        }
    }

    public RepairCenterViewModel(IRepairService repairService, ISystemScanService scanService,
        IEventBus? eventBus = null, IActiveOperationRegistry? activeOperations = null)
    {
        _repairService = repairService;
        _scanService = scanService;
        _eventBus = eventBus;
        _activeOperations = activeOperations;
        IsAdmin = CheckIsAdmin();
    }

    public void Initialize()
    {
        LoadAllItems();
        BuildQuickRepairActions();
        BuildEmergencyRecoveryActions();
        BuildScenarioRecoveryActions();
        BuildCategoryGroups();
        BuildSystemRepairCategories();
        BuildOptimizationRepairGroups();
        RefreshRepairHistory();
        // 诊断结果必须反映当前机器状态，不能把旧缓存重新呈现为“当前异常”。
        ClearScanCache();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        RefreshDashboardState();
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        // Why: Invoke 同步阻塞，后台线程触发语言切换时会与 UI 线程互等死锁。
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            BuildQuickRepairActions();
            BuildEmergencyRecoveryActions();
            BuildScenarioRecoveryActions();
            BuildCategoryGroups();
            BuildSystemRepairCategories();
            BuildOptimizationRepairGroups();
            RefreshRepairHistory();
            OnPropertyChanged(nameof(FilteredGroups));
            RefreshDashboardState();
        });
    }

    private static bool CheckIsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public void Cleanup() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsRepairing = false;
        _repairOperationGate.Exit();
        foreach (var item in AllItems)
        {
            item.IsRepairing = false;
            item.Progress = 0;
        }
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        lock (_ctsLock)
        {
            _repairCts?.Cancel();
            _repairCts?.Dispose();
            _repairCts = null;
        }
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        GC.SuppressFinalize(this);
    }

    private void LoadAllItems()
    {
        AllItems.Clear();
        foreach (var item in _repairService.GetAllRepairItems())
            AllItems.Add(new RepairItemViewModel(item));
    }

    private void BuildQuickRepairActions()
    {
        QuickRepairActions.Clear();
        AddQuickRepairAction("repair.store", IconGlyphs.Package, "Microsoft Store 无法打开", "商店进程崩溃、闪退或加载白屏", "#2010B981", "商店组件", "一键修复");
        AddQuickRepairAction("repair.audio_service", IconGlyphs.AudioMute, "系统无声音", "扬声器图标显示叉号，或音频设备未识别", "#207C3AED", "声音故障", "一键修复");
        AddQuickRepairAction("repair.audio_endpoint", IconGlyphs.Headphone, "耳机或扬声器没有输出", "插入耳机后无声音，或默认播放设备无法正常输出", "#207C3AED", "声音故障", "一键修复");
        AddQuickRepairAction("repair.audio_driver", IconGlyphs.Audio, "音频设备识别异常", "声卡或耳机设备消失、设备管理器显示异常", "#207C3AED", "声音故障", "重新检测");
        AddQuickRepairAction("repair.network_adapter", IconGlyphs.Wifi, "Wi-Fi 无法连接", "搜不到 Wi-Fi、连接后无网络或频繁掉线", "#2022D3EE", "网络异常", "一键修复");
        AddQuickRepairAction("repair.dns", IconGlyphs.Dns, "网站域名无法解析", "网络已连接但网页提示找不到服务器或 DNS 错误", "#2022D3EE", "网络异常", "一键修复");
        AddQuickRepairAction("repair.winsock", IconGlyphs.Network, "网络连接异常", "应用无法联网、代理已关闭但连接仍然失败", "#2022D3EE", "网络异常", "一键修复");
        AddQuickRepairAction("repair.tcp_ip", IconGlyphs.Ethernet, "网络协议栈异常", "有线或无线网络频繁断开、无法获取有效网络配置", "#2022D3EE", "网络异常", "一键修复");
        AddQuickRepairAction("repair.network_reset", IconGlyphs.Refresh, "网络完全重置", "常规网络修复无效时，重置网络配置并按提示重启", "#20F97316", "需重启", "执行重置");
        AddQuickRepairAction("repair.bluetooth_adapter", IconGlyphs.Bluetooth, "蓝牙打不开", "蓝牙开关消失、配对失败或设备识别异常", "#203B82F6", "外设连接", "一键修复");
        AddQuickRepairAction("repair.taskbar", IconGlyphs.Task, "任务栏无响应", "任务栏卡死、图标不显示或右键无菜单", "#202563EB", "桌面外壳", "一键修复");
        AddQuickRepairAction("repair.context_menu", IconGlyphs.Menu, "右键菜单卡顿", "右键打开慢，菜单卡几秒才出现", "#20F97316", "桌面外壳", "一键修复");
        AddQuickRepairAction("repair.voice_input", IconGlyphs.Microphone, "修复语音输入", "修复语音输入快捷键、语音服务与输入组件异常", "#20EC4899", "输入异常", "一键修复");
        AddQuickRepairAction("repair.revert.win_key_shortcuts", IconGlyphs.Key, "Win 键快捷键失效", "恢复 Win+V、Win+Shift+S、Win+R 等系统快捷键", "#20A855F7", "快捷键异常", "一键恢复");
        AddQuickRepairAction("repair.webcam", IconGlyphs.Monitor, "摄像头无法使用", "修复摄像头调用失败、权限异常与设备状态问题", "#20F59E0B", "设备异常", "一键修复");
        AddQuickRepairAction("repair.security_center", IconGlyphs.Shield, "Windows 安全中心打不开", "恢复安全中心服务与核心组件注册状态", "#20EF4444", "系统安全", "一键修复");
        AddQuickRepairAction("repair.windows_search", IconGlyphs.Search, "Windows 搜索无法使用", "开始菜单或资源管理器搜索无结果、长时间卡在加载中", "#20A855F7", "系统组件", "检查并修复");
        AddQuickRepairAction("repair.search_index", IconGlyphs.Search, "文件搜索结果不完整", "文件明明存在却搜索不到，或搜索索引明显落后", "#20A855F7", "系统组件", "检查并修复");
        AddQuickRepairAction("repair.windows_update", IconGlyphs.Update, "Windows 更新失败", "更新反复下载失败、安装卡住或显示错误代码", "#20F97316", "系统组件", "检查并修复");
        AddQuickRepairAction("repair.sfc_dism", IconGlyphs.ShieldCheck, "系统文件损坏", "系统组件报错、程序频繁崩溃或文件完整性检查失败", "#20EF4444", "系统组件", "检查并修复");
        AddQuickRepairAction("repair.chkdsk", IconGlyphs.Disk, "磁盘错误检查", "磁盘读写报错、文件系统提示需要扫描修复", "#20F59E0B", "存储设备", "检查并修复");
        AddQuickRepairAction("repair.recycle_bin", IconGlyphs.Delete, "回收站无法清空", "清空回收站失败、仍显示残留文件或提示访问被拒绝", "#20F59E0B", "存储设备", "检查并修复");
        AddQuickRepairAction("repair.xbox", IconGlyphs.Game, "Xbox 服务异常", "修复 Xbox 服务与游戏相关系统组件", "#2022C55E", "游戏组件", "一键修复");
        AddQuickRepairAction("repair.game_mode", IconGlyphs.Boost, "游戏模式异常", "恢复游戏模式及相关性能策略到稳定状态", "#20F97316", "游戏性能", "一键修复");
    }

    private void BuildEmergencyRecoveryActions()
    {
        EmergencyRecoveryActions.Clear();
        EmergencyRecoveryActions.Add(new QuickRepairActionViewModel(
            OpenRestoreWizardActionId,
            "🧭",
            "系统还原向导",
            "当修复无效时优先回滚到稳定还原点，适合崩溃/蓝屏后兜底。",
            "#2014B8A6",
            QuickRepairActionType.OpenSystemRestoreWizard,
            null,
            actionTag: "推荐第一步",
            actionButtonText: "打开向导"));

        EmergencyRecoveryActions.Add(new QuickRepairActionViewModel(
            ResetPowerDefaultsActionId,
            "🔋",
            "恢复默认电源",
            "CPU 占用异常、频率调度异常时，恢复系统默认电源计划。",
            "#20F59E0B",
            QuickRepairActionType.ResetPowerDefaults,
            null,
            actionTag: "CPU 100%优先",
            actionButtonText: "恢复默认"));

        EmergencyRecoveryActions.Add(new QuickRepairActionViewModel(
            OpenDeviceManagerActionId,
            "🧩",
            "驱动重装入口",
            "网速骤降、显卡/网卡异常时，打开设备管理器执行驱动重装。",
            "#2022D3EE",
            QuickRepairActionType.OpenDeviceManager,
            null,
            actionTag: "硬件问题",
            actionButtonText: "打开设备管理器"));

        EmergencyRecoveryActions.Add(new QuickRepairActionViewModel(
            FullRestoreActionId,
            "🧰",
            "全量恢复",
            "一键恢复常见优化项与核心系统设置到默认状态，作为兜底恢复。",
            "#20DC2626",
            QuickRepairActionType.FullRestore,
            null,
            actionTag: "高风险",
            actionButtonText: "执行全量恢复",
            isHighRisk: true));

        EmergencyRecoveryActions.Add(new QuickRepairActionViewModel(
            AdvancedScriptGuideActionId,
            "🧪",
            "高级恢复脚本指引",
            "当常规修复和还原点都无效时，提供管理员 PowerShell 深度恢复指引。",
            "#20BE123C",
            QuickRepairActionType.ShowAdvancedScriptGuide,
            null,
            actionTag: "专家模式",
            actionButtonText: "查看指引",
            isHighRisk: true));
    }

    private void BuildScenarioRecoveryActions()
    {
        ScenarioRecoveryActions.Clear();

        ScenarioRecoveryActions.Add(new QuickRepairActionViewModel(
            ScenarioCpuHighLoadActionId,
            "🔥",
            "CPU 100% 长时间占用",
            "优先恢复默认电源计划，回退激进调度参数，再复测占用曲线。",
            "#20F97316",
            QuickRepairActionType.ScenarioFallback,
            null,
            actionTag: "推荐路径 A",
            actionButtonText: "按路径处理"));

        ScenarioRecoveryActions.Add(new QuickRepairActionViewModel(
            ScenarioNetworkDropActionId,
            "📉",
            "网速骤降 / 延迟飙升",
            "先执行 WiFi 修复，再进入设备管理器重装网卡驱动并重启验证。",
            "#203B82F6",
            QuickRepairActionType.ScenarioFallback,
            null,
            actionTag: "推荐路径 B",
            actionButtonText: "按路径处理"));

        ScenarioRecoveryActions.Add(new QuickRepairActionViewModel(
            ScenarioFpsDropActionId,
            "🎯",
            "游戏帧率突然下降",
            "先恢复游戏模式默认配置，再检查显卡驱动并重装后复测游戏。",
            "#2022D3EE",
            QuickRepairActionType.ScenarioFallback,
            null,
            actionTag: "推荐路径 C",
            actionButtonText: "按路径处理"));

        ScenarioRecoveryActions.Add(new QuickRepairActionViewModel(
            ScenarioSystemCrashActionId,
            "💥",
            "系统崩溃反复出现",
            "优先系统还原；无效时再执行全量恢复和高级脚本指引。",
            "#20DC2626",
            QuickRepairActionType.ScenarioFallback,
            null,
            actionTag: "推荐路径 D",
            actionButtonText: "按路径处理",
            isHighRisk: true));
    }

    private void AddQuickRepairAction(
        string repairId,
        string icon,
        string fallbackTitle,
        string description,
        string accent,
        string actionTag,
        string actionButtonText)
    {
        var itemVm = FindRepairItemVmById(repairId);
        // “常见问题”是功能目录，不是“当前可写入项目”的过滤结果。
        // 每个预置项都应可见；不可执行的项保留详情入口并明确显示其
        // 诊断/安全拦截状态，绝不伪装成一键修复。
        var item = itemVm?.Item;
        RepairExecutionAvailability availability = itemVm?.Availability
            ?? RepairExecutionAvailability.FailClosed;
        string title = itemVm?.LocalizedName ?? fallbackTitle;

        QuickRepairActions.Add(new QuickRepairActionViewModel(
            repairId,
            icon,
            title,
            description,
            accent,
            QuickRepairActionType.RepairItem,
            item?.Id,
            actionTag,
            itemVm?.CanRepair == true ? actionButtonText : "不可用",
            availability: availability));
    }

    private RepairItemViewModel? FindRepairItemVmById(string repairId) =>
        AllItems.FirstOrDefault(i => i.Item.Id.Equals(repairId, StringComparison.OrdinalIgnoreCase));

    private void BuildCategoryGroups()
    {
        CategoryGroups.Clear();
        var loc = LocalizationService.Instance;
        
        var categoryInfo = new Dictionary<RepairCategory, (string NameKey, string Icon, string Color)>
        {
            { RepairCategory.Network, ("Repair.Category.Network", "🌐", "#22D3EE") },
            { RepairCategory.Audio, ("Repair.Category.Audio", "🔊", "#A78BFA") },
            { RepairCategory.Bluetooth, ("Repair.Category.Bluetooth", "📱", "#3B82F6") },
            { RepairCategory.Display, ("Repair.Category.Display", "🖥️", "#F59E0B") },
            { RepairCategory.Peripheral, ("Repair.Category.Peripheral", "🖱️", "#EC4899") },
            { RepairCategory.Storage, ("Repair.Category.Storage", "💾", "#10B981") },
            { RepairCategory.SystemCore, ("Repair.Category.SystemCore", "🛡️", "#EF4444") },
            { RepairCategory.UwpApps, (loc.CurrentLanguage == "en-US" ? "UWP Apps" : "UWP 应用", "🧩", "#14B8A6") },
            { RepairCategory.SystemFramework, (loc.CurrentLanguage == "en-US" ? "System Framework" : "系统框架", "🧱", "#F97316") },
            { RepairCategory.OptimizationRevert, ("Repair.Category.OptimizationRevert", "↩️", "#71717A") }
        };

        var grouped = AllItems.GroupBy(i => i.Item.Category);
        
        foreach (var group in grouped.OrderBy(g => GetCategoryOrder(g.Key)))
        {
            if (categoryInfo.TryGetValue(group.Key, out var info))
            {
                CategoryGroups.Add(new RepairCategoryGroup(
                    group.Key,
                    loc.Get(info.NameKey),
                    info.Icon,
                    info.Color,
                    group.ToList()
                ));
            }
        }
        
        OnPropertyChanged(nameof(FilteredGroups));
    }

    private static int GetCategoryOrder(RepairCategory category) => category switch
    {
        RepairCategory.Network => 0,
        RepairCategory.Audio => 1,
        RepairCategory.Bluetooth => 2,
        RepairCategory.Display => 3,
        RepairCategory.Peripheral => 4,
        RepairCategory.Storage => 5,
        RepairCategory.SystemCore => 6,
        RepairCategory.UwpApps => 7,
        RepairCategory.SystemFramework => 8,
        RepairCategory.OptimizationRevert => 9,
        _ => 99
    };

    private void BuildSystemRepairCategories()
    {
        SystemRepairCategories.Clear();
        AddSystemCategoryCard(RepairCategory.Audio, "声音与麦克风", "没有声音、麦克风异常、播放设备故障", "🔊", "#207C3AED");
        AddSystemCategoryCard(RepairCategory.Network, "网络与 Wi-Fi", "Wi-Fi 断连、无法联网、网络堆栈异常", "🌐", "#2022D3EE");
        AddSystemCategoryCard(RepairCategory.Bluetooth, "蓝牙与无线外设", "蓝牙打不开、配对失败、连接不稳定", "📶", "#203B82F6");
        AddSystemCategoryCard(RepairCategory.Display, "显示与摄像头", "摄像头调用失败、显示策略异常", "🖥️", "#20F59E0B");
        AddSystemCategoryCard(RepairCategory.SystemCore, "系统核心与安全", "安全中心、系统服务、核心组件异常", "🛡️", "#20EF4444");
        AddSystemCategoryCard(RepairCategory.UwpApps, "应用商店与 UWP", "Microsoft Store、Xbox 组件与 UWP 运行故障", "🛍️", "#2010B981");
        AddSystemCategoryCard(RepairCategory.SystemFramework, "系统框架与组件", "系统框架注册异常、组件加载异常", "🧱", "#20F97316");
        AddSystemCategoryCard(RepairCategory.Storage, "磁盘与存储", "缓存损坏、文件系统异常、存储策略问题", "💾", "#2014B8A6");
        AddSystemCategoryCard(RepairCategory.Peripheral, "外设与输入设备", "键鼠手柄等外设不可用或识别异常", "🖱️", "#20EC4899");
    }

    private void BuildOptimizationRepairGroups()
    {
        OptimizationRepairGroups.Clear();

        var optimizationItems = AllItems.Where(item => item.IsOptimizationRevert).ToList();
        if (optimizationItems.Count == 0)
        {
            return;
        }

        var networkItems = optimizationItems
            .Where(IsNetworkOptimizationItem)
            .ToList();
        AddOptimizationGroupCard("opt_network", "网络优化还原", "恢复网络延迟、吞吐、协议相关优化为系统默认值", "🌐", "#2022D3EE", "network", networkItems);

        var gamingItems = optimizationItems
            .Where(IsGamingOptimizationItem)
            .ToList();
        AddOptimizationGroupCard("opt_game", "游戏与图形优化还原", "恢复游戏模式、录制与图形策略优化为默认状态", "🎮", "#20F97316", "game", gamingItems);

        var systemItems = optimizationItems
            .Except(networkItems)
            .Except(gamingItems)
            .ToList();
        AddOptimizationGroupCard("opt_system", "系统策略优化还原", "恢复服务、计划任务与系统策略优化为微软默认", "🧰", "#2071717A", string.Empty, systemItems);
    }

    private void AddSystemCategoryCard(RepairCategory category, string title, string description, string icon, string accent)
    {
        var items = AllItems.Count(item => item.Item.Category == category);
        if (items == 0)
        {
            return;
        }

        var issues = PotentialIssues.Count(issue => MapScanCategory(issue.Issue.Category) == category);
        SystemRepairCategories.Add(new SystemRepairCategoryCardViewModel(
            category.ToString(),
            title,
            description,
            icon,
            accent,
            items,
            issues,
            "查看并修复"));
    }

    private void AddOptimizationGroupCard(
        string id,
        string title,
        string description,
        string icon,
        string accent,
        string keyword,
        IReadOnlyCollection<RepairItemViewModel> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var detectedCount = OptimizedItems.Count(item => items.Any(opt => opt.Item.Id == item.Item.Id));
        OptimizationRepairGroups.Add(new OptimizationRepairGroupViewModel(
            id,
            title,
            description,
            icon,
            accent,
            items.Count,
            detectedCount,
            keyword,
            "定位并恢复"));
    }

    private static bool IsNetworkOptimizationItem(RepairItemViewModel item)
    {
        var id = item.Item.Id;
        return id.Contains("network", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("dns", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("tcp", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("mtu", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGamingOptimizationItem(RepairItemViewModel item)
    {
        var id = item.Item.Id;
        return id.Contains("game", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("dvr", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("fullscreen", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("gpu", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshRepairHistory()
    {
        RecentRepairs.Clear();
        var history = _repairService.GetRepairHistory(8);
        foreach (var entry in history)
        {
            RecentRepairs.Add(new RepairHistoryCardViewModel(
                entry.RepairItemId,
                entry.RepairItemName,
                GetCategoryDisplayName(entry.Category),
                entry.Success,
                entry.Message,
                entry.Duration,
                ToRelativeTime(entry.Timestamp),
                entry.Timestamp));
        }
    }

    private void RefreshDashboardState()
    {
        OnPropertyChanged(nameof(TotalRepairItemsCount));
        OnPropertyChanged(nameof(PotentialIssueCount));
        OnPropertyChanged(nameof(NormalItemCount));
        OnPropertyChanged(nameof(RepairableIssueCount));
        OnPropertyChanged(nameof(ReadOnlyIssueCount));
        OnPropertyChanged(nameof(SafetyBlockedIssueCount));
        OnPropertyChanged(nameof(HasNoDetectedIssues));
        BuildSystemRepairCategories();
        BuildOptimizationRepairGroups();
    }

    [RelayCommand]
    private void ShowScanResultDetails(DetectedIssueViewModel? issueVm)
    {
        if (issueVm == null)
            return;

        Views.InfoDialog.Show(
            Application.Current.MainWindow,
            issueVm.Title,
            $"异常依据：{issueVm.Description}\n\n严重程度：{issueVm.SeverityText}\n处理建议：{issueVm.RepairStatusText}",
            IconGlyphs.Info);
    }

    [RelayCommand]
    private async Task RepairDetectedIssueAsync(DetectedIssueViewModel? issueVm)
    {
        if (issueVm?.SuggestedRepair is null || !issueVm.CanRepair)
        {
            StatusMessage = issueVm?.RepairStatusText ?? "该异常没有可执行的自动修复。";
            return;
        }

        await RepairItemInternalAsync(issueVm.SuggestedRepair, issueVm);
    }

    [RelayCommand]
    private void FocusOptimizationGroup(OptimizationRepairGroupViewModel? group)
    {
        SelectedCategory = RepairCategory.OptimizationRevert;
        SearchQuery = group?.QueryKeyword ?? string.Empty;
        StatusMessage = group == null ? string.Empty : $"已定位：{group.Title}";
    }

    private string GetCategoryDisplayName(RepairCategory category)
    {
        var loc = LocalizationService.Instance;
        return category switch
        {
            RepairCategory.Audio => loc.CurrentLanguage == "en-US" ? "Audio" : "音频",
            RepairCategory.Network => loc.CurrentLanguage == "en-US" ? "Network" : "网络",
            RepairCategory.Bluetooth => loc.CurrentLanguage == "en-US" ? "Bluetooth" : "蓝牙",
            RepairCategory.Display => loc.CurrentLanguage == "en-US" ? "Display" : "显示",
            RepairCategory.Peripheral => loc.CurrentLanguage == "en-US" ? "Peripheral" : "外设",
            RepairCategory.Storage => loc.CurrentLanguage == "en-US" ? "Storage" : "存储",
            RepairCategory.SystemCore => loc.CurrentLanguage == "en-US" ? "System Core" : "系统核心",
            RepairCategory.UwpApps => loc.CurrentLanguage == "en-US" ? "UWP Apps" : "UWP 应用",
            RepairCategory.SystemFramework => loc.CurrentLanguage == "en-US" ? "System Framework" : "系统框架",
            RepairCategory.OptimizationRevert => loc.CurrentLanguage == "en-US" ? "Optimization Revert" : "优化还原",
            _ => category.ToString()
        };
    }

    private static RepairCategory MapScanCategory(ScanCategory category) => category switch
    {
        ScanCategory.Network => RepairCategory.Network,
        ScanCategory.Audio => RepairCategory.Audio,
        ScanCategory.Bluetooth => RepairCategory.Bluetooth,
        ScanCategory.Display => RepairCategory.Display,
        ScanCategory.Peripheral => RepairCategory.Peripheral,
        ScanCategory.Storage => RepairCategory.Storage,
        ScanCategory.System => RepairCategory.SystemCore,
        ScanCategory.Optimization => RepairCategory.OptimizationRevert,
        _ => RepairCategory.SystemCore
    };

    private static string ToRelativeTime(DateTime timestamp)
    {
        var span = DateTime.Now - timestamp;
        if (span.TotalSeconds < 60) return "刚刚";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} 天前";
        return timestamp.ToString("yyyy-MM-dd HH:mm");
    }

    private CancellationToken GetNewRepairToken()
    {
        lock (_ctsLock)
        {
            _repairCts?.Cancel();
            _repairCts?.Dispose();
            _repairCts = new CancellationTokenSource();
            return _repairCts.Token;
        }
    }

    [RelayCommand]
    private Task RepairItemAsync(RepairItemViewModel? itemVm) =>
        RepairItemInternalAsync(itemVm, null);

    private async Task RepairItemInternalAsync(RepairItemViewModel? itemVm, DetectedIssueViewModel? detectedIssue)
    {
        if (itemVm == null || itemVm.IsRepairing || IsScanning || IsRepairing || _disposed ||
            !_repairOperationGate.TryEnter()) return;

        if (!itemVm.CanRepair)
        {
            _repairOperationGate.Exit();
            StatusMessage = $"{itemVm.LocalizedName} 当前{itemVm.AvailabilityText}，不会执行系统修改。";
            Views.InfoDialog.Show(
                Application.Current.MainWindow,
                "修复未执行",
                StatusMessage,
                IconGlyphs.Info);
            return;
        }

        IsRepairing = true;
        itemVm.IsRepairing = true;
        itemVm.Progress = 0;
        try
        {
            StatusMessage = string.Format(Loc.Get("Repair.Checking"), itemVm.LocalizedName);

            bool isHealthy;
            try
            {
                isHealthy = await itemVm.Item.CheckHealthAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"无法确认“{itemVm.LocalizedName}”当前是否异常，未执行修复。";
                Views.InfoDialog.Show(
                    Application.Current.MainWindow,
                    "修复未执行",
                    $"检查没有得到可信结果，因此没有修改系统。\n\n原因：{ex.Message}",
                    IconGlyphs.Warning);
                return;
            }

            if (isHealthy)
            {
                StatusMessage = $"未发现“{itemVm.LocalizedName}”对应的异常，未执行修复。";
                Views.InfoDialog.Show(
                    Application.Current.MainWindow,
                    "无需修复",
                    "本次即时检查未发现该项异常，所以没有修改系统设置。若问题仍存在，请先重新扫描并查看具体检测依据。",
                    IconGlyphs.Success);
                return;
            }

            // P1-REPAIR-001: 权限只在确认异常后才检查，避免普通用户误点时无谓请求提升。
            if (!GameOptimizer.Core.Services.PermissionChecker.IsRunningAsAdministrator())
            {
                StatusMessage = Loc.Get("Repair.AdminRequired");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Views.InfoDialog.Show(Application.Current.MainWindow,
                        Loc.Get("Common.AdminRequired"),
                        Loc.Get("Repair.AdminRequiredMessage"),
                        Constants.IconGlyphs.Lock);
                });
                return;
            }

            var detectionSummary = detectedIssue is null
                ? "即时检查发现该修复项对应的状态异常。"
                : $"本次扫描发现：{detectedIssue.Description}";
            var confirmationContext = new RepairConfirmationContext(
                IsHealthy: false,
                DetectionSummary: detectionSummary,
                PlannedChanges: OrchestratedRepairChangeExecutor.GetPlannedChangeSummaries(itemVm.Item.Id),
                RecoverySummary: itemVm.Item.IsRisky
                    ? "此操作可能影响现有配置或需要重启；执行器会保留可用的恢复材料，仍请在完成后复测。"
                    : "执行器会在操作前核对状态并记录结果；若执行失败，会按可用恢复材料尝试回退。");

            var owner = Application.Current.MainWindow;
            var confirmed = Views.RepairConfirmDialog.Show(owner, itemVm.Item, confirmationContext);
            if (!confirmed)
            {
                StatusMessage = Loc.Get("Repair.Cancelled");
                return;
            }

            StatusMessage = string.Format(Loc.Get("Repair.Repairing"), itemVm.LocalizedName);
            var ct = GetNewRepairToken();
            using var activeLease = _activeOperations?.Begin(
                $"repair.item.{itemVm.Item.Id}",
                $"修复：{itemVm.LocalizedName}",
                ActiveOperationRisk.Mutating,
                ActiveOperationClosePolicy.CancelAndDrain,
                () => { lock (_ctsLock) _repairCts?.Cancel(); });
            var progress = new Progress<RepairProgress>(p =>
            {
                if (_disposed) return;
                // Why: Progress 回调可能在后台线程，Invoke 同步阻塞有死锁风险。
                _ = Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    itemVm.Progress = p.PercentComplete;
                    StatusMessage = p.Message;
                });
            });

            var result = await _repairService.RepairAsync(itemVm.Item, progress, ct);
            if (result.ChangeReceipt is { } receipt)
            {
                activeLease?.UpdateHandoff(new ActiveOperationHandoff(
                    [receipt.ReceiptId.ToString("D")],
                    $"Repair item: {receipt.RepairItemId}"));
            }
            var presentation = CommandResultPresentationMapper.FromRepair(result);
            StatusMessage = $"{itemVm.LocalizedName}: {presentation.DisplayText}";

            if (result.Success)
            {
                // VM-013: 发布修复完成事件，通知优化模块刷新状态
                if (_eventBus != null)
                {
                    var mayAffectOptimization = itemVm.IsOptimizationRevert || 
                        itemVm.Item.Category == RepairCategory.OptimizationRevert;
                    Debug.WriteLine($"[RepairCenterVM] Publishing RepairCompletedEvent: {itemVm.Item.Id}, MayAffect={mayAffectOptimization}");
                    await _eventBus.PublishAsync(new RepairCompletedEvent(itemVm.Item.Id, true, mayAffectOptimization));
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (_disposed) return;
            StatusMessage = $"{itemVm.LocalizedName}: {CommandResultPresentationMapper.FromException(new OperationCanceledException()).DisplayText}";
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            StatusMessage = $"{itemVm.LocalizedName}: {CommandResultPresentationMapper.FromException(ex).DisplayText}";
            LogRepairError(itemVm.Item.Id, itemVm.LocalizedName, ex);
        }
        finally
        {
            itemVm.IsRepairing = false;
            itemVm.Progress = 0;
            IsRepairing = false;
            _repairOperationGate.Exit();
            if (!_disposed)
            {
                RefreshRepairHistory();
            }
        }
    }

    [RelayCommand]
    private async Task ExecuteQuickRepairActionAsync(QuickRepairActionViewModel? action)
    {
        if (action == null || IsScanning || IsRepairing) return;

        switch (action.ActionType)
        {
            case QuickRepairActionType.RepairItem:
                await ExecuteRepairItemByIdAsync(action.TargetRepairId, action.Title);
                break;
            case QuickRepairActionType.FullRestore:
                await RunFullRestoreAsync();
                break;
            case QuickRepairActionType.OpenSystemRestoreWizard:
                OpenSystemRestoreWizard();
                break;
            case QuickRepairActionType.ResetPowerDefaults:
                await RunResetPowerDefaultsAsync();
                break;
            case QuickRepairActionType.OpenDeviceManager:
                OpenDeviceManagerGuide();
                break;
            case QuickRepairActionType.ShowAdvancedScriptGuide:
                await ShowAdvancedScriptGuideAsync();
                break;
            case QuickRepairActionType.ScenarioFallback:
                await ExecuteScenarioFallbackAsync(action.ActionId);
                break;
            default:
                StatusMessage = "不支持的快捷修复操作";
                break;
        }
    }

    private async Task ExecuteScenarioFallbackAsync(string scenarioId)
    {
        switch (scenarioId)
        {
            case ScenarioCpuHighLoadActionId:
            {
                var confirmed = Views.ConfirmDialog.Show(
                    Application.Current.MainWindow,
                    "CPU 100% 处理路径",
                    "将按推荐路径执行：\n1. 恢复默认电源计划\n2. 重启后复测占用\n\n是否立即执行第 1 步？",
                    IconGlyphs.Warning);
                if (!confirmed)
                {
                    StatusMessage = "已取消 CPU 100% 场景处理";
                    return;
                }
                await RunResetPowerDefaultsAsync();
                return;
            }
            case ScenarioNetworkDropActionId:
            {
                var confirmed = Views.ConfirmDialog.Show(
                    Application.Current.MainWindow,
                    "网速骤降处理路径",
                    "将按推荐路径执行：\n1. 执行 WiFi 快速修复\n2. 打开设备管理器，重装网卡驱动\n3. 重启后复测\n\n是否继续？",
                    IconGlyphs.Info);
                if (!confirmed)
                {
                    StatusMessage = "已取消网速骤降场景处理";
                    return;
                }

                await ExecuteRepairItemByIdAsync("repair.network_adapter", "WiFi");
                OpenDeviceManagerGuide();
                return;
            }
            case ScenarioFpsDropActionId:
            {
                var confirmed = Views.ConfirmDialog.Show(
                    Application.Current.MainWindow,
                    "帧率骤降处理路径",
                    "将按推荐路径执行：\n1. 恢复游戏模式默认配置\n2. 打开设备管理器重装显卡驱动\n3. 重启后复测游戏帧率\n\n是否继续？",
                    IconGlyphs.Info);
                if (!confirmed)
                {
                    StatusMessage = "已取消帧率骤降场景处理";
                    return;
                }

                await ExecuteRepairItemByIdAsync("repair.game_mode", "游戏模式");
                OpenDeviceManagerGuide();
                return;
            }
            case ScenarioSystemCrashActionId:
            {
                var confirmed = Views.ConfirmDialog.Show(
                    Application.Current.MainWindow,
                    "系统崩溃处理路径",
                    "推荐路径：\n1. 先打开系统还原向导进行回滚\n2. 若无效，再执行全量恢复\n3. 仍无效时使用高级脚本指引\n\n是否先执行第 1 步？",
                    IconGlyphs.Warning);
                if (!confirmed)
                {
                    StatusMessage = "已取消系统崩溃场景处理";
                    return;
                }

                OpenSystemRestoreWizard();
                return;
            }
            default:
                StatusMessage = "未知场景路径";
                return;
        }
    }

    private async Task ExecuteRepairItemByIdAsync(string? repairId, string actionTitle)
    {
        if (string.IsNullOrWhiteSpace(repairId))
        {
            StatusMessage = "快捷修复项配置无效";
            return;
        }

        var target = FindRepairItemVmById(repairId);
        if (target == null)
        {
            StatusMessage = $"未找到修复项: {actionTitle}";
            return;
        }

        await RepairItemAsync(target);
    }

    private void OpenSystemRestoreWizard()
    {
        var confirmed = Views.ConfirmDialog.Show(
            Application.Current.MainWindow,
            "系统还原提示",
            "将打开 Windows 系统还原向导（rstrui）。\n\n建议优先选择最近稳定还原点，恢复后重启并复测问题。\n\n是否继续？",
            IconGlyphs.Info);

        if (!confirmed)
        {
            StatusMessage = "已取消打开系统还原向导";
            return;
        }

        var (process, error) = GameOptimizer.Core.Services.SafeProcessHelper.StartProcess(
            new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });

        StatusMessage = process != null
            ? "已打开系统还原向导，请按提示完成恢复"
            : $"打开系统还原向导失败: {error}";
    }

    private async Task RunResetPowerDefaultsAsync()
    {
        if (IsScanning || IsRepairing || _disposed || !_repairOperationGate.TryEnter()) return;

        if (!GameOptimizer.Core.Services.PermissionChecker.IsRunningAsAdministrator())
        {
            _repairOperationGate.Exit();
            StatusMessage = Loc.Get("Repair.AdminRequired");
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Views.InfoDialog.Show(Application.Current.MainWindow,
                    Loc.Get("Common.AdminRequired"),
                    Loc.Get("Repair.AdminRequiredMessage"),
                    Constants.IconGlyphs.Lock);
            });
            return;
        }

        var powerVm = FindRepairItemVmById("repair.power");
        if (powerVm == null)
        {
            _repairOperationGate.Exit();
            StatusMessage = "未找到默认电源恢复项";
            return;
        }

        if (!powerVm.CanRepair)
        {
            _repairOperationGate.Exit();
            StatusMessage = "默认电源恢复当前未接入可验证的执行与恢复链路，未修改系统。";
            Views.InfoDialog.Show(
                Application.Current.MainWindow,
                "恢复未执行",
                StatusMessage,
                IconGlyphs.Info);
            return;
        }

        var confirmed = Views.ConfirmDialog.Show(
            Application.Current.MainWindow,
            "恢复默认电源确认",
            "将执行 powercfg /restoredefaultschemes 恢复默认电源计划。\n\n此操作会清空自定义电源计划，是否继续？",
            IconGlyphs.Warning);

        if (!confirmed)
        {
            _repairOperationGate.Exit();
            StatusMessage = "已取消恢复默认电源";
            return;
        }

        IsRepairing = true;
        try
        {
            var ct = GetNewRepairToken();
            var progress = new Progress<RepairProgress>(p =>
            {
                if (_disposed) return;
                StatusMessage = $"恢复默认电源 - {p.Message}";
            });

            var result = await _repairService.RepairAsync(powerVm.Item, progress, ct);
            var presentation = CommandResultPresentationMapper.FromRepair(result);
            StatusMessage = $"恢复默认电源: {presentation.DisplayText}";
            if (result.Success)
            {
                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync(new RepairCompletedEvent(powerVm.Item.Id, true, false));
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (_disposed) return;
            StatusMessage = $"恢复默认电源: {CommandResultPresentationMapper.FromException(new OperationCanceledException()).DisplayText}";
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            StatusMessage = $"恢复默认电源: {CommandResultPresentationMapper.FromException(ex).DisplayText}";
        }
        finally
        {
            IsRepairing = false;
            _repairOperationGate.Exit();
            if (!_disposed)
            {
                RefreshRepairHistory();
                RefreshDashboardState();
            }
        }
    }

    private void OpenDeviceManagerGuide()
    {
        var (process, error) = GameOptimizer.Core.Services.SafeProcessHelper.StartProcess(
            new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });

        if (process == null)
        {
            StatusMessage = $"打开设备管理器失败: {error}";
            return;
        }

        StatusMessage = "设备管理器已打开，请按指引重装异常驱动";
        Views.InfoDialog.Show(
            Application.Current.MainWindow,
            "驱动重装建议",
            "建议顺序：\n" +
            "1. 优先重装网卡驱动（网速骤降）\n" +
            "2. 再重装显卡驱动（游戏帧率突降/卡顿）\n" +
            "3. 重装后重启系统并复测",
            IconGlyphs.Info);
    }

    private async Task ShowAdvancedScriptGuideAsync()
    {
        // Never copy or suggest an unpinned remote script for a recovery path.
        // A repair UI must only expose operations that have a local owner,
        // confirmation, receipt and read-back contract.
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Views.InfoDialog.Show(
                Application.Current.MainWindow,
                "高级恢复暂不可用",
                "为避免执行来源不明或无法回滚的脚本，此版本不提供在线脚本恢复。请先使用系统还原或联系技术支持。",
                IconGlyphs.Warning);
        });
        StatusMessage = "高级脚本恢复未接入：缺少本地可验证的执行与回滚链路";
    }

    private async Task RunFullRestoreAsync()
    {
        if (IsScanning || IsRepairing || _disposed || !_repairOperationGate.TryEnter()) return;

        if (!GameOptimizer.Core.Services.PermissionChecker.IsRunningAsAdministrator())
        {
            _repairOperationGate.Exit();
            StatusMessage = Loc.Get("Repair.AdminRequired");
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Views.InfoDialog.Show(Application.Current.MainWindow,
                    Loc.Get("Common.AdminRequired"),
                    Loc.Get("Repair.AdminRequiredMessage"),
                    Constants.IconGlyphs.Lock);
            });
            return;
        }

        var revertItems = AllItems.Where(i => i.IsOptimizationRevert).ToList();
        var baselineRestoreIds = new[]
        {
            "repair.game_mode",
            "repair.game_dvr",
            "repair.fullscreen_optimization",
            "repair.game_bar",
            "repair.power"
        };
        var baselineItems = AllItems
            .Where(i => baselineRestoreIds.Contains(i.Item.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var queue = revertItems
            .Concat(baselineItems)
            .GroupBy(i => i.Item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (queue.Count == 0)
        {
            _repairOperationGate.Exit();
            StatusMessage = "未检测到可执行的全量恢复项";
            return;
        }

        var confirmMessage =
            $"将执行 {queue.Count} 项恢复操作，包含优化回滚和系统默认项恢复。\n\n" +
            "此操作可能影响当前个性化设置，部分项可能需要重启后生效。\n\n是否继续？";

        var confirmed = Views.ConfirmDialog.Show(
            Application.Current.MainWindow,
            "全量恢复确认",
            confirmMessage,
            IconGlyphs.Warning);

        if (!confirmed)
        {
            _repairOperationGate.Exit();
            StatusMessage = "已取消全量恢复";
            return;
        }

        IsRepairing = true;
        var successCount = 0;
        var failedCount = 0;
        var needReboot = false;
        var failureReasons = new List<string>();

        try
        {
            var ct = GetNewRepairToken();
            using var activeLease = _activeOperations?.Begin(
                "repair.full-restore",
                "修复中心全量恢复",
                ActiveOperationRisk.RecoveryCritical,
                ActiveOperationClosePolicy.CancelAndDrain,
                () => { lock (_ctsLock) _repairCts?.Cancel(); });
            var receiptIds = new List<string>();
            for (var index = 0; index < queue.Count; index++)
            {
                ct.ThrowIfCancellationRequested();

                var itemVm = queue[index];
                var progress = new Progress<RepairProgress>(p =>
                {
                    if (_disposed) return;
                    StatusMessage = $"全量恢复 ({index + 1}/{queue.Count}) {itemVm.LocalizedName} - {p.Message}";
                });

                var result = await _repairService.RepairAsync(itemVm.Item, progress, ct);
                if (result.ChangeReceipt is { } receipt)
                {
                    receiptIds.Add(receipt.ReceiptId.ToString("D"));
                    activeLease?.UpdateHandoff(new ActiveOperationHandoff(
                        receiptIds.ToArray(),
                        $"已处理 {index + 1}/{queue.Count} 项；请在修复历史中核对。"));
                }
                if (result.Success)
                {
                    successCount++;
                    needReboot = needReboot || result.RequiresReboot;
                }
                else
                {
                    failedCount++;
                    failureReasons.Add($"{itemVm.LocalizedName}: {CommandResultPresentationMapper.FromRepair(result).DisplayText}");
                }

                if (_eventBus != null)
                {
                    var mayAffectOptimization = itemVm.IsOptimizationRevert ||
                        itemVm.Item.Category == RepairCategory.OptimizationRevert;
                    await _eventBus.PublishAsync(new RepairCompletedEvent(itemVm.Item.Id, result.Success, mayAffectOptimization));
                }
            }

            StatusMessage = $"全量恢复完成：成功 {successCount} 项，失败 {failedCount} 项" +
                            (needReboot ? "，建议重启电脑后生效" : string.Empty) +
                            (failureReasons.Count > 0 ? $"。{string.Join("；", failureReasons)}" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            if (_disposed) return;
            StatusMessage = "全量恢复已取消";
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            StatusMessage = $"全量恢复: {CommandResultPresentationMapper.FromException(ex).DisplayText}";
        }
        finally
        {
            IsRepairing = false;
            _repairOperationGate.Exit();
            if (!_disposed)
            {
                RefreshDashboardState();
            }
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SearchQuery = string.Empty;
        SelectedCategory = null;
    }

    [RelayCommand]
    private void FilterByCategory(string? categoryStr)
    {
        if (string.IsNullOrEmpty(categoryStr))
            SelectedCategory = null;
        else if (Enum.TryParse<RepairCategory>(categoryStr, out var cat))
            SelectedCategory = SelectedCategory == cat ? null : cat;
    }

    // 真实诊断有一个统一超时；不再逐个遍历“修复工具”并把前置条件当作异常。
    private static readonly TimeSpan GlobalScanTimeout = TimeSpan.FromSeconds(120);
    private CancellationTokenSource? _scanCts;
    private bool _scanCancellationRequested;

    private bool CanCancelScan() => IsScanning;
    private bool CanStartScan() => !IsScanning && !_disposed;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        if (!IsScanning || _scanCts is null)
            return;

        _scanCancellationRequested = true;
        ScanProgressText = Loc.Get("Repair.ScanCancelling");
        _scanCts.Cancel();
    }

    partial void OnIsScanningChanged(bool value)
    {
        StartScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        if (IsScanning) return;

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource(GlobalScanTimeout);
        var globalCt = _scanCts.Token;

        IsScanning = true;
        _scanCancellationRequested = false;
        ScanProgress = 0;
        PotentialIssues.Clear();
        OptimizedItems.Clear();
        ScannedItemCount = 0;
        SkippedScanItemCount = 0;
        HasScanResult = false;
        ScanProgressText = Loc.Get("Repair.PreparingScan");

        var completed = false;
        try
        {
            var progress = new Progress<ScanProgressInfo>(item =>
            {
                ScanProgress = item.TotalTasks == 0 ? 0 : item.CompletedTasks * 100 / item.TotalTasks;
                ScanProgressText = $"正在诊断：{item.CurrentTask}（{item.CompletedTasks}/{item.TotalTasks}）";
            });
            var result = await _scanService.ScanAllAsync(progress, globalCt).WaitAsync(globalCt);
            ScannedItemCount = result.TotalScanned;
            foreach (var issue in result.Issues)
            {
                var suggestedRepair = string.IsNullOrWhiteSpace(issue.SuggestedRepairId)
                    ? null
                    : FindRepairItemVmById(issue.SuggestedRepairId);
                PotentialIssues.Add(new DetectedIssueViewModel(issue, suggestedRepair));
            }
            HasScanResult = true;
            ScanProgressText = PotentialIssues.Count == 0
                ? "诊断完成：未发现需要处理的异常。"
                : $"诊断完成：发现 {PotentialIssues.Count} 项有明确依据的异常。";
            completed = true;
        }
        catch (OperationCanceledException)
        {
            HasScanResult = true; // 显示已收集到的部分结果
            ScanProgressText = _scanCancellationRequested
                ? Loc.Get("Repair.ScanCancelled")
                : Loc.Get("Repair.ScanTimeout");
        }
        catch (Exception ex)
        {
            ScanProgressText = string.Format(Loc.Get("Repair.ScanError"), ex.Message);
        }
        finally
        {
            IsScanning = false;
            if (completed)
                ScanProgress = 100;
            RefreshDashboardState();
        }
    }

    [RelayCommand]
    private void ClearScanResult()
    {
        PotentialIssues.Clear();
        OptimizedItems.Clear();
        ScannedItemCount = 0;
        SkippedScanItemCount = 0;
        HasScanResult = false;
        ScanProgressText = string.Empty;
        foreach (var item in AllItems)
        {
            item.HasPotentialIssue = false;
            item.IsOptimized = false;
            item.IsScanEvaluated = false;
        }
        ClearScanCache(); // P3-002: 清除缓存
        RefreshDashboardState();
    }
    
    // P3-002: 清除缓存文件
    private static void ClearScanCache()
    {
        try
        {
            if (File.Exists(ScanCachePath))
                GameOptimizer.Core.Services.SafeFileHelper.DeleteFile(ScanCachePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RepairCenterVM] Failed to clear scan cache: {ex.Message}");
        }
    }
    
    // 错误日志记录
    private static void LogRepairError(string itemId, string itemName, Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppPaths.LogsDir, "repair_errors.log");
            var logEntry = new StringBuilder();
            logEntry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ========== 修复错误 ==========");
            logEntry.AppendLine($"项目ID: {itemId}");
            logEntry.AppendLine($"项目名称: {itemName}");
            logEntry.AppendLine($"错误类型: {ex.GetType().Name}");
            logEntry.AppendLine($"错误消息: {ex.Message}");
            logEntry.AppendLine($"堆栈跟踪:\n{ex.StackTrace}");
            logEntry.AppendLine();
            
            File.AppendAllText(logPath, logEntry.ToString());
            Debug.WriteLine($"[RepairCenterVM] 错误已记录到: {logPath}");
        }
        catch (Exception logEx)
        {
            Debug.WriteLine($"[RepairCenterVM] 记录错误日志失败: {logEx.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportScanReportAsync()
    {
        if (!HasScanResult)
        {
            StatusMessage = Loc.Get("Repair.NoScanResult");
            return;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"RepairScan_{timestamp}.txt";
            Directory.CreateDirectory(AppPaths.ReportsExportDir);
            var filePath = Path.Combine(AppPaths.ReportsExportDir, fileName);

            var report = new StringBuilder();
            report.AppendLine("=".PadRight(60, '='));
            report.AppendLine($"{Loc.Get("Repair.ReportTitle")}");
            report.AppendLine($"{Loc.Get("Repair.ScanTime")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine("=".PadRight(60, '='));
            report.AppendLine();

            if (OptimizedItems.Count > 0)
            {
                report.AppendLine($"{Loc.Get("Repair.OptimizedSection")} ({OptimizedItems.Count}):");
                report.AppendLine("-".PadRight(60, '-'));
                foreach (var item in OptimizedItems)
                {
                    report.AppendLine($"  • {item.LocalizedName}");
                    report.AppendLine($"    {item.LocalizedDescription}");
                    report.AppendLine();
                }
            }

            if (PotentialIssues.Count > 0)
            {
                report.AppendLine($"{Loc.Get("Repair.IssuesSection")} ({PotentialIssues.Count}):");
                report.AppendLine("-".PadRight(60, '-'));
                foreach (var item in PotentialIssues)
                {
                    report.AppendLine($"  • {item.Title}（{item.SeverityText}）");
                    report.AppendLine($"    异常依据：{item.Description}");
                    report.AppendLine($"    处理建议：{item.RepairStatusText}");
                    report.AppendLine();
                }
            }

            report.AppendLine("=".PadRight(60, '='));
            report.AppendLine(Loc.Get("Repair.ReportFooter"));
            report.AppendLine("=".PadRight(60, '='));

            await GameOptimizer.Core.Services.SafeFileHelper.WriteAllTextAsync(filePath, report.ToString());
            StatusMessage = Loc.Get("Repair.ReportExported", filePath);

            var confirmed = Views.ConfirmDialog.Show(
                Application.Current.MainWindow,
                Loc.Get("Repair.ExportSuccessTitle"),
                Loc.Get("Repair.ExportSuccessMsg", fileName),
                Constants.IconGlyphs.Check);

            if (confirmed)
            {
                GameOptimizer.Core.Services.SafeProcessHelper.StartProcess(new ProcessStartInfo(filePath)
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Get("Repair.ExportFailed", ex.Message);
            Debug.WriteLine($"[RepairCenterVM] Export scan report failed: {ex}");
        }
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        try
        {
            var logsDir = AppPaths.LogsDir;
            if (!Directory.Exists(logsDir))
            {
                GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(logsDir, out _);
            }

            var (process, error) = GameOptimizer.Core.Services.SafeProcessHelper.StartProcess(
                new ProcessStartInfo("explorer.exe", logsDir)
                {
                    UseShellExecute = true
                });

            if (process == null)
            {
                StatusMessage = LocalizationService.Instance.Get("Repair.CannotOpenLogDir", error ?? "");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.Instance.Get("Repair.OpenLogDirFailed", ex.Message);
        }
    }
}

public partial class RepairItemViewModel : ObservableObject
{
    public RepairItem Item { get; }
    
    [ObservableProperty] private bool isRepairing;
    [ObservableProperty] private int progress;
    [ObservableProperty] private bool hasPotentialIssue;
    [ObservableProperty] private bool isOptimized;
    [ObservableProperty] private bool isScanEvaluated;

    public string LocalizedName
    {
        get
        {
            if (IsOptimizationRevert && !string.IsNullOrEmpty(Item.NameKey))
            {
                var loc = LocalizationService.Instance;
                var isEnglish = loc.CurrentLanguage == "en-US";
                var title = OptimizationTranslations.Instance.GetTitle(Item.NameKey, Item.Name, isEnglish);
                var revertPrefix = loc.Get("Repair.RevertPrefix");
                return title.StartsWith("还原:") || title.StartsWith("Revert:") ? title : revertPrefix + title;
            }
            if (!string.IsNullOrEmpty(Item.NameKey))
                return LocalizationService.Instance.Get(Item.NameKey);
            return Item.Name;
        }
    }
    
    public string LocalizedDescription
    {
        get
        {
            if (IsOptimizationRevert && !string.IsNullOrEmpty(Item.DescriptionKey))
            {
                var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
                return OptimizationTranslations.Instance.GetDescription(Item.DescriptionKey, Item.Description, isEnglish);
            }
            if (!string.IsNullOrEmpty(Item.DescriptionKey))
                return LocalizationService.Instance.Get(Item.DescriptionKey);
            return Item.Description;
        }
    }

    public string RepairButtonText => LocalizationService.Instance.Get("Repair.Repair");
    public string RestoreButtonText => LocalizationService.Instance.Get("Repair.Restore");
    public string OptimizedBadgeText => LocalizationService.Instance.Get("Repair.Optimized");
    public string MaybeAbnormalBadgeText => LocalizationService.Instance.Get("Repair.MaybeAbnormal");
    public RepairExecutionAvailability Availability => Item.Availability;
    public bool CanRepair => Item.CanMutate;
    public string AvailabilityText => Availability switch
    {
        RepairExecutionAvailability.WorkingBidirectional => "可修复",
        RepairExecutionAvailability.ReadOnly => "仅检测",
        RepairExecutionAvailability.SafetyBlocked => "已安全拦截",
        _ => "暂不可用"
    };

    public bool IsOptimizationRevert => 
        Item.Category == RepairCategory.OptimizationRevert || 
        !string.IsNullOrEmpty(Item.RelatedOptimization);

    public RepairItemViewModel(RepairItem item) => Item = item;
}

/// <summary>
/// A scan finding is distinct from a repair tool: it exists only when the
/// diagnostic service observed a concrete abnormal condition on this machine.
/// </summary>
public sealed class DetectedIssueViewModel
{
    public ScanIssue Issue { get; }
    public RepairItemViewModel? SuggestedRepair { get; }
    public string Title => Issue.Title;
    public string Description => Issue.Description;
    public bool CanRepair => SuggestedRepair?.CanRepair == true;
    public RepairExecutionAvailability Availability => SuggestedRepair?.Availability
        ?? RepairExecutionAvailability.ReadOnly;
    public string SeverityText => Issue.Severity switch
    {
        GameOptimizer.Core.Services.Repair.IssueSeverity.Critical => "严重异常",
        GameOptimizer.Core.Services.Repair.IssueSeverity.Error => "异常",
        GameOptimizer.Core.Services.Repair.IssueSeverity.Warning => "需要关注",
        _ => "提示"
    };
    public string RepairStatusText => SuggestedRepair switch
    {
        null => "已检测到异常，但当前没有可验证的自动修复；请查看详情后人工处理。",
        { CanRepair: true } repair => $"建议执行“{repair.LocalizedName}”。",
        _ => $"已检测到异常；“{SuggestedRepair.LocalizedName}”当前{SuggestedRepair.AvailabilityText}，不会自动执行。"
    };
    public string RepairButtonText => CanRepair ? "修复" : "仅详情";

    public DetectedIssueViewModel(ScanIssue issue, RepairItemViewModel? suggestedRepair)
    {
        Issue = issue;
        SuggestedRepair = suggestedRepair;
    }
}

public class RepairCategoryGroup
{
    public RepairCategory Category { get; }
    public string CategoryName { get; }
    public string CategoryIcon { get; }
    public string CategoryColor { get; }
    public IReadOnlyList<RepairItemViewModel> Items { get; }
    public int ItemCount => Items.Count;
    public string LocalizedItemCount => string.Format(LocalizationService.Instance.Get("Repair.ItemCount"), Items.Count);

    public RepairCategoryGroup(RepairCategory category, string name, string icon, string color, IReadOnlyList<RepairItemViewModel> items)
    {
        Category = category;
        CategoryName = name;
        CategoryIcon = icon;
        CategoryColor = color;
        Items = items;
    }
}

public sealed class SystemRepairCategoryCardViewModel
{
    public string CategoryFilterValue { get; }
    public string Title { get; }
    public string Description { get; }
    public string Icon { get; }
    public Brush AccentBackground { get; }
    public int ItemCount { get; }
    public int PotentialIssueCount { get; }
    public string ActionText { get; }

    public SystemRepairCategoryCardViewModel(
        string categoryFilterValue,
        string title,
        string description,
        string icon,
        string accentHex,
        int itemCount,
        int potentialIssueCount,
        string actionText)
    {
        CategoryFilterValue = categoryFilterValue;
        Title = title;
        Description = description;
        Icon = icon;
        AccentBackground = CreateBrush(accentHex);
        ItemCount = itemCount;
        PotentialIssueCount = potentialIssueCount;
        ActionText = actionText;
    }

    private static Brush CreateBrush(string accentHex)
    {
        try
        {
            var parsed = ColorConverter.ConvertFromString(accentHex);
            if (parsed is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
        }

        return Brushes.Transparent;
    }
}

public sealed class OptimizationRepairGroupViewModel
{
    public string GroupId { get; }
    public string Title { get; }
    public string Description { get; }
    public string Icon { get; }
    public Brush AccentBackground { get; }
    public int ItemCount { get; }
    public int DetectedCount { get; }
    public string QueryKeyword { get; }
    public string ActionText { get; }

    public OptimizationRepairGroupViewModel(
        string groupId,
        string title,
        string description,
        string icon,
        string accentHex,
        int itemCount,
        int detectedCount,
        string queryKeyword,
        string actionText)
    {
        GroupId = groupId;
        Title = title;
        Description = description;
        Icon = icon;
        AccentBackground = CreateBrush(accentHex);
        ItemCount = itemCount;
        DetectedCount = detectedCount;
        QueryKeyword = queryKeyword;
        ActionText = actionText;
    }

    private static Brush CreateBrush(string accentHex)
    {
        try
        {
            var parsed = ColorConverter.ConvertFromString(accentHex);
            if (parsed is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
        }

        return Brushes.Transparent;
    }
}

public sealed class RepairHistoryCardViewModel
{
    private static readonly Brush SuccessBrush = CreateBrush("#22C55E");
    private static readonly Brush FailedBrush = CreateBrush("#F97316");

    public string RepairItemId { get; }
    public string RepairItemName { get; }
    public string CategoryName { get; }
    public bool IsSuccess { get; }
    public string Message { get; }
    public TimeSpan Duration { get; }
    public string TimeAgoText { get; }
    public DateTime Timestamp { get; }
    public string ResultText => IsSuccess ? "已修复" : "修复失败";
    public Brush ResultBrush => IsSuccess ? SuccessBrush : FailedBrush;
    public string DurationText => Duration.TotalMilliseconds < 1
        ? "-"
        : $"{Math.Max(1, (int)Duration.TotalSeconds)}s";

    public RepairHistoryCardViewModel(
        string repairItemId,
        string repairItemName,
        string categoryName,
        bool isSuccess,
        string message,
        TimeSpan duration,
        string timeAgoText,
        DateTime timestamp)
    {
        RepairItemId = repairItemId;
        RepairItemName = repairItemName;
        CategoryName = categoryName;
        IsSuccess = isSuccess;
        Message = message;
        Duration = duration;
        TimeAgoText = timeAgoText;
        Timestamp = timestamp;
    }

    private static Brush CreateBrush(string accentHex)
    {
        try
        {
            var parsed = ColorConverter.ConvertFromString(accentHex);
            if (parsed is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
        }

        return Brushes.Transparent;
    }
}

public enum QuickRepairActionType
{
    RepairItem,
    FullRestore,
    OpenSystemRestoreWizard,
    ResetPowerDefaults,
    OpenDeviceManager,
    ShowAdvancedScriptGuide,
    ScenarioFallback
}

public sealed class QuickRepairActionViewModel
{
    public string ActionId { get; }
    public string Icon { get; }
    public string Title { get; }
    public string Description { get; }
    public Brush AccentBackground { get; }
    public QuickRepairActionType ActionType { get; }
    public string? TargetRepairId { get; }
    public string ActionTag { get; }
    public string ActionButtonText { get; }
    public bool IsHighRisk { get; }
    public RepairExecutionAvailability? Availability { get; }
    public bool IsActionAvailable => Availability is null or RepairExecutionAvailability.WorkingBidirectional;
    public string AvailabilityText => Availability switch
    {
        RepairExecutionAvailability.WorkingBidirectional => "可修复",
        RepairExecutionAvailability.ReadOnly => "仅检测",
        RepairExecutionAvailability.SafetyBlocked => "已安全拦截",
        RepairExecutionAvailability.FailClosed => "暂不可用",
        _ => "请先检测"
    };

    public QuickRepairActionViewModel(
        string actionId,
        string icon,
        string title,
        string description,
        string accentHex,
        QuickRepairActionType actionType,
        string? targetRepairId,
        string actionTag = "",
        string actionButtonText = "立即处理",
        bool isHighRisk = false,
        RepairExecutionAvailability? availability = null)
    {
        ActionId = actionId;
        Icon = icon;
        Title = title;
        Description = description;
        AccentBackground = CreateBrush(accentHex);
        ActionType = actionType;
        TargetRepairId = targetRepairId;
        ActionTag = actionTag;
        ActionButtonText = actionButtonText;
        IsHighRisk = isHighRisk;
        Availability = availability;
    }

    private static Brush CreateBrush(string accentHex)
    {
        try
        {
            var parsed = ColorConverter.ConvertFromString(accentHex);
            if (parsed is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
        }

        return Brushes.Transparent;
    }
}
