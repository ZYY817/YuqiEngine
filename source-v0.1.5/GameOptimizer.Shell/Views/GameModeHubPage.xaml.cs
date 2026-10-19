using System.Windows;
using System.Windows.Controls;

namespace GameOptimizer.Shell.Views;

public partial class GameModeHubPage : UserControl
{
    public GameModeHubPage()
    {
        InitializeComponent();
    }

    private void OpenBackupManager_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is GameOptimizer.Shell.MainWindow mainWindow)
        {
            mainWindow.NavigateToPage("backup-manager");
        }
    }

    // 游戏模式入口保持安全阻断：不启动任何进程、不调用内存清理/垃圾回收 API。
}
