using System.Windows.Media;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Navigation;

namespace GameOptimizer.Tests.Navigation;

public sealed class NavigationCatalogTests
{
    [Fact]
    public void Catalog_contains_24_visible_routes_and_29_unique_hosts()
    {
        var catalog = new NavigationCatalog();

        Assert.Equal(24, catalog.VisibleRoutes.Count);
        Assert.Equal(29, catalog.Routes.Count);
        Assert.Equal(
            catalog.VisibleRoutes.Count,
            catalog.VisibleRoutes.Select(route => route.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            catalog.Routes.Count,
            catalog.Routes.Select(route => route.HostName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(24, catalog.VisibleSections.Count);
        Assert.All(catalog.Routes, route => Assert.Same(route, catalog.Resolve(route.Key)));
    }

    [Fact]
    public void Appx_cleanup_is_a_visible_route_in_modules_group_with_unique_page_host()
    {
        var catalog = new NavigationCatalog();
        var route = catalog.Resolve("appx-cleanup");

        Assert.NotNull(route);
        Assert.True(route!.IsVisible);
        Assert.Equal("AppxCleanupPageHost", route.HostName);
        Assert.Equal("modules", route.GroupKey);
        Assert.Single(catalog.Routes, item => item.PageType == route.PageType);
        Assert.Equal(typeof(GameOptimizer.Shell.Views.AppxCleanupPage), route.PageType);
        Assert.Equal("appx-cleanup", catalog.Resolve("appx")?.Key);
        Assert.Equal("appx-cleanup", catalog.Resolve("uninstall")?.Key);
    }

    [Fact]
    public void Internal_routes_are_resolvable_but_not_visible()
    {
        var catalog = new NavigationCatalog();

        Assert.False(catalog.Resolve("module-detail")!.IsVisible);
        Assert.False(catalog.Resolve("cpu-affinity")!.IsVisible);
        Assert.False(catalog.Resolve("core-accelerator")!.IsVisible);
        Assert.DoesNotContain(catalog.VisibleSections, section => section.Key == "module-detail");
        Assert.DoesNotContain(catalog.VisibleSections, section => section.Key == "cpu-affinity");
    }

    [Theory]
    [InlineData("memory", "memory-optimization")]
    [InlineData("audio", "audio-optimization")]
    [InlineData("performance", "performance-boost")]
    [InlineData("game-mode-hub", "esports-mode")]
    [InlineData("backup", "backup-manager")]
    [InlineData("nettest", "network-test")]
    public void Legacy_deep_link_aliases_resolve_to_canonical_visible_keys(string alias, string expectedKey)
    {
        var catalog = new NavigationCatalog();

        Assert.Equal(expectedKey, catalog.Resolve(alias)?.Key);
    }

    [Fact]
    public void Every_route_page_type_is_a_usercontrol_hostable_in_contentcontrol()
    {
        // Why: 页面宿主是 ContentControl，Page 只能挂在 Window/Frame 下——
        // 曾因此点击 AI 助手后整页空白（InvalidOperationException 于 Measure 阶段）。
        var catalog = new NavigationCatalog();
        Assert.All(catalog.Routes, route =>
            Assert.True(
                typeof(System.Windows.Controls.UserControl).IsAssignableFrom(route.PageType),
                $"{route.Key} 的页面类型 {route.PageType.Name} 必须继承 UserControl"));
    }

    [Fact]
    public void Offline_help_route_is_removed()
    {
        var catalog = new NavigationCatalog();

        Assert.Null(catalog.Resolve("offline-help"));
        Assert.Null(catalog.Resolve("help"));
        Assert.Null(catalog.Resolve("guide"));
        Assert.Null(catalog.Resolve("recovery-guide"));
    }

    [Fact]
    public void Optimization_and_tool_routes_use_distinct_non_placeholder_icons()
    {
        var catalog = new NavigationCatalog();
        string[] governedKeys =
        {
            "memory-optimization", "audio-optimization", "performance-boost", "privacy-nav",
            "services-nav", "restore-point", "repair-center", "backup-manager",
            "cpu-core-management", "power-plan-editor"
        };

        var icons = governedKeys.Select(key => catalog.Resolve(key)!.IconData).ToArray();

        Assert.All(icons, icon => Assert.False(string.IsNullOrWhiteSpace(icon)));
        Assert.Equal(icons.Length, icons.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_visible_route_uses_one_unique_valid_24px_navigation_icon()
    {
        var catalog = new NavigationCatalog();
        var routeIcons = catalog.VisibleRoutes.Select(route => route.IconData).ToArray();
        var canonicalIcons = typeof(NavigationIconData)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        // Why: 图标常量表同时服务侧栏与顶栏（如 AI 助手），只要求可见路由使用的图标
        // 全部来自常量表且互不重复，不要求常量全被可见路由用掉。
        Assert.Equal(catalog.VisibleRoutes.Count, routeIcons.Distinct(StringComparer.Ordinal).Count());
        Assert.All(routeIcons, icon => Assert.Contains(icon, canonicalIcons));
        Assert.All(routeIcons, iconData =>
        {
            var geometry = Geometry.Parse(iconData);
            var bounds = geometry.Bounds;
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0, $"Icon escapes the 24px origin: {iconData}");
            Assert.True(bounds.Right <= 24 && bounds.Bottom <= 24, $"Icon escapes the 24px grid: {iconData}");
        });
    }
}
