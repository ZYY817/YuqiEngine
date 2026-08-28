// File: GameOptimizer.Shell/Navigation/PageFactory.cs
using System.Windows;
using GameOptimizer.Shell.Views;
using GameOptimizer.Shell.ViewModels;
using GameOptimizer.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Shell.Navigation;

public sealed class PageFactory : IPageFactory
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, FrameworkElement> _pages = new(StringComparer.OrdinalIgnoreCase);

    public PageFactory(IServiceProvider services)
    {
        _services = services;
    }

    public FrameworkElement GetOrCreate(NavigationRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (_pages.TryGetValue(route.Key, out var page))
        {
            return page;
        }

        var registrations = _services.GetServices(route.PageType).ToArray();
        if (registrations.Length == 0)
        {
            throw new InvalidOperationException(
                $"No DI registration exists for navigation route '{route.Key}' ({route.PageType.FullName}).");
        }

        if (registrations.Length != 1)
        {
            throw new InvalidOperationException(
                $"Navigation route '{route.Key}' requires exactly one DI registration for "
                + $"{route.PageType.FullName}, but found {registrations.Length}.");
        }

        page = registrations[0] as FrameworkElement
            ?? throw new InvalidOperationException(
                $"DI registration for navigation route '{route.Key}' ({route.PageType.FullName}) "
                + "did not resolve to a FrameworkElement.");
        ConfigurePage(route, page);
        InitializeInjectedPage(page);
        _pages.Add(route.Key, page);
        return page;
    }

    private static void ConfigurePage(NavigationRoute route, FrameworkElement page)
    {
        if (page is MasterModuleTemplate moduleTemplate && route.ModuleKey is not null)
        {
            moduleTemplate.ModuleKey = route.ModuleKey;
            moduleTemplate.ModuleTitle = route.ModuleTitle ?? string.Empty;
            moduleTemplate.ModuleSubtitle = route.ModuleSubtitle ?? string.Empty;
        }
    }

    private static void InitializeInjectedPage(FrameworkElement page)
    {
        if (page is CoreAcceleratorPage && page.DataContext is CoreAcceleratorViewModel coreAcceleratorViewModel)
        {
            _ = InitializeAsync(coreAcceleratorViewModel.InitializeAsync, nameof(CoreAcceleratorPage));
        }
        else if (page is CpuCoreManagementPage && page.DataContext is CpuCoreManagementViewModel cpuCoreManagementViewModel)
        {
            _ = InitializeAsync(cpuCoreManagementViewModel.InitializeAsync, nameof(CpuCoreManagementPage));
        }
    }

    private static async Task InitializeAsync(Func<Task> initialize, string pageName)
    {
        try
        {
            await initialize().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError(pageName, "注入 ViewModel 初始化失败", ex);
        }
    }
}
