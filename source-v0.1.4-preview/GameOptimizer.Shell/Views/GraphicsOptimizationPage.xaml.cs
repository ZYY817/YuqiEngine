using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Views;

public partial class GraphicsOptimizationPage : UserControl
{
    public GraphicsOptimizationPage()
    {
        InitializeComponent();
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // MasterModuleTemplate 已通过 ModuleKey="graphics" 自动处理本地化，无需重复设置
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 无需清理，MasterModuleTemplate 自行管理事件订阅
    }
}
