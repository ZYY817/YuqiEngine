// File: GameOptimizer.Shell/Navigation/NavigationCatalog.cs
using System.Collections.ObjectModel;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.Navigation;

public sealed class NavigationCatalog : INavigationCatalog
{
    private readonly List<NavigationRoute> _routes = CreateRoutes();
    private readonly Dictionary<string, NavigationRoute> _lookup;
    private readonly ObservableCollection<NavigationSectionModel> _visibleSections = new();
    private readonly ReadOnlyObservableCollection<NavigationSectionModel> _visibleSectionsView;

    public NavigationCatalog()
    {
        _lookup = BuildLookup(_routes);
        _visibleSectionsView = new(_visibleSections);
        RebuildVisibleSections();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<NavigationRoute> Routes => _routes;

    public IReadOnlyList<NavigationRoute> VisibleRoutes => _routes.Where(route => route.IsVisible).ToArray();

    public ReadOnlyObservableCollection<NavigationSectionModel> VisibleSections => _visibleSectionsView;

    public NavigationRoute? Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return _lookup.TryGetValue(key.Trim(), out var route) ? route : null;
    }

    private void OnLanguageChanged(object? sender, string language)
    {
        RebuildVisibleSections();
    }

    private void RebuildVisibleSections()
    {
        _visibleSections.Clear();
        foreach (var route in _routes.Where(route => route.IsVisible))
        {
        _visibleSections.Add(route.CreateSection(key => LocalizationService.Instance.Get(key)));
        }
    }

    private static Dictionary<string, NavigationRoute> BuildLookup(IEnumerable<NavigationRoute> routes)
    {
        var lookup = new Dictionary<string, NavigationRoute>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            AddKey(lookup, route.Key, route);
            foreach (var alias in route.Aliases)
            {
                AddKey(lookup, alias, route);
            }
        }

