// File: GameOptimizer.Shell/Navigation/NavigationRoute.cs
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Navigation;

public sealed class NavigationRoute
{
    public NavigationRoute(
        string key,
        string hostName,
        Type pageType,
        bool isVisible,
        string? groupKey = null,
        string? groupTitleKey = null,
        string? titleKey = null,
        string? subtitleKey = null,
        string iconData = "",
        string? moduleKey = null,
        string? moduleTitle = null,
        string? moduleSubtitle = null,
        params string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentNullException.ThrowIfNull(pageType);

        Key = key;
        HostName = hostName;
        PageType = pageType;
        IsVisible = isVisible;
        GroupKey = groupKey;
        GroupTitleKey = groupTitleKey;
        TitleKey = titleKey;
        SubtitleKey = subtitleKey;
        IconData = iconData;
        ModuleKey = moduleKey;
        ModuleTitle = moduleTitle;
        ModuleSubtitle = moduleSubtitle;
        Aliases = aliases ?? Array.Empty<string>();
    }

    public string Key { get; }

    public string HostName { get; }

    public Type PageType { get; }

    public bool IsVisible { get; }

    public string? GroupKey { get; }

    public string? GroupTitleKey { get; }

    public string? TitleKey { get; }

    public string? SubtitleKey { get; }

    public string IconData { get; }

    public string? ModuleKey { get; }

    public string? ModuleTitle { get; }

    public string? ModuleSubtitle { get; }

    public IReadOnlyList<string> Aliases { get; }

    public NavigationSectionModel CreateSection(Func<string, string> localize)
    {
        if (!IsVisible || GroupKey is null || GroupTitleKey is null || TitleKey is null || SubtitleKey is null)
        {
            throw new InvalidOperationException($"导航路由 {Key} 没有完整的可见导航元数据。");
        }

        return new NavigationSectionModel(
            Key,
            localize(TitleKey),
            localize(SubtitleKey),
            IconData,
            GroupKey,
            localize(GroupTitleKey),
            ModuleKey);
    }
}
