using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace GameOptimizer.Tests.Visual;

public sealed class ThemeResourceLoadContractTests
{
    [Fact]
    public void DarkThemeDictionaryLoadsAndResolvesExtendedBrushes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // WPF permits only one Application instance per AppDomain. Other visual
                // contract tests may have initialized it already, so reuse the process
                // instance when present instead of making this test order-dependent.
                _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "/YuqiEngine;component/Themes/DarkTheme.xaml",
                        UriKind.Relative)
                };

                foreach (var key in new[]
                         {
                             "Theme.Brush.ButtonBackground",
                             "Theme.Brush.ButtonBackgroundHover",
                             "Theme.Brush.RowBackgroundExtreme",
                             "Theme.Brush.IconBackgroundExtreme"
                         })
                {
                    Assert.IsAssignableFrom<Brush>(dictionary[key]);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
