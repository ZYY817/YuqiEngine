using System;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        PageDiagnosticService.Log("OverviewPage", "Constructor called");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachRouter(DataContext as DashboardViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachRouter(DataContext as DashboardViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachRouter(e.OldValue as DashboardViewModel);
        AttachRouter(e.NewValue as DashboardViewModel);
    }

    private void AttachRouter(DashboardViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.NavigateToPageRequested -= OnNavigateToPageRequested;
            viewModel.NavigateToPageRequested += OnNavigateToPageRequested;
        }
    }

    private void DetachRouter(DashboardViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.NavigateToPageRequested -= OnNavigateToPageRequested;
        }
    }

    private void OnNavigateToPageRequested(string target)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            try
            {
                mainWindow.NavigateToPage(target);
            }
            catch (Exception ex)
            {
                PageDiagnosticService.LogError("OverviewPage", $"Navigation to '{target}' failed", ex);
            }
        }
    }

    private void SupportAuthorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AboutDialog(focusSupportSection: true)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("OverviewPage", "Opening author information failed", ex);
        }
    }

}
