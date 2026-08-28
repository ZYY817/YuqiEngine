// File: GameOptimizer.Shell/Navigation/NavigationRouter.cs
namespace GameOptimizer.Shell.Navigation;

public sealed class NavigationRouter : INavigationRouter
{
    private readonly INavigationCatalog _catalog;
    private readonly IPageFactory _pageFactory;
    private string _currentKey = "overview";
    private string _lastVisibleKey = "overview";

    public NavigationRouter(INavigationCatalog catalog, IPageFactory pageFactory)
    {
        _catalog = catalog;
        _pageFactory = pageFactory;
    }

    public string CurrentKey => _currentKey;

    public string LastVisibleKey => _lastVisibleKey;

    public event EventHandler<NavigationChangedEventArgs>? Navigated;

    public bool Navigate(string key)
    {
        var route = _catalog.Resolve(key);
        if (route is null)
        {
            return false;
        }

        var previousRoute = _catalog.Resolve(_currentKey);
        _pageFactory.GetOrCreate(route);
        _currentKey = route.Key;
        if (route.IsVisible)
        {
            _lastVisibleKey = route.Key;
        }

        Navigated?.Invoke(this, new NavigationChangedEventArgs(route, previousRoute));
        return true;
    }
}
