using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Tests.Shell;

public sealed class NetworkNodeSelectorInteractionTests
{
    [Fact]
    public void SelectorOpensAndContainsPresetAndCustomNodesOnStaUiThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? application = null;
            var ownsApplication = false;
            try
            {
                ownsApplication = Application.Current is null;
                application = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                foreach (var resource in new[]
                         {
                             "DarkTheme.xaml",
                             "DesignTokens.xaml",
                             "GlobalControlStyles.xaml",
                             "Animations.xaml",
                             "ModernStyles.xaml",
                             "ShellCompatibilityResources.xaml",
                             "YuqiComponentLibrary.xaml"
                         })
                {
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/YuqiEngine;component/Themes/{resource}", UriKind.Relative)
                    });
                }

                var page = new NetworkTestPage();
                var host = new Window
                {
                    Width = 1100,
                    Height = 760,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = page
                };
                host.Show();
                host.UpdateLayout();

                var opened = page.TryOpenNodeSelectorDropDown();

                Assert.True(opened);
                Assert.True(page.NodeSelector.IsDropDownOpen);
                Assert.NotNull(page.NodeSelector.ItemsSource);
                Assert.True(page.NodeSelector.Items.Count >= 2);
                Assert.Contains(
                    page.NodeSelector.Items.Cast<NetworkSpeedTestNodeOption>(),
                    static option => option.IsCustom);
                page.NodeSelector.IsDropDownOpen = false;
                host.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (ownsApplication)
                    application?.Shutdown();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
