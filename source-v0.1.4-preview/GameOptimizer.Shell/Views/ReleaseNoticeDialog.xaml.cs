using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace GameOptimizer.Shell.Views;

public partial class ReleaseNoticeDialog : Window
{
    private bool _acknowledged;
    private bool _allowClose;

    public ReleaseNoticeDialog()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        version = version?.Split('+')[0];
        VersionText.Text = string.IsNullOrWhiteSpace(version) ? "预览版" : $"v{version}";
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void AcknowledgementCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ContinueButton.IsEnabled = AcknowledgementCheckBox.IsChecked == true;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        _acknowledged = true;
        DialogResult = true;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // This notice is release-scoped and intentionally cannot be dismissed without
        // acknowledgement. A dedicated exit action remains available.
        if (!_acknowledged && !_allowClose)
        {
            e.Cancel = true;
        }
    }
}
