using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class RestorePointPage : UserControl
{
    private bool _isInitialized;
    private readonly DispatcherTimer _capabilityTimer = new()
    {
        Interval = TimeSpan.FromSeconds(30)
    };

    public RestorePointViewModel ViewModel { get; }

    public RestorePointPage(RestorePointViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        PageDiagnosticService.Log("RestorePointPage", "Constructor called");
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _capabilityTimer.Tick += OnCapabilityTimerTick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        try
        {
            await ViewModel.InitializeAsync();
            _capabilityTimer.Start();
            PageDiagnosticService.LogLoaded("RestorePointPage", true, $"{ViewModel.RestorePoints.Count} restore points loaded");
        }
        catch (Exception ex)
        {
            // Why: 初始化失败时不得保留已初始化标记，否则页面在后续 Loaded
            // 中跳过初始化永远停在空状态。
            _isInitialized = false;
            PageDiagnosticService.LogError("RestorePointPage", "OnLoaded error", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCurrentOperation();
        _capabilityTimer.Stop();
        _isInitialized = false;
    }

    private async void OnCapabilityTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await ViewModel.ReevaluateCapabilityAsync();
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("RestorePointPage", "Capability reevaluation failed", ex);
        }
    }
}
