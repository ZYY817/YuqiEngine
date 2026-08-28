using System;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class RepairCenterPage : UserControl
{
    private readonly IRepairService _repairService;
    private readonly ISystemScanService _scanService;
    private readonly IActiveOperationRegistry _activeOperations;
    private RepairCenterViewModel? _viewModel;

    public RepairCenterPage(
        IRepairService repairService,
        ISystemScanService scanService,
        IActiveOperationRegistry activeOperations)
    {
        _repairService = repairService ?? throw new ArgumentNullException(nameof(repairService));
        _scanService = scanService ?? throw new ArgumentNullException(nameof(scanService));
        _activeOperations = activeOperations ?? throw new ArgumentNullException(nameof(activeOperations));
        PageDiagnosticService.Log("RepairCenterPage", "Constructor called");
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            return;
        }

        try
        {
            _viewModel = new RepairCenterViewModel(
                _repairService,
                _scanService,
                activeOperations: _activeOperations);
            DataContext = _viewModel;
            _viewModel.Initialize();

            PageDiagnosticService.LogLoaded("RepairCenterPage", true, $"{_viewModel.AllItems.Count} items loaded");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("RepairCenterPage", "Init error", ex);
            ToastService.Instance.Error("Repair Center", "Initialization failed. Please try again.");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel?.Cleanup();
        if (_viewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _viewModel = null;
        DataContext = null;
    }

    private void QuickRepairDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: QuickRepairActionViewModel action })
        {
            ToastService.Instance.Info(action.Title, action.Description);
        }
    }
}
