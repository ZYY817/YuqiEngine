using System.Text;

namespace GameOptimizer.Tests.Navigation;

public sealed class PageFactoryContractTests
{
    [Fact]
    public void PageFactory_requires_exactly_one_DI_page_and_has_no_creation_fallback()
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "GameOptimizer.Shell", "Navigation", "PageFactory.cs"),
            Encoding.UTF8);

        Assert.Contains("GetServices(route.PageType)", source, StringComparison.Ordinal);
        Assert.Contains("registrations.Length == 0", source, StringComparison.Ordinal);
        Assert.Contains("registrations.Length != 1", source, StringComparison.Ordinal);
        Assert.Contains("did not resolve to a FrameworkElement", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivatorUtilities", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService(route.PageType)", source, StringComparison.Ordinal);
        Assert.Contains("_pages.TryGetValue(route.Key", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("未找到包含 GameOptimizer.sln 的工作区根目录。");
    }
}