        return lookup;
    }

    private static void AddKey(IDictionary<string, NavigationRoute> lookup, string key, NavigationRoute route)
    {
        if (!lookup.TryAdd(key, route))
        {
            throw new InvalidOperationException($"导航键重复：{key}");
        }
    }

    private static List<NavigationRoute> CreateRoutes()
    {
        const string monitor = "monitor";
        const string modules = "modules";
        const string tools = "tools";
        const string settings = "settings";

        return new List<NavigationRoute>
        {
            Visible("overview", "OverviewPageHost", typeof(OverviewPage), monitor, "Nav.Overview", NavigationIconData.Overview),
            Visible("memory-optimization", "MemoryOptimizationPageHost", typeof(MemoryOptimizationPage), modules, "Nav.MemoryOptimization", NavigationIconData.Memory, aliases: new[] { "memory" }),
            Visible("audio-optimization", "AudioOptimizationPageHost", typeof(AudioOptimizationPage), modules, "Nav.AudioOptimization", NavigationIconData.Audio, aliases: new[] { "audio" }),
            Visible("performance-boost", "PerformanceBoostPageHost", typeof(PerformanceBoostPage), modules, "Nav.PerformanceBoost", NavigationIconData.Performance, aliases: new[] { "performance" }),
            Visible("cpu-nav", "CpuModulePageHost", typeof(CpuOptimizationPage), modules, "Nav.CpuOptimization", NavigationIconData.Cpu, aliases: new[] { "cpu" }),
            Visible("graphics-nav", "GraphicsModulePageHost", typeof(GraphicsOptimizationPage), modules, "Nav.GraphicsOptimization", NavigationIconData.Graphics, aliases: new[] { "graphics" }),
            Visible("network-nav", "NetworkModulePageHost", typeof(NetworkOptimizationPage), modules, "Nav.NetworkOptimization", NavigationIconData.Network, aliases: new[] { "network" }),
            Visible("privacy-nav", "PrivacyModulePageHost", typeof(PrivacyShieldPage), modules, "Nav.PrivacyShield", NavigationIconData.Privacy, aliases: new[] { "privacy" }),
            Visible("system-nav", "SystemModulePageHost", typeof(MasterModuleTemplate), modules, "Nav.SystemTuning", NavigationIconData.System, moduleKey: "system", moduleTitle: "系统调校", moduleSubtitle: "底层系统参数与调度优化", aliases: new[] { "tuning" }),
            Visible("explorer-nav", "ExplorerModulePageHost", typeof(MasterModuleTemplate), modules, "Nav.DesktopExperience", NavigationIconData.Folder, moduleKey: "explorer", moduleTitle: "桌面体验", moduleSubtitle: "资源管理器与Shell优化", aliases: new[] { "explorer" }),
            Visible("tasks-nav", "TasksModulePageHost", typeof(MasterModuleTemplate), modules, "Nav.TaskScheduler", NavigationIconData.Tasks, moduleKey: "tasks", moduleTitle: "任务调度", moduleSubtitle: "禁用不必要的计划任务", aliases: new[] { "scheduler" }),
            Visible("services-nav", "ServicesModulePageHost", typeof(MasterModuleTemplate), modules, "Nav.SystemServices", NavigationIconData.Services, moduleKey: "services", moduleTitle: "系统服务", moduleSubtitle: "优化后台服务与进程", aliases: new[] { "services" }),
            Visible("system-cleanup", "SystemCleanupPageHost", typeof(SystemCleanupPage), modules, "Nav.SystemCleanup", NavigationIconData.Cleanup, aliases: new[] { "cleanup" }),
            Visible("appx-cleanup", "AppxCleanupPageHost", typeof(AppxCleanupPage), modules, "Nav.AppxCleanup", NavigationIconData.Apps, aliases: new[] { "appx", "uninstall" }),
            Visible("peripheral-nav", "PeripheralModulePageHost", typeof(PeripheralOptimizationPage), modules, "Nav.PeripheralOptimization", NavigationIconData.Peripheral, aliases: new[] { "peripheral" }),
            Visible("esports-mode", "EsportsModePageHost", typeof(EsportsModePage), modules, "Nav.EsportsMode", NavigationIconData.Esports, aliases: new[] { "game-mode-hub" }),
            Visible("hardware-info", "HardwareInfoPageHost", typeof(HardwareInfoPage), tools, "Nav.HardwareInfo", NavigationIconData.Hardware, aliases: new[] { "hardware" }),
            Visible("network-test", "NetworkTestPageHost", typeof(NetworkTestPage), tools, "Nav.NetworkTest", NavigationIconData.NetworkTest, aliases: new[] { "nettest" }),
            Visible("restore-point", "RestorePointPageHost", typeof(RestorePointPage), tools, "Nav.RestorePoint", NavigationIconData.RestorePoint, aliases: new[] { "restore" }),
            Visible("repair-center", "RepairCenterPageHost", typeof(RepairCenterPage), tools, "Nav.RepairCenter", NavigationIconData.Repair, aliases: new[] { "repair" }),
            Visible("backup-manager", "BackupManagerPageHost", typeof(BackupManagerPage), tools, "Nav.BackupManager", NavigationIconData.Backup, aliases: new[] { "backup" }),
            Visible("cpu-core-management", "CpuCoreManagementPageHost", typeof(CpuCoreManagementPage), tools, "Nav.CpuCoreManagement", NavigationIconData.CoreManagement),
            Visible("power-plan-editor", "PowerPlanEditorPageHost", typeof(PowerPlanEditorPage), tools, "Nav.PowerOptimization", NavigationIconData.Power),
            Visible("settings", "SettingsPageHost", typeof(SettingsPage), settings, "Nav.Settings", NavigationIconData.Settings),

            Internal("cpu-affinity", "CpuAffinityPageHost", typeof(CpuAffinityPage)),
            Internal("core-accelerator", "CoreAcceleratorPageHost", typeof(CoreAcceleratorPage)),
            // Why: 启动项管理页是已实现功能但此前无任何路由，DI 注册成为摆设；
            // 以内部路由接入，不出现在侧栏导航。
            Internal("startup-manager", "StartupManagerPageHost", typeof(StartupManagerPage)),
            Internal("module-detail", "ModulePageHost", typeof(ModulePage))
        };

        static NavigationRoute Visible(string key, string host, Type pageType, string groupKey, string titleKey, string iconData, string? moduleKey = null, string? moduleTitle = null, string? moduleSubtitle = null, params string[] aliases)
            => new(key, host, pageType, true, groupKey, GroupTitleKey(groupKey), titleKey, $"{titleKey}.Subtitle", iconData, moduleKey, moduleTitle, moduleSubtitle, aliases);

        static string GroupTitleKey(string groupKey)
            => groupKey switch
            {
                "monitor" => "NavGroup.Monitor",
                "modules" => "NavGroup.Modules",
                "tools" => "NavGroup.Tools",
                "settings" => "NavGroup.Settings",
                _ => throw new ArgumentOutOfRangeException(nameof(groupKey), groupKey, "未知导航分组")
            };

        static NavigationRoute Internal(string key, string host, Type pageType)
            => new(key, host, pageType, false);
    }
}
