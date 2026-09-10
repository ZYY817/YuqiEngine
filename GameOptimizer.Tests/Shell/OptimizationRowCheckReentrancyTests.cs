using System.Runtime.ExceptionServices;
using System.Windows;
using GameOptimizer.Shell.Controls;

namespace GameOptimizer.Tests.Shell;

public sealed class OptimizationRowCheckReentrancyTests
{
    [Fact]
    public void ProgrammaticCheckOnAppliedIncompatibleRowDoesNotRecurse()
    {
        RunOnSta(() =>
        {
            var row = CreateRow();
            row.IsCompatible = false;
            var applied = true;
            var depth = 0;
            var maxDepth = 0;
            var notifications = 0;

            row.CheckedChanged += (_, _) =>
            {
                notifications++;
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                if (depth > 32)
                {
                    throw new InvalidOperationException("OptimizationRow check state re-entered until the stack would overflow.");
                }

                try
                {
                    // Mirrors EsportsModePage.OnRowCheckedChanged before the stack-guard fix:
                    // incompatible rows uncheck themselves, applied rows re-check themselves.
                    if (!row.IsCompatible && row.IsChecked)
                    {
                        row.IsChecked = false;
                    }
                    else if (applied && !row.IsChecked)
                    {
                        row.IsChecked = true;
                    }
                }
                finally
                {
                    depth--;
                }
            };

            row.IsChecked = true;

            Assert.True(row.IsChecked);
            Assert.True(maxDepth <= 1, $"CheckedChanged re-entered {maxDepth} times for a programmatic set.");
            Assert.True(notifications <= 1);
        });
    }

    [Fact]
    public void UserToggleOnAppliedIncompatibleRowDoesNotRecurse()
    {
        RunOnSta(() =>
        {
            var row = CreateRow();
            row.Measure(new Size(480, 64));
            row.Arrange(new Rect(0, 0, 480, 64));
            row.UpdateLayout();
            row.IsCompatible = false;
            var applied = true;
            var depth = 0;
            var maxDepth = 0;

            row.CheckedChanged += (_, _) =>
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                if (depth > 32)
                {
                    throw new InvalidOperationException("OptimizationRow check state re-entered until the stack would overflow.");
                }

                try
                {
                    if (!row.IsCompatible && row.IsChecked)
                    {
                        row.IsChecked = false;
                    }
                    else if (applied && !row.IsChecked)
                    {
                        row.IsChecked = true;
                    }
                }
                finally
                {
                    depth--;
                }
            };

            row.RowToggle.IsChecked = true;

            Assert.True(maxDepth <= 1, $"User toggle re-entered CheckedChanged {maxDepth} times.");
        });
    }

    private static OptimizationRow CreateRow()
    {
        EnsureThemeResources();
        return new OptimizationRow
        {
            OperationId = "security.disable_vbs",
            Title = "VBS",
            Width = 480,
            Height = 56
        };
    }

    private static void EnsureThemeResources()
    {
        var application = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        if (application.Resources.MergedDictionaries.Count > 0)
        {
            return;
        }

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
    }

    private static void RunOnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
