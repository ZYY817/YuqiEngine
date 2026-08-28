// File: GameOptimizer.Shell/Views/AppxCleanupPage.xaml.cs
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class AppxCleanupPage : UserControl
{
    private readonly AppxCleanupViewModel _viewModel;
    private bool _initialized;

    public AppxCleanupPage(AppxCleanupViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PageDiagnosticService.Log("AppxCleanupPage", "OnLoaded");
        if (_initialized)
            return;
        _initialized = true;
        _ = InitializeSafeAsync();
    }

    /// <summary>首次进入页面时异步枚举（WinRT 目录查询较慢），失败路径由 VM 状态机承载。</summary>
    private async Task InitializeSafeAsync()
    {
        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("AppxCleanupPage", "初始化枚举失败", ex);
        }
    }
}
