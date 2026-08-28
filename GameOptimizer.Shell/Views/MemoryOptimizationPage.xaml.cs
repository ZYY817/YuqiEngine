// File: GameOptimizer.Shell/Views/MemoryOptimizationPage.xaml.cs
using System.Windows.Controls;

namespace GameOptimizer.Shell.Views;

/// <summary>
/// 内存优化页面 - 使用统一模板架构
/// </summary>
/// <remarks>
/// 此页面已迁移到MasterModuleTemplate架构。
/// 所有UI逻辑和交互由MasterModuleTemplate处理。
/// 内存特定功能(监控、清理)由MemoryControlPanel扩展提供。
/// </remarks>
public partial class MemoryOptimizationPage : UserControl
{
    public MemoryOptimizationPage()
    {
        InitializeComponent();
    }
}
