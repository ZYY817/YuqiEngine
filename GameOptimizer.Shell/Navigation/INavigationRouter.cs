// File: GameOptimizer.Shell/Navigation/INavigationRouter.cs
namespace GameOptimizer.Shell.Navigation;

public interface INavigationRouter
{
    string CurrentKey { get; }

    string LastVisibleKey { get; }

    event EventHandler<NavigationChangedEventArgs>? Navigated;

    bool Navigate(string key);
}

public sealed class NavigationChangedEventArgs : EventArgs
{
    public NavigationChangedEventArgs(NavigationRoute route, NavigationRoute? previousRoute)
    {
        Route = route;
        PreviousRoute = previousRoute;
    }

    public NavigationRoute Route { get; }

    public NavigationRoute? PreviousRoute { get; }
}
