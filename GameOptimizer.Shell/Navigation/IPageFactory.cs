// File: GameOptimizer.Shell/Navigation/IPageFactory.cs
using System.Windows;

namespace GameOptimizer.Shell.Navigation;

public interface IPageFactory
{
    FrameworkElement GetOrCreate(NavigationRoute route);
}
