namespace GameOptimizer.Shell.Models;

public class NavigationSectionModel
{
    public NavigationSectionModel(
        string key,
        string title,
        string subtitle,
        string iconData,
        string groupKey = "default",
        string groupTitle = "导航",
        string? targetModuleKey = null)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        IconData = iconData;
        GroupKey = groupKey;
        GroupTitle = groupTitle;
        TargetModuleKey = targetModuleKey;
    }

    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string IconData { get; }

    public string GroupKey { get; }

    public string GroupTitle { get; }

    public string? TargetModuleKey { get; }

    // Why: WPF ListBoxItem 的 UIA Name 取自 ToString()，
    // 返回 Title 让自动化测试能通过 Name 定位导航项
    public override string ToString() => Title;
}
