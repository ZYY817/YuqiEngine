// File: GameOptimizer.Shell/Navigation/INavigationCatalog.cs
using System.Collections.ObjectModel;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Navigation;

public interface INavigationCatalog
{
    IReadOnlyList<NavigationRoute> Routes { get; }

    IReadOnlyList<NavigationRoute> VisibleRoutes { get; }

    ReadOnlyObservableCollection<NavigationSectionModel> VisibleSections { get; }

    NavigationRoute? Resolve(string key);
}
